using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace SirenChanger;

internal enum TransitAnnouncementTtsSynthesisStatus
{
	Success,
	Pending,
	NotAvailable,
	Failure
}

// Transit TTS service with async clip cache and out-of-process synthesis fallback for game runtimes
// where in-process unmanaged activation is unavailable.
internal static class TransitAnnouncementTtsService
{
	private const int kMaxCachedClips = 64;

	private const int kSynthesisTimeoutMs = 12000;

	private const int kVoiceListTimeoutMs = 8000;

	private static readonly object s_SyncRoot = new object();

	private static readonly Dictionary<string, CachedTtsClip> s_ClipCache = new Dictionary<string, CachedTtsClip>(StringComparer.Ordinal);

	private static readonly Dictionary<string, PendingSynthesis> s_PendingSynthesisByKey = new Dictionary<string, PendingSynthesis>(StringComparer.Ordinal);

	private static string[] s_InstalledVoiceNames = Array.Empty<string>();

	private static bool s_InstalledVoicesInitialized;

	private static bool s_BackendUnavailable;

	private static string s_BackendUnavailableMessage = string.Empty;

	private static readonly SemaphoreSlim s_SynthesisConcurrencyGate = new SemaphoreSlim(2, 2);

	private sealed class CachedTtsClip
	{
		public AudioClip Clip { get; set; } = null!;

		public long LastAccessUtcTicks { get; set; }
	}

	private sealed class PendingSynthesis
	{
		public Task<SynthesisJobResult> Task { get; set; } = null!;
	}

	private sealed class SynthesisJobResult
	{
		public bool Success { get; set; }

		public bool BackendUnavailable { get; set; }

		public byte[] WavBytes { get; set; } = Array.Empty<byte>();

		public string ResolvedVoice { get; set; } = string.Empty;

		public string Message { get; set; } = string.Empty;
	}

	internal static string NormalizeSpeechText(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return string.Empty;
		}

		StringBuilder builder = new StringBuilder(text.Length);
		bool sawWhitespace = false;
		for (int i = 0; i < text.Length; i++)
		{
			char c = text[i];
			if (char.IsWhiteSpace(c))
			{
				sawWhitespace = true;
				continue;
			}

			if (sawWhitespace && builder.Length > 0)
			{
				builder.Append(' ');
			}

			builder.Append(c);
			sawWhitespace = false;
		}

