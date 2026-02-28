using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Colossal;
using Colossal.Logging;

namespace SirenChanger;

[DataContract]
// Persistent registry mapping cities (save GUID) to sound-set identifiers.
internal sealed class CitySoundProfileRegistry
{
	internal const string SettingsFileName = "CitySoundProfileRegistry.json";

	internal const string ProfilesDirectoryName = "Profiles";

	internal const string DefaultSetId = "__default__";

	internal const string DefaultSetDisplayName = "Default";

	[DataMember(Order = 1)]
	public bool AutoApplyByCity { get; set; } = true;

	[DataMember(Order = 2)]
	public string ActiveSetId { get; set; } = DefaultSetId;

	[DataMember(Order = 3)]
	public string SelectedSetId { get; set; } = DefaultSetId;

	[DataMember(Order = 4)]
	public string PendingNewSetName { get; set; } = string.Empty;

	[DataMember(Order = 5)]
	public Dictionary<string, CitySoundProfileSet> Sets { get; set; } = new Dictionary<string, CitySoundProfileSet>(StringComparer.OrdinalIgnoreCase);

	[DataMember(Order = 6)]
	public List<CitySoundProfileBinding> Bindings { get; set; } = new List<CitySoundProfileBinding>();

	// Build a fully normalized registry with guaranteed default set.
	public static CitySoundProfileRegistry CreateDefault()
	{
		return new CitySoundProfileRegistry().Normalize();
	}

	// Load registry from disk and recover to defaults when file is missing or invalid.
	public static CitySoundProfileRegistry LoadOrCreate(string settingsPath, ILog log)
	{
		if (!File.Exists(settingsPath))
		{
			CitySoundProfileRegistry created = CreateDefault();
			Save(settingsPath, created, log);
			return created;
		}

		try
		{
			string json = File.ReadAllText(settingsPath);
			CitySoundProfileRegistry? loaded;
			string error;
			if (!JsonDataSerializer.TryDeserialize(json, out loaded, out error) || loaded == null)
			{
				log.Warn($"City sound profile registry parse failed at {settingsPath}: {error}");
				return CreateDefault();
			}

			return loaded.Normalize();
		}
		catch (Exception ex)
		{
			log.Warn($"City sound profile registry read failed at {settingsPath}: {ex.Message}");
			return CreateDefault();
		}
	}

	// Persist registry JSON after normalization to keep on-disk format stable.
	public static void Save(string settingsPath, CitySoundProfileRegistry settings, ILog log)
	{
		try
		{
			string? directory = Path.GetDirectoryName(settingsPath);
			if (!string.IsNullOrWhiteSpace(directory))
			{
				Directory.CreateDirectory(directory);
			}

			string json = JsonDataSerializer.Serialize(settings.Normalize());
			File.WriteAllText(settingsPath, json);
		}
		catch (Exception ex)
		{
			log.Warn($"Failed to write city sound profile registry {settingsPath}: {ex.Message}");
		}
	}

	// Canonicalize sets/bindings and enforce invariants used by runtime/UI code.
	internal CitySoundProfileRegistry Normalize()
	{
		Dictionary<string, CitySoundProfileSet> normalizedSets = new Dictionary<string, CitySoundProfileSet>(StringComparer.OrdinalIgnoreCase);
		if (Sets != null)
		{
			foreach (KeyValuePair<string, CitySoundProfileSet> pair in Sets)
			{
				CitySoundProfileSet value = pair.Value ?? new CitySoundProfileSet();
				string setId = NormalizeSetId(string.IsNullOrWhiteSpace(value.SetId) ? pair.Key : value.SetId);
				if (string.IsNullOrWhiteSpace(setId))
				{
					continue;
				}

				string fallbackDisplay = string.Equals(setId, DefaultSetId, StringComparison.OrdinalIgnoreCase)
					? DefaultSetDisplayName
					: setId;
				value.SetId = setId;
				value.DisplayName = NormalizeDisplayName(value.DisplayName, fallbackDisplay);
				if (value.CreatedUtcTicks <= 0)
				{
					value.CreatedUtcTicks = DateTime.UtcNow.Ticks;
				}

				normalizedSets[setId] = value;
			}
		}

		if (!normalizedSets.TryGetValue(DefaultSetId, out CitySoundProfileSet? defaultSet) || defaultSet == null)
		{
			normalizedSets[DefaultSetId] = new CitySoundProfileSet
			{
				SetId = DefaultSetId,
				DisplayName = DefaultSetDisplayName,
				CreatedUtcTicks = DateTime.UtcNow.Ticks
			};
		}
		else
		{
			defaultSet.SetId = DefaultSetId;
			defaultSet.DisplayName = DefaultSetDisplayName;
			if (defaultSet.CreatedUtcTicks <= 0)
			{
				defaultSet.CreatedUtcTicks = DateTime.UtcNow.Ticks;
			}
		}

		Sets = normalizedSets;
		ActiveSetId = NormalizeExistingSetId(ActiveSetId, normalizedSets);
		SelectedSetId = NormalizeExistingSetId(SelectedSetId, normalizedSets, fallback: ActiveSetId);
		PendingNewSetName = (PendingNewSetName ?? string.Empty).Trim();
		Bindings = NormalizeBindings(Bindings, normalizedSets);
		return this;
	}

