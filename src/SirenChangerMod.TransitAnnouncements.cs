using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.UI.Widgets;
using UnityEngine;

namespace SirenChanger;

// Fixed slot IDs used by settings UI and runtime playback routing.
internal enum TransitAnnouncementSlot
{
	TrainArrival = 0,
	TrainDeparture = 1,
	BusArrival = 2,
	BusDeparture = 3,
	MetroArrival = 4,
	MetroDeparture = 5,
	TramArrival = 6,
	TramDeparture = 7
}

// Result type for loading one transit announcement clip.
internal enum TransitAnnouncementLoadStatus
{
	Success,
	NotConfigured,
	Pending,
	Failure
}

// Transit-announcement domain config + options helpers.
public sealed partial class SirenChangerMod
{
	internal const string TransitAnnouncementSettingsFileName = "TransitAnnouncementSettings.json";

	internal const string TransitAnnouncementCustomFolderName = "Custom Announcements";

	// Older builds used different folder names; keep them discoverable after upgrades.
	private static readonly string[] s_TransitAnnouncementLegacyFolderNames =
	{
		"Custom Transit Announcements",
		"Transit Announcements"
	};

	internal static AudioReplacementDomainConfig TransitAnnouncementConfig { get; private set; } = AudioReplacementDomainConfig.CreateDefault(TransitAnnouncementCustomFolderName);

	// Canonical target keys used as stable slots in TargetSelections.
	private static readonly string[] s_TransitAnnouncementTargetKeys =
	{
		"train.arrival",
		"train.departure",
		"bus.arrival",
		"bus.departure",
		"metro.arrival",
		"metro.departure",
		"tram.arrival",
		"tram.departure"
	};

	private static int s_TransitAnnouncementDropdownCacheVersion = -1;

	private static DropdownItem<string>[] s_TransitAnnouncementDropdownWithDefault = Array.Empty<DropdownItem<string>>();

	// Sync transit-announcement custom files to profile keys and refresh scan metadata.
	internal static bool SyncCustomTransitAnnouncementCatalog(bool saveIfChanged, bool forceStatusRefresh = false)
	{
		TransitAnnouncementConfig.Normalize(TransitAnnouncementCustomFolderName);
		bool adoptedLegacyFolder = TryAdoptLegacyTransitAnnouncementFolder();
		bool slotTargetChanged = NormalizeTransitAnnouncementTargets();
		bool moduleCatalogChanged = RefreshAudioModuleCatalog();

		AudioDomainCatalogSyncResult result = AudioDomainCatalogSync.Synchronize(
			TransitAnnouncementConfig,
			SettingsDirectory,
			TransitAnnouncementCustomFolderName,
			SirenSfxProfile.CreateFallback(),
			Log,
			GetAudioModuleProfileKeys(DeveloperAudioDomain.TransitAnnouncement),
			key => TryGetAudioModuleProfileTemplate(DeveloperAudioDomain.TransitAnnouncement, key, out SirenSfxProfile profile)
				? profile
				: null);
		bool catalogChanged = result.ConfigChanged;
		bool implicitModuleProfilesChanged = RefreshImplicitModuleTemplateProfiles(
			DeveloperAudioDomain.TransitAnnouncement,
			TransitAnnouncementConfig.CustomProfiles,
			SirenSfxProfile.CreateFallback());
		bool scanMetadataChanged = UpdateDomainCatalogScanMetadata(TransitAnnouncementConfig, result, forceStatusRefresh);
		bool slotTargetChangedAfterSync = NormalizeTransitAnnouncementTargets();
		if (catalogChanged || implicitModuleProfilesChanged || adoptedLegacyFolder || slotTargetChanged || slotTargetChangedAfterSync || scanMetadataChanged || moduleCatalogChanged)
		{
			if (saveIfChanged && (catalogChanged || implicitModuleProfilesChanged || adoptedLegacyFolder || slotTargetChanged || slotTargetChangedAfterSync || scanMetadataChanged))
			{
				SaveConfig();
			}

			if (catalogChanged || implicitModuleProfilesChanged || adoptedLegacyFolder || slotTargetChanged || slotTargetChangedAfterSync)
			{
				ConfigVersion++;
			}

			NotifyOptionsCatalogChanged();
		}

		return catalogChanged || implicitModuleProfilesChanged || adoptedLegacyFolder || slotTargetChanged || slotTargetChangedAfterSync;
	}

