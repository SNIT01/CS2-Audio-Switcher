using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using Colossal.Logging;
using Game.Modding;
using Game.SceneFlow;

namespace SirenChanger;

// Discovers optional Audio Switcher module packs and exposes their audio entries.
internal static class AudioModuleCatalog
{
	// Prefix used to distinguish module-backed selections from local custom files.
	internal const string ModuleSelectionPrefix = "__module__";

	private const string kManifestFileName = "AudioSwitcherModule.json";

	private const string kLegacyManifestFileName = "SirenChangerModule.json";

	private static readonly Dictionary<string, ModuleAudioEntry> s_SirenEntries = new Dictionary<string, ModuleAudioEntry>(StringComparer.OrdinalIgnoreCase);

	private static readonly Dictionary<string, ModuleAudioEntry> s_VehicleEngineEntries = new Dictionary<string, ModuleAudioEntry>(StringComparer.OrdinalIgnoreCase);

	private static readonly Dictionary<string, ModuleAudioEntry> s_AmbientEntries = new Dictionary<string, ModuleAudioEntry>(StringComparer.OrdinalIgnoreCase);

	private static string[] s_ModuleRoots = Array.Empty<string>();

	private const int kRootPriorityActiveLoadedMod = 0;

	private const int kRootPriorityKnownLocation = 100;

	internal static bool Refresh(ILog log, string currentModRootPath)
	{
		// Re-scan all candidate roots and replace in-memory catalogs only when content changed.
		Dictionary<string, ModuleAudioEntry> nextSirens = new Dictionary<string, ModuleAudioEntry>(StringComparer.OrdinalIgnoreCase);
		Dictionary<string, ModuleAudioEntry> nextVehicleEngines = new Dictionary<string, ModuleAudioEntry>(StringComparer.OrdinalIgnoreCase);
		Dictionary<string, ModuleAudioEntry> nextAmbient = new Dictionary<string, ModuleAudioEntry>(StringComparer.OrdinalIgnoreCase);
		List<string> rootList = CollectCandidateRoots(currentModRootPath);

		for (int i = 0; i < rootList.Count; i++)
		{
			string rootPath = rootList[i];
			TryLoadManifestFromRoot(rootPath, nextSirens, nextVehicleEngines, nextAmbient, log);
		}

		bool changed =
			!DictionaryContentEquals(s_SirenEntries, nextSirens) ||
			!DictionaryContentEquals(s_VehicleEngineEntries, nextVehicleEngines) ||
			!DictionaryContentEquals(s_AmbientEntries, nextAmbient) ||
			!SequenceEqualsIgnoreCase(s_ModuleRoots, rootList);
		if (!changed)
		{
			return false;
		}

		ReplaceEntries(s_SirenEntries, nextSirens);
		ReplaceEntries(s_VehicleEngineEntries, nextVehicleEngines);
		ReplaceEntries(s_AmbientEntries, nextAmbient);
		s_ModuleRoots = rootList.ToArray();
		log.Info($"Audio module scan complete. Roots: {s_ModuleRoots.Length}, Sirens: {s_SirenEntries.Count}, Engines: {s_VehicleEngineEntries.Count}, Ambient: {s_AmbientEntries.Count}");
		return true;
	}

	internal static string[] GetProfileKeys(DeveloperAudioDomain domain)
	{
		// Stable ordering keeps dropdown values deterministic between scans.
		Dictionary<string, ModuleAudioEntry> map = GetDomainMap(domain);
		if (map.Count == 0)
		{
			return Array.Empty<string>();
		}

		return map.Keys
			.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
			.ToArray();
	}

	internal static bool TryGetFilePath(DeveloperAudioDomain domain, string profileKey, out string filePath)
	{
		// Resolve module path only when key exists and file is still present on disk.
		filePath = string.Empty;
		string normalized = SirenPathUtils.NormalizeProfileKey(profileKey ?? string.Empty);
		if (string.IsNullOrWhiteSpace(normalized))
		{
			return false;
		}

		Dictionary<string, ModuleAudioEntry> map = GetDomainMap(domain);
		if (!map.TryGetValue(normalized, out ModuleAudioEntry entry))
		{
			return false;
		}

		if (!File.Exists(entry.FilePath))
		{
			return false;
		}

		filePath = entry.FilePath;
		return true;
	}