	// Check whether one normalized set ID exists in the registry.
	internal bool ContainsSet(string setId)
	{
		string normalized = NormalizeSetId(setId);
		return Sets.ContainsKey(normalized);
	}

	// Ensure one set entry exists and return its normalized/stable ID.
	internal string EnsureSet(string setId, string displayName)
	{
		string normalized = NormalizeSetId(setId);
		string fallbackName = string.Equals(normalized, DefaultSetId, StringComparison.OrdinalIgnoreCase)
			? DefaultSetDisplayName
			: normalized;
		if (!Sets.TryGetValue(normalized, out CitySoundProfileSet set) || set == null)
		{
			set = new CitySoundProfileSet
			{
				SetId = normalized,
				DisplayName = NormalizeDisplayName(displayName, fallbackName),
				CreatedUtcTicks = DateTime.UtcNow.Ticks
			};
			Sets[normalized] = set;
		}
		else
		{
			set.SetId = normalized;
			set.DisplayName = NormalizeDisplayName(set.DisplayName, fallbackName);
			if (set.CreatedUtcTicks <= 0)
			{
				set.CreatedUtcTicks = DateTime.UtcNow.Ticks;
			}
		}

		return normalized;
	}

	// Remove one non-default set and reassign dependent bindings to Default.
	internal bool RemoveSet(string setId)
	{
		string normalized = NormalizeSetId(setId);
		if (string.Equals(normalized, DefaultSetId, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		if (!Sets.Remove(normalized))
		{
			return false;
		}

		ReassignBindingsToDefault(normalized);
		ActiveSetId = NormalizeExistingSetId(ActiveSetId, Sets);
		SelectedSetId = NormalizeExistingSetId(SelectedSetId, Sets, fallback: ActiveSetId);
		return true;
	}

	// Move all references from a removed set to Default to avoid dangling bindings.
	internal void ReassignBindingsToDefault(string removedSetId)
	{
		string removed = NormalizeSetId(removedSetId);
		for (int i = 0; i < Bindings.Count; i++)
		{
			CitySoundProfileBinding binding = Bindings[i];
			if (string.Equals(binding.SetId, removed, StringComparison.OrdinalIgnoreCase))
			{
				binding.SetId = DefaultSetId;
			}
		}
	}

	// Resolve the display name for one set ID when it exists.
	internal bool TryGetSetDisplayName(string setId, out string displayName)
	{
		displayName = string.Empty;
		string normalized = NormalizeSetId(setId);
		if (!Sets.TryGetValue(normalized, out CitySoundProfileSet set) || set == null)
		{
			return false;
		}

		displayName = set.DisplayName;
		return true;
	}

	internal string ResolveBoundSetId(string saveAssetGuid)
	{
		// Binding resolution is GUID-only; unknown GUIDs always fall back to Default.
		string normalizedGuid = NormalizeGuidKey(saveAssetGuid);
		if (string.IsNullOrWhiteSpace(normalizedGuid))
		{
			return DefaultSetId;
		}

		for (int i = 0; i < Bindings.Count; i++)
		{
			CitySoundProfileBinding binding = Bindings[i];
			if (string.Equals(binding.SaveAssetGuid, normalizedGuid, StringComparison.OrdinalIgnoreCase))
			{
				return NormalizeExistingSetId(binding.SetId, Sets);
			}
		}

		return DefaultSetId;
	}

	internal bool HasBindingForCity(string saveAssetGuid)
	{
		// Fast existence check for one city GUID.
		string normalizedGuid = NormalizeGuidKey(saveAssetGuid);
		if (string.IsNullOrWhiteSpace(normalizedGuid))
		{
			return false;
		}

		for (int i = 0; i < Bindings.Count; i++)
		{
			if (string.Equals(Bindings[i].SaveAssetGuid, normalizedGuid, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}

		return false;
	}

	internal bool UpsertBinding(
		string saveAssetGuid,
		string setId,
		string lastSeenDisplayName,
		string lastSeenMapName,
		string saveSessionGuid)
	{
		// Insert/update one GUID binding and only mark changed when meaningful fields differ.
		string normalizedGuid = NormalizeGuidKey(saveAssetGuid);
		if (string.IsNullOrWhiteSpace(normalizedGuid))
		{
			return false;
		}

		string normalizedSet = NormalizeExistingSetId(setId, Sets);
		string normalizedSessionGuid = NormalizeSessionGuid(saveSessionGuid);
		int bindingIndex = -1;
		if (!string.IsNullOrWhiteSpace(normalizedGuid))
		{
			for (int i = 0; i < Bindings.Count; i++)
			{
				if (string.Equals(Bindings[i].SaveAssetGuid, normalizedGuid, StringComparison.OrdinalIgnoreCase))
				{
					bindingIndex = i;
					break;
				}
			}
		}

		bool changed = false;
		if (bindingIndex < 0)
		{
			Bindings.Add(new CitySoundProfileBinding
			{
				SaveAssetGuid = normalizedGuid,
				SaveInfoId = string.Empty,
				SetId = normalizedSet,
				SaveSessionGuid = normalizedSessionGuid,
				LastSeenDisplayName = (lastSeenDisplayName ?? string.Empty).Trim(),
				LastSeenMapName = (lastSeenMapName ?? string.Empty).Trim(),
				LastSeenUtcTicks = DateTime.UtcNow.Ticks
			});
			return true;
		}

		CitySoundProfileBinding binding = Bindings[bindingIndex];
		if (!string.Equals(binding.SaveAssetGuid, normalizedGuid, StringComparison.OrdinalIgnoreCase))
		{
			binding.SaveAssetGuid = normalizedGuid;
			changed = true;
		}

		if (!string.Equals(binding.SetId, normalizedSet, StringComparison.OrdinalIgnoreCase))
		{
			binding.SetId = normalizedSet;
			changed = true;
		}

		if (!string.Equals(binding.SaveSessionGuid, normalizedSessionGuid, StringComparison.OrdinalIgnoreCase))
		{
			binding.SaveSessionGuid = normalizedSessionGuid;
			changed = true;
		}

		string normalizedDisplayName = (lastSeenDisplayName ?? string.Empty).Trim();
		if (!string.Equals(binding.LastSeenDisplayName, normalizedDisplayName, StringComparison.Ordinal))
		{
			binding.LastSeenDisplayName = normalizedDisplayName;
			changed = true;
		}

		string normalizedMapName = (lastSeenMapName ?? string.Empty).Trim();
		if (!string.Equals(binding.LastSeenMapName, normalizedMapName, StringComparison.Ordinal))
		{
			binding.LastSeenMapName = normalizedMapName;
			changed = true;
		}

		if (changed)
		{
			binding.LastSeenUtcTicks = DateTime.UtcNow.Ticks;
		}

		return changed;
	}

	internal bool RemoveBindingForCity(string saveAssetGuid)
	{
		// Remove all rows that match the GUID key to recover from accidental duplicates.
		string normalizedGuid = NormalizeGuidKey(saveAssetGuid);
		if (string.IsNullOrWhiteSpace(normalizedGuid))
		{
			return false;
		}

		bool removed = false;
		for (int i = Bindings.Count - 1; i >= 0; i--)
		{
			CitySoundProfileBinding binding = Bindings[i];
			if (!string.Equals(binding.SaveAssetGuid, normalizedGuid, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			Bindings.RemoveAt(i);
			removed = true;
		}

		return removed;
	}

	internal string CreateUniqueSetId(string desiredName)
	{
		// Keep IDs human-readable first, then fall back to GUID suffix if needed.
		string baseId = NormalizeSetId(desiredName);
		if (string.IsNullOrWhiteSpace(baseId) || string.Equals(baseId, DefaultSetId, StringComparison.OrdinalIgnoreCase))
		{
			baseId = "set";
		}

		if (!Sets.ContainsKey(baseId))
		{
			return baseId;
		}

		for (int i = 2; i < 1000; i++)
		{
			string next = $"{baseId}-{i}";
			if (!Sets.ContainsKey(next))
			{
				return next;
			}
		}

		return $"{baseId}-{Guid.NewGuid():N}";
	}

	// Normalize user-facing set names into safe, stable identifier keys.
	internal static string NormalizeSetId(string? rawSetId)
	{
		string raw = (rawSetId ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(raw) ||
			string.Equals(raw, DefaultSetId, StringComparison.OrdinalIgnoreCase) ||
			string.Equals(raw, DefaultSetDisplayName, StringComparison.OrdinalIgnoreCase))
		{
			return DefaultSetId;
		}

		StringBuilder builder = new StringBuilder(raw.Length);
		bool prevDash = false;
		for (int i = 0; i < raw.Length; i++)
		{
			char c = raw[i];
			if (char.IsLetterOrDigit(c))
			{
				builder.Append(char.ToLowerInvariant(c));
				prevDash = false;
				continue;
			}

			if (c == '_' || c == '-' || char.IsWhiteSpace(c))
			{
				if (!prevDash)
				{
					builder.Append('-');
					prevDash = true;
				}
			}
		}

		string normalized = builder.ToString().Trim('-');
		return string.IsNullOrWhiteSpace(normalized) ? DefaultSetId : normalized;
	}

	// Ensure display names never serialize as blank values.
	internal static string NormalizeDisplayName(string? rawDisplayName, string fallback)
	{
		string normalized = (rawDisplayName ?? string.Empty).Trim();
		if (!string.IsNullOrWhiteSpace(normalized))
		{
			return normalized;
		}

		return string.IsNullOrWhiteSpace(fallback) ? DefaultSetDisplayName : fallback.Trim();
	}

	// Normalize and validate save GUID keys used for city binding lookups.
	internal static string NormalizeGuidKey(string? rawGuid)
	{
		string normalized = (rawGuid ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(normalized))
		{
			return string.Empty;
		}

		if (!Hash128.TryParse(normalized, out Hash128 parsed) || !parsed.isValid)
		{
			return string.Empty;
		}

		return parsed.ToString();
	}

	// Normalize and validate persisted session GUID values used for safe binding migration.
	internal static string NormalizeSessionGuid(string? rawSessionGuid)
	{
		string normalized = (rawSessionGuid ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(normalized))
		{
			return string.Empty;
		}

		if (!Guid.TryParse(normalized, out Guid parsed) || parsed == Guid.Empty)
		{
			return string.Empty;
		}

		return parsed.ToString("D");
	}

	// Resolve the registry JSON path under the mod settings directory.
	internal static string GetRegistryPath(string settingsDirectory)
	{
		return Path.Combine(settingsDirectory, SettingsFileName);
	}

	// Resolve one set directory path, using the settings root for the Default set.
	internal static string GetSetDirectoryPath(string settingsDirectory, string setId, bool ensureExists)
	{
		string normalizedSet = NormalizeSetId(setId);
		if (string.Equals(normalizedSet, DefaultSetId, StringComparison.OrdinalIgnoreCase))
		{
			if (ensureExists)
			{
				Directory.CreateDirectory(settingsDirectory);
			}

			return settingsDirectory;
		}

		string profilesRoot = Path.Combine(settingsDirectory, ProfilesDirectoryName);
		string setDirectory = Path.Combine(profilesRoot, normalizedSet);
		if (ensureExists)
		{
			Directory.CreateDirectory(setDirectory);
		}

		return setDirectory;
	}

	// Resolve one settings file path for a specific set.
	internal static string GetSetSettingsPath(string settingsDirectory, string setId, string fileName, bool ensureDirectoryExists)
	{
		string setDirectory = GetSetDirectoryPath(settingsDirectory, setId, ensureDirectoryExists);
		string safeFileName = string.IsNullOrWhiteSpace(fileName)
			? SirenReplacementConfig.SettingsFileName
			: Path.GetFileName(fileName.Trim());
		return Path.Combine(setDirectory, safeFileName);
	}

	// Resolve a set ID to an existing entry with fallback, then Default.
	private static string NormalizeExistingSetId(
		string setId,
		IReadOnlyDictionary<string, CitySoundProfileSet> availableSets,
		string? fallback = null)
	{
		string normalized = NormalizeSetId(setId);
		if (availableSets.ContainsKey(normalized))
		{
			return normalized;
		}

		string normalizedFallback = NormalizeSetId(fallback);
		if (availableSets.ContainsKey(normalizedFallback))
		{
			return normalizedFallback;
		}

		return DefaultSetId;
	}

	private static List<CitySoundProfileBinding> NormalizeBindings(
		List<CitySoundProfileBinding>? source,
		IReadOnlyDictionary<string, CitySoundProfileSet> availableSets)
	{
		// Canonicalize binding rows and collapse duplicates by GUID.
		List<CitySoundProfileBinding> normalized = new List<CitySoundProfileBinding>();
		if (source == null || source.Count == 0)
		{
			return normalized;
		}

		HashSet<string> seenGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		for (int i = 0; i < source.Count; i++)
		{
			CitySoundProfileBinding current = source[i] ?? new CitySoundProfileBinding();
			string guid = NormalizeGuidKey(current.SaveAssetGuid);
			if (string.IsNullOrWhiteSpace(guid))
			{
				continue;
			}

			if (!string.IsNullOrWhiteSpace(guid) && !seenGuids.Add(guid))
			{
				continue;
			}

			string setId = NormalizeSetId(current.SetId);
			if (!availableSets.ContainsKey(setId))
			{
				setId = DefaultSetId;
			}

			normalized.Add(new CitySoundProfileBinding
			{
				SaveAssetGuid = guid,
				SaveInfoId = string.Empty,
				SetId = setId,
				SaveSessionGuid = NormalizeSessionGuid(current.SaveSessionGuid),
				LastSeenDisplayName = (current.LastSeenDisplayName ?? string.Empty).Trim(),
				LastSeenMapName = (current.LastSeenMapName ?? string.Empty).Trim(),
				LastSeenUtcTicks = current.LastSeenUtcTicks
			});
		}

		return normalized;
	}
}

[DataContract]
// Metadata for one configurable sound set.
internal sealed class CitySoundProfileSet
{
	[DataMember(Order = 1)]
	public string SetId { get; set; } = CitySoundProfileRegistry.DefaultSetId;

	[DataMember(Order = 2)]
	public string DisplayName { get; set; } = CitySoundProfileRegistry.DefaultSetDisplayName;

	[DataMember(Order = 3)]
	public long CreatedUtcTicks { get; set; }

	[DataMember(Order = 4)]
	public long LastUsedUtcTicks { get; set; }
}

[DataContract]
// City-to-set binding keyed by save asset GUID.
internal sealed class CitySoundProfileBinding
{
	[DataMember(Order = 1)]
	public string SaveAssetGuid { get; set; } = string.Empty;

	[DataMember(Order = 2)]
	// Legacy field retained for backward compatibility; runtime matching is GUID-only.
	public string SaveInfoId { get; set; } = string.Empty;

	[DataMember(Order = 3)]
	public string SetId { get; set; } = CitySoundProfileRegistry.DefaultSetId;

	[DataMember(Order = 4)]
	public string SaveSessionGuid { get; set; } = string.Empty;

	[DataMember(Order = 5)]
	public string LastSeenDisplayName { get; set; } = string.Empty;

	[DataMember(Order = 6)]
	public string LastSeenMapName { get; set; } = string.Empty;

	[DataMember(Order = 7)]
	public long LastSeenUtcTicks { get; set; }
}