		return builder.ToString().Trim();
	}

	internal static string NormalizeVoiceSelection(string voiceName)
	{
		string normalized = (voiceName ?? string.Empty).Trim();
		if (string.Equals(normalized, "default", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(normalized, "default system voice", StringComparison.OrdinalIgnoreCase))
		{
			return string.Empty;
		}

		return normalized;
	}

	internal static string[] GetInstalledVoiceNames()
	{
		lock (s_SyncRoot)
		{
			if (!s_InstalledVoicesInitialized)
			{
				s_InstalledVoicesInitialized = true;
				s_InstalledVoiceNames = LoadInstalledVoices();
			}

			string[] snapshot = new string[s_InstalledVoiceNames.Length];
			Array.Copy(s_InstalledVoiceNames, snapshot, s_InstalledVoiceNames.Length);
			return snapshot;
		}
	}

	internal static TransitAnnouncementTtsSynthesisStatus TrySynthesizeClip(
		string text,
		string requestedVoice,
		out AudioClip clip,
		out string message)
	{
		clip = null!;
		message = string.Empty;

		string normalizedText = NormalizeSpeechText(text);
		if (string.IsNullOrWhiteSpace(normalizedText))
		{
			message = "Speech text was empty.";
			return TransitAnnouncementTtsSynthesisStatus.Failure;
		}

		if (!IsWindowsPlatform())
		{
			message = "Transit TTS is only available on Windows.";
			return TransitAnnouncementTtsSynthesisStatus.NotAvailable;
		}

		string normalizedVoice = NormalizeVoiceSelection(requestedVoice);
		string cacheKey = BuildCacheKey(normalizedText, normalizedVoice);

		SynthesisJobResult? completedResult = null;
		lock (s_SyncRoot)
		{
			if (s_BackendUnavailable)
			{
				message = string.IsNullOrWhiteSpace(s_BackendUnavailableMessage)
					? "Transit TTS backend is unavailable."
					: s_BackendUnavailableMessage;
				return TransitAnnouncementTtsSynthesisStatus.NotAvailable;
			}

			if (TryGetCachedClipLocked(cacheKey, out AudioClip cached))
			{
				clip = cached;
				message = "Loaded cached speech clip.";
				return TransitAnnouncementTtsSynthesisStatus.Success;
			}

			if (s_PendingSynthesisByKey.TryGetValue(cacheKey, out PendingSynthesis? pending) &&
				pending?.Task != null)
			{
				if (!pending.Task.IsCompleted)
				{
					message = "Speech synthesis is in progress.";
					return TransitAnnouncementTtsSynthesisStatus.Pending;
				}

				s_PendingSynthesisByKey.Remove(cacheKey);
				try
				{
					completedResult = pending.Task.Result;
				}
				catch (Exception ex)
				{
					message = $"Speech synthesis task failed: {ex.Message}";
					return TransitAnnouncementTtsSynthesisStatus.Failure;
				}
			}
		}

		if (completedResult != null)
		{
			if (!completedResult.Success)
			{
				message = string.IsNullOrWhiteSpace(completedResult.Message)
					? "Speech synthesis failed."
					: completedResult.Message;
				if (completedResult.BackendUnavailable)
				{
					lock (s_SyncRoot)
					{
						s_BackendUnavailable = true;
						s_BackendUnavailableMessage = message;
					}
				}

				return completedResult.BackendUnavailable
					? TransitAnnouncementTtsSynthesisStatus.NotAvailable
					: TransitAnnouncementTtsSynthesisStatus.Failure;
			}

			string clipName = $"SC_TTS_{ComputeStableHash(cacheKey):X8}";
			if (!WaveClipLoader.TryCreateClipFromWavBytes(completedResult.WavBytes, clipName, out clip, out string clipError))
			{
				message = $"Generated speech could not be decoded: {clipError}";
				return TransitAnnouncementTtsSynthesisStatus.Failure;
			}

			lock (s_SyncRoot)
			{
				StoreCachedClipLocked(cacheKey, clip);
			}

			message = string.IsNullOrWhiteSpace(completedResult.Message)
				? "Synthesized speech."
				: completedResult.Message;
			return TransitAnnouncementTtsSynthesisStatus.Success;
		}

		Task<SynthesisJobResult> task = Task.Run(() =>
		{
			s_SynthesisConcurrencyGate.Wait();
			try
			{
				return SynthesizeWithExternalProcess(normalizedText, normalizedVoice);
			}
			finally
			{
				s_SynthesisConcurrencyGate.Release();
			}
		});
		lock (s_SyncRoot)
		{
			if (!s_PendingSynthesisByKey.ContainsKey(cacheKey))
			{
				s_PendingSynthesisByKey[cacheKey] = new PendingSynthesis
				{
					Task = task
				};
			}
		}

		message = "Speech synthesis queued.";
		return TransitAnnouncementTtsSynthesisStatus.Pending;
	}

	internal static void Release()
	{
		lock (s_SyncRoot)
		{
			foreach (KeyValuePair<string, CachedTtsClip> entry in s_ClipCache)
			{
				if (entry.Value?.Clip != null)
				{
					UnityEngine.Object.Destroy(entry.Value.Clip);
				}
			}

			s_ClipCache.Clear();
			s_PendingSynthesisByKey.Clear();
			s_InstalledVoiceNames = Array.Empty<string>();
			s_InstalledVoicesInitialized = false;
			s_BackendUnavailable = false;
			s_BackendUnavailableMessage = string.Empty;
		}
	}

	private static string[] LoadInstalledVoices()
	{
		if (!IsWindowsPlatform())
		{
			return Array.Empty<string>();
		}

		string script = @"
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Speech
$synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
try {
	$synth.GetInstalledVoices() | ForEach-Object { $_.VoiceInfo.Name } | Sort-Object -Unique | ForEach-Object { Write-Output $_ }
}
finally {
	$synth.Dispose()
}";

		if (!TryRunPowerShell(script, kVoiceListTimeoutMs, out string stdout, out string stderr, out string error))
		{
			string resolvedError = ChooseBestError(error, stderr);
			if (IsBackendUnavailableError(error, stderr))
			{
				lock (s_SyncRoot)
				{
					s_BackendUnavailable = true;
					s_BackendUnavailableMessage = $"Transit TTS backend is unavailable: {resolvedError}";
				}
			}

			SirenChangerMod.Log.Warn($"Transit TTS voice enumeration failed: {resolvedError}");
			return Array.Empty<string>();
		}

		string[] voices = stdout
			.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
			.Select(static value => NormalizeVoiceSelection(value))
			.Where(static value => !string.IsNullOrWhiteSpace(value))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
			.ToArray();
		return voices;
	}

	private static SynthesisJobResult SynthesizeWithExternalProcess(string normalizedText, string normalizedVoice)
	{
		string outputPath = Path.Combine(Path.GetTempPath(), $"SC_TTS_{Guid.NewGuid():N}.wav");
		try
		{
			string textBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(normalizedText));
			string voiceBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(normalizedVoice));
			string escapedOutPath = EscapeSingleQuotedPowerShellLiteral(outputPath);

			string script = $@"
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Speech
$text = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String('{textBase64}'))
$requestedVoice = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String('{voiceBase64}'))
$outPath = '{escapedOutPath}'
$synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
$resolvedVoice = ''
$warning = ''
try {{
	if (-not [string]::IsNullOrWhiteSpace($requestedVoice)) {{
		$match = $synth.GetInstalledVoices() | ForEach-Object {{ $_.VoiceInfo.Name }} | Where-Object {{ $_ -ieq $requestedVoice }} | Select-Object -First 1
		if ($null -ne $match) {{
			$synth.SelectVoice($match)
		}} else {{
			$warning = ""Requested voice '$requestedVoice' is not installed; using default system voice.""
		}}
	}}

	$resolvedVoice = $synth.Voice.Name
	$synth.SetOutputToWaveFile($outPath)
	$synth.Speak($text)
	$synth.SetOutputToNull()
	Write-Output ('VOICE=' + $resolvedVoice)
	if (-not [string]::IsNullOrWhiteSpace($warning)) {{
		Write-Output ('WARN=' + $warning)
	}}
}}
finally {{
	$synth.Dispose()
}}";

			if (!TryRunPowerShell(script, kSynthesisTimeoutMs, out string stdout, out string stderr, out string error))
			{
				bool backendUnavailable = IsBackendUnavailableError(error, stderr);
				return new SynthesisJobResult
				{
					Success = false,
					BackendUnavailable = backendUnavailable,
					Message = $"Speech synthesis failed: {ChooseBestError(error, stderr)}"
				};
			}

			if (!File.Exists(outputPath))
			{
				return new SynthesisJobResult
				{
					Success = false,
					Message = "Speech synthesis completed but produced no audio file."
				};
			}

			byte[] wavBytes = File.ReadAllBytes(outputPath);
			if (wavBytes.Length == 0)
			{
				return new SynthesisJobResult
				{
					Success = false,
					Message = "Speech synthesis produced an empty audio file."
				};
			}

			ParseSynthesisMetadata(stdout, out string resolvedVoice, out string warning);
			string successMessage = !string.IsNullOrWhiteSpace(warning)
				? warning
				: (string.IsNullOrWhiteSpace(resolvedVoice)
					? "Synthesized speech using default system voice."
					: $"Synthesized speech using '{resolvedVoice}'.");
			return new SynthesisJobResult
			{
				Success = true,
				WavBytes = wavBytes,
				ResolvedVoice = resolvedVoice,
				Message = successMessage
			};
		}
		catch (Exception ex)
		{
			return new SynthesisJobResult
			{
				Success = false,
				Message = $"Speech synthesis failed: {ex.Message}"
			};
		}
		finally
		{
			TryDeleteFile(outputPath);
		}
	}

	private static void ParseSynthesisMetadata(string stdout, out string resolvedVoice, out string warning)
	{
		resolvedVoice = string.Empty;
		warning = string.Empty;
		if (string.IsNullOrWhiteSpace(stdout))
		{
			return;
		}

		string[] lines = stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
		for (int i = 0; i < lines.Length; i++)
		{
			string line = lines[i].Trim();
			if (line.StartsWith("VOICE=", StringComparison.OrdinalIgnoreCase))
			{
				resolvedVoice = NormalizeVoiceSelection(line.Substring("VOICE=".Length));
			}
			else if (line.StartsWith("WARN=", StringComparison.OrdinalIgnoreCase))
			{
				warning = line.Substring("WARN=".Length).Trim();
			}
		}
	}

	private static bool TryRunPowerShell(string script, int timeoutMs, out string stdout, out string stderr, out string error)
	{
		stdout = string.Empty;
		stderr = string.Empty;
		error = string.Empty;
		try
		{
			byte[] scriptBytes = Encoding.Unicode.GetBytes(script);
			string encodedScript = Convert.ToBase64String(scriptBytes);

			ProcessStartInfo startInfo = new ProcessStartInfo
			{
				FileName = "powershell.exe",
				Arguments = $"-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encodedScript}",
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				CreateNoWindow = true
			};

			using (Process process = new Process { StartInfo = startInfo })
			{
				process.Start();
				Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
				Task<string> stderrTask = process.StandardError.ReadToEndAsync();
				if (!process.WaitForExit(timeoutMs))
				{
					try
					{
						process.Kill();
					}
					catch
					{
						// Ignore kill failures while handling timeout.
					}

					error = "PowerShell process timed out.";
					return false;
				}

				try
				{
					Task.WaitAll(new Task[] { stdoutTask, stderrTask }, 1000);
				}
				catch
				{
					// Ignore read task completion exceptions; handled by fallback string assignment below.
				}

				stdout = stdoutTask.IsCompleted && !stdoutTask.IsFaulted && !stdoutTask.IsCanceled
					? stdoutTask.Result
					: string.Empty;
				stderr = stderrTask.IsCompleted && !stderrTask.IsFaulted && !stderrTask.IsCanceled
					? stderrTask.Result
					: string.Empty;
				if (process.ExitCode != 0)
				{
					error = $"PowerShell exited with code {process.ExitCode}.";
					return false;
				}

				return true;
			}
		}
		catch (Win32Exception ex)
		{
			error = ex.Message;
			return false;
		}
		catch (Exception ex)
		{
			error = ex.Message;
			return false;
		}
	}

	private static bool IsBackendUnavailableError(string error, string stderr)
	{
		string combined = $"{error}\n{stderr}".ToLowerInvariant();
		return combined.Contains("powershell") && combined.Contains("cannot find") ||
			combined.Contains("not recognized as the name of a cmdlet") ||
			combined.Contains("assembly 'system.speech' could not be found");
	}

	private static string ChooseBestError(string error, string stderr)
	{
		if (!string.IsNullOrWhiteSpace(stderr))
		{
			string[] lines = stderr.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
			if (lines.Length > 0)
			{
				return lines[0].Trim();
			}
		}

		return string.IsNullOrWhiteSpace(error) ? "Unknown error." : error;
	}

	private static string EscapeSingleQuotedPowerShellLiteral(string value)
	{
		return (value ?? string.Empty).Replace("'", "''");
	}

	private static bool IsWindowsPlatform()
	{
		PlatformID platform = Environment.OSVersion.Platform;
		return platform == PlatformID.Win32NT ||
			platform == PlatformID.Win32S ||
			platform == PlatformID.Win32Windows ||
			platform == PlatformID.WinCE;
	}

	private static string BuildCacheKey(string text, string voice)
	{
		return $"{voice}\n{text}";
	}

	private static bool TryGetCachedClipLocked(string cacheKey, out AudioClip clip)
	{
		clip = null!;
		if (!s_ClipCache.TryGetValue(cacheKey, out CachedTtsClip entry) ||
			entry == null ||
			entry.Clip == null)
		{
			return false;
		}

		entry.LastAccessUtcTicks = DateTime.UtcNow.Ticks;
		clip = entry.Clip;
		return true;
	}

	private static void StoreCachedClipLocked(string cacheKey, AudioClip clip)
	{
		if (s_ClipCache.TryGetValue(cacheKey, out CachedTtsClip existing) &&
			existing?.Clip != null)
		{
			UnityEngine.Object.Destroy(existing.Clip);
		}

		s_ClipCache[cacheKey] = new CachedTtsClip
		{
			Clip = clip,
			LastAccessUtcTicks = DateTime.UtcNow.Ticks
		};
		TrimCacheLocked();
	}

	private static void TrimCacheLocked()
	{
		if (s_ClipCache.Count <= kMaxCachedClips)
		{
			return;
		}

		int removeCount = s_ClipCache.Count - kMaxCachedClips;
		List<KeyValuePair<string, CachedTtsClip>> entries = s_ClipCache.ToList();
		entries.Sort(static (left, right) => left.Value.LastAccessUtcTicks.CompareTo(right.Value.LastAccessUtcTicks));
		for (int i = 0; i < removeCount; i++)
		{
			KeyValuePair<string, CachedTtsClip> entry = entries[i];
			if (entry.Value?.Clip != null)
			{
				UnityEngine.Object.Destroy(entry.Value.Clip);
			}

			s_ClipCache.Remove(entry.Key);
		}
	}

	private static void TryDeleteFile(string path)
	{
		try
		{
			if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
			{
				File.Delete(path);
			}
		}
		catch
		{
			// Ignore temp cleanup failures.
		}
	}

	private static uint ComputeStableHash(string text)
	{
		unchecked
		{
			uint hash = 2166136261;
			for (int i = 0; i < text.Length; i++)
			{
				hash ^= text[i];
				hash *= 16777619;
			}

			return hash;
		}
	}
}
