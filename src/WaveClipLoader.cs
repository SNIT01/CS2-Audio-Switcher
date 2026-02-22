using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace SirenChanger;

// Loads custom siren audio files and caches decoded AudioClip instances.
internal static class WaveClipLoader
{
	private const int kMaxCachedClips = 32;

	private static readonly TimeSpan kOggLoadTimeout = TimeSpan.FromSeconds(10);

	private static readonly TimeSpan kOggRetryCooldown = TimeSpan.FromSeconds(10);

	private static readonly Dictionary<string, CachedClip> s_ClipCache = new Dictionary<string, CachedClip>(StringComparer.OrdinalIgnoreCase);

	private static readonly Dictionary<string, PendingOggLoad> s_PendingOggLoads = new Dictionary<string, PendingOggLoad>(StringComparer.OrdinalIgnoreCase);

	private static readonly Dictionary<string, FailedOggLoad> s_FailedOggLoads = new Dictionary<string, FailedOggLoad>(StringComparer.OrdinalIgnoreCase);

	private static int s_AsyncCompletionVersion = 1;

	// Cached decoded clip metadata for hot reload safety.
	private sealed class CachedClip
	{
		public AudioClip Clip { get; set; } = null!;

		public long LastWriteUtcTicks { get; set; }

		public long FileLength { get; set; }

		public long LastAccessUtcTicks { get; set; }
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

	// Recent OGG failure cache so repeated retries do not spam logs or stall frame time.
	private sealed class FailedOggLoad
	{
		public long FileLength { get; set; }

		public long LastWriteUtcTicks { get; set; }

		public long FailedUtcTicks { get; set; }

		public string Error { get; set; } = string.Empty;
	}

	// Tri-state result used by runtime apply logic.
	internal enum AudioLoadStatus
	{
		Success,
		Pending,
		Failure
	}

	public static int AsyncCompletionVersion => s_AsyncCompletionVersion;

	// Compatibility wrapper for existing call sites that only care about success/failure.
	public static bool TryLoadAudio(string filePath, out AudioClip clip, out string error)
	{
		return LoadAudio(filePath, out clip, out error) == AudioLoadStatus.Success;
	}

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
		s_FailedOggLoads.Clear();

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
		if (s_PendingOggLoads.Count == 0)
		{
			return;
		}

		List<string> keys = s_PendingOggLoads.Keys.ToList();
		for (int i = 0; i < keys.Count; i++)
		{
			string path = keys[i];
			if (!s_PendingOggLoads.TryGetValue(path, out PendingOggLoad? pending) || pending == null)
			{
				continue;
			}

			TryFinalizePendingOgg(path, pending, out _, out _);
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

		s_ClipCache[path] = new CachedClip
		{
			Clip = clip,
			FileLength = fileLength,
			LastWriteUtcTicks = lastWriteTicks,
			LastAccessUtcTicks = DateTime.UtcNow.Ticks
		};

		TrimClipCache();
	}

	// Remove least-recently-used entries when cache size is exceeded.
	private static void TrimClipCache()
	{
		if (s_ClipCache.Count <= kMaxCachedClips)
		{
			return;
		}

		int removeCount = s_ClipCache.Count - kMaxCachedClips;
		List<string> evictionKeys = s_ClipCache
			.OrderBy(static pair => pair.Value.LastAccessUtcTicks)
			.Take(removeCount)
			.Select(static pair => pair.Key)
			.ToList();

		for (int i = 0; i < evictionKeys.Count; i++)
		{
			string key = evictionKeys[i];
			if (s_ClipCache.TryGetValue(key, out CachedClip entry) && entry?.Clip != null)
			{
				UnityEngine.Object.Destroy(entry.Clip);
			}

			s_ClipCache.Remove(key);
		}
	}

	// Route decode path by extension.
	private static AudioLoadStatus TryLoadAudioInternal(string normalizedPath, long fileLength, long lastWriteTicks, out AudioClip clip, out string error)
	{
		string extension = Path.GetExtension(normalizedPath);
		if (string.Equals(extension, ".wav", StringComparison.OrdinalIgnoreCase))
		{
			if (TryLoadWavInternal(normalizedPath, out AudioClip loaded, out error))
			{
				loaded.name = $"SC_{Path.GetFileNameWithoutExtension(normalizedPath)}";
				clip = loaded;
				StoreCachedClip(normalizedPath, loaded, fileLength, lastWriteTicks);
				return AudioLoadStatus.Success;
			}

			clip = null!;
			return AudioLoadStatus.Failure;
		}

		if (string.Equals(extension, ".ogg", StringComparison.OrdinalIgnoreCase))
		{
			return TryLoadOggInternal(normalizedPath, fileLength, lastWriteTicks, out clip, out error);
		}

		clip = null!;
		error = $"Unsupported audio extension '{extension}'. Supported: {SirenPathUtils.GetSupportedCustomSirenExtensionsLabel()}.";
		return AudioLoadStatus.Failure;
	}