	// Rescan custom announcement files from options UI.
	internal static void RefreshCustomTransitAnnouncementsFromOptions()
	{
		// Some no-op scans can end up not incrementing OptionsVersion; guarantee one UI refresh per click.
		int optionsVersionBefore = OptionsVersion;
		SyncCustomTransitAnnouncementCatalog(saveIfChanged: true, forceStatusRefresh: true);
		if (OptionsVersion == optionsVersionBefore)
		{
			NotifyOptionsCatalogChanged();
		}
	}

	// Build dropdown items for transit-announcement slot selectors.
	internal static DropdownItem<string>[] BuildTransitAnnouncementDropdownItems()
	{
		EnsureTransitAnnouncementDropdownCacheCurrent();
		return s_TransitAnnouncementDropdownWithDefault;
	}

	// True when at least one custom announcement file is currently available.
	internal static bool HasTransitAnnouncementProfiles()
	{
		return TransitAnnouncementConfig.CustomProfiles.Count > 0;
	}

	// Status text for transit announcement file scans.
	internal static string GetTransitAnnouncementCatalogScanStatusText()
	{
		string folderName = string.IsNullOrWhiteSpace(TransitAnnouncementConfig.CustomFolderName)
			? TransitAnnouncementCustomFolderName
			: TransitAnnouncementConfig.CustomFolderName;
		// Show the resolved folder to make file-placement/debug issues obvious in UI.
		string folderPath = SirenPathUtils.GetCustomSirensDirectory(SettingsDirectory, folderName, ensureExists: false);
		return $"{BuildDomainCatalogScanStatusText(TransitAnnouncementConfig, "Rescan Custom Announcement Files")}\nFolder: {folderPath}";
	}

	// Read one slot selection value.
	internal static string GetTransitAnnouncementSelection(TransitAnnouncementSlot slot)
	{
		return TransitAnnouncementConfig.GetTargetSelection(GetTransitAnnouncementTargetKey(slot));
	}

	// Update one slot selection value.
	internal static void SetTransitAnnouncementSelection(TransitAnnouncementSlot slot, string selection)
	{
		TransitAnnouncementConfig.SetTargetSelection(GetTransitAnnouncementTargetKey(slot), selection);
	}

	// Resolve and load one slot clip using current config selections.
	internal static TransitAnnouncementLoadStatus TryLoadTransitAnnouncementClip(
		TransitAnnouncementSlot slot,
		out AudioClip clip,
		out SirenSfxProfile profile,
		out string message)
	{
		clip = null!;
		profile = SirenSfxProfile.CreateFallback();
		message = string.Empty;

		if (!TransitAnnouncementConfig.Enabled)
		{
			message = "Transit announcements are disabled.";
			return TransitAnnouncementLoadStatus.NotConfigured;
		}

		string primarySelection = AudioReplacementDomainConfig.NormalizeProfileKey(GetTransitAnnouncementSelection(slot));
		TransitAnnouncementLoadStatus primaryStatus = TryLoadTransitAnnouncementSelection(
			primarySelection,
			out clip,
			out profile,
			out string primaryMessage);
		if (primaryStatus == TransitAnnouncementLoadStatus.Success ||
			primaryStatus == TransitAnnouncementLoadStatus.Pending ||
			primaryStatus == TransitAnnouncementLoadStatus.NotConfigured)
		{
			message = primaryMessage;
			return primaryStatus;
		}

		switch (TransitAnnouncementConfig.MissingSelectionFallbackBehavior)
		{
			case SirenFallbackBehavior.Mute:
				message = $"Primary announcement '{FormatSirenDisplayName(primarySelection)}' failed and fallback behavior is Mute.";
				return TransitAnnouncementLoadStatus.NotConfigured;
			case SirenFallbackBehavior.AlternateCustomSiren:
			{
				string alternateSelection = AudioReplacementDomainConfig.NormalizeProfileKey(TransitAnnouncementConfig.AlternateFallbackSelection);
				if (AudioReplacementDomainConfig.IsDefaultSelection(alternateSelection) || string.IsNullOrWhiteSpace(alternateSelection))
				{
					message =
						$"Primary announcement '{FormatSirenDisplayName(primarySelection)}' failed ({primaryMessage}). " +
						"Alternate fallback is configured but Alternate Announcement is set to Default.";
					return TransitAnnouncementLoadStatus.Failure;
				}

				if (string.Equals(alternateSelection, primarySelection, StringComparison.OrdinalIgnoreCase))
				{
					message =
						$"Primary announcement '{FormatSirenDisplayName(primarySelection)}' failed ({primaryMessage}). " +
						$"Alternate fallback points to the same selection '{FormatSirenDisplayName(alternateSelection)}'.";
					return TransitAnnouncementLoadStatus.Failure;
				}

				TransitAnnouncementLoadStatus alternateStatus = TryLoadTransitAnnouncementSelection(
					alternateSelection,
					out clip,
					out profile,
					out string alternateMessage);
				if (alternateStatus == TransitAnnouncementLoadStatus.Success)
				{
					message =
						$"Using alternate fallback announcement '{FormatSirenDisplayName(alternateSelection)}' " +
						$"after '{FormatSirenDisplayName(primarySelection)}' failed.";
					return TransitAnnouncementLoadStatus.Success;
				}

				if (alternateStatus == TransitAnnouncementLoadStatus.Pending)
				{
					message = alternateMessage;
					return TransitAnnouncementLoadStatus.Pending;
				}

				message =
					$"Primary announcement '{FormatSirenDisplayName(primarySelection)}' failed ({primaryMessage}). " +
					$"Alternate fallback '{FormatSirenDisplayName(alternateSelection)}' failed ({alternateMessage}).";
				return TransitAnnouncementLoadStatus.Failure;
			}
			default:
				message = $"Primary announcement '{FormatSirenDisplayName(primarySelection)}' failed ({primaryMessage}).";
				return TransitAnnouncementLoadStatus.NotConfigured;
		}
	}

