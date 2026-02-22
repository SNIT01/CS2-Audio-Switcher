using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using Colossal.Logging;

namespace SirenChanger;

// Emergency vehicle groups supported by the replacement UI.
public enum EmergencySirenVehicleType
{
	Police,
	Fire,
	Ambulance
}

// Region split used by target prefab naming convention.
public enum SirenRegion
{
	NorthAmerica,
	Europe
}

// Behavior used when a selected custom siren cannot be loaded.
public enum SirenFallbackBehavior
{
	Default = 0,
	Mute = 1,
	AlternateCustomSiren = 2
}

[DataContract]
// Persistent settings model for siren replacement behavior.
public sealed class SirenReplacementConfig
{
	public const string SettingsFileName = "SirenChangerSettings.json";

	public const string DetectedSirensFileName = "DetectedSirens.json";

	public const string DefaultSelectionToken = "__default__";

	public const string DefaultCustomSirensFolderName = "Custom Sirens";

	[DataMember(Order = 1)]
	public bool Enabled { get; set; } = true;

	[DataMember(Order = 2)]
	public bool DumpDetectedSirens { get; set; }

	[DataMember(Order = 3)]
	public string CustomSirensFolderName { get; set; } = DefaultCustomSirensFolderName;

	[DataMember(Order = 4)]
	public string PoliceSirenSelection { get; set; } = DefaultSelectionToken;

	[DataMember(Order = 5)]
	public string FireSirenSelection { get; set; } = DefaultSelectionToken;

	[DataMember(Order = 6)]
	public string AmbulanceSirenSelection { get; set; } = DefaultSelectionToken;

	[DataMember(Order = 7)]
	public string EditProfileSelection { get; set; } = string.Empty;

	[DataMember(Order = 8)]
	public Dictionary<string, SirenSfxProfile> CustomSirenProfiles { get; set; } = new Dictionary<string, SirenSfxProfile>();

	[DataMember(Order = 9)]
	public bool CustomProfileTemplateInitialized { get; set; }

	[DataMember(Order = 10)]
	public bool DumpAllSirenCandidates { get; set; }

	[DataMember(Order = 11)]
	public List<string> PendingTemplateProfileKeys { get; set; } = new List<string>();

	[DataMember(Order = 12)]
	public string PoliceSirenSelectionNA { get; set; } = DefaultSelectionToken;

	[DataMember(Order = 13)]
	public string PoliceSirenSelectionEU { get; set; } = DefaultSelectionToken;

	[DataMember(Order = 14)]
	public string FireSirenSelectionNA { get; set; } = DefaultSelectionToken;

	[DataMember(Order = 15)]
	public string FireSirenSelectionEU { get; set; } = DefaultSelectionToken;

	[DataMember(Order = 16)]
	public string AmbulanceSirenSelectionNA { get; set; } = DefaultSelectionToken;

	[DataMember(Order = 17)]
	public string AmbulanceSirenSelectionEU { get; set; } = DefaultSelectionToken;

	[DataMember(Order = 18)]
	public SirenFallbackBehavior MissingSirenFallbackBehavior { get; set; } = SirenFallbackBehavior.Default;

	[DataMember(Order = 19)]
	public string AlternateFallbackSelection { get; set; } = DefaultSelectionToken;

	[DataMember(Order = 20)]
	public string CopyFromProfileSelection { get; set; } = string.Empty;

	[DataMember(Order = 21)]
	public long LastCatalogScanUtcTicks { get; set; }

	[DataMember(Order = 22)]
	public int LastCatalogScanFileCount { get; set; }

	[DataMember(Order = 23)]
	public int LastCatalogScanAddedCount { get; set; }

	[DataMember(Order = 24)]
	public int LastCatalogScanRemovedCount { get; set; }

	[DataMember(Order = 25)]
	public List<string> LastCatalogScanChangedFiles { get; set; } = new List<string>();

	[DataMember(Order = 26)]
	public long LastValidationUtcTicks { get; set; }

	[DataMember(Order = 27)]
	public string LastValidationReport { get; set; } = string.Empty;