	internal static bool TryGetProfileTemplate(DeveloperAudioDomain domain, string profileKey, out SirenSfxProfile profile)
	{
		// Return a defensive clone so callers cannot mutate catalog state.
		profile = null!;
		string normalized = SirenPathUtils.NormalizeProfileKey(profileKey ?? string.Empty);
		if (string.IsNullOrWhiteSpace(normalized))
		{
			return false;
		}

		Dictionary<string, ModuleAudioEntry> map = GetDomainMap(domain);
		if (!map.TryGetValue(normalized, out ModuleAudioEntry entry) || entry.ProfileTemplate == null)
		{
			return false;
		}

		profile = entry.ProfileTemplate.ClampCopy();
		return true;
	}

	internal static bool TryGetDisplayName(string profileKey, out string displayName)
	{
		displayName = string.Empty;
		string normalized = SirenPathUtils.NormalizeProfileKey(profileKey ?? string.Empty);
		if (string.IsNullOrWhiteSpace(normalized))
		{
			return false;
		}

		if (TryGetEntry(normalized, out ModuleAudioEntry entry))
		{
			displayName = entry.DisplayName;
			return true;
		}

		return false;
	}

	internal static bool IsModuleSelection(string profileKey)
	{
		string normalized = SirenPathUtils.NormalizeProfileKey(profileKey ?? string.Empty);
		if (string.IsNullOrWhiteSpace(normalized))
		{
			return false;
		}

		string prefix = $"{ModuleSelectionPrefix}/";
		return normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
	}

	private static List<string> CollectCandidateRoots(string currentModRootPath)
	{
		// Merge roots from loaded mods and known directories, using priority for dedupe ordering.
		Dictionary<string, int> roots = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		string currentRoot = string.Empty;
		if (!string.IsNullOrWhiteSpace(currentModRootPath))
		{
			currentRoot = SafeGetFullPath(currentModRootPath);
		}

		AddActiveExecutableRoots(roots, currentRoot);
		AddKnownModRoots(roots, currentRoot);
		return roots
			.OrderBy(static pair => pair.Value)
			.ThenBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
			.Select(static pair => pair.Key)
			.ToList();
	}

	private static void AddActiveExecutableRoots(IDictionary<string, int> roots, string currentModRootPath)
	{
		ModManager? manager = GameManager.instance?.modManager;
		if (manager == null)
		{
			return;
		}

		foreach (ModManager.ModInfo modInfo in manager)
		{
			if (modInfo?.asset == null)
			{
				continue;
			}

			string assetPath = modInfo.asset.path ?? string.Empty;
			if (string.IsNullOrWhiteSpace(assetPath))
			{
				continue;
			}

			string? rootPath = Path.GetDirectoryName(assetPath);
			AddRoot(roots, rootPath, currentModRootPath, kRootPriorityActiveLoadedMod);
		}
	}

	private static void AddKnownModRoots(IDictionary<string, int> roots, string currentModRootPath)
	{
		// Include managed and unmanaged workshop/cache locations used by the game launcher.
		string userData = Environment.GetEnvironmentVariable("CSII_USERDATAPATH") ?? string.Empty;
		string gameUserData = !string.IsNullOrWhiteSpace(userData)
			? userData
			: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Colossal Order", "Cities Skylines II");
		if (string.IsNullOrWhiteSpace(gameUserData))
		{
			return;
		}

		string[] parentDirectories = new[]
		{
			Path.Combine(gameUserData, "Mods"),
			Path.Combine(gameUserData, ".cache", "Mods", "mods_subscribed"),
			Path.Combine(gameUserData, ".cache", "Mods", "mods_unmanaged"),
			Path.Combine(gameUserData, ".cache", "Mods", "mods_workInProgress")
		};

		for (int i = 0; i < parentDirectories.Length; i++)
		{
			AddRootChildren(roots, parentDirectories[i], currentModRootPath, kRootPriorityKnownLocation);
		}
	}

