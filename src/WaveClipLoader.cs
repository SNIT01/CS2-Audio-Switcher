using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace SirenChanger;

// Loads custom siren audio files and caches decoded AudioClip instances.
internal static class WaveClipLoader
{
	private const int kMaxCachedClips = 128;

	private const long kMaxCachedClipBytes = 512L * 1024L * 1024L;

	private static readonly TimeSpan kOggLoadTimeout = TimeSpan.FromSeconds(10);

	private static readonly TimeSpan kAudioRetryCooldown = TimeSpan.FromSeconds(10);

	private static readonly Dictionary<string, CachedClip> s_ClipCache = new Dictionary<string, CachedClip>(StringComparer.OrdinalIgnoreCase);

	private static readonly Dictionary<string, PendingOggLoad> s_PendingOggLoads = new Dictionary<string, PendingOggLoad>(StringComparer.OrdinalIgnoreCase);

	private static readonly Dictionary<string, PendingWavLoad> s_PendingWavLoads = new Dictionary<string, PendingWavLoad>(StringComparer.OrdinalIgnoreCase);

	private static readonly Dictionary<string, FailedOggLoad> s_FailedOggLoads = new Dictionary<string, FailedOggLoad>(StringComparer.OrdinalIgnoreCase);

	private static readonly Dictionary<string, FailedWavLoad> s_FailedWavLoads = new Dictionary<string, FailedWavLoad>(StringComparer.OrdinalIgnoreCase);

	private static readonly List<string> s_PendingOggKeysScratch = new List<string>();

	private static readonly List<string> s_PendingWavKeysScratch = new List<string>();

	private static int s_AsyncCompletionVersion = 1;

	private static int s_LastAsyncPollFrame = -1;

	// Cached decoded clip metadata for hot reload safety.
	private sealed class CachedClip
	{
		public AudioClip Clip { get; set; } = null!;

		public long LastWriteUtcTicks { get; set; }

		public long FileLength { get; set; }

		public long LastAccessUtcTicks { get; set; }

		public long CacheBytes { get; set; }
	}

	// In-flight OGG decode request tracked across update ticks.
	private sealed class PendingOggLoad
	{
		public UnityWebRequest Request { get; set; } = null!;

		public UnityWebRequestAsyncOperation Operation { get; set; } = null!;

		public long FileLength { get; set; }

		public long LastWriteUtcTicks { get; set; }

		public long StartedUtcTicks { get; set; }
	}

	private sealed class PendingWavLoad
	{
		public Task<WavDecodeResult> DecodeTask { get; set; } = null!;

		public long FileLength { get; set; }

		public long LastWriteUtcTicks { get; set; }

		public long StartedUtcTicks { get; set; }
	}

	// Recent OGG failure cache so repeated retries do not spam logs or stall frame time.
	private sealed class FailedOggLoad
	{
		public long FileLength { get; set; }

		public long LastWriteUtcTicks { get; set; }

		public long FailedUtcTicks { get; set; }

		public string Error { get; set; } = string.Empty;
	}

	private sealed class FailedWavLoad
	{
		public long FileLength { get; set; }

		public long LastWriteUtcTicks { get; set; }

		public long FailedUtcTicks { get; set; }

		public string Error { get; set; } = string.Empty;
	}

	private readonly struct WavDecodeResult
	{
		public WavDecodeResult(float[] samples, int channels, int sampleRate, string error)
		{
			Samples = samples;
			Channels = channels;
			SampleRate = sampleRate;
			Error = error;
		}

		public float[] Samples { get; }

		public int Channels { get; }

		public int SampleRate { get; }

		public string Error { get; }

		public bool Success => Samples != null && Samples.Length > 0 && Channels > 0 && SampleRate > 0 && string.IsNullOrWhiteSpace(Error);
	}

	// Tri-state result used by runtime apply logic.
	internal enum AudioLoadStatus
	{
		Success,
		Pending,
		Failure
	}

	public static int AsyncCompletionVersion => s_AsyncCompletionVersion;