	[DataMember(Order = 28)]
	public Dictionary<string, string> VehiclePrefabSelections { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

	[DataMember(Order = 29)]
	public string VehiclePrefabSelectionTarget { get; set; } = string.Empty;

	[DataMember(Order = 30)]
	public List<string> KnownEmergencyVehiclePrefabs { get; set; } = new List<string>();

	[DataMember(Order = 102)]
	public List<string> SirenTokens { get; set; } = new List<string> { "siren", "alarm", "emergency" };

	// Create normalized default configuration.
	public static SirenReplacementConfig CreateDefault()
	{
		return new SirenReplacementConfig().Normalize();
	}

	// Load config from disk or create defaults if missing/invalid.
	public static SirenReplacementConfig LoadOrCreate(string settingsDirectory, ILog log)
	{
		string settingsPath = SirenPathUtils.GetSettingsFilePath(settingsDirectory);
		if (!File.Exists(settingsPath))
		{
			SirenReplacementConfig created = CreateDefault();
			Save(settingsPath, created, log);
			log.Info($"Created default settings at: {settingsPath}");
			return created;
		}

		try
		{
			string json = File.ReadAllText(settingsPath);
			SirenReplacementConfig? config;
			string error;
			if (!JsonDataSerializer.TryDeserialize(json, out config, out error) || config == null)
			{
				log.Warn($"Settings file could not be parsed, using defaults: {settingsPath}. {error}");
				return CreateDefault();
			}

			return config.Normalize();
		}
		catch (Exception ex)
		{
			log.Warn($"Failed to read settings at {settingsPath}. Using defaults. {ex.Message}");
			return CreateDefault();
		}
	}

	// Save config JSON to disk.
	public static void Save(string settingsPath, SirenReplacementConfig settings, ILog log)
	{
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(settingsPath) ?? string.Empty);
			string json = JsonDataSerializer.Serialize(settings);
			File.WriteAllText(settingsPath, json);
		}
		catch (Exception ex)
		{
			log.Warn($"Failed to write settings file {settingsPath}: {ex.Message}");
		}
	}

	// Normalize all config fields into safe/canonical forms.
	internal SirenReplacementConfig Normalize()
	{
		CustomSirenProfiles = NormalizeProfiles(CustomSirenProfiles);
		SirenTokens = NormalizeTokenList(SirenTokens);
		PendingTemplateProfileKeys = NormalizeKeyList(PendingTemplateProfileKeys);
		LastCatalogScanChangedFiles = NormalizeScanChangeList(LastCatalogScanChangedFiles);
		CustomSirensFolderName = SirenPathUtils.SanitizeCustomSirensFolderName(CustomSirensFolderName);

		PoliceSirenSelection = NormalizeSelection(PoliceSirenSelection);
		FireSirenSelection = NormalizeSelection(FireSirenSelection);
		AmbulanceSirenSelection = NormalizeSelection(AmbulanceSirenSelection);

		SetSelection(EmergencySirenVehicleType.Police, SirenRegion.NorthAmerica, string.IsNullOrWhiteSpace(PoliceSirenSelectionNA) ? PoliceSirenSelection : PoliceSirenSelectionNA);
		SetSelection(EmergencySirenVehicleType.Police, SirenRegion.Europe, string.IsNullOrWhiteSpace(PoliceSirenSelectionEU) ? PoliceSirenSelection : PoliceSirenSelectionEU);
		SetSelection(EmergencySirenVehicleType.Fire, SirenRegion.NorthAmerica, string.IsNullOrWhiteSpace(FireSirenSelectionNA) ? FireSirenSelection : FireSirenSelectionNA);
		SetSelection(EmergencySirenVehicleType.Fire, SirenRegion.Europe, string.IsNullOrWhiteSpace(FireSirenSelectionEU) ? FireSirenSelection : FireSirenSelectionEU);
		SetSelection(EmergencySirenVehicleType.Ambulance, SirenRegion.NorthAmerica, string.IsNullOrWhiteSpace(AmbulanceSirenSelectionNA) ? AmbulanceSirenSelection : AmbulanceSirenSelectionNA);
		SetSelection(EmergencySirenVehicleType.Ambulance, SirenRegion.Europe, string.IsNullOrWhiteSpace(AmbulanceSirenSelectionEU) ? AmbulanceSirenSelection : AmbulanceSirenSelectionEU);

		PoliceSirenSelection = PoliceSirenSelectionNA;
		FireSirenSelection = FireSirenSelectionNA;
		AmbulanceSirenSelection = AmbulanceSirenSelectionNA;

		if (!Enum.IsDefined(typeof(SirenFallbackBehavior), MissingSirenFallbackBehavior))
		{
			MissingSirenFallbackBehavior = SirenFallbackBehavior.Default;
		}

		AlternateFallbackSelection = NormalizeSelection(AlternateFallbackSelection);
		EditProfileSelection = SirenPathUtils.NormalizeProfileKey(EditProfileSelection ?? string.Empty);
		CopyFromProfileSelection = SirenPathUtils.NormalizeProfileKey(CopyFromProfileSelection ?? string.Empty);
		VehiclePrefabSelections = NormalizeVehicleSelectionDictionary(VehiclePrefabSelections, CustomSirenProfiles.Keys);
		VehiclePrefabSelectionTarget = NormalizeVehiclePrefabKey(VehiclePrefabSelectionTarget);
		KnownEmergencyVehiclePrefabs = NormalizeVehiclePrefabList(KnownEmergencyVehiclePrefabs);
		LastValidationReport = LastValidationReport ?? string.Empty;
		return this;
	}

	// Read selection by vehicle and region.
	internal string GetSelection(EmergencySirenVehicleType vehicle, SirenRegion region)
	{
		switch (vehicle)
		{
			case EmergencySirenVehicleType.Police:
				return region == SirenRegion.Europe ? PoliceSirenSelectionEU : PoliceSirenSelectionNA;
			case EmergencySirenVehicleType.Fire:
				return region == SirenRegion.Europe ? FireSirenSelectionEU : FireSirenSelectionNA;
			case EmergencySirenVehicleType.Ambulance:
				return region == SirenRegion.Europe ? AmbulanceSirenSelectionEU : AmbulanceSirenSelectionNA;
			default:
				return DefaultSelectionToken;
		}
	}

	// Set selection for a specific vehicle+region.
	internal void SetSelection(EmergencySirenVehicleType vehicle, SirenRegion region, string selection)
	{
		string normalized = NormalizeSelection(selection);
		switch (vehicle)
		{
			case EmergencySirenVehicleType.Police:
				if (region == SirenRegion.Europe)
				{
					PoliceSirenSelectionEU = normalized;
				}
				else
				{
					PoliceSirenSelectionNA = normalized;
					PoliceSirenSelection = normalized;
				}
				break;
			case EmergencySirenVehicleType.Fire:
				if (region == SirenRegion.Europe)
				{
					FireSirenSelectionEU = normalized;
				}
				else
				{
					FireSirenSelectionNA = normalized;
					FireSirenSelection = normalized;
				}
				break;
			case EmergencySirenVehicleType.Ambulance:
				if (region == SirenRegion.Europe)
				{
					AmbulanceSirenSelectionEU = normalized;
				}
				else
				{
					AmbulanceSirenSelectionNA = normalized;
					AmbulanceSirenSelection = normalized;
				}
				break;
		}
	}

	// Read a per-vehicle-prefab override selection, falling back to Default.
	internal string GetVehiclePrefabSelection(string vehiclePrefabName)
	{
		string key = NormalizeVehiclePrefabKey(vehiclePrefabName);
		if (string.IsNullOrWhiteSpace(key))
		{
			return DefaultSelectionToken;
		}

		return VehiclePrefabSelections.TryGetValue(key, out string selection)
			? NormalizeSelection(selection)
			: DefaultSelectionToken;
	}

	// Set or clear a per-vehicle-prefab override selection.
	internal bool SetVehiclePrefabSelection(string vehiclePrefabName, string selection)
	{
		string key = NormalizeVehiclePrefabKey(vehiclePrefabName);
		if (string.IsNullOrWhiteSpace(key))
		{
			return false;
		}

		string normalizedSelection = NormalizeSelection(selection);
		if (IsDefaultSelection(normalizedSelection))
		{
			return VehiclePrefabSelections.Remove(key);
		}

		if (VehiclePrefabSelections.TryGetValue(key, out string existing) &&
			string.Equals(existing, normalizedSelection, StringComparison.Ordinal))
		{
			return false;
		}

		VehiclePrefabSelections[key] = normalizedSelection;
		return true;
	}

	// Update the currently selected vehicle prefab key used by options editing UI.
	internal void SetVehiclePrefabSelectionTarget(string vehiclePrefabName)
	{
		VehiclePrefabSelectionTarget = NormalizeVehiclePrefabKey(vehiclePrefabName);
	}

	// Align per-vehicle override keys against discovered vehicle prefabs and keep editor target valid.
	internal bool SynchronizeVehiclePrefabSelections(ICollection<string> discoveredVehiclePrefabs)
	{
		Dictionary<string, string> discoveredCanonical = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		if (discoveredVehiclePrefabs != null)
		{
			foreach (string raw in discoveredVehiclePrefabs)
			{
				string normalized = NormalizeVehiclePrefabKey(raw);
				if (string.IsNullOrWhiteSpace(normalized))
				{
					continue;
				}

				discoveredCanonical[normalized] = raw.Trim();
			}
		}

		bool changed = false;
		Dictionary<string, string> filtered = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		Dictionary<string, string> normalizedSelections = NormalizeVehicleSelectionDictionary(VehiclePrefabSelections, CustomSirenProfiles.Keys);
		foreach (KeyValuePair<string, string> pair in normalizedSelections)
		{
			if (!discoveredCanonical.TryGetValue(pair.Key, out string canonicalVehicleKey))
			{
				changed = true;
				continue;
			}

			filtered[canonicalVehicleKey] = pair.Value;
		}

		if (!DictionariesEqualIgnoreCase(VehiclePrefabSelections, filtered))
		{
			VehiclePrefabSelections = filtered;
			changed = true;
		}

		List<string> sortedDiscovered = discoveredCanonical.Values
			.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
			.ToList();
		if (!ListEqualsIgnoreCase(KnownEmergencyVehiclePrefabs, sortedDiscovered))
		{
			KnownEmergencyVehiclePrefabs = sortedDiscovered;
			changed = true;
		}

		string normalizedTarget = NormalizeVehiclePrefabKey(VehiclePrefabSelectionTarget);
		string nextTarget = string.Empty;
		if (sortedDiscovered.Count > 0)
		{
			nextTarget = sortedDiscovered[0];
			for (int i = 0; i < sortedDiscovered.Count; i++)
			{
				if (string.Equals(sortedDiscovered[i], normalizedTarget, StringComparison.OrdinalIgnoreCase))
				{
					nextTarget = sortedDiscovered[i];
					break;
				}
			}
		}

		if (!string.Equals(VehiclePrefabSelectionTarget, nextTarget, StringComparison.Ordinal))
		{
			VehiclePrefabSelectionTarget = nextTarget;
			changed = true;
		}

		return changed;
	}

	// Ensure all saved selections point to currently available profile keys.
	internal bool EnsureSelectionsValid(ICollection<string> availableKeys)
	{
		bool changed = false;

		changed |= EnsureVehicleSelectionValid(EmergencySirenVehicleType.Police, SirenRegion.NorthAmerica, availableKeys);
		changed |= EnsureVehicleSelectionValid(EmergencySirenVehicleType.Police, SirenRegion.Europe, availableKeys);
		changed |= EnsureVehicleSelectionValid(EmergencySirenVehicleType.Fire, SirenRegion.NorthAmerica, availableKeys);
		changed |= EnsureVehicleSelectionValid(EmergencySirenVehicleType.Fire, SirenRegion.Europe, availableKeys);
		changed |= EnsureVehicleSelectionValid(EmergencySirenVehicleType.Ambulance, SirenRegion.NorthAmerica, availableKeys);
		changed |= EnsureVehicleSelectionValid(EmergencySirenVehicleType.Ambulance, SirenRegion.Europe, availableKeys);

		string normalizedFallbackSelection = NormalizeSelection(AlternateFallbackSelection);
		if (!IsDefaultSelection(normalizedFallbackSelection) && !availableKeys.Contains(normalizedFallbackSelection))
		{
			normalizedFallbackSelection = DefaultSelectionToken;
		}

		if (!string.Equals(AlternateFallbackSelection, normalizedFallbackSelection, StringComparison.Ordinal))
		{
			AlternateFallbackSelection = normalizedFallbackSelection;
			changed = true;
		}

		if (string.IsNullOrWhiteSpace(EditProfileSelection) || !availableKeys.Contains(EditProfileSelection))
		{
			string next = availableKeys.Count > 0 ? availableKeys.OrderBy(static k => k, StringComparer.OrdinalIgnoreCase).First() : string.Empty;
			if (!string.Equals(EditProfileSelection, next, StringComparison.Ordinal))
			{
				EditProfileSelection = next;
				changed = true;
			}
		}

		if (string.IsNullOrWhiteSpace(CopyFromProfileSelection) || !availableKeys.Contains(CopyFromProfileSelection))
		{
			string fallback = !string.IsNullOrWhiteSpace(EditProfileSelection)
				? EditProfileSelection
				: availableKeys.OrderBy(static k => k, StringComparer.OrdinalIgnoreCase).FirstOrDefault() ?? string.Empty;
			if (!string.Equals(CopyFromProfileSelection, fallback, StringComparison.Ordinal))
			{
				CopyFromProfileSelection = fallback;
				changed = true;
			}
		}

		Dictionary<string, string> normalizedVehicleSelections = NormalizeVehicleSelectionDictionary(VehiclePrefabSelections, availableKeys);
		if (!DictionariesEqualIgnoreCase(VehiclePrefabSelections, normalizedVehicleSelections))
		{
			VehiclePrefabSelections = normalizedVehicleSelections;
			changed = true;
		}

		string normalizedVehicleTarget = NormalizeVehiclePrefabKey(VehiclePrefabSelectionTarget);
		if (!string.Equals(VehiclePrefabSelectionTarget, normalizedVehicleTarget, StringComparison.Ordinal))
		{
			VehiclePrefabSelectionTarget = normalizedVehicleTarget;
			changed = true;
		}

		return changed;
	}

	// Fetch a profile by normalized key.
	internal bool TryGetProfile(string key, out SirenSfxProfile profile)
	{
		profile = null!;
		if (string.IsNullOrWhiteSpace(key))
		{
			return false;
		}

		string normalized = SirenPathUtils.NormalizeProfileKey(key);
		return CustomSirenProfiles.TryGetValue(normalized, out profile!);
	}

	// Validate one vehicle+region selection against available profile keys.
	private bool EnsureVehicleSelectionValid(EmergencySirenVehicleType vehicle, SirenRegion region, ICollection<string> availableKeys)
	{
		string selection = NormalizeSelection(GetSelection(vehicle, region));
		SetSelection(vehicle, region, selection);
		if (IsDefaultSelection(selection))
		{
			return false;
		}

		if (!availableKeys.Contains(selection))
		{
			SetSelection(vehicle, region, DefaultSelectionToken);
			return true;
		}

		return false;
	}

	// Convert raw selection input into default token or normalized key.
	private static string NormalizeSelection(string selection)
	{
		if (string.IsNullOrWhiteSpace(selection))
		{
			return DefaultSelectionToken;
		}

		if (string.Equals(selection.Trim(), DefaultSelectionToken, StringComparison.OrdinalIgnoreCase))
		{
			return DefaultSelectionToken;
		}

		string normalizedKey = SirenPathUtils.NormalizeProfileKey(selection);
		return string.IsNullOrWhiteSpace(normalizedKey) ? DefaultSelectionToken : normalizedKey;
	}

	// Normalize vehicle prefab keys used for per-prefab assignment dictionary keys.
	internal static string NormalizeVehiclePrefabKey(string vehiclePrefabName)
	{
		return string.IsNullOrWhiteSpace(vehiclePrefabName) ? string.Empty : vehiclePrefabName.Trim();
	}

	// True when selection is effectively default behavior.
	internal static bool IsDefaultSelection(string? selection)
	{
		if (string.IsNullOrWhiteSpace(selection))
		{
			return true;
		}

		return string.Equals(selection!.Trim(), DefaultSelectionToken, StringComparison.OrdinalIgnoreCase);
	}

	// Normalize discovered vehicle prefab list (trim + dedupe + sort).
	private static List<string> NormalizeVehiclePrefabList(List<string>? source)
	{
		List<string> result = new List<string>();
		if (source == null)
		{
			return result;
		}

		HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		for (int i = 0; i < source.Count; i++)
		{
			string key = NormalizeVehiclePrefabKey(source[i]);
			if (string.IsNullOrWhiteSpace(key) || !seen.Add(key))
			{
				continue;
			}

			result.Add(key);
		}

		result.Sort(StringComparer.OrdinalIgnoreCase);
		return result;
	}

	// Normalize per-vehicle override dictionary and drop invalid/default references.
	private static Dictionary<string, string> NormalizeVehicleSelectionDictionary(
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
			string vehicleKey = NormalizeVehiclePrefabKey(entry.Key);
			if (string.IsNullOrWhiteSpace(vehicleKey))
			{
				continue;
			}

			string selection = NormalizeSelection(entry.Value);
			if (IsDefaultSelection(selection) || !available.Contains(selection))
			{
				continue;
			}

			result[vehicleKey] = selection;
		}

		return result;
	}

	// Normalize profile dictionary keys and clamp profile values.
	private static Dictionary<string, SirenSfxProfile> NormalizeProfiles(Dictionary<string, SirenSfxProfile>? source)
	{
		Dictionary<string, SirenSfxProfile> result = new Dictionary<string, SirenSfxProfile>(StringComparer.OrdinalIgnoreCase);
		if (source == null)
		{
			return result;
		}

		foreach (KeyValuePair<string, SirenSfxProfile> entry in source)
		{
			string key = SirenPathUtils.NormalizeProfileKey(entry.Key);
			if (string.IsNullOrWhiteSpace(key))
			{
				continue;
			}

			SirenSfxProfile profile = (entry.Value ?? SirenSfxProfile.CreateFallback()).ClampCopy();
			result[key] = profile;
		}

		return result;
	}

	// Normalize siren token list and keep at least one fallback token.
	private static List<string> NormalizeTokenList(List<string>? tokens)
	{
		List<string> result = new List<string>();
		if (tokens == null)
		{
			return result;
		}

		for (int i = 0; i < tokens.Count; i++)
		{
			string? token = tokens[i];
			if (string.IsNullOrWhiteSpace(token))
			{
				continue;
			}

			result.Add(token.Trim());
		}

		if (result.Count == 0)
		{
			result.Add("siren");
		}

		return result;
	}

	// Normalize profile key list (dedupe + sort).
	private static List<string> NormalizeKeyList(List<string>? source)
	{
		List<string> result = new List<string>();
		if (source == null)
		{
			return result;
		}

		HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		for (int i = 0; i < source.Count; i++)
		{
			string normalized = SirenPathUtils.NormalizeProfileKey(source[i] ?? string.Empty);
			if (string.IsNullOrWhiteSpace(normalized) || !seen.Add(normalized))
			{
				continue;
			}

			result.Add(normalized);
		}

		result.Sort(StringComparer.OrdinalIgnoreCase);
		return result;
	}

	// Normalize scan-change text list (dedupe + trimmed values).
	private static List<string> NormalizeScanChangeList(List<string>? source)
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

	// Case-insensitive dictionary equality used for config-change detection.
	private static bool DictionariesEqualIgnoreCase(
		Dictionary<string, string> left,
		Dictionary<string, string> right)
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

	// Case-insensitive list equality helper for discovered-vehicle persistence.
	private static bool ListEqualsIgnoreCase(List<string> left, List<string> right)
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

