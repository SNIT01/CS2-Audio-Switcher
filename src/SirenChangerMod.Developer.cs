using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Runtime.Serialization;
using Game.Prefabs.Effects;
using Game.UI.Widgets;
using UnityEngine;

namespace SirenChanger;

// Supported developer-tool audio domains.
internal enum DeveloperAudioDomain
{
	Siren,
	VehicleEngine,
	Ambient
}

// Developer-tab catalog/state for detected runtime sounds and utility actions.
public sealed partial class SirenChangerMod
{
	private const string kDeveloperExportsFolderName = "Exports";

	private const string kDetectedCopySourcePrefix = "__detected_sfx__";

	private const string kDeveloperModuleManifestFileName = "AudioSwitcherModule.json";

	private const string kDeveloperModuleDefaultDisplayName = "Audio Switcher Local Audio Pack";

	private const string kDeveloperModuleDefaultId = "audio.switcher.local.pack";

	private const string kDeveloperModuleDefaultFolderName = "AudioSwitcherLocalModule";

	private static readonly Dictionary<string, DetectedAudioEntry> s_DetectedSirenAudio = new Dictionary<string, DetectedAudioEntry>(StringComparer.OrdinalIgnoreCase);

	private static readonly Dictionary<string, DetectedAudioEntry> s_DetectedEngineAudio = new Dictionary<string, DetectedAudioEntry>(StringComparer.OrdinalIgnoreCase);

	private static readonly Dictionary<string, DetectedAudioEntry> s_DetectedAmbientAudio = new Dictionary<string, DetectedAudioEntry>(StringComparer.OrdinalIgnoreCase);

	private static string s_DeveloperSelectedSirenKey = string.Empty;

	private static string s_DeveloperSelectedEngineKey = string.Empty;

	private static string s_DeveloperSelectedAmbientKey = string.Empty;

	private static string s_DeveloperSirenStatus = "No detected siren sounds are available yet. Load a map/editor session to detect sounds.";

	private static string s_DeveloperEngineStatus = "No detected vehicle engine sounds are available yet. Load a map/editor session to detect sounds.";

	private static string s_DeveloperAmbientStatus = "No detected ambient sounds are available yet. Load a map/editor session to detect sounds.";

	private static string s_DeveloperModuleDisplayName = kDeveloperModuleDefaultDisplayName;

	private static string s_DeveloperModuleId = kDeveloperModuleDefaultId;

	private static string s_DeveloperModuleFolderName = kDeveloperModuleDefaultFolderName;

	private static string s_DeveloperModuleStatus = "Ready to create a module from local custom audio files.";

	private static string s_DeveloperModuleExportDirectory = string.Empty;

	private static string s_DeveloperModuleSelectedLocalSirenKey = string.Empty;

	private static string s_DeveloperModuleSelectedLocalEngineKey = string.Empty;

	private static string s_DeveloperModuleSelectedLocalAmbientKey = string.Empty;

	private static readonly HashSet<string> s_DeveloperModuleIncludedSirens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private static readonly HashSet<string> s_DeveloperModuleIncludedEngines = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private static readonly HashSet<string> s_DeveloperModuleIncludedAmbient = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private static bool s_DeveloperModuleIncludeInitialized;

	// Clears every detected-audio domain. Called on unload to avoid stale object references.
	internal static void ClearAllDetectedDeveloperAudio()
	{
		bool changed = false;
		changed |= ResetDetectedAudioDomainInternal(DeveloperAudioDomain.Siren);
		changed |= ResetDetectedAudioDomainInternal(DeveloperAudioDomain.VehicleEngine);
		changed |= ResetDetectedAudioDomainInternal(DeveloperAudioDomain.Ambient);
		if (changed)
		{
			OptionsVersion++;
		}
	}

	// Clears one detected-audio domain and resets its selection.
	internal static void ResetDetectedAudioDomain(DeveloperAudioDomain domain)
	{
		if (ResetDetectedAudioDomainInternal(domain))
		{
			OptionsVersion++;
		}
	}

	// Start a fresh detection pass for one domain.
	internal static void BeginDetectedAudioCollection(DeveloperAudioDomain domain)
	{
		GetDetectedAudioMap(domain).Clear();
	}

	// Register one detected SFX source into the active domain catalog.
	internal static void RegisterDetectedAudioEntry(DeveloperAudioDomain domain, string prefabName, SFX sfx)
	{
		if (sfx == null || sfx.m_AudioClip == null)
		{
			return;
		}

		string normalizedPrefabName = AudioReplacementDomainConfig.NormalizeTargetKey(prefabName);
		if (string.IsNullOrWhiteSpace(normalizedPrefabName))
		{
			normalizedPrefabName = (prefabName ?? string.Empty).Trim();
		}

		if (string.IsNullOrWhiteSpace(normalizedPrefabName))
		{
			return;
		}

		AudioClip clip = sfx.m_AudioClip;
		string clipName = string.IsNullOrWhiteSpace(clip.name)
			? "UnnamedClip"
			: clip.name.Trim();

		Dictionary<string, DetectedAudioEntry> map = GetDetectedAudioMap(domain);
		string baseKey = normalizedPrefabName;
		string key = baseKey;
		int suffix = 2;
		while (map.ContainsKey(key))
		{
			key = $"{baseKey}#{suffix}";
			suffix++;
		}

		string displayName = $"{normalizedPrefabName} ({clipName})";
		map[key] = new DetectedAudioEntry(
			key,
			normalizedPrefabName,
			displayName,
			clipName,
			clip,
			SirenSfxProfile.FromSfx(sfx));
	}

	// Finalize one detection pass, normalize selection, and refresh UI values.
	internal static void CompleteDetectedAudioCollection(DeveloperAudioDomain domain)
	{
		Dictionary<string, DetectedAudioEntry> map = GetDetectedAudioMap(domain);
		ref string selection = ref GetDeveloperSelectionRef(domain);
		ref string status = ref GetDeveloperStatusRef(domain);

		if (map.Count == 0)
		{
			selection = string.Empty;
			status = GetNoDetectedMessage(domain);
			OptionsVersion++;
			return;
		}

		if (string.IsNullOrWhiteSpace(selection) || !map.ContainsKey(selection))
		{
			selection = GetSortedEntries(map)[0].Key;
		}

		status = $"Detected {map.Count} {GetDeveloperDomainPluralLabel(domain)} in the active world.";
		OptionsVersion++;
	}

	// Returns true when at least one detected entry exists for this domain.
	internal static bool HasDetectedDeveloperAudio(DeveloperAudioDomain domain)
	{
		return GetDetectedAudioMap(domain).Count > 0;
	}

	// Dropdown source for one domain in Developer tab.
	internal static DropdownItem<string>[] BuildDeveloperDetectedDropdown(DeveloperAudioDomain domain)
	{
		Dictionary<string, DetectedAudioEntry> map = GetDetectedAudioMap(domain);
		if (map.Count == 0)
		{
			return new[]
			{
				new DropdownItem<string>
				{
					value = string.Empty,
					displayName = GetNoDetectedDropdownLabel(domain),
					disabled = true
				}
			};
		}

		List<DetectedAudioEntry> entries = GetSortedEntries(map);
		List<DropdownItem<string>> items = new List<DropdownItem<string>>(entries.Count);
		for (int i = 0; i < entries.Count; i++)
		{
			DetectedAudioEntry entry = entries[i];
			items.Add(new DropdownItem<string>
			{
				value = entry.Key,
				displayName = entry.DisplayName
			});
		}

		return items.ToArray();
	}

	
	// Dropdown source used by profile-copy controls for detected runtime SFX profiles.
	internal static DropdownItem<string>[] BuildDetectedCopySourceDropdown(DeveloperAudioDomain domain)
	{
		Dictionary<string, DetectedAudioEntry> map = GetDetectedAudioMap(domain);
		if (map.Count == 0)
		{
			return Array.Empty<DropdownItem<string>>();
		}

		List<DetectedAudioEntry> entries = GetSortedEntries(map);
		List<DropdownItem<string>> items = new List<DropdownItem<string>>(entries.Count);
		for (int i = 0; i < entries.Count; i++)
		{
			DetectedAudioEntry entry = entries[i];
			string value = BuildDetectedCopySourceValue(entry.Key);
			if (string.IsNullOrWhiteSpace(value))
			{
				continue;
			}

			items.Add(new DropdownItem<string>
			{
				value = value,
				displayName = $"Detected: {entry.DisplayName}"
			});
		}

		return items.ToArray();
	}

