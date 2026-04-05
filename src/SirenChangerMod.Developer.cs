using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using Colossal.IO.AssetDatabase;
using Colossal.PSI.Common;
using Game.PSI;
using Game.Prefabs.Effects;
using Game.UI.Menu;
using Game.UI.Widgets;
using UnityEngine;

namespace SirenChanger;

// Supported developer-tool audio domains.
internal enum DeveloperAudioDomain
{
	Siren,
	VehicleEngine,
	Ambient,
	Building,
	TransitAnnouncement
}

// PDX Mods access levels exposed in the developer options UI.
internal enum DeveloperModuleUploadAccessLevel
{
	Public = 0,
	Private = 1,
	Unlisted = 2
}

// PDX Mods publish strategy: new listing or update an existing listing.
internal enum DeveloperModuleUploadPublishMode
{
	CreateNew = 0,
	UpdateExisting = 1
}

// Developer-tab catalog/state for detected runtime sounds and utility actions.
public sealed partial class SirenChangerMod
{
	private const string kDetectedCopySourcePrefix = "__detected_sfx__";

	private const string kDeveloperModuleManifestFileName = "AudioSwitcherModule.json";

	private const string kDeveloperModuleDefaultDisplayName = "Audio Switcher Local Audio Pack";

	private const string kDeveloperModuleDefaultId = "audio.switcher.local.pack";

	private const string kDeveloperModuleDefaultFolderName = "AudioSwitcherLocalModule";

	private const string kDeveloperModuleDefaultVersion = "1.0.0";

	private const string kDeveloperModuleAssetContentFolderName = "content";

	private const string kDeveloperModuleUploadManifestRelativePath = "content/AudioSwitcherModule.json";

	private const string kDeveloperModuleUploadDefaultThumbnailFileName = "thumbnail.png";

	private const string kDeveloperModuleUploadDefaultChangeLog = "Uploaded via Audio Switcher module uploader.";

	private const string kDeveloperModuleUploadDefaultShortDescription = "Audio Switcher asset module package.";

	private const string kDeveloperModuleUploadDefaultRecommendedGameVersion = "1.*";

	private const int kDeveloperModuleUploadDescriptionMaxLength = 4000;

	private const int kDeveloperModuleUploadDisplayNameMaxLength = 128;

	private const int kDeveloperModuleUploadShortDescriptionMaxLength = 300;

	private const int kDeveloperModuleUploadVersionMaxLength = 64;

	private const int kDeveloperModuleUploadGameVersionMaxLength = 64;

	private const int kDeveloperModuleUploadAdditionalDependenciesMaxLength = 2000;

	private const string kDeveloperModuleUploadLegacyAssetTag = "Asset";

	private const string kDeveloperModuleUploadAssetPackTag = "AssetPack";

	private static readonly char[] s_DeveloperModuleUploadDependencySeparators = { ',', ';', '\n' };

	private static readonly string[] s_DeveloperModuleSoundSetProfileSettingsFileNames =
	{
		SirenReplacementConfig.SettingsFileName,
		VehicleEngineSettingsFileName,
		AmbientSettingsFileName,
		BuildingSettingsFileName,
		TransitAnnouncementSettingsFileName
	};

	// Official published PDX Mods ID for Audio Switcher.
	private const int kAudioSwitcherOfficialPublishedId = 135367;

	// 1x1 transparent PNG used when no custom thumbnail exists in generated module folders.
	private const string kDeveloperModuleUploadDefaultThumbnailBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+tmL0AAAAASUVORK5CYII=";

	private static readonly Dictionary<string, DetectedAudioEntry> s_DetectedSirenAudio = new Dictionary<string, DetectedAudioEntry>(StringComparer.OrdinalIgnoreCase);

	private static readonly Dictionary<string, DetectedAudioEntry> s_DetectedEngineAudio = new Dictionary<string, DetectedAudioEntry>(StringComparer.OrdinalIgnoreCase);

	private static readonly Dictionary<string, DetectedAudioEntry> s_DetectedAmbientAudio = new Dictionary<string, DetectedAudioEntry>(StringComparer.OrdinalIgnoreCase);

	private static readonly Dictionary<string, DetectedAudioEntry> s_DetectedBuildingAudio = new Dictionary<string, DetectedAudioEntry>(StringComparer.OrdinalIgnoreCase);

	private static string s_DeveloperSelectedSirenKey = string.Empty;

	private static string s_DeveloperSelectedEngineKey = string.Empty;

	private static string s_DeveloperSelectedAmbientKey = string.Empty;

	private static string s_DeveloperSelectedBuildingKey = string.Empty;

	private static string s_DeveloperSirenStatus = "No detected siren sounds are available yet. Load a map/editor session to detect sounds.";

	private static string s_DeveloperEngineStatus = "No detected vehicle engine sounds are available yet. Load a map/editor session to detect sounds.";

	private static string s_DeveloperAmbientStatus = "No detected ambient sounds are available yet. Load a map/editor session to detect sounds.";

	private static string s_DeveloperBuildingStatus = "No detected building sounds are available yet. Load a map/editor session to detect sounds.";

	private static string s_DeveloperModuleDisplayName = kDeveloperModuleDefaultDisplayName;

	private static string s_DeveloperModuleId = kDeveloperModuleDefaultId;

	private static string s_DeveloperModuleFolderName = kDeveloperModuleDefaultFolderName;

	private static string s_DeveloperModuleVersion = kDeveloperModuleDefaultVersion;

	private static string s_DeveloperModuleStatus = "Ready to create a module from local custom audio files.";

	private static string s_DeveloperModuleUploadStatus = "No asset-module upload has been run yet.";

	private static string s_DeveloperModuleExportDirectory = string.Empty;

	private static string s_DeveloperLastUploadReadyModulePath = string.Empty;

	private static string s_DeveloperModuleUploadThumbnailPath = string.Empty;

	private static string s_DeveloperModuleUploadThumbnailDirectory = string.Empty;

	private static DeveloperModuleUploadAccessLevel s_DeveloperModuleUploadAccessLevel = DeveloperModuleUploadAccessLevel.Private;

	private static DeveloperModuleUploadPublishMode s_DeveloperModuleUploadPublishMode = DeveloperModuleUploadPublishMode.CreateNew;

	private static int s_DeveloperModuleUploadExistingPublishedId;

	private static string s_DeveloperModuleUploadDescription = string.Empty;

	private static string s_DeveloperModuleUploadAdditionalDependencies = string.Empty;

	private static int s_DeveloperAudioSwitcherDependencyPublishedIdCache;

	private static readonly object s_DeveloperModuleUploadSync = new object();

	private static bool s_DeveloperModuleUploadInProgress;

	private static string s_DeveloperModuleSelectedLocalSirenKey = string.Empty;

	private static string s_DeveloperModuleSelectedLocalEngineKey = string.Empty;

	private static string s_DeveloperModuleSelectedLocalAmbientKey = string.Empty;

	private static string s_DeveloperModuleSelectedLocalBuildingKey = string.Empty;

	private static string s_DeveloperModuleSelectedLocalTransitAnnouncementKey = string.Empty;

	private static string s_DeveloperModuleSelectedSoundSetProfileId = string.Empty;

	private static readonly HashSet<string> s_DeveloperModuleIncludedSirens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private static readonly HashSet<string> s_DeveloperModuleIncludedEngines = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private static readonly HashSet<string> s_DeveloperModuleIncludedAmbient = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private static readonly HashSet<string> s_DeveloperModuleIncludedBuildings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private static readonly HashSet<string> s_DeveloperModuleIncludedTransitAnnouncements = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private static readonly HashSet<string> s_DeveloperModuleIncludedSoundSetProfiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private static bool s_DeveloperModuleIncludeInitialized;

	// Clears every detected-audio domain. Called on unload to avoid stale object references.
	internal static void ClearAllDetectedDeveloperAudio()
	{
		bool changed = false;
		changed |= ResetDetectedAudioDomainInternal(DeveloperAudioDomain.Siren);
		changed |= ResetDetectedAudioDomainInternal(DeveloperAudioDomain.VehicleEngine);
		changed |= ResetDetectedAudioDomainInternal(DeveloperAudioDomain.Ambient);
		changed |= ResetDetectedAudioDomainInternal(DeveloperAudioDomain.Building);
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
		s_DeveloperModuleDisplayName = value ?? string.Empty;
	}

	// Read/write module id field used by the Developer Module Builder section.
	internal static string GetDeveloperModuleId()
	{
		return s_DeveloperModuleId;
	}

