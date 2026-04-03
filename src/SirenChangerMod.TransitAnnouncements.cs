using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.UI.Widgets;
using Unity.Entities;
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
	TramDeparture = 7,
	FerryArrival = 8,
	FerryDeparture = 9
}

// Supported transit service buckets for line overrides and slot mapping.
internal enum TransitAnnouncementServiceType
{
	Train = 0,
	Bus = 1,
	Metro = 2,
	Tram = 3,
	Ferry = 4
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

	// Canonical slot keys used when storing per-line overrides.
	private static readonly string[] s_TransitAnnouncementLeadTargetKeys =
	{
		"train.arrival",
		"train.departure",
		"bus.arrival",
		"bus.departure",
		"metro.arrival",
		"metro.departure",
		"tram.arrival",
		"tram.departure",
		"ferry.arrival",
		"ferry.departure"
	};

	private static readonly string[] s_TransitAnnouncementServiceVoiceKeys =
	{
		"train",
		"bus",
		"metro",
		"tram",
		"ferry"
	};

	private static int s_TransitAnnouncementDropdownCacheVersion = -1;

	private static DropdownItem<string>[] s_TransitAnnouncementDropdownWithDefault = Array.Empty<DropdownItem<string>>();

	private static int s_TransitAnnouncementLineServiceDropdownCacheVersion = -1;

	private static DropdownItem<string>[] s_TransitAnnouncementLineServiceDropdown = Array.Empty<DropdownItem<string>>();

	private static int s_TransitAnnouncementLineDropdownCacheVersion = -1;

	private static DropdownItem<string>[] s_TransitAnnouncementLineDropdown = Array.Empty<DropdownItem<string>>();

	private static int s_TransitAnnouncementStationDropdownCacheVersion = -1;

	private static DropdownItem<string>[] s_TransitAnnouncementStationDropdown = Array.Empty<DropdownItem<string>>();

	private static TransitAnnouncementServiceType s_TransitAnnouncementLineEditorService = TransitAnnouncementServiceType.Train;

	private static readonly HashSet<string> s_ObservedTransitLinesThisSession = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private static readonly HashSet<string> s_ObservedTransitStationsThisSession = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private static readonly HashSet<string> s_ObservedTransitStationLinesThisSession = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private static string s_LastTransitLineScanStatus =
		"No scan run yet. Click Scan Transit Lines in a loaded city session.";

	private const float kTransitObservationAutosaveIntervalSeconds = 15f;

	private static bool s_TransitObservationMetadataDirty;

	private static float s_NextTransitObservationAutosaveRealtime = -1f;

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
				ClearTransitObservationMetadataDirty();
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

	// Scan active worlds for transit lines so per-line overrides can be prepared without waiting for announcements.
	internal static void RefreshTransitLinesFromOptions()
	{
		HashSet<string> knownLinesBefore = new HashSet<string>(
			(TransitAnnouncementConfig.TransitAnnouncementKnownLines ?? new List<string>())
			.Select(NormalizeTransitLineIdentity)
			.Where(static key => !string.IsNullOrWhiteSpace(key)),
			StringComparer.OrdinalIgnoreCase);
		HashSet<string> knownStationsBefore = new HashSet<string>(
			(TransitAnnouncementConfig.TransitAnnouncementKnownStations ?? new List<string>())
			.Select(NormalizeTransitStationIdentity)
			.Where(static key => !string.IsNullOrWhiteSpace(key)),
			StringComparer.OrdinalIgnoreCase);
		HashSet<string> knownStationLinesBefore = new HashSet<string>(
			(TransitAnnouncementConfig.TransitAnnouncementKnownStationLines ?? new List<string>())
			.Select(NormalizeTransitStationLineIdentity)
			.Where(static key => !string.IsNullOrWhiteSpace(key)),
			StringComparer.OrdinalIgnoreCase);

		if (!TryScanTransitLines(
				out int scannedWorldCount,
				out int scannedVehicleCount,
				out int observedLineCount,
				out int observedStationCount,
				out int observedStationLineCount,
				out string status))
		{
			s_LastTransitLineScanStatus = status;
			OptionsVersion++;
			return;
		}

		HashSet<string> knownLinesAfter = new HashSet<string>(
			(TransitAnnouncementConfig.TransitAnnouncementKnownLines ?? new List<string>())
			.Select(NormalizeTransitLineIdentity)
			.Where(static key => !string.IsNullOrWhiteSpace(key)),
			StringComparer.OrdinalIgnoreCase);
		HashSet<string> knownStationsAfter = new HashSet<string>(
			(TransitAnnouncementConfig.TransitAnnouncementKnownStations ?? new List<string>())
			.Select(NormalizeTransitStationIdentity)
			.Where(static key => !string.IsNullOrWhiteSpace(key)),
			StringComparer.OrdinalIgnoreCase);
		HashSet<string> knownStationLinesAfter = new HashSet<string>(
			(TransitAnnouncementConfig.TransitAnnouncementKnownStationLines ?? new List<string>())
			.Select(NormalizeTransitStationLineIdentity)
			.Where(static key => !string.IsNullOrWhiteSpace(key)),
			StringComparer.OrdinalIgnoreCase);

		int addedLineCount = knownLinesAfter.Count(line => !knownLinesBefore.Contains(line));
		int addedStationCount = knownStationsAfter.Count(station => !knownStationsBefore.Contains(station));
		int addedStationLineCount = knownStationLinesAfter.Count(pair => !knownStationLinesBefore.Contains(pair));
		if (addedLineCount > 0 || addedStationCount > 0 || addedStationLineCount > 0)
		{
			SaveConfig();
			ConfigVersion++;
			ClearTransitObservationMetadataDirty();
		}

		s_LastTransitLineScanStatus =
			$"{status}\nScanned worlds: {scannedWorldCount}, vehicles: {scannedVehicleCount}, observed lines: {observedLineCount}, observed stations: {observedStationCount}, observed station-line pairs: {observedStationLineCount}\nAdded lines: {addedLineCount}, stations: {addedStationCount}, station-line pairs: {addedStationLineCount}\nKnown lines total: {knownLinesAfter.Count}, known stations total: {knownStationsAfter.Count}, known station-line pairs total: {knownStationLinesAfter.Count}";
		OptionsVersion++;
	}

	// Status text for the last explicit transit-line scan action.
	internal static string GetTransitLineScanStatusText()
	{
		return s_LastTransitLineScanStatus;
	}

	internal static void FlushTransitObservationAutosaveIfDue()
	{
		if (!s_TransitObservationMetadataDirty)
		{
			return;
		}

		if (s_NextTransitObservationAutosaveRealtime > 0f &&
			Time.unscaledTime < s_NextTransitObservationAutosaveRealtime)
		{
			return;
		}

		SaveConfig();
		ConfigVersion++;
		ClearTransitObservationMetadataDirty();
	}

	internal static void PersistTransitObservationMetadataNow()
	{
		if (!s_TransitObservationMetadataDirty)
		{
			return;
		}

		SaveConfig();
		ConfigVersion++;
		ClearTransitObservationMetadataDirty();
	}

	private static void MarkTransitObservationMetadataDirty()
	{
		s_TransitObservationMetadataDirty = true;
		s_NextTransitObservationAutosaveRealtime = Time.unscaledTime + kTransitObservationAutosaveIntervalSeconds;
	}

	private static void ClearTransitObservationMetadataDirty()
	{
		s_TransitObservationMetadataDirty = false;
		s_NextTransitObservationAutosaveRealtime = -1f;
	}

	// Clear per-session observed-line memory when loading a new city/session.
	internal static void ResetTransitLineObservationSession()
	{
		s_ObservedTransitLinesThisSession.Clear();
		s_ObservedTransitStationsThisSession.Clear();
		s_ObservedTransitStationLinesThisSession.Clear();
	}