	// Resolve a detected copy-source token to a runtime SFX profile snapshot.
	internal static bool TryGetDetectedCopySourceProfile(DeveloperAudioDomain domain, string copySourceSelection, out SirenSfxProfile profile)
	{
		profile = null!;
		if (!TryParseDetectedCopySourceValue(copySourceSelection, out string detectedKey))
		{
			return false;
		}

		Dictionary<string, DetectedAudioEntry> map = GetDetectedAudioMap(domain);
		if (!map.TryGetValue(detectedKey, out DetectedAudioEntry entry))
		{
			return false;
		}

		profile = entry.Profile.ClampCopy();
		return true;
	}

	// Get currently selected detected key for one domain.
	internal static string GetDeveloperSelection(DeveloperAudioDomain domain)
	{
		return GetDeveloperSelectionRef(domain);
	}

	// Set currently selected detected key for one domain.
	internal static void SetDeveloperSelection(DeveloperAudioDomain domain, string selection)
	{
		Dictionary<string, DetectedAudioEntry> map = GetDetectedAudioMap(domain);
		string key = (selection ?? string.Empty).Trim();
		ref string selected = ref GetDeveloperSelectionRef(domain);

		string nextSelection = !string.IsNullOrWhiteSpace(key) && map.ContainsKey(key)
			? key
			: map.Count > 0
				? GetSortedEntries(map)[0].Key
				: string.Empty;
		if (string.Equals(selected, nextSelection, StringComparison.Ordinal))
		{
			return;
		}

		selected = nextSelection;
		OptionsVersion++;
	}

	// Status text for last Developer-tab action on one domain.
	internal static string GetDeveloperActionStatusText(DeveloperAudioDomain domain)
	{
		return GetDeveloperStatusRef(domain);
	}

	
	// Read/write module display-name field used by the Developer Module Builder section.
	internal static string GetDeveloperModuleDisplayName()
	{
		return s_DeveloperModuleDisplayName;
	}

	// Read/write module id field used by the Developer Module Builder section.
	internal static void SetDeveloperModuleDisplayName(string value)
	{
		s_DeveloperModuleDisplayName = NormalizeDeveloperModuleDisplayName(value);
	}

	// Read/write module id field used by the Developer Module Builder section.
	internal static string GetDeveloperModuleId()
	{
		return s_DeveloperModuleId;
	}

	// Update module id while enforcing safe manifest-compatible characters.
	internal static void SetDeveloperModuleId(string value)
	{
		s_DeveloperModuleId = NormalizeDeveloperModuleId(value);
	}

	// Read/write output folder name used when generating a module under the Mods folder.
	internal static string GetDeveloperModuleFolderName()
	{
		return s_DeveloperModuleFolderName;
	}

	// Update output folder name while enforcing safe file-system characters.
	internal static void SetDeveloperModuleFolderName(string value)
	{
		s_DeveloperModuleFolderName = NormalizeDeveloperModuleFolderName(value);
	}

	// Read/write module export directory used by the directory picker in the Developer Module Builder.
	internal static string GetDeveloperModuleExportDirectory()
	{
		if (string.IsNullOrWhiteSpace(s_DeveloperModuleExportDirectory))
		{
			s_DeveloperModuleExportDirectory = GetDeveloperModulesRootDirectory(ensureExists: true);
		}

		return s_DeveloperModuleExportDirectory;
	}