	// Update module id while enforcing safe manifest-compatible characters (including periods).
	// Keep edge separators during live editing so users can type segmented IDs like "com.example".
	internal static void SetDeveloperModuleId(string value)
	{
		s_DeveloperModuleId = NormalizeDeveloperModuleId(value, trimEdgeSeparators: false);
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

	// Read/write module version used in generated manifests and uploads.
	internal static string GetDeveloperModuleVersion()
	{
		return s_DeveloperModuleVersion;
	}

	// Update module version while allowing only digits and periods during editing.
	internal static void SetDeveloperModuleVersion(string value)
	{
		s_DeveloperModuleVersion = NormalizeDeveloperModuleVersion(value, trimEdgePeriods: false);
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

	// Read/write PDX Mods access level used when uploading generated asset modules.
	internal static int GetDeveloperModuleUploadAccessLevel()
	{
		return (int)s_DeveloperModuleUploadAccessLevel;
	}

	// Update selected upload visibility level.
	internal static void SetDeveloperModuleUploadAccessLevel(int value)
	{
		DeveloperModuleUploadAccessLevel next = NormalizeDeveloperModuleUploadAccessLevel(value);
		if (s_DeveloperModuleUploadAccessLevel == next)
		{
			return;
		}

		s_DeveloperModuleUploadAccessLevel = next;
		OptionsVersion++;
	}

	// Read/write upload publish mode (new listing vs existing listing update).
	internal static int GetDeveloperModuleUploadPublishMode()
	{
		return (int)s_DeveloperModuleUploadPublishMode;
	}

	// Update selected upload publish mode.
	internal static void SetDeveloperModuleUploadPublishMode(int value)
	{
		DeveloperModuleUploadPublishMode next = NormalizeDeveloperModuleUploadPublishMode(value);
		if (s_DeveloperModuleUploadPublishMode == next)
		{
			return;
		}

		s_DeveloperModuleUploadPublishMode = next;
		OptionsVersion++;
	}

	// True when upload is configured to update an existing published listing.
	internal static bool IsDeveloperModuleUploadUpdateExistingEnabled()
	{
		return s_DeveloperModuleUploadPublishMode == DeveloperModuleUploadPublishMode.UpdateExisting;
	}

	// Read/write existing published ID text used when update-existing mode is active.
	internal static string GetDeveloperModuleUploadExistingPublishedIdText()
	{
		if (s_DeveloperModuleUploadExistingPublishedId <= 0)
		{
			return string.Empty;
		}

		return s_DeveloperModuleUploadExistingPublishedId.ToString(CultureInfo.InvariantCulture);
	}

	// Update existing published ID from options text input.
	internal static void SetDeveloperModuleUploadExistingPublishedIdText(string value)
	{
		int next = NormalizeDeveloperModuleUploadExistingPublishedId(value);
		if (s_DeveloperModuleUploadExistingPublishedId == next)
		{
			return;
		}

		s_DeveloperModuleUploadExistingPublishedId = next;
		OptionsVersion++;
	}

	// Read/write optional PDX page description used for module uploads.
	internal static string GetDeveloperModuleUploadDescription()
	{
		return s_DeveloperModuleUploadDescription;
	}

	// Update optional PDX page description used for module uploads.
	internal static void SetDeveloperModuleUploadDescription(string value)
	{
		s_DeveloperModuleUploadDescription = NormalizeDeveloperModuleUploadDescription(value);
	}

	// Read/write optional additional dependency IDs for uploaded modules.
	internal static string GetDeveloperModuleUploadAdditionalDependencies()
	{
		return s_DeveloperModuleUploadAdditionalDependencies;
	}

	// Update optional additional dependency IDs for uploaded modules.
	internal static void SetDeveloperModuleUploadAdditionalDependencies(string value)
	{
		string normalized = NormalizeDeveloperModuleUploadAdditionalDependencies(value);
		if (string.Equals(s_DeveloperModuleUploadAdditionalDependencies, normalized, StringComparison.Ordinal))
		{
			return;
		}

		s_DeveloperModuleUploadAdditionalDependencies = normalized;
		OptionsVersion++;
	}

	// Read currently selected upload thumbnail candidate path.
	internal static string GetDeveloperModuleUploadThumbnailPath()
	{
		string normalized = NormalizeDeveloperModuleUploadThumbnailPath(s_DeveloperModuleUploadThumbnailPath);
		if (string.IsNullOrWhiteSpace(normalized))
		{
			return string.Empty;
		}

		List<string> candidates = GetDeveloperModuleUploadThumbnailCandidates(out _, out _);
		if (!ContainsDeveloperModuleUploadThumbnailCandidate(candidates, normalized))
		{
			s_DeveloperModuleUploadThumbnailPath = string.Empty;
			return string.Empty;
		}

		return normalized;
	}

	// Update selected upload thumbnail candidate path.
	internal static void SetDeveloperModuleUploadThumbnailPath(string value)
	{
		string normalized = NormalizeDeveloperModuleUploadThumbnailPath(value);
		if (!string.IsNullOrWhiteSpace(normalized))
		{
			List<string> candidates = GetDeveloperModuleUploadThumbnailCandidates(out _, out _);
			if (!ContainsDeveloperModuleUploadThumbnailCandidate(candidates, normalized))
			{
				normalized = string.Empty;
			}
		}

		if (string.Equals(s_DeveloperModuleUploadThumbnailPath, normalized, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}

		s_DeveloperModuleUploadThumbnailPath = normalized;
		OptionsVersion++;
	}

	// Read/write optional directory used to discover upload thumbnail candidates.
	internal static string GetDeveloperModuleUploadThumbnailDirectory()
	{
		return s_DeveloperModuleUploadThumbnailDirectory;
	}

	// Update optional directory used to discover upload thumbnail candidates.
	internal static void SetDeveloperModuleUploadThumbnailDirectory(string value)
	{
		string normalized = NormalizeDeveloperModuleUploadThumbnailDirectory(value);
		if (string.Equals(s_DeveloperModuleUploadThumbnailDirectory, normalized, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}

		s_DeveloperModuleUploadThumbnailDirectory = normalized;
		List<string> candidates = GetDeveloperModuleUploadThumbnailCandidates(out _, out _);
		if (!ContainsDeveloperModuleUploadThumbnailCandidate(candidates, s_DeveloperModuleUploadThumbnailPath))
		{
			s_DeveloperModuleUploadThumbnailPath = string.Empty;
		}

		OptionsVersion++;
	}

	// Dropdown source for selectable upload thumbnail files.
	internal static DropdownItem<string>[] BuildDeveloperModuleUploadThumbnailDropdown()
	{
		List<string> candidates = GetDeveloperModuleUploadThumbnailCandidates(out string moduleRootPath, out string thumbnailDirectoryPath);
		List<DropdownItem<string>> items = new List<DropdownItem<string>>(candidates.Count + 1)
		{
			new DropdownItem<string>
			{
				value = string.Empty,
				displayName = "Auto (thumbnail.png or generated default)"
			}
		};

		for (int i = 0; i < candidates.Count; i++)
		{
			string candidate = candidates[i];
			string fileName = Path.GetFileName(candidate);
			string displayName = string.IsNullOrWhiteSpace(fileName) ? candidate : fileName;
			if (IsPathWithinDirectory(candidate, moduleRootPath))
			{
				displayName = $"{displayName} (Latest Module)";
			}
			else if (IsPathWithinDirectory(candidate, thumbnailDirectoryPath))
			{
				displayName = $"{displayName} (Thumbnail Directory)";
			}

			items.Add(new DropdownItem<string>
			{
				value = candidate,
				displayName = displayName
			});
		}

		return items.ToArray();
	}

	// Re-scan available upload thumbnail candidates and refresh options.
	internal static void RefreshDeveloperModuleUploadThumbnailOptions()
	{
		List<string> candidates = GetDeveloperModuleUploadThumbnailCandidates(out string moduleRootPath, out string thumbnailDirectoryPath);
		if (!ContainsDeveloperModuleUploadThumbnailCandidate(candidates, s_DeveloperModuleUploadThumbnailPath))
		{
			s_DeveloperModuleUploadThumbnailPath = string.Empty;
		}

		bool hasModuleSource = !string.IsNullOrWhiteSpace(moduleRootPath) && Directory.Exists(moduleRootPath);
		bool hasDirectoryConfigured = !string.IsNullOrWhiteSpace(thumbnailDirectoryPath);
		bool hasDirectorySource = !string.IsNullOrWhiteSpace(thumbnailDirectoryPath) && Directory.Exists(thumbnailDirectoryPath);
		if (!hasModuleSource && !hasDirectorySource)
		{
			if (hasDirectoryConfigured)
			{
				SetDeveloperModuleUploadStatus(
					$"Thumbnail Directory '{thumbnailDirectoryPath}' does not exist. Update the directory and try scanning again.",
					isWarning: true);
				return;
			}

			SetDeveloperModuleUploadStatus(
				"Thumbnail scan could not run because no upload package exists and no Thumbnail Directory is configured.",
				isWarning: true);
			return;
		}

		if (candidates.Count == 0)
		{
			string sourceSummary = hasModuleSource && hasDirectorySource
				? $"'{moduleRootPath}' and '{thumbnailDirectoryPath}'"
				: hasModuleSource
					? $"'{moduleRootPath}'"
					: $"'{thumbnailDirectoryPath}'";
			SetDeveloperModuleUploadStatus(
				$"Thumbnail scan found no .png/.jpg/.jpeg files in {sourceSummary}. Auto thumbnail mode will be used.",
				isWarning: true);
			return;
		}

		string selectedSourceSummary = hasModuleSource && hasDirectorySource
			? $"latest module and Thumbnail Directory ({thumbnailDirectoryPath})"
			: hasModuleSource
				? "latest module"
				: $"Thumbnail Directory ({thumbnailDirectoryPath})";
		SetDeveloperModuleUploadStatus(
			$"Thumbnail scan found {candidates.Count} selectable file(s) from {selectedSourceSummary}.",
			isWarning: false);
	}

	// Status text for last asset-module upload action.
	internal static string GetDeveloperModuleUploadStatusText()
	{
		return s_DeveloperModuleUploadStatus;
	}

	// Read-only status text that makes the active upload pipeline explicit in options.
	internal static string GetDeveloperModuleUploadModeStatusText()
	{
		return "Asset Upload Mode Active\nUploads are published through the PDX asset pipeline (not code-mod publishing).";
	}

	// True when a module asset upload is currently in progress.
	internal static bool IsDeveloperModuleUploadInProgress()
	{
		lock (s_DeveloperModuleUploadSync)
		{
			return s_DeveloperModuleUploadInProgress;
		}
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

	// Dropdown source for module-builder sound-set profile selector.
	internal static DropdownItem<string>[] BuildDeveloperModuleSoundSetProfileDropdown()
	{
		EnsureDeveloperModuleIncludeStateCurrent();
		List<string> setIds = GetEligibleDeveloperModuleSoundSetProfileIds();
		if (setIds.Count == 0)
		{
			return new[]
			{
				new DropdownItem<string>
				{
					value = string.Empty,
					displayName = "No sound set profiles found",
					disabled = true
				}
			};
		}

		List<DropdownItem<string>> items = new List<DropdownItem<string>>(setIds.Count);
		for (int i = 0; i < setIds.Count; i++)
		{
			string setId = setIds[i];
			string displayName = BuildDeveloperModuleSoundSetProfileDisplayName(setId);
			if (s_DeveloperModuleIncludedSoundSetProfiles.Contains(setId))
			{
				displayName = $"{displayName} (Included)";
			}

			items.Add(new DropdownItem<string>
			{
				value = setId,
				displayName = displayName
			});
		}

		return items.ToArray();
	}

	// Get currently selected sound-set profile in module-builder state.
	internal static string GetDeveloperModuleSoundSetProfileSelection()
	{
		EnsureDeveloperModuleIncludeStateCurrent();
		return s_DeveloperModuleSelectedSoundSetProfileId;
	}

	// Set sound-set profile selection used by module-builder include/exclude actions.
	internal static void SetDeveloperModuleSoundSetProfileSelection(string selection)
	{
		EnsureDeveloperModuleIncludeStateCurrent();
		string normalized = CitySoundProfileRegistry.NormalizeSetId(selection);
		List<string> setIds = GetEligibleDeveloperModuleSoundSetProfileIds();
		string next = string.Empty;
		if (!string.IsNullOrWhiteSpace(normalized))
		{
			for (int i = 0; i < setIds.Count; i++)
			{
				if (string.Equals(setIds[i], normalized, StringComparison.OrdinalIgnoreCase))
				{
					next = setIds[i];
					break;
				}
			}
		}

		if (string.IsNullOrWhiteSpace(next) && setIds.Count > 0)
		{
			next = setIds[0];
		}

		if (string.Equals(s_DeveloperModuleSelectedSoundSetProfileId, next, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}

		s_DeveloperModuleSelectedSoundSetProfileId = next;
		OptionsVersion++;
	}

	// Include currently selected sound-set profile in module export.
	internal static void IncludeSelectedSoundSetProfileInModule()
	{
		EnsureDeveloperModuleIncludeStateCurrent();
		string selection = CitySoundProfileRegistry.NormalizeSetId(s_DeveloperModuleSelectedSoundSetProfileId);
		if (string.IsNullOrWhiteSpace(selection))
		{
			SetDeveloperModuleStatus("No sound set profile is selected.", isWarning: true);
			return;
		}

		if (!s_DeveloperModuleIncludedSoundSetProfiles.Add(selection))
		{
			SetDeveloperModuleStatus(
				$"Sound set profile '{BuildDeveloperModuleSoundSetProfileDisplayName(selection)}' is already included.",
				isWarning: false);
			return;
		}

		List<string> coverageWarnings = BuildDeveloperModuleCurrentSoundSetProfileCoverageWarningsForSetIds(new[] { selection });
		if (coverageWarnings.Count > 0)
		{
			SetDeveloperModuleStatus(
				$"Included sound set profile '{BuildDeveloperModuleSoundSetProfileDisplayName(selection)}' for module export. Warning: profile references audio that is not included in this module.",
				isWarning: true);
			return;
		}

		SetDeveloperModuleStatus(
			$"Included sound set profile '{BuildDeveloperModuleSoundSetProfileDisplayName(selection)}' for module export.",
			isWarning: false);
	}

	// Exclude currently selected sound-set profile from module export.
	internal static void ExcludeSelectedSoundSetProfileFromModule()
	{
		EnsureDeveloperModuleIncludeStateCurrent();
		string selection = CitySoundProfileRegistry.NormalizeSetId(s_DeveloperModuleSelectedSoundSetProfileId);
		if (string.IsNullOrWhiteSpace(selection))
		{
			SetDeveloperModuleStatus("No sound set profile is selected.", isWarning: true);
			return;
		}

		if (!s_DeveloperModuleIncludedSoundSetProfiles.Remove(selection))
		{
			SetDeveloperModuleStatus(
				$"Sound set profile '{BuildDeveloperModuleSoundSetProfileDisplayName(selection)}' is already excluded.",
				isWarning: false);
			return;
		}

		SetDeveloperModuleStatus(
			$"Excluded sound set profile '{BuildDeveloperModuleSoundSetProfileDisplayName(selection)}' from module export.",
			isWarning: false);
	}

	// Include every available sound-set profile in module export.
	internal static void IncludeAllSoundSetProfilesInModule()
	{
		EnsureDeveloperModuleIncludeStateCurrent();
		List<string> setIds = GetEligibleDeveloperModuleSoundSetProfileIds();
		if (setIds.Count == 0)
		{
			SetDeveloperModuleStatus("No sound set profiles are currently available to include.", isWarning: true);
			return;
		}

		int added = 0;
		for (int i = 0; i < setIds.Count; i++)
		{
			if (s_DeveloperModuleIncludedSoundSetProfiles.Add(setIds[i]))
			{
				added++;
			}
		}

		List<string> coverageWarnings = BuildDeveloperModuleCurrentSoundSetProfileCoverageWarningsForSetIds(s_DeveloperModuleIncludedSoundSetProfiles);
		bool hasCoverageWarnings = coverageWarnings.Count > 0;
		SetDeveloperModuleStatus(
			added > 0
				? hasCoverageWarnings
					? $"Included {added} sound set profile(s). Total included profiles: {GetTotalDeveloperModuleIncludedSoundSetProfileCount()}. Warning: one or more selected sound-set profiles reference audio not included in this module."
					: $"Included {added} sound set profile(s). Total included profiles: {GetTotalDeveloperModuleIncludedSoundSetProfileCount()}."
				: hasCoverageWarnings
					? $"All available sound set profiles are already included ({GetTotalDeveloperModuleIncludedSoundSetProfileCount()}). Warning: one or more selected sound-set profiles reference audio not included in this module."
					: $"All available sound set profiles are already included ({GetTotalDeveloperModuleIncludedSoundSetProfileCount()}).",
			isWarning: hasCoverageWarnings);
	}

	// Clear all selected sound-set profiles from module export.
	internal static void ClearSoundSetProfileModuleInclusions()
	{
		EnsureDeveloperModuleIncludeStateCurrent();
		int previous = GetTotalDeveloperModuleIncludedSoundSetProfileCount();
		if (previous == 0)
		{
			SetDeveloperModuleStatus("No sound set profiles are currently included.", isWarning: false);
			return;
		}

		s_DeveloperModuleIncludedSoundSetProfiles.Clear();
		SetDeveloperModuleStatus("Cleared all included sound set profiles.", isWarning: false);
	}

	// Read-only summary of selected sound-set profiles.
	internal static string GetDeveloperModuleSoundSetProfileSummaryText()
	{
		EnsureDeveloperModuleIncludeStateCurrent();
		List<string> setIds = GetEligibleDeveloperModuleSoundSetProfileIds();
		if (setIds.Count == 0)
		{
			return "No sound set profiles are currently available.";
		}

		StringBuilder builder = new StringBuilder(256);
		builder.Append("Included: ")
			.Append(GetTotalDeveloperModuleIncludedSoundSetProfileCount())
			.Append('/')
			.Append(setIds.Count);
		for (int i = 0; i < setIds.Count; i++)
		{
			string setId = setIds[i];
			if (!s_DeveloperModuleIncludedSoundSetProfiles.Contains(setId))
			{
				continue;
			}

			builder.Append('\n').Append(" - ").Append(BuildDeveloperModuleSoundSetProfileDisplayName(setId));
		}

		List<string> coverageWarnings = BuildDeveloperModuleCurrentSoundSetProfileCoverageWarningsForSetIds(s_DeveloperModuleIncludedSoundSetProfiles);
		if (coverageWarnings.Count > 0)
		{
			builder
				.Append('\n')
				.Append("Warnings: ")
				.Append(coverageWarnings.Count)
				.Append(" selected sound-set profile reference(s) are not included in module audio.");
			int warningPreviewCount = Math.Min(coverageWarnings.Count, 10);
			for (int i = 0; i < warningPreviewCount; i++)
			{
				builder.Append('\n').Append(" ! ").Append(coverageWarnings[i]);
			}

			if (coverageWarnings.Count > warningPreviewCount)
			{
				builder.Append('\n')
					.Append(" ! ")
					.Append(coverageWarnings.Count - warningPreviewCount)
					.Append(" additional warning(s) not shown.");
			}
		}

		return builder.ToString();
	}

	// Build warning lines when included sound-set profiles reference selections not included in module audio.
	private static List<string> BuildDeveloperModuleCurrentSoundSetProfileCoverageWarningsForSetIds(IEnumerable<string> setIds)
	{
		List<DeveloperSoundSetProfileSnapshot> snapshots = BuildDeveloperSoundSetProfileSnapshotsFromLocalRegistry(setIds);
		Dictionary<DeveloperAudioDomain, HashSet<string>> includedByDomain = BuildDeveloperModuleIncludedAudioKeysByDomain();
		return BuildDeveloperSoundSetProfileCoverageWarnings(snapshots, includedByDomain);
	}

	// Capture currently selected local module keys per audio domain.
	private static Dictionary<DeveloperAudioDomain, HashSet<string>> BuildDeveloperModuleIncludedAudioKeysByDomain()
	{
		return new Dictionary<DeveloperAudioDomain, HashSet<string>>
		{
			{ DeveloperAudioDomain.Siren, new HashSet<string>(s_DeveloperModuleIncludedSirens, StringComparer.OrdinalIgnoreCase) },
			{ DeveloperAudioDomain.VehicleEngine, new HashSet<string>(s_DeveloperModuleIncludedEngines, StringComparer.OrdinalIgnoreCase) },
			{ DeveloperAudioDomain.Ambient, new HashSet<string>(s_DeveloperModuleIncludedAmbient, StringComparer.OrdinalIgnoreCase) },
			{ DeveloperAudioDomain.Building, new HashSet<string>(s_DeveloperModuleIncludedBuildings, StringComparer.OrdinalIgnoreCase) },
			{ DeveloperAudioDomain.TransitAnnouncement, new HashSet<string>(s_DeveloperModuleIncludedTransitAnnouncements, StringComparer.OrdinalIgnoreCase) }
		};
	}

	// Build set snapshots from the local sound-set registry paths.
	private static List<DeveloperSoundSetProfileSnapshot> BuildDeveloperSoundSetProfileSnapshotsFromLocalRegistry(IEnumerable<string> setIds)
	{
		List<DeveloperSoundSetProfileSnapshot> snapshots = new List<DeveloperSoundSetProfileSnapshot>();
		HashSet<string> seenSetIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (string rawSetId in setIds ?? Enumerable.Empty<string>())
		{
			string normalizedSetId = CitySoundProfileRegistry.NormalizeSetId(rawSetId);
			if (string.IsNullOrWhiteSpace(normalizedSetId) || !seenSetIds.Add(normalizedSetId))
			{
				continue;
			}

			DeveloperSoundSetProfileSnapshot snapshot = new DeveloperSoundSetProfileSnapshot
			{
				SetId = normalizedSetId,
				DisplayName = BuildDeveloperModuleSoundSetProfileDisplayName(normalizedSetId)
			};
			snapshot.SettingsPathByDomain[DeveloperAudioDomain.Siren] = CitySoundProfileRegistry.GetSetSettingsPath(
				SettingsDirectory,
				normalizedSetId,
				SirenReplacementConfig.SettingsFileName,
				ensureDirectoryExists: false);
			snapshot.SettingsPathByDomain[DeveloperAudioDomain.VehicleEngine] = CitySoundProfileRegistry.GetSetSettingsPath(
				SettingsDirectory,
				normalizedSetId,
				VehicleEngineSettingsFileName,
				ensureDirectoryExists: false);
			snapshot.SettingsPathByDomain[DeveloperAudioDomain.Ambient] = CitySoundProfileRegistry.GetSetSettingsPath(
				SettingsDirectory,
				normalizedSetId,
				AmbientSettingsFileName,
				ensureDirectoryExists: false);
			snapshot.SettingsPathByDomain[DeveloperAudioDomain.Building] = CitySoundProfileRegistry.GetSetSettingsPath(
				SettingsDirectory,
				normalizedSetId,
				BuildingSettingsFileName,
				ensureDirectoryExists: false);
			snapshot.SettingsPathByDomain[DeveloperAudioDomain.TransitAnnouncement] = CitySoundProfileRegistry.GetSetSettingsPath(
				SettingsDirectory,
				normalizedSetId,
				TransitAnnouncementSettingsFileName,
				ensureDirectoryExists: false);
			snapshots.Add(snapshot);
		}

		return snapshots;
	}

	// Compare profile selections against included module keys and return warning lines.
	private static List<string> BuildDeveloperSoundSetProfileCoverageWarnings(
		IReadOnlyList<DeveloperSoundSetProfileSnapshot> snapshots,
		IReadOnlyDictionary<DeveloperAudioDomain, HashSet<string>> includedByDomain)
	{
		List<string> warnings = new List<string>();
		if (snapshots == null || snapshots.Count == 0)
		{
			return warnings;
		}

		for (int snapshotIndex = 0; snapshotIndex < snapshots.Count; snapshotIndex++)
		{
			DeveloperSoundSetProfileSnapshot snapshot = snapshots[snapshotIndex];
			foreach (KeyValuePair<DeveloperAudioDomain, HashSet<string>> pair in includedByDomain)
			{
				DeveloperAudioDomain domain = pair.Key;
				HashSet<string> includedKeys = pair.Value;
				if (!snapshot.SettingsPathByDomain.TryGetValue(domain, out string settingsPath) ||
					string.IsNullOrWhiteSpace(settingsPath))
				{
					warnings.Add($"{snapshot.DisplayName}: {GetDeveloperModuleDomainName(domain)} settings path is missing.");
					continue;
				}

				if (!TryCollectReferencedSelectionsFromSettingsFile(domain, settingsPath, out HashSet<string> referencedSelections, out string loadError))
				{
					warnings.Add($"{snapshot.DisplayName}: {GetDeveloperModuleDomainName(domain)} settings could not be read ({loadError}).");
					continue;
				}

				if (referencedSelections.Count == 0)
				{
					continue;
				}

				List<string> missingSelections = referencedSelections
					.Where(selection => !includedKeys.Contains(selection))
					.OrderBy(selection => selection, StringComparer.OrdinalIgnoreCase)
					.ToList();
				if (missingSelections.Count == 0)
				{
					continue;
				}

				string sample = string.Join(", ", missingSelections.Take(3));
				if (missingSelections.Count > 3)
				{
					sample = $"{sample}, ...";
				}

				warnings.Add(
					$"{snapshot.DisplayName}: {GetDeveloperModuleDomainName(domain)} references {missingSelections.Count.ToString(CultureInfo.InvariantCulture)} not-included selection(s): {sample}");
			}
		}

		return warnings;
	}

	// Read one domain settings file and collect all referenced custom selection keys.
	private static bool TryCollectReferencedSelectionsFromSettingsFile(
		DeveloperAudioDomain domain,
		string settingsPath,
		out HashSet<string> selections,
		out string error)
	{
		selections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		error = string.Empty;
		if (string.IsNullOrWhiteSpace(settingsPath) || !File.Exists(settingsPath))
		{
			error = "file not found";
			return false;
		}

		try
		{
			string json = File.ReadAllText(settingsPath);
			switch (domain)
			{
				case DeveloperAudioDomain.Siren:
					if (!JsonDataSerializer.TryDeserialize(json, out SirenReplacementConfig? sirenConfig, out string sirenParseError) ||
						sirenConfig == null)
					{
						error = sirenParseError;
						return false;
					}

					sirenConfig.Normalize();
					CollectSirenReplacementSelections(sirenConfig, selections);
					return true;

				case DeveloperAudioDomain.VehicleEngine:
				case DeveloperAudioDomain.Ambient:
				case DeveloperAudioDomain.Building:
				case DeveloperAudioDomain.TransitAnnouncement:
					if (!JsonDataSerializer.TryDeserialize(json, out AudioReplacementDomainConfig? domainConfig, out string domainParseError) ||
						domainConfig == null)
					{
						error = domainParseError;
						return false;
					}

					domainConfig.Normalize(GetLocalDomainFolderName(domain));
					CollectDomainReplacementSelections(domain, domainConfig, selections);
					return true;

				default:
					error = "unsupported domain";
					return false;
			}
		}
		catch (Exception ex)
		{
			error = ex.Message;
			return false;
		}
	}

	// Collect referenced custom selections from siren config fields.
	private static void CollectSirenReplacementSelections(SirenReplacementConfig config, ISet<string> selections)
	{
		AddSelectionIfCustom(config.PoliceSirenSelectionNA, SirenReplacementConfig.IsDefaultSelection, selections);
		AddSelectionIfCustom(config.PoliceSirenSelectionEU, SirenReplacementConfig.IsDefaultSelection, selections);
		AddSelectionIfCustom(config.FireSirenSelectionNA, SirenReplacementConfig.IsDefaultSelection, selections);
		AddSelectionIfCustom(config.FireSirenSelectionEU, SirenReplacementConfig.IsDefaultSelection, selections);
		AddSelectionIfCustom(config.AmbulanceSirenSelectionNA, SirenReplacementConfig.IsDefaultSelection, selections);
		AddSelectionIfCustom(config.AmbulanceSirenSelectionEU, SirenReplacementConfig.IsDefaultSelection, selections);
		AddSelectionIfCustom(config.AlternateFallbackSelection, SirenReplacementConfig.IsDefaultSelection, selections);

		Dictionary<string, string> vehicleSelections = config.VehiclePrefabSelections ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (KeyValuePair<string, string> pair in vehicleSelections)
		{
			AddSelectionIfCustom(pair.Value, SirenReplacementConfig.IsDefaultSelection, selections);
		}
	}

	// Collect referenced custom selections from one generic domain config.
	private static void CollectDomainReplacementSelections(
		DeveloperAudioDomain domain,
		AudioReplacementDomainConfig config,
		ISet<string> selections)
	{
		AddSelectionIfCustom(config.DefaultSelection, AudioReplacementDomainConfig.IsDefaultSelection, selections);
		AddSelectionIfCustom(config.AlternateFallbackSelection, AudioReplacementDomainConfig.IsDefaultSelection, selections);

		Dictionary<string, string> targetSelections = config.TargetSelections ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (KeyValuePair<string, string> pair in targetSelections)
		{
			AddSelectionIfCustom(pair.Value, AudioReplacementDomainConfig.IsDefaultSelection, selections);
		}

		if (domain != DeveloperAudioDomain.TransitAnnouncement)
		{
			return;
		}

		Dictionary<string, string> lineSelections = config.TransitAnnouncementLineSelections ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (KeyValuePair<string, string> pair in lineSelections)
		{
			AddSelectionIfCustom(pair.Value, AudioReplacementDomainConfig.IsDefaultSelection, selections);
		}

		Dictionary<string, string> stationLineSelections = config.TransitAnnouncementStationLineSelections ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (KeyValuePair<string, string> pair in stationLineSelections)
		{
			AddSelectionIfCustom(pair.Value, AudioReplacementDomainConfig.IsDefaultSelection, selections);
		}
	}

	// Add one selection only when it resolves to a custom/module key and is not default.
	private static void AddSelectionIfCustom(string selection, Func<string?, bool> isDefaultSelection, ISet<string> destination)
	{
		if (isDefaultSelection(selection))
		{
			return;
		}

		string normalized = SirenPathUtils.NormalizeProfileKey(selection ?? string.Empty);
		if (string.IsNullOrWhiteSpace(normalized))
		{
			return;
		}

		destination.Add(normalized);
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
		List<string> buildingKeys = GetEligibleLocalModuleKeys(DeveloperAudioDomain.Building);
		List<string> transitAnnouncementKeys = GetEligibleLocalModuleKeys(DeveloperAudioDomain.TransitAnnouncement);

		int totalAvailable = sirenKeys.Count + engineKeys.Count + ambientKeys.Count + buildingKeys.Count + transitAnnouncementKeys.Count;
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

		HashSet<string> buildingIncluded = GetDeveloperModuleIncludedSet(DeveloperAudioDomain.Building);
		for (int i = 0; i < buildingKeys.Count; i++)
		{
			if (buildingIncluded.Add(buildingKeys[i]))
			{
				added++;
			}
		}

		HashSet<string> transitIncluded = GetDeveloperModuleIncludedSet(DeveloperAudioDomain.TransitAnnouncement);
		for (int i = 0; i < transitAnnouncementKeys.Count; i++)
		{
			if (transitIncluded.Add(transitAnnouncementKeys[i]))
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
		s_DeveloperModuleIncludedBuildings.Clear();
		s_DeveloperModuleIncludedTransitAnnouncements.Clear();
		SetDeveloperModuleStatus("Cleared all included local audio files.", isWarning: false);
	}

	// Read-only summary of currently included local files and per-domain counts.
	internal static string GetDeveloperModuleInclusionSummaryText()
	{
		EnsureDeveloperModuleIncludeStateCurrent();
		List<string> sirenKeys = GetEligibleLocalModuleKeys(DeveloperAudioDomain.Siren);
		List<string> engineKeys = GetEligibleLocalModuleKeys(DeveloperAudioDomain.VehicleEngine);
		List<string> ambientKeys = GetEligibleLocalModuleKeys(DeveloperAudioDomain.Ambient);
		List<string> buildingKeys = GetEligibleLocalModuleKeys(DeveloperAudioDomain.Building);
		List<string> transitAnnouncementKeys = GetEligibleLocalModuleKeys(DeveloperAudioDomain.TransitAnnouncement);

		int totalAvailable = sirenKeys.Count + engineKeys.Count + ambientKeys.Count + buildingKeys.Count + transitAnnouncementKeys.Count;
		if (totalAvailable == 0)
		{
			return "No local custom audio files are currently available.";
		}

		StringBuilder builder = new StringBuilder(512);
		builder.Append("Included: ").Append(GetTotalDeveloperModuleIncludedCount()).Append('/').Append(totalAvailable);
		AppendDeveloperModuleInclusionSummary(builder, "Sirens", sirenKeys, s_DeveloperModuleIncludedSirens);
		AppendDeveloperModuleInclusionSummary(builder, "Vehicle Engines", engineKeys, s_DeveloperModuleIncludedEngines);
		AppendDeveloperModuleInclusionSummary(builder, "Ambient Sounds", ambientKeys, s_DeveloperModuleIncludedAmbient);
		AppendDeveloperModuleInclusionSummary(builder, "Building Sounds", buildingKeys, s_DeveloperModuleIncludedBuildings);
		AppendDeveloperModuleInclusionSummary(builder, "Transit Announcements", transitAnnouncementKeys, s_DeveloperModuleIncludedTransitAnnouncements);
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

	// Build a standalone module from local custom audio files and profiles.
	internal static void CreateDeveloperModuleFromLocalAudio()
	{
		_ = CreateDeveloperModuleFromLocalAudioInternal(uploadReadyAssetPackage: false, out _);
	}

	// Build an upload-ready asset module package with manifest/audio rooted under content/.
	internal static void CreateDeveloperModuleAssetPackageFromLocalAudio()
	{
		_ = CreateDeveloperModuleFromLocalAudioInternal(uploadReadyAssetPackage: true, out _);
	}

	// Build a fresh upload-ready package and immediately upload that exact package to PDX Mods.
	internal static void BuildAndUploadDeveloperAssetModuleToPdxMods()
	{
		if (!CreateDeveloperModuleFromLocalAudioInternal(uploadReadyAssetPackage: true, out string moduleRootPath))
		{
			SetDeveloperModuleUploadStatus("Build + Upload aborted because the upload package could not be generated.", isWarning: true);
			return;
		}

		// Fire-and-forget: upload happens asynchronously and reports status via callback
#pragma warning disable CS4014
		UploadDeveloperAssetModuleToPdxMods(moduleRootPath);
#pragma warning restore CS4014
	}

	// Upload the latest generated upload-ready asset module via the PDX asset pipeline.
	internal static void UploadLatestDeveloperAssetModuleToPdxMods()
	{
		if (!TryResolveLatestUploadReadyModulePath(out string moduleRootPath, out string resolveError))
		{
			SetDeveloperModuleUploadStatus(resolveError, isWarning: true);
			return;
		}

		// Fire-and-forget: upload happens asynchronously and reports status via callback
#pragma warning disable CS4014
		UploadDeveloperAssetModuleToPdxMods(moduleRootPath);
#pragma warning restore CS4014
	}

	// Upload one upload-ready asset module path through the PDX asset pipeline.
	private static async Task UploadDeveloperAssetModuleToPdxMods(string moduleRootPath)
	{
		DeveloperModuleUploadAccessLevel accessLevel;
		DeveloperModuleUploadPublishMode publishMode;
		int existingPublishedId;
		lock (s_DeveloperModuleUploadSync)
		{
			if (s_DeveloperModuleUploadInProgress)
			{
				SetDeveloperModuleUploadStatus("An asset-module upload is already in progress.", isWarning: true);
				return;
			}

			s_DeveloperModuleUploadInProgress = true;
			accessLevel = s_DeveloperModuleUploadAccessLevel;
			publishMode = s_DeveloperModuleUploadPublishMode;
			existingPublishedId = s_DeveloperModuleUploadExistingPublishedId;
		}

		string accessLevelLabel = GetDeveloperModuleUploadAccessLevelLabel(accessLevel);
		string publishModeLabel = GetDeveloperModuleUploadPublishModeLabel(publishMode);
		string existingIdSuffix = publishMode == DeveloperModuleUploadPublishMode.UpdateExisting
			? $", existing ID '{existingPublishedId.ToString(CultureInfo.InvariantCulture)}'"
			: string.Empty;
		SetDeveloperModuleUploadStatus(
			$"Starting upload for '{moduleRootPath}' with mode '{publishModeLabel}'{existingIdSuffix} and access level '{accessLevelLabel}'.",
			isWarning: false);

		try
		{
			await UploadLatestDeveloperAssetModuleToPdxModsInternalAsync(moduleRootPath, accessLevel, publishMode, existingPublishedId);
		}
		catch (OperationCanceledException)
		{
			SetDeveloperModuleUploadStatus("Asset-module upload was cancelled.", isWarning: true);
		}
		catch (Exception ex)
		{
			Log.Error($"Asset-module upload exception: {ex}");
			SetDeveloperModuleUploadStatus($"Asset-module upload failed: {ex.Message}", isWarning: true);
		}
		finally
		{
			lock (s_DeveloperModuleUploadSync)
			{
				s_DeveloperModuleUploadInProgress = false;
			}
		}
	}

	// Shared builder for both local folder modules and upload-ready asset module packages.
	private static bool CreateDeveloperModuleFromLocalAudioInternal(bool uploadReadyAssetPackage, out string moduleRootPath)
	{
		moduleRootPath = string.Empty;
		EnsureDeveloperModuleIncludeStateCurrent();
		int selectedAudioCount = GetTotalDeveloperModuleIncludedCount();
		int selectedSoundSetProfileCount = GetTotalDeveloperModuleIncludedSoundSetProfileCount();
		if (selectedAudioCount == 0)
		{
			SetDeveloperModuleStatus(
				selectedSoundSetProfileCount > 0
					? "Profile-only modules are temporarily disabled. Include one or more local audio files before creating a module."
					: "No local audio files are selected. Include one or more local audio files before creating a module.",
				isWarning: true);
			return false;
		}

		s_DeveloperModuleDisplayName = NormalizeDeveloperModuleDisplayName(s_DeveloperModuleDisplayName);
		s_DeveloperModuleId = NormalizeDeveloperModuleId(s_DeveloperModuleId);
		s_DeveloperModuleFolderName = NormalizeDeveloperModuleFolderName(s_DeveloperModuleFolderName);
		s_DeveloperModuleVersion = NormalizeDeveloperModuleVersion(s_DeveloperModuleVersion);

		string displayName = s_DeveloperModuleDisplayName;
		string moduleId = s_DeveloperModuleId;
		string moduleFolderName = s_DeveloperModuleFolderName;
		string moduleVersion = s_DeveloperModuleVersion;

		try
		{
			string exportRoot = GetResolvedDeveloperModuleExportDirectory(ensureExists: true);
			if (string.IsNullOrWhiteSpace(exportRoot))
			{
				SetDeveloperModuleStatus("Unable to resolve the module export directory.", isWarning: true);
				return false;
			}

			moduleRootPath = BuildUniqueModuleDirectoryPath(exportRoot, moduleFolderName);
			Directory.CreateDirectory(moduleRootPath);
			string moduleContentRootPath = uploadReadyAssetPackage
				? Path.Combine(moduleRootPath, kDeveloperModuleAssetContentFolderName)
				: moduleRootPath;
			Directory.CreateDirectory(moduleContentRootPath);
			if (selectedSoundSetProfileCount > 0)
			{
				// Persist in-memory edits before exporting sound-set profile snapshots.
				SaveConfig();
			}

			int skippedMissing = 0;
			int skippedUnsupported = 0;
			int skippedModuleSelections = 0;
			int skippedMissingProfileSettings = 0;
			int skippedProfileCopyFailures = 0;

			List<DeveloperModuleManifestEntry> sirenEntries = ExportLocalProfilesToModule(
				Config.CustomSirenProfiles,
				Config.CustomSirensFolderName,
				"Audio/Sirens",
				moduleContentRootPath,
				s_DeveloperModuleIncludedSirens,
				ref skippedMissing,
				ref skippedUnsupported,
				ref skippedModuleSelections);

			List<DeveloperModuleManifestEntry> engineEntries = ExportLocalProfilesToModule(
				VehicleEngineConfig.CustomProfiles,
				VehicleEngineConfig.CustomFolderName,
				"Audio/Engines",
				moduleContentRootPath,
				s_DeveloperModuleIncludedEngines,
				ref skippedMissing,
				ref skippedUnsupported,
				ref skippedModuleSelections);

			List<DeveloperModuleManifestEntry> ambientEntries = ExportLocalProfilesToModule(
				AmbientConfig.CustomProfiles,
				AmbientConfig.CustomFolderName,
				"Audio/Ambient",
				moduleContentRootPath,
				s_DeveloperModuleIncludedAmbient,
				ref skippedMissing,
				ref skippedUnsupported,
				ref skippedModuleSelections);

			List<DeveloperModuleManifestEntry> buildingEntries = ExportLocalProfilesToModule(
				BuildingConfig.CustomProfiles,
				BuildingConfig.CustomFolderName,
				"Audio/Buildings",
				moduleContentRootPath,
				s_DeveloperModuleIncludedBuildings,
				ref skippedMissing,
				ref skippedUnsupported,
				ref skippedModuleSelections);

			List<DeveloperModuleManifestEntry> transitAnnouncementEntries = ExportLocalProfilesToModule(
				TransitAnnouncementConfig.CustomProfiles,
				TransitAnnouncementConfig.CustomFolderName,
				"Audio/TransitAnnouncements",
				moduleContentRootPath,
				s_DeveloperModuleIncludedTransitAnnouncements,
				ref skippedMissing,
				ref skippedUnsupported,
				ref skippedModuleSelections);

			List<DeveloperModuleManifestSoundSetProfile> soundSetProfiles = ExportSelectedSoundSetProfilesToModule(
				moduleContentRootPath,
				s_DeveloperModuleIncludedSoundSetProfiles,
				ref skippedMissingProfileSettings,
				ref skippedProfileCopyFailures);
			List<string> soundSetCoverageWarnings = BuildDeveloperModuleCurrentSoundSetProfileCoverageWarningsForSetIds(
				s_DeveloperModuleIncludedSoundSetProfiles);

			int totalAudioExported = sirenEntries.Count + engineEntries.Count + ambientEntries.Count + buildingEntries.Count + transitAnnouncementEntries.Count;
			if (totalAudioExported == 0)
			{
				TryDeleteDirectory(moduleRootPath);
				moduleRootPath = string.Empty;
				SetDeveloperModuleStatus(
					$"No selected local audio files were eligible for generation. Skipped audio missing: {skippedMissing}, audio unsupported: {skippedUnsupported}, audio module-based selections: {skippedModuleSelections}. Profile-only modules are temporarily disabled (profile missing/copy failures: {skippedMissingProfileSettings}/{skippedProfileCopyFailures}).",
					isWarning: true);
				return false;
			}

			DeveloperModuleManifest manifest = new DeveloperModuleManifest
			{
				SchemaVersion = 1,
				ModuleId = moduleId,
				DisplayName = displayName,
				Version = moduleVersion,
				Sirens = sirenEntries,
				VehicleEngines = engineEntries,
				Ambient = ambientEntries,
				Buildings = buildingEntries,
				TransitAnnouncements = transitAnnouncementEntries,
				SoundSetProfiles = soundSetProfiles
			};

			string manifestPath = Path.Combine(moduleContentRootPath, kDeveloperModuleManifestFileName);
			string manifestJson = JsonDataSerializer.Serialize(manifest);
			File.WriteAllText(manifestPath, manifestJson, new UTF8Encoding(false));
			WriteDeveloperModuleReadme(
				moduleRootPath,
				displayName,
				moduleId,
				moduleVersion,
				sirenEntries.Count,
				engineEntries.Count,
				ambientEntries.Count,
				buildingEntries.Count,
				transitAnnouncementEntries.Count,
				soundSetProfiles.Count,
				uploadReadyAssetPackage);
			string manifestRelativePath = Path.GetRelativePath(moduleRootPath, manifestPath).Replace('\\', '/');

			string statusMessage = uploadReadyAssetPackage
				? $"Created upload-ready asset module '{displayName}' at '{moduleRootPath}'. Version: {moduleVersion}. Manifest: {manifestRelativePath}. Sirens: {sirenEntries.Count}, Engines: {engineEntries.Count}, Ambient: {ambientEntries.Count}, Buildings: {buildingEntries.Count}, Transit: {transitAnnouncementEntries.Count}, Sound Set Profiles: {soundSetProfiles.Count}. Skipped audio missing/unsupported/module: {skippedMissing}/{skippedUnsupported}/{skippedModuleSelections}. Skipped profile missing/copy failures: {skippedMissingProfileSettings}/{skippedProfileCopyFailures}. Coverage warnings: {soundSetCoverageWarnings.Count}."
				: $"Created local module (legacy layout) '{displayName}' at '{moduleRootPath}'. Version: {moduleVersion}. Sirens: {sirenEntries.Count}, Engines: {engineEntries.Count}, Ambient: {ambientEntries.Count}, Buildings: {buildingEntries.Count}, Transit: {transitAnnouncementEntries.Count}, Sound Set Profiles: {soundSetProfiles.Count}. Skipped audio missing/unsupported/module: {skippedMissing}/{skippedUnsupported}/{skippedModuleSelections}. Skipped profile missing/copy failures: {skippedMissingProfileSettings}/{skippedProfileCopyFailures}. Coverage warnings: {soundSetCoverageWarnings.Count}.";
			if (soundSetCoverageWarnings.Count > 0)
			{
				statusMessage = $"{statusMessage} {string.Join(" | ", soundSetCoverageWarnings)}";
			}

			SetDeveloperModuleStatus(statusMessage, isWarning: soundSetCoverageWarnings.Count > 0);
			if (uploadReadyAssetPackage)
			{
				s_DeveloperLastUploadReadyModulePath = moduleRootPath;
				SetDeveloperModuleUploadStatus($"Ready to upload: '{moduleRootPath}'.", isWarning: false);
			}

			SyncCustomSirenCatalog(saveIfChanged: true, forceStatusRefresh: true);
			SyncCustomVehicleEngineCatalog(saveIfChanged: true, forceStatusRefresh: true);
			SyncCustomAmbientCatalog(saveIfChanged: true, forceStatusRefresh: true);
			SyncCustomBuildingCatalog(saveIfChanged: true, forceStatusRefresh: true);
			SyncCustomTransitAnnouncementCatalog(saveIfChanged: true, forceStatusRefresh: true);
			return true;
		}
		catch (Exception ex)
		{
			moduleRootPath = string.Empty;
			SetDeveloperModuleStatus($"Module generation failed: {ex.Message}", isWarning: true);
			return false;
		}
	}

	// Upload one generated asset-module package through the in-game PDX asset-upload flow.
	private static async Task UploadLatestDeveloperAssetModuleToPdxModsInternalAsync(
		string moduleRootPath,
		DeveloperModuleUploadAccessLevel accessLevel,
		DeveloperModuleUploadPublishMode publishMode,
		int existingPublishedId)
	{
		string resolvedModuleRoot = string.IsNullOrWhiteSpace(moduleRootPath) 
			? string.Empty 
			: Path.GetFullPath(moduleRootPath);
		if (string.IsNullOrWhiteSpace(resolvedModuleRoot) || !Directory.Exists(resolvedModuleRoot))
		{
			SetDeveloperModuleUploadStatus("Upload failed because the selected module folder no longer exists.", isWarning: true);
			return;
		}

		if (publishMode == DeveloperModuleUploadPublishMode.UpdateExisting && existingPublishedId <= 0)
		{
			SetDeveloperModuleUploadStatus(
				"Upload failed because Update Existing mode requires a valid existing published Mod ID.",
				isWarning: true);
			return;
		}

		string manifestPath = Path.Combine(
			resolvedModuleRoot,
			kDeveloperModuleUploadManifestRelativePath.Replace('/', Path.DirectorySeparatorChar));
		if (!File.Exists(manifestPath))
		{
			SetDeveloperModuleUploadStatus(
				$"Upload failed because '{kDeveloperModuleUploadManifestRelativePath}' was not found in '{resolvedModuleRoot}'.",
				isWarning: true);
			return;
		}

		if (!TryBuildDeveloperModuleUploadMetadata(
				resolvedModuleRoot,
				manifestPath,
				out DeveloperModuleUploadMetadata metadata,
				out string metadataError))
		{
			SetDeveloperModuleUploadStatus(metadataError, isWarning: true);
			return;
		}

		string sourceContentPath = Path.Combine(resolvedModuleRoot, kDeveloperModuleAssetContentFolderName);
		if (!Directory.Exists(sourceContentPath))
		{
			SetDeveloperModuleUploadStatus(
				$"Upload failed because '{kDeveloperModuleAssetContentFolderName}' was not found in '{resolvedModuleRoot}'.",
				isWarning: true);
			return;
		}

		// Validate content size before upload to catch limits early
		long totalContentSizeBytes = 0;
		try
		{
			var dirInfo = new DirectoryInfo(sourceContentPath);
			totalContentSizeBytes = dirInfo.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
			const long MaxContentSizeBytes = 500 * 1024 * 1024; // 500 MB
			if (totalContentSizeBytes > MaxContentSizeBytes)
			{
				SetDeveloperModuleUploadStatus(
					$"Upload failed: content size ({(totalContentSizeBytes / (1024.0 * 1024.0)):F1} MB) exceeds maximum limit (500 MB).",
					isWarning: true);
				return;
			}
		}
		catch (Exception sizeCheckEx)
		{
			Log.Warn($"Could not validate content size: {sizeCheckEx.Message}");
		}

		PdxAssetUploadHandle uploadHandle = new PdxAssetUploadHandle();
		try
		{
			ConfigureDeveloperModuleAssetUploadHandle(uploadHandle, metadata, accessLevel, publishMode, existingPublishedId);

			(bool preflightSuccess, string preflightError) = await TryRunDeveloperModuleUploadPreflightChecksAsync(uploadHandle);
			if (!preflightSuccess)
			{
				SetDeveloperModuleUploadStatus(preflightError, isWarning: true);
				return;
			}

			if (publishMode == DeveloperModuleUploadPublishMode.UpdateExisting)
			{
				SetDeveloperModuleUploadStatus(
					$"Validating ownership and type for existing Mod ID '{existingPublishedId.ToString(CultureInfo.InvariantCulture)}'...",
					isWarning: false);
				(bool targetOk, string targetError) = await TryValidateDeveloperModuleUploadUpdateTargetAsync(uploadHandle, existingPublishedId);
				if (!targetOk)
				{
					SetDeveloperModuleUploadStatus(targetError, isWarning: true);
					return;
				}
			}

			SetDeveloperModuleUploadStatus("Creating upload staging folder on PDX Mods...", isWarning: false);
			IModsUploadSupport.ModOperationResult beginResult = await BeginDeveloperModuleUploadRegistrationAsync(uploadHandle);
			if (!beginResult.m_Success)
			{
				LogDeveloperModuleUploadFailureDiagnostics("registration", beginResult, uploadHandle);
				await TryCleanupDeveloperModuleUploadHandleAsync(uploadHandle, "Failed to cleanup upload after registration error");
				SetDeveloperModuleUploadStatus(
					BuildDeveloperModuleUploadFailureMessage("Asset upload registration failed.", beginResult),
					isWarning: true);
				return;
			}

			NormalizeDeveloperModuleUploadTags(uploadHandle);
			NormalizeDeveloperModuleUploadExternalLinks(uploadHandle);
			await TryPrimeAudioSwitcherDependencyPublishedIdCacheFromPlatformListingsAsync(uploadHandle);
			if (!TryAttachAudioSwitcherUploadDependency(uploadHandle, out string dependencyError))
			{
				SetDeveloperModuleUploadStatus($"Asset upload failed while applying dependency metadata: {dependencyError}", isWarning: true);
				await TryCleanupDeveloperModuleUploadHandleAsync(uploadHandle, "Failed to cleanup staged upload after dependency error");
				return;
			}

			if (!TryCopyDirectoryContents(sourceContentPath, uploadHandle.GetAbsoluteContentPath(), out string copyError))
			{
				SetDeveloperModuleUploadStatus($"Asset upload failed while staging content: {copyError}", isWarning: true);
				await TryCleanupDeveloperModuleUploadHandleAsync(uploadHandle, "Failed to cleanup staged asset upload content");
				return;
			}

			if (!TryStageDeveloperModuleUploadThumbnail(uploadHandle, metadata.ThumbnailPath, out string thumbnailError))
			{
				SetDeveloperModuleUploadStatus($"Asset upload failed while staging thumbnail: {thumbnailError}", isWarning: true);
				await TryCleanupDeveloperModuleUploadHandleAsync(uploadHandle, "Failed to cleanup staged upload after thumbnail error");
				return;
			}

			if (!TryRunDeveloperModuleUploadPublishPreflightChecks(uploadHandle, out string publishPreflightError))
			{
				SetDeveloperModuleUploadStatus($"Asset upload preflight failed before publish: {publishPreflightError}", isWarning: true);
				await TryCleanupDeveloperModuleUploadHandleAsync(uploadHandle, "Failed to cleanup staged upload after publish preflight error");
				return;
			}

			SetDeveloperModuleUploadStatus("Publishing asset upload to PDX Mods...", isWarning: false);
			IModsUploadSupport.ModOperationResult finalizeResult = await uploadHandle.FinalizeSubmit();
			if (!finalizeResult.m_Success)
			{
				LogDeveloperModuleUploadFailureDiagnostics("publish", finalizeResult, uploadHandle);
				await TryCleanupDeveloperModuleUploadHandleAsync(uploadHandle, "Failed to cleanup upload after publish error");
				SetDeveloperModuleUploadStatus(
					BuildDeveloperModuleUploadFailureMessage("Asset upload publish failed.", finalizeResult),
					isWarning: true);
				return;
			}

			int publishedId = finalizeResult.m_ModInfo.m_PublishedID;
			if (publishedId > 0)
			{
				lock (s_DeveloperModuleUploadSync)
				{
					s_DeveloperModuleUploadExistingPublishedId = publishedId;
				}
			}

			string modIdText = publishedId > 0
				? publishedId.ToString(CultureInfo.InvariantCulture)
				: "unknown";
			string publishModeLabel = GetDeveloperModuleUploadPublishModeLabel(publishMode);
			SetDeveloperModuleUploadStatus(
				$"Asset upload completed for '{metadata.DisplayName}'. Mode: {publishModeLabel}. Mod ID: {modIdText}. Access: {GetDeveloperModuleUploadAccessLevelLabel(accessLevel)}.",
				isWarning: false);
		}
		catch (Exception ex)
		{
			await TryCleanupDeveloperModuleUploadHandleAsync(uploadHandle, "Failed to cleanup asset upload after exception");
			SetDeveloperModuleUploadStatus($"Asset upload failed: {ex.Message}", isWarning: true);
		}
	}

	// Register upload staging folder directly through PDX SDK to avoid built-in asset packaging.
	private static async Task<IModsUploadSupport.ModOperationResult> BeginDeveloperModuleUploadRegistrationAsync(PdxAssetUploadHandle uploadHandle)
	{
		if (uploadHandle == null)
		{
			return BuildDeveloperModuleUploadRegistrationErrorResult("Upload registration failed: upload handle is unavailable.");
		}

		try
		{
			FieldInfo? managerField = uploadHandle
				.GetType()
				.GetField("m_Manager", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			object? manager = managerField?.GetValue(uploadHandle);
			if (manager == null)
			{
				string managerTypeName = uploadHandle.GetType().FullName ?? "<unknown>";
				return BuildDeveloperModuleUploadRegistrationErrorResult(
					$"Upload registration failed: PDX manager is unavailable. Handle type: {managerTypeName}",
					uploadHandle.modInfo);
			}

			MethodInfo? getNewFolderMethod = manager
				.GetType()
				.GetMethod(
					"GetNewModUploadFolder",
					BindingFlags.Instance | BindingFlags.Public,
					null,
					new[] { typeof(IModsUploadSupport.ModInfo) },
					null);
			if (getNewFolderMethod == null)
			{
				string managerType = manager.GetType().FullName ?? "<unknown>";
				return BuildDeveloperModuleUploadRegistrationErrorResult(
					$"Upload registration failed: PDX Mods API incompatible. Could not resolve GetNewModUploadFolder on manager type {managerType}. This may indicate a game/SDK version mismatch.",
					uploadHandle.modInfo);
			}

			object? invocationResult = getNewFolderMethod.Invoke(manager, new object?[] { uploadHandle.modInfo });
			if (!(invocationResult is Task registrationTask))
			{
				return BuildDeveloperModuleUploadRegistrationErrorResult("Upload registration failed: GetNewModUploadFolder returned an unexpected result.", uploadHandle.modInfo);
			}

			await registrationTask;
			PropertyInfo? resultProperty = registrationTask
				.GetType()
				.GetProperty("Result", BindingFlags.Instance | BindingFlags.Public);
			object? resultObject = resultProperty?.GetValue(registrationTask, null);
			if (!(resultObject is IModsUploadSupport.ModOperationResult operationResult))
			{
				return BuildDeveloperModuleUploadRegistrationErrorResult("Upload registration failed: GetNewModUploadFolder did not provide a valid operation result.", uploadHandle.modInfo);
			}

			uploadHandle.modInfo = operationResult.m_ModInfo;
			return operationResult;
		}
		catch (TargetInvocationException tie) when (tie.InnerException != null)
		{
			return BuildDeveloperModuleUploadRegistrationErrorResult(
				$"Upload registration failed: {tie.InnerException.Message}",
				uploadHandle.modInfo);
		}
		catch (Exception ex)
		{
			return BuildDeveloperModuleUploadRegistrationErrorResult(
				$"Upload registration failed: {ex.Message}",
				uploadHandle.modInfo);
		}
	}

	// Build a failed registration result with one concise error message.
	private static IModsUploadSupport.ModOperationResult BuildDeveloperModuleUploadRegistrationErrorResult(
		string message,
		IModsUploadSupport.ModInfo? modInfo = null)
	{
		IModsUploadSupport.ModOperationResult result = new IModsUploadSupport.ModOperationResult
		{
			m_Success = false,
			m_ModInfo = modInfo ?? default
		};
		result.m_Error = new IModsUploadSupport.ModError
		{
			m_Details = (message ?? string.Empty).Trim()
		};
		return result;
	}

	// Validate upload prerequisites before registering a PDX Mods asset upload.
	private static async Task<(bool Success, string Error)> TryRunDeveloperModuleUploadPreflightChecksAsync(PdxAssetUploadHandle uploadHandle)
	{
		PlatformManager? platformManager = PlatformManager.instance;
		if (platformManager == null)
		{
			return (false, "Upload preflight failed: platform services are not initialized.");
		}

		if (!platformManager.hasConnectivity)
		{
			return (false, "Upload preflight failed: no internet connection is available.");
		}

		if (platformManager.isOfflineOnly)
		{
			return (false, "Upload preflight failed: platform sharing is in offline-only mode.");
		}

		if (!uploadHandle.LoggedIn())
		{
			return (false, "Upload preflight failed: sign in to your Paradox account before uploading.");
		}

		bool platformDataSynced = false;
		try
		{
			SetDeveloperModuleUploadStatus("Running upload preflight checks...", isWarning: false);
			await uploadHandle.SyncPlatformData();
			platformDataSynced = true;
		}
		catch (Exception ex)
		{
			string syncErrorDetail = ex.Message;
			if (ex.InnerException != null && !string.IsNullOrWhiteSpace(ex.InnerException.Message))
			{
				syncErrorDetail = $"{syncErrorDetail} | Inner: {ex.InnerException.Message}";
			}

			Log.Warn($"Upload preflight: SyncPlatformData failed; continuing with reduced checks. {syncErrorDetail}");
			SetDeveloperModuleUploadStatus(
				$"Upload preflight warning: platform metadata sync failed ({ex.Message}). Continuing with reduced checks.",
				isWarning: true);
		}

		if (platformDataSynced)
		{
			string socialProfileName = (uploadHandle.socialProfile.m_Name ?? string.Empty).Trim();
			if (string.IsNullOrWhiteSpace(socialProfileName))
			{
				return (false, "Upload preflight failed: no PDX Mods social profile found. Complete your profile and retry.");
			}
		}

		return (true, string.Empty);
	}

	// Validate that Update Existing targets one author-owned asset listing and not the base Audio Switcher listing.
	private static async Task<(bool Success, string Error)> TryValidateDeveloperModuleUploadUpdateTargetAsync(
		PdxAssetUploadHandle uploadHandle,
		int existingPublishedId)
	{
		if (uploadHandle == null)
		{
			return (false, "Upload preflight failed: upload handle is unavailable.");
		}

		if (existingPublishedId <= 0)
		{
			return (false, "Upload failed: Update Existing requires a valid existing published Mod ID.");
		}

		List<IModsUploadSupport.ModInfo> authorMods = await CollectDeveloperModuleAuthorModsAsync(uploadHandle);
		if (authorMods.Count == 0)
		{
			return (
				false,
				$"Upload failed: unable to verify ownership for existing Mod ID '{existingPublishedId.ToString(CultureInfo.InvariantCulture)}'. " +
				"Refresh platform data and retry.");
		}

		bool foundTarget = false;
		IModsUploadSupport.ModInfo targetModInfo = default;
		for (int i = 0; i < authorMods.Count; i++)
		{
			IModsUploadSupport.ModInfo candidate = authorMods[i];
			if (candidate.m_PublishedID != existingPublishedId)
			{
				continue;
			}

			targetModInfo = candidate;
			foundTarget = true;
			break;
		}

		if (!foundTarget)
		{
			return (
				false,
				$"Upload failed: existing Mod ID '{existingPublishedId.ToString(CultureInfo.InvariantCulture)}' was not found in your PDX Mods account listings.");
		}

		string targetDisplayName = (targetModInfo.m_DisplayName ?? string.Empty).Trim();
		if (existingPublishedId == kAudioSwitcherOfficialPublishedId ||
			string.Equals(targetDisplayName, kOptionsPanelDisplayName, StringComparison.OrdinalIgnoreCase))
		{
			return (
				false,
				$"Upload failed: existing Mod ID '{existingPublishedId.ToString(CultureInfo.InvariantCulture)}' points to the base Audio Switcher listing. " +
				"Select a separate module asset listing.");
		}

		string[] targetTags = targetModInfo.m_Tags ?? Array.Empty<string>();
		if (targetTags.Length > 0)
		{
			bool hasAssetPackTag = false;
			for (int i = 0; i < targetTags.Length; i++)
			{
				string tag = (targetTags[i] ?? string.Empty).Trim();
				if (string.Equals(tag, kDeveloperModuleUploadAssetPackTag, StringComparison.OrdinalIgnoreCase) ||
					string.Equals(tag, kDeveloperModuleUploadLegacyAssetTag, StringComparison.OrdinalIgnoreCase))
				{
					hasAssetPackTag = true;
					break;
				}
			}

			if (!hasAssetPackTag)
			{
				return (
					false,
					$"Upload failed: existing Mod ID '{existingPublishedId.ToString(CultureInfo.InvariantCulture)}' does not appear to be an asset module listing (missing AssetPack tag).");
			}
		}
		else
		{
			Log.Warn(
				$"Update Existing target validation for ID {existingPublishedId.ToString(CultureInfo.InvariantCulture)} returned no tags; proceeding with ownership-only verification.");
		}

		string displayLabel = string.IsNullOrWhiteSpace(targetDisplayName) ? "<unnamed>" : targetDisplayName;
		Log.Info(
			$"Validated Update Existing target. ID={existingPublishedId.ToString(CultureInfo.InvariantCulture)}, Display='{displayLabel}'.");
		return (true, string.Empty);
	}

	// Collect author-owned mod listings from upload handle state, then fall back to ListAllModsByMe.
	private static async Task<List<IModsUploadSupport.ModInfo>> CollectDeveloperModuleAuthorModsAsync(PdxAssetUploadHandle uploadHandle)
	{
		List<IModsUploadSupport.ModInfo> authorMods = new List<IModsUploadSupport.ModInfo>();
		HashSet<int> seenPublishedIds = new HashSet<int>();

		try
		{
			IReadOnlyList<IModsUploadSupport.ModInfo>? existingAuthorMods = uploadHandle.authorMods;
			if (existingAuthorMods != null)
			{
				for (int i = 0; i < existingAuthorMods.Count; i++)
				{
					IModsUploadSupport.ModInfo modInfo = existingAuthorMods[i];
					int publishedId = modInfo.m_PublishedID;
					if (publishedId <= 0 || !seenPublishedIds.Add(publishedId))
					{
						continue;
					}

					authorMods.Add(modInfo);
				}
			}
		}
		catch (Exception ex)
		{
			Log.Warn($"Failed reading author mods from upload handle: {ex.Message}");
		}

		if (authorMods.Count > 0)
		{
			return authorMods;
		}

		try
		{
			FieldInfo? managerField = uploadHandle
				.GetType()
				.GetField("m_Manager", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			object? manager = managerField?.GetValue(uploadHandle);
			if (manager == null)
			{
				return authorMods;
			}

			MethodInfo? listMethod = manager
				.GetType()
				.GetMethod("ListAllModsByMe", BindingFlags.Instance | BindingFlags.Public);
			if (listMethod == null)
			{
				return authorMods;
			}

			object? invocationResult = InvokeListAllModsByMeForDependencyCache(manager, listMethod);
			if (!(invocationResult is Task listTask))
			{
				return authorMods;
			}

			await listTask;
			PropertyInfo? resultProperty = listTask.GetType().GetProperty("Result", BindingFlags.Instance | BindingFlags.Public);
			object? resultObject = resultProperty?.GetValue(listTask, null);
			if (!(resultObject is IEnumerable resultEnumerable))
			{
				return authorMods;
			}

			foreach (object? item in resultEnumerable)
			{
				if (!(item is IModsUploadSupport.ModInfo modInfo))
				{
					continue;
				}

				int publishedId = modInfo.m_PublishedID;
				if (publishedId <= 0 || !seenPublishedIds.Add(publishedId))
				{
					continue;
				}

				authorMods.Add(modInfo);
			}
		}
		catch (Exception ex)
		{
			Log.Warn($"Failed to collect author mod listings for Update Existing validation: {ex.Message}");
		}

		return authorMods;
	}

	// Best-effort cleanup helper for failed upload sessions.
	private static async Task TryCleanupDeveloperModuleUploadHandleAsync(PdxAssetUploadHandle uploadHandle, string contextMessage)
	{
		try
		{
			MethodInfo? cleanupMethod = uploadHandle
				.GetType()
				.GetMethod("Cleanup", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (cleanupMethod == null)
			{
				return;
			}

			object? result = cleanupMethod.Invoke(uploadHandle, null);
			if (result is Task cleanupTask)
			{
				await cleanupTask;
			}
		}
		catch (Exception cleanupEx)
		{
			Log.Warn($"{contextMessage}: {cleanupEx.Message}");
		}
	}

	// Validate staged upload payload before calling FinalizeSubmit.
	private static bool TryRunDeveloperModuleUploadPublishPreflightChecks(PdxAssetUploadHandle uploadHandle, out string error)
	{
		error = string.Empty;
		if (uploadHandle == null)
		{
			error = "Upload handle is unavailable.";
			return false;
		}

		IModsUploadSupport.ModInfo modInfo = uploadHandle.modInfo;
		if (string.IsNullOrWhiteSpace(modInfo.m_DisplayName))
		{
			error = "Display Name is missing.";
			return false;
		}
		modInfo.m_DisplayName = modInfo.m_DisplayName.Trim();
		if (modInfo.m_DisplayName.Length > kDeveloperModuleUploadDisplayNameMaxLength)
		{
			error = $"Display Name exceeds {kDeveloperModuleUploadDisplayNameMaxLength.ToString(CultureInfo.InvariantCulture)} characters.";
			return false;
		}

		if (string.IsNullOrWhiteSpace(modInfo.m_ShortDescription))
		{
			error = "Short Description is missing.";
			return false;
		}
		modInfo.m_ShortDescription = modInfo.m_ShortDescription.Trim();
		if (modInfo.m_ShortDescription.Length > kDeveloperModuleUploadShortDescriptionMaxLength)
		{
			error = $"Short Description exceeds {kDeveloperModuleUploadShortDescriptionMaxLength.ToString(CultureInfo.InvariantCulture)} characters.";
			return false;
		}

		if (string.IsNullOrWhiteSpace(modInfo.m_LongDescription))
		{
			error = "Long Description is missing.";
			return false;
		}
		modInfo.m_LongDescription = NormalizeDeveloperModuleUploadDescription(modInfo.m_LongDescription);
		if (modInfo.m_LongDescription.Length > kDeveloperModuleUploadDescriptionMaxLength)
		{
			error = $"Long Description exceeds {kDeveloperModuleUploadDescriptionMaxLength.ToString(CultureInfo.InvariantCulture)} characters.";
			return false;
		}

		if (string.IsNullOrWhiteSpace(modInfo.m_UserModVersion))
		{
			error = "Mod Version is missing.";
			return false;
		}
		modInfo.m_UserModVersion = NormalizeDeveloperModuleVersion(modInfo.m_UserModVersion);
		if (modInfo.m_UserModVersion.Length > kDeveloperModuleUploadVersionMaxLength)
		{
			error = $"Mod Version exceeds {kDeveloperModuleUploadVersionMaxLength.ToString(CultureInfo.InvariantCulture)} characters.";
			return false;
		}

		if (string.IsNullOrWhiteSpace(modInfo.m_RecommendedGameVersion))
		{
			error = "Recommended Game Version is missing.";
			return false;
		}
		modInfo.m_RecommendedGameVersion = modInfo.m_RecommendedGameVersion.Trim();
		if (modInfo.m_RecommendedGameVersion.Length > kDeveloperModuleUploadGameVersionMaxLength)
		{
			error = $"Recommended Game Version exceeds {kDeveloperModuleUploadGameVersionMaxLength.ToString(CultureInfo.InvariantCulture)} characters.";
			return false;
		}

		string[] tags = modInfo.m_Tags ?? Array.Empty<string>();
		if (tags.Length == 0)
		{
			error = "At least one publish tag is required (expected AssetPack).";
			return false;
		}

		bool hasAssetPackTag = false;
		for (int i = 0; i < tags.Length; i++)
		{
			string tag = (tags[i] ?? string.Empty).Trim();
			if (string.IsNullOrWhiteSpace(tag))
			{
				error = $"Publish tag at index {i} is empty.";
				return false;
			}

			if (string.Equals(tag, kDeveloperModuleUploadLegacyAssetTag, StringComparison.OrdinalIgnoreCase))
			{
				error = $"Invalid legacy tag '{kDeveloperModuleUploadLegacyAssetTag}' detected. Use '{kDeveloperModuleUploadAssetPackTag}' instead.";
				return false;
			}

			if (string.Equals(tag, kDeveloperModuleUploadAssetPackTag, StringComparison.OrdinalIgnoreCase))
			{
				hasAssetPackTag = true;
			}
		}

		if (!hasAssetPackTag)
		{
			error = $"Required upload tag '{kDeveloperModuleUploadAssetPackTag}' is missing.";
			return false;
		}

		uploadHandle.modInfo = modInfo;

		IModsUploadSupport.ModInfo.ModDependency[] dependencies = modInfo.m_ModDependencies ??
			Array.Empty<IModsUploadSupport.ModInfo.ModDependency>();
		if (dependencies.Length == 0)
		{
			error = "Audio Switcher dependency metadata is missing.";
			return false;
		}

		int expectedAudioSwitcherDependencyId = ResolveAudioSwitcherDependencyPublishedId(uploadHandle.authorMods);
		bool hasExpectedAudioSwitcherDependency = false;
		for (int i = 0; i < dependencies.Length; i++)
		{
			if (dependencies[i].m_Id <= 0)
			{
				error = $"Dependency at index {i} has an invalid published ID.";
				return false;
			}

			if (dependencies[i].m_Id == expectedAudioSwitcherDependencyId)
			{
				hasExpectedAudioSwitcherDependency = true;
			}
		}

		if (!hasExpectedAudioSwitcherDependency)
		{
			error = $"Audio Switcher dependency metadata is invalid. Expected published ID {expectedAudioSwitcherDependencyId.ToString(CultureInfo.InvariantCulture)}.";
			return false;
		}

		if (!TryParseDeveloperModuleAdditionalUploadDependencies(
				out List<IModsUploadSupport.ModInfo.ModDependency> configuredAdditionalDependencies,
				out string additionalDependencyError))
		{
			error = additionalDependencyError;
			return false;
		}

		for (int dependencyIndex = 0; dependencyIndex < configuredAdditionalDependencies.Count; dependencyIndex++)
		{
			int configuredDependencyId = configuredAdditionalDependencies[dependencyIndex].m_Id;
			bool foundConfiguredDependency = false;
			for (int i = 0; i < dependencies.Length; i++)
			{
				if (dependencies[i].m_Id != configuredDependencyId)
				{
					continue;
				}

				foundConfiguredDependency = true;
				break;
			}

			if (!foundConfiguredDependency)
			{
				error =
					$"Configured dependency ID {configuredDependencyId.ToString(CultureInfo.InvariantCulture)} is missing from staged upload metadata.";
				return false;
			}
		}

		List<IModsUploadSupport.ExternalLinkData>? externalLinks = modInfo.m_ExternalLinks;
		if (externalLinks != null)
		{
			for (int i = 0; i < externalLinks.Count; i++)
			{
				IModsUploadSupport.ExternalLinkData link = externalLinks[i];
				if (string.IsNullOrWhiteSpace(link.m_URL))
				{
					error = $"External link at index {i} has an empty URL.";
					return false;
				}
			}
		}

		string contentPath = uploadHandle.GetAbsoluteContentPath();
		if (string.IsNullOrWhiteSpace(contentPath) || !Directory.Exists(contentPath))
		{
			error = "Staged content directory could not be resolved.";
			return false;
		}

		try
		{
			string[] stagedFiles = Directory.GetFiles(contentPath, "*", SearchOption.AllDirectories);
			if (stagedFiles.Length == 0)
			{
				error = "Staged content directory is empty.";
				return false;
			}
		}
		catch (Exception ex)
		{
			error = $"Unable to read staged content files: {ex.Message}";
			return false;
		}

		if (!TryValidateStagedDeveloperModuleSoundSetProfiles(contentPath, out string soundSetValidationWarning, out string soundSetValidationError))
		{
			error = soundSetValidationError;
			return false;
		}

		if (!string.IsNullOrWhiteSpace(soundSetValidationWarning))
		{
			Log.Warn($"Upload preflight warning: {soundSetValidationWarning}");
		}

		if (string.IsNullOrWhiteSpace(modInfo.m_ThumbnailFilename))
		{
			error = "Thumbnail filename was not set in upload metadata.";
			return false;
		}

		if (!TryResolveDeveloperModuleUploadMetadataDirectory(uploadHandle, out string metadataDirectoryPath, out string metadataError))
		{
			error = metadataError;
			return false;
		}

		string thumbnailPath = Path.Combine(metadataDirectoryPath, modInfo.m_ThumbnailFilename);
		if (!File.Exists(thumbnailPath))
		{
			error = $"Staged thumbnail was not found at '{thumbnailPath}'.";
			return false;
		}

		Log.Info($"Asset upload publish preflight passed. {BuildDeveloperModuleUploadModInfoSummary(modInfo)}");
		return true;
	}

	// Validate staged sound-set profile snapshots and warn when they reference non-included audio keys.
	private static bool TryValidateStagedDeveloperModuleSoundSetProfiles(
		string contentPath,
		out string warning,
		out string error)
	{
		warning = string.Empty;
		error = string.Empty;
		if (string.IsNullOrWhiteSpace(contentPath) || !Directory.Exists(contentPath))
		{
			error = "Staged content directory could not be resolved.";
			return false;
		}

		string manifestPath = Path.Combine(contentPath, kDeveloperModuleManifestFileName);
		if (!File.Exists(manifestPath))
		{
			error = $"Staged module manifest '{kDeveloperModuleManifestFileName}' was not found.";
			return false;
		}

		DeveloperModuleManifest manifest;
		try
		{
			string manifestJson = File.ReadAllText(manifestPath);
			if (!JsonDataSerializer.TryDeserialize(manifestJson, out DeveloperModuleManifest? parsedManifest, out string parseError) ||
				parsedManifest == null)
			{
				error = $"Staged module manifest could not be parsed: {parseError}";
				return false;
			}

			manifest = parsedManifest;
		}
		catch (Exception ex)
		{
			error = $"Staged module manifest could not be read: {ex.Message}";
			return false;
		}

		List<DeveloperSoundSetProfileSnapshot> snapshots = BuildDeveloperSoundSetProfileSnapshotsFromManifest(contentPath, manifest);
		if (snapshots.Count == 0)
		{
			return true;
		}

		Dictionary<DeveloperAudioDomain, HashSet<string>> includedByDomain = BuildDeveloperModuleIncludedAudioKeysByDomainFromManifest(manifest);
		List<string> coverageWarnings = BuildDeveloperSoundSetProfileCoverageWarnings(snapshots, includedByDomain);
		if (coverageWarnings.Count == 0)
		{
			return true;
		}

		int previewCount = Math.Min(coverageWarnings.Count, 5);
		string preview = string.Join(" | ", coverageWarnings.Take(previewCount));
		if (coverageWarnings.Count > previewCount)
		{
			preview = $"{preview} | +{(coverageWarnings.Count - previewCount).ToString(CultureInfo.InvariantCulture)} more";
		}

		warning = $"Detected {coverageWarnings.Count.ToString(CultureInfo.InvariantCulture)} sound-set profile reference warning(s). {preview}";
		return true;
	}

	// Build domain include sets from manifest entries for staged preflight validation.
	private static Dictionary<DeveloperAudioDomain, HashSet<string>> BuildDeveloperModuleIncludedAudioKeysByDomainFromManifest(DeveloperModuleManifest manifest)
	{
		HashSet<string> BuildKeySet(IReadOnlyList<DeveloperModuleManifestEntry>? entries)
		{
			HashSet<string> keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			if (entries == null)
			{
				return keys;
			}

			for (int i = 0; i < entries.Count; i++)
			{
				string normalized = SirenPathUtils.NormalizeProfileKey(entries[i]?.Key ?? string.Empty);
				if (!string.IsNullOrWhiteSpace(normalized))
				{
					keys.Add(normalized);
				}
			}

			return keys;
		}

		return new Dictionary<DeveloperAudioDomain, HashSet<string>>
		{
			{ DeveloperAudioDomain.Siren, BuildKeySet(manifest.Sirens) },
			{ DeveloperAudioDomain.VehicleEngine, BuildKeySet(manifest.VehicleEngines) },
			{ DeveloperAudioDomain.Ambient, BuildKeySet(manifest.Ambient) },
			{ DeveloperAudioDomain.Building, BuildKeySet(manifest.Buildings) },
			{ DeveloperAudioDomain.TransitAnnouncement, BuildKeySet(manifest.TransitAnnouncements) }
		};
	}

	// Build profile snapshots from manifest folder/file metadata under staged content root.
	private static List<DeveloperSoundSetProfileSnapshot> BuildDeveloperSoundSetProfileSnapshotsFromManifest(
		string contentPath,
		DeveloperModuleManifest manifest)
	{
		List<DeveloperSoundSetProfileSnapshot> snapshots = new List<DeveloperSoundSetProfileSnapshot>();
		List<DeveloperModuleManifestSoundSetProfile> soundSetProfiles = manifest.SoundSetProfiles ?? new List<DeveloperModuleManifestSoundSetProfile>();
		for (int index = 0; index < soundSetProfiles.Count; index++)
		{
			DeveloperModuleManifestSoundSetProfile entry = soundSetProfiles[index] ?? new DeveloperModuleManifestSoundSetProfile();
			string normalizedSetId = CitySoundProfileRegistry.NormalizeSetId(entry.SetId);
			if (string.IsNullOrWhiteSpace(normalizedSetId))
			{
				continue;
			}

			string displayName = string.IsNullOrWhiteSpace(entry.DisplayName)
				? BuildDeveloperModuleSoundSetProfileDisplayName(normalizedSetId)
				: $"{entry.DisplayName.Trim()} ({normalizedSetId})";
			string folder = SirenPathUtils.NormalizeProfileKey(entry.Folder ?? string.Empty);
			if (string.IsNullOrWhiteSpace(folder))
			{
				folder = $"Profiles/{normalizedSetId}";
			}

			HashSet<string> declaredFiles = new HashSet<string>(
				(entry.Files ?? new List<string>())
					.Select(file => Path.GetFileName((file ?? string.Empty).Trim()))
					.Where(file => !string.IsNullOrWhiteSpace(file)),
				StringComparer.OrdinalIgnoreCase);

			DeveloperSoundSetProfileSnapshot snapshot = new DeveloperSoundSetProfileSnapshot
			{
				SetId = normalizedSetId,
				DisplayName = displayName
			};

			if (TryResolveDeveloperModuleProfileSettingsPath(contentPath, folder, declaredFiles, SirenReplacementConfig.SettingsFileName, out string sirenSettingsPath))
			{
				snapshot.SettingsPathByDomain[DeveloperAudioDomain.Siren] = sirenSettingsPath;
			}

			if (TryResolveDeveloperModuleProfileSettingsPath(contentPath, folder, declaredFiles, VehicleEngineSettingsFileName, out string engineSettingsPath))
			{
				snapshot.SettingsPathByDomain[DeveloperAudioDomain.VehicleEngine] = engineSettingsPath;
			}

			if (TryResolveDeveloperModuleProfileSettingsPath(contentPath, folder, declaredFiles, AmbientSettingsFileName, out string ambientSettingsPath))
			{
				snapshot.SettingsPathByDomain[DeveloperAudioDomain.Ambient] = ambientSettingsPath;
			}

			if (TryResolveDeveloperModuleProfileSettingsPath(contentPath, folder, declaredFiles, BuildingSettingsFileName, out string buildingSettingsPath))
			{
				snapshot.SettingsPathByDomain[DeveloperAudioDomain.Building] = buildingSettingsPath;
			}

			if (TryResolveDeveloperModuleProfileSettingsPath(contentPath, folder, declaredFiles, TransitAnnouncementSettingsFileName, out string transitSettingsPath))
			{
				snapshot.SettingsPathByDomain[DeveloperAudioDomain.TransitAnnouncement] = transitSettingsPath;
			}

			snapshots.Add(snapshot);
		}

		return snapshots;
	}

	// Resolve one profile settings file path from staged content root while enforcing root containment.
	private static bool TryResolveDeveloperModuleProfileSettingsPath(
		string contentPath,
		string relativeFolder,
		ISet<string> declaredFiles,
		string expectedFileName,
		out string settingsPath)
	{
		settingsPath = string.Empty;
		string fileName = Path.GetFileName(expectedFileName ?? string.Empty);
		if (string.IsNullOrWhiteSpace(fileName))
		{
			return false;
		}

		string relative = $"{relativeFolder}/{fileName}";
		if (declaredFiles.Count > 0 && !declaredFiles.Contains(fileName))
		{
			// Keep checking fallback path so legacy manifests without Files lists still work.
		}

		if (!TryResolveDeveloperModulePathWithinRoot(contentPath, relative, out string resolved) || !File.Exists(resolved))
		{
			return false;
		}

		settingsPath = resolved;
		return true;
	}

	// Resolve one relative path under a root and reject traversal outside of the root.
	private static bool TryResolveDeveloperModulePathWithinRoot(string rootPath, string relativePath, out string resolvedPath)
	{
		resolvedPath = string.Empty;
		string normalizedRelative = SirenPathUtils.NormalizeProfileKey(relativePath ?? string.Empty);
		if (string.IsNullOrWhiteSpace(normalizedRelative))
		{
			return false;
		}

		string fullRoot;
		try
		{
			fullRoot = Path.GetFullPath(rootPath);
		}
		catch
		{
			return false;
		}

		if (string.IsNullOrWhiteSpace(fullRoot) || !Directory.Exists(fullRoot))
		{
			return false;
		}

		string rootWithSeparator = fullRoot.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
			? fullRoot
			: fullRoot + Path.DirectorySeparatorChar;
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

		resolvedPath = combined;
		return true;
	}

	// Stage selected thumbnail into the upload metadata directory and bind it in mod info.
	private static bool TryStageDeveloperModuleUploadThumbnail(PdxAssetUploadHandle uploadHandle, string thumbnailSourcePath, out string error)
	{
		error = string.Empty;
		string resolvedThumbnailPath = NormalizeDeveloperModuleUploadThumbnailPath(thumbnailSourcePath);
		if (string.IsNullOrWhiteSpace(resolvedThumbnailPath) || !File.Exists(resolvedThumbnailPath))
		{
			error = "Thumbnail file was not found while staging upload metadata.";
			return false;
		}

		string extension = Path.GetExtension(resolvedThumbnailPath);
		if (!IsSupportedDeveloperModuleUploadThumbnailExtension(extension))
		{
			error = "Thumbnail format is not supported. Use .png, .jpg, or .jpeg.";
			return false;
		}

		if (!TryResolveDeveloperModuleUploadMetadataDirectory(uploadHandle, out string metadataDirectoryPath, out string metadataError))
		{
			error = metadataError;
			return false;
		}

		try
		{
			Directory.CreateDirectory(metadataDirectoryPath);
			string fileName = $"thumbnail{extension.ToLowerInvariant()}";
			string destinationPath = Path.Combine(metadataDirectoryPath, fileName);
			File.Copy(resolvedThumbnailPath, destinationPath, overwrite: true);

			IModsUploadSupport.ModInfo modInfo = uploadHandle.modInfo;
			// PDX publish expects an absolute thumbnail path in m_ThumbnailFilename.
			modInfo.m_ThumbnailFilename = Path.GetFullPath(destinationPath).Replace("\\", "/");
			uploadHandle.modInfo = modInfo;
			return true;
		}
		catch (Exception ex)
		{
			error = ex.Message;
			return false;
		}
	}

	// Resolve metadata path from upload handle across game versions.
	private static bool TryResolveDeveloperModuleUploadMetadataDirectory(
		PdxAssetUploadHandle uploadHandle,
		out string metadataDirectoryPath,
		out string error)
	{
		metadataDirectoryPath = string.Empty;
		error = string.Empty;

		try
		{
			MethodInfo? method = uploadHandle
				.GetType()
				.GetMethod("GetAbsoluteMetadataPath", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (method != null)
			{
				object? result = method.Invoke(uploadHandle, null);
				string? path = result as string;
				if (!string.IsNullOrWhiteSpace(path))
				{
					metadataDirectoryPath = path!;
					return true;
				}
			}

			if (TryResolveDeveloperModuleUploadMetadataDirectoryFromContentPath(uploadHandle, out string fallbackPath))
			{
				metadataDirectoryPath = fallbackPath;
				return true;
			}

			error = "Upload failed because the metadata staging path could not be resolved.";
			return false;
		}
		catch (Exception ex)
		{
			error = $"Upload failed while resolving metadata path: {ex.Message}";
			return false;
		}
	}

	// Fallback metadata-path resolver using the content path parent folder.
	private static bool TryResolveDeveloperModuleUploadMetadataDirectoryFromContentPath(
		PdxAssetUploadHandle uploadHandle,
		out string metadataDirectoryPath)
	{
		metadataDirectoryPath = string.Empty;
		if (uploadHandle == null)
		{
			return false;
		}

		try
		{
			string contentPath = uploadHandle.GetAbsoluteContentPath();
			if (string.IsNullOrWhiteSpace(contentPath))
			{
				return false;
			}

			string? rootPath = Path.GetDirectoryName(contentPath);
			if (string.IsNullOrWhiteSpace(rootPath))
			{
				return false;
			}

			string metadataFolderName = ResolveDeveloperModuleUploadMetadataFolderName();
			if (string.IsNullOrWhiteSpace(metadataFolderName))
			{
				return false;
			}

			metadataDirectoryPath = Path.Combine(rootPath, metadataFolderName);
			return !string.IsNullOrWhiteSpace(metadataDirectoryPath);
		}
		catch
		{
			return false;
		}
	}

	// Resolve metadata directory folder name from platform constants with a stable fallback.
	private static string ResolveDeveloperModuleUploadMetadataFolderName()
	{
		try
		{
			FieldInfo? field = typeof(IModsUploadSupport.ModInfo).GetField(
				"kMetadataDirectory",
				BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
			if (field != null)
			{
				string? value = field.GetValue(null) as string;
				if (!string.IsNullOrWhiteSpace(value))
				{
					return value!.Trim();
				}
			}
		}
		catch
		{
			// Fall through to default.
		}

		return "metadata";
	}

	// Resolve latest upload-ready module by cached path first, then by scanning export directory.
	private static bool TryResolveLatestUploadReadyModulePath(out string moduleRootPath, out string error)
	{
		moduleRootPath = string.Empty;
		error = string.Empty;

		string cached = NormalizeDeveloperModuleExportDirectory(s_DeveloperLastUploadReadyModulePath);
		if (!string.IsNullOrWhiteSpace(cached) && HasUploadReadyModuleManifest(cached))
		{
			moduleRootPath = cached;
			return true;
		}

		string exportRoot = GetResolvedDeveloperModuleExportDirectory(ensureExists: false);
		if (string.IsNullOrWhiteSpace(exportRoot) || !Directory.Exists(exportRoot))
		{
			error = "No upload-ready module found. Build one first using 'Build + Upload'.";
			return false;
		}

		string[] directories = EnumerateDirectoriesSafe(exportRoot);
		DateTime latestWriteUtc = DateTime.MinValue;
		string latestPath = string.Empty;
		for (int i = 0; i < directories.Length; i++)
		{
			string candidate = NormalizeDeveloperModuleExportDirectory(directories[i]);
			if (string.IsNullOrWhiteSpace(candidate) || !HasUploadReadyModuleManifest(candidate))
			{
				continue;
			}

			DateTime writeUtc;
			try
			{
				writeUtc = Directory.GetLastWriteTimeUtc(candidate);
			}
			catch
			{
				writeUtc = DateTime.MinValue;
			}

			if (writeUtc >= latestWriteUtc)
			{
				latestWriteUtc = writeUtc;
				latestPath = candidate;
			}
		}

		if (string.IsNullOrWhiteSpace(latestPath))
		{
			error = "No upload-ready module found. Build one first using 'Build + Upload'.";
			return false;
		}

		s_DeveloperLastUploadReadyModulePath = latestPath;
		moduleRootPath = latestPath;
		return true;
	}

	// Build upload metadata from manifest + defaults used by PDX asset upload.
	private static bool TryBuildDeveloperModuleUploadMetadata(
		string moduleRootPath,
		string manifestPath,
		out DeveloperModuleUploadMetadata metadata,
		out string error)
	{
		metadata = new DeveloperModuleUploadMetadata(
			displayName: NormalizeDeveloperModuleDisplayName(s_DeveloperModuleDisplayName),
			moduleId: NormalizeDeveloperModuleId(s_DeveloperModuleId),
			shortDescription: kDeveloperModuleUploadDefaultShortDescription,
			longDescription: kDeveloperModuleUploadDefaultShortDescription,
			modVersion: NormalizeDeveloperModuleVersion(s_DeveloperModuleVersion),
			gameVersion: string.IsNullOrWhiteSpace(Application.version)
				? kDeveloperModuleUploadDefaultRecommendedGameVersion
				: Application.version,
			thumbnailPath: string.Empty);
		error = string.Empty;

		string displayName = metadata.DisplayName;
		string moduleId = metadata.ModuleId;
		string moduleVersion = metadata.ModVersion;
		try
		{
			string manifestJson = File.ReadAllText(manifestPath);
			if (JsonDataSerializer.TryDeserialize(manifestJson, out DeveloperModuleManifest? manifest, out _) &&
				manifest != null)
			{
				if (!string.IsNullOrWhiteSpace(manifest.DisplayName))
				{
					displayName = manifest.DisplayName.Trim();
				}

				if (!string.IsNullOrWhiteSpace(manifest.ModuleId))
				{
					moduleId = NormalizeDeveloperModuleId(manifest.ModuleId);
				}

				if (!string.IsNullOrWhiteSpace(manifest.Version))
				{
					moduleVersion = NormalizeDeveloperModuleVersion(manifest.Version);
				}
			}
		}
		catch (Exception ex)
		{
			Log.Warn($"Failed to read upload-ready manifest metadata from '{manifestPath}'. {ex.Message}");
		}

		if (!TryResolveDeveloperModuleUploadThumbnailSourcePath(moduleRootPath, out string thumbnailPath, out string thumbnailError))
		{
			error = thumbnailError;
			return false;
		}

		string shortDescription = BuildDeveloperModuleUploadShortDescription(displayName);
		string configuredDescription = NormalizeDeveloperModuleUploadDescription(s_DeveloperModuleUploadDescription);
		string longDescription = string.IsNullOrWhiteSpace(configuredDescription)
			? BuildDeveloperModuleUploadLongDescription(displayName, moduleId)
			: configuredDescription;
		metadata = new DeveloperModuleUploadMetadata(
			displayName: displayName,
			moduleId: moduleId,
			shortDescription: shortDescription,
			longDescription: longDescription,
			modVersion: moduleVersion,
			gameVersion: metadata.GameVersion,
			thumbnailPath: thumbnailPath);
		return true;
	}

	// Resolve explicit thumbnail path (if set) or ensure module-local default thumbnail exists.
	private static bool TryResolveDeveloperModuleUploadThumbnailSourcePath(string moduleRootPath, out string thumbnailPath, out string error)
	{
		thumbnailPath = string.Empty;
		error = string.Empty;

		string configuredPath = NormalizeDeveloperModuleUploadThumbnailPath(s_DeveloperModuleUploadThumbnailPath);
		if (!string.IsNullOrWhiteSpace(configuredPath))
		{
			string resolved = configuredPath;
			if (!Path.IsPathRooted(resolved))
			{
				try
				{
					resolved = Path.GetFullPath(Path.Combine(moduleRootPath, resolved));
				}
				catch (Exception ex)
				{
					error = $"Upload thumbnail path is invalid: {ex.Message}";
					return false;
				}
			}

			if (!File.Exists(resolved))
			{
				error = $"Upload thumbnail was not found at '{resolved}'.";
				return false;
			}

			if (!IsSupportedDeveloperModuleUploadThumbnailExtension(Path.GetExtension(resolved)))
			{
				error = "Upload thumbnail must be .png, .jpg, or .jpeg.";
				return false;
			}

			thumbnailPath = resolved;
			return true;
		}

		return TryEnsureDeveloperModuleUploadThumbnail(moduleRootPath, out thumbnailPath, out error);
	}

	// Build selectable thumbnail candidates from latest module root, optional thumbnail directory, and persisted selection.
	private static List<string> GetDeveloperModuleUploadThumbnailCandidates(out string moduleRootPath, out string thumbnailDirectoryPath)
	{
		moduleRootPath = string.Empty;
		thumbnailDirectoryPath = NormalizeDeveloperModuleUploadThumbnailDirectory(s_DeveloperModuleUploadThumbnailDirectory);
		HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		List<string> candidates = new List<string>();

		string persisted = NormalizeDeveloperModuleUploadThumbnailPath(s_DeveloperModuleUploadThumbnailPath);
		if (!string.IsNullOrWhiteSpace(persisted) &&
			File.Exists(persisted) &&
			IsSupportedDeveloperModuleUploadThumbnailExtension(Path.GetExtension(persisted)) &&
			seen.Add(persisted))
		{
			candidates.Add(persisted);
		}

		if (TryResolveLatestUploadReadyModulePath(out string latestModuleRoot, out _))
		{
			moduleRootPath = latestModuleRoot;
			AddDeveloperModuleUploadThumbnailCandidatesFromDirectory(latestModuleRoot, seen, candidates);
		}

		if (!string.IsNullOrWhiteSpace(thumbnailDirectoryPath) && Directory.Exists(thumbnailDirectoryPath))
		{
			AddDeveloperModuleUploadThumbnailCandidatesFromDirectory(thumbnailDirectoryPath, seen, candidates);
		}

		candidates.Sort(StringComparer.OrdinalIgnoreCase);
		return candidates;
	}

	// Add supported thumbnail files from one directory into a de-duplicated candidate list.
	private static void AddDeveloperModuleUploadThumbnailCandidatesFromDirectory(
		string directoryPath,
		ISet<string> seen,
		ICollection<string> candidates)
	{
		if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
		{
			return;
		}

		string[] files = EnumerateFilesSafe(directoryPath);
		for (int i = 0; i < files.Length; i++)
		{
			string normalized = NormalizeDeveloperModuleUploadThumbnailPath(files[i]);
			if (string.IsNullOrWhiteSpace(normalized))
			{
				continue;
			}

			if (!IsSupportedDeveloperModuleUploadThumbnailExtension(Path.GetExtension(normalized)))
			{
				continue;
			}

			if (seen.Add(normalized))
			{
				candidates.Add(normalized);
			}
		}
	}

	// True when file path is contained within the given directory root.
	private static bool IsPathWithinDirectory(string filePath, string directoryPath)
	{
		string normalizedFilePath = NormalizeDeveloperModuleUploadThumbnailPath(filePath);
		string normalizedDirectoryPath = NormalizeDeveloperModuleUploadThumbnailDirectory(directoryPath);
		if (string.IsNullOrWhiteSpace(normalizedFilePath) || string.IsNullOrWhiteSpace(normalizedDirectoryPath))
		{
			return false;
		}

		if (string.Equals(normalizedFilePath, normalizedDirectoryPath, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		string rootedDirectory = normalizedDirectoryPath.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
			? normalizedDirectoryPath
			: normalizedDirectoryPath + Path.DirectorySeparatorChar;
		return normalizedFilePath.StartsWith(rootedDirectory, StringComparison.OrdinalIgnoreCase);
	}

	// Return true when selected path exists in currently discovered thumbnail candidates.
	private static bool ContainsDeveloperModuleUploadThumbnailCandidate(IReadOnlyList<string> candidates, string path)
	{
		string normalized = NormalizeDeveloperModuleUploadThumbnailPath(path);
		if (string.IsNullOrWhiteSpace(normalized))
		{
			return false;
		}

		for (int i = 0; i < candidates.Count; i++)
		{
			if (string.Equals(candidates[i], normalized, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}

		return false;
	}

	// Apply upload metadata and access level to one PDX upload handle.
	private static void ConfigureDeveloperModuleAssetUploadHandle(
		PdxAssetUploadHandle uploadHandle,
		DeveloperModuleUploadMetadata metadata,
		DeveloperModuleUploadAccessLevel accessLevel,
		DeveloperModuleUploadPublishMode publishMode,
		int existingPublishedId)
	{
		IModsUploadSupport.ModInfo modInfo = uploadHandle.modInfo;
		modInfo.m_DisplayName = metadata.DisplayName;
		modInfo.m_ShortDescription = metadata.ShortDescription;
		modInfo.m_LongDescription = metadata.LongDescription;
		modInfo.m_UserModVersion = metadata.ModVersion;
		modInfo.m_RecommendedGameVersion = metadata.GameVersion;
		modInfo.m_Changelog = kDeveloperModuleUploadDefaultChangeLog;
		modInfo.m_Visibility = ConvertDeveloperModuleUploadAccessLevel(accessLevel);
		modInfo.m_Tags = new[] { kDeveloperModuleUploadAssetPackTag };
		modInfo.m_ExternalLinks = new List<IModsUploadSupport.ExternalLinkData>();
		if (publishMode == DeveloperModuleUploadPublishMode.UpdateExisting)
		{
			modInfo.m_PublishedID = existingPublishedId;
		}
		else
		{
			modInfo.m_PublishedID = 0;
		}

		uploadHandle.modInfo = modInfo;
		uploadHandle.updateExisting = publishMode == DeveloperModuleUploadPublishMode.UpdateExisting;
	}

	// Normalize tags for current PDX taxonomy (legacy "Asset" -> "AssetPack").
	private static void NormalizeDeveloperModuleUploadTags(PdxAssetUploadHandle uploadHandle)
	{
		if (uploadHandle == null)
		{
			return;
		}

		bool replacedLegacyAssetTag = false;
		HashSet<string> normalizedTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		void AddTag(string rawTag)
		{
			string tag = (rawTag ?? string.Empty).Trim();
			if (string.IsNullOrWhiteSpace(tag))
			{
				return;
			}

			if (string.Equals(tag, kDeveloperModuleUploadLegacyAssetTag, StringComparison.OrdinalIgnoreCase))
			{
				tag = kDeveloperModuleUploadAssetPackTag;
				replacedLegacyAssetTag = true;
			}

			normalizedTags.Add(tag);
		}

		IModsUploadSupport.ModInfo modInfo = uploadHandle.modInfo;
		string[] currentTags = modInfo.m_Tags ?? Array.Empty<string>();
		for (int i = 0; i < currentTags.Length; i++)
		{
			AddTag(currentTags[i]);
		}

		foreach (string tag in uploadHandle.tags)
		{
			AddTag(tag);
		}

		for (int i = 0; i < uploadHandle.additionalTags.Count; i++)
		{
			AddTag(uploadHandle.additionalTags[i]);
		}

		if (normalizedTags.Count == 0)
		{
			normalizedTags.Add(kDeveloperModuleUploadAssetPackTag);
		}

		modInfo.m_Tags = new string[normalizedTags.Count];
		normalizedTags.CopyTo(modInfo.m_Tags);
		uploadHandle.modInfo = modInfo;

		if (replacedLegacyAssetTag)
		{
			Log.Info($"Normalized legacy upload tag '{kDeveloperModuleUploadLegacyAssetTag}' to '{kDeveloperModuleUploadAssetPackTag}'.");
		}
	}

	// Remove placeholder/invalid external links added by SDK defaults.
	private static void NormalizeDeveloperModuleUploadExternalLinks(PdxAssetUploadHandle uploadHandle)
	{
		if (uploadHandle == null)
		{
			return;
		}

		IModsUploadSupport.ModInfo modInfo = uploadHandle.modInfo;
		List<IModsUploadSupport.ExternalLinkData>? externalLinks = modInfo.m_ExternalLinks;
		if (externalLinks == null || externalLinks.Count == 0)
		{
			return;
		}

		List<IModsUploadSupport.ExternalLinkData> normalized = new List<IModsUploadSupport.ExternalLinkData>(externalLinks.Count);
		int removedCount = 0;
		for (int i = 0; i < externalLinks.Count; i++)
		{
			IModsUploadSupport.ExternalLinkData link = externalLinks[i];
			string type = (link.m_Type ?? string.Empty).Trim();
			string url = (link.m_URL ?? string.Empty).Trim();
			if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(url))
			{
				removedCount++;
				continue;
			}

			link.m_Type = type;
			link.m_URL = url;
			normalized.Add(link);
		}

		if (removedCount <= 0)
		{
			return;
		}

		modInfo.m_ExternalLinks = normalized;
		uploadHandle.modInfo = modInfo;
		Log.Info($"Removed {removedCount.ToString(CultureInfo.InvariantCulture)} empty/invalid external link placeholder(s) from asset upload metadata.");
	}

	// Prime dependency ID cache from account listings without requiring full SyncPlatformData().
	private static async Task TryPrimeAudioSwitcherDependencyPublishedIdCacheFromPlatformListingsAsync(PdxAssetUploadHandle uploadHandle)
	{
		if (uploadHandle == null || s_DeveloperAudioSwitcherDependencyPublishedIdCache > 0)
		{
			return;
		}

		try
		{
			FieldInfo? managerField = uploadHandle
				.GetType()
				.GetField("m_Manager", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			object? manager = managerField?.GetValue(uploadHandle);
			if (manager == null)
			{
				return;
			}

			MethodInfo? listMethod = manager
				.GetType()
				.GetMethod("ListAllModsByMe", BindingFlags.Instance | BindingFlags.Public);
			if (listMethod == null)
			{
				return;
			}

			object? invocationResult = InvokeListAllModsByMeForDependencyCache(manager, listMethod);
			if (!(invocationResult is Task listTask))
			{
				return;
			}

			await listTask;
			PropertyInfo? resultProperty = listTask.GetType().GetProperty("Result", BindingFlags.Instance | BindingFlags.Public);
			object? resultObject = resultProperty?.GetValue(listTask, null);
			if (!(resultObject is IEnumerable resultEnumerable))
			{
				return;
			}

			List<IModsUploadSupport.ModInfo> authorMods = new List<IModsUploadSupport.ModInfo>();
			foreach (object? item in resultEnumerable)
			{
				if (item is IModsUploadSupport.ModInfo modInfo)
				{
					authorMods.Add(modInfo);
				}
			}

			int resolvedId = ResolveAudioSwitcherDependencyPublishedIdFromAuthorMods(authorMods);
			if (resolvedId == kAudioSwitcherOfficialPublishedId)
			{
				s_DeveloperAudioSwitcherDependencyPublishedIdCache = resolvedId;
			}
			else if (resolvedId > 0)
			{
				Log.Warn($"Dependency cache priming ignored non-official Audio Switcher candidate ID {resolvedId.ToString(CultureInfo.InvariantCulture)}.");
			}
		}
		catch (Exception ex)
		{
			Log.Warn($"Dependency cache priming from account listings failed: {ex.Message}");
		}
	}

	// Invoke ListAllModsByMe with resilient argument binding across platform SDK signatures.
	private static object? InvokeListAllModsByMeForDependencyCache(object manager, MethodInfo listMethod)
	{
		ParameterInfo[] parameters = listMethod.GetParameters();
		object?[] args = new object?[parameters.Length];
		for (int i = 0; i < parameters.Length; i++)
		{
			ParameterInfo parameter = parameters[i];
			Type parameterType = parameter.ParameterType;
			if (parameterType == typeof(string[]))
			{
				args[i] = Array.Empty<string>();
				continue;
			}

			if (parameterType == typeof(int))
			{
				args[i] = 200;
				continue;
			}

			if (parameter.HasDefaultValue)
			{
				args[i] = parameter.DefaultValue;
				continue;
			}

			args[i] = parameterType.IsValueType
				? Activator.CreateInstance(parameterType)
				: null;
		}

		return listMethod.Invoke(manager, args);
	}

	// Parse optional dependency IDs entered in options (comma/semicolon/newline delimited; optional @version suffix).
	private static bool TryParseDeveloperModuleAdditionalUploadDependencies(
		out List<IModsUploadSupport.ModInfo.ModDependency> dependencies,
		out string error)
	{
		dependencies = new List<IModsUploadSupport.ModInfo.ModDependency>();
		error = string.Empty;

		string raw = NormalizeDeveloperModuleUploadAdditionalDependencies(s_DeveloperModuleUploadAdditionalDependencies);
		if (string.IsNullOrWhiteSpace(raw))
		{
			return true;
		}

		string[] tokens = raw.Split(s_DeveloperModuleUploadDependencySeparators, StringSplitOptions.RemoveEmptyEntries);
		HashSet<int> seen = new HashSet<int>();
		for (int i = 0; i < tokens.Length; i++)
		{
			string token = (tokens[i] ?? string.Empty).Trim();
			if (string.IsNullOrWhiteSpace(token))
			{
				continue;
			}

			string idToken = token;
			string versionToken = string.Empty;
			int atIndex = token.IndexOf('@');
			if (atIndex >= 0)
			{
				if (token.IndexOf('@', atIndex + 1) >= 0)
				{
					error = $"Invalid additional dependency token '{token}'. Use '<PublishedId>' or '<PublishedId>@<Version>'.";
					return false;
				}

				idToken = token.Substring(0, atIndex).Trim();
				versionToken = token.Substring(atIndex + 1).Trim();
			}

			if (!int.TryParse(idToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out int publishedId) || publishedId <= 0)
			{
				error = $"Invalid additional dependency token '{token}'. Published IDs must be positive integers.";
				return false;
			}

			if (publishedId == kAudioSwitcherOfficialPublishedId || !seen.Add(publishedId))
			{
				continue;
			}

			dependencies.Add(new IModsUploadSupport.ModInfo.ModDependency
			{
				m_Id = publishedId,
				m_Version = NormalizeDeveloperModuleDependencyVersion(versionToken)
			});
		}

		return true;
	}

	// Normalize optional dependency version suffix.
	private static string NormalizeDeveloperModuleDependencyVersion(string rawVersion)
	{
		string normalized = (rawVersion ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(normalized))
		{
			return string.Empty;
		}

		if (normalized.Length <= kDeveloperModuleUploadVersionMaxLength)
		{
			return normalized;
		}

		return normalized.Substring(0, kDeveloperModuleUploadVersionMaxLength).Trim();
	}

	// Add Audio Switcher as a required dependency on every auto-uploaded module asset.
	private static bool TryAttachAudioSwitcherUploadDependency(PdxAssetUploadHandle uploadHandle, out string error)
	{
		error = string.Empty;
		if (uploadHandle == null)
		{
			error = "Upload handle is unavailable.";
			return false;
		}

		int dependencyPublishedId = ResolveAudioSwitcherDependencyPublishedId(uploadHandle.authorMods);
		if (dependencyPublishedId <= 0)
		{
			error = "Could not resolve the published PDX mod ID for 'Audio Switcher'. Ensure Audio Switcher is installed/enabled and visible to your PDX account, then retry.";
			return false;
		}

		if (!TryParseDeveloperModuleAdditionalUploadDependencies(
				out List<IModsUploadSupport.ModInfo.ModDependency> configuredAdditionalDependencies,
				out string additionalDependencyError))
		{
			error = additionalDependencyError;
			return false;
		}

		IModsUploadSupport.ModInfo modInfo = uploadHandle.modInfo;
		IModsUploadSupport.ModInfo.ModDependency[] existingDependencies = modInfo.m_ModDependencies ??
			Array.Empty<IModsUploadSupport.ModInfo.ModDependency>();
		List<IModsUploadSupport.ModInfo.ModDependency> mergedDependencies =
			new List<IModsUploadSupport.ModInfo.ModDependency>(existingDependencies.Length + configuredAdditionalDependencies.Count + 1);
		HashSet<int> seenDependencyIds = new HashSet<int>();
		bool hasAudioSwitcherDependency = false;

		void AddOrMergeDependency(IModsUploadSupport.ModInfo.ModDependency dependency)
		{
			if (dependency.m_Id <= 0)
			{
				return;
			}

			dependency.m_Version = NormalizeDeveloperModuleDependencyVersion(dependency.m_Version);
			if (dependency.m_Id == dependencyPublishedId)
			{
				hasAudioSwitcherDependency = true;
			}

			if (!seenDependencyIds.Add(dependency.m_Id))
			{
				if (!string.IsNullOrWhiteSpace(dependency.m_Version))
				{
					for (int index = 0; index < mergedDependencies.Count; index++)
					{
						IModsUploadSupport.ModInfo.ModDependency existing = mergedDependencies[index];
						if (existing.m_Id != dependency.m_Id || !string.IsNullOrWhiteSpace(existing.m_Version))
						{
							continue;
						}

						existing.m_Version = dependency.m_Version;
						mergedDependencies[index] = existing;
						break;
					}
				}

				return;
			}

			mergedDependencies.Add(dependency);
		}

		for (int i = 0; i < existingDependencies.Length; i++)
		{
			AddOrMergeDependency(existingDependencies[i]);
		}

		for (int i = 0; i < configuredAdditionalDependencies.Count; i++)
		{
			AddOrMergeDependency(configuredAdditionalDependencies[i]);
		}

		if (!hasAudioSwitcherDependency)
		{
			AddOrMergeDependency(new IModsUploadSupport.ModInfo.ModDependency
			{
				m_Id = dependencyPublishedId,
				m_Version = string.Empty
			});
		}

		modInfo.m_ModDependencies = mergedDependencies.ToArray();
		uploadHandle.modInfo = modInfo;
		Log.Info(
			$"Applied upload dependency metadata. Audio Switcher ID={dependencyPublishedId.ToString(CultureInfo.InvariantCulture)}, " +
			$"additional configured={configuredAdditionalDependencies.Count.ToString(CultureInfo.InvariantCulture)}, total={mergedDependencies.Count.ToString(CultureInfo.InvariantCulture)}.");
		return true;
	}

	// Resolve Audio Switcher published ID from the current author's PDX mod listings.
	private static int ResolveAudioSwitcherDependencyPublishedId(IReadOnlyList<IModsUploadSupport.ModInfo> authorMods)
	{
		int authorPublishedId = ResolveAudioSwitcherDependencyPublishedIdFromAuthorMods(authorMods);
		if (authorPublishedId > 0)
		{
			if (authorPublishedId == kAudioSwitcherOfficialPublishedId)
			{
				Log.Info($"Audio Switcher dependency resolved from author mods: ID={authorPublishedId}");
				s_DeveloperAudioSwitcherDependencyPublishedIdCache = authorPublishedId;
				return authorPublishedId;
			}

			Log.Warn(
				$"Audio Switcher dependency candidate from author mods used non-official ID {authorPublishedId.ToString(CultureInfo.InvariantCulture)}. " +
				$"Falling back to official ID {kAudioSwitcherOfficialPublishedId.ToString(CultureInfo.InvariantCulture)}.");
		}

		if (TryResolveAudioSwitcherDependencyPublishedIdFromActiveParadoxMods(out int activePublishedId))
		{
			if (activePublishedId == kAudioSwitcherOfficialPublishedId)
			{
				Log.Info($"Audio Switcher dependency resolved from active mods: ID={activePublishedId}");
				s_DeveloperAudioSwitcherDependencyPublishedIdCache = activePublishedId;
				return activePublishedId;
			}

			Log.Warn(
				$"Audio Switcher dependency candidate from active mods used non-official ID {activePublishedId.ToString(CultureInfo.InvariantCulture)}. " +
				$"Falling back to official ID {kAudioSwitcherOfficialPublishedId.ToString(CultureInfo.InvariantCulture)}.");
		}

		if (s_DeveloperAudioSwitcherDependencyPublishedIdCache == kAudioSwitcherOfficialPublishedId)
		{
			Log.Info($"Audio Switcher dependency resolved from cache: ID={s_DeveloperAudioSwitcherDependencyPublishedIdCache}");
			return s_DeveloperAudioSwitcherDependencyPublishedIdCache;
		}

		if (s_DeveloperAudioSwitcherDependencyPublishedIdCache > 0)
		{
			Log.Warn(
				$"Ignoring non-official cached Audio Switcher dependency ID {s_DeveloperAudioSwitcherDependencyPublishedIdCache.ToString(CultureInfo.InvariantCulture)}. " +
				$"Using official ID {kAudioSwitcherOfficialPublishedId.ToString(CultureInfo.InvariantCulture)}.");
		}
		else
		{
			Log.Warn(
				$"Audio Switcher dependency could not be resolved from platform data. " +
				$"Using official ID {kAudioSwitcherOfficialPublishedId.ToString(CultureInfo.InvariantCulture)}.");
		}

		s_DeveloperAudioSwitcherDependencyPublishedIdCache = kAudioSwitcherOfficialPublishedId;
		return kAudioSwitcherOfficialPublishedId;
	}

	// Resolve dependency ID from author-owned mods returned by platform sync.
	private static int ResolveAudioSwitcherDependencyPublishedIdFromAuthorMods(IReadOnlyList<IModsUploadSupport.ModInfo> authorMods)
	{
		if (authorMods == null || authorMods.Count == 0)
		{
			return 0;
		}

		int bestFallbackId = 0;
		for (int i = 0; i < authorMods.Count; i++)
		{
			IModsUploadSupport.ModInfo modInfo = authorMods[i];
			int publishedId = modInfo.m_PublishedID;
			if (publishedId <= 0)
			{
				continue;
			}

			string displayName = (modInfo.m_DisplayName ?? string.Empty).Trim();
			if (string.IsNullOrWhiteSpace(displayName))
			{
				continue;
			}

			if (string.Equals(displayName, kOptionsPanelDisplayName, StringComparison.OrdinalIgnoreCase))
			{
				return publishedId;
			}

			if (bestFallbackId <= 0 && MatchesAudioSwitcherDependencyName(displayName))
			{
				bestFallbackId = publishedId;
			}
		}

		return bestFallbackId;
	}

	// Resolve dependency ID from active Paradox mods as a fallback when author list is empty/unavailable.
	private static bool TryResolveAudioSwitcherDependencyPublishedIdFromActiveParadoxMods(out int publishedId)
	{
		publishedId = 0;
		try
		{
			if (!(AssetDatabase<ParadoxMods>.instance?.dataSource is ParadoxModsDataSource dataSource))
			{
				return false;
			}

			MethodInfo? getActiveModsMethod = dataSource.GetType().GetMethod("GetActiveMods", BindingFlags.Public | BindingFlags.Instance);
			if (getActiveModsMethod == null)
			{
				return false;
			}

			object? activeModsObject = getActiveModsMethod.Invoke(dataSource, null);
			if (!(activeModsObject is IEnumerable activeMods))
			{
				return false;
			}

			int bestFallbackId = 0;
			foreach (object? activeMod in activeMods)
			{
				if (activeMod == null)
				{
					continue;
				}

				string displayName = ReadActiveParadoxModDisplayName(activeMod);
				int candidateId = ReadActiveParadoxModPublishedId(activeMod);
				if (candidateId <= 0 || string.IsNullOrWhiteSpace(displayName))
				{
					continue;
				}

				if (string.Equals(displayName, kOptionsPanelDisplayName, StringComparison.OrdinalIgnoreCase))
				{
					publishedId = candidateId;
					return true;
				}

				if (bestFallbackId <= 0 && MatchesAudioSwitcherDependencyName(displayName))
				{
					bestFallbackId = candidateId;
				}
			}

			if (bestFallbackId > 0)
			{
				publishedId = bestFallbackId;
				return true;
			}
		}
		catch (Exception ex)
		{
			Log.Warn($"Failed to resolve Audio Switcher dependency ID from active Paradox mods: {ex.Message}");
		}

		return false;
	}

	// Match Audio Switcher listing names across historical naming variants.
	private static bool MatchesAudioSwitcherDependencyName(string displayName)
	{
		string normalized = NormalizeAudioSwitcherDependencyName(displayName);
		if (string.IsNullOrWhiteSpace(normalized))
		{
			return false;
		}

		return string.Equals(normalized, "audioswitcher", StringComparison.Ordinal) ||
			string.Equals(normalized, "sirenchanger", StringComparison.Ordinal);
	}

	// Normalize listing display names for exact dependency-name comparisons.
	private static string NormalizeAudioSwitcherDependencyName(string displayName)
	{
		string trimmed = (displayName ?? string.Empty).Trim();
		if (trimmed.Length == 0)
		{
			return string.Empty;
		}

		StringBuilder builder = new StringBuilder(trimmed.Length);
		for (int i = 0; i < trimmed.Length; i++)
		{
			char c = trimmed[i];
			if (char.IsLetterOrDigit(c))
			{
				builder.Append(char.ToLowerInvariant(c));
			}
		}

		return builder.ToString();
	}

	// Read one active Paradox mod display name via tolerant reflection.
	private static string ReadActiveParadoxModDisplayName(object activeMod)
	{
		if (TryReadActiveParadoxModString(activeMod, "displayName", out string displayName) ||
			TryReadActiveParadoxModString(activeMod, "DisplayName", out displayName) ||
			TryReadActiveParadoxModString(activeMod, "name", out displayName) ||
			TryReadActiveParadoxModString(activeMod, "Name", out displayName) ||
			TryReadActiveParadoxModString(activeMod, "m_DisplayName", out displayName))
		{
			return displayName;
		}

		return string.Empty;
	}

	// Read one active Paradox mod published ID via tolerant reflection.
	private static int ReadActiveParadoxModPublishedId(object activeMod)
	{
		if (TryReadActiveParadoxModInt(activeMod, "m_PublishedID", out int publishedId) ||
			TryReadActiveParadoxModInt(activeMod, "publishedID", out publishedId) ||
			TryReadActiveParadoxModInt(activeMod, "publishedId", out publishedId) ||
			TryReadActiveParadoxModInt(activeMod, "id", out publishedId) ||
			TryReadActiveParadoxModInt(activeMod, "ID", out publishedId))
		{
			return publishedId;
		}

		return 0;
	}

	// Try reading a string member from one active-mod record.
	private static bool TryReadActiveParadoxModString(object activeMod, string memberName, out string value)
	{
		value = string.Empty;
		if (activeMod == null || string.IsNullOrWhiteSpace(memberName))
		{
			return false;
		}

		Type type = activeMod.GetType();
		FieldInfo? field = type.GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
		if (field != null && field.FieldType == typeof(string))
		{
			value = ((string?)field.GetValue(activeMod) ?? string.Empty).Trim();
			return !string.IsNullOrWhiteSpace(value);
		}

		PropertyInfo? property = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
		if (property != null && property.PropertyType == typeof(string) && property.GetIndexParameters().Length == 0)
		{
			value = ((string?)property.GetValue(activeMod, null) ?? string.Empty).Trim();
			return !string.IsNullOrWhiteSpace(value);
		}

		return false;
	}

	// Try reading a positive integer ID member from one active-mod record.
	private static bool TryReadActiveParadoxModInt(object activeMod, string memberName, out int value)
	{
		value = 0;
		if (activeMod == null || string.IsNullOrWhiteSpace(memberName))
		{
			return false;
		}

		Type type = activeMod.GetType();
		FieldInfo? field = type.GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
		if (field != null && TryConvertToPositiveInt(field.GetValue(activeMod), out value))
		{
			return true;
		}

		PropertyInfo? property = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
		if (property != null && property.GetIndexParameters().Length == 0 && TryConvertToPositiveInt(property.GetValue(activeMod, null), out value))
		{
			return true;
		}

		return false;
	}

	// Convert common scalar/string ID representations to one positive int value.
	private static bool TryConvertToPositiveInt(object? rawValue, out int value)
	{
		value = 0;
		switch (rawValue)
		{
			case int typedInt when typedInt > 0:
				value = typedInt;
				return true;
			case long typedLong when typedLong > 0 && typedLong <= int.MaxValue:
				value = (int)typedLong;
				return true;
			case uint typedUInt when typedUInt > 0 && typedUInt <= int.MaxValue:
				value = (int)typedUInt;
				return true;
			case ulong typedULong when typedULong > 0 && typedULong <= int.MaxValue:
				value = (int)typedULong;
				return true;
			case short typedShort when typedShort > 0:
				value = typedShort;
				return true;
			case ushort typedUShort when typedUShort > 0:
				value = typedUShort;
				return true;
		}

		string text = (rawValue?.ToString() ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}

		if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedInt) && parsedInt > 0)
		{
			value = parsedInt;
			return true;
		}

		if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsedLong) &&
			parsedLong > 0 &&
			parsedLong <= int.MaxValue)
		{
			value = (int)parsedLong;
			return true;
		}

		return false;
	}

	// Build a compact upload failure message from platform operation data.
	private static string BuildDeveloperModuleUploadFailureMessage(string prefix, IModsUploadSupport.ModOperationResult result)
	{
		string detail = GetDeveloperModuleUploadErrorDetail(result.m_Error);
		if (string.IsNullOrWhiteSpace(detail))
		{
			return prefix;
		}

		return $"{prefix} {detail}";
	}

	// Log verbose failure diagnostics for registration/publish API responses.
	private static void LogDeveloperModuleUploadFailureDiagnostics(
		string phase,
		IModsUploadSupport.ModOperationResult result,
		PdxAssetUploadHandle? uploadHandle)
	{
		try
		{
			StringBuilder builder = new StringBuilder(512);
			builder.Append("Asset upload ")
				.Append(string.IsNullOrWhiteSpace(phase) ? "operation" : phase.Trim())
				.Append(" failed. ");
			string errorDetail = GetDeveloperModuleUploadErrorDetail(result.m_Error);
			if (!string.IsNullOrWhiteSpace(errorDetail))
			{
				builder.Append("Error: ").Append(errorDetail).Append(". ");
			}
			else
			{
				builder.Append("Error: <no details>. ");
			}

			IModsUploadSupport.ModInfo selectedInfo = result.m_ModInfo;
			if (string.IsNullOrWhiteSpace(selectedInfo.m_DisplayName) && uploadHandle != null)
			{
				selectedInfo = uploadHandle.modInfo;
			}

			builder.Append(BuildDeveloperModuleUploadModInfoSummary(selectedInfo));
			Log.Warn(builder.ToString());
		}
		catch (Exception ex)
		{
			Log.Warn($"Asset upload diagnostics logging failed: {ex.Message}");
		}
	}

	// Build compact upload metadata summary for diagnostics.
	private static string BuildDeveloperModuleUploadModInfoSummary(IModsUploadSupport.ModInfo modInfo)
	{
		string displayName = (modInfo.m_DisplayName ?? string.Empty).Trim();
		string shortDescription = (modInfo.m_ShortDescription ?? string.Empty).Trim();
		string longDescription = (modInfo.m_LongDescription ?? string.Empty).Trim();
		string userVersion = (modInfo.m_UserModVersion ?? string.Empty).Trim();
		string gameVersion = (modInfo.m_RecommendedGameVersion ?? string.Empty).Trim();
		string changelog = (modInfo.m_Changelog ?? string.Empty).Trim();
		string thumbnail = (modInfo.m_ThumbnailFilename ?? string.Empty).Trim();
		string[] tags = modInfo.m_Tags ?? Array.Empty<string>();
		IModsUploadSupport.ModInfo.ModDependency[] dependencies = modInfo.m_ModDependencies ??
			Array.Empty<IModsUploadSupport.ModInfo.ModDependency>();
		List<IModsUploadSupport.ExternalLinkData>? externalLinks = modInfo.m_ExternalLinks;

		StringBuilder dependencyBuilder = new StringBuilder();
		for (int i = 0; i < dependencies.Length; i++)
		{
			if (i > 0)
			{
				dependencyBuilder.Append(", ");
			}

			IModsUploadSupport.ModInfo.ModDependency dependency = dependencies[i];
			string version = (dependency.m_Version ?? string.Empty).Trim();
			if (string.IsNullOrWhiteSpace(version))
			{
				dependencyBuilder.Append(dependency.m_Id.ToString(CultureInfo.InvariantCulture));
			}
			else
			{
				dependencyBuilder.Append(dependency.m_Id.ToString(CultureInfo.InvariantCulture))
					.Append('@')
					.Append(version);
			}
		}

		StringBuilder builder = new StringBuilder(512);
		builder.Append("ModInfo: ")
			.Append("Display='").Append(displayName).Append("', ")
			.Append("ShortLen=").Append(shortDescription.Length.ToString(CultureInfo.InvariantCulture)).Append(", ")
			.Append("LongLen=").Append(longDescription.Length.ToString(CultureInfo.InvariantCulture)).Append(", ")
			.Append("Version='").Append(userVersion).Append("', ")
			.Append("Game='").Append(gameVersion).Append("', ")
			.Append("PublishedID=").Append(modInfo.m_PublishedID.ToString(CultureInfo.InvariantCulture)).Append(", ")
			.Append("Visibility=").Append(modInfo.m_Visibility.ToString()).Append(", ")
			.Append("Thumb='").Append(thumbnail).Append("', ")
			.Append("Tags=[").Append(string.Join(", ", tags)).Append("], ")
			.Append("Dependencies=[").Append(dependencyBuilder).Append("], ")
			.Append("ExternalLinks=").Append((externalLinks?.Count ?? 0).ToString(CultureInfo.InvariantCulture)).Append(", ")
			.Append("ChangeLogLen=").Append(changelog.Length.ToString(CultureInfo.InvariantCulture));
		return builder.ToString();
	}

	// Extract one compact error line from platform error payload.
	private static string GetDeveloperModuleUploadErrorDetail(IModsUploadSupport.ModError error)
	{
		List<string> parts = new List<string>(4);
		try
		{
			foreach (string line in error.GetLines())
			{
				string trimmed = (line ?? string.Empty).Trim();
				if (!string.IsNullOrWhiteSpace(trimmed))
				{
					parts.Add(trimmed);
				}
			}
		}
		catch
		{
			// Fall back to details/raw text.
		}

		string details = (error.m_Details ?? string.Empty).Trim();
		if (!string.IsNullOrWhiteSpace(details))
		{
			parts.Add(details);
		}

		string raw = (error.m_Raw ?? string.Empty).Trim();
		if (!string.IsNullOrWhiteSpace(raw))
		{
			parts.Add(raw);
		}

		for (int i = parts.Count - 1; i >= 1; i--)
		{
			for (int j = 0; j < i; j++)
			{
				if (string.Equals(parts[i], parts[j], StringComparison.OrdinalIgnoreCase))
				{
					parts.RemoveAt(i);
					break;
				}
			}
		}

		if (parts.Count == 0)
		{
			return string.Empty;
		}

		const int kMaxLength = 420;
		string merged = string.Join(" | ", parts);
		if (merged.Length > kMaxLength)
		{
			return merged.Substring(0, kMaxLength) + "...";
		}

		return merged;
	}

	// Recursively copy source directory contents into destination.
	private static bool TryCopyDirectoryContents(string sourceDirectory, string destinationDirectory, out string error)
	{
		error = string.Empty;
		if (string.IsNullOrWhiteSpace(sourceDirectory) || !Directory.Exists(sourceDirectory))
		{
			error = $"Source directory '{sourceDirectory}' was not found.";
			return false;
		}

		try
		{
			Directory.CreateDirectory(destinationDirectory);
			string[] files = Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories);
			for (int i = 0; i < files.Length; i++)
			{
				string sourceFile = files[i];
				string relative = Path.GetRelativePath(sourceDirectory, sourceFile);
				string destinationFile = Path.Combine(destinationDirectory, relative);
				string? parent = Path.GetDirectoryName(destinationFile);
				if (!string.IsNullOrWhiteSpace(parent))
				{
					Directory.CreateDirectory(parent);
				}

				File.Copy(sourceFile, destinationFile, overwrite: true);
			}

			return true;
		}
		catch (Exception ex)
		{
			error = ex.Message;
			return false;
		}
	}

	// Ensure generated module has a thumbnail image that can act as upload preview fallback.
	private static bool TryEnsureDeveloperModuleUploadThumbnail(string moduleRootPath, out string thumbnailPath, out string error)
	{
		thumbnailPath = Path.Combine(moduleRootPath, kDeveloperModuleUploadDefaultThumbnailFileName);
		error = string.Empty;
		if (File.Exists(thumbnailPath))
		{
			return true;
		}

		try
		{
			byte[] bytes = Convert.FromBase64String(kDeveloperModuleUploadDefaultThumbnailBase64);
			File.WriteAllBytes(thumbnailPath, bytes);
			return true;
		}
		catch (Exception ex)
		{
			error = $"Unable to create default thumbnail for upload: {ex.Message}";
			return false;
		}
	}

	// Supported static image formats for upload preview thumbnails.
	private static bool IsSupportedDeveloperModuleUploadThumbnailExtension(string extension)
	{
		string normalized = (extension ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(normalized))
		{
			return false;
		}

		return string.Equals(normalized, ".png", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(normalized, ".jpg", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(normalized, ".jpeg", StringComparison.OrdinalIgnoreCase);
	}

	// Human-readable short description for generated asset-module uploads.
	private static string BuildDeveloperModuleUploadShortDescription(string displayName)
	{
		string trimmed = string.IsNullOrWhiteSpace(displayName) ? string.Empty : displayName.Trim();
		if (string.IsNullOrWhiteSpace(trimmed))
		{
			return kDeveloperModuleUploadDefaultShortDescription;
		}

		string generated = $"{trimmed} - Audio Switcher asset module";
		if (generated.Length <= kDeveloperModuleUploadShortDescriptionMaxLength)
		{
			return generated;
		}

		string truncated = generated.Substring(0, kDeveloperModuleUploadShortDescriptionMaxLength).Trim();
		return string.IsNullOrWhiteSpace(truncated) ? kDeveloperModuleUploadDefaultShortDescription : truncated;
	}

	// Long description attached to generated asset-module uploads.
	private static string BuildDeveloperModuleUploadLongDescription(string displayName, string moduleId)
	{
		StringBuilder builder = new StringBuilder(256);
		builder.Append("Generated by Audio Switcher Developer Module Creation & Upload tools.");
		if (!string.IsNullOrWhiteSpace(displayName))
		{
			builder.Append(' ').Append("Display Name: ").Append(displayName.Trim()).Append('.');
		}

		if (!string.IsNullOrWhiteSpace(moduleId))
		{
			builder.Append(' ').Append("Module ID: ").Append(moduleId).Append('.');
		}

		builder.Append(' ').Append("Contains AudioSwitcherModule.json and audio assets under content/.");
		return builder.ToString();
	}

	// Return true when module folder has upload-ready manifest in content/.
	private static bool HasUploadReadyModuleManifest(string moduleRootPath)
	{
		if (string.IsNullOrWhiteSpace(moduleRootPath) || !Directory.Exists(moduleRootPath))
		{
			return false;
		}

		string manifestPath = Path.Combine(
			moduleRootPath,
			kDeveloperModuleUploadManifestRelativePath.Replace('/', Path.DirectorySeparatorChar));
		return File.Exists(manifestPath);
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

	// Copy selected city sound-set profile settings into generated module output.
	private static List<DeveloperModuleManifestSoundSetProfile> ExportSelectedSoundSetProfilesToModule(
		string moduleRootPath,
		ISet<string> includedSetIds,
		ref int skippedMissingSettingsFiles,
		ref int skippedCopyFailures)
	{
		List<DeveloperModuleManifestSoundSetProfile> exported = new List<DeveloperModuleManifestSoundSetProfile>();
		if (includedSetIds == null || includedSetIds.Count == 0)
		{
			return exported;
		}

		List<string> normalizedSetIds = includedSetIds
			.Select(static setId => CitySoundProfileRegistry.NormalizeSetId(setId))
			.Where(static setId => !string.IsNullOrWhiteSpace(setId))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.OrderBy(static setId => setId, StringComparer.OrdinalIgnoreCase)
			.ToList();
		for (int i = 0; i < normalizedSetIds.Count; i++)
		{
			string setId = normalizedSetIds[i];
			string relativeFolderPath = $"Profiles/{setId}";
			string outputFolderPath = Path.Combine(moduleRootPath, relativeFolderPath.Replace('/', Path.DirectorySeparatorChar));
			Directory.CreateDirectory(outputFolderPath);

			List<string> copiedFiles = new List<string>(s_DeveloperModuleSoundSetProfileSettingsFileNames.Length);
			for (int fileIndex = 0; fileIndex < s_DeveloperModuleSoundSetProfileSettingsFileNames.Length; fileIndex++)
			{
				string fileName = s_DeveloperModuleSoundSetProfileSettingsFileNames[fileIndex];
				string sourcePath = CitySoundProfileRegistry.GetSetSettingsPath(
					SettingsDirectory,
					setId,
					fileName,
					ensureDirectoryExists: false);
				if (!File.Exists(sourcePath))
				{
					skippedMissingSettingsFiles++;
					continue;
				}

				string outputPath = Path.Combine(outputFolderPath, fileName);
				try
				{
					File.Copy(sourcePath, outputPath, overwrite: true);
					copiedFiles.Add(fileName.Replace('\\', '/'));
				}
				catch (Exception ex)
				{
					skippedCopyFailures++;
					Log.Warn($"Failed to copy sound set profile settings '{fileName}' for set '{setId}'. {ex.Message}");
				}
			}

			if (copiedFiles.Count == 0)
			{
				TryDeleteDirectory(outputFolderPath);
				continue;
			}

			exported.Add(new DeveloperModuleManifestSoundSetProfile
			{
				SetId = setId,
				DisplayName = GetSoundSetDisplayName(setId),
				Folder = relativeFolderPath.Replace('\\', '/'),
				Files = copiedFiles
			});
		}

		return exported;
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
	// When trimEdgeSeparators is false, preserve leading/trailing separators for in-progress UI typing.
	private static string NormalizeDeveloperModuleId(string value, bool trimEdgeSeparators = true)
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

		if (trimEdgeSeparators)
		{
			normalized = normalized.Trim('-', '.', '_');
		}

		return string.IsNullOrWhiteSpace(normalized) ? kDeveloperModuleDefaultId : normalized;
	}

	// Normalize module version input to digits and periods only.
	// When trimEdgePeriods is false, preserve trailing periods for in-progress UI typing.
	private static string NormalizeDeveloperModuleVersion(string value, bool trimEdgePeriods = true)
	{
		string source = string.IsNullOrWhiteSpace(value)
			? kDeveloperModuleDefaultVersion
			: value.Trim();

		StringBuilder builder = new StringBuilder(source.Length);
		for (int i = 0; i < source.Length; i++)
		{
			char c = source[i];
			if (char.IsDigit(c) || c == '.')
			{
				builder.Append(c);
			}
		}

		string normalized = builder.ToString();
		while (normalized.Contains("..", StringComparison.Ordinal))
		{
			normalized = normalized.Replace("..", ".", StringComparison.Ordinal);
		}

		if (trimEdgePeriods)
		{
			normalized = normalized.Trim('.');
		}

		return string.IsNullOrWhiteSpace(normalized) ? kDeveloperModuleDefaultVersion : normalized;
	}

	// Normalize upload display-name metadata and enforce platform-safe length.
	private static string NormalizeDeveloperModuleUploadDisplayName(string value)
	{
		string normalized = NormalizeDeveloperModuleDisplayName(value);
		return TruncateDeveloperModuleUploadText(normalized, kDeveloperModuleUploadDisplayNameMaxLength, kDeveloperModuleDefaultDisplayName);
	}

	// Normalize upload short-description metadata and enforce platform-safe length.
	private static string NormalizeDeveloperModuleUploadShortDescription(string value)
	{
		string normalized = string.IsNullOrWhiteSpace(value)
			? kDeveloperModuleUploadDefaultShortDescription
			: value.Trim();
		return TruncateDeveloperModuleUploadText(normalized, kDeveloperModuleUploadShortDescriptionMaxLength, kDeveloperModuleUploadDefaultShortDescription);
	}

	// Normalize upload mod-version metadata and enforce platform-safe length.
	private static string NormalizeDeveloperModuleUploadVersion(string value)
	{
		string normalized = NormalizeDeveloperModuleVersion(value);
		return TruncateDeveloperModuleUploadText(normalized, kDeveloperModuleUploadVersionMaxLength, kDeveloperModuleDefaultVersion);
	}

	// Normalize upload game-version metadata and enforce platform-safe length.
	private static string NormalizeDeveloperModuleUploadGameVersion(string value)
	{
		string normalized = string.IsNullOrWhiteSpace(value)
			? kDeveloperModuleUploadDefaultRecommendedGameVersion
			: value.Trim();
		return TruncateDeveloperModuleUploadText(normalized, kDeveloperModuleUploadGameVersionMaxLength, kDeveloperModuleUploadDefaultRecommendedGameVersion);
	}

	// Trim and truncate one upload metadata text field with fallback support.
	private static string TruncateDeveloperModuleUploadText(string value, int maxLength, string fallback)
	{
		string normalized = (value ?? string.Empty).Trim();
		string fallbackValue = (fallback ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(normalized))
		{
			normalized = fallbackValue;
		}

		if (maxLength > 0 && normalized.Length > maxLength)
		{
			normalized = normalized.Substring(0, maxLength).Trim();
		}

		return string.IsNullOrWhiteSpace(normalized) ? fallbackValue : normalized;
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

	// Normalize optional upload description text for PDX page metadata.
	private static string NormalizeDeveloperModuleUploadDescription(string value)
	{
		string normalized = (value ?? string.Empty)
			.Replace("\r\n", "\n")
			.Replace('\r', '\n');
		if (string.IsNullOrWhiteSpace(normalized))
		{
			return string.Empty;
		}

		if (normalized.Length <= kDeveloperModuleUploadDescriptionMaxLength)
		{
			return normalized;
		}

		return normalized.Substring(0, kDeveloperModuleUploadDescriptionMaxLength);
	}

	// Normalize optional additional upload dependencies text.
	private static string NormalizeDeveloperModuleUploadAdditionalDependencies(string value)
	{
		string normalized = (value ?? string.Empty)
			.Replace("\r\n", "\n")
			.Replace('\r', '\n')
			.Trim();
		if (string.IsNullOrWhiteSpace(normalized))
		{
			return string.Empty;
		}

		if (normalized.Length <= kDeveloperModuleUploadAdditionalDependenciesMaxLength)
		{
			return normalized;
		}

		return normalized.Substring(0, kDeveloperModuleUploadAdditionalDependenciesMaxLength).Trim();
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

	// Normalize optional upload thumbnail path, preserving relative values.
	private static string NormalizeDeveloperModuleUploadThumbnailPath(string value)
	{
		string raw = (value ?? string.Empty).Trim().Trim('"');
		if (string.IsNullOrWhiteSpace(raw))
		{
			return string.Empty;
		}

		if (!Path.IsPathRooted(raw))
		{
			return raw;
		}

		try
		{
			return Path.GetFullPath(raw);
		}
		catch
		{
			return raw;
		}
	}

	// Normalize optional thumbnail-directory input into a full path when possible.
	private static string NormalizeDeveloperModuleUploadThumbnailDirectory(string value)
	{
		string raw = (value ?? string.Empty).Trim().Trim('"');
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

	// Clamp stored upload access-level value to supported enum values.
	private static DeveloperModuleUploadAccessLevel NormalizeDeveloperModuleUploadAccessLevel(int value)
	{
		switch ((DeveloperModuleUploadAccessLevel)value)
		{
			case DeveloperModuleUploadAccessLevel.Public:
			case DeveloperModuleUploadAccessLevel.Private:
			case DeveloperModuleUploadAccessLevel.Unlisted:
				return (DeveloperModuleUploadAccessLevel)value;
			default:
				return DeveloperModuleUploadAccessLevel.Private;
		}
	}

	// Clamp stored upload publish-mode value to supported enum values.
	private static DeveloperModuleUploadPublishMode NormalizeDeveloperModuleUploadPublishMode(int value)
	{
		switch ((DeveloperModuleUploadPublishMode)value)
		{
			case DeveloperModuleUploadPublishMode.CreateNew:
			case DeveloperModuleUploadPublishMode.UpdateExisting:
				return (DeveloperModuleUploadPublishMode)value;
			default:
				return DeveloperModuleUploadPublishMode.CreateNew;
		}
	}

	// Normalize existing published ID input used in update-existing mode.
	private static int NormalizeDeveloperModuleUploadExistingPublishedId(string value)
	{
		string raw = (value ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(raw))
		{
			return 0;
		}

		if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) && parsed > 0)
		{
			return parsed;
		}

		return 0;
	}

	// Map UI upload access level to platform visibility enum.
	private static IModsUploadSupport.Visibility ConvertDeveloperModuleUploadAccessLevel(DeveloperModuleUploadAccessLevel value)
	{
		switch (value)
		{
			case DeveloperModuleUploadAccessLevel.Public:
				return IModsUploadSupport.Visibility.Public;
			case DeveloperModuleUploadAccessLevel.Private:
				return IModsUploadSupport.Visibility.Private;
			case DeveloperModuleUploadAccessLevel.Unlisted:
				return IModsUploadSupport.Visibility.Unlisted;
			default:
				return IModsUploadSupport.Visibility.Private;
		}
	}

	// Convert upload publish mode into a human-readable label.
	private static string GetDeveloperModuleUploadPublishModeLabel(DeveloperModuleUploadPublishMode value)
	{
		switch (value)
		{
			case DeveloperModuleUploadPublishMode.CreateNew:
				return "Create New";
			case DeveloperModuleUploadPublishMode.UpdateExisting:
				return "Update Existing";
			default:
				return "Create New";
		}
	}

	// Convert upload access level into a human-readable label.
	private static string GetDeveloperModuleUploadAccessLevelLabel(DeveloperModuleUploadAccessLevel value)
	{
		switch (value)
		{
			case DeveloperModuleUploadAccessLevel.Public:
				return "Public";
			case DeveloperModuleUploadAccessLevel.Private:
				return "Private";
			case DeveloperModuleUploadAccessLevel.Unlisted:
				return "Unlisted";
			default:
				return "Private";
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
		List<string> buildingKeys = GetEligibleLocalModuleKeys(DeveloperAudioDomain.Building);
		List<string> transitAnnouncementKeys = GetEligibleLocalModuleKeys(DeveloperAudioDomain.TransitAnnouncement);
		List<string> soundSetProfileIds = GetEligibleDeveloperModuleSoundSetProfileIds();

		bool changed = false;
		changed |= SyncDeveloperModuleDomainState(DeveloperAudioDomain.Siren, sirenKeys);
		changed |= SyncDeveloperModuleDomainState(DeveloperAudioDomain.VehicleEngine, engineKeys);
		changed |= SyncDeveloperModuleDomainState(DeveloperAudioDomain.Ambient, ambientKeys);
		changed |= SyncDeveloperModuleDomainState(DeveloperAudioDomain.Building, buildingKeys);
		changed |= SyncDeveloperModuleDomainState(DeveloperAudioDomain.TransitAnnouncement, transitAnnouncementKeys);
		changed |= SyncDeveloperModuleSoundSetProfileState(soundSetProfileIds);

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

			for (int i = 0; i < buildingKeys.Count; i++)
			{
				if (s_DeveloperModuleIncludedBuildings.Add(buildingKeys[i]))
				{
					changed = true;
				}
			}

			for (int i = 0; i < transitAnnouncementKeys.Count; i++)
			{
				if (s_DeveloperModuleIncludedTransitAnnouncements.Add(transitAnnouncementKeys[i]))
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

	// Validate sound-set profile selection and included profile set IDs against current registry state.
	private static bool SyncDeveloperModuleSoundSetProfileState(IReadOnlyList<string> eligibleSetIds)
	{
		HashSet<string> eligible = new HashSet<string>(eligibleSetIds, StringComparer.OrdinalIgnoreCase);
		bool changed = false;

		List<string> staleIncluded = new List<string>();
		foreach (string setId in s_DeveloperModuleIncludedSoundSetProfiles)
		{
			if (!eligible.Contains(setId))
			{
				staleIncluded.Add(setId);
			}
		}

		for (int i = 0; i < staleIncluded.Count; i++)
		{
			if (s_DeveloperModuleIncludedSoundSetProfiles.Remove(staleIncluded[i]))
			{
				changed = true;
			}
		}

		string normalizedSelection = CitySoundProfileRegistry.NormalizeSetId(s_DeveloperModuleSelectedSoundSetProfileId);
		if (string.IsNullOrWhiteSpace(normalizedSelection) || !eligible.Contains(normalizedSelection))
		{
			string next = eligibleSetIds.Count > 0 ? eligibleSetIds[0] : string.Empty;
			if (!string.Equals(s_DeveloperModuleSelectedSoundSetProfileId, next, StringComparison.OrdinalIgnoreCase))
			{
				s_DeveloperModuleSelectedSoundSetProfileId = next;
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

	// Enumerate sound-set profile IDs currently available in the city sound-set registry.
	private static List<string> GetEligibleDeveloperModuleSoundSetProfileIds()
	{
		List<string> setIds = new List<string>();
		if (s_CitySoundProfileRegistry?.Sets != null)
		{
			foreach (KeyValuePair<string, CitySoundProfileSet> pair in s_CitySoundProfileRegistry.Sets)
			{
				string normalized = CitySoundProfileRegistry.NormalizeSetId(pair.Key);
				if (string.IsNullOrWhiteSpace(normalized))
				{
					continue;
				}

				if (!setIds.Contains(normalized, StringComparer.OrdinalIgnoreCase))
				{
					setIds.Add(normalized);
				}
			}
		}

		if (!setIds.Contains(CitySoundProfileRegistry.DefaultSetId, StringComparer.OrdinalIgnoreCase))
		{
			setIds.Add(CitySoundProfileRegistry.DefaultSetId);
		}

		setIds.Sort((left, right) =>
		{
			string leftDisplay = GetSoundSetDisplayName(left);
			string rightDisplay = GetSoundSetDisplayName(right);
			int byDisplay = string.Compare(leftDisplay, rightDisplay, StringComparison.OrdinalIgnoreCase);
			return byDisplay != 0
				? byDisplay
				: string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
		});
		return setIds;
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
			case DeveloperAudioDomain.Ambient:
				return AmbientConfig.CustomProfiles;
			case DeveloperAudioDomain.Building:
				return BuildingConfig.CustomProfiles;
			case DeveloperAudioDomain.TransitAnnouncement:
				return TransitAnnouncementConfig.CustomProfiles;
			default:
				throw new ArgumentOutOfRangeException(nameof(domain), domain, "Unknown local audio domain.");
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
			case DeveloperAudioDomain.Ambient:
				return string.IsNullOrWhiteSpace(AmbientConfig.CustomFolderName)
					? AmbientCustomFolderName
					: AmbientConfig.CustomFolderName;
			case DeveloperAudioDomain.Building:
				return string.IsNullOrWhiteSpace(BuildingConfig.CustomFolderName)
					? BuildingCustomFolderName
					: BuildingConfig.CustomFolderName;
			case DeveloperAudioDomain.TransitAnnouncement:
				return string.IsNullOrWhiteSpace(TransitAnnouncementConfig.CustomFolderName)
					? TransitAnnouncementCustomFolderName
					: TransitAnnouncementConfig.CustomFolderName;
			default:
				throw new ArgumentOutOfRangeException(nameof(domain), domain, "Unknown local audio domain.");
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
			case DeveloperAudioDomain.Ambient:
				return ref s_DeveloperModuleSelectedLocalAmbientKey;
			case DeveloperAudioDomain.Building:
				return ref s_DeveloperModuleSelectedLocalBuildingKey;
			case DeveloperAudioDomain.TransitAnnouncement:
				return ref s_DeveloperModuleSelectedLocalTransitAnnouncementKey;
			default:
				throw new ArgumentOutOfRangeException(nameof(domain), domain, "Unknown local audio domain.");
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
			case DeveloperAudioDomain.Ambient:
				return s_DeveloperModuleIncludedAmbient;
			case DeveloperAudioDomain.Building:
				return s_DeveloperModuleIncludedBuildings;
			case DeveloperAudioDomain.TransitAnnouncement:
				return s_DeveloperModuleIncludedTransitAnnouncements;
			default:
				throw new ArgumentOutOfRangeException(nameof(domain), domain, "Unknown local audio domain.");
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
			case DeveloperAudioDomain.Ambient:
				return "No local ambient files found";
			case DeveloperAudioDomain.Building:
				return "No local building files found";
			case DeveloperAudioDomain.TransitAnnouncement:
				return "No local transit announcement files found";
			default:
				throw new ArgumentOutOfRangeException(nameof(domain), domain, "Unknown local audio domain.");
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
			case DeveloperAudioDomain.Ambient:
				return "ambient";
			case DeveloperAudioDomain.Building:
				return "building";
			case DeveloperAudioDomain.TransitAnnouncement:
				return "transit announcement";
			default:
				throw new ArgumentOutOfRangeException(nameof(domain), domain, "Unknown local audio domain.");
		}
	}

	// Aggregate count of currently included local module-builder files across all domains.
	private static int GetTotalDeveloperModuleIncludedCount()
	{
		return s_DeveloperModuleIncludedSirens.Count +
			s_DeveloperModuleIncludedEngines.Count +
			s_DeveloperModuleIncludedAmbient.Count +
			s_DeveloperModuleIncludedBuildings.Count +
			s_DeveloperModuleIncludedTransitAnnouncements.Count;
	}

	// Aggregate count of currently included sound-set profiles for module-builder export.
	private static int GetTotalDeveloperModuleIncludedSoundSetProfileCount()
	{
		return s_DeveloperModuleIncludedSoundSetProfiles.Count;
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

	// Build a readable sound-set profile label with stable set-ID suffix.
	private static string BuildDeveloperModuleSoundSetProfileDisplayName(string setId)
	{
		string normalized = CitySoundProfileRegistry.NormalizeSetId(setId);
		string displayName = GetSoundSetDisplayName(normalized);
		return $"{displayName} ({normalized})";
	}

	// Status setter used by asset-module upload actions.
	private static void SetDeveloperModuleUploadStatus(string message, bool isWarning)
	{
		s_DeveloperModuleUploadStatus = string.IsNullOrWhiteSpace(message) ? "Action completed." : message.Trim();
		if (isWarning)
		{
			Log.Warn(s_DeveloperModuleUploadStatus);
		}
		else
		{
			Log.Info(s_DeveloperModuleUploadStatus);
		}

		OptionsVersion++;
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
		string moduleVersion,
		int sirenCount,
		int engineCount,
		int ambientCount,
		int buildingCount,
		int transitAnnouncementCount,
		int soundSetProfileCount,
		bool uploadReadyAssetPackage)
	{
		StringBuilder builder = new StringBuilder(512);
		builder.AppendLine("Audio Switcher Generated Module");
		builder.AppendLine();
		builder.Append("Display Name: ").AppendLine(displayName);
		builder.Append("Module ID: ").AppendLine(moduleId);
		builder.Append("Module Version: ").AppendLine(moduleVersion);
		builder.AppendLine();
		builder.Append("Sirens: ").AppendLine(sirenCount.ToString(CultureInfo.InvariantCulture));
		builder.Append("Vehicle Engines: ").AppendLine(engineCount.ToString(CultureInfo.InvariantCulture));
		builder.Append("Ambient Sounds: ").AppendLine(ambientCount.ToString(CultureInfo.InvariantCulture));
		builder.Append("Building Sounds: ").AppendLine(buildingCount.ToString(CultureInfo.InvariantCulture));
		builder.Append("Transit Announcements: ").AppendLine(transitAnnouncementCount.ToString(CultureInfo.InvariantCulture));
		builder.Append("Sound Set Profiles: ").AppendLine(soundSetProfileCount.ToString(CultureInfo.InvariantCulture));
		builder.AppendLine();
		builder.AppendLine(uploadReadyAssetPackage
			? "Generated by Audio Switcher Developer > Module Creation & Upload (Build + Upload)."
			: "Generated by Audio Switcher Developer > Module Creation & Upload (Build Local).");
		if (uploadReadyAssetPackage)
		{
			builder.AppendLine();
			builder.AppendLine("Upload-ready layout:");
			builder.Append(" - ").Append(kDeveloperModuleAssetContentFolderName).AppendLine("/AudioSwitcherModule.json");
			builder.Append(" - ").Append(kDeveloperModuleAssetContentFolderName).AppendLine("/Audio/");
			builder.Append(" - ").Append(kDeveloperModuleAssetContentFolderName).AppendLine("/Profiles/");
			builder.AppendLine("Upload this folder to PDX Mods as an asset package.");
		}

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

	// Directory enumeration helper used by upload-ready module discovery.
	private static string[] EnumerateDirectoriesSafe(string directoryPath)
	{
		if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
		{
			return Array.Empty<string>();
		}

		try
		{
			return Directory.GetDirectories(directoryPath);
		}
		catch
		{
			return Array.Empty<string>();
		}
	}

	// File enumeration helper used by upload-thumbnail candidate discovery.
	private static string[] EnumerateFilesSafe(string directoryPath)
	{
		if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
		{
			return Array.Empty<string>();
		}

		try
		{
			return Directory.GetFiles(directoryPath, "*", SearchOption.TopDirectoryOnly);
		}
		catch
		{
			return Array.Empty<string>();
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
			case DeveloperAudioDomain.Ambient:
				return s_DetectedAmbientAudio;
			case DeveloperAudioDomain.Building:
				return s_DetectedBuildingAudio;
			default:
				throw new ArgumentOutOfRangeException(nameof(domain), domain, "Unknown detected audio domain.");
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
			case DeveloperAudioDomain.Ambient:
				return ref s_DeveloperSelectedAmbientKey;
			case DeveloperAudioDomain.Building:
				return ref s_DeveloperSelectedBuildingKey;
			default:
				throw new ArgumentOutOfRangeException(nameof(domain), domain, "Unknown detected audio domain.");
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
			case DeveloperAudioDomain.Ambient:
				return ref s_DeveloperAmbientStatus;
			case DeveloperAudioDomain.Building:
				return ref s_DeveloperBuildingStatus;
			default:
				throw new ArgumentOutOfRangeException(nameof(domain), domain, "Unknown detected audio domain.");
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
			case DeveloperAudioDomain.Ambient:
				return "ambient sound";
			case DeveloperAudioDomain.Building:
				return "building sound";
			default:
				throw new ArgumentOutOfRangeException(nameof(domain), domain, "Unknown detected audio domain.");
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
			case DeveloperAudioDomain.Ambient:
				return "ambient sounds";
			case DeveloperAudioDomain.Building:
				return "building sounds";
			default:
				throw new ArgumentOutOfRangeException(nameof(domain), domain, "Unknown detected audio domain.");
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
			case DeveloperAudioDomain.Ambient:
				return "No detected ambient sounds";
			case DeveloperAudioDomain.Building:
				return "No detected building sounds";
			default:
				throw new ArgumentOutOfRangeException(nameof(domain), domain, "Unknown detected audio domain.");
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
			case DeveloperAudioDomain.Ambient:
				return "No detected ambient sounds are available yet. Load a map/editor session to detect sounds.";
			case DeveloperAudioDomain.Building:
				return "No detected building sounds are available yet. Load a map/editor session to detect sounds.";
			default:
				throw new ArgumentOutOfRangeException(nameof(domain), domain, "Unknown detected audio domain.");
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

	private static string FormatFloat(float value)
	{
		return value.ToString("0.###", CultureInfo.InvariantCulture);
	}

	// In-memory view of one sound-set profile snapshot and its domain settings files.
	private sealed class DeveloperSoundSetProfileSnapshot
	{
		public string SetId { get; set; } = CitySoundProfileRegistry.DefaultSetId;

		public string DisplayName { get; set; } = CitySoundProfileRegistry.DefaultSetDisplayName;

		public Dictionary<DeveloperAudioDomain, string> SettingsPathByDomain { get; } = new Dictionary<DeveloperAudioDomain, string>();
	}

	// Aggregated metadata used when uploading generated asset modules to PDX Mods.
	private sealed class DeveloperModuleUploadMetadata
	{
		public DeveloperModuleUploadMetadata(
			string displayName,
			string moduleId,
			string shortDescription,
			string longDescription,
			string modVersion,
			string gameVersion,
			string thumbnailPath)
		{
			DisplayName = NormalizeDeveloperModuleUploadDisplayName(displayName);
			ModuleId = string.IsNullOrWhiteSpace(moduleId) ? kDeveloperModuleDefaultId : moduleId.Trim();
			ShortDescription = NormalizeDeveloperModuleUploadShortDescription(shortDescription);
			LongDescription = string.IsNullOrWhiteSpace(longDescription)
				? ShortDescription
				: NormalizeDeveloperModuleUploadDescription(longDescription);
			ModVersion = NormalizeDeveloperModuleUploadVersion(modVersion);
			GameVersion = NormalizeDeveloperModuleUploadGameVersion(gameVersion);
			ThumbnailPath = (thumbnailPath ?? string.Empty).Trim();
		}

		public string DisplayName { get; }

		public string ModuleId { get; }

		public string ShortDescription { get; }

		public string LongDescription { get; }

		public string ModVersion { get; }

		public string GameVersion { get; }

		public string ThumbnailPath { get; }
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

		[DataMember(Order = 4, Name = "version")]
		public string Version { get; set; } = string.Empty;

		[DataMember(Order = 5, Name = "sirens")]
		public List<DeveloperModuleManifestEntry> Sirens { get; set; } = new List<DeveloperModuleManifestEntry>();

		[DataMember(Order = 6, Name = "vehicleEngines")]
		public List<DeveloperModuleManifestEntry> VehicleEngines { get; set; } = new List<DeveloperModuleManifestEntry>();

		[DataMember(Order = 7, Name = "ambient")]
		public List<DeveloperModuleManifestEntry> Ambient { get; set; } = new List<DeveloperModuleManifestEntry>();

		[DataMember(Order = 8, Name = "buildings")]
		public List<DeveloperModuleManifestEntry> Buildings { get; set; } = new List<DeveloperModuleManifestEntry>();

		[DataMember(Order = 9, Name = "transitAnnouncements")]
		public List<DeveloperModuleManifestEntry> TransitAnnouncements { get; set; } = new List<DeveloperModuleManifestEntry>();

		[DataMember(Order = 10, Name = "soundSetProfiles")]
		public List<DeveloperModuleManifestSoundSetProfile> SoundSetProfiles { get; set; } = new List<DeveloperModuleManifestSoundSetProfile>();
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

	[DataContract]
	// One generated sound-set profile snapshot included in a module package.
	private sealed class DeveloperModuleManifestSoundSetProfile
	{
		[DataMember(Order = 1, Name = "setId")]
		public string SetId { get; set; } = CitySoundProfileRegistry.DefaultSetId;

		[DataMember(Order = 2, Name = "displayName")]
		public string DisplayName { get; set; } = CitySoundProfileRegistry.DefaultSetDisplayName;

		[DataMember(Order = 3, Name = "folder")]
		public string Folder { get; set; } = string.Empty;

		[DataMember(Order = 4, Name = "files")]
		public List<string> Files { get; set; } = new List<string>();
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