	// Resolve and load one explicit transit announcement selection without fallback behavior.
	private static TransitAnnouncementLoadStatus TryLoadTransitAnnouncementSelection(
		string normalizedSelection,
		out AudioClip clip,
		out SirenSfxProfile profile,
		out string message)
	{
		clip = null!;
		profile = SirenSfxProfile.CreateFallback();
		message = string.Empty;

		if (AudioReplacementDomainConfig.IsDefaultSelection(normalizedSelection) || string.IsNullOrWhiteSpace(normalizedSelection))
		{
			message = "No custom announcement is selected.";
			return TransitAnnouncementLoadStatus.NotConfigured;
		}

		if (!TryResolveAudioProfilePath(
			DeveloperAudioDomain.TransitAnnouncement,
			TransitAnnouncementConfig.CustomFolderName,
			normalizedSelection,
			out string path))
		{
			message = $"Selected announcement '{normalizedSelection}' could not be found.";
			return TransitAnnouncementLoadStatus.Failure;
		}

		if (!TransitAnnouncementConfig.TryGetProfile(normalizedSelection, out profile))
		{
			profile = SirenSfxProfile.CreateFallback();
		}

		WaveClipLoader.AudioLoadStatus loadStatus = WaveClipLoader.LoadAudio(path, out clip, out string error);
		if (loadStatus == WaveClipLoader.AudioLoadStatus.Pending)
		{
			message = $"Announcement '{FormatSirenDisplayName(normalizedSelection)}' is still loading.";
			return TransitAnnouncementLoadStatus.Pending;
		}

		if (loadStatus != WaveClipLoader.AudioLoadStatus.Success)
		{
			message = $"Failed to load announcement '{normalizedSelection}': {error}";
			return TransitAnnouncementLoadStatus.Failure;
		}

		message = $"Loaded announcement '{FormatSirenDisplayName(normalizedSelection)}'.";
		return TransitAnnouncementLoadStatus.Success;
	}

	// Keep slot target keys present so per-slot selections persist safely.
	internal static bool NormalizeTransitAnnouncementTargets()
	{
		return TransitAnnouncementConfig.SynchronizeTargets(s_TransitAnnouncementTargetKeys);
	}

	// Human-readable slot label used in logs and status messages.
	internal static string GetTransitAnnouncementSlotLabel(TransitAnnouncementSlot slot)
	{
		switch (slot)
		{
			case TransitAnnouncementSlot.TrainArrival:
				return "Train Arrival";
			case TransitAnnouncementSlot.TrainDeparture:
				return "Train Departure";
			case TransitAnnouncementSlot.BusArrival:
				return "Bus Arrival";
			case TransitAnnouncementSlot.BusDeparture:
				return "Bus Departure";
			case TransitAnnouncementSlot.MetroArrival:
				return "Metro Arrival";
			case TransitAnnouncementSlot.MetroDeparture:
				return "Metro Departure";
			case TransitAnnouncementSlot.TramArrival:
				return "Tram Arrival";
			case TransitAnnouncementSlot.TramDeparture:
				return "Tram Departure";
			default:
				return "Transit Announcement";
		}
	}