	// Decode PCM/float WAV bytes and create a Unity clip.
	private static bool TryLoadWavInternal(string normalizedPath, out AudioClip clip, out string error)
	{
		clip = null!;
		byte[] wavBytes = File.ReadAllBytes(normalizedPath);
		float[] samples;
		int channels;
		int sampleRate;
		string parseError;
		if (!TryDecodeWav(wavBytes, out samples, out channels, out sampleRate, out parseError))
		{
			error = parseError;
			return false;
		}

		int sampleFrames = samples.Length / channels;
		AudioClip loaded = AudioClip.Create($"SC_{Path.GetFileNameWithoutExtension(normalizedPath)}", sampleFrames, channels, sampleRate, stream: false);
		if (!loaded.SetData(samples, 0))
		{
			UnityEngine.Object.Destroy(loaded);
			error = "Unity failed to copy PCM samples into the audio clip.";
			return false;
		}

		clip = loaded;
		error = string.Empty;
		return true;
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
				if (elapsedTicks < kOggRetryCooldown.Ticks)
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
		byte[]? sampleBytes = null;

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
				sampleBytes = new byte[chunkSize];
				Buffer.BlockCopy(data, offset, sampleBytes, 0, chunkSize);
			}

			offset += chunkSize;
			if ((chunkSize & 1) == 1 && offset < data.Length)
			{
				offset++;
			}
		}

		if (channels <= 0 || sampleRate <= 0 || sampleBytes == null)
		{
			error = "Missing required WAV chunks.";
			return false;
		}

		if (format == 1)
		{
			return TryDecodePcm(sampleBytes, bitsPerSample, channels, out samples, out error);
		}

		if (format == 3 && bitsPerSample == 32)
		{
			return TryDecodeFloat32(sampleBytes, channels, out samples, out error);
		}

		error = $"Unsupported WAV format: format={format}, bits={bitsPerSample}. Supported: PCM 8/16/24/32 and IEEE float 32.";
		return false;
	}

	// Decode integer PCM bit depths into normalized float samples.
	private static bool TryDecodePcm(byte[] data, int bitsPerSample, int channels, out float[] samples, out string error)
	{
		error = string.Empty;
		samples = Array.Empty<float>();

		if (bitsPerSample != 8 && bitsPerSample != 16 && bitsPerSample != 24 && bitsPerSample != 32)
		{
			error = $"Unsupported PCM bit depth: {bitsPerSample}.";
			return false;
		}

		int bytesPerSample = bitsPerSample / 8;
		if (bytesPerSample <= 0 || data.Length < bytesPerSample)
		{
			error = "Invalid PCM sample size.";
			return false;
		}

		int rawSampleCount = data.Length / bytesPerSample;
		int alignedSampleCount = rawSampleCount - (rawSampleCount % channels);
		samples = new float[alignedSampleCount];

		if (bitsPerSample == 8)
		{
			for (int i = 0; i < alignedSampleCount; i++)
			{
				samples[i] = (data[i] - 128f) / 128f;
			}
			return true;
		}

		if (bitsPerSample == 16)
		{
			int offset = 0;
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
			int offset = 0;
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

		int offset32 = 0;
		for (int i = 0; i < alignedSampleCount; i++)
		{
			int value = BitConverter.ToInt32(data, offset32);
			samples[i] = value / 2147483648f;
			offset32 += 4;
		}
		return true;
	}

	// Decode IEEE float32 sample payload.
	private static bool TryDecodeFloat32(byte[] data, int channels, out float[] samples, out string error)
	{
		error = string.Empty;
		samples = Array.Empty<float>();

		if ((data.Length & 3) != 0)
		{
			error = "Invalid float32 data length.";
			return false;
		}

		int rawSampleCount = data.Length / 4;
		int alignedSampleCount = rawSampleCount - (rawSampleCount % channels);
		samples = new float[alignedSampleCount];

		int offset = 0;
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