	// Entry point: try cache first, then decode according to extension.
	internal static AudioLoadStatus LoadAudio(string filePath, out AudioClip clip, out string error)
	{
		error = string.Empty;
		clip = null!;
		string normalizedPath;

		try
		{
			normalizedPath = Path.GetFullPath(filePath);
			FileInfo fileInfo = new FileInfo(normalizedPath);
				if (!fileInfo.Exists)
				{
					error = "File does not exist.";
					return AudioLoadStatus.Failure;
				}

			long fileLength = fileInfo.Length;
			long lastWriteTicks = fileInfo.LastWriteTimeUtc.Ticks;
			if (TryGetCachedClip(normalizedPath, fileLength, lastWriteTicks, out AudioClip cached))
			{
				clip = cached;
				return AudioLoadStatus.Success;
			}

			return TryLoadAudioInternal(normalizedPath, fileLength, lastWriteTicks, out clip, out error);
		}
		catch (Exception ex)
		{
			error = ex.Message;
			return AudioLoadStatus.Failure;
		}
	}

	// Release all decoded clips and pending requests during mod unload.
	public static void ReleaseLoadedClips()
	{
		foreach (KeyValuePair<string, PendingOggLoad> item in s_PendingOggLoads)
		{
			DisposePendingOggLoad(item.Value, abort: true);
		}

		s_PendingOggLoads.Clear();
		s_PendingWavLoads.Clear();
		s_FailedOggLoads.Clear();
		s_FailedWavLoads.Clear();
		s_PendingOggKeysScratch.Clear();
		s_PendingWavKeysScratch.Clear();
		s_LastAsyncPollFrame = -1;

		foreach (KeyValuePair<string, CachedClip> item in s_ClipCache)
		{
			if (item.Value?.Clip != null)
			{
				UnityEngine.Object.Destroy(item.Value.Clip);
			}
		}

		s_ClipCache.Clear();
	}

	// Drive async OGG request completion from runtime update loop.
	public static void PollAsyncLoads()
	{
		int frame = Time.frameCount;
		if (s_LastAsyncPollFrame == frame)
		{
			return;
		}

		s_LastAsyncPollFrame = frame;
		if (s_PendingOggLoads.Count == 0 && s_PendingWavLoads.Count == 0)
		{
			return;
		}

		if (s_PendingOggLoads.Count > 0)
		{
			s_PendingOggKeysScratch.Clear();
			foreach (string key in s_PendingOggLoads.Keys)
			{
				s_PendingOggKeysScratch.Add(key);
			}

			for (int i = 0; i < s_PendingOggKeysScratch.Count; i++)
			{
				string path = s_PendingOggKeysScratch[i];
				if (!s_PendingOggLoads.TryGetValue(path, out PendingOggLoad? pending) || pending == null)
				{
					continue;
				}

				TryFinalizePendingOgg(path, pending, out _, out _);
			}

			s_PendingOggKeysScratch.Clear();
		}

		if (s_PendingWavLoads.Count > 0)
		{
			s_PendingWavKeysScratch.Clear();
			foreach (string key in s_PendingWavLoads.Keys)
			{
				s_PendingWavKeysScratch.Add(key);
			}

			for (int i = 0; i < s_PendingWavKeysScratch.Count; i++)
			{
				string path = s_PendingWavKeysScratch[i];
				if (!s_PendingWavLoads.TryGetValue(path, out PendingWavLoad? pending) || pending == null)
				{
					continue;
				}

				TryFinalizePendingWav(path, pending, out _, out _);
			}

			s_PendingWavKeysScratch.Clear();
		}
	}

	// Return a cache hit only if file metadata still matches.
	private static bool TryGetCachedClip(string path, long fileLength, long lastWriteTicks, out AudioClip clip)
	{
		clip = null!;
		if (!s_ClipCache.TryGetValue(path, out CachedClip entry) || entry == null)
		{
			return false;
		}

		if (entry.Clip == null || entry.FileLength != fileLength || entry.LastWriteUtcTicks != lastWriteTicks)
		{
			if (entry.Clip != null)
			{
				UnityEngine.Object.Destroy(entry.Clip);
			}

			s_ClipCache.Remove(path);
			return false;
		}

		entry.LastAccessUtcTicks = DateTime.UtcNow.Ticks;
		clip = entry.Clip;
		return true;
	}