// File/path helpers with strict normalization to avoid directory traversal issues.
internal static class SirenPathUtils
{
	private const string ModName = "SirenChanger";

	private static readonly char[] s_InvalidFileNameChars = Path.GetInvalidFileNameChars();

	private static readonly HashSet<string> s_SupportedCustomSirenExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		".wav",
		".ogg"
	};

	// Resolve mod settings folder path from game userdata or local app data.
	public static string GetSettingsDirectory(bool ensureExists)
	{
		string? userData = Environment.GetEnvironmentVariable("CSII_USERDATAPATH");
		string baseDirectory = !string.IsNullOrWhiteSpace(userData)
			? Path.Combine(userData, "Mods", ModName)
			: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Colossal Order", "Cities Skylines II", "Mods", ModName);

		if (ensureExists)
		{
			Directory.CreateDirectory(baseDirectory);
		}

		return baseDirectory;
	}

	// Resolve settings file path from settings directory.
	public static string GetSettingsFilePath(string settingsDirectory)
	{
		return GetSettingsFilePath(settingsDirectory, SirenReplacementConfig.SettingsFileName);
	}

	// Resolve a specific settings file path from settings directory.
	public static string GetSettingsFilePath(string settingsDirectory, string fileName)
	{
		string safeFileName = string.IsNullOrWhiteSpace(fileName)
			? SirenReplacementConfig.SettingsFileName
			: Path.GetFileName(fileName.Trim());
		return Path.Combine(settingsDirectory, safeFileName);
	}

	// Resolve custom siren folder path and optionally create it.
	public static string GetCustomSirensDirectory(string settingsDirectory, string folderName, bool ensureExists)
	{
		string safeFolder = SanitizeCustomSirensFolderName(folderName);
		string directory = Path.Combine(settingsDirectory, safeFolder);
		if (ensureExists)
		{
			Directory.CreateDirectory(directory);
		}

		return directory;
	}

	// Restrict folder names to one safe segment and return the specified fallback when invalid.
	public static string SanitizeSingleFolderName(string? folderName, string fallbackFolderName)
	{
		string fallback = string.IsNullOrWhiteSpace(fallbackFolderName)
			? SirenReplacementConfig.DefaultCustomSirensFolderName
			: fallbackFolderName.Trim();
		string raw = folderName ?? string.Empty;
		if (string.IsNullOrWhiteSpace(raw))
		{
			return fallback;
		}

		string normalized = raw.Trim().Replace('\\', '/').Trim('/');
		if (string.IsNullOrWhiteSpace(normalized) ||
			Path.IsPathRooted(normalized) ||
			normalized.IndexOf('/') >= 0 ||
			normalized == "." ||
			normalized == ".." ||
			normalized.IndexOfAny(s_InvalidFileNameChars) >= 0)
		{
			return fallback;
		}

		return normalized;
	}

	// Restrict custom siren folder name to a single safe segment.
	public static string SanitizeCustomSirensFolderName(string? folderName)
	{
		return SanitizeSingleFolderName(folderName, SirenReplacementConfig.DefaultCustomSirensFolderName);
	}

	// Normalize profile key paths and reject unsafe segments.
	public static string NormalizeProfileKey(string key)
	{
		if (string.IsNullOrWhiteSpace(key))
		{
			return string.Empty;
		}

		string normalized = key.Trim().Replace('\\', '/');
		while (normalized.Contains("//", StringComparison.Ordinal))
		{
			normalized = normalized.Replace("//", "/", StringComparison.Ordinal);
		}

		normalized = normalized.Trim('/');
		if (string.IsNullOrWhiteSpace(normalized))
		{
			return string.Empty;
		}

		string[] segments = normalized.Split('/');
		List<string> safeSegments = new List<string>(segments.Length);
		for (int i = 0; i < segments.Length; i++)
		{
			string segment = segments[i].Trim();
			if (string.IsNullOrWhiteSpace(segment) || segment == ".")
			{
				continue;
			}

			if (segment == ".." || segment.IndexOfAny(s_InvalidFileNameChars) >= 0)
			{
				return string.Empty;
			}

			safeSegments.Add(segment);
		}

		return safeSegments.Count == 0 ? string.Empty : string.Join("/", safeSegments);
	}

	// Enumerate custom siren files and return normalized relative keys.
	public static List<string> EnumerateCustomSirenKeys(string settingsDirectory, string folderName)
	{
		string directory = GetCustomSirensDirectory(settingsDirectory, folderName, ensureExists: true);
		if (!Directory.Exists(directory))
		{
			return new List<string>();
		}

		IEnumerable<string> files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
			.Where(static filePath => IsSupportedCustomSirenExtension(Path.GetExtension(filePath)));
		List<string> keys = new List<string>();
		foreach (string filePath in files)
		{
			string relativePath = Path.GetRelativePath(directory, filePath);
			string key = NormalizeProfileKey(relativePath);
			if (!string.IsNullOrWhiteSpace(key))
			{
				keys.Add(key);
			}
		}

		keys.Sort(StringComparer.OrdinalIgnoreCase);
		return keys;
	}

	// Resolve normalized profile key to absolute file path under custom folder.
	public static bool TryGetCustomSirenFilePath(string settingsDirectory, string folderName, string profileKey, out string resolvedPath)
	{
		resolvedPath = string.Empty;
		if (string.IsNullOrWhiteSpace(profileKey))
		{
			return false;
		}

		string key = NormalizeProfileKey(profileKey);
		if (string.IsNullOrWhiteSpace(key))
		{
			return false;
		}

		string directory = GetCustomSirensDirectory(settingsDirectory, folderName, ensureExists: true);
		string rootPath = EnsureTrailingSeparator(Path.GetFullPath(directory));
		string candidate = Path.GetFullPath(Path.Combine(rootPath, key.Replace('/', Path.DirectorySeparatorChar)));
		if (!candidate.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		if (!IsSupportedCustomSirenExtension(Path.GetExtension(candidate)))
		{
			return false;
		}

		if (File.Exists(candidate))
		{
			resolvedPath = candidate;
			return true;
		}

		return false;
	}

	// Supported custom siren formats.
	public static bool IsSupportedCustomSirenExtension(string? extension)
	{
		if (string.IsNullOrWhiteSpace(extension))
		{
			return false;
		}

		return s_SupportedCustomSirenExtensions.Contains(extension!);
	}

	// Human-readable supported extension list for UI/report text.
	public static string GetSupportedCustomSirenExtensionsLabel()
	{
		return ".wav, .ogg";
	}

	// Ensure root prefix checks are separator-safe.
	private static string EnsureTrailingSeparator(string value)
	{
		if (value.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
		{
			return value;
		}

		return value + Path.DirectorySeparatorChar;
	}
}

// Synchronizes disk catalog with config profiles and selection validity.
internal static class SirenCatalogSync
{
	// Apply catalog changes and return a structured summary.
	public static SirenCatalogSyncResult Synchronize(
		SirenReplacementConfig config,
		string settingsDirectory,
		SirenSfxProfile template,
		ILog log,
		ICollection<string>? externalKeys = null,
		Func<string, SirenSfxProfile?>? externalProfileSeedProvider = null)
	{
		SirenCatalogSyncResult result = new SirenCatalogSyncResult();
		bool changed = false;
		string folderName = config.CustomSirensFolderName;
		List<string> localDiscoveredKeys = SirenPathUtils.EnumerateCustomSirenKeys(settingsDirectory, folderName);
		result.FoundFileCount = localDiscoveredKeys.Count;
		HashSet<string> available = new HashSet<string>(localDiscoveredKeys, StringComparer.OrdinalIgnoreCase);
		HashSet<string> external = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		if (externalKeys != null)
		{
			foreach (string rawExternalKey in externalKeys)
			{
				string normalizedExternal = SirenPathUtils.NormalizeProfileKey(rawExternalKey ?? string.Empty);
				if (string.IsNullOrWhiteSpace(normalizedExternal) || SirenReplacementConfig.IsDefaultSelection(normalizedExternal))
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

		HashSet<string> pendingTemplate = new HashSet<string>(config.PendingTemplateProfileKeys, StringComparer.OrdinalIgnoreCase);
		SirenSfxProfile fallbackTemplate = SirenSfxProfile.CreateFallback();
		bool templateReady = !template.ApproximatelyEquals(fallbackTemplate);
		SirenSfxProfile seedTemplate = templateReady ? template : fallbackTemplate;
		List<string> availableSorted = available.OrderBy(static key => key, StringComparer.OrdinalIgnoreCase).ToList();

		for (int i = 0; i < availableSorted.Count; i++)
		{
			string key = availableSorted[i];
			if (config.CustomSirenProfiles.ContainsKey(key))
			{
				continue;
			}

			bool isExternal = external.Contains(key);
			SirenSfxProfile? externalSeed = externalProfileSeedProvider?.Invoke(key);
			SirenSfxProfile seed = (externalSeed ?? seedTemplate).ClampCopy();
			config.CustomSirenProfiles[key] = seed;
			if (!templateReady && !isExternal)
			{
				pendingTemplate.Add(key);
			}

			changed = true;
			result.AddedKeys.Add(key);
			if (isExternal)
			{
				log.Info($"Registered module siren profile: {key}");
			}
			else
			{
				log.Info($"Registered custom siren profile: {key}");
			}
		}

		List<string> stale = config.CustomSirenProfiles.Keys.Where(key => !available.Contains(key)).ToList();
		for (int i = 0; i < stale.Count; i++)
		{
			string key = stale[i];
			config.CustomSirenProfiles.Remove(key);
			pendingTemplate.Remove(key);
			changed = true;
			result.RemovedKeys.Add(key);
			if (AudioModuleCatalog.IsModuleSelection(key))
			{
				log.Info($"Removed unavailable module siren profile: {key}");
			}
			else
			{
				log.Info($"Removed missing custom siren profile: {key}");
			}
		}

		if (templateReady && pendingTemplate.Count > 0)
		{
			string[] pendingKeys = pendingTemplate.ToArray();
			for (int i = 0; i < pendingKeys.Length; i++)
			{
				string key = pendingKeys[i];
				if (config.CustomSirenProfiles.TryGetValue(key, out SirenSfxProfile existing) &&
					existing != null &&
					existing.ApproximatelyEquals(fallbackTemplate))
				{
					config.CustomSirenProfiles[key] = template.ClampCopy();
					changed = true;
					log.Info($"Updated custom siren profile from PoliceCarSirenNA template: {key}");
				}

				pendingTemplate.Remove(key);
				changed = true;
			}

			config.CustomProfileTemplateInitialized = true;
		}

		if (config.EnsureSelectionsValid(available))
		{
			changed = true;
		}

		List<string> nextPending = pendingTemplate.OrderBy(static k => k, StringComparer.OrdinalIgnoreCase).ToList();
		if (!ListEqualsIgnoreCase(config.PendingTemplateProfileKeys, nextPending))
		{
			config.PendingTemplateProfileKeys = nextPending;
			changed = true;
		}

		if (config.CustomProfileTemplateInitialized != templateReady)
		{
			config.CustomProfileTemplateInitialized = templateReady;
			changed = true;
		}

		result.ConfigChanged = changed;
		return result;
	}

	// Case-insensitive list equality helper for pending-template tracking.
	private static bool ListEqualsIgnoreCase(List<string> left, List<string> right)
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

// Sync operation summary used by options/status update logic.
internal sealed class SirenCatalogSyncResult
{
	public bool ConfigChanged { get; set; }

	public int FoundFileCount { get; set; }

	public List<string> AddedKeys { get; } = new List<string>();

	public List<string> RemovedKeys { get; } = new List<string>();
}