	// Remove discovered lines that are no longer observed in-session and have no overrides.
	internal static void PruneStaleTransitAnnouncementLinesFromOptions()
	{
		HashSet<string> protectedLines = GetTransitLineKeysReferencedByOverrides();
		HashSet<string> protectedStations = GetTransitStationKeysReferencedByOverrides();
		HashSet<string> protectedStationLines = GetTransitStationLineKeysReferencedByOverrides();

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

		List<string> existingKnownStations = TransitAnnouncementConfig.TransitAnnouncementKnownStations ?? new List<string>();
		List<string> keptStations = new List<string>(existingKnownStations.Count);
		for (int i = 0; i < existingKnownStations.Count; i++)
		{
			string normalized = NormalizeTransitStationIdentity(existingKnownStations[i]);
			if (string.IsNullOrWhiteSpace(normalized))
			{
				continue;
			}

			if (protectedStations.Contains(normalized) || s_ObservedTransitStationsThisSession.Contains(normalized))
			{
				keptStations.Add(normalized);
			}
		}

		foreach (string protectedStation in protectedStations)
		{
			if (keptStations.Any(station => string.Equals(station, protectedStation, StringComparison.OrdinalIgnoreCase)))
			{
				continue;
			}

			keptStations.Add(protectedStation);
		}

		keptStations.Sort(StringComparer.OrdinalIgnoreCase);
		if (!ListEqualsIgnoreCaseKeys(existingKnownStations, keptStations))
		{
			TransitAnnouncementConfig.TransitAnnouncementKnownStations = keptStations;
			changed = true;
		}

		List<string> existingKnownStationLines = TransitAnnouncementConfig.TransitAnnouncementKnownStationLines ?? new List<string>();
		List<string> keptStationLines = new List<string>(existingKnownStationLines.Count);
		for (int i = 0; i < existingKnownStationLines.Count; i++)
		{
			string normalizedPair = NormalizeTransitStationLineIdentity(existingKnownStationLines[i]);
			if (string.IsNullOrWhiteSpace(normalizedPair) ||
				!TryParseTransitStationLineIdentity(normalizedPair, out string stationKey, out string lineKey) ||
				!keptStations.Any(station => string.Equals(station, stationKey, StringComparison.OrdinalIgnoreCase)) ||
				!keptLines.Any(line => string.Equals(line, lineKey, StringComparison.OrdinalIgnoreCase)))
			{
				continue;
			}

			if (protectedStationLines.Contains(normalizedPair) || s_ObservedTransitStationLinesThisSession.Contains(normalizedPair))
			{
				keptStationLines.Add(normalizedPair);
			}
		}

		foreach (string protectedPair in protectedStationLines)
		{
			if (!TryParseTransitStationLineIdentity(protectedPair, out string protectedStation, out string protectedLine) ||
				!keptStations.Any(station => string.Equals(station, protectedStation, StringComparison.OrdinalIgnoreCase)) ||
				!keptLines.Any(line => string.Equals(line, protectedLine, StringComparison.OrdinalIgnoreCase)) ||
				keptStationLines.Any(pair => string.Equals(pair, protectedPair, StringComparison.OrdinalIgnoreCase)))
			{
				continue;
			}

			keptStationLines.Add(protectedPair);
		}

		keptStationLines.Sort(StringComparer.OrdinalIgnoreCase);
		if (!ListEqualsIgnoreCaseKeys(existingKnownStationLines, keptStationLines))
		{
			TransitAnnouncementConfig.TransitAnnouncementKnownStationLines = keptStationLines;
			changed = true;
		}

		Dictionary<string, string> lineDisplayMap = TransitAnnouncementConfig.TransitAnnouncementLineDisplayByKey ??
			new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		List<string> lineDisplayKeys = lineDisplayMap.Keys.ToList();
		for (int i = 0; i < lineDisplayKeys.Count; i++)
		{
			string normalized = NormalizeTransitLineIdentity(lineDisplayKeys[i]);
			if (!keptLines.Any(line => string.Equals(line, normalized, StringComparison.OrdinalIgnoreCase)))
			{
				lineDisplayMap.Remove(lineDisplayKeys[i]);
				changed = true;
			}
		}

		TransitAnnouncementConfig.TransitAnnouncementLineDisplayByKey = lineDisplayMap;

		Dictionary<string, string> stationDisplayMap = TransitAnnouncementConfig.TransitAnnouncementStationDisplayByKey ??
			new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		List<string> stationDisplayKeys = stationDisplayMap.Keys.ToList();
		for (int i = 0; i < stationDisplayKeys.Count; i++)
		{
			string normalized = NormalizeTransitStationIdentity(stationDisplayKeys[i]);
			if (!keptStations.Any(station => string.Equals(station, normalized, StringComparison.OrdinalIgnoreCase)))
			{
				stationDisplayMap.Remove(stationDisplayKeys[i]);
				changed = true;
			}
		}

		TransitAnnouncementConfig.TransitAnnouncementStationDisplayByKey = stationDisplayMap;

		string selectedStation = NormalizeTransitStationIdentity(TransitAnnouncementConfig.TransitAnnouncementSelectedStation);
		if (!string.IsNullOrWhiteSpace(selectedStation) &&
			!keptStations.Any(station => string.Equals(station, selectedStation, StringComparison.OrdinalIgnoreCase)))
		{
			TransitAnnouncementConfig.TransitAnnouncementSelectedStation = GetFirstTransitStationForService(s_TransitAnnouncementLineEditorService);
			changed = true;
		}

		string selectedLine = NormalizeTransitLineIdentity(TransitAnnouncementConfig.TransitAnnouncementSelectedLine);
		string effectiveStation = NormalizeTransitStationIdentity(TransitAnnouncementConfig.TransitAnnouncementSelectedStation);
		if (!string.IsNullOrWhiteSpace(selectedLine) &&
			!IsStationLineForService(effectiveStation, selectedLine, s_TransitAnnouncementLineEditorService))
		{
			TransitAnnouncementConfig.TransitAnnouncementSelectedLine = GetFirstTransitLineForStationService(effectiveStation, s_TransitAnnouncementLineEditorService);
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
			ClearTransitObservationMetadataDirty();
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

	// Build dropdown items for selecting a discovered station within the selected service.
	internal static DropdownItem<string>[] BuildTransitAnnouncementStationDropdownItems()
	{
		EnsureTransitAnnouncementStationDropdownCacheCurrent();
		return s_TransitAnnouncementStationDropdown;
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
			s_TransitAnnouncementStationDropdownCacheVersion = -1;
			s_TransitAnnouncementStationDropdown = Array.Empty<DropdownItem<string>>();
			s_TransitAnnouncementLineDropdownCacheVersion = -1;
			s_TransitAnnouncementLineDropdown = Array.Empty<DropdownItem<string>>();
			changed = true;
		}

		string selectedStation = GetTransitAnnouncementSelectedStationForOptions();
		if (!IsStationAvailableForService(selectedStation, serviceType))
		{
			string firstStationForService = GetFirstTransitStationForService(serviceType);
			SetTransitAnnouncementSelectedStationForOptions(firstStationForService);
			selectedStation = GetTransitAnnouncementSelectedStationForOptions();
			changed = true;
		}

		string selectedLine = GetTransitAnnouncementSelectedLineForOptions();
		if (!IsStationLineForService(selectedStation, selectedLine, serviceType))
		{
			string firstLineForService = GetFirstTransitLineForStationService(selectedStation, serviceType);
			SetTransitAnnouncementSelectedLineForOptions(firstLineForService);
			changed = true;
		}

		if (changed)
		{
			OptionsVersion++;
		}
	}

	internal static string GetTransitAnnouncementSelectedStationForOptions()
	{
		string selected = NormalizeTransitStationIdentity(TransitAnnouncementConfig.TransitAnnouncementSelectedStation);
		if (!string.IsNullOrWhiteSpace(selected))
		{
			return selected;
		}

		string firstForService = GetFirstTransitStationForService(s_TransitAnnouncementLineEditorService);
		if (!string.IsNullOrWhiteSpace(firstForService))
		{
			TransitAnnouncementConfig.TransitAnnouncementSelectedStation = firstForService;
			return firstForService;
		}

		return string.Empty;
	}

	internal static void SetTransitAnnouncementSelectedStationForOptions(string stationKey)
	{
		string normalized = NormalizeTransitStationIdentity(stationKey);
		if (string.Equals(TransitAnnouncementConfig.TransitAnnouncementSelectedStation, normalized, StringComparison.Ordinal))
		{
			return;
		}

		TransitAnnouncementConfig.TransitAnnouncementSelectedStation = normalized;
		if (!string.IsNullOrWhiteSpace(normalized))
		{
			TrackTransitStationForOptions(normalized);
		}

		string selectedLine = GetTransitAnnouncementSelectedLineForOptions();
		if (!IsStationLineForService(normalized, selectedLine, s_TransitAnnouncementLineEditorService))
		{
			TransitAnnouncementConfig.TransitAnnouncementSelectedLine =
				GetFirstTransitLineForStationService(normalized, s_TransitAnnouncementLineEditorService);
		}

		OptionsVersion++;
	}

	internal static string GetTransitAnnouncementSelectedLineForOptions()
	{
		string selectedStation = GetTransitAnnouncementSelectedStationForOptions();
		string selected = NormalizeTransitLineIdentity(TransitAnnouncementConfig.TransitAnnouncementSelectedLine);
		if (!string.IsNullOrWhiteSpace(selected) &&
			IsStationLineForService(selectedStation, selected, s_TransitAnnouncementLineEditorService))
		{
			return selected;
		}

		string firstForService = GetFirstTransitLineForStationService(selectedStation, s_TransitAnnouncementLineEditorService);
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
		string selectedStation = GetTransitAnnouncementSelectedStationForOptions();
		if (!string.IsNullOrWhiteSpace(normalized) &&
			!IsStationLineForService(selectedStation, normalized, s_TransitAnnouncementLineEditorService))
		{
			string stationForLine = GetFirstTransitStationForLineService(normalized, s_TransitAnnouncementLineEditorService);
			if (!string.IsNullOrWhiteSpace(stationForLine))
			{
				TransitAnnouncementConfig.TransitAnnouncementSelectedStation = stationForLine;
				selectedStation = stationForLine;
			}
		}

		if (!string.IsNullOrWhiteSpace(normalized) &&
			!IsStationLineForService(selectedStation, normalized, s_TransitAnnouncementLineEditorService))
		{
			normalized = GetFirstTransitLineForStationService(selectedStation, s_TransitAnnouncementLineEditorService);
		}

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

		TrackTransitLineForOptions(normalized, string.Empty);
		OptionsVersion++;
	}

	internal static string GetSelectedTransitAnnouncementLineStatusText()
	{
		string selectedStation = GetTransitAnnouncementSelectedStationForOptions();
		if (string.IsNullOrWhiteSpace(selectedStation))
		{
			return $"No {GetTransitAnnouncementServiceLabel(s_TransitAnnouncementLineEditorService).ToLowerInvariant()} stations detected yet. Drive lines through stations in this city or click Scan Transit Lines.";
		}

		string selectedLine = GetTransitAnnouncementSelectedLineForOptions();
		if (string.IsNullOrWhiteSpace(selectedLine))
		{
			return $"No {GetTransitAnnouncementServiceLabel(s_TransitAnnouncementLineEditorService).ToLowerInvariant()} lines detected for the selected station yet. Drive the line through this station or click Scan Transit Lines.";
		}

		if (!TryParseTransitLineIdentity(selectedLine, out TransitAnnouncementServiceType serviceType, out string lineStableId))
		{
			return "Selected line key is invalid.";
		}

		string stationDisplayName = GetTransitStationDisplayName(selectedStation);
		string stationStableId = GetTransitStationStableId(selectedStation);
		string lineDisplayName = GetTransitLineDisplayName(selectedLine);
		if (string.IsNullOrWhiteSpace(lineDisplayName))
		{
			lineDisplayName = lineStableId;
		}
		if (string.IsNullOrWhiteSpace(stationDisplayName))
		{
			stationDisplayName = stationStableId;
		}

		string arrivalSelection = GetTransitAnnouncementStationLineSelection(ResolveServiceSlot(serviceType, isArrival: true), selectedStation, selectedLine);
		string departureSelection = GetTransitAnnouncementStationLineSelection(ResolveServiceSlot(serviceType, isArrival: false), selectedStation, selectedLine);
		string arrivalText = AudioReplacementDomainConfig.IsDefaultSelection(arrivalSelection)
			? "None"
			: FormatSirenDisplayName(arrivalSelection);
		string departureText = AudioReplacementDomainConfig.IsDefaultSelection(departureSelection)
			? "None"
			: FormatSirenDisplayName(departureSelection);
		return $"Station: {stationDisplayName}\nStation ID: {stationStableId}\nLine: {lineDisplayName}\nLine ID: {lineStableId}\nService: {GetTransitAnnouncementServiceLabel(serviceType)}\nArrival override: {arrivalText}\nDeparture override: {departureText}";
	}

	internal static string GetTransitAnnouncementLineArrivalSelectionForOptions()
	{
		string selectedStation = GetTransitAnnouncementSelectedStationForOptions();
		string selectedLine = GetTransitAnnouncementSelectedLineForOptions();
		if (string.IsNullOrWhiteSpace(selectedStation) ||
			string.IsNullOrWhiteSpace(selectedLine) ||
			!TryParseTransitLineIdentity(selectedLine, out TransitAnnouncementServiceType serviceType, out _))
		{
			return SirenReplacementConfig.DefaultSelectionToken;
		}

		return GetTransitAnnouncementStationLineSelection(ResolveServiceSlot(serviceType, isArrival: true), selectedStation, selectedLine);
	}

	internal static void SetTransitAnnouncementLineArrivalSelectionForOptions(string selection)
	{
		string selectedStation = GetTransitAnnouncementSelectedStationForOptions();
		string selectedLine = GetTransitAnnouncementSelectedLineForOptions();
		if (string.IsNullOrWhiteSpace(selectedStation) ||
			string.IsNullOrWhiteSpace(selectedLine) ||
			!TryParseTransitLineIdentity(selectedLine, out TransitAnnouncementServiceType serviceType, out _))
		{
			return;
		}

		SetTransitAnnouncementStationLineSelection(
			ResolveServiceSlot(serviceType, isArrival: true),
			selectedStation,
			selectedLine,
			selection);
	}

	internal static string GetTransitAnnouncementLineDepartureSelectionForOptions()
	{
		string selectedStation = GetTransitAnnouncementSelectedStationForOptions();
		string selectedLine = GetTransitAnnouncementSelectedLineForOptions();
		if (string.IsNullOrWhiteSpace(selectedStation) ||
			string.IsNullOrWhiteSpace(selectedLine) ||
			!TryParseTransitLineIdentity(selectedLine, out TransitAnnouncementServiceType serviceType, out _))
		{
			return SirenReplacementConfig.DefaultSelectionToken;
		}

		return GetTransitAnnouncementStationLineSelection(ResolveServiceSlot(serviceType, isArrival: false), selectedStation, selectedLine);
	}

	internal static void SetTransitAnnouncementLineDepartureSelectionForOptions(string selection)
	{
		string selectedStation = GetTransitAnnouncementSelectedStationForOptions();
		string selectedLine = GetTransitAnnouncementSelectedLineForOptions();
		if (string.IsNullOrWhiteSpace(selectedStation) ||
			string.IsNullOrWhiteSpace(selectedLine) ||
			!TryParseTransitLineIdentity(selectedLine, out TransitAnnouncementServiceType serviceType, out _))
		{
			return;
		}

		SetTransitAnnouncementStationLineSelection(
			ResolveServiceSlot(serviceType, isArrival: false),
			selectedStation,
			selectedLine,
			selection);
	}

	internal static bool IsTransitAnnouncementLineEditorDisabled()
	{
		return string.IsNullOrWhiteSpace(GetTransitAnnouncementSelectedStationForOptions()) ||
			string.IsNullOrWhiteSpace(GetTransitAnnouncementSelectedLineForOptions());
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

	// Build the full step sequence for one transit event without TTS dependencies.
	internal static TransitAnnouncementLoadStatus TryBuildTransitAnnouncementSequence(
		TransitAnnouncementSlot slot,
		string lineKey,
		out List<TransitAnnouncementPlaybackSegment> segments,
		out string message)
	{
		return TryBuildTransitAnnouncementSequence(slot, string.Empty, lineKey, out segments, out message);
	}

	// Build the full step sequence for one station+line transit event without TTS dependencies.
	internal static TransitAnnouncementLoadStatus TryBuildTransitAnnouncementSequence(
		TransitAnnouncementSlot slot,
		string stationKey,
		string lineKey,
		out List<TransitAnnouncementPlaybackSegment> segments,
		out string message)
	{
		segments = new List<TransitAnnouncementPlaybackSegment>(1);
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
			stationKey,
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

	private static TransitAnnouncementLoadStatus TryAppendPrimaryTransitAnnouncementStep(
		TransitAnnouncementSlot slot,
		string stationKey,
		string lineKey,
		ICollection<TransitAnnouncementPlaybackSegment> segments,
		out string message)
	{
		string normalizedStationKey = NormalizeTransitStationIdentity(stationKey);
		string normalizedLineKey = NormalizeTransitLineIdentity(lineKey);
		if (string.IsNullOrWhiteSpace(normalizedLineKey))
		{
			message = "Transit line could not be resolved for this vehicle.";
			return TransitAnnouncementLoadStatus.NotConfigured;
		}

		string lineOverrideSelection = GetTransitAnnouncementStationLineSelection(slot, normalizedStationKey, normalizedLineKey);
		return TryAppendConfiguredAudioStep(
			lineOverrideSelection,
			"Line announcement audio",
			segments,
			out message);
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

	// Transit announcements are now line-only; remove any legacy slot target selections.
	internal static bool NormalizeTransitAnnouncementTargets()
	{
		bool changed = false;
		if (TransitAnnouncementConfig.TargetSelections.Count > 0)
		{
			TransitAnnouncementConfig.TargetSelections.Clear();
			changed = true;
		}

		if (TransitAnnouncementConfig.KnownTargets.Count > 0)
		{
			TransitAnnouncementConfig.KnownTargets.Clear();
			changed = true;
		}

		if (!string.IsNullOrWhiteSpace(TransitAnnouncementConfig.TargetSelectionTarget))
		{
			TransitAnnouncementConfig.TargetSelectionTarget = string.Empty;
			changed = true;
		}

		return changed;
	}

	// Run one explicit transit-line scan across currently loaded worlds.
	private static bool TryScanTransitLines(
		out int scannedWorldCount,
		out int scannedVehicleCount,
		out int observedLineCount,
		out int observedStationCount,
		out int observedStationLineCount,
		out string status)
	{
		scannedWorldCount = 0;
		scannedVehicleCount = 0;
		observedLineCount = 0;
		observedStationCount = 0;
		observedStationLineCount = 0;
		status = string.Empty;

		string firstFailure = string.Empty;
		var worlds = World.All;
		for (int i = 0; i < worlds.Count; i++)
		{
			World world = worlds[i];
			if (world == null || !world.IsCreated)
			{
				continue;
			}

			TransitAnnouncementSystem? transitSystem = world.GetExistingSystemManaged<TransitAnnouncementSystem>();
			if (transitSystem == null)
			{
				continue;
			}

			if (!transitSystem.TryScanTransitLinesForOptions(
					out int worldVehicleCount,
					out int worldObservedLineCount,
					out int worldObservedStationCount,
					out int worldObservedStationLineCount,
					out string worldStatus))
			{
				if (string.IsNullOrWhiteSpace(firstFailure) && !string.IsNullOrWhiteSpace(worldStatus))
				{
					firstFailure = worldStatus;
				}

				continue;
			}

			scannedWorldCount++;
			scannedVehicleCount += worldVehicleCount;
			observedLineCount += worldObservedLineCount;
			observedStationCount += worldObservedStationCount;
			observedStationLineCount += worldObservedStationLineCount;
		}

		if (scannedWorldCount == 0)
		{
			status = string.IsNullOrWhiteSpace(firstFailure)
				? "No active transit simulation world is available. Load a city and retry."
				: firstFailure;
			return false;
		}

		status = $"Last scan: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
		return true;
	}

	// Keep transit line selections and known-line metadata aligned and normalized.
	internal static bool NormalizeTransitAnnouncementSpeechSettings()
	{
		bool changed = false;
		TransitAnnouncementConfig.TransitAnnouncementLineDisplayByKey ??=
			new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		TransitAnnouncementConfig.TransitAnnouncementStationDisplayByKey ??=
			new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		TransitAnnouncementConfig.TransitAnnouncementStationLineSelections ??=
			new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		TransitAnnouncementConfig.TransitAnnouncementKnownStations ??= new List<string>();
		TransitAnnouncementConfig.TransitAnnouncementKnownStationLines ??= new List<string>();

		HashSet<string> validLineSlotKeys = new HashSet<string>(s_TransitAnnouncementLeadTargetKeys, StringComparer.OrdinalIgnoreCase);
		Dictionary<string, string> normalizedLineSelections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		Dictionary<string, string> normalizedStationLineSelections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		HashSet<string> knownLines = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		HashSet<string> knownStations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		HashSet<string> knownStationLines = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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

		foreach (KeyValuePair<string, string> pair in TransitAnnouncementConfig.TransitAnnouncementStationLineSelections)
		{
			if (!TryParseTransitStationLineOverrideKey(pair.Key, out string slotKey, out string stationKey, out string lineKey))
			{
				changed = true;
				continue;
			}

			slotKey = AudioReplacementDomainConfig.NormalizeTargetKey(slotKey);
			stationKey = NormalizeTransitStationIdentity(stationKey);
			lineKey = NormalizeTransitLineIdentity(lineKey);
			if (string.IsNullOrWhiteSpace(slotKey) ||
				string.IsNullOrWhiteSpace(stationKey) ||
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

			string normalizedOverrideKey = BuildTransitStationLineOverrideKey(slotKey, stationKey, lineKey);
			if (string.IsNullOrWhiteSpace(normalizedOverrideKey))
			{
				changed = true;
				continue;
			}

			normalizedStationLineSelections[normalizedOverrideKey] = selection;
			knownLines.Add(lineKey);
			knownStations.Add(stationKey);
			knownStationLines.Add(BuildTransitStationLineIdentity(stationKey, lineKey));
		}

		if (!DictionaryEqualsIgnoreCaseKeysOrdinalValues(
			TransitAnnouncementConfig.TransitAnnouncementLineSelections,
			normalizedLineSelections))
		{
			TransitAnnouncementConfig.TransitAnnouncementLineSelections = normalizedLineSelections;
			changed = true;
		}

		if (!DictionaryEqualsIgnoreCaseKeysOrdinalValues(
			TransitAnnouncementConfig.TransitAnnouncementStationLineSelections,
			normalizedStationLineSelections))
		{
			TransitAnnouncementConfig.TransitAnnouncementStationLineSelections = normalizedStationLineSelections;
			changed = true;
		}

		string selectedLine = NormalizeTransitLineIdentity(TransitAnnouncementConfig.TransitAnnouncementSelectedLine);
		if (!string.IsNullOrWhiteSpace(selectedLine))
		{
			knownLines.Add(selectedLine);
		}

		string selectedStation = NormalizeTransitStationIdentity(TransitAnnouncementConfig.TransitAnnouncementSelectedStation);
		if (!string.IsNullOrWhiteSpace(selectedStation))
		{
			knownStations.Add(selectedStation);
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

		foreach (string rawStation in TransitAnnouncementConfig.TransitAnnouncementKnownStations)
		{
			string stationKey = NormalizeTransitStationIdentity(rawStation);
			if (string.IsNullOrWhiteSpace(stationKey))
			{
				changed = true;
				continue;
			}

			knownStations.Add(stationKey);
		}

		foreach (string rawStationLine in TransitAnnouncementConfig.TransitAnnouncementKnownStationLines)
		{
			string stationLineKey = NormalizeTransitStationLineIdentity(rawStationLine);
			if (string.IsNullOrWhiteSpace(stationLineKey) ||
				!TryParseTransitStationLineIdentity(stationLineKey, out string stationKey, out string lineKey))
			{
				changed = true;
				continue;
			}

			knownStations.Add(stationKey);
			knownLines.Add(lineKey);
			knownStationLines.Add(stationLineKey);
		}

		List<string> normalizedKnownLines = knownLines
			.OrderBy(static line => line, StringComparer.OrdinalIgnoreCase)
			.ToList();
		if (!ListEqualsIgnoreCaseKeys(TransitAnnouncementConfig.TransitAnnouncementKnownLines, normalizedKnownLines))
		{
			TransitAnnouncementConfig.TransitAnnouncementKnownLines = normalizedKnownLines;
			changed = true;
		}

		List<string> normalizedKnownStations = knownStations
			.OrderBy(static station => station, StringComparer.OrdinalIgnoreCase)
			.ToList();
		if (!ListEqualsIgnoreCaseKeys(TransitAnnouncementConfig.TransitAnnouncementKnownStations, normalizedKnownStations))
		{
			TransitAnnouncementConfig.TransitAnnouncementKnownStations = normalizedKnownStations;
			changed = true;
		}

		List<string> normalizedKnownStationLines = knownStationLines
			.Where(static pair => !string.IsNullOrWhiteSpace(pair))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.OrderBy(static pair => pair, StringComparer.OrdinalIgnoreCase)
			.ToList();
		if (!ListEqualsIgnoreCaseKeys(TransitAnnouncementConfig.TransitAnnouncementKnownStationLines, normalizedKnownStationLines))
		{
			TransitAnnouncementConfig.TransitAnnouncementKnownStationLines = normalizedKnownStationLines;
			changed = true;
		}

		if (string.IsNullOrWhiteSpace(selectedStation))
		{
			selectedStation = GetFirstTransitStationForService(s_TransitAnnouncementLineEditorService);
		}
		else if (!knownStations.Contains(selectedStation))
		{
			knownStations.Add(selectedStation);
			TransitAnnouncementConfig.TransitAnnouncementKnownStations = knownStations
				.OrderBy(static station => station, StringComparer.OrdinalIgnoreCase)
				.ToList();
			changed = true;
		}

		if (!string.Equals(TransitAnnouncementConfig.TransitAnnouncementSelectedStation, selectedStation, StringComparison.Ordinal))
		{
			TransitAnnouncementConfig.TransitAnnouncementSelectedStation = selectedStation;
			changed = true;
		}

		if (string.IsNullOrWhiteSpace(selectedLine) ||
			!IsStationLineForService(selectedStation, selectedLine, s_TransitAnnouncementLineEditorService))
		{
			selectedLine = GetFirstTransitLineForStationService(selectedStation, s_TransitAnnouncementLineEditorService);
		}

		if (string.IsNullOrWhiteSpace(selectedLine))
		{
			selectedLine = GetFirstTransitLineForService(s_TransitAnnouncementLineEditorService);
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

		Dictionary<string, string> normalizedStationDisplayMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (KeyValuePair<string, string> pair in TransitAnnouncementConfig.TransitAnnouncementStationDisplayByKey)
		{
			string stationKey = NormalizeTransitStationIdentity(pair.Key);
			if (string.IsNullOrWhiteSpace(stationKey) || !knownStations.Contains(stationKey))
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

			normalizedStationDisplayMap[stationKey] = displayName;
		}

		if (!DictionaryEqualsIgnoreCaseKeysOrdinalValues(
			TransitAnnouncementConfig.TransitAnnouncementStationDisplayByKey,
			normalizedStationDisplayMap))
		{
			TransitAnnouncementConfig.TransitAnnouncementStationDisplayByKey = normalizedStationDisplayMap;
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
			case TransitAnnouncementSlot.FerryArrival:
				return "Ferry Arrival";
			case TransitAnnouncementSlot.FerryDeparture:
				return "Ferry Departure";
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
			case TransitAnnouncementServiceType.Ferry:
				return "Ferry";
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
			MarkTransitObservationMetadataDirty();
			NotifyOptionsCatalogChanged();
		}
	}

	internal static void RegisterTransitStationLineObservation(
		string stationKey,
		string stationDisplayName,
		string lineKey,
		string lineDisplayName)
	{
		string normalizedStationKey = NormalizeTransitStationIdentity(stationKey);
		string normalizedLineKey = NormalizeTransitLineIdentity(lineKey);
		if (string.IsNullOrWhiteSpace(normalizedLineKey))
		{
			return;
		}

		if (string.IsNullOrWhiteSpace(normalizedStationKey))
		{
			RegisterTransitLineObservation(normalizedLineKey, lineDisplayName);
			return;
		}

		string stationLineKey = BuildTransitStationLineIdentity(normalizedStationKey, normalizedLineKey);
		if (string.IsNullOrWhiteSpace(stationLineKey))
		{
			RegisterTransitLineObservation(normalizedLineKey, lineDisplayName);
			return;
		}

		s_ObservedTransitLinesThisSession.Add(normalizedLineKey);
		s_ObservedTransitStationsThisSession.Add(normalizedStationKey);
		s_ObservedTransitStationLinesThisSession.Add(stationLineKey);
		if (TrackTransitStationLineForOptions(
				normalizedStationKey,
				stationDisplayName,
				normalizedLineKey,
				lineDisplayName))
		{
			MarkTransitObservationMetadataDirty();
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

	internal static string BuildTransitStationIdentity(string stationStableId)
	{
		string normalizedStableId = NormalizeTransitStationStableId(stationStableId);
		return string.IsNullOrWhiteSpace(normalizedStableId)
			? string.Empty
			: normalizedStableId;
	}

	internal static bool TryParseTransitStationIdentity(string stationKey, out string stationStableId)
	{
		stationStableId = NormalizeTransitStationStableId(stationKey);
		return !string.IsNullOrWhiteSpace(stationStableId);
	}

	internal static string NormalizeTransitStationIdentity(string stationKey)
	{
		return TryParseTransitStationIdentity(stationKey, out string stationStableId)
			? BuildTransitStationIdentity(stationStableId)
			: string.Empty;
	}

	internal static string BuildTransitStationLineIdentity(string stationKey, string lineKey)
	{
		string normalizedStationKey = NormalizeTransitStationIdentity(stationKey);
		string normalizedLineKey = NormalizeTransitLineIdentity(lineKey);
		if (string.IsNullOrWhiteSpace(normalizedStationKey) || string.IsNullOrWhiteSpace(normalizedLineKey))
		{
			return string.Empty;
		}

		return $"{normalizedStationKey}\n{normalizedLineKey}";
	}

	internal static bool TryParseTransitStationLineIdentity(
		string stationLineKey,
		out string stationKey,
		out string lineKey)
	{
		stationKey = string.Empty;
		lineKey = string.Empty;
		string normalized = AudioReplacementDomainConfig.NormalizeTransitLineKey(stationLineKey);
		if (string.IsNullOrWhiteSpace(normalized))
		{
			return false;
		}

		int split = normalized.IndexOf('\n');
		if (split <= 0 || split >= normalized.Length - 1)
		{
			return false;
		}

		stationKey = NormalizeTransitStationIdentity(normalized.Substring(0, split));
		lineKey = NormalizeTransitLineIdentity(normalized.Substring(split + 1));
		return !string.IsNullOrWhiteSpace(stationKey) && !string.IsNullOrWhiteSpace(lineKey);
	}

	internal static string NormalizeTransitStationLineIdentity(string stationLineKey)
	{
		return TryParseTransitStationLineIdentity(stationLineKey, out string stationKey, out string lineKey)
			? BuildTransitStationLineIdentity(stationKey, lineKey)
			: string.Empty;
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

	internal static string GetTransitStationDisplayName(string stationKey)
	{
		string normalizedStationKey = NormalizeTransitStationIdentity(stationKey);
		if (string.IsNullOrWhiteSpace(normalizedStationKey))
		{
			return string.Empty;
		}

		Dictionary<string, string> displayMap = TransitAnnouncementConfig.TransitAnnouncementStationDisplayByKey ??
			new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		if (displayMap.TryGetValue(normalizedStationKey, out string displayName) &&
			!string.IsNullOrWhiteSpace(displayName))
		{
			return displayName;
		}

		return FormatTransitStationStableIdForDisplay(GetTransitStationStableId(normalizedStationKey));
	}

	internal static string GetTransitStationStableId(string stationKey)
	{
		return TryParseTransitStationIdentity(stationKey, out string stableId)
			? stableId
			: string.Empty;
	}

	internal static string GetTransitAnnouncementStationLineSelection(
		TransitAnnouncementSlot slot,
		string stationKey,
		string lineKey)
	{
		string normalizedStationKey = NormalizeTransitStationIdentity(stationKey);
		string normalizedLineKey = NormalizeTransitLineIdentity(lineKey);
		string slotKey = GetTransitAnnouncementLeadTargetKey(slot);
		if (string.IsNullOrWhiteSpace(normalizedLineKey) || string.IsNullOrWhiteSpace(slotKey))
		{
			return SirenReplacementConfig.DefaultSelectionToken;
		}

		if (!string.IsNullOrWhiteSpace(normalizedStationKey))
		{
			string overrideKey = BuildTransitStationLineOverrideKey(slotKey, normalizedStationKey, normalizedLineKey);
			if (TryGetTransitAnnouncementStationSelectionByOverrideKey(overrideKey, out string stationSelection) ||
				TryGetTransitAnnouncementStationSelectionByFallback(slotKey, normalizedStationKey, normalizedLineKey, out stationSelection))
			{
				string normalizedSelection = AudioReplacementDomainConfig.NormalizeProfileKey(stationSelection);
				return AudioReplacementDomainConfig.IsDefaultSelection(normalizedSelection)
					? SirenReplacementConfig.DefaultSelectionToken
					: normalizedSelection;
			}

			// Station-specific configuration takes precedence: no station override means no playback.
			return SirenReplacementConfig.DefaultSelectionToken;
		}

		return GetTransitAnnouncementLineSelection(slot, normalizedLineKey);
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
			if (!TryParseTransitLineIdentity(normalizedLineKey, out TransitAnnouncementServiceType serviceType, out string stableId) ||
				!TryGetTransitAnnouncementSelectionWithLegacyFallback(
					slotKey,
					normalizedLineKey,
					serviceType,
					stableId,
					out selection))
			{
				return SirenReplacementConfig.DefaultSelectionToken;
			}
		}

		string normalizedSelection = AudioReplacementDomainConfig.NormalizeProfileKey(selection);
		return AudioReplacementDomainConfig.IsDefaultSelection(normalizedSelection)
			? SirenReplacementConfig.DefaultSelectionToken
			: normalizedSelection;
	}

	private static bool TryGetTransitAnnouncementStationSelectionByOverrideKey(string overrideKey, out string selection)
	{
		selection = string.Empty;
		return !string.IsNullOrWhiteSpace(overrideKey) &&
			TransitAnnouncementConfig.TransitAnnouncementStationLineSelections.TryGetValue(overrideKey, out selection);
	}

	private static bool TryGetTransitAnnouncementStationSelectionByFallback(
		string slotKey,
		string stationKey,
		string lineKey,
		out string selection)
	{
		selection = string.Empty;
		foreach (KeyValuePair<string, string> pair in TransitAnnouncementConfig.TransitAnnouncementStationLineSelections)
		{
			if (!TryParseTransitStationLineOverrideKey(pair.Key, out string candidateSlotKey, out string candidateStationKey, out string candidateLineKey) ||
				!string.Equals(candidateSlotKey, slotKey, StringComparison.OrdinalIgnoreCase) ||
				!DoesStationKeyMatchFallback(stationKey, candidateStationKey) ||
				!DoesLineKeyMatchFallback(lineKey, candidateLineKey))
			{
				continue;
			}

			selection = pair.Value;
			return true;
		}

		return false;
	}

	private static bool TryGetTransitAnnouncementSelectionByOverrideKey(string overrideKey, out string selection)
	{
		selection = string.Empty;
		return !string.IsNullOrWhiteSpace(overrideKey) &&
			TransitAnnouncementConfig.TransitAnnouncementLineSelections.TryGetValue(overrideKey, out selection);
	}

	private static bool TryGetTransitAnnouncementSelectionWithLegacyFallback(
		string slotKey,
		string normalizedLineKey,
		TransitAnnouncementServiceType serviceType,
		string stableId,
		out string selection)
	{
		// Legacy overrides may still use number/entity/label line identity formats.
		foreach (string fallbackLineKey in GetTransitLineIdentityFallbackKeys(normalizedLineKey, serviceType, stableId))
		{
			if (string.IsNullOrWhiteSpace(fallbackLineKey) ||
				string.Equals(fallbackLineKey, normalizedLineKey, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			string fallbackOverrideKey = BuildTransitLineOverrideKey(slotKey, fallbackLineKey);
			if (TryGetTransitAnnouncementSelectionByOverrideKey(fallbackOverrideKey, out selection))
			{
				return true;
			}
		}

		if (TryGetTransitAnnouncementSelectionByRouteNumberFallback(slotKey, serviceType, stableId, out selection))
		{
			return true;
		}

		if (TryGetTransitAnnouncementSelectionByDisplayNameFallback(
			slotKey,
			serviceType,
			normalizedLineKey,
			out selection))
		{
			return true;
		}

		selection = string.Empty;
		return false;
	}

	private static bool DoesLineKeyMatchFallback(string expectedLineKey, string candidateLineKey)
	{
		string normalizedExpectedLineKey = NormalizeTransitLineIdentity(expectedLineKey);
		string normalizedCandidateLineKey = NormalizeTransitLineIdentity(candidateLineKey);
		if (string.IsNullOrWhiteSpace(normalizedExpectedLineKey) ||
			string.IsNullOrWhiteSpace(normalizedCandidateLineKey))
		{
			return false;
		}

		if (string.Equals(normalizedExpectedLineKey, normalizedCandidateLineKey, StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		if (!TryParseTransitLineIdentity(normalizedExpectedLineKey, out TransitAnnouncementServiceType expectedService, out string expectedStableId) ||
			!TryParseTransitLineIdentity(normalizedCandidateLineKey, out TransitAnnouncementServiceType candidateService, out string candidateStableId) ||
			expectedService != candidateService)
		{
			return false;
		}

		if (TryExtractRouteNumberFromStableId(expectedStableId, out int expectedRouteNumber) &&
			TryExtractRouteNumberFromStableId(candidateStableId, out int candidateRouteNumber) &&
			expectedRouteNumber > 0 &&
			expectedRouteNumber == candidateRouteNumber)
		{
			return true;
		}

		string expectedDisplay = AudioReplacementDomainConfig.NormalizeTransitDisplayText(GetTransitLineDisplayName(normalizedExpectedLineKey));
		string candidateDisplay = AudioReplacementDomainConfig.NormalizeTransitDisplayText(GetTransitLineDisplayName(normalizedCandidateLineKey));
		return !string.IsNullOrWhiteSpace(expectedDisplay) &&
			string.Equals(expectedDisplay, candidateDisplay, StringComparison.OrdinalIgnoreCase);
	}

	private static bool DoesStationKeyMatchFallback(string expectedStationKey, string candidateStationKey)
	{
		string normalizedExpectedStationKey = NormalizeTransitStationIdentity(expectedStationKey);
		string normalizedCandidateStationKey = NormalizeTransitStationIdentity(candidateStationKey);
		if (string.IsNullOrWhiteSpace(normalizedExpectedStationKey) ||
			string.IsNullOrWhiteSpace(normalizedCandidateStationKey))
		{
			return false;
		}

		if (string.Equals(normalizedExpectedStationKey, normalizedCandidateStationKey, StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		string expectedStableId = GetTransitStationStableId(normalizedExpectedStationKey);
		string candidateStableId = GetTransitStationStableId(normalizedCandidateStationKey);
		if (TryExtractStationGridFromStableId(expectedStableId, out int expectedGridX, out int expectedGridZ) &&
			TryExtractStationGridFromStableId(candidateStableId, out int candidateGridX, out int candidateGridZ) &&
			expectedGridX == candidateGridX &&
			expectedGridZ == candidateGridZ)
		{
			return true;
		}

		if (TryExtractStationEntityFromStableId(expectedStableId, out int expectedEntityIndex, out int expectedEntityVersion) &&
			TryExtractStationEntityFromStableId(candidateStableId, out int candidateEntityIndex, out int candidateEntityVersion) &&
			expectedEntityIndex == candidateEntityIndex &&
			expectedEntityVersion == candidateEntityVersion)
		{
			return true;
		}

		string expectedStableName = ExtractStationNameFromStableId(expectedStableId);
		string candidateStableName = ExtractStationNameFromStableId(candidateStableId);
		if (!string.IsNullOrWhiteSpace(expectedStableName) &&
			string.Equals(expectedStableName, candidateStableName, StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		string expectedDisplay = AudioReplacementDomainConfig.NormalizeTransitDisplayText(GetTransitStationDisplayName(normalizedExpectedStationKey));
		string candidateDisplay = AudioReplacementDomainConfig.NormalizeTransitDisplayText(GetTransitStationDisplayName(normalizedCandidateStationKey));
		return !string.IsNullOrWhiteSpace(expectedDisplay) &&
			string.Equals(expectedDisplay, candidateDisplay, StringComparison.OrdinalIgnoreCase);
	}

	// Fallback for line identities by matching slot/service plus route number.
	private static bool TryGetTransitAnnouncementSelectionByRouteNumberFallback(
		string slotKey,
		TransitAnnouncementServiceType serviceType,
		string stableId,
		out string selection)
	{
		selection = string.Empty;
		if (!TryExtractRouteNumberFromStableId(stableId, out int routeNumber) || routeNumber <= 0)
		{
			return false;
		}

		foreach (KeyValuePair<string, string> pair in TransitAnnouncementConfig.TransitAnnouncementLineSelections)
		{
			if (!TryParseTransitLineOverrideKey(pair.Key, out string candidateSlotKey, out string candidateLineKey) ||
				!string.Equals(candidateSlotKey, slotKey, StringComparison.OrdinalIgnoreCase) ||
				!TryParseTransitLineIdentity(candidateLineKey, out TransitAnnouncementServiceType candidateService, out string candidateStableId) ||
				candidateService != serviceType ||
				!TryExtractRouteNumberFromStableId(candidateStableId, out int candidateRouteNumber) ||
				candidateRouteNumber != routeNumber)
			{
				continue;
			}

			selection = pair.Value;
			return true;
		}

		return false;
	}

	// Fallback for non-numbered lines by matching slot+service+display name.
	private static bool TryGetTransitAnnouncementSelectionByDisplayNameFallback(
		string slotKey,
		TransitAnnouncementServiceType serviceType,
		string normalizedLineKey,
		out string selection)
	{
		selection = string.Empty;
		string displayName = AudioReplacementDomainConfig.NormalizeTransitDisplayText(GetTransitLineDisplayName(normalizedLineKey));
		if (string.IsNullOrWhiteSpace(displayName))
		{
			return false;
		}

		foreach (KeyValuePair<string, string> pair in TransitAnnouncementConfig.TransitAnnouncementLineSelections)
		{
			if (!TryParseTransitLineOverrideKey(pair.Key, out string candidateSlotKey, out string candidateLineKey) ||
				!string.Equals(candidateSlotKey, slotKey, StringComparison.OrdinalIgnoreCase) ||
				!TryParseTransitLineIdentity(candidateLineKey, out TransitAnnouncementServiceType candidateService, out _) ||
				candidateService != serviceType)
			{
				continue;
			}

			string candidateDisplayName = AudioReplacementDomainConfig.NormalizeTransitDisplayText(GetTransitLineDisplayName(candidateLineKey));
			if (!string.IsNullOrWhiteSpace(candidateDisplayName) &&
				string.Equals(candidateDisplayName, displayName, StringComparison.OrdinalIgnoreCase))
			{
				selection = pair.Value;
				return true;
			}
		}

		return false;
	}

	private static IEnumerable<string> GetTransitLineIdentityFallbackKeys(
		string normalizedLineKey,
		TransitAnnouncementServiceType serviceType,
		string stableId)
	{
		if (TryParseRouteStableId(stableId, out int routeEntityIndex, out int routeEntityVersion, out int routeNumber))
		{
			if (routeNumber > 0)
			{
				yield return BuildTransitLineIdentity(serviceType, $"number:{routeNumber}");
			}

			yield return BuildTransitLineIdentity(serviceType, $"entity:{routeEntityIndex}:{routeEntityVersion}");
		}

		yield return BuildLabelTransitLineIdentity(serviceType, GetTransitLineDisplayName(normalizedLineKey));
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
				SaveConfig();
				ClearTransitObservationMetadataDirty();
				ConfigVersion++;
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
		SaveConfig();
		ClearTransitObservationMetadataDirty();
		ConfigVersion++;
		OptionsVersion++;
	}

	internal static void SetTransitAnnouncementStationLineSelection(
		TransitAnnouncementSlot slot,
		string stationKey,
		string lineKey,
		string selection)
	{
		string normalizedStationKey = NormalizeTransitStationIdentity(stationKey);
		string normalizedLineKey = NormalizeTransitLineIdentity(lineKey);
		string slotKey = GetTransitAnnouncementLeadTargetKey(slot);
		if (string.IsNullOrWhiteSpace(normalizedStationKey) ||
			string.IsNullOrWhiteSpace(normalizedLineKey) ||
			string.IsNullOrWhiteSpace(slotKey))
		{
			return;
		}

		string overrideKey = BuildTransitStationLineOverrideKey(slotKey, normalizedStationKey, normalizedLineKey);
		if (string.IsNullOrWhiteSpace(overrideKey))
		{
			return;
		}

		string normalizedSelection = AudioReplacementDomainConfig.NormalizeProfileKey(selection);
		if (AudioReplacementDomainConfig.IsDefaultSelection(normalizedSelection) || string.IsNullOrWhiteSpace(normalizedSelection))
		{
			if (TransitAnnouncementConfig.TransitAnnouncementStationLineSelections.Remove(overrideKey))
			{
				SaveConfig();
				ClearTransitObservationMetadataDirty();
				ConfigVersion++;
				OptionsVersion++;
			}
			return;
		}

		if (TransitAnnouncementConfig.TransitAnnouncementStationLineSelections.TryGetValue(overrideKey, out string existing) &&
			string.Equals(existing, normalizedSelection, StringComparison.Ordinal))
		{
			return;
		}

		TransitAnnouncementConfig.TransitAnnouncementStationLineSelections[overrideKey] = normalizedSelection;
		TrackTransitStationLineForOptions(normalizedStationKey, string.Empty, normalizedLineKey, string.Empty);
		SaveConfig();
		ClearTransitObservationMetadataDirty();
		ConfigVersion++;
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
			s_TransitAnnouncementStationDropdownCacheVersion = -1;
			s_TransitAnnouncementStationDropdown = Array.Empty<DropdownItem<string>>();
			s_TransitAnnouncementLineDropdownCacheVersion = -1;
			s_TransitAnnouncementLineDropdown = Array.Empty<DropdownItem<string>>();
		}

		return changed;
	}

	private static bool TrackTransitStationForOptions(string stationKey, string displayName = "")
	{
		string normalizedStationKey = NormalizeTransitStationIdentity(stationKey);
		if (string.IsNullOrWhiteSpace(normalizedStationKey))
		{
			return false;
		}

		bool changed = false;
		List<string> knownStations = TransitAnnouncementConfig.TransitAnnouncementKnownStations ?? new List<string>();
		if (!knownStations.Any(existing => string.Equals(existing, normalizedStationKey, StringComparison.OrdinalIgnoreCase)))
		{
			knownStations.Add(normalizedStationKey);
			knownStations.Sort(StringComparer.OrdinalIgnoreCase);
			TransitAnnouncementConfig.TransitAnnouncementKnownStations = knownStations;
			changed = true;
		}

		if (string.IsNullOrWhiteSpace(TransitAnnouncementConfig.TransitAnnouncementSelectedStation))
		{
			TransitAnnouncementConfig.TransitAnnouncementSelectedStation = normalizedStationKey;
			changed = true;
		}

		string normalizedDisplayName = AudioReplacementDomainConfig.NormalizeTransitDisplayText(displayName);
		if (!string.IsNullOrWhiteSpace(normalizedDisplayName))
		{
			Dictionary<string, string> displayMap = TransitAnnouncementConfig.TransitAnnouncementStationDisplayByKey ??
				new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			if (!displayMap.TryGetValue(normalizedStationKey, out string existingDisplay) ||
				!string.Equals(existingDisplay, normalizedDisplayName, StringComparison.Ordinal))
			{
				displayMap[normalizedStationKey] = normalizedDisplayName;
				TransitAnnouncementConfig.TransitAnnouncementStationDisplayByKey = displayMap;
				changed = true;
			}
		}

		s_TransitAnnouncementStationDropdownCacheVersion = -1;
		s_TransitAnnouncementStationDropdown = Array.Empty<DropdownItem<string>>();
		return changed;
	}

	private static bool TrackTransitStationLineForOptions(
		string stationKey,
		string stationDisplayName,
		string lineKey,
		string lineDisplayName)
	{
		string normalizedStationKey = NormalizeTransitStationIdentity(stationKey);
		string normalizedLineKey = NormalizeTransitLineIdentity(lineKey);
		if (string.IsNullOrWhiteSpace(normalizedStationKey) || string.IsNullOrWhiteSpace(normalizedLineKey))
		{
			return false;
		}

		bool changed = false;
		changed |= TrackTransitStationForOptions(normalizedStationKey, stationDisplayName);
		changed |= TrackTransitLineForOptions(normalizedLineKey, lineDisplayName);

		string stationLineKey = BuildTransitStationLineIdentity(normalizedStationKey, normalizedLineKey);
		if (!string.IsNullOrWhiteSpace(stationLineKey))
		{
			List<string> knownStationLines = TransitAnnouncementConfig.TransitAnnouncementKnownStationLines ?? new List<string>();
			if (!knownStationLines.Any(existing => string.Equals(existing, stationLineKey, StringComparison.OrdinalIgnoreCase)))
			{
				knownStationLines.Add(stationLineKey);
				knownStationLines.Sort(StringComparer.OrdinalIgnoreCase);
				TransitAnnouncementConfig.TransitAnnouncementKnownStationLines = knownStationLines;
				changed = true;
			}
		}

		if (string.IsNullOrWhiteSpace(TransitAnnouncementConfig.TransitAnnouncementSelectedStation))
		{
			TransitAnnouncementConfig.TransitAnnouncementSelectedStation = normalizedStationKey;
			changed = true;
		}

		if (string.IsNullOrWhiteSpace(TransitAnnouncementConfig.TransitAnnouncementSelectedLine))
		{
			TransitAnnouncementConfig.TransitAnnouncementSelectedLine = normalizedLineKey;
			changed = true;
		}

		if (TryParseTransitLineIdentity(normalizedLineKey, out TransitAnnouncementServiceType serviceType, out _) &&
			serviceType == s_TransitAnnouncementLineEditorService)
		{
			s_TransitAnnouncementStationDropdownCacheVersion = -1;
			s_TransitAnnouncementStationDropdown = Array.Empty<DropdownItem<string>>();
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

		foreach (string overrideKey in TransitAnnouncementConfig.TransitAnnouncementStationLineSelections.Keys)
		{
			if (TryParseTransitStationLineOverrideKey(overrideKey, out _, out _, out string lineKey))
			{
				lineKeys.Add(lineKey);
			}
		}

		return lineKeys;
	}

	private static HashSet<string> GetTransitStationKeysReferencedByOverrides()
	{
		HashSet<string> stationKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (string overrideKey in TransitAnnouncementConfig.TransitAnnouncementStationLineSelections.Keys)
		{
			if (TryParseTransitStationLineOverrideKey(overrideKey, out _, out string stationKey, out _))
			{
				stationKeys.Add(stationKey);
			}
		}

		return stationKeys;
	}

	private static HashSet<string> GetTransitStationLineKeysReferencedByOverrides()
	{
		HashSet<string> stationLineKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (string overrideKey in TransitAnnouncementConfig.TransitAnnouncementStationLineSelections.Keys)
		{
			if (TryParseTransitStationLineOverrideKey(overrideKey, out _, out string stationKey, out string lineKey))
			{
				string normalizedPair = BuildTransitStationLineIdentity(stationKey, lineKey);
				if (!string.IsNullOrWhiteSpace(normalizedPair))
				{
					stationLineKeys.Add(normalizedPair);
				}
			}
		}

		return stationLineKeys;
	}

	private static bool IsLineForService(string lineKey, TransitAnnouncementServiceType serviceType)
	{
		return TryParseTransitLineIdentity(lineKey, out TransitAnnouncementServiceType parsedService, out _) &&
			parsedService == serviceType;
	}

	private static bool IsStationAvailableForService(string stationKey, TransitAnnouncementServiceType serviceType)
	{
		string normalizedStationKey = NormalizeTransitStationIdentity(stationKey);
		if (string.IsNullOrWhiteSpace(normalizedStationKey))
		{
			return false;
		}

		List<string> stationLines = TransitAnnouncementConfig.TransitAnnouncementKnownStationLines ?? new List<string>();
		for (int i = 0; i < stationLines.Count; i++)
		{
			if (!TryParseTransitStationLineIdentity(stationLines[i], out string candidateStation, out string candidateLine) ||
				!string.Equals(candidateStation, normalizedStationKey, StringComparison.OrdinalIgnoreCase) ||
				!IsLineForService(candidateLine, serviceType))
			{
				continue;
			}

			return true;
		}

		return false;
	}

	private static bool IsStationLineForService(
		string stationKey,
		string lineKey,
		TransitAnnouncementServiceType serviceType)
	{
		string normalizedStationKey = NormalizeTransitStationIdentity(stationKey);
		string normalizedLineKey = NormalizeTransitLineIdentity(lineKey);
		if (string.IsNullOrWhiteSpace(normalizedStationKey) ||
			string.IsNullOrWhiteSpace(normalizedLineKey) ||
			!IsLineForService(normalizedLineKey, serviceType))
		{
			return false;
		}

		List<string> stationLines = TransitAnnouncementConfig.TransitAnnouncementKnownStationLines ?? new List<string>();
		for (int i = 0; i < stationLines.Count; i++)
		{
			if (TryParseTransitStationLineIdentity(stationLines[i], out string candidateStation, out string candidateLine) &&
				string.Equals(candidateStation, normalizedStationKey, StringComparison.OrdinalIgnoreCase) &&
				string.Equals(candidateLine, normalizedLineKey, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}

		return false;
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

	private static string GetFirstTransitStationForService(TransitAnnouncementServiceType serviceType)
	{
		List<string> stations = (TransitAnnouncementConfig.TransitAnnouncementKnownStations ?? new List<string>())
			.Where(static station => !string.IsNullOrWhiteSpace(station))
			.OrderBy(station => GetTransitStationDisplayName(station), StringComparer.OrdinalIgnoreCase)
			.ThenBy(static station => station, StringComparer.OrdinalIgnoreCase)
			.ToList();
		for (int i = 0; i < stations.Count; i++)
		{
			if (IsStationAvailableForService(stations[i], serviceType))
			{
				return stations[i];
			}
		}

		return string.Empty;
	}

	private static string GetFirstTransitLineForStationService(string stationKey, TransitAnnouncementServiceType serviceType)
	{
		string normalizedStationKey = NormalizeTransitStationIdentity(stationKey);
		if (string.IsNullOrWhiteSpace(normalizedStationKey))
		{
			return string.Empty;
		}

		List<string> lines = new List<string>();
		List<string> stationLines = TransitAnnouncementConfig.TransitAnnouncementKnownStationLines ?? new List<string>();
		for (int i = 0; i < stationLines.Count; i++)
		{
			if (!TryParseTransitStationLineIdentity(stationLines[i], out string candidateStation, out string candidateLine) ||
				!string.Equals(candidateStation, normalizedStationKey, StringComparison.OrdinalIgnoreCase) ||
				!IsLineForService(candidateLine, serviceType) ||
				lines.Any(existing => string.Equals(existing, candidateLine, StringComparison.OrdinalIgnoreCase)))
			{
				continue;
			}

			lines.Add(candidateLine);
		}

		lines.Sort((left, right) =>
		{
			int displayCompare = StringComparer.OrdinalIgnoreCase.Compare(GetTransitLineDisplayName(left), GetTransitLineDisplayName(right));
			return displayCompare != 0
				? displayCompare
				: StringComparer.OrdinalIgnoreCase.Compare(left, right);
		});
		return lines.Count > 0 ? lines[0] : string.Empty;
	}

	private static string GetFirstTransitStationForLineService(string lineKey, TransitAnnouncementServiceType serviceType)
	{
		string normalizedLineKey = NormalizeTransitLineIdentity(lineKey);
		if (string.IsNullOrWhiteSpace(normalizedLineKey) || !IsLineForService(normalizedLineKey, serviceType))
		{
			return string.Empty;
		}

		List<string> stations = new List<string>();
		List<string> stationLines = TransitAnnouncementConfig.TransitAnnouncementKnownStationLines ?? new List<string>();
		for (int i = 0; i < stationLines.Count; i++)
		{
			if (!TryParseTransitStationLineIdentity(stationLines[i], out string candidateStation, out string candidateLine) ||
				!string.Equals(candidateLine, normalizedLineKey, StringComparison.OrdinalIgnoreCase) ||
				stations.Any(existing => string.Equals(existing, candidateStation, StringComparison.OrdinalIgnoreCase)))
			{
				continue;
			}

			stations.Add(candidateStation);
		}

		stations.Sort((left, right) =>
		{
			int displayCompare = StringComparer.OrdinalIgnoreCase.Compare(GetTransitStationDisplayName(left), GetTransitStationDisplayName(right));
			return displayCompare != 0
				? displayCompare
				: StringComparer.OrdinalIgnoreCase.Compare(left, right);
		});
		return stations.Count > 0 ? stations[0] : string.Empty;
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

		if (TryParseRouteStableId(normalized, out int routeEntityIndex, out int routeEntityVersion, out int routeNumber))
		{
			return $"route:{routeEntityIndex}:{routeEntityVersion}:{Math.Max(0, routeNumber)}";
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

	private static string NormalizeTransitStationStableId(string stationStableId)
	{
		string normalized = AudioReplacementDomainConfig.NormalizeTransitLineKey(stationStableId);
		if (string.IsNullOrWhiteSpace(normalized))
		{
			return string.Empty;
		}

		if (normalized.StartsWith("name:", StringComparison.OrdinalIgnoreCase))
		{
			string raw = normalized.Substring("name:".Length).Trim();
			int coordinateSplit = raw.LastIndexOf('@');
			if (coordinateSplit > 0 && coordinateSplit < raw.Length - 1)
			{
				string namePart = AudioReplacementDomainConfig.NormalizeTransitDisplayText(raw.Substring(0, coordinateSplit));
				string coordinatePart = raw.Substring(coordinateSplit + 1);
				if (TryParseTransitStationGridCoordinate(coordinatePart, out int x, out int z))
				{
					return string.IsNullOrWhiteSpace(namePart)
						? $"pos:{x}:{z}"
						: $"name:{namePart}@{x}:{z}";
				}
			}

			string name = AudioReplacementDomainConfig.NormalizeTransitDisplayText(raw);
			return string.IsNullOrWhiteSpace(name)
				? string.Empty
				: $"name:{name}";
		}

		if (normalized.StartsWith("pos:", StringComparison.OrdinalIgnoreCase))
		{
			string raw = normalized.Substring("pos:".Length).Trim();
			return TryParseTransitStationGridCoordinate(raw, out int x, out int z)
				? $"pos:{x}:{z}"
				: string.Empty;
		}

		if (normalized.StartsWith("entity:", StringComparison.OrdinalIgnoreCase))
		{
			string raw = normalized.Substring("entity:".Length).Trim();
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

		string fallbackName = AudioReplacementDomainConfig.NormalizeTransitDisplayText(normalized);
		return string.IsNullOrWhiteSpace(fallbackName)
			? string.Empty
			: $"name:{fallbackName}";
	}

	private static bool TryParseTransitStationGridCoordinate(string raw, out int x, out int z)
	{
		x = 0;
		z = 0;
		string[] parts = (raw ?? string.Empty).Split(':');
		return parts.Length == 2 &&
			int.TryParse(parts[0], out x) &&
			int.TryParse(parts[1], out z);
	}

	private static bool TryExtractStationGridFromStableId(string stableId, out int x, out int z)
	{
		x = 0;
		z = 0;
		string normalizedStableId = NormalizeTransitStationStableId(stableId);
		if (string.IsNullOrWhiteSpace(normalizedStableId))
		{
			return false;
		}

		if (normalizedStableId.StartsWith("pos:", StringComparison.OrdinalIgnoreCase))
		{
			string raw = normalizedStableId.Substring("pos:".Length);
			return TryParseTransitStationGridCoordinate(raw, out x, out z);
		}

		if (!normalizedStableId.StartsWith("name:", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		string rawName = normalizedStableId.Substring("name:".Length);
		int coordinateSplit = rawName.LastIndexOf('@');
		return coordinateSplit > 0 &&
			coordinateSplit < rawName.Length - 1 &&
			TryParseTransitStationGridCoordinate(rawName.Substring(coordinateSplit + 1), out x, out z);
	}

	private static bool TryExtractStationEntityFromStableId(string stableId, out int entityIndex, out int entityVersion)
	{
		entityIndex = 0;
		entityVersion = 0;
		string normalizedStableId = NormalizeTransitStationStableId(stableId);
		if (!normalizedStableId.StartsWith("entity:", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		string[] parts = normalizedStableId.Substring("entity:".Length).Split(':');
		return parts.Length == 2 &&
			int.TryParse(parts[0], out entityIndex) &&
			int.TryParse(parts[1], out entityVersion) &&
			entityIndex >= 0 &&
			entityVersion >= 0;
	}

	private static string ExtractStationNameFromStableId(string stableId)
	{
		string normalizedStableId = NormalizeTransitStationStableId(stableId);
		if (!normalizedStableId.StartsWith("name:", StringComparison.OrdinalIgnoreCase))
		{
			return string.Empty;
		}

		string raw = normalizedStableId.Substring("name:".Length);
		int coordinateSplit = raw.LastIndexOf('@');
		string name = coordinateSplit > 0
			? raw.Substring(0, coordinateSplit)
			: raw;
		return AudioReplacementDomainConfig.NormalizeTransitDisplayText(name);
	}

	private static bool TryExtractRouteNumberFromStableId(string stableId, out int routeNumber)
	{
		routeNumber = 0;
		string normalizedStableId = NormalizeTransitLineStableId(stableId);
		if (string.IsNullOrWhiteSpace(normalizedStableId))
		{
			return false;
		}

		if (normalizedStableId.StartsWith("number:", StringComparison.OrdinalIgnoreCase))
		{
			string numberText = normalizedStableId.Substring("number:".Length).Trim();
			return int.TryParse(numberText, out routeNumber) && routeNumber > 0;
		}

		if (TryParseRouteStableId(normalizedStableId, out _, out _, out int parsedRouteNumber) && parsedRouteNumber > 0)
		{
			routeNumber = parsedRouteNumber;
			return true;
		}

		return false;
	}

	private static bool TryParseRouteStableId(
		string stableId,
		out int routeEntityIndex,
		out int routeEntityVersion,
		out int routeNumber)
	{
		routeEntityIndex = 0;
		routeEntityVersion = 0;
		routeNumber = 0;
		string normalizedStableId = AudioReplacementDomainConfig.NormalizeTransitLineKey(stableId);
		if (!normalizedStableId.StartsWith("route:", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		string[] parts = normalizedStableId.Substring("route:".Length).Split(':');
		if (parts.Length != 3 ||
			!int.TryParse(parts[0], out routeEntityIndex) ||
			!int.TryParse(parts[1], out routeEntityVersion) ||
			!int.TryParse(parts[2], out routeNumber) ||
			routeEntityIndex < 0 ||
			routeEntityVersion < 0 ||
			routeNumber < 0)
		{
			routeEntityIndex = 0;
			routeEntityVersion = 0;
			routeNumber = 0;
			return false;
		}

		return true;
	}

	private static string FormatTransitLineStableIdForDisplay(string stableId)
	{
		string normalizedStableId = NormalizeTransitLineStableId(stableId);
		if (string.IsNullOrWhiteSpace(normalizedStableId))
		{
			return string.Empty;
		}

		if (TryParseRouteStableId(normalizedStableId, out int routeEntityIndex, out _, out int routeNumber) &&
			routeEntityIndex >= 0)
		{
			return routeNumber > 0
				? $"Line {routeNumber}"
				: $"Route {routeEntityIndex}";
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

	private static string FormatTransitStationStableIdForDisplay(string stableId)
	{
		string normalizedStableId = NormalizeTransitStationStableId(stableId);
		if (string.IsNullOrWhiteSpace(normalizedStableId))
		{
			return string.Empty;
		}

		if (normalizedStableId.StartsWith("name:", StringComparison.OrdinalIgnoreCase))
		{
			string raw = normalizedStableId.Substring("name:".Length);
			int coordinateSplit = raw.LastIndexOf('@');
			if (coordinateSplit > 0 && coordinateSplit < raw.Length - 1)
			{
				string namePart = raw.Substring(0, coordinateSplit);
				string coordinatePart = raw.Substring(coordinateSplit + 1);
				if (TryParseTransitStationGridCoordinate(coordinatePart, out int x, out int z))
				{
					return $"{namePart} ({x}, {z})";
				}
			}

			return raw;
		}

		if (normalizedStableId.StartsWith("pos:", StringComparison.OrdinalIgnoreCase))
		{
			string raw = normalizedStableId.Substring("pos:".Length);
			if (TryParseTransitStationGridCoordinate(raw, out int x, out int z))
			{
				return $"Station ({x}, {z})";
			}
		}

		if (normalizedStableId.StartsWith("entity:", StringComparison.OrdinalIgnoreCase))
		{
			string[] parts = normalizedStableId.Substring("entity:".Length).Split(':');
			return parts.Length > 0
				? $"Stop {parts[0]}"
				: "Stop";
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
			case TransitAnnouncementServiceType.Ferry:
				return isArrival ? TransitAnnouncementSlot.FerryArrival : TransitAnnouncementSlot.FerryDeparture;
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

		if (string.Equals(normalized, s_TransitAnnouncementServiceVoiceKeys[4], StringComparison.OrdinalIgnoreCase))
		{
			serviceType = TransitAnnouncementServiceType.Ferry;
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

	private static string BuildTransitStationLineOverrideKey(string slotKey, string stationKey, string lineKey)
	{
		string normalizedSlotKey = AudioReplacementDomainConfig.NormalizeTargetKey(slotKey);
		string normalizedStationKey = NormalizeTransitStationIdentity(stationKey);
		string normalizedLineKey = NormalizeTransitLineIdentity(lineKey);
		if (string.IsNullOrWhiteSpace(normalizedSlotKey) ||
			string.IsNullOrWhiteSpace(normalizedStationKey) ||
			string.IsNullOrWhiteSpace(normalizedLineKey))
		{
			return string.Empty;
		}

		return $"{normalizedSlotKey}\n{normalizedStationKey}\n{normalizedLineKey}";
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

	internal static bool TryParseTransitStationLineOverrideKey(
		string overrideKey,
		out string slotKey,
		out string stationKey,
		out string lineKey)
	{
		slotKey = string.Empty;
		stationKey = string.Empty;
		lineKey = string.Empty;
		string normalized = AudioReplacementDomainConfig.NormalizeTransitLineKey(overrideKey);
		if (string.IsNullOrWhiteSpace(normalized))
		{
			return false;
		}

		string[] parts = normalized.Split('\n');
		if (parts.Length != 3)
		{
			return false;
		}

		slotKey = AudioReplacementDomainConfig.NormalizeTargetKey(parts[0]);
		stationKey = NormalizeTransitStationIdentity(parts[1]);
		lineKey = NormalizeTransitLineIdentity(parts[2]);
		return !string.IsNullOrWhiteSpace(slotKey) &&
			!string.IsNullOrWhiteSpace(stationKey) &&
			!string.IsNullOrWhiteSpace(lineKey);
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

		EnsureTransitAnnouncementStationDropdownCacheCurrent();
		string selectedStation = GetTransitAnnouncementSelectedStationForOptions();
		HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		List<string> available = new List<string>();
		List<string> stationLines = TransitAnnouncementConfig.TransitAnnouncementKnownStationLines ?? new List<string>();
		for (int i = 0; i < stationLines.Count; i++)
		{
			if (!TryParseTransitStationLineIdentity(stationLines[i], out string stationKey, out string lineKey) ||
				!string.Equals(stationKey, selectedStation, StringComparison.OrdinalIgnoreCase) ||
				!IsLineForService(lineKey, s_TransitAnnouncementLineEditorService) ||
				!seen.Add(lineKey))
			{
				continue;
			}

			available.Add(lineKey);
		}

		available = available
			.OrderBy(line => GetTransitLineDisplayName(line), StringComparer.OrdinalIgnoreCase)
			.ThenBy(static line => line, StringComparer.OrdinalIgnoreCase)
			.ToList();

		if (available.Count == 0)
		{
			string stationDisplayName = GetTransitStationDisplayName(selectedStation);
			if (string.IsNullOrWhiteSpace(stationDisplayName))
			{
				stationDisplayName = "selected station";
			}

			s_TransitAnnouncementLineDropdown = new[]
			{
				new DropdownItem<string>
				{
					value = string.Empty,
					displayName = $"No {GetTransitAnnouncementServiceLabel(s_TransitAnnouncementLineEditorService).ToLowerInvariant()} lines detected for {stationDisplayName}",
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

	private static void EnsureTransitAnnouncementStationDropdownCacheCurrent()
	{
		if (s_TransitAnnouncementStationDropdownCacheVersion == OptionsVersion &&
			s_TransitAnnouncementStationDropdown.Length > 0)
		{
			return;
		}

		List<string> availableStations = (TransitAnnouncementConfig.TransitAnnouncementKnownStations ?? new List<string>())
			.Where(static station => !string.IsNullOrWhiteSpace(station))
			.Where(station => IsStationAvailableForService(station, s_TransitAnnouncementLineEditorService))
			.OrderBy(station => GetTransitStationDisplayName(station), StringComparer.OrdinalIgnoreCase)
			.ThenBy(static station => station, StringComparer.OrdinalIgnoreCase)
			.ToList();

		if (availableStations.Count == 0)
		{
			s_TransitAnnouncementStationDropdown = new[]
			{
				new DropdownItem<string>
				{
					value = string.Empty,
					displayName = $"No {GetTransitAnnouncementServiceLabel(s_TransitAnnouncementLineEditorService).ToLowerInvariant()} stations detected",
					disabled = true
				}
			};
			s_TransitAnnouncementStationDropdownCacheVersion = OptionsVersion;
			return;
		}

		string currentSelected = GetTransitAnnouncementSelectedStationForOptions();
		if (!availableStations.Any(station => string.Equals(station, currentSelected, StringComparison.OrdinalIgnoreCase)))
		{
			TransitAnnouncementConfig.TransitAnnouncementSelectedStation = availableStations[0];
		}

		List<DropdownItem<string>> options = new List<DropdownItem<string>>(availableStations.Count);
		for (int i = 0; i < availableStations.Count; i++)
		{
			string stationKey = availableStations[i];
			string displayName = GetTransitStationDisplayName(stationKey);
			options.Add(new DropdownItem<string>
			{
				value = stationKey,
				displayName = string.IsNullOrWhiteSpace(displayName) ? stationKey : displayName
			});
		}

		s_TransitAnnouncementStationDropdown = options.ToArray();
		s_TransitAnnouncementStationDropdownCacheVersion = OptionsVersion;
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
				displayName = "None (No announcement)"
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

	// Map a fixed slot enum to its stable per-line override slot key.
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
			case TransitAnnouncementSlot.FerryArrival:
				return s_TransitAnnouncementLeadTargetKeys[8];
			case TransitAnnouncementSlot.FerryDeparture:
				return s_TransitAnnouncementLeadTargetKeys[9];
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
			case TransitAnnouncementServiceType.Ferry:
				return s_TransitAnnouncementServiceVoiceKeys[4];
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