	// Store or replace a cache entry and enforce LRU-like trimming.
	private static void StoreCachedClip(string path, AudioClip clip, long fileLength, long lastWriteTicks)
	{
		if (s_ClipCache.TryGetValue(path, out CachedClip existing) && existing?.Clip != null)
		{
			UnityEngine.Object.Destroy(existing.Clip);
		}

		long cacheBytes = EstimateClipCacheBytes(clip);
		if (cacheBytes > kMaxCachedClipBytes)
		{
			s_ClipCache.Remove(path);
			return;
		}

		s_ClipCache[path] = new CachedClip
		{
			Clip = clip,
			FileLength = fileLength,
			LastWriteUtcTicks = lastWriteTicks,
			LastAccessUtcTicks = DateTime.UtcNow.Ticks,
			CacheBytes = cacheBytes
		};

		TrimClipCache();
	}

	private static long EstimateClipCacheBytes(AudioClip clip)
	{
		try
		{
			if (clip == null || clip.samples <= 0 || clip.channels <= 0)
			{
				return 0;
			}

			return (long)clip.samples * clip.channels * sizeof(float);
		}
		catch
		{
			return 0;
		}
	}

	private static long GetTotalCachedClipBytes()
	{
		long total = 0;
		foreach (KeyValuePair<string, CachedClip> pair in s_ClipCache)
		{
			total += Math.Max(0L, pair.Value?.CacheBytes ?? 0L);
		}

		return total;
	}

	// Remove least-recently-used entries when cache size or byte budget is exceeded.
	private static void TrimClipCache()
	{
		long totalBytes = GetTotalCachedClipBytes();
		if (s_ClipCache.Count <= kMaxCachedClips && totalBytes <= kMaxCachedClipBytes)
		{
			return;
		}

		while (s_ClipCache.Count > kMaxCachedClips || totalBytes > kMaxCachedClipBytes)
		{
			string oldestKey = string.Empty;
			long oldestTicks = long.MaxValue;
			foreach (KeyValuePair<string, CachedClip> pair in s_ClipCache)
			{
				CachedClip? candidate = pair.Value;
				if (candidate == null)
				{
					continue;
				}

				if (candidate.LastAccessUtcTicks >= oldestTicks)
				{
					continue;
				}

				oldestTicks = candidate.LastAccessUtcTicks;
				oldestKey = pair.Key;
			}

			if (string.IsNullOrWhiteSpace(oldestKey))
			{
				break;
			}

			long removedBytes = 0;
			if (s_ClipCache.TryGetValue(oldestKey, out CachedClip entry) && entry?.Clip != null)
			{
				removedBytes = Math.Max(0L, entry.CacheBytes);
				UnityEngine.Object.Destroy(entry.Clip);
			}

			s_ClipCache.Remove(oldestKey);
			totalBytes = Math.Max(0L, totalBytes - removedBytes);
		}
	}

	// Route decode path by extension.
	private static AudioLoadStatus TryLoadAudioInternal(string normalizedPath, long fileLength, long lastWriteTicks, out AudioClip clip, out string error)
	{
		string extension = Path.GetExtension(normalizedPath);
		if (string.Equals(extension, ".wav", StringComparison.OrdinalIgnoreCase))
		{
			return TryLoadWavInternal(normalizedPath, fileLength, lastWriteTicks, out clip, out error);
		}

		if (string.Equals(extension, ".ogg", StringComparison.OrdinalIgnoreCase))
		{
			return TryLoadOggInternal(normalizedPath, fileLength, lastWriteTicks, out clip, out error);
		}

		clip = null!;
		error = $"Unsupported audio extension '{extension}'. Supported: {SirenPathUtils.GetSupportedCustomSirenExtensionsLabel()}.";
		return AudioLoadStatus.Failure;
	}

