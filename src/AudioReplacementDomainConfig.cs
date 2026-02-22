using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using Colossal.Logging;

namespace SirenChanger;

[DataContract]
// Generic config model used by non-siren audio domains (engine/ambient).
internal sealed class AudioReplacementDomainConfig
{
	[DataMember(Order = 1)]
	public bool Enabled { get; set; } = true;

	[DataMember(Order = 2)]
	public string CustomFolderName { get; set; } = string.Empty;

	[DataMember(Order = 3)]
	public string DefaultSelection { get; set; } = SirenReplacementConfig.DefaultSelectionToken;

	[DataMember(Order = 4)]
	public string EditProfileSelection { get; set; } = string.Empty;

	[DataMember(Order = 5)]
	public string CopyFromProfileSelection { get; set; } = string.Empty;

	[DataMember(Order = 6)]
	public Dictionary<string, SirenSfxProfile> CustomProfiles { get; set; } = new Dictionary<string, SirenSfxProfile>();

	[DataMember(Order = 7)]
	public Dictionary<string, string> TargetSelections { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

	[DataMember(Order = 8)]
	public string TargetSelectionTarget { get; set; } = string.Empty;

	[DataMember(Order = 9)]
	public List<string> KnownTargets { get; set; } = new List<string>();

	[DataMember(Order = 10)]
	public SirenFallbackBehavior MissingSelectionFallbackBehavior { get; set; } = SirenFallbackBehavior.Default;

	[DataMember(Order = 11)]
	public string AlternateFallbackSelection { get; set; } = SirenReplacementConfig.DefaultSelectionToken;

	[DataMember(Order = 20)]
	public long LastCatalogScanUtcTicks { get; set; }

	[DataMember(Order = 21)]
	public int LastCatalogScanFileCount { get; set; }

	[DataMember(Order = 22)]
	public int LastCatalogScanAddedCount { get; set; }

	[DataMember(Order = 23)]
	public int LastCatalogScanRemovedCount { get; set; }

	[DataMember(Order = 24)]
	public List<string> LastCatalogScanChangedFiles { get; set; } = new List<string>();

	[DataMember(Order = 30)]
	public long LastTargetScanUtcTicks { get; set; }

	[DataMember(Order = 31)]
	public string LastTargetScanStatus { get; set; } = string.Empty;

	[DataMember(Order = 40)]
	public long LastValidationUtcTicks { get; set; }

	[DataMember(Order = 41)]
	public string LastValidationReport { get; set; } = string.Empty;

	// Create a normalized default config for one domain.
	public static AudioReplacementDomainConfig CreateDefault(string defaultFolderName)
	{
		return new AudioReplacementDomainConfig
		{
			CustomFolderName = defaultFolderName
		}.Normalize(defaultFolderName);
	}

	// Load config from disk, or create a default file when missing/invalid.
	public static AudioReplacementDomainConfig LoadOrCreate(string settingsPath, string defaultFolderName, ILog log)
	{
		if (!File.Exists(settingsPath))
		{
			AudioReplacementDomainConfig created = CreateDefault(defaultFolderName);
			Save(settingsPath, created, log);
			return created;
		}

		try
		{
			string json = File.ReadAllText(settingsPath);
			AudioReplacementDomainConfig? loaded;
			string error;
			if (!JsonDataSerializer.TryDeserialize(json, out loaded, out error) || loaded == null)
			{
				log.Warn($"Domain settings parse failed at {settingsPath}: {error}");
				return CreateDefault(defaultFolderName);
			}

			return loaded.Normalize(defaultFolderName);
		}
		catch (Exception ex)
		{
			log.Warn($"Domain settings read failed at {settingsPath}: {ex.Message}");
			return CreateDefault(defaultFolderName);
		}
	}

	// Persist config JSON to disk.
	public static void Save(string settingsPath, AudioReplacementDomainConfig settings, ILog log)
	{
		try
		{
			string? directory = Path.GetDirectoryName(settingsPath);
			if (!string.IsNullOrWhiteSpace(directory))
			{
				Directory.CreateDirectory(directory);
			}

			string json = JsonDataSerializer.Serialize(settings);
			File.WriteAllText(settingsPath, json);
		}
		catch (Exception ex)
		{
			log.Warn($"Failed to write domain settings file {settingsPath}: {ex.Message}");
		}
	}

	// Normalize all fields into safe values.
	internal AudioReplacementDomainConfig Normalize(string defaultFolderName)
	{
		CustomFolderName = SirenPathUtils.SanitizeSingleFolderName(CustomFolderName, defaultFolderName);
		if (string.IsNullOrWhiteSpace(CustomFolderName))
		{
			CustomFolderName = defaultFolderName;
		}

		DefaultSelection = NormalizeSelection(DefaultSelection);
		EditProfileSelection = NormalizeProfileKey(EditProfileSelection);
		CopyFromProfileSelection = NormalizeProfileKey(CopyFromProfileSelection);
		AlternateFallbackSelection = NormalizeSelection(AlternateFallbackSelection);

		if (!Enum.IsDefined(typeof(SirenFallbackBehavior), MissingSelectionFallbackBehavior))
		{
			MissingSelectionFallbackBehavior = SirenFallbackBehavior.Default;
		}

		CustomProfiles = NormalizeProfiles(CustomProfiles);
		KnownTargets = NormalizeTargetList(KnownTargets);
		TargetSelections = NormalizeTargetSelections(TargetSelections, CustomProfiles.Keys);
		TargetSelectionTarget = NormalizeTargetKey(TargetSelectionTarget);
		LastCatalogScanChangedFiles = NormalizeTextList(LastCatalogScanChangedFiles);
		LastValidationReport ??= string.Empty;
		LastTargetScanStatus ??= string.Empty;
		return this;
	}

	// Try resolve one profile key.
	internal bool TryGetProfile(string key, out SirenSfxProfile profile)
	{
		profile = null!;
		string normalized = NormalizeProfileKey(key);
		if (string.IsNullOrWhiteSpace(normalized))
		{
			return false;
		}

		return CustomProfiles.TryGetValue(normalized, out profile!);
	}

	// Get selection for one target key, defaulting to Default token.
	internal string GetTargetSelection(string targetKey)
	{
		string key = NormalizeTargetKey(targetKey);
		if (string.IsNullOrWhiteSpace(key))
		{
			return SirenReplacementConfig.DefaultSelectionToken;
		}

		return TargetSelections.TryGetValue(key, out string value)
			? NormalizeSelection(value)
			: SirenReplacementConfig.DefaultSelectionToken;
	}

	// Set or clear selection for one target key.
	internal bool SetTargetSelection(string targetKey, string selection)
	{
		string key = NormalizeTargetKey(targetKey);
		if (string.IsNullOrWhiteSpace(key))
		{
			return false;
		}

		string normalizedSelection = NormalizeSelection(selection);
		if (IsDefaultSelection(normalizedSelection))
		{
			return TargetSelections.Remove(key);
		}

		if (TargetSelections.TryGetValue(key, out string existing) &&
			string.Equals(existing, normalizedSelection, StringComparison.Ordinal))
		{
			return false;
		}

		TargetSelections[key] = normalizedSelection;
		return true;
	}

	// Update the currently focused target entry in options.
	internal void SetTargetSelectionTarget(string targetKey)
	{
		TargetSelectionTarget = NormalizeTargetKey(targetKey);
	}

	// Synchronize known target names and target selections against discovered targets.
	internal bool SynchronizeTargets(ICollection<string> discoveredTargets)
	{
		Dictionary<string, string> canonical = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		if (discoveredTargets != null)
		{
			foreach (string raw in discoveredTargets)
			{
				string normalized = NormalizeTargetKey(raw);
				if (string.IsNullOrWhiteSpace(normalized))
				{
					continue;
				}

				canonical[normalized] = normalized;
			}
		}

		bool changed = false;
		Dictionary<string, string> filtered = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		Dictionary<string, string> normalizedSelections = NormalizeTargetSelections(TargetSelections, CustomProfiles.Keys);
		foreach (KeyValuePair<string, string> pair in normalizedSelections)
		{
			if (!canonical.TryGetValue(pair.Key, out string canonicalKey))
			{
				changed = true;
				continue;
			}

			filtered[canonicalKey] = pair.Value;
		}

		if (!DictionariesEqualIgnoreCase(TargetSelections, filtered))
		{
			TargetSelections = filtered;
			changed = true;
		}

		List<string> sortedTargets = canonical.Values.OrderBy(static v => v, StringComparer.OrdinalIgnoreCase).ToList();
		if (!ListsEqualIgnoreCase(KnownTargets, sortedTargets))
		{
			KnownTargets = sortedTargets;
			changed = true;
		}

		string normalizedTarget = NormalizeTargetKey(TargetSelectionTarget);
		string nextTarget = string.Empty;
		if (sortedTargets.Count > 0)
		{
			nextTarget = sortedTargets[0];
			for (int i = 0; i < sortedTargets.Count; i++)
			{
				if (string.Equals(sortedTargets[i], normalizedTarget, StringComparison.OrdinalIgnoreCase))
				{
					nextTarget = sortedTargets[i];
					break;
				}
			}
		}

		if (!string.Equals(TargetSelectionTarget, nextTarget, StringComparison.Ordinal))
		{
			TargetSelectionTarget = nextTarget;
			changed = true;
		}

		return changed;
	}

	// Ensure saved references point to available profile keys.
	internal bool EnsureSelectionsValid(ICollection<string> availableKeys)
	{
		bool changed = false;
		HashSet<string> available = new HashSet<string>(availableKeys ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

		string normalizedDefault = NormalizeSelection(DefaultSelection);
		if (!IsDefaultSelection(normalizedDefault) && !available.Contains(normalizedDefault))
		{
			normalizedDefault = SirenReplacementConfig.DefaultSelectionToken;
		}

		if (!string.Equals(DefaultSelection, normalizedDefault, StringComparison.Ordinal))
		{
			DefaultSelection = normalizedDefault;
			changed = true;
		}

		string normalizedFallback = NormalizeSelection(AlternateFallbackSelection);
		if (!IsDefaultSelection(normalizedFallback) && !available.Contains(normalizedFallback))
		{
			normalizedFallback = SirenReplacementConfig.DefaultSelectionToken;
		}

		if (!string.Equals(AlternateFallbackSelection, normalizedFallback, StringComparison.Ordinal))
		{
			AlternateFallbackSelection = normalizedFallback;
			changed = true;
		}

		if (string.IsNullOrWhiteSpace(EditProfileSelection) || !available.Contains(EditProfileSelection))
		{
			string next = available.Count > 0 ? available.OrderBy(static v => v, StringComparer.OrdinalIgnoreCase).First() : string.Empty;
			if (!string.Equals(EditProfileSelection, next, StringComparison.Ordinal))
			{
				EditProfileSelection = next;
				changed = true;
			}
		}

		if (string.IsNullOrWhiteSpace(CopyFromProfileSelection) || !available.Contains(CopyFromProfileSelection))
		{
			string fallback = !string.IsNullOrWhiteSpace(EditProfileSelection)
				? EditProfileSelection
				: available.OrderBy(static v => v, StringComparer.OrdinalIgnoreCase).FirstOrDefault() ?? string.Empty;
			if (!string.Equals(CopyFromProfileSelection, fallback, StringComparison.Ordinal))
			{
				CopyFromProfileSelection = fallback;
				changed = true;
			}
		}

		Dictionary<string, string> normalizedSelections = NormalizeTargetSelections(TargetSelections, available);
		if (!DictionariesEqualIgnoreCase(TargetSelections, normalizedSelections))
		{
			TargetSelections = normalizedSelections;
			changed = true;
		}

		string normalizedTarget = NormalizeTargetKey(TargetSelectionTarget);
		if (!string.Equals(TargetSelectionTarget, normalizedTarget, StringComparison.Ordinal))
		{
			TargetSelectionTarget = normalizedTarget;
			changed = true;
		}

		return changed;
	}

	private static string NormalizeSelection(string selection)
	{
		if (string.IsNullOrWhiteSpace(selection))
		{
			return SirenReplacementConfig.DefaultSelectionToken;
		}

		if (string.Equals(selection.Trim(), SirenReplacementConfig.DefaultSelectionToken, StringComparison.OrdinalIgnoreCase))
		{
			return SirenReplacementConfig.DefaultSelectionToken;
		}

		string normalizedKey = NormalizeProfileKey(selection);
		return string.IsNullOrWhiteSpace(normalizedKey) ? SirenReplacementConfig.DefaultSelectionToken : normalizedKey;
	}

	internal static bool IsDefaultSelection(string? selection)
	{
		string normalized = selection?.Trim() ?? string.Empty;
		if (normalized.Length == 0)
		{
			return true;
		}

		return string.Equals(normalized, SirenReplacementConfig.DefaultSelectionToken, StringComparison.OrdinalIgnoreCase);
	}

	internal static string NormalizeTargetKey(string? targetKey)
	{
		return targetKey?.Trim() ?? string.Empty;
	}

	internal static string NormalizeProfileKey(string? key)
	{
		return SirenPathUtils.NormalizeProfileKey(key ?? string.Empty);
	}

	private static Dictionary<string, SirenSfxProfile> NormalizeProfiles(Dictionary<string, SirenSfxProfile>? source)
	{
		Dictionary<string, SirenSfxProfile> result = new Dictionary<string, SirenSfxProfile>(StringComparer.OrdinalIgnoreCase);
		if (source == null)
		{
			return result;
		}

		foreach (KeyValuePair<string, SirenSfxProfile> entry in source)
		{
			string key = NormalizeProfileKey(entry.Key);
			if (string.IsNullOrWhiteSpace(key))
			{
				continue;
			}

			result[key] = (entry.Value ?? SirenSfxProfile.CreateFallback()).ClampCopy();
		}

		return result;
	}

	private static List<string> NormalizeTargetList(List<string>? source)
	{
		List<string> result = new List<string>();
		if (source == null)
		{
			return result;
		}

		HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		for (int i = 0; i < source.Count; i++)
		{
			string key = NormalizeTargetKey(source[i]);
			if (string.IsNullOrWhiteSpace(key) || !seen.Add(key))
			{
				continue;
			}

			result.Add(key);
		}

		result.Sort(StringComparer.OrdinalIgnoreCase);
		return result;
	}

	private static List<string> NormalizeTextList(List<string>? source)
	{
		List<string> result = new List<string>();
		if (source == null)
		{
			return result;
		}

		HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		for (int i = 0; i < source.Count; i++)
		{
			string value = (source[i] ?? string.Empty).Trim();
			if (string.IsNullOrWhiteSpace(value) || !seen.Add(value))
			{
				continue;
			}

			result.Add(value);
		}

		return result;
	}

	private static Dictionary<string, string> NormalizeTargetSelections(
		Dictionary<string, string>? source,
		ICollection<string> availableSelectionKeys)
	{
		Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		HashSet<string> available = new HashSet<string>(availableSelectionKeys ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
		if (source == null)
		{
			return result;
		}

		foreach (KeyValuePair<string, string> entry in source)
		{
			string targetKey = NormalizeTargetKey(entry.Key);
			if (string.IsNullOrWhiteSpace(targetKey))
			{
				continue;
			}

			string selection = NormalizeSelection(entry.Value);
			if (IsDefaultSelection(selection) || !available.Contains(selection))
			{
				continue;
			}

			result[targetKey] = selection;
		}

		return result;
	}

	private static bool DictionariesEqualIgnoreCase(Dictionary<string, string> left, Dictionary<string, string> right)
	{
		if (ReferenceEquals(left, right))
		{
			return true;
		}

		if (left.Count != right.Count)
		{
			return false;
		}

		foreach (KeyValuePair<string, string> pair in left)
		{
			if (!right.TryGetValue(pair.Key, out string value) ||
				!string.Equals(pair.Value, value, StringComparison.Ordinal))
			{
				return false;
			}
		}

		return true;
	}

	private static bool ListsEqualIgnoreCase(List<string> left, List<string> right)
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
}

// Catalog synchronization helper for engine/ambient custom file folders.
internal static class AudioDomainCatalogSync
{
	// Synchronize profile keys with discovered files and keep selections valid.
	public static AudioDomainCatalogSyncResult Synchronize(
		AudioReplacementDomainConfig config,
		string settingsDirectory,
		string defaultFolderName,
		SirenSfxProfile template,
		ILog log,
		ICollection<string>? externalKeys = null,
		Func<string, SirenSfxProfile?>? externalProfileSeedProvider = null)
	{
		AudioDomainCatalogSyncResult result = new AudioDomainCatalogSyncResult();
		bool changed = false;

		config.Normalize(defaultFolderName);
		string folderName = string.IsNullOrWhiteSpace(config.CustomFolderName) ? defaultFolderName : config.CustomFolderName;
		List<string> localDiscoveredKeys = SirenPathUtils.EnumerateCustomSirenKeys(settingsDirectory, folderName);
		result.FoundFileCount = localDiscoveredKeys.Count;
		HashSet<string> available = new HashSet<string>(localDiscoveredKeys, StringComparer.OrdinalIgnoreCase);
		HashSet<string> external = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		if (externalKeys != null)
		{
			foreach (string rawExternalKey in externalKeys)
			{
				string normalizedExternal = AudioReplacementDomainConfig.NormalizeProfileKey(rawExternalKey ?? string.Empty);
				if (string.IsNullOrWhiteSpace(normalizedExternal) || AudioReplacementDomainConfig.IsDefaultSelection(normalizedExternal))
				{
					continue;
				}

				if (available.Add(normalizedExternal))
				{
					external.Add(normalizedExternal);
				}
				else if (!localDiscoveredKeys.Contains(normalizedExternal, StringComparer.OrdinalIgnoreCase))
				{
					external.Add(normalizedExternal);
				}
			}
		}

		SirenSfxProfile seed = (template ?? SirenSfxProfile.CreateFallback()).ClampCopy();
		List<string> availableSorted = available.OrderBy(static key => key, StringComparer.OrdinalIgnoreCase).ToList();

		for (int i = 0; i < availableSorted.Count; i++)
		{
			string key = availableSorted[i];
			if (config.CustomProfiles.ContainsKey(key))
			{
				continue;
			}

			bool isExternal = external.Contains(key);
			SirenSfxProfile? externalSeed = externalProfileSeedProvider?.Invoke(key);
			config.CustomProfiles[key] = (externalSeed ?? seed).ClampCopy();
			changed = true;
			result.AddedKeys.Add(key);
			if (isExternal)
			{
				log.Info($"Registered module audio profile: {key}");
			}
			else
			{
				log.Info($"Registered custom audio profile: {key}");
			}
		}

		List<string> stale = config.CustomProfiles.Keys.Where(key => !available.Contains(key)).ToList();
		for (int i = 0; i < stale.Count; i++)
		{
			string key = stale[i];
			config.CustomProfiles.Remove(key);
			changed = true;
			result.RemovedKeys.Add(key);
			if (AudioModuleCatalog.IsModuleSelection(key))
			{
				log.Info($"Removed unavailable module audio profile: {key}");
			}
			else
			{
				log.Info($"Removed missing custom audio profile: {key}");
			}
		}

		if (config.EnsureSelectionsValid(available))
		{
			changed = true;
		}

		result.ConfigChanged = changed;
		return result;
	}
}

// Result payload for one domain catalog synchronization pass.
internal sealed class AudioDomainCatalogSyncResult
{
	public bool ConfigChanged { get; set; }

	public int FoundFileCount { get; set; }

	public List<string> AddedKeys { get; } = new List<string>();

	public List<string> RemovedKeys { get; } = new List<string>();
}