	// Rebuild dropdown cache when options version changes.
	private static void EnsureTransitAnnouncementDropdownCacheCurrent()
	{
		if (s_TransitAnnouncementDropdownCacheVersion == OptionsVersion &&
			s_TransitAnnouncementDropdownWithDefault.Length > 0)
		{
			return;
		}

		List<string> keys = TransitAnnouncementConfig.CustomProfiles.Keys
			.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
			.ToList();
		List<DropdownItem<string>> options = new List<DropdownItem<string>>(keys.Count + 1)
		{
			new DropdownItem<string>
			{
				value = SirenReplacementConfig.DefaultSelectionToken,
				displayName = "Default (None)"
			}
		};

		for (int i = 0; i < keys.Count; i++)
		{
			string key = keys[i];
			options.Add(new DropdownItem<string>
			{
				value = key,
				displayName = FormatSirenDisplayName(key)
			});
		}

		if (keys.Count == 0)
		{
			options.Add(new DropdownItem<string>
			{
				value = string.Empty,
				displayName = "No custom announcements found",
				disabled = true
			});
		}

		s_TransitAnnouncementDropdownWithDefault = options.ToArray();
		s_TransitAnnouncementDropdownCacheVersion = OptionsVersion;
	}

	// Map a fixed slot enum to its stable target-selection key.
	private static string GetTransitAnnouncementTargetKey(TransitAnnouncementSlot slot)
	{
		switch (slot)
		{
			case TransitAnnouncementSlot.TrainArrival:
				return s_TransitAnnouncementTargetKeys[0];
			case TransitAnnouncementSlot.TrainDeparture:
				return s_TransitAnnouncementTargetKeys[1];
			case TransitAnnouncementSlot.BusArrival:
				return s_TransitAnnouncementTargetKeys[2];
			case TransitAnnouncementSlot.BusDeparture:
				return s_TransitAnnouncementTargetKeys[3];
			case TransitAnnouncementSlot.MetroArrival:
				return s_TransitAnnouncementTargetKeys[4];
			case TransitAnnouncementSlot.MetroDeparture:
				return s_TransitAnnouncementTargetKeys[5];
			case TransitAnnouncementSlot.TramArrival:
				return s_TransitAnnouncementTargetKeys[6];
			case TransitAnnouncementSlot.TramDeparture:
				return s_TransitAnnouncementTargetKeys[7];
			default:
				return string.Empty;
		}
	}

	// When users upgrade from older builds, keep transit file discovery working from legacy folder names.
	private static bool TryAdoptLegacyTransitAnnouncementFolder()
	{
		string currentFolder = string.IsNullOrWhiteSpace(TransitAnnouncementConfig.CustomFolderName)
			? TransitAnnouncementCustomFolderName
			: TransitAnnouncementConfig.CustomFolderName;
		if (HasLocalTransitAnnouncementFiles(currentFolder))
		{
			return false;
		}

		if (!string.Equals(currentFolder, TransitAnnouncementCustomFolderName, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		for (int i = 0; i < s_TransitAnnouncementLegacyFolderNames.Length; i++)
		{
			string legacyFolder = s_TransitAnnouncementLegacyFolderNames[i];
			if (!HasLocalTransitAnnouncementFiles(legacyFolder))
			{
				continue;
			}

			TransitAnnouncementConfig.CustomFolderName = legacyFolder;
			Log.Info($"Transit announcement scan adopted legacy folder '{legacyFolder}'.");
			return true;
		}

		return false;
	}

	// Fast local-file presence check used by legacy-folder migration logic.
	private static bool HasLocalTransitAnnouncementFiles(string folderName)
	{
		if (string.IsNullOrWhiteSpace(folderName))
		{
			return false;
		}

		// Probe without creating directories so fallback checks do not leave empty folders behind.
		string directory = SirenPathUtils.GetCustomSirensDirectory(SettingsDirectory, folderName, ensureExists: false);
		if (!Directory.Exists(directory))
		{
			return false;
		}

		List<string> keys = SirenPathUtils.EnumerateCustomSirenKeys(SettingsDirectory, folderName);
		return keys.Count > 0;
	}
}