	// Start or poll async WAV read/decode without blocking the simulation thread.
	private static AudioLoadStatus TryLoadWavInternal(string normalizedPath, long fileLength, long lastWriteTicks, out AudioClip clip, out string error)
	{
		clip = null!;
		if (s_FailedWavLoads.TryGetValue(normalizedPath, out FailedWavLoad? failed) && failed != null)
		{
			if (failed.FileLength != fileLength || failed.LastWriteUtcTicks != lastWriteTicks)
			{
				s_FailedWavLoads.Remove(normalizedPath);
			}
			else
			{
				long elapsedTicks = DateTime.UtcNow.Ticks - failed.FailedUtcTicks;
				if (elapsedTicks < kAudioRetryCooldown.Ticks)
				{
					error = $"WAV load recently failed: {failed.Error}";
					return AudioLoadStatus.Failure;
				}

				s_FailedWavLoads.Remove(normalizedPath);
			}
		}

		if (s_PendingWavLoads.TryGetValue(normalizedPath, out PendingWavLoad? pending) && pending != null)
		{
			if (pending.FileLength != fileLength || pending.LastWriteUtcTicks != lastWriteTicks)
			{
				s_PendingWavLoads.Remove(normalizedPath);
				s_AsyncCompletionVersion++;
			}
			else
			{
				AudioLoadStatus status = TryFinalizePendingWav(normalizedPath, pending, out clip, out error);
				if (status != AudioLoadStatus.Pending)
				{
					return status;
				}

				error = "WAV decode is still in progress. Try again in a moment.";
				return AudioLoadStatus.Pending;
			}
		}

		s_PendingWavLoads[normalizedPath] = new PendingWavLoad
		{
			DecodeTask = Task.Run(() => DecodeWavFile(normalizedPath)),
			FileLength = fileLength,
			LastWriteUtcTicks = lastWriteTicks,
			StartedUtcTicks = DateTime.UtcNow.Ticks
		};

		error = "WAV decode started asynchronously. Try again shortly.";
		return AudioLoadStatus.Pending;
	}

	private static WavDecodeResult DecodeWavFile(string normalizedPath)
	{
		try
		{
			byte[] wavBytes = File.ReadAllBytes(normalizedPath);
			if (wavBytes == null || wavBytes.Length == 0)
			{
				return new WavDecodeResult(Array.Empty<float>(), 0, 0, "WAV payload was empty.");
			}

			if (!TryDecodeWav(wavBytes, out float[] samples, out int channels, out int sampleRate, out string error))
			{
				return new WavDecodeResult(Array.Empty<float>(), 0, 0, error);
			}

			return new WavDecodeResult(samples, channels, sampleRate, string.Empty);
		}
		catch (Exception ex)
		{
			return new WavDecodeResult(Array.Empty<float>(), 0, 0, ex.Message);
		}
	}

	// Decode WAV bytes and create an in-memory Unity clip (used by file and TTS paths).
	internal static bool TryCreateClipFromWavBytes(
		byte[] wavBytes,
		string clipName,
		out AudioClip clip,
		out string error)
	{
		clip = null!;
		error = string.Empty;
		if (wavBytes == null || wavBytes.Length == 0)
		{
			error = "WAV payload was empty.";
			return false;
		}

		float[] samples;
		int channels;
		int sampleRate;
		string parseError;
		if (!TryDecodeWav(wavBytes, out samples, out channels, out sampleRate, out parseError))
		{
			error = parseError;
			return false;
		}

		return TryCreateClipFromDecodedSamples(samples, channels, sampleRate, clipName, out clip, out error);
	}

	private static bool TryCreateClipFromDecodedSamples(
		float[] samples,
		int channels,
		int sampleRate,
		string clipName,
		out AudioClip clip,
		out string error)
	{
		clip = null!;
		error = string.Empty;
		if (samples == null || samples.Length == 0 || channels <= 0 || sampleRate <= 0)
		{
			error = "WAV payload contained no sample frames.";
			return false;
		}

		int sampleFrames = samples.Length / channels;
		if (sampleFrames <= 0)
		{
			error = "WAV payload contained no sample frames.";
			return false;
		}

		string resolvedClipName = string.IsNullOrWhiteSpace(clipName) ? "SC_AudioClip" : clipName.Trim();
		AudioClip loaded = AudioClip.Create(resolvedClipName, sampleFrames, channels, sampleRate, stream: false);
		if (!loaded.SetData(samples, 0))
		{
			UnityEngine.Object.Destroy(loaded);
			error = "Unity failed to copy PCM samples into the audio clip.";
			return false;
		}

		clip = loaded;
		return true;
	}