	private static void AddRootChildren(IDictionary<string, int> roots, string parentDirectory, string currentModRootPath, int priority)
	{
		if (string.IsNullOrWhiteSpace(parentDirectory) || !Directory.Exists(parentDirectory))
		{
			return;
		}

		try
		{
			IEnumerable<string> directories = Directory.EnumerateDirectories(parentDirectory, "*", SearchOption.TopDirectoryOnly);
			foreach (string directory in directories)
			{
				AddRoot(roots, directory, currentModRootPath, priority);
			}
		}
		catch
		{
			// Ignore inaccessible entries and continue with available candidates.
		}
	}

	private static void AddRoot(IDictionary<string, int> roots, string? rootPath, string currentModRootPath, int priority)
	{
		if (string.IsNullOrWhiteSpace(rootPath))
		{
			return;
		}

		string fullPath = SafeGetFullPath(rootPath!);
		if (string.IsNullOrWhiteSpace(fullPath) || !Directory.Exists(fullPath))
		{
			return;
		}

		if (!string.IsNullOrWhiteSpace(currentModRootPath) &&
			string.Equals(fullPath, currentModRootPath, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}

		if (roots.TryGetValue(fullPath, out int existingPriority))
		{
			if (priority < existingPriority)
			{
				roots[fullPath] = priority;
			}

			return;
		}

		roots.Add(fullPath, priority);
	}

	private static void TryLoadManifestFromRoot(
		string rootPath,
		IDictionary<string, ModuleAudioEntry> sirens,
		IDictionary<string, ModuleAudioEntry> vehicleEngines,
		IDictionary<string, ModuleAudioEntry> ambient,
		ILog log)
	{
		// Accept both current and legacy manifest file names for backward compatibility.
		string manifestPath = ResolveManifestPath(rootPath);
		if (string.IsNullOrWhiteSpace(manifestPath))
		{
			return;
		}

		string json;
		try
		{
			json = File.ReadAllText(manifestPath);
		}
		catch (Exception ex)
		{
			log.Warn($"Audio module manifest read failed: {manifestPath}. {ex.Message}");
			return;
		}

		if (!JsonDataSerializer.TryDeserialize(json, out AudioModuleManifest? manifest, out string error) || manifest == null)
		{
			log.Warn($"Audio module manifest parse failed: {manifestPath}. {error}");
			return;
		}

		if (manifest.SchemaVersion != 0 && manifest.SchemaVersion != 1)
		{
			log.Warn($"Audio module manifest has unsupported schemaVersion '{manifest.SchemaVersion}': {manifestPath}");
			return;
		}

		string moduleId = BuildModuleId(manifest, rootPath);
		string moduleDisplayName = string.IsNullOrWhiteSpace(manifest.DisplayName)
			? moduleId
			: manifest.DisplayName.Trim();
		List<AudioModuleManifestEntry> sirenEntries = manifest.Sirens ?? new List<AudioModuleManifestEntry>();
		List<AudioModuleManifestEntry> vehicleEngineEntries = manifest.VehicleEngines ?? new List<AudioModuleManifestEntry>();
		List<AudioModuleManifestEntry> ambientEntries = manifest.Ambient ?? new List<AudioModuleManifestEntry>();

		RegisterEntries(rootPath, moduleId, moduleDisplayName, DeveloperAudioDomain.Siren, sirenEntries, sirens, log);
		RegisterEntries(rootPath, moduleId, moduleDisplayName, DeveloperAudioDomain.VehicleEngine, vehicleEngineEntries, vehicleEngines, log);
		RegisterEntries(rootPath, moduleId, moduleDisplayName, DeveloperAudioDomain.Ambient, ambientEntries, ambient, log);
	}

	private static void RegisterEntries(
		string rootPath,
		string moduleId,
		string moduleDisplayName,
		DeveloperAudioDomain domain,
		IList<AudioModuleManifestEntry> entries,
		IDictionary<string, ModuleAudioEntry> destination,
		ILog log)
	{
		// Normalize and validate every entry before inserting into the domain map.
		if (entries == null || entries.Count == 0)
		{
			return;
		}

		for (int i = 0; i < entries.Count; i++)
		{
			AudioModuleManifestEntry entry = entries[i] ?? new AudioModuleManifestEntry();
			if (!TryResolveModuleAudioPath(rootPath, entry.File, out string absolutePath))
			{
				log.Warn($"Audio module entry skipped due to invalid file path. Module='{moduleDisplayName}', Domain={domain}, File='{entry.File}'");
				continue;
			}

			string profileKey = BuildProfileKey(domain, moduleId, entry.Key, entry.File);
			if (string.IsNullOrWhiteSpace(profileKey))
			{
				log.Warn($"Audio module entry skipped due to invalid profile key. Module='{moduleDisplayName}', Domain={domain}, File='{entry.File}'");
				continue;
			}

			string displayName = BuildDisplayName(moduleDisplayName, entry, profileKey);
			SirenSfxProfile? profileTemplate = entry.Profile?.ClampCopy();
			ModuleAudioEntry normalized = new ModuleAudioEntry(
				profileKey,
				displayName,
				absolutePath,
				moduleId,
				moduleDisplayName,
				profileTemplate);
			if (destination.ContainsKey(profileKey))
			{
				log.Warn($"Audio module entry key collision skipped: '{profileKey}' from module '{moduleDisplayName}'.");
				continue;
			}

			destination.Add(profileKey, normalized);
		}
	}

	private static string BuildProfileKey(DeveloperAudioDomain domain, string moduleId, string entryKey, string filePath)
	{
		// Keys are namespaced by module and domain to avoid collisions with local profiles.
		string normalizedModuleId = SanitizeModuleSegment(moduleId);
		if (string.IsNullOrWhiteSpace(normalizedModuleId))
		{
			return string.Empty;
		}

		string normalizedEntry = SirenPathUtils.NormalizeProfileKey(entryKey ?? string.Empty);
		if (string.IsNullOrWhiteSpace(normalizedEntry))
		{
			normalizedEntry = SirenPathUtils.NormalizeProfileKey(filePath ?? string.Empty);
		}

		if (string.IsNullOrWhiteSpace(normalizedEntry))
		{
			return string.Empty;
		}

		string raw = $"{ModuleSelectionPrefix}/{GetDomainSegment(domain)}/{normalizedModuleId}/{normalizedEntry}";
		return SirenPathUtils.NormalizeProfileKey(raw);
	}

	private static string BuildDisplayName(string moduleDisplayName, AudioModuleManifestEntry entry, string profileKey)
	{
		string name = (entry.DisplayName ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(name))
		{
			string fallback = Path.GetFileNameWithoutExtension(entry.File ?? string.Empty);
			if (string.IsNullOrWhiteSpace(fallback))
			{
				string normalized = SirenPathUtils.NormalizeProfileKey(profileKey);
				fallback = Path.GetFileNameWithoutExtension(normalized);
			}

			name = string.IsNullOrWhiteSpace(fallback) ? profileKey : fallback;
		}

		return $"{name} [Module: {moduleDisplayName}]";
	}

	private static bool TryResolveModuleAudioPath(string rootPath, string relativePath, out string absolutePath)
	{
		// Normalize and enforce root containment to block path traversal in manifests.
		absolutePath = string.Empty;
		string normalizedRelative = SirenPathUtils.NormalizeProfileKey(relativePath ?? string.Empty);
		if (string.IsNullOrWhiteSpace(normalizedRelative))
		{
			return false;
		}

		string root = SafeGetFullPath(rootPath);
		if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
		{
			return false;
		}

		string rootWithSeparator = EnsureTrailingSeparator(root);
		string combined;
		try
		{
			combined = Path.GetFullPath(Path.Combine(rootWithSeparator, normalizedRelative.Replace('/', Path.DirectorySeparatorChar)));
		}
		catch
		{
			return false;
		}

		if (!combined.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		string extension = Path.GetExtension(combined);
		if (!SirenPathUtils.IsSupportedCustomSirenExtension(extension))
		{
			return false;
		}

		if (!File.Exists(combined))
		{
			return false;
		}

		absolutePath = combined;
		return true;
	}

	private static string ResolveManifestPath(string rootPath)
	{
		string primary = Path.Combine(rootPath, kManifestFileName);
		if (File.Exists(primary))
		{
			return primary;
		}

		string legacy = Path.Combine(rootPath, kLegacyManifestFileName);
		if (File.Exists(legacy))
		{
			return legacy;
		}

		return string.Empty;
	}

	private static string BuildModuleId(AudioModuleManifest manifest, string rootPath)
	{
		// Prefer explicit ID, then display name, then folder name as a final fallback.
		string raw = manifest.ModuleId ?? string.Empty;
		if (string.IsNullOrWhiteSpace(raw))
		{
			raw = manifest.DisplayName ?? string.Empty;
		}

		if (string.IsNullOrWhiteSpace(raw))
		{
			raw = new DirectoryInfo(rootPath).Name;
		}

		return SanitizeModuleSegment(raw);
	}

	private static string SanitizeModuleSegment(string value)
	{
		// Keep module identifier URL/path-safe and deterministic across platforms.
		if (string.IsNullOrWhiteSpace(value))
		{
			return string.Empty;
		}

		char[] chars = value.Trim().ToCharArray();
		for (int i = 0; i < chars.Length; i++)
		{
			char c = chars[i];
			if (char.IsLetterOrDigit(c) || c == '-' || c == '_')
			{
				continue;
			}

			chars[i] = '-';
		}

		string normalized = new string(chars);
		while (normalized.Contains("--", StringComparison.Ordinal))
		{
			normalized = normalized.Replace("--", "-", StringComparison.Ordinal);
		}

		normalized = normalized.Trim('-');
		if (string.IsNullOrWhiteSpace(normalized))
		{
			return string.Empty;
		}

		return normalized;
	}

	private static string GetDomainSegment(DeveloperAudioDomain domain)
	{
		switch (domain)
		{
			case DeveloperAudioDomain.Siren:
				return "sirens";
			case DeveloperAudioDomain.VehicleEngine:
				return "vehicle-engines";
			case DeveloperAudioDomain.Ambient:
				return "ambient";
			default:
				return "unknown";
		}
	}

	private static bool TryGetEntry(string normalizedKey, out ModuleAudioEntry entry)
	{
		entry = null!;
		if (s_SirenEntries.TryGetValue(normalizedKey, out entry))
		{
			return true;
		}

		if (s_VehicleEngineEntries.TryGetValue(normalizedKey, out entry))
		{
			return true;
		}

		if (s_AmbientEntries.TryGetValue(normalizedKey, out entry))
		{
			return true;
		}

		return false;
	}

	private static Dictionary<string, ModuleAudioEntry> GetDomainMap(DeveloperAudioDomain domain)
	{
		switch (domain)
		{
			case DeveloperAudioDomain.Siren:
				return s_SirenEntries;
			case DeveloperAudioDomain.VehicleEngine:
				return s_VehicleEngineEntries;
			case DeveloperAudioDomain.Ambient:
				return s_AmbientEntries;
			default:
				throw new ArgumentOutOfRangeException(nameof(domain), domain, "Unknown audio module domain.");
		}
	}

	private static bool DictionaryContentEquals(
		IDictionary<string, ModuleAudioEntry> left,
		IDictionary<string, ModuleAudioEntry> right)
	{
		// Content comparison avoids unnecessary UI refreshes when only object references changed.
		if (ReferenceEquals(left, right))
		{
			return true;
		}

		if (left.Count != right.Count)
		{
			return false;
		}

		foreach (KeyValuePair<string, ModuleAudioEntry> pair in left)
		{
			if (!right.TryGetValue(pair.Key, out ModuleAudioEntry other) || !pair.Value.ContentEquals(other))
			{
				return false;
			}
		}

		return true;
	}

	private static bool SequenceEqualsIgnoreCase(IList<string> left, IList<string> right)
	{
		if (ReferenceEquals(left, right))
		{
			return true;
		}

		if (left.Count != right.Count)
		{
			return false;
		}

		for (int i = 0; i < left.Count; i++)
		{
			if (!string.Equals(left[i], right[i], StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}
		}

		return true;
	}

	private static void ReplaceEntries(IDictionary<string, ModuleAudioEntry> destination, IDictionary<string, ModuleAudioEntry> source)
	{
		destination.Clear();
		foreach (KeyValuePair<string, ModuleAudioEntry> pair in source)
		{
			destination[pair.Key] = pair.Value;
		}
	}

	private static string SafeGetFullPath(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return string.Empty;
		}

		try
		{
			return Path.GetFullPath(path);
		}
		catch
		{
			return string.Empty;
		}
	}

	private static string EnsureTrailingSeparator(string path)
	{
		if (path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
		{
			return path;
		}

		return path + Path.DirectorySeparatorChar;
	}

	[DataContract]
	// On-disk schema for module manifests.
	private sealed class AudioModuleManifest
	{
		[DataMember(Order = 1, Name = "schemaVersion")]
		public int SchemaVersion { get; set; }

		[DataMember(Order = 2, Name = "moduleId")]
		public string ModuleId { get; set; } = string.Empty;

		[DataMember(Order = 3, Name = "displayName")]
		public string DisplayName { get; set; } = string.Empty;

		[DataMember(Order = 4, Name = "sirens")]
		public List<AudioModuleManifestEntry> Sirens { get; set; } = new List<AudioModuleManifestEntry>();

		[DataMember(Order = 5, Name = "vehicleEngines")]
		public List<AudioModuleManifestEntry> VehicleEngines { get; set; } = new List<AudioModuleManifestEntry>();

		[DataMember(Order = 6, Name = "ambient")]
		public List<AudioModuleManifestEntry> Ambient { get; set; } = new List<AudioModuleManifestEntry>();
	}

	[DataContract]
	// One manifest entry mapping a key to an audio file and optional SFX template.
	private sealed class AudioModuleManifestEntry
	{
		[DataMember(Order = 1, Name = "key")]
		public string Key { get; set; } = string.Empty;

		[DataMember(Order = 2, Name = "displayName")]
		public string DisplayName { get; set; } = string.Empty;

		[DataMember(Order = 3, Name = "file")]
		public string File { get; set; } = string.Empty;

		[DataMember(Order = 4, Name = "profile")]
		public SirenSfxProfile? Profile { get; set; }
	}

	// Cached resolved module entry stored per domain.
	private sealed class ModuleAudioEntry
	{
		public ModuleAudioEntry(
			string profileKey,
			string displayName,
			string filePath,
			string moduleId,
			string moduleDisplayName,
			SirenSfxProfile? profileTemplate)
		{
			ProfileKey = profileKey;
			DisplayName = displayName;
			FilePath = filePath;
			ModuleId = moduleId;
			ModuleDisplayName = moduleDisplayName;
			ProfileTemplate = profileTemplate;
		}

		public string ProfileKey { get; }

		public string DisplayName { get; }

		public string FilePath { get; }

		public string ModuleId { get; }

		public string ModuleDisplayName { get; }

		public SirenSfxProfile? ProfileTemplate { get; }

		public bool ContentEquals(ModuleAudioEntry? other)
		{
			if (other == null)
			{
				return false;
			}

			bool leftProfileIsNull = ProfileTemplate == null;
			bool rightProfileIsNull = other.ProfileTemplate == null;
			if (leftProfileIsNull != rightProfileIsNull)
			{
				return false;
			}

			if (!leftProfileIsNull && !ProfileTemplate!.ApproximatelyEquals(other.ProfileTemplate))
			{
				return false;
			}

			return string.Equals(ProfileKey, other.ProfileKey, StringComparison.OrdinalIgnoreCase) &&
				string.Equals(DisplayName, other.DisplayName, StringComparison.Ordinal) &&
				string.Equals(FilePath, other.FilePath, StringComparison.OrdinalIgnoreCase) &&
				string.Equals(ModuleId, other.ModuleId, StringComparison.OrdinalIgnoreCase) &&
				string.Equals(ModuleDisplayName, other.ModuleDisplayName, StringComparison.Ordinal);
		}
	}
}