	// Update export directory from options, normalizing to an absolute path when possible.
	internal static void SetDeveloperModuleExportDirectory(string value)
	{
		string normalized = NormalizeDeveloperModuleExportDirectory(value);
		if (string.IsNullOrWhiteSpace(normalized))
		{
			normalized = GetDeveloperModulesRootDirectory(ensureExists: true);
		}

		if (string.Equals(s_DeveloperModuleExportDirectory, normalized, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}

		s_DeveloperModuleExportDirectory = normalized;
		OptionsVersion++;
	}

	// Dropdown source for local-audio module-builder selectors (siren/engine/ambient).
	internal static DropdownItem<string>[] BuildDeveloperModuleLocalAudioDropdown(DeveloperAudioDomain domain)
	{
		EnsureDeveloperModuleIncludeStateCurrent();
		List<string> keys = GetEligibleLocalModuleKeys(domain);
		if (keys.Count == 0)
		{
			return new[]
			{
				new DropdownItem<string>
				{
					value = string.Empty,
					displayName = GetDeveloperModuleLocalDropdownEmptyLabel(domain),
					disabled = true
				}
			};
		}

		HashSet<string> included = GetDeveloperModuleIncludedSet(domain);
		List<DropdownItem<string>> items = new List<DropdownItem<string>>(keys.Count);
		for (int i = 0; i < keys.Count; i++)
		{
			string key = keys[i];
			string displayName = FormatSirenDisplayName(key);
			if (included.Contains(key))
			{
				displayName = $"{displayName} (Included)";
			}

			items.Add(new DropdownItem<string>
			{
				value = key,
				displayName = displayName
			});
		}

		return items.ToArray();
	}

	// Get currently selected local key in one module-builder domain.
	internal static string GetDeveloperModuleLocalAudioSelection(DeveloperAudioDomain domain)
	{
		EnsureDeveloperModuleIncludeStateCurrent();
		return GetDeveloperModuleLocalSelectionRef(domain);
	}

	// Set current local selection in one module-builder domain.
	internal static void SetDeveloperModuleLocalAudioSelection(DeveloperAudioDomain domain, string selection)
	{
		EnsureDeveloperModuleIncludeStateCurrent();
		string normalized = SirenPathUtils.NormalizeProfileKey(selection ?? string.Empty);
		List<string> keys = GetEligibleLocalModuleKeys(domain);
		ref string current = ref GetDeveloperModuleLocalSelectionRef(domain);

		string next = string.Empty;
		if (!string.IsNullOrWhiteSpace(normalized))
		{
			for (int i = 0; i < keys.Count; i++)
			{
				if (string.Equals(keys[i], normalized, StringComparison.OrdinalIgnoreCase))
				{
					next = keys[i];
					break;
				}
			}
		}

		if (string.IsNullOrWhiteSpace(next) && keys.Count > 0)
		{
			next = keys[0];
		}

		if (string.Equals(current, next, StringComparison.Ordinal))
		{
			return;
		}

		current = next;
		OptionsVersion++;
	}

	// Include the currently selected local file in one module-builder domain.
	internal static void IncludeSelectedLocalAudioInModule(DeveloperAudioDomain domain)
	{
		EnsureDeveloperModuleIncludeStateCurrent();
		ref string selection = ref GetDeveloperModuleLocalSelectionRef(domain);
		if (string.IsNullOrWhiteSpace(selection))
		{
			SetDeveloperModuleStatus($"No local {GetDeveloperModuleDomainName(domain)} file is selected.", isWarning: true);
			return;
		}

		HashSet<string> included = GetDeveloperModuleIncludedSet(domain);
		if (!included.Add(selection))
		{
			SetDeveloperModuleStatus($"Local {GetDeveloperModuleDomainName(domain)} '{FormatSirenDisplayName(selection)}' is already included.", isWarning: false);
			return;
		}

		SetDeveloperModuleStatus($"Included local {GetDeveloperModuleDomainName(domain)} '{FormatSirenDisplayName(selection)}' for module export.", isWarning: false);
	}

	// Exclude the currently selected local file in one module-builder domain.
	internal static void ExcludeSelectedLocalAudioFromModule(DeveloperAudioDomain domain)
	{
		EnsureDeveloperModuleIncludeStateCurrent();
		ref string selection = ref GetDeveloperModuleLocalSelectionRef(domain);
		if (string.IsNullOrWhiteSpace(selection))
		{
			SetDeveloperModuleStatus($"No local {GetDeveloperModuleDomainName(domain)} file is selected.", isWarning: true);
			return;
		}

		HashSet<string> included = GetDeveloperModuleIncludedSet(domain);
		if (!included.Remove(selection))
		{
			SetDeveloperModuleStatus($"Local {GetDeveloperModuleDomainName(domain)} '{FormatSirenDisplayName(selection)}' is already excluded.", isWarning: false);
			return;
		}

		SetDeveloperModuleStatus($"Excluded local {GetDeveloperModuleDomainName(domain)} '{FormatSirenDisplayName(selection)}' from module export.", isWarning: false);
	}

	// Include every currently eligible local audio file across all domains.
	internal static void IncludeAllLocalAudioInModule()
	{
		EnsureDeveloperModuleIncludeStateCurrent();
		List<string> sirenKeys = GetEligibleLocalModuleKeys(DeveloperAudioDomain.Siren);
		List<string> engineKeys = GetEligibleLocalModuleKeys(DeveloperAudioDomain.VehicleEngine);
		List<string> ambientKeys = GetEligibleLocalModuleKeys(DeveloperAudioDomain.Ambient);

		int totalAvailable = sirenKeys.Count + engineKeys.Count + ambientKeys.Count;
		if (totalAvailable == 0)
		{
			SetDeveloperModuleStatus("No local audio files are currently available to include.", isWarning: true);
			return;
		}

		int added = 0;
		HashSet<string> sirenIncluded = GetDeveloperModuleIncludedSet(DeveloperAudioDomain.Siren);
		for (int i = 0; i < sirenKeys.Count; i++)
		{
			if (sirenIncluded.Add(sirenKeys[i]))
			{
				added++;
			}
		}

		HashSet<string> engineIncluded = GetDeveloperModuleIncludedSet(DeveloperAudioDomain.VehicleEngine);
		for (int i = 0; i < engineKeys.Count; i++)
		{
			if (engineIncluded.Add(engineKeys[i]))
			{
				added++;
			}
		}

		HashSet<string> ambientIncluded = GetDeveloperModuleIncludedSet(DeveloperAudioDomain.Ambient);
		for (int i = 0; i < ambientKeys.Count; i++)
		{
			if (ambientIncluded.Add(ambientKeys[i]))
			{
				added++;
			}
		}

		SetDeveloperModuleStatus(
			added > 0
				? $"Included {added} local file(s). Total included: {GetTotalDeveloperModuleIncludedCount()}."
				: $"All available local files are already included ({GetTotalDeveloperModuleIncludedCount()}).",
			isWarning: false);
	}

	// Clear all local-file inclusions across all module-builder domains.
	internal static void ClearLocalAudioModuleInclusions()
	{
		EnsureDeveloperModuleIncludeStateCurrent();
		int previous = GetTotalDeveloperModuleIncludedCount();
		if (previous == 0)
		{
			SetDeveloperModuleStatus("No local audio files are currently included.", isWarning: false);
			return;
		}

		s_DeveloperModuleIncludedSirens.Clear();
		s_DeveloperModuleIncludedEngines.Clear();
		s_DeveloperModuleIncludedAmbient.Clear();
		SetDeveloperModuleStatus("Cleared all included local audio files.", isWarning: false);
	}

	// Read-only summary of currently included local files and per-domain counts.
	internal static string GetDeveloperModuleInclusionSummaryText()
	{
		EnsureDeveloperModuleIncludeStateCurrent();
		List<string> sirenKeys = GetEligibleLocalModuleKeys(DeveloperAudioDomain.Siren);
		List<string> engineKeys = GetEligibleLocalModuleKeys(DeveloperAudioDomain.VehicleEngine);
		List<string> ambientKeys = GetEligibleLocalModuleKeys(DeveloperAudioDomain.Ambient);

		int totalAvailable = sirenKeys.Count + engineKeys.Count + ambientKeys.Count;
		if (totalAvailable == 0)
		{
			return "No local custom audio files are currently available.";
		}

		StringBuilder builder = new StringBuilder(512);
		builder.Append("Included: ").Append(GetTotalDeveloperModuleIncludedCount()).Append('/').Append(totalAvailable);
		AppendDeveloperModuleInclusionSummary(builder, "Sirens", sirenKeys, s_DeveloperModuleIncludedSirens);
		AppendDeveloperModuleInclusionSummary(builder, "Vehicle Engines", engineKeys, s_DeveloperModuleIncludedEngines);
		AppendDeveloperModuleInclusionSummary(builder, "Ambient Sounds", ambientKeys, s_DeveloperModuleIncludedAmbient);
		return builder.ToString();
	}

	// Status text for last local-audio module generation action.
	internal static string GetDeveloperModuleStatusText()
	{
		return s_DeveloperModuleStatus;
	}
	// Read-only SFX parameter panel text for selected detected entry.
	internal static string GetDeveloperSfxParametersText(DeveloperAudioDomain domain)
	{
		try
		{
			if (TryGetSelectedDeveloperEntry(domain, out DetectedAudioEntry entry, out _))
			{
				return BuildDetectedDeveloperSfxParametersText(entry);
			}

			if (TryGetDeveloperLocalSfxProfile(domain, out string localProfileKey, out SirenSfxProfile localProfile))
			{
				return BuildLocalDeveloperSfxParametersText(domain, localProfileKey, localProfile);
			}

			return GetNoDetectedMessage(domain);
		}
		catch (Exception ex)
		{
			Log.Warn($"Failed to build developer SFX parameter text for {domain}. {ex.Message}");
			return $"Unable to read SFX parameters: {ex.Message}";
		}
	}

	private static string BuildDetectedDeveloperSfxParametersText(DetectedAudioEntry entry)
	{
		// Render a read-only snapshot of the selected detected runtime source.
		StringBuilder builder = new StringBuilder(512);
		builder.Append("Source Type: Detected Runtime");
		builder.Append('\n').Append("Source Prefab: ").Append(entry.PrefabName);
		builder.Append('\n').Append("Clip Name: ").Append(entry.ClipName);

		if (entry.Clip == null)
		{
			builder.Append('\n').Append("Clip Status: unavailable");
		}
		else
		{
			builder.Append('\n').Append("Length: ").Append(FormatFloat(entry.Clip.length)).Append(" sec");
			builder.Append('\n').Append("Channels: ").Append(entry.Clip.channels);
			builder.Append('\n').Append("Frequency: ").Append(entry.Clip.frequency).Append(" Hz");
		}

		AppendSfxProfileParameterLines(builder, entry.Profile);
		return builder.ToString();
	}

	private static bool TryGetDeveloperLocalSfxProfile(DeveloperAudioDomain domain, out string profileKey, out SirenSfxProfile profile)
	{
		// Resolve currently selected local profile, or first eligible one as fallback.
		profileKey = string.Empty;
		profile = null!;

		IDictionary<string, SirenSfxProfile> profiles = GetLocalDomainProfiles(domain);
		if (profiles == null || profiles.Count == 0)
		{
			return false;
		}

		ref string selected = ref GetDeveloperModuleLocalSelectionRef(domain);
		string normalizedSelected = SirenPathUtils.NormalizeProfileKey(selected ?? string.Empty);
		if (!string.IsNullOrWhiteSpace(normalizedSelected) &&
			!AudioModuleCatalog.IsModuleSelection(normalizedSelected) &&
			profiles.TryGetValue(normalizedSelected, out SirenSfxProfile selectedProfile) &&
			selectedProfile != null)
		{
			profileKey = normalizedSelected;
			profile = selectedProfile.ClampCopy();
			return true;
		}

		List<string> eligible = GetEligibleLocalModuleKeys(domain);
		for (int i = 0; i < eligible.Count; i++)
		{
			string key = eligible[i];
			if (!profiles.TryGetValue(key, out SirenSfxProfile fallbackProfile) || fallbackProfile == null)
			{
				continue;
			}

			selected = key;
			profileKey = key;
			profile = fallbackProfile.ClampCopy();
			return true;
		}

		return false;
	}

	private static string BuildLocalDeveloperSfxParametersText(DeveloperAudioDomain domain, string profileKey, SirenSfxProfile profile)
	{
		// Render a read-only snapshot for a local custom profile selection.
		StringBuilder builder = new StringBuilder(512);
		builder.Append("Source Type: Local Custom Profile");
		builder.Append('\n').Append("Domain: ").Append(GetDeveloperDomainPluralLabel(domain));
		builder.Append('\n').Append("Profile Key: ").Append(profileKey);

		string folderName = GetLocalDomainFolderName(domain);
		if (SirenPathUtils.TryGetCustomSirenFilePath(SettingsDirectory, folderName, profileKey, out string sourcePath))
		{
			builder.Append('\n').Append("File: ").Append(sourcePath);
		}

		AppendSfxProfileParameterLines(builder, profile);
		return builder.ToString();
	}

	private static void AppendSfxProfileParameterLines(StringBuilder builder, SirenSfxProfile profile)
	{
		// Normalize/clamp once before formatting to keep displayed values stable.
		SirenSfxProfile safeProfile = (profile ?? SirenSfxProfile.CreateFallback()).ClampCopy();
		builder.Append('\n').Append("Volume: ").Append(FormatFloat(safeProfile.Volume));
		builder.Append('\n').Append("Pitch: ").Append(FormatFloat(safeProfile.Pitch));
		builder.Append('\n').Append("Spatial Blend: ").Append(FormatFloat(safeProfile.SpatialBlend));
		builder.Append('\n').Append("Doppler: ").Append(FormatFloat(safeProfile.Doppler));
		builder.Append('\n').Append("Spread: ").Append(FormatFloat(safeProfile.Spread));
		builder.Append('\n').Append("Min Distance: ").Append(FormatFloat(safeProfile.MinDistance));
		builder.Append('\n').Append("Max Distance: ").Append(FormatFloat(safeProfile.MaxDistance));
		builder.Append('\n').Append("Loop: ").Append(safeProfile.Loop ? "True" : "False");
		builder.Append('\n').Append("Rolloff Mode: ").Append(safeProfile.RolloffMode.ToString());
		builder.Append('\n').Append("Fade In: ").Append(FormatFloat(safeProfile.FadeInSeconds)).Append(" sec");
		builder.Append('\n').Append("Fade Out: ").Append(FormatFloat(safeProfile.FadeOutSeconds)).Append(" sec");
		builder.Append('\n').Append("Random Start Time: ").Append(safeProfile.RandomStartTime ? "True" : "False");
	}

	// Preview selected detected entry for one domain.
	internal static void PreviewDeveloperSelection(DeveloperAudioDomain domain)
	{
		if (!TryGetSelectedDeveloperEntry(domain, out DetectedAudioEntry entry, out string error))
		{
			SetDeveloperStatus(domain, error, isWarning: true);
			return;
		}

		if (entry.Clip == null)
		{
			SetDeveloperStatus(domain, $"Cannot preview detected {GetDeveloperDomainSingularLabel(domain)} because its clip is unavailable.", isWarning: true);
			return;
		}

		if (!TryPlayPreviewClip(entry.Clip, entry.Profile, out string previewError))
		{
			SetDeveloperStatus(domain, $"Preview failed: {previewError}", isWarning: true);
			return;
		}

		SetDeveloperStatus(domain, $"Previewing detected {GetDeveloperDomainSingularLabel(domain)} '{entry.DisplayName}'.", isWarning: false);
	}

	// Export selected detected entry for one domain as a WAV file.
	internal static void ExportDeveloperSelection(DeveloperAudioDomain domain)
	{
		SetDeveloperStatus(domain, "Export from the Developer tab is currently disabled.", isWarning: true);
	}

	
	// Build a standalone module from local custom audio files and profiles.
	internal static void CreateDeveloperModuleFromLocalAudio()
	{
		EnsureDeveloperModuleIncludeStateCurrent();
		if (GetTotalDeveloperModuleIncludedCount() == 0)
		{
			SetDeveloperModuleStatus("No local audio files are selected. Include one or more files before creating a module.", isWarning: true);
			return;
		}

		s_DeveloperModuleDisplayName = NormalizeDeveloperModuleDisplayName(s_DeveloperModuleDisplayName);
		s_DeveloperModuleId = NormalizeDeveloperModuleId(s_DeveloperModuleId);
		s_DeveloperModuleFolderName = NormalizeDeveloperModuleFolderName(s_DeveloperModuleFolderName);

		string displayName = s_DeveloperModuleDisplayName;
		string moduleId = s_DeveloperModuleId;
		string moduleFolderName = s_DeveloperModuleFolderName;

		try
		{
			string exportRoot = GetResolvedDeveloperModuleExportDirectory(ensureExists: true);
			if (string.IsNullOrWhiteSpace(exportRoot))
			{
				SetDeveloperModuleStatus("Unable to resolve the module export directory.", isWarning: true);
				return;
			}

			string moduleRootPath = BuildUniqueModuleDirectoryPath(exportRoot, moduleFolderName);
			Directory.CreateDirectory(moduleRootPath);

			int skippedMissing = 0;
			int skippedUnsupported = 0;
			int skippedModuleSelections = 0;

			List<DeveloperModuleManifestEntry> sirenEntries = ExportLocalProfilesToModule(
				Config.CustomSirenProfiles,
				Config.CustomSirensFolderName,
				"Audio/Sirens",
				moduleRootPath,
				s_DeveloperModuleIncludedSirens,
				ref skippedMissing,
				ref skippedUnsupported,
				ref skippedModuleSelections);

			List<DeveloperModuleManifestEntry> engineEntries = ExportLocalProfilesToModule(
				VehicleEngineConfig.CustomProfiles,
				VehicleEngineConfig.CustomFolderName,
				"Audio/Engines",
				moduleRootPath,
				s_DeveloperModuleIncludedEngines,
				ref skippedMissing,
				ref skippedUnsupported,
				ref skippedModuleSelections);

			List<DeveloperModuleManifestEntry> ambientEntries = ExportLocalProfilesToModule(
				AmbientConfig.CustomProfiles,
				AmbientConfig.CustomFolderName,
				"Audio/Ambient",
				moduleRootPath,
				s_DeveloperModuleIncludedAmbient,
				ref skippedMissing,
				ref skippedUnsupported,
				ref skippedModuleSelections);

			int totalExported = sirenEntries.Count + engineEntries.Count + ambientEntries.Count;
			if (totalExported == 0)
			{
				TryDeleteDirectory(moduleRootPath);
				SetDeveloperModuleStatus(
					$"No selected local audio files were eligible for module generation. Skipped missing: {skippedMissing}, unsupported format: {skippedUnsupported}, module-based selections: {skippedModuleSelections}.",
					isWarning: true);
				return;
			}

			DeveloperModuleManifest manifest = new DeveloperModuleManifest
			{
				SchemaVersion = 1,
				ModuleId = moduleId,
				DisplayName = displayName,
				Sirens = sirenEntries,
				VehicleEngines = engineEntries,
				Ambient = ambientEntries
			};

			string manifestPath = Path.Combine(moduleRootPath, kDeveloperModuleManifestFileName);
			string manifestJson = JsonDataSerializer.Serialize(manifest);
			File.WriteAllText(manifestPath, manifestJson, new UTF8Encoding(false));
			WriteDeveloperModuleReadme(moduleRootPath, displayName, moduleId, sirenEntries.Count, engineEntries.Count, ambientEntries.Count);

			SetDeveloperModuleStatus(
				$"Created module '{displayName}' at '{moduleRootPath}'. Sirens: {sirenEntries.Count}, Engines: {engineEntries.Count}, Ambient: {ambientEntries.Count}. Skipped missing/unsupported/module: {skippedMissing}/{skippedUnsupported}/{skippedModuleSelections}.",
				isWarning: false);

			SyncCustomSirenCatalog(saveIfChanged: true, forceStatusRefresh: true);
			SyncCustomVehicleEngineCatalog(saveIfChanged: true, forceStatusRefresh: true);
			SyncCustomAmbientCatalog(saveIfChanged: true, forceStatusRefresh: true);
		}
		catch (Exception ex)
		{
			SetDeveloperModuleStatus($"Module generation failed: {ex.Message}", isWarning: true);
		}
	}

	// Copy one domain's local custom profiles into the generated module output folder.
	private static List<DeveloperModuleManifestEntry> ExportLocalProfilesToModule(
		IDictionary<string, SirenSfxProfile> profiles,
		string localFolderName,
		string moduleDomainFolder,
		string moduleRootPath,
		ISet<string> includedKeys,
		ref int skippedMissing,
		ref int skippedUnsupported,
		ref int skippedModuleSelections)
	{
		List<DeveloperModuleManifestEntry> entries = new List<DeveloperModuleManifestEntry>();
		if (profiles == null || profiles.Count == 0 || includedKeys == null || includedKeys.Count == 0)
		{
			return entries;
		}

		List<string> keys = new List<string>(profiles.Keys);
		keys.Sort(StringComparer.OrdinalIgnoreCase);
		for (int i = 0; i < keys.Count; i++)
		{
			string normalizedKey = SirenPathUtils.NormalizeProfileKey(keys[i] ?? string.Empty);
			if (string.IsNullOrWhiteSpace(normalizedKey) || SirenReplacementConfig.IsDefaultSelection(normalizedKey))
			{
				continue;
			}

			if (!includedKeys.Contains(normalizedKey))
			{
				continue;
			}

			if (AudioModuleCatalog.IsModuleSelection(normalizedKey))
			{
				skippedModuleSelections++;
				continue;
			}

			if (!profiles.TryGetValue(normalizedKey, out SirenSfxProfile profile) || profile == null)
			{
				continue;
			}

			if (!SirenPathUtils.TryGetCustomSirenFilePath(SettingsDirectory, localFolderName, normalizedKey, out string sourcePath) ||
				!File.Exists(sourcePath))
			{
				skippedMissing++;
				continue;
			}

			if (!SirenPathUtils.IsSupportedCustomSirenExtension(Path.GetExtension(sourcePath)))
			{
				skippedUnsupported++;
				continue;
			}

			string relativeFilePath = $"{moduleDomainFolder}/{normalizedKey}".Replace('\\', '/');
			string outputPath = Path.Combine(moduleRootPath, relativeFilePath.Replace('/', Path.DirectorySeparatorChar));
			string? outputDirectory = Path.GetDirectoryName(outputPath);
			if (!string.IsNullOrWhiteSpace(outputDirectory))
			{
				Directory.CreateDirectory(outputDirectory);
			}

			try
			{
				File.Copy(sourcePath, outputPath, overwrite: true);
			}
			catch (Exception ex)
			{
				Log.Warn($"Skipping module export for '{normalizedKey}' because file copy failed. {ex.Message}");
				skippedMissing++;
				continue;
			}

			entries.Add(new DeveloperModuleManifestEntry
			{
				Key = normalizedKey,
				DisplayName = BuildLocalModuleEntryDisplayName(normalizedKey),
				File = relativeFilePath,
				Profile = profile.ClampCopy()
			});
		}

		return entries;
	}

	// Resolve the target root where generated modules are written.
	private static string GetDeveloperModulesRootDirectory(bool ensureExists)
	{
		string? modsRoot = Path.GetDirectoryName(SettingsDirectory);
		string resolved = string.IsNullOrWhiteSpace(modsRoot)
			? Path.Combine(SettingsDirectory, "Generated Modules")
			: modsRoot;
		if (ensureExists)
		{
			Directory.CreateDirectory(resolved);
		}

		return resolved;
	}

	// Choose an output directory name without clobbering existing module folders.
	private static string BuildUniqueModuleDirectoryPath(string modsRoot, string folderName)
	{
		string normalizedFolderName = NormalizeDeveloperModuleFolderName(folderName);
		string candidate = Path.Combine(modsRoot, normalizedFolderName);
		if (!Directory.Exists(candidate))
		{
			return candidate;
		}

		string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
		candidate = Path.Combine(modsRoot, $"{normalizedFolderName}_{timestamp}");
		if (!Directory.Exists(candidate))
		{
			return candidate;
		}

		for (int i = 2; i < 1000; i++)
		{
			string indexed = Path.Combine(modsRoot, $"{normalizedFolderName}_{timestamp}_{i}");
			if (!Directory.Exists(indexed))
			{
				return indexed;
			}
		}

		return Path.Combine(modsRoot, $"{normalizedFolderName}_{Guid.NewGuid():N}");
	}

	// Normalize module display name input.
	private static string NormalizeDeveloperModuleDisplayName(string value)
	{
		string normalized = (value ?? string.Empty).Trim();
		return string.IsNullOrWhiteSpace(normalized) ? kDeveloperModuleDefaultDisplayName : normalized;
	}

	// Normalize module identifier input.
	private static string NormalizeDeveloperModuleId(string value)
	{
		string source = string.IsNullOrWhiteSpace(value)
			? kDeveloperModuleDefaultId
			: value.Trim();

		StringBuilder builder = new StringBuilder(source.Length);
		for (int i = 0; i < source.Length; i++)
		{
			char c = char.ToLowerInvariant(source[i]);
			if (char.IsLetterOrDigit(c) || c == '.' || c == '_' || c == '-')
			{
				builder.Append(c);
			}
			else
			{
				builder.Append('-');
			}
		}

		string normalized = builder.ToString();
		while (normalized.Contains("--", StringComparison.Ordinal))
		{
			normalized = normalized.Replace("--", "-", StringComparison.Ordinal);
		}

		normalized = normalized.Trim('-', '.', '_');
		return string.IsNullOrWhiteSpace(normalized) ? kDeveloperModuleDefaultId : normalized;
	}

	// Normalize module output folder name input.
	private static string NormalizeDeveloperModuleFolderName(string value)
	{
		string source = string.IsNullOrWhiteSpace(value)
			? kDeveloperModuleDefaultFolderName
			: value.Trim();

		char[] invalid = Path.GetInvalidFileNameChars();
		StringBuilder builder = new StringBuilder(source.Length);
		for (int i = 0; i < source.Length; i++)
		{
			char c = source[i];
			if (Array.IndexOf(invalid, c) >= 0 || c == '/' || c == '\\' || char.IsControl(c))
			{
				builder.Append('-');
			}
			else
			{
				builder.Append(c);
			}
		}

		string normalized = builder.ToString().Trim().Trim('.');
		while (normalized.Contains("--", StringComparison.Ordinal))
		{
			normalized = normalized.Replace("--", "-", StringComparison.Ordinal);
		}

		normalized = normalized.Trim('-');
		return string.IsNullOrWhiteSpace(normalized) ? kDeveloperModuleDefaultFolderName : normalized;
	}

	// Normalize module export directory input into a full path when possible.
	private static string NormalizeDeveloperModuleExportDirectory(string value)
	{
		string raw = (value ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(raw))
		{
			return string.Empty;
		}

		try
		{
			return Path.GetFullPath(raw);
		}
		catch
		{
			return string.Empty;
		}
	}

	// Resolve active export directory for module generation, defaulting to the mod directory parent.
	private static string GetResolvedDeveloperModuleExportDirectory(bool ensureExists)
	{
		string resolved = NormalizeDeveloperModuleExportDirectory(s_DeveloperModuleExportDirectory);
		if (string.IsNullOrWhiteSpace(resolved))
		{
			resolved = GetDeveloperModulesRootDirectory(ensureExists);
		}
		else if (ensureExists)
		{
			Directory.CreateDirectory(resolved);
		}

		s_DeveloperModuleExportDirectory = resolved;
		return resolved;
	}

	// Keep module-builder local selections and include lists aligned with current local catalog contents.
	private static void EnsureDeveloperModuleIncludeStateCurrent()
	{
		List<string> sirenKeys = GetEligibleLocalModuleKeys(DeveloperAudioDomain.Siren);
		List<string> engineKeys = GetEligibleLocalModuleKeys(DeveloperAudioDomain.VehicleEngine);
		List<string> ambientKeys = GetEligibleLocalModuleKeys(DeveloperAudioDomain.Ambient);

		bool changed = false;
		changed |= SyncDeveloperModuleDomainState(DeveloperAudioDomain.Siren, sirenKeys);
		changed |= SyncDeveloperModuleDomainState(DeveloperAudioDomain.VehicleEngine, engineKeys);
		changed |= SyncDeveloperModuleDomainState(DeveloperAudioDomain.Ambient, ambientKeys);

		if (!s_DeveloperModuleIncludeInitialized)
		{
			for (int i = 0; i < sirenKeys.Count; i++)
			{
				if (s_DeveloperModuleIncludedSirens.Add(sirenKeys[i]))
				{
					changed = true;
				}
			}

			for (int i = 0; i < engineKeys.Count; i++)
			{
				if (s_DeveloperModuleIncludedEngines.Add(engineKeys[i]))
				{
					changed = true;
				}
			}

			for (int i = 0; i < ambientKeys.Count; i++)
			{
				if (s_DeveloperModuleIncludedAmbient.Add(ambientKeys[i]))
				{
					changed = true;
				}
			}

			s_DeveloperModuleIncludeInitialized = true;
		}

		if (changed)
		{
			OptionsVersion++;
		}
	}

	// Validate one domain's selected key and included set against currently eligible local files.
	private static bool SyncDeveloperModuleDomainState(DeveloperAudioDomain domain, IReadOnlyList<string> eligibleKeys)
	{
		HashSet<string> eligible = new HashSet<string>(eligibleKeys, StringComparer.OrdinalIgnoreCase);
		HashSet<string> included = GetDeveloperModuleIncludedSet(domain);
		bool changed = false;

		List<string> staleIncluded = new List<string>();
		foreach (string key in included)
		{
			if (!eligible.Contains(key))
			{
				staleIncluded.Add(key);
			}
		}

		for (int i = 0; i < staleIncluded.Count; i++)
		{
			if (included.Remove(staleIncluded[i]))
			{
				changed = true;
			}
		}

		ref string selection = ref GetDeveloperModuleLocalSelectionRef(domain);
		if (string.IsNullOrWhiteSpace(selection) || !eligible.Contains(selection))
		{
			string next = eligibleKeys.Count > 0 ? eligibleKeys[0] : string.Empty;
			if (!string.Equals(selection, next, StringComparison.Ordinal))
			{
				selection = next;
				changed = true;
			}
		}

		return changed;
	}

	// Enumerate local (non-module) profile keys that currently map to readable custom audio files.
	private static List<string> GetEligibleLocalModuleKeys(DeveloperAudioDomain domain)
	{
		IDictionary<string, SirenSfxProfile> profiles = GetLocalDomainProfiles(domain);
		if (profiles == null || profiles.Count == 0)
		{
			return new List<string>();
		}

		string folderName = GetLocalDomainFolderName(domain);
		HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		List<string> keys = new List<string>();
		foreach (KeyValuePair<string, SirenSfxProfile> pair in profiles)
		{
			string normalized = SirenPathUtils.NormalizeProfileKey(pair.Key ?? string.Empty);
			if (string.IsNullOrWhiteSpace(normalized) ||
				SirenReplacementConfig.IsDefaultSelection(normalized) ||
				AudioModuleCatalog.IsModuleSelection(normalized))
			{
				continue;
			}

			if (!SirenPathUtils.TryGetCustomSirenFilePath(SettingsDirectory, folderName, normalized, out string resolvedPath) ||
				!File.Exists(resolvedPath) ||
				!SirenPathUtils.IsSupportedCustomSirenExtension(Path.GetExtension(resolvedPath)))
			{
				continue;
			}

			if (seen.Add(normalized))
			{
				keys.Add(normalized);
			}
		}

		keys.Sort(StringComparer.OrdinalIgnoreCase);
		return keys;
	}

	// Map module-builder domain to active local profile dictionary.
	private static IDictionary<string, SirenSfxProfile> GetLocalDomainProfiles(DeveloperAudioDomain domain)
	{
		switch (domain)
		{
			case DeveloperAudioDomain.Siren:
				return Config.CustomSirenProfiles;
			case DeveloperAudioDomain.VehicleEngine:
				return VehicleEngineConfig.CustomProfiles;
			default:
				return AmbientConfig.CustomProfiles;
		}
	}

	// Map module-builder domain to configured local custom folder name.
	private static string GetLocalDomainFolderName(DeveloperAudioDomain domain)
	{
		switch (domain)
		{
			case DeveloperAudioDomain.Siren:
				return string.IsNullOrWhiteSpace(Config.CustomSirensFolderName)
					? SirenReplacementConfig.DefaultCustomSirensFolderName
					: Config.CustomSirensFolderName;
			case DeveloperAudioDomain.VehicleEngine:
				return string.IsNullOrWhiteSpace(VehicleEngineConfig.CustomFolderName)
					? VehicleEngineCustomFolderName
					: VehicleEngineConfig.CustomFolderName;
			default:
				return string.IsNullOrWhiteSpace(AmbientConfig.CustomFolderName)
					? AmbientCustomFolderName
					: AmbientConfig.CustomFolderName;
		}
	}

	// Domain-specific selected local key backing fields for module-builder dropdowns.
	private static ref string GetDeveloperModuleLocalSelectionRef(DeveloperAudioDomain domain)
	{
		switch (domain)
		{
			case DeveloperAudioDomain.Siren:
				return ref s_DeveloperModuleSelectedLocalSirenKey;
			case DeveloperAudioDomain.VehicleEngine:
				return ref s_DeveloperModuleSelectedLocalEngineKey;
			default:
				return ref s_DeveloperModuleSelectedLocalAmbientKey;
		}
	}

	// Domain-specific include sets used by module-builder include/exclude actions.
	private static HashSet<string> GetDeveloperModuleIncludedSet(DeveloperAudioDomain domain)
	{
		switch (domain)
		{
			case DeveloperAudioDomain.Siren:
				return s_DeveloperModuleIncludedSirens;
			case DeveloperAudioDomain.VehicleEngine:
				return s_DeveloperModuleIncludedEngines;
			default:
				return s_DeveloperModuleIncludedAmbient;
		}
	}

	// Empty-state dropdown labels for each module-builder local-audio domain.
	private static string GetDeveloperModuleLocalDropdownEmptyLabel(DeveloperAudioDomain domain)
	{
		switch (domain)
		{
			case DeveloperAudioDomain.Siren:
				return "No local siren files found";
			case DeveloperAudioDomain.VehicleEngine:
				return "No local engine files found";
			default:
				return "No local ambient files found";
		}
	}

	// Friendly singular domain names used in include/exclude status text.
	private static string GetDeveloperModuleDomainName(DeveloperAudioDomain domain)
	{
		switch (domain)
		{
			case DeveloperAudioDomain.Siren:
				return "siren";
			case DeveloperAudioDomain.VehicleEngine:
				return "engine";
			default:
				return "ambient";
		}
	}

	// Aggregate count of currently included local module-builder files across all domains.
	private static int GetTotalDeveloperModuleIncludedCount()
	{
		return s_DeveloperModuleIncludedSirens.Count + s_DeveloperModuleIncludedEngines.Count + s_DeveloperModuleIncludedAmbient.Count;
	}

	// Append one domain's include summary block with per-file lines.
	private static void AppendDeveloperModuleInclusionSummary(
		StringBuilder builder,
		string domainLabel,
		IReadOnlyList<string> eligibleKeys,
		ISet<string> included)
	{
		int includedCount = 0;
		for (int i = 0; i < eligibleKeys.Count; i++)
		{
			if (included.Contains(eligibleKeys[i]))
			{
				includedCount++;
			}
		}

		builder.Append('\n').Append(domainLabel).Append(": ").Append(includedCount).Append('/').Append(eligibleKeys.Count);
		if (includedCount == 0)
		{
			return;
		}

		for (int i = 0; i < eligibleKeys.Count; i++)
		{
			string key = eligibleKeys[i];
			if (!included.Contains(key))
			{
				continue;
			}

			builder.Append('\n').Append(" - ").Append(FormatSirenDisplayName(key));
		}
	}

	// Build a readable display label from a local profile key.
	private static string BuildLocalModuleEntryDisplayName(string profileKey)
	{
		string normalized = profileKey.Replace('\\', '/');
		string baseName = Path.GetFileNameWithoutExtension(normalized);
		if (string.IsNullOrWhiteSpace(baseName))
		{
			return normalized;
		}

		return baseName;
	}

	// Status setter used by local module generation actions.
	private static void SetDeveloperModuleStatus(string message, bool isWarning)
	{
		s_DeveloperModuleStatus = string.IsNullOrWhiteSpace(message) ? "Action completed." : message.Trim();
		if (isWarning)
		{
			Log.Warn(s_DeveloperModuleStatus);
		}
		else
		{
			Log.Info(s_DeveloperModuleStatus);
		}

		OptionsVersion++;
	}

	// Write a short README into generated module output.
	private static void WriteDeveloperModuleReadme(
		string moduleRootPath,
		string displayName,
		string moduleId,
		int sirenCount,
		int engineCount,
		int ambientCount)
	{
		StringBuilder builder = new StringBuilder(512);
		builder.AppendLine("Audio Switcher Generated Module");
		builder.AppendLine();
		builder.Append("Display Name: ").AppendLine(displayName);
		builder.Append("Module ID: ").AppendLine(moduleId);
		builder.AppendLine();
		builder.Append("Sirens: ").AppendLine(sirenCount.ToString(CultureInfo.InvariantCulture));
		builder.Append("Vehicle Engines: ").AppendLine(engineCount.ToString(CultureInfo.InvariantCulture));
		builder.Append("Ambient Sounds: ").AppendLine(ambientCount.ToString(CultureInfo.InvariantCulture));
		builder.AppendLine();
		builder.AppendLine("Generated by Audio Switcher Developer > Module Builder.");

		string readmePath = Path.Combine(moduleRootPath, "README.txt");
		File.WriteAllText(readmePath, builder.ToString(), new UTF8Encoding(false));
	}

	// Best-effort cleanup helper for aborted module generation.
	private static void TryDeleteDirectory(string directoryPath)
	{
		if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
		{
			return;
		}

		try
		{
			Directory.Delete(directoryPath, recursive: true);
		}
		catch
		{
			// Ignore cleanup failures and keep reporting original error state.
		}
	}
	private static bool ResetDetectedAudioDomainInternal(DeveloperAudioDomain domain)
	{
		// Clear one detected domain and reset UI-facing selection/status state.
		Dictionary<string, DetectedAudioEntry> map = GetDetectedAudioMap(domain);
		bool changed = map.Count > 0 || !string.IsNullOrWhiteSpace(GetDeveloperSelectionRef(domain));
		map.Clear();

		ref string selection = ref GetDeveloperSelectionRef(domain);
		selection = string.Empty;

		ref string status = ref GetDeveloperStatusRef(domain);
		string emptyMessage = GetNoDetectedMessage(domain);
		if (!string.Equals(status, emptyMessage, StringComparison.Ordinal))
		{
			status = emptyMessage;
			changed = true;
		}

		return changed;
	}

	private static bool TryGetSelectedDeveloperEntry(DeveloperAudioDomain domain, out DetectedAudioEntry entry, out string error)
	{
		// Resolve selected entry; if invalid, pick the first sorted entry as fallback.
		Dictionary<string, DetectedAudioEntry> map = GetDetectedAudioMap(domain);
		if (map.Count == 0)
		{
			entry = null!;
			error = GetNoDetectedMessage(domain);
			return false;
		}

		ref string selection = ref GetDeveloperSelectionRef(domain);
		if (!string.IsNullOrWhiteSpace(selection) && map.TryGetValue(selection, out entry))
		{
			error = string.Empty;
			return true;
		}

		List<DetectedAudioEntry> entries = GetSortedEntries(map);
		entry = entries[0];
		selection = entry.Key;
		error = string.Empty;
		return true;
	}

	private static List<DetectedAudioEntry> GetSortedEntries(Dictionary<string, DetectedAudioEntry> map)
	{
		List<DetectedAudioEntry> entries = new List<DetectedAudioEntry>(map.Values);
		entries.Sort(static (left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.DisplayName, right.DisplayName));
		return entries;
	}

	// Domain-to-catalog map resolver for detected runtime entries.
	private static Dictionary<string, DetectedAudioEntry> GetDetectedAudioMap(DeveloperAudioDomain domain)
	{
		switch (domain)
		{
			case DeveloperAudioDomain.Siren:
				return s_DetectedSirenAudio;
			case DeveloperAudioDomain.VehicleEngine:
				return s_DetectedEngineAudio;
			default:
				return s_DetectedAmbientAudio;
		}
	}

	private static ref string GetDeveloperSelectionRef(DeveloperAudioDomain domain)
	{
		// Domain-to-selection backing field resolver.
		switch (domain)
		{
			case DeveloperAudioDomain.Siren:
				return ref s_DeveloperSelectedSirenKey;
			case DeveloperAudioDomain.VehicleEngine:
				return ref s_DeveloperSelectedEngineKey;
			default:
				return ref s_DeveloperSelectedAmbientKey;
		}
	}

	private static ref string GetDeveloperStatusRef(DeveloperAudioDomain domain)
	{
		// Domain-to-status backing field resolver.
		switch (domain)
		{
			case DeveloperAudioDomain.Siren:
				return ref s_DeveloperSirenStatus;
			case DeveloperAudioDomain.VehicleEngine:
				return ref s_DeveloperEngineStatus;
			default:
				return ref s_DeveloperAmbientStatus;
		}
	}

	
	private static string BuildDetectedCopySourceValue(string detectedKey)
	{
		// Format copy-source token persisted in settings dropdown values.
		string normalized = SirenPathUtils.NormalizeProfileKey(detectedKey ?? string.Empty);
		if (string.IsNullOrWhiteSpace(normalized))
		{
			return string.Empty;
		}

		return $"{kDetectedCopySourcePrefix}/{normalized}";
	}

	private static bool TryParseDetectedCopySourceValue(string selection, out string detectedKey)
	{
		// Parse and validate detected copy-source token.
		detectedKey = string.Empty;
		if (string.IsNullOrWhiteSpace(selection))
		{
			return false;
		}

		string normalizedSelection = SirenPathUtils.NormalizeProfileKey(selection);
		if (string.IsNullOrWhiteSpace(normalizedSelection))
		{
			return false;
		}

		string prefix = $"{kDetectedCopySourcePrefix}/";
		if (!normalizedSelection.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		string key = normalizedSelection.Substring(prefix.Length);
		if (string.IsNullOrWhiteSpace(key))
		{
			return false;
		}

		detectedKey = key;
		return true;
	}

	private static string GetDeveloperDomainSingularLabel(DeveloperAudioDomain domain)
	{
		switch (domain)
		{
			case DeveloperAudioDomain.Siren:
				return "siren sound";
			case DeveloperAudioDomain.VehicleEngine:
				return "vehicle engine sound";
			default:
				return "ambient sound";
		}
	}

	private static string GetDeveloperDomainPluralLabel(DeveloperAudioDomain domain)
	{
		switch (domain)
		{
			case DeveloperAudioDomain.Siren:
				return "siren sounds";
			case DeveloperAudioDomain.VehicleEngine:
				return "vehicle engine sounds";
			default:
				return "ambient sounds";
		}
	}

	private static string GetNoDetectedDropdownLabel(DeveloperAudioDomain domain)
	{
		switch (domain)
		{
			case DeveloperAudioDomain.Siren:
				return "No detected siren sounds";
			case DeveloperAudioDomain.VehicleEngine:
				return "No detected vehicle engine sounds";
			default:
				return "No detected ambient sounds";
		}
	}

	private static string GetNoDetectedMessage(DeveloperAudioDomain domain)
	{
		switch (domain)
		{
			case DeveloperAudioDomain.Siren:
				return "No detected siren sounds are available yet. Load a map/editor session to detect sounds.";
			case DeveloperAudioDomain.VehicleEngine:
				return "No detected vehicle engine sounds are available yet. Load a map/editor session to detect sounds.";
			default:
				return "No detected ambient sounds are available yet. Load a map/editor session to detect sounds.";
		}
	}

	private static void SetDeveloperStatus(DeveloperAudioDomain domain, string message, bool isWarning)
	{
		// Update domain status text, log channel, and force UI refresh.
		ref string status = ref GetDeveloperStatusRef(domain);
		status = string.IsNullOrWhiteSpace(message) ? "Action completed." : message.Trim();
		if (isWarning)
		{
			Log.Warn(status);
		}
		else
		{
			Log.Info(status);
		}

		OptionsVersion++;
	}

	private static string GetDeveloperExportDirectory(DeveloperAudioDomain domain, bool ensureExists)
	{
		// Domain-specific export path under the mod's settings directory.
		string domainFolder;
		switch (domain)
		{
			case DeveloperAudioDomain.Siren:
				domainFolder = "Sirens";
				break;
			case DeveloperAudioDomain.VehicleEngine:
				domainFolder = "Vehicle Engines";
				break;
			default:
				domainFolder = "Ambient Sounds";
				break;
		}

		string directory = Path.Combine(SettingsDirectory, kDeveloperExportsFolderName, domainFolder);
		if (ensureExists)
		{
			Directory.CreateDirectory(directory);
		}

		return directory;
	}

	private static string BuildDeveloperExportBaseName(DetectedAudioEntry entry)
	{
		// Use prefab+clip names to generate a human-readable export filename stem.
		string prefab = SanitizeExportFileNameSegment(entry.PrefabName);
		string clip = SanitizeExportFileNameSegment(entry.ClipName);
		if (string.Equals(prefab, clip, StringComparison.OrdinalIgnoreCase))
		{
			return prefab;
		}

		return $"{prefab}_{clip}";
	}

	private static string SanitizeExportFileNameSegment(string value)
	{
		// Replace invalid filename chars so generated files are portable.
		string source = string.IsNullOrWhiteSpace(value) ? "audio" : value.Trim();
		char[] invalid = Path.GetInvalidFileNameChars();
		StringBuilder builder = new StringBuilder(source.Length);
		for (int i = 0; i < source.Length; i++)
		{
			char c = source[i];
			if (Array.IndexOf(invalid, c) >= 0 || c == '/' || c == '\\' || char.IsControl(c))
			{
				builder.Append('_');
			}
			else
			{
				builder.Append(c);
			}
		}

		string sanitized = builder.ToString().Trim().Trim('.');
		return string.IsNullOrWhiteSpace(sanitized) ? "audio" : sanitized;
	}

	private static string BuildUniqueExportPath(string directory, string baseName, string extension)
	{
		// Primary timestamped name, then indexed fallback, then GUID as last resort.
		string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
		string candidate = Path.Combine(directory, $"{baseName}_{timestamp}{extension}");
		if (!File.Exists(candidate))
		{
			return candidate;
		}

		for (int i = 2; i < 1000; i++)
		{
			string indexed = Path.Combine(directory, $"{baseName}_{timestamp}_{i}{extension}");
			if (!File.Exists(indexed))
			{
				return indexed;
			}
		}

		return Path.Combine(directory, $"{baseName}_{Guid.NewGuid():N}{extension}");
	}

	private static bool TryEncodeAudioClipToWave(AudioClip clip, out byte[] waveBytes, out string error)
	{
		// Encode Unity float PCM samples to a 16-bit PCM WAV byte stream.
		waveBytes = Array.Empty<byte>();
		error = string.Empty;

		if (clip == null)
		{
			error = "Audio clip is unavailable.";
			return false;
		}

		int channels = clip.channels;
		int sampleRate = clip.frequency;
		int samplesPerChannel = clip.samples;
		if (channels <= 0 || sampleRate <= 0 || samplesPerChannel <= 0)
		{
			error = "Audio clip has no readable PCM sample data.";
			return false;
		}

		int sampleCount = samplesPerChannel * channels;
		float[] samples = new float[sampleCount];
		try
		{
			if (!clip.GetData(samples, 0))
			{
				error = "Unity could not read sample data from this clip.";
				return false;
			}
		}
		catch (Exception ex)
		{
			error = $"Unable to read clip samples: {ex.Message}";
			return false;
		}

		byte[] pcmData = new byte[sampleCount * 2];
		int writeIndex = 0;
		for (int i = 0; i < samples.Length; i++)
		{
			float clamped = Mathf.Clamp(samples[i], -1f, 1f);
			short pcmSample = (short)Mathf.RoundToInt(clamped * short.MaxValue);
			pcmData[writeIndex++] = (byte)(pcmSample & 0xFF);
			pcmData[writeIndex++] = (byte)((pcmSample >> 8) & 0xFF);
		}

		int byteRate = sampleRate * channels * 2;
		short blockAlign = (short)(channels * 2);
		int dataSize = pcmData.Length;
		int riffChunkSize = 36 + dataSize;

		using MemoryStream stream = new MemoryStream(44 + dataSize);
		using (BinaryWriter writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true))
		{
			writer.Write(Encoding.ASCII.GetBytes("RIFF"));
			writer.Write(riffChunkSize);
			writer.Write(Encoding.ASCII.GetBytes("WAVE"));
			writer.Write(Encoding.ASCII.GetBytes("fmt "));
			writer.Write(16);
			writer.Write((short)1);
			writer.Write((short)channels);
			writer.Write(sampleRate);
			writer.Write(byteRate);
			writer.Write(blockAlign);
			writer.Write((short)16);
			writer.Write(Encoding.ASCII.GetBytes("data"));
			writer.Write(dataSize);
			writer.Write(pcmData);
			writer.Flush();
		}

		waveBytes = stream.ToArray();
		return true;
	}

	private static string FormatFloat(float value)
	{
		return value.ToString("0.###", CultureInfo.InvariantCulture);
	}

	
	[DataContract]
	// On-disk schema used for generated developer module manifests.
	private sealed class DeveloperModuleManifest
	{
		[DataMember(Order = 1, Name = "schemaVersion")]
		public int SchemaVersion { get; set; }

		[DataMember(Order = 2, Name = "moduleId")]
		public string ModuleId { get; set; } = string.Empty;

		[DataMember(Order = 3, Name = "displayName")]
		public string DisplayName { get; set; } = string.Empty;

		[DataMember(Order = 4, Name = "sirens")]
		public List<DeveloperModuleManifestEntry> Sirens { get; set; } = new List<DeveloperModuleManifestEntry>();

		[DataMember(Order = 5, Name = "vehicleEngines")]
		public List<DeveloperModuleManifestEntry> VehicleEngines { get; set; } = new List<DeveloperModuleManifestEntry>();

		[DataMember(Order = 6, Name = "ambient")]
		public List<DeveloperModuleManifestEntry> Ambient { get; set; } = new List<DeveloperModuleManifestEntry>();
	}

	[DataContract]
	// One generated module entry for a copied local audio file.
	private sealed class DeveloperModuleManifestEntry
	{
		[DataMember(Order = 1, Name = "key")]
		public string Key { get; set; } = string.Empty;

		[DataMember(Order = 2, Name = "displayName")]
		public string DisplayName { get; set; } = string.Empty;

		[DataMember(Order = 3, Name = "file")]
		public string File { get; set; } = string.Empty;

		[DataMember(Order = 4, Name = "profile")]
		public SirenSfxProfile Profile { get; set; } = SirenSfxProfile.CreateFallback();
	}

	// In-memory snapshot for one detected runtime audio source.
	private sealed class DetectedAudioEntry
	{
		public DetectedAudioEntry(string key, string prefabName, string displayName, string clipName, AudioClip clip, SirenSfxProfile profile)
		{
			Key = key;
			PrefabName = prefabName;
			DisplayName = displayName;
			ClipName = clipName;
			Clip = clip;
			Profile = profile.ClampCopy();
		}

		public string Key { get; }

		public string PrefabName { get; }

		public string DisplayName { get; }

		public string ClipName { get; }

		public AudioClip Clip { get; }

		public SirenSfxProfile Profile { get; }
	}
}