	private static AudioLoadStatus TryFinalizePendingWav(string path, PendingWavLoad pending, out AudioClip clip, out string error)
	{
		clip = null!;
		error = string.Empty;
		if (!pending.DecodeTask.IsCompleted)
		{
			return AudioLoadStatus.Pending;
		}

		if (pending.DecodeTask.IsCanceled)
		{
			error = "WAV decode was canceled.";
			RecordFailedWavLoad(path, pending.FileLength, pending.LastWriteUtcTicks, error);
			s_PendingWavLoads.Remove(path);
			s_AsyncCompletionVersion++;
			return AudioLoadStatus.Failure;
		}

		if (pending.DecodeTask.IsFaulted)
		{
			error = pending.DecodeTask.Exception?.GetBaseException().Message ?? "WAV decode failed.";
			RecordFailedWavLoad(path, pending.FileLength, pending.LastWriteUtcTicks, error);
			s_PendingWavLoads.Remove(path);
			s_AsyncCompletionVersion++;
			return AudioLoadStatus.Failure;
		}

		WavDecodeResult result = pending.DecodeTask.Result;
		if (!result.Success)
		{
			error = string.IsNullOrWhiteSpace(result.Error) ? "WAV decode failed." : result.Error;
			RecordFailedWavLoad(path, pending.FileLength, pending.LastWriteUtcTicks, error);
			s_PendingWavLoads.Remove(path);
			s_AsyncCompletionVersion++;
			return AudioLoadStatus.Failure;
		}

		if (!TryCreateClipFromDecodedSamples(
			result.Samples,
			result.Channels,
			result.SampleRate,
			$"SC_{Path.GetFileNameWithoutExtension(path)}",
			out AudioClip loaded,
			out error))
		{
			RecordFailedWavLoad(path, pending.FileLength, pending.LastWriteUtcTicks, error);
			s_PendingWavLoads.Remove(path);
			s_AsyncCompletionVersion++;
			return AudioLoadStatus.Failure;
		}

		StoreCachedClip(path, loaded, pending.FileLength, pending.LastWriteUtcTicks);
		clip = loaded;
		s_FailedWavLoads.Remove(path);
		s_PendingWavLoads.Remove(path);
		s_AsyncCompletionVersion++;
		return AudioLoadStatus.Success;
	}

	// Start or poll async OGG decode without blocking the simulation thread.
	private static AudioLoadStatus TryLoadOggInternal(string normalizedPath, long fileLength, long lastWriteTicks, out AudioClip clip, out string error)
	{
		clip = null!;
		if (s_FailedOggLoads.TryGetValue(normalizedPath, out FailedOggLoad? failed) && failed != null)
		{
			if (failed.FileLength != fileLength || failed.LastWriteUtcTicks != lastWriteTicks)
			{
				s_FailedOggLoads.Remove(normalizedPath);
			}
			else
			{
				long elapsedTicks = DateTime.UtcNow.Ticks - failed.FailedUtcTicks;
				if (elapsedTicks < kAudioRetryCooldown.Ticks)
				{
					error = $"OGG load recently failed: {failed.Error}";
					return AudioLoadStatus.Failure;
				}

				s_FailedOggLoads.Remove(normalizedPath);
			}
		}

		if (s_PendingOggLoads.TryGetValue(normalizedPath, out PendingOggLoad? pending) && pending != null)
		{
			if (pending.FileLength != fileLength || pending.LastWriteUtcTicks != lastWriteTicks)
			{
				DisposePendingOggLoad(pending, abort: true);
				s_PendingOggLoads.Remove(normalizedPath);
				s_AsyncCompletionVersion++;
			}
			else
			{
				AudioLoadStatus status = TryFinalizePendingOgg(normalizedPath, pending, out clip, out error);
				if (status != AudioLoadStatus.Pending)
				{
					return status;
				}

				error = "OGG decode is still in progress. Try again in a moment.";
				return AudioLoadStatus.Pending;
			}
		}

		string fileUri = new Uri(normalizedPath).AbsoluteUri;
		UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(fileUri, AudioType.OGGVORBIS);
		UnityWebRequestAsyncOperation operation = request.SendWebRequest();
		s_PendingOggLoads[normalizedPath] = new PendingOggLoad
		{
			Request = request,
			Operation = operation,
			FileLength = fileLength,
			LastWriteUtcTicks = lastWriteTicks,
			StartedUtcTicks = DateTime.UtcNow.Ticks
		};

		error = "OGG decode started asynchronously. Try again shortly.";
		return AudioLoadStatus.Pending;
	}

