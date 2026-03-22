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

// Supported transit service buckets for line overrides and slot mapping.
internal enum TransitAnnouncementServiceType
{
	Train = 0,
	Bus = 1,
	Metro = 2,
	Tram = 3
}

// Result type for loading one transit announcement clip.
internal enum TransitAnnouncementLoadStatus
{
	Success,
	NotConfigured,
	Pending,
	Failure
}

// One playback segment in the announcement sequence.
internal readonly struct TransitAnnouncementPlaybackSegment
{
	internal TransitAnnouncementPlaybackSegment(AudioClip clip, SirenSfxProfile profile)
	{
		Clip = clip;
		Profile = (profile ?? SirenSfxProfile.CreateFallback()).ClampCopy();
	}

	internal AudioClip Clip { get; }

	internal SirenSfxProfile Profile { get; }
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
	private static readonly string[] s_TransitAnnouncementLeadTargetKeys =
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

	// Canonical target keys for optional trailing clips in the sequence.
	private static readonly string[] s_TransitAnnouncementTailTargetKeys =
	{
		"train.arrival.tail",
		"train.departure.tail",
		"bus.arrival.tail",
		"bus.departure.tail",
		"metro.arrival.tail",
		"metro.departure.tail",
		"tram.arrival.tail",
		"tram.departure.tail"
	};

	private static readonly string[] s_TransitAnnouncementSelectionTargetKeys =
		s_TransitAnnouncementLeadTargetKeys.Concat(s_TransitAnnouncementTailTargetKeys).ToArray();

	private static readonly string[] s_TransitAnnouncementServiceVoiceKeys =
	{
		"train",
		"bus",
		"metro",
		"tram"
	};

	private static int s_TransitAnnouncementDropdownCacheVersion = -1;

	private static DropdownItem<string>[] s_TransitAnnouncementDropdownWithDefault = Array.Empty<DropdownItem<string>>();

	private static int s_TransitAnnouncementLineServiceDropdownCacheVersion = -1;

	private static DropdownItem<string>[] s_TransitAnnouncementLineServiceDropdown = Array.Empty<DropdownItem<string>>();

	private static int s_TransitAnnouncementLineDropdownCacheVersion = -1;

	private static DropdownItem<string>[] s_TransitAnnouncementLineDropdown = Array.Empty<DropdownItem<string>>();

	private static TransitAnnouncementServiceType s_TransitAnnouncementLineEditorService = TransitAnnouncementServiceType.Train;

	private static readonly HashSet<string> s_ObservedTransitLinesThisSession = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	// Sync transit-announcement custom files to profile keys and refresh scan metadata.
	internal static bool SyncCustomTransitAnnouncementCatalog(bool saveIfChanged, bool forceStatusRefresh = false)
	{
		TransitAnnouncementConfig.Normalize(TransitAnnouncementCustomFolderName);
		bool adoptedLegacyFolder = TryAdoptLegacyTransitAnnouncementFolder();
		bool slotTargetChanged = NormalizeTransitAnnouncementTargets();
		bool speechSettingsChanged = NormalizeTransitAnnouncementSpeechSettings();
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
		bool speechSettingsChangedAfterSync = NormalizeTransitAnnouncementSpeechSettings();
		if (catalogChanged ||
			implicitModuleProfilesChanged ||
			adoptedLegacyFolder ||
			slotTargetChanged ||
			slotTargetChangedAfterSync ||
			speechSettingsChanged ||
			speechSettingsChangedAfterSync ||
			scanMetadataChanged ||
			moduleCatalogChanged)
		{
			if (saveIfChanged &&
				(catalogChanged ||
					implicitModuleProfilesChanged ||
					adoptedLegacyFolder ||
					slotTargetChanged ||
					slotTargetChangedAfterSync ||
					speechSettingsChanged ||
					speechSettingsChangedAfterSync ||
					scanMetadataChanged))
			{
				SaveConfig();
			}

			if (catalogChanged ||
				implicitModuleProfilesChanged ||
				adoptedLegacyFolder ||
				slotTargetChanged ||
				slotTargetChangedAfterSync ||
				speechSettingsChanged ||
				speechSettingsChangedAfterSync)
			{
				ConfigVersion++;
			}

			NotifyOptionsCatalogChanged();
		}

		return catalogChanged ||
			implicitModuleProfilesChanged ||
			adoptedLegacyFolder ||
			slotTargetChanged ||
			slotTargetChangedAfterSync ||
			speechSettingsChanged ||
			speechSettingsChangedAfterSync;
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

	// Clear per-session observed-line memory when loading a new city/session.
	internal static void ResetTransitLineObservationSession()
	{
		s_ObservedTransitLinesThisSession.Clear();
	}

	// Remove discovered lines that are no longer observed in-session and have no overrides.
	internal static void PruneStaleTransitAnnouncementLinesFromOptions()
	{
		HashSet<string> protectedLines = GetTransitLineKeysReferencedByOverrides();
		List<string> existingKnownLines = TransitAnnouncementConfig.TransitAnnouncementKnownLines ?? new List<string>();
		List<string> keptLines = new List<string>(existingKnownLines.Count);
		for (int i = 0; i < existingKnownLines.Count; i++)
		{
			string normalized = NormalizeTransitLineIdentity(existingKnownLines[i]);
			if (string.IsNullOrWhiteSpace(normalized))
			{
				continue;
			}

			if (protectedLines.Contains(normalized) || s_ObservedTransitLinesThisSession.Contains(normalized))
			{
				keptLines.Add(normalized);
			}
		}

		foreach (string protectedLine in protectedLines)
		{
			if (keptLines.Any(line => string.Equals(line, protectedLine, StringComparison.OrdinalIgnoreCase)))
			{
				continue;
			}

			keptLines.Add(protectedLine);
		}

		keptLines.Sort(StringComparer.OrdinalIgnoreCase);
		bool changed = !ListEqualsIgnoreCaseKeys(existingKnownLines, keptLines);
		if (changed)
		{
			TransitAnnouncementConfig.TransitAnnouncementKnownLines = keptLines;
		}

		Dictionary<string, string> displayMap = TransitAnnouncementConfig.TransitAnnouncementLineDisplayByKey ??
			new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		List<string> displayKeys = displayMap.Keys.ToList();
		for (int i = 0; i < displayKeys.Count; i++)
		{
			string normalized = NormalizeTransitLineIdentity(displayKeys[i]);
			if (!keptLines.Any(line => string.Equals(line, normalized, StringComparison.OrdinalIgnoreCase)))
			{
				displayMap.Remove(displayKeys[i]);
				changed = true;
			}
		}

		TransitAnnouncementConfig.TransitAnnouncementLineDisplayByKey = displayMap;
		string selectedLine = NormalizeTransitLineIdentity(TransitAnnouncementConfig.TransitAnnouncementSelectedLine);
		if (!string.IsNullOrWhiteSpace(selectedLine) &&
			!keptLines.Any(line => string.Equals(line, selectedLine, StringComparison.OrdinalIgnoreCase)))
		{
			TransitAnnouncementConfig.TransitAnnouncementSelectedLine = GetFirstTransitLineForService(s_TransitAnnouncementLineEditorService);
			changed = true;
		}

		if (!changed)
		{
			return;
		}

		changed |= NormalizeTransitAnnouncementSpeechSettings();
		if (changed)
		{
			SaveConfig();
			ConfigVersion++;
		}

		NotifyOptionsCatalogChanged();
	}

	// Build dropdown items for transit-announcement slot selectors.
	internal static DropdownItem<string>[] BuildTransitAnnouncementDropdownItems()
	{
		EnsureTransitAnnouncementDropdownCacheCurrent();
		return s_TransitAnnouncementDropdownWithDefault;
	}

	// Build dropdown items for selecting which transit service's lines are being edited.
	internal static DropdownItem<string>[] BuildTransitAnnouncementLineServiceDropdownItems()
	{
		EnsureTransitAnnouncementLineServiceDropdownCacheCurrent();
		return s_TransitAnnouncementLineServiceDropdown;
	}

	// Build dropdown items for selecting a discovered line within the selected service.
	internal static DropdownItem<string>[] BuildTransitAnnouncementLineDropdownItems()
	{
		EnsureTransitAnnouncementLineDropdownCacheCurrent();
		return s_TransitAnnouncementLineDropdown;
	}

	internal static string GetTransitAnnouncementLineEditorService()
	{
		return GetTransitAnnouncementServiceVoiceKey(s_TransitAnnouncementLineEditorService);
	}

	internal static void SetTransitAnnouncementLineEditorService(string serviceKey)
	{
		if (!TryParseTransitAnnouncementServiceKey(serviceKey, out TransitAnnouncementServiceType serviceType))
		{
			return;
		}

		bool changed = false;
		if (s_TransitAnnouncementLineEditorService != serviceType)
		{
			s_TransitAnnouncementLineEditorService = serviceType;
			s_TransitAnnouncementLineDropdownCacheVersion = -1;
			s_TransitAnnouncementLineDropdown = Array.Empty<DropdownItem<string>>();
			changed = true;
		}

		string selectedLine = GetTransitAnnouncementSelectedLineForOptions();
		if (!IsLineForService(selectedLine, serviceType))
		{
			string firstLineForService = GetFirstTransitLineForService(serviceType);
			SetTransitAnnouncementSelectedLineForOptions(firstLineForService);
			changed = true;
		}

		if (changed)
		{
			OptionsVersion++;
		}
	}

	internal static string GetTransitAnnouncementSelectedLineForOptions()
	{
		string selected = NormalizeTransitLineIdentity(TransitAnnouncementConfig.TransitAnnouncementSelectedLine);
		if (!string.IsNullOrWhiteSpace(selected))
		{
			return selected;
		}

		string firstForService = GetFirstTransitLineForService(s_TransitAnnouncementLineEditorService);
		if (!string.IsNullOrWhiteSpace(firstForService))
		{
			TransitAnnouncementConfig.TransitAnnouncementSelectedLine = firstForService;
			return firstForService;
		}

		return string.Empty;
	}

	internal static void SetTransitAnnouncementSelectedLineForOptions(string lineKey)
	{
		string normalized = NormalizeTransitLineIdentity(lineKey);
		if (string.Equals(TransitAnnouncementConfig.TransitAnnouncementSelectedLine, normalized, StringComparison.Ordinal))
		{
			return;
		}

		TransitAnnouncementConfig.TransitAnnouncementSelectedLine = normalized;
		if (string.IsNullOrWhiteSpace(normalized))
		{
			OptionsVersion++;
			return;
		}

		TrackTransitLineForOptions(normalized);
		OptionsVersion++;
	}

	internal static string GetSelectedTransitAnnouncementLineStatusText()
	{
		string selectedLine = GetTransitAnnouncementSelectedLineForOptions();
		if (string.IsNullOrWhiteSpace(selectedLine))
		{
			return $"No {GetTransitAnnouncementServiceLabel(s_TransitAnnouncementLineEditorService).ToLowerInvariant()} lines detected yet. Drive a line in this city to populate overrides.";
		}

		if (!TryParseTransitLineIdentity(selectedLine, out TransitAnnouncementServiceType serviceType, out string lineStableId))
		{
			return "Selected line key is invalid.";
		}

		string lineDisplayName = GetTransitLineDisplayName(selectedLine);
		if (string.IsNullOrWhiteSpace(lineDisplayName))
		{
			lineDisplayName = lineStableId;
		}
		string arrivalSelection = GetTransitAnnouncementLineSelection(ResolveServiceSlot(serviceType, isArrival: true), selectedLine);
		string departureSelection = GetTransitAnnouncementLineSelection(ResolveServiceSlot(serviceType, isArrival: false), selectedLine);
		string arrivalText = AudioReplacementDomainConfig.IsDefaultSelection(arrivalSelection)
			? "Default"
			: FormatSirenDisplayName(arrivalSelection);
		string departureText = AudioReplacementDomainConfig.IsDefaultSelection(departureSelection)
			? "Default"
			: FormatSirenDisplayName(departureSelection);
		return $"Line: {lineDisplayName}\nLine ID: {lineStableId}\nService: {GetTransitAnnouncementServiceLabel(serviceType)}\nArrival override: {arrivalText}\nDeparture override: {departureText}";
	}

	internal static string GetTransitAnnouncementLineArrivalSelectionForOptions()
	{
		string selectedLine = GetTransitAnnouncementSelectedLineForOptions();
		if (string.IsNullOrWhiteSpace(selectedLine) ||
			!TryParseTransitLineIdentity(selectedLine, out TransitAnnouncementServiceType serviceType, out _))
		{
			return SirenReplacementConfig.DefaultSelectionToken;
		}

		return GetTransitAnnouncementLineSelection(ResolveServiceSlot(serviceType, isArrival: true), selectedLine);
	}

	internal static void SetTransitAnnouncementLineArrivalSelectionForOptions(string selection)
	{
		string selectedLine = GetTransitAnnouncementSelectedLineForOptions();
		if (string.IsNullOrWhiteSpace(selectedLine) ||
			!TryParseTransitLineIdentity(selectedLine, out TransitAnnouncementServiceType serviceType, out _))
		{
			return;
		}

		SetTransitAnnouncementLineSelection(ResolveServiceSlot(serviceType, isArrival: true), selectedLine, selection);
	}

	internal static string GetTransitAnnouncementLineDepartureSelectionForOptions()
	{
		string selectedLine = GetTransitAnnouncementSelectedLineForOptions();
		if (string.IsNullOrWhiteSpace(selectedLine) ||
			!TryParseTransitLineIdentity(selectedLine, out TransitAnnouncementServiceType serviceType, out _))
		{
			return SirenReplacementConfig.DefaultSelectionToken;
		}

		return GetTransitAnnouncementLineSelection(ResolveServiceSlot(serviceType, isArrival: false), selectedLine);
	}

	internal static void SetTransitAnnouncementLineDepartureSelectionForOptions(string selection)
	{
		string selectedLine = GetTransitAnnouncementSelectedLineForOptions();
		if (string.IsNullOrWhiteSpace(selectedLine) ||
			!TryParseTransitLineIdentity(selectedLine, out TransitAnnouncementServiceType serviceType, out _))
		{
			return;
		}

		SetTransitAnnouncementLineSelection(ResolveServiceSlot(serviceType, isArrival: false), selectedLine, selection);
	}

	internal static bool IsTransitAnnouncementLineEditorDisabled()
	{
		return string.IsNullOrWhiteSpace(GetTransitAnnouncementSelectedLineForOptions());
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

	// Read one slot lead-in selection value.
	internal static string GetTransitAnnouncementSelection(TransitAnnouncementSlot slot)
	{
		return GetTransitAnnouncementLeadSelection(slot);
	}

	// Update one slot lead-in selection value.
	internal static void SetTransitAnnouncementSelection(TransitAnnouncementSlot slot, string selection)
	{
		SetTransitAnnouncementLeadSelection(slot, selection);
	}

	internal static string GetTransitAnnouncementLeadSelection(TransitAnnouncementSlot slot)
	{
		return TransitAnnouncementConfig.GetTargetSelection(GetTransitAnnouncementLeadTargetKey(slot));
	}

	internal static void SetTransitAnnouncementLeadSelection(TransitAnnouncementSlot slot, string selection)
	{
		TransitAnnouncementConfig.SetTargetSelection(GetTransitAnnouncementLeadTargetKey(slot), selection);
	}

	internal static string GetTransitAnnouncementTailSelection(TransitAnnouncementSlot slot)
	{
		return TransitAnnouncementConfig.GetTargetSelection(GetTransitAnnouncementTailTargetKey(slot));
	}

	internal static void SetTransitAnnouncementTailSelection(TransitAnnouncementSlot slot, string selection)
	{
		TransitAnnouncementConfig.SetTargetSelection(GetTransitAnnouncementTailTargetKey(slot), selection);
	}

	// Build the full step sequence for one transit event without TTS dependencies.
	internal static TransitAnnouncementLoadStatus TryBuildTransitAnnouncementSequence(
		TransitAnnouncementSlot slot,
		string lineKey,
		out List<TransitAnnouncementPlaybackSegment> segments,
		out string message)
	{
		segments = new List<TransitAnnouncementPlaybackSegment>(2);
		message = string.Empty;
		if (!TransitAnnouncementConfig.Enabled)
		{
			message = "Transit announcements are disabled.";
			return TransitAnnouncementLoadStatus.NotConfigured;
		}

		bool hasPendingStep = false;
		string pendingMessage = string.Empty;

		TransitAnnouncementLoadStatus primaryStatus = TryAppendPrimaryTransitAnnouncementStep(
			slot,
			lineKey,
			segments,
			out string primaryMessage);
		if (primaryStatus == TransitAnnouncementLoadStatus.Failure)
		{
			message = primaryMessage;
			return TransitAnnouncementLoadStatus.Failure;
		}

		if (primaryStatus == TransitAnnouncementLoadStatus.Pending)
		{
			hasPendingStep = true;
			pendingMessage = primaryMessage;
		}

		TransitAnnouncementLoadStatus tailStatus = TryAppendConfiguredAudioStep(
			GetTransitAnnouncementTailSelection(slot),
			"Tail audio",
			segments,
			out string tailMessage);
		if (tailStatus == TransitAnnouncementLoadStatus.Failure)
		{
			message = tailMessage;
			return TransitAnnouncementLoadStatus.Failure;
		}

		if (tailStatus == TransitAnnouncementLoadStatus.Pending)
		{
			hasPendingStep = true;
			pendingMessage = string.IsNullOrWhiteSpace(pendingMessage)
				? tailMessage
				: pendingMessage;
		}

		if (hasPendingStep)
		{
			message = string.IsNullOrWhiteSpace(pendingMessage)
				? "Announcement sequence is pending because audio is still loading."
				: pendingMessage;
			return TransitAnnouncementLoadStatus.Pending;
		}

		if (segments.Count == 0)
		{
			message = "No announcement steps are configured.";
			return TransitAnnouncementLoadStatus.NotConfigured;
		}

		message = $"Prepared {segments.Count} announcement step(s).";
		return TransitAnnouncementLoadStatus.Success;
	}

	// Resolve and load one slot lead-in clip using current config selections.
	internal static TransitAnnouncementLoadStatus TryLoadTransitAnnouncementClip(
		TransitAnnouncementSlot slot,
		out AudioClip clip,
		out SirenSfxProfile profile,
		out string message)
	{
		return TryLoadTransitAnnouncementSelectionWithFallback(
			GetTransitAnnouncementLeadSelection(slot),
			out clip,
			out profile,
			out message);
	}

	private static TransitAnnouncementLoadStatus TryAppendPrimaryTransitAnnouncementStep(
		TransitAnnouncementSlot slot,
		string lineKey,
		ICollection<TransitAnnouncementPlaybackSegment> segments,
		out string message)
	{
		string defaultLeadSelection = GetTransitAnnouncementLeadSelection(slot);
		string normalizedLineKey = NormalizeTransitLineIdentity(lineKey);
		if (string.IsNullOrWhiteSpace(normalizedLineKey))
		{
			return TryAppendConfiguredAudioStep(
				defaultLeadSelection,
				"Primary announcement audio",
				segments,
				out message);
		}

		string lineOverrideSelection = GetTransitAnnouncementLineSelection(slot, normalizedLineKey);
		if (AudioReplacementDomainConfig.IsDefaultSelection(lineOverrideSelection))
		{
			return TryAppendConfiguredAudioStep(
				defaultLeadSelection,
				"Primary announcement audio",
				segments,
				out message);
		}

		TransitAnnouncementLoadStatus lineOverrideStatus = TryLoadTransitAnnouncementSelection(
			AudioReplacementDomainConfig.NormalizeProfileKey(lineOverrideSelection),
			out AudioClip lineClip,
			out SirenSfxProfile lineProfile,
			out string lineOverrideMessage);
		if (lineOverrideStatus == TransitAnnouncementLoadStatus.Success)
		{
			segments.Add(new TransitAnnouncementPlaybackSegment(lineClip, lineProfile));
			message = "Primary announcement loaded from line override.";
			return TransitAnnouncementLoadStatus.Success;
		}

		if (lineOverrideStatus == TransitAnnouncementLoadStatus.Pending)
		{
			message = string.IsNullOrWhiteSpace(lineOverrideMessage)
				? "Line override audio is still loading."
				: lineOverrideMessage;
			return TransitAnnouncementLoadStatus.Pending;
		}

		TransitAnnouncementLoadStatus fallbackStatus = TryAppendConfiguredAudioStep(
			defaultLeadSelection,
			"Primary announcement audio fallback",
			segments,
			out string fallbackMessage);
		switch (fallbackStatus)
		{
			case TransitAnnouncementLoadStatus.Success:
				message =
					$"Line override failed ({lineOverrideMessage}). Falling back to slot default lead clip.";
				return TransitAnnouncementLoadStatus.Success;
			case TransitAnnouncementLoadStatus.Pending:
				message = string.IsNullOrWhiteSpace(fallbackMessage)
					? "Slot default lead clip is still loading after line override failed."
					: fallbackMessage;
				return TransitAnnouncementLoadStatus.Pending;
			case TransitAnnouncementLoadStatus.NotConfigured:
				message = string.IsNullOrWhiteSpace(fallbackMessage)
					? $"Line override failed ({lineOverrideMessage}) and no slot default lead clip is configured."
					: fallbackMessage;
				return TransitAnnouncementLoadStatus.NotConfigured;
			default:
				message =
					$"Line override failed ({lineOverrideMessage}) and slot default lead clip failed ({fallbackMessage}).";
				return TransitAnnouncementLoadStatus.Failure;
		}
	}

	private static TransitAnnouncementLoadStatus TryAppendConfiguredAudioStep(
		string selection,
		string stepLabel,
		ICollection<TransitAnnouncementPlaybackSegment> segments,
		out string message)
	{
		TransitAnnouncementLoadStatus status = TryLoadTransitAnnouncementSelectionWithFallback(
			selection,
			out AudioClip clip,
			out SirenSfxProfile profile,
			out string loadMessage);
		switch (status)
		{
			case TransitAnnouncementLoadStatus.Success:
				segments.Add(new TransitAnnouncementPlaybackSegment(clip, profile));
				message = $"{stepLabel} loaded.";
				return TransitAnnouncementLoadStatus.Success;
			case TransitAnnouncementLoadStatus.NotConfigured:
				message = string.IsNullOrWhiteSpace(loadMessage)
					? $"{stepLabel} not configured."
					: loadMessage;
				return TransitAnnouncementLoadStatus.NotConfigured;
			case TransitAnnouncementLoadStatus.Pending:
				message = string.IsNullOrWhiteSpace(loadMessage)
					? $"{stepLabel} is still loading."
					: loadMessage;
				return TransitAnnouncementLoadStatus.Pending;
			default:
				message = string.IsNullOrWhiteSpace(loadMessage)
					? $"{stepLabel} failed."
					: $"{stepLabel} failed: {loadMessage}";
				return TransitAnnouncementLoadStatus.Failure;
		}
	}

	private static TransitAnnouncementLoadStatus TryLoadTransitAnnouncementSelectionWithFallback(
		string selection,
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

		string primarySelection = AudioReplacementDomainConfig.NormalizeProfileKey(selection);
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
		return TransitAnnouncementConfig.SynchronizeTargets(s_TransitAnnouncementSelectionTargetKeys);
	}

	// Keep transit line selections and known-line metadata aligned and normalized.
	internal static bool NormalizeTransitAnnouncementSpeechSettings()
	{
		bool changed = false;
		TransitAnnouncementConfig.TransitAnnouncementLineDisplayByKey ??=
			new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		HashSet<string> validLineSlotKeys = new HashSet<string>(s_TransitAnnouncementLeadTargetKeys, StringComparer.OrdinalIgnoreCase);
		Dictionary<string, string> normalizedLineSelections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		HashSet<string> knownLines = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (KeyValuePair<string, string> pair in TransitAnnouncementConfig.TransitAnnouncementLineSelections)
		{
			if (!TryParseTransitLineOverrideKey(pair.Key, out string slotKey, out string lineKey))
			{
				changed = true;
				continue;
			}

			slotKey = AudioReplacementDomainConfig.NormalizeTargetKey(slotKey);
			lineKey = NormalizeTransitLineIdentity(lineKey);
			if (string.IsNullOrWhiteSpace(slotKey) ||
				string.IsNullOrWhiteSpace(lineKey) ||
				!validLineSlotKeys.Contains(slotKey))
			{
				changed = true;
				continue;
			}

			string selection = AudioReplacementDomainConfig.NormalizeProfileKey(pair.Value);
			if (AudioReplacementDomainConfig.IsDefaultSelection(selection))
			{
				changed = true;
				continue;
			}

			string normalizedOverrideKey = BuildTransitLineOverrideKey(slotKey, lineKey);
			normalizedLineSelections[normalizedOverrideKey] = selection;
			knownLines.Add(lineKey);
		}

		if (!DictionaryEqualsIgnoreCaseKeysOrdinalValues(
			TransitAnnouncementConfig.TransitAnnouncementLineSelections,
			normalizedLineSelections))
		{
			TransitAnnouncementConfig.TransitAnnouncementLineSelections = normalizedLineSelections;
			changed = true;
		}

		string selectedLine = NormalizeTransitLineIdentity(TransitAnnouncementConfig.TransitAnnouncementSelectedLine);
		if (!string.IsNullOrWhiteSpace(selectedLine))
		{
			knownLines.Add(selectedLine);
		}

		foreach (string rawLine in TransitAnnouncementConfig.TransitAnnouncementKnownLines)
		{
			string lineKey = NormalizeTransitLineIdentity(rawLine);
			if (string.IsNullOrWhiteSpace(lineKey))
			{
				changed = true;
				continue;
			}

			knownLines.Add(lineKey);
		}

		List<string> normalizedKnownLines = knownLines
			.OrderBy(static line => line, StringComparer.OrdinalIgnoreCase)
			.ToList();
		if (!ListEqualsIgnoreCaseKeys(TransitAnnouncementConfig.TransitAnnouncementKnownLines, normalizedKnownLines))
		{
			TransitAnnouncementConfig.TransitAnnouncementKnownLines = normalizedKnownLines;
			changed = true;
		}

		if (string.IsNullOrWhiteSpace(selectedLine))
		{
			selectedLine = normalizedKnownLines.Count > 0 ? normalizedKnownLines[0] : string.Empty;
		}
		else if (!knownLines.Contains(selectedLine))
		{
			knownLines.Add(selectedLine);
			normalizedKnownLines = knownLines
				.OrderBy(static line => line, StringComparer.OrdinalIgnoreCase)
				.ToList();
			TransitAnnouncementConfig.TransitAnnouncementKnownLines = normalizedKnownLines;
			changed = true;
		}

		if (!string.Equals(TransitAnnouncementConfig.TransitAnnouncementSelectedLine, selectedLine, StringComparison.Ordinal))
		{
			TransitAnnouncementConfig.TransitAnnouncementSelectedLine = selectedLine;
			changed = true;
		}

		Dictionary<string, string> normalizedDisplayMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (KeyValuePair<string, string> pair in TransitAnnouncementConfig.TransitAnnouncementLineDisplayByKey)
		{
			string lineKey = NormalizeTransitLineIdentity(pair.Key);
			if (string.IsNullOrWhiteSpace(lineKey) || !knownLines.Contains(lineKey))
			{
				changed = true;
				continue;
			}

			string displayName = AudioReplacementDomainConfig.NormalizeTransitDisplayText(pair.Value);
			if (string.IsNullOrWhiteSpace(displayName))
			{
				changed = true;
				continue;
			}

			normalizedDisplayMap[lineKey] = displayName;
		}

		if (!DictionaryEqualsIgnoreCaseKeysOrdinalValues(
			TransitAnnouncementConfig.TransitAnnouncementLineDisplayByKey,
			normalizedDisplayMap))
		{
			TransitAnnouncementConfig.TransitAnnouncementLineDisplayByKey = normalizedDisplayMap;
			changed = true;
		}

		return changed;
	}

	// Apply global Public Transport tab playback controls to one resolved announcement profile.
	internal static SirenSfxProfile BuildTransitAnnouncementPlaybackProfile(SirenSfxProfile profile)
	{
		SirenSfxProfile effective = (profile ?? SirenSfxProfile.CreateFallback()).ClampCopy();
		effective.Volume = Mathf.Clamp01(TransitAnnouncementConfig.GlobalAnnouncementVolume);
		effective.MinDistance = Mathf.Max(0f, TransitAnnouncementConfig.GlobalAnnouncementMinDistance);
		effective.MaxDistance = Mathf.Max(effective.MinDistance + 0.01f, TransitAnnouncementConfig.GlobalAnnouncementMaxDistance);
		return effective;
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

	internal static string GetTransitAnnouncementServiceLabel(TransitAnnouncementServiceType serviceType)
	{
		switch (serviceType)
		{
			case TransitAnnouncementServiceType.Train:
				return "Train";
			case TransitAnnouncementServiceType.Bus:
				return "Bus";
			case TransitAnnouncementServiceType.Metro:
				return "Metro";
			case TransitAnnouncementServiceType.Tram:
				return "Tram";
			default:
				return "Transit";
		}
	}

	internal static void RegisterTransitLineObservation(string lineKey, string displayName)
	{
		string normalizedLineKey = NormalizeTransitLineIdentity(lineKey);
		if (string.IsNullOrWhiteSpace(normalizedLineKey))
		{
			return;
		}

		s_ObservedTransitLinesThisSession.Add(normalizedLineKey);
		if (TrackTransitLineForOptions(normalizedLineKey, displayName))
		{
			NotifyOptionsCatalogChanged();
		}
	}

	internal static string BuildTransitLineIdentity(TransitAnnouncementServiceType serviceType, string lineStableId)
	{
		string normalizedStableId = NormalizeTransitLineStableId(lineStableId);
		string serviceKey = GetTransitAnnouncementServiceVoiceKey(serviceType);
		if (string.IsNullOrWhiteSpace(serviceKey) || string.IsNullOrWhiteSpace(normalizedStableId))
		{
			return string.Empty;
		}

		return $"{serviceKey}\n{normalizedStableId}";
	}

	internal static bool TryParseTransitLineIdentity(
		string lineKey,
		out TransitAnnouncementServiceType serviceType,
		out string lineStableId)
	{
		serviceType = default;
		lineStableId = string.Empty;
		string normalized = AudioReplacementDomainConfig.NormalizeTransitLineKey(lineKey);
		if (string.IsNullOrWhiteSpace(normalized))
		{
			return false;
		}

		int split = normalized.IndexOf('\n');
		if (split <= 0 || split >= normalized.Length - 1)
		{
			return false;
		}

		string serviceKey = normalized.Substring(0, split).Trim();
		string stableId = NormalizeTransitLineStableId(normalized.Substring(split + 1));
		if (!TryParseTransitAnnouncementServiceKey(serviceKey, out serviceType) ||
			string.IsNullOrWhiteSpace(stableId))
		{
			return false;
		}

		lineStableId = stableId;
		return true;
	}

	internal static string NormalizeTransitLineIdentity(string lineKey)
	{
		return TryParseTransitLineIdentity(lineKey, out TransitAnnouncementServiceType serviceType, out string lineStableId)
			? BuildTransitLineIdentity(serviceType, lineStableId)
			: string.Empty;
	}

	internal static string GetTransitLineDisplayName(string lineKey)
	{
		string normalizedLineKey = NormalizeTransitLineIdentity(lineKey);
		if (string.IsNullOrWhiteSpace(normalizedLineKey))
		{
			return string.Empty;
		}

		Dictionary<string, string> displayMap = TransitAnnouncementConfig.TransitAnnouncementLineDisplayByKey ??
			new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		if (displayMap.TryGetValue(normalizedLineKey, out string displayName) &&
			!string.IsNullOrWhiteSpace(displayName))
		{
			return displayName;
		}

		if (!TryParseTransitLineIdentity(normalizedLineKey, out _, out string stableId))
		{
			return string.Empty;
		}

		return FormatTransitLineStableIdForDisplay(stableId);
	}

	internal static string GetTransitAnnouncementLineSelection(TransitAnnouncementSlot slot, string lineKey)
	{
		string normalizedLineKey = NormalizeTransitLineIdentity(lineKey);
		string slotKey = GetTransitAnnouncementLeadTargetKey(slot);
		if (string.IsNullOrWhiteSpace(normalizedLineKey) || string.IsNullOrWhiteSpace(slotKey))
		{
			return SirenReplacementConfig.DefaultSelectionToken;
		}

		string overrideKey = BuildTransitLineOverrideKey(slotKey, normalizedLineKey);
		if (!TryGetTransitAnnouncementSelectionByOverrideKey(overrideKey, out string selection))
		{
			// Compatibility fallback for overrides created before stable route IDs were introduced.
			if (TryParseTransitLineIdentity(normalizedLineKey, out TransitAnnouncementServiceType serviceType, out _) &&
				TryGetTransitAnnouncementSelectionByOverrideKey(
					BuildTransitLineOverrideKey(slotKey, BuildLabelTransitLineIdentity(serviceType, GetTransitLineDisplayName(normalizedLineKey))),
					out selection))
			{
				// Matched a legacy display-name-based line key.
			}
			else
			{
				return SirenReplacementConfig.DefaultSelectionToken;
			}
		}

		string normalizedSelection = AudioReplacementDomainConfig.NormalizeProfileKey(selection);
		return AudioReplacementDomainConfig.IsDefaultSelection(normalizedSelection)
			? SirenReplacementConfig.DefaultSelectionToken
			: normalizedSelection;
	}

	private static bool TryGetTransitAnnouncementSelectionByOverrideKey(string overrideKey, out string selection)
	{
		selection = string.Empty;
		return !string.IsNullOrWhiteSpace(overrideKey) &&
			TransitAnnouncementConfig.TransitAnnouncementLineSelections.TryGetValue(overrideKey, out selection);
	}

	internal static void SetTransitAnnouncementLineSelection(
		TransitAnnouncementSlot slot,
		string lineKey,
		string selection)
	{
		string normalizedLineKey = NormalizeTransitLineIdentity(lineKey);
		string slotKey = GetTransitAnnouncementLeadTargetKey(slot);
		if (string.IsNullOrWhiteSpace(normalizedLineKey) || string.IsNullOrWhiteSpace(slotKey))
		{
			return;
		}

		string overrideKey = BuildTransitLineOverrideKey(slotKey, normalizedLineKey);
		if (string.IsNullOrWhiteSpace(overrideKey))
		{
			return;
		}

		string normalizedSelection = AudioReplacementDomainConfig.NormalizeProfileKey(selection);
		if (AudioReplacementDomainConfig.IsDefaultSelection(normalizedSelection) || string.IsNullOrWhiteSpace(normalizedSelection))
		{
			if (TransitAnnouncementConfig.TransitAnnouncementLineSelections.Remove(overrideKey))
			{
				OptionsVersion++;
			}
			return;
		}

		if (TransitAnnouncementConfig.TransitAnnouncementLineSelections.TryGetValue(overrideKey, out string existing) &&
			string.Equals(existing, normalizedSelection, StringComparison.Ordinal))
		{
			return;
		}

		TransitAnnouncementConfig.TransitAnnouncementLineSelections[overrideKey] = normalizedSelection;
		TrackTransitLineForOptions(normalizedLineKey);
		OptionsVersion++;
	}

	private static bool TrackTransitLineForOptions(string lineKey, string displayName = "")
	{
		string normalizedLineKey = NormalizeTransitLineIdentity(lineKey);
		if (string.IsNullOrWhiteSpace(normalizedLineKey))
		{
			return false;
		}

		bool changed = false;
		List<string> knownLines = TransitAnnouncementConfig.TransitAnnouncementKnownLines ?? new List<string>();
		if (!knownLines.Any(existing => string.Equals(existing, normalizedLineKey, StringComparison.OrdinalIgnoreCase)))
		{
			knownLines.Add(normalizedLineKey);
			knownLines.Sort(StringComparer.OrdinalIgnoreCase);
			TransitAnnouncementConfig.TransitAnnouncementKnownLines = knownLines;
			changed = true;
		}

		if (string.IsNullOrWhiteSpace(TransitAnnouncementConfig.TransitAnnouncementSelectedLine))
		{
			TransitAnnouncementConfig.TransitAnnouncementSelectedLine = normalizedLineKey;
			changed = true;
		}

		string normalizedDisplayName = AudioReplacementDomainConfig.NormalizeTransitDisplayText(displayName);
		if (!string.IsNullOrWhiteSpace(normalizedDisplayName))
		{
			Dictionary<string, string> displayMap = TransitAnnouncementConfig.TransitAnnouncementLineDisplayByKey ??
				new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			if (!displayMap.TryGetValue(normalizedLineKey, out string existingDisplay) ||
				!string.Equals(existingDisplay, normalizedDisplayName, StringComparison.Ordinal))
			{
				displayMap[normalizedLineKey] = normalizedDisplayName;
				TransitAnnouncementConfig.TransitAnnouncementLineDisplayByKey = displayMap;
				changed = true;
			}
		}

		if (TryParseTransitLineIdentity(normalizedLineKey, out TransitAnnouncementServiceType serviceType, out _) &&
			serviceType == s_TransitAnnouncementLineEditorService)
		{
			s_TransitAnnouncementLineDropdownCacheVersion = -1;
			s_TransitAnnouncementLineDropdown = Array.Empty<DropdownItem<string>>();
		}

		return changed;
	}

	private static HashSet<string> GetTransitLineKeysReferencedByOverrides()
	{
		HashSet<string> lineKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (string overrideKey in TransitAnnouncementConfig.TransitAnnouncementLineSelections.Keys)
		{
			if (TryParseTransitLineOverrideKey(overrideKey, out _, out string lineKey))
			{
				lineKeys.Add(lineKey);
			}
		}

		return lineKeys;
	}

	private static bool IsLineForService(string lineKey, TransitAnnouncementServiceType serviceType)
	{
		return TryParseTransitLineIdentity(lineKey, out TransitAnnouncementServiceType parsedService, out _) &&
			parsedService == serviceType;
	}

	private static string GetFirstTransitLineForService(TransitAnnouncementServiceType serviceType)
	{
		List<string> lines = TransitAnnouncementConfig.TransitAnnouncementKnownLines ?? new List<string>();
		for (int i = 0; i < lines.Count; i++)
		{
			if (IsLineForService(lines[i], serviceType))
			{
				return lines[i];
			}
		}

		return string.Empty;
	}

	private static string BuildLabelTransitLineIdentity(TransitAnnouncementServiceType serviceType, string displayName)
	{
		string normalizedDisplayName = AudioReplacementDomainConfig.NormalizeTransitDisplayText(displayName);
		return string.IsNullOrWhiteSpace(normalizedDisplayName)
			? string.Empty
			: BuildTransitLineIdentity(serviceType, $"label:{normalizedDisplayName}");
	}

	private static string NormalizeTransitLineStableId(string lineStableId)
	{
		string normalized = AudioReplacementDomainConfig.NormalizeTransitLineKey(lineStableId);
		if (string.IsNullOrWhiteSpace(normalized))
		{
			return string.Empty;
		}

		if (normalized.StartsWith("number:", StringComparison.OrdinalIgnoreCase))
		{
			string numberText = normalized.Substring("number:".Length).Trim();
			return int.TryParse(numberText, out int number) && number > 0
				? $"number:{number}"
				: string.Empty;
		}

		if (normalized.StartsWith("entity:", StringComparison.OrdinalIgnoreCase))
		{
			string raw = normalized.Substring("entity:".Length);
			string[] parts = raw.Split(':');
			if (parts.Length != 2 ||
				!int.TryParse(parts[0], out int entityIndex) ||
				!int.TryParse(parts[1], out int entityVersion) ||
				entityIndex < 0 ||
				entityVersion < 0)
			{
				return string.Empty;
			}

			return $"entity:{entityIndex}:{entityVersion}";
		}

		string labelText = normalized.StartsWith("label:", StringComparison.OrdinalIgnoreCase)
			? normalized.Substring("label:".Length)
			: normalized;
		string normalizedLabel = AudioReplacementDomainConfig.NormalizeTransitDisplayText(labelText);
		return string.IsNullOrWhiteSpace(normalizedLabel)
			? string.Empty
			: $"label:{normalizedLabel}";
	}

	private static string FormatTransitLineStableIdForDisplay(string stableId)
	{
		string normalizedStableId = NormalizeTransitLineStableId(stableId);
		if (string.IsNullOrWhiteSpace(normalizedStableId))
		{
			return string.Empty;
		}

		if (normalizedStableId.StartsWith("number:", StringComparison.OrdinalIgnoreCase))
		{
			string numberText = normalizedStableId.Substring("number:".Length);
			return string.IsNullOrWhiteSpace(numberText)
				? "Line"
				: $"Line {numberText}";
		}

		if (normalizedStableId.StartsWith("label:", StringComparison.OrdinalIgnoreCase))
		{
			return normalizedStableId.Substring("label:".Length);
		}

		if (normalizedStableId.StartsWith("entity:", StringComparison.OrdinalIgnoreCase))
		{
			string[] parts = normalizedStableId.Substring("entity:".Length).Split(':');
			return parts.Length > 0
				? $"Route {parts[0]}"
				: "Route";
		}

		return normalizedStableId;
	}

	private static TransitAnnouncementSlot ResolveServiceSlot(TransitAnnouncementServiceType serviceType, bool isArrival)
	{
		switch (serviceType)
		{
			case TransitAnnouncementServiceType.Train:
				return isArrival ? TransitAnnouncementSlot.TrainArrival : TransitAnnouncementSlot.TrainDeparture;
			case TransitAnnouncementServiceType.Bus:
				return isArrival ? TransitAnnouncementSlot.BusArrival : TransitAnnouncementSlot.BusDeparture;
			case TransitAnnouncementServiceType.Metro:
				return isArrival ? TransitAnnouncementSlot.MetroArrival : TransitAnnouncementSlot.MetroDeparture;
			case TransitAnnouncementServiceType.Tram:
				return isArrival ? TransitAnnouncementSlot.TramArrival : TransitAnnouncementSlot.TramDeparture;
			default:
				return TransitAnnouncementSlot.TrainArrival;
		}
	}

	private static bool TryParseTransitAnnouncementServiceKey(string serviceKey, out TransitAnnouncementServiceType serviceType)
	{
		serviceType = default;
		string normalized = AudioReplacementDomainConfig.NormalizeTargetKey(serviceKey);
		if (string.IsNullOrWhiteSpace(normalized))
		{
			return false;
		}

		if (string.Equals(normalized, s_TransitAnnouncementServiceVoiceKeys[0], StringComparison.OrdinalIgnoreCase))
		{
			serviceType = TransitAnnouncementServiceType.Train;
			return true;
		}

		if (string.Equals(normalized, s_TransitAnnouncementServiceVoiceKeys[1], StringComparison.OrdinalIgnoreCase))
		{
			serviceType = TransitAnnouncementServiceType.Bus;
			return true;
		}

		if (string.Equals(normalized, s_TransitAnnouncementServiceVoiceKeys[2], StringComparison.OrdinalIgnoreCase))
		{
			serviceType = TransitAnnouncementServiceType.Metro;
			return true;
		}

		if (string.Equals(normalized, s_TransitAnnouncementServiceVoiceKeys[3], StringComparison.OrdinalIgnoreCase))
		{
			serviceType = TransitAnnouncementServiceType.Tram;
			return true;
		}

		return false;
	}

	private static string BuildTransitLineOverrideKey(string slotKey, string lineKey)
	{
		string normalizedSlotKey = AudioReplacementDomainConfig.NormalizeTargetKey(slotKey);
		string normalizedLineKey = NormalizeTransitLineIdentity(lineKey);
		if (string.IsNullOrWhiteSpace(normalizedSlotKey) || string.IsNullOrWhiteSpace(normalizedLineKey))
		{
			return string.Empty;
		}

		return $"{normalizedSlotKey}\n{normalizedLineKey}";
	}

	internal static bool TryParseTransitLineOverrideKey(string overrideKey, out string slotKey, out string lineKey)
	{
		slotKey = string.Empty;
		lineKey = string.Empty;
		string normalized = AudioReplacementDomainConfig.NormalizeTransitLineKey(overrideKey);
		if (string.IsNullOrWhiteSpace(normalized))
		{
			return false;
		}

		int split = normalized.IndexOf('\n');
		if (split <= 0 || split >= normalized.Length - 1)
		{
			return false;
		}

		slotKey = AudioReplacementDomainConfig.NormalizeTargetKey(normalized.Substring(0, split));
		lineKey = NormalizeTransitLineIdentity(normalized.Substring(split + 1));
		return !string.IsNullOrWhiteSpace(slotKey) && !string.IsNullOrWhiteSpace(lineKey);
	}

	private static bool ListEqualsIgnoreCaseKeys(IList<string> left, IList<string> right)
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

	private static void EnsureTransitAnnouncementLineServiceDropdownCacheCurrent()
	{
		if (s_TransitAnnouncementLineServiceDropdownCacheVersion == OptionsVersion &&
			s_TransitAnnouncementLineServiceDropdown.Length > 0)
		{
			return;
		}

		List<DropdownItem<string>> options = new List<DropdownItem<string>>(s_TransitAnnouncementServiceVoiceKeys.Length);
		for (int i = 0; i < s_TransitAnnouncementServiceVoiceKeys.Length; i++)
		{
			string key = s_TransitAnnouncementServiceVoiceKeys[i];
			if (!TryParseTransitAnnouncementServiceKey(key, out TransitAnnouncementServiceType serviceType))
			{
				continue;
			}

			options.Add(new DropdownItem<string>
			{
				value = key,
				displayName = GetTransitAnnouncementServiceLabel(serviceType)
			});
		}

		s_TransitAnnouncementLineServiceDropdown = options.ToArray();
		s_TransitAnnouncementLineServiceDropdownCacheVersion = OptionsVersion;
	}

	private static void EnsureTransitAnnouncementLineDropdownCacheCurrent()
	{
		if (s_TransitAnnouncementLineDropdownCacheVersion == OptionsVersion &&
			s_TransitAnnouncementLineDropdown.Length > 0)
		{
			return;
		}

		List<string> available = (TransitAnnouncementConfig.TransitAnnouncementKnownLines ?? new List<string>())
			.Where(static line => !string.IsNullOrWhiteSpace(line))
			.Where(line => IsLineForService(line, s_TransitAnnouncementLineEditorService))
			.OrderBy(line => GetTransitLineDisplayName(line), StringComparer.OrdinalIgnoreCase)
			.ThenBy(static line => line, StringComparer.OrdinalIgnoreCase)
			.ToList();

		if (available.Count == 0)
		{
			s_TransitAnnouncementLineDropdown = new[]
			{
				new DropdownItem<string>
				{
					value = string.Empty,
					displayName = $"No {GetTransitAnnouncementServiceLabel(s_TransitAnnouncementLineEditorService).ToLowerInvariant()} lines detected",
					disabled = true
				}
			};
			s_TransitAnnouncementLineDropdownCacheVersion = OptionsVersion;
			return;
		}

		string currentSelected = GetTransitAnnouncementSelectedLineForOptions();
		if (!available.Any(line => string.Equals(line, currentSelected, StringComparison.OrdinalIgnoreCase)))
		{
			TransitAnnouncementConfig.TransitAnnouncementSelectedLine = available[0];
		}

		List<DropdownItem<string>> options = new List<DropdownItem<string>>(available.Count);
		for (int i = 0; i < available.Count; i++)
		{
			string lineKey = available[i];
			string displayName = GetTransitLineDisplayName(lineKey);
			options.Add(new DropdownItem<string>
			{
				value = lineKey,
				displayName = string.IsNullOrWhiteSpace(displayName) ? lineKey : displayName
			});
		}

		s_TransitAnnouncementLineDropdown = options.ToArray();
		s_TransitAnnouncementLineDropdownCacheVersion = OptionsVersion;
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

	// Map a fixed slot enum to its stable lead target-selection key.
	private static string GetTransitAnnouncementLeadTargetKey(TransitAnnouncementSlot slot)
	{
		switch (slot)
		{
			case TransitAnnouncementSlot.TrainArrival:
				return s_TransitAnnouncementLeadTargetKeys[0];
			case TransitAnnouncementSlot.TrainDeparture:
				return s_TransitAnnouncementLeadTargetKeys[1];
			case TransitAnnouncementSlot.BusArrival:
				return s_TransitAnnouncementLeadTargetKeys[2];
			case TransitAnnouncementSlot.BusDeparture:
				return s_TransitAnnouncementLeadTargetKeys[3];
			case TransitAnnouncementSlot.MetroArrival:
				return s_TransitAnnouncementLeadTargetKeys[4];
			case TransitAnnouncementSlot.MetroDeparture:
				return s_TransitAnnouncementLeadTargetKeys[5];
			case TransitAnnouncementSlot.TramArrival:
				return s_TransitAnnouncementLeadTargetKeys[6];
			case TransitAnnouncementSlot.TramDeparture:
				return s_TransitAnnouncementLeadTargetKeys[7];
			default:
				return string.Empty;
		}
	}

	// Map a fixed slot enum to its stable tail target-selection key.
	private static string GetTransitAnnouncementTailTargetKey(TransitAnnouncementSlot slot)
	{
		switch (slot)
		{
			case TransitAnnouncementSlot.TrainArrival:
				return s_TransitAnnouncementTailTargetKeys[0];
			case TransitAnnouncementSlot.TrainDeparture:
				return s_TransitAnnouncementTailTargetKeys[1];
			case TransitAnnouncementSlot.BusArrival:
				return s_TransitAnnouncementTailTargetKeys[2];
			case TransitAnnouncementSlot.BusDeparture:
				return s_TransitAnnouncementTailTargetKeys[3];
			case TransitAnnouncementSlot.MetroArrival:
				return s_TransitAnnouncementTailTargetKeys[4];
			case TransitAnnouncementSlot.MetroDeparture:
				return s_TransitAnnouncementTailTargetKeys[5];
			case TransitAnnouncementSlot.TramArrival:
				return s_TransitAnnouncementTailTargetKeys[6];
			case TransitAnnouncementSlot.TramDeparture:
				return s_TransitAnnouncementTailTargetKeys[7];
			default:
				return string.Empty;
		}
	}

	private static string GetTransitAnnouncementServiceVoiceKey(TransitAnnouncementServiceType serviceType)
	{
		switch (serviceType)
		{
			case TransitAnnouncementServiceType.Train:
				return s_TransitAnnouncementServiceVoiceKeys[0];
			case TransitAnnouncementServiceType.Bus:
				return s_TransitAnnouncementServiceVoiceKeys[1];
			case TransitAnnouncementServiceType.Metro:
				return s_TransitAnnouncementServiceVoiceKeys[2];
			case TransitAnnouncementServiceType.Tram:
				return s_TransitAnnouncementServiceVoiceKeys[3];
			default:
				return string.Empty;
		}
	}

	private static bool DictionaryEqualsIgnoreCaseKeysOrdinalValues(
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