	// Finalize a pending OGG request into cache once Unity completes decode.
	private static AudioLoadStatus TryFinalizePendingOgg(string path, PendingOggLoad pending, out AudioClip clip, out string error)
	{
		clip = null!;
		error = string.Empty;

		long nowTicks = DateTime.UtcNow.Ticks;
		if (!pending.Operation.isDone)
		{
			long elapsedTicks = nowTicks - pending.StartedUtcTicks;
			if (elapsedTicks < kOggLoadTimeout.Ticks)
			{
				return AudioLoadStatus.Pending;
			}

			error = "Timed out while decoding OGG data.";
			RecordFailedOggLoad(path, pending.FileLength, pending.LastWriteUtcTicks, error);
			DisposePendingOggLoad(pending, abort: true);
			s_PendingOggLoads.Remove(path);
			s_AsyncCompletionVersion++;
			return AudioLoadStatus.Failure;
		}

		if (pending.Request.result != UnityWebRequest.Result.Success)
		{
			error = string.IsNullOrWhiteSpace(pending.Request.error)
				? "UnityWebRequest returned an unknown error while decoding OGG."
				: pending.Request.error;
			RecordFailedOggLoad(path, pending.FileLength, pending.LastWriteUtcTicks, error);
			DisposePendingOggLoad(pending, abort: false);
			s_PendingOggLoads.Remove(path);
			s_AsyncCompletionVersion++;
			return AudioLoadStatus.Failure;
		}

		AudioClip? loaded = DownloadHandlerAudioClip.GetContent(pending.Request);
		if (loaded == null)
		{
			error = "Unity returned no audio clip for the OGG file.";
			RecordFailedOggLoad(path, pending.FileLength, pending.LastWriteUtcTicks, error);
			DisposePendingOggLoad(pending, abort: false);
			s_PendingOggLoads.Remove(path);
			s_AsyncCompletionVersion++;
			return AudioLoadStatus.Failure;
		}

		loaded.name = $"SC_{Path.GetFileNameWithoutExtension(path)}";
		StoreCachedClip(path, loaded, pending.FileLength, pending.LastWriteUtcTicks);
		clip = loaded;
		s_FailedOggLoads.Remove(path);

		DisposePendingOggLoad(pending, abort: false);
		s_PendingOggLoads.Remove(path);
		s_AsyncCompletionVersion++;
		return AudioLoadStatus.Success;
	}

	// Track failure metadata for short cooldown-based retry throttling.
	private static void RecordFailedOggLoad(string path, long fileLength, long lastWriteUtcTicks, string error)
	{
		s_FailedOggLoads[path] = new FailedOggLoad
		{
			FileLength = fileLength,
			LastWriteUtcTicks = lastWriteUtcTicks,
			FailedUtcTicks = DateTime.UtcNow.Ticks,
			Error = error
		};
	}

	private static void RecordFailedWavLoad(string path, long fileLength, long lastWriteUtcTicks, string error)
	{
		s_FailedWavLoads[path] = new FailedWavLoad
		{
			FileLength = fileLength,
			LastWriteUtcTicks = lastWriteUtcTicks,
			FailedUtcTicks = DateTime.UtcNow.Ticks,
			Error = error
		};
	}

	// Dispose UnityWebRequest safely for both timeout and normal completion paths.
	private static void DisposePendingOggLoad(PendingOggLoad pending, bool abort)
	{
		try
		{
			if (abort)
			{
				pending.Request.Abort();
			}
		}
		catch
		{
			// Ignore abort exceptions while cleaning up pending requests.
		}

		pending.Request.Dispose();
	}

	// Parse RIFF/WAV container and extract sample data payload.
	private static bool TryDecodeWav(byte[] data, out float[] samples, out int channels, out int sampleRate, out string error)
	{
		samples = Array.Empty<float>();
		channels = 0;
		sampleRate = 0;
		error = string.Empty;

		if (data.Length < 44)
		{
			error = "WAV too small.";
			return false;
		}

		if (ReadFourCC(data, 0) != "RIFF" || ReadFourCC(data, 8) != "WAVE")
		{
			error = "Invalid WAV header (RIFF/WAVE missing).";
			return false;
		}

		ushort format = 0;
		ushort bitsPerSample = 0;
		int sampleDataOffset = -1;
		int sampleDataLength = 0;

		int offset = 12;
		while (offset + 8 <= data.Length)
		{
			string chunkId = ReadFourCC(data, offset);
			int chunkSize = BitConverter.ToInt32(data, offset + 4);
			offset += 8;

			if (chunkSize < 0 || offset + chunkSize > data.Length)
			{
				error = "Malformed WAV chunk.";
				return false;
			}

			if (chunkId == "fmt ")
			{
				if (chunkSize < 16)
				{
					error = "Invalid fmt chunk.";
					return false;
				}

				format = BitConverter.ToUInt16(data, offset + 0);
				channels = BitConverter.ToUInt16(data, offset + 2);
				sampleRate = BitConverter.ToInt32(data, offset + 4);
				bitsPerSample = BitConverter.ToUInt16(data, offset + 14);
			}
			else if (chunkId == "data")
			{
				sampleDataOffset = offset;
				sampleDataLength = chunkSize;
			}

			offset += chunkSize;
			if ((chunkSize & 1) == 1 && offset < data.Length)
			{
				offset++;
			}
		}

		if (channels <= 0 || sampleRate <= 0 || sampleDataOffset < 0 || sampleDataLength <= 0)
		{
			error = "Missing required WAV chunks.";
			return false;
		}

		if (format == 1)
		{
			return TryDecodePcm(data, sampleDataOffset, sampleDataLength, bitsPerSample, channels, out samples, out error);
		}

		if (format == 3 && bitsPerSample == 32)
		{
			return TryDecodeFloat32(data, sampleDataOffset, sampleDataLength, channels, out samples, out error);
		}

		error = $"Unsupported WAV format: format={format}, bits={bitsPerSample}. Supported: PCM 8/16/24/32 and IEEE float 32.";
		return false;
	}

	// Decode integer PCM bit depths into normalized float samples.
	private static bool TryDecodePcm(byte[] data, int dataOffset, int dataLength, int bitsPerSample, int channels, out float[] samples, out string error)
	{
		error = string.Empty;
		samples = Array.Empty<float>();

		if (bitsPerSample != 8 && bitsPerSample != 16 && bitsPerSample != 24 && bitsPerSample != 32)
		{
			error = $"Unsupported PCM bit depth: {bitsPerSample}.";
			return false;
		}

		int bytesPerSample = bitsPerSample / 8;
		if (dataOffset < 0 ||
			dataLength < 0 ||
			dataOffset > data.Length - dataLength ||
			bytesPerSample <= 0 ||
			dataLength < bytesPerSample)
		{
			error = "Invalid PCM sample size.";
			return false;
		}

		int rawSampleCount = dataLength / bytesPerSample;
		int alignedSampleCount = rawSampleCount - (rawSampleCount % channels);
		samples = new float[alignedSampleCount];

		if (bitsPerSample == 8)
		{
			for (int i = 0; i < alignedSampleCount; i++)
			{
				samples[i] = (data[dataOffset + i] - 128f) / 128f;
			}
			return true;
		}

		if (bitsPerSample == 16)
		{
			int offset = dataOffset;
			for (int i = 0; i < alignedSampleCount; i++)
			{
				short value = BitConverter.ToInt16(data, offset);
				samples[i] = value / 32768f;
				offset += 2;
			}
			return true;
		}

		if (bitsPerSample == 24)
		{
			int offset = dataOffset;
			for (int i = 0; i < alignedSampleCount; i++)
			{
				int sample = data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16);
				if ((sample & 0x800000) != 0)
				{
					sample |= unchecked((int)0xFF000000);
				}
				samples[i] = sample / 8388608f;
				offset += 3;
			}
			return true;
		}

		int offset32 = dataOffset;
		for (int i = 0; i < alignedSampleCount; i++)
		{
			int value = BitConverter.ToInt32(data, offset32);
			samples[i] = value / 2147483648f;
			offset32 += 4;
		}
		return true;
	}

	// Decode IEEE float32 sample payload.
	private static bool TryDecodeFloat32(byte[] data, int dataOffset, int dataLength, int channels, out float[] samples, out string error)
	{
		error = string.Empty;
		samples = Array.Empty<float>();

		if (dataOffset < 0 ||
			dataLength < 0 ||
			dataOffset > data.Length - dataLength ||
			(dataLength & 3) != 0)
		{
			error = "Invalid float32 data length.";
			return false;
		}

		int rawSampleCount = dataLength / 4;
		int alignedSampleCount = rawSampleCount - (rawSampleCount % channels);
		samples = new float[alignedSampleCount];

		int offset = dataOffset;
		for (int i = 0; i < alignedSampleCount; i++)
		{
			samples[i] = BitConverter.ToSingle(data, offset);
			offset += 4;
		}

		return true;
	}

	// Read ASCII chunk IDs (e.g., RIFF, WAVE, fmt, data).
	private static string ReadFourCC(byte[] bytes, int offset)
	{
		return Encoding.ASCII.GetString(bytes, offset, 4);
	}
}
