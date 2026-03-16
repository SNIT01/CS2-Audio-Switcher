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

// Per-mode voice selection scope used by transit TTS configuration.
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

// One playback segment in the announcement sequence (custom clip or synthesized speech).
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

	private static int s_TransitAnnouncementVoiceDropdownCacheVersion = -1;

	private static DropdownItem<string>[] s_TransitAnnouncementVoiceDropdown = Array.Empty<DropdownItem<string>>();

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

	// Build dropdown items for transit-announcement slot selectors.
	internal static DropdownItem<string>[] BuildTransitAnnouncementDropdownItems()
	{
		EnsureTransitAnnouncementDropdownCacheCurrent();
		return s_TransitAnnouncementDropdownWithDefault;
	}

	// Build dropdown items for transit TTS voice selectors.
	internal static DropdownItem<string>[] BuildTransitAnnouncementVoiceDropdownItems()
	{
		EnsureTransitAnnouncementVoiceDropdownCacheCurrent();
		return s_TransitAnnouncementVoiceDropdown;
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

	internal static string GetTransitAnnouncementCustomText(TransitAnnouncementSlot slot)
	{
		string key = GetTransitAnnouncementLeadTargetKey(slot);
		if (string.IsNullOrWhiteSpace(key) ||
			!TransitAnnouncementConfig.TransitAnnouncementCustomTextByTarget.TryGetValue(key, out string value))
		{
			return string.Empty;
		}

		// Preserve live text-box editing state (including in-progress spaces) in options UI.
		return value ?? string.Empty;
	}

	internal static void SetTransitAnnouncementCustomText(TransitAnnouncementSlot slot, string text)
	{
		string key = GetTransitAnnouncementLeadTargetKey(slot);
		if (string.IsNullOrWhiteSpace(key))
		{
			return;
		}

		// Do not normalize on each keystroke; that strips trailing spaces and prevents typing words.
		string raw = text ?? string.Empty;
		if (string.IsNullOrWhiteSpace(raw))
		{
			TransitAnnouncementConfig.TransitAnnouncementCustomTextByTarget.Remove(key);
			return;
		}

		TransitAnnouncementConfig.TransitAnnouncementCustomTextByTarget[key] = raw;
	}

	internal static string GetTransitAnnouncementServiceVoice(TransitAnnouncementServiceType serviceType)
	{
		string key = GetTransitAnnouncementServiceVoiceKey(serviceType);
		if (string.IsNullOrWhiteSpace(key) ||
			!TransitAnnouncementConfig.TransitAnnouncementVoiceByService.TryGetValue(key, out string voice))
		{
			return string.Empty;
		}

		return TransitAnnouncementTtsService.NormalizeVoiceSelection(voice);
	}

	internal static void SetTransitAnnouncementServiceVoice(TransitAnnouncementServiceType serviceType, string voiceName)
	{
		string key = GetTransitAnnouncementServiceVoiceKey(serviceType);
		if (string.IsNullOrWhiteSpace(key))
		{
			return;
		}

		string normalized = TransitAnnouncementTtsService.NormalizeVoiceSelection(voiceName);
		if (string.IsNullOrWhiteSpace(normalized))
		{
			TransitAnnouncementConfig.TransitAnnouncementVoiceByService.Remove(key);
			return;
		}

		TransitAnnouncementConfig.TransitAnnouncementVoiceByService[key] = normalized;
	}
	// Build the full step sequence for one transit event.
	internal static TransitAnnouncementLoadStatus TryBuildTransitAnnouncementSequence(
		TransitAnnouncementSlot slot,
		TransitAnnouncementServiceType serviceType,
		string stopOrServiceText,
		out List<TransitAnnouncementPlaybackSegment> segments,
		out string message)
	{
		segments = new List<TransitAnnouncementPlaybackSegment>(4);
		message = string.Empty;
		if (!TransitAnnouncementConfig.Enabled)
		{
			message = "Transit announcements are disabled.";
			return TransitAnnouncementLoadStatus.NotConfigured;
		}

		bool hasPendingStep = false;
		string pendingMessage = string.Empty;

		TransitAnnouncementLoadStatus leadStatus = TryAppendConfiguredAudioStep(
			GetTransitAnnouncementLeadSelection(slot),
			"Lead-in audio",
			segments,
			out string leadMessage);
		if (leadStatus == TransitAnnouncementLoadStatus.Failure)
		{
			message = leadMessage;
			return TransitAnnouncementLoadStatus.Failure;
		}

		if (leadStatus == TransitAnnouncementLoadStatus.Pending)
		{
			hasPendingStep = true;
			pendingMessage = leadMessage;
		}

		string requestedVoice = GetTransitAnnouncementServiceVoice(serviceType);
		TransitAnnouncementLoadStatus customTextStatus = TryAppendSpeechStep(
			GetTransitAnnouncementCustomText(slot),
			requestedVoice,
			"Custom TTS",
			segments,
			out string customTtsMessage);
		if (customTextStatus == TransitAnnouncementLoadStatus.Failure)
		{
			message = customTtsMessage;
			return TransitAnnouncementLoadStatus.Failure;
		}
		if (customTextStatus == TransitAnnouncementLoadStatus.Pending)
		{
			hasPendingStep = true;
			pendingMessage = string.IsNullOrWhiteSpace(pendingMessage)
				? customTtsMessage
				: pendingMessage;
		}

		TransitAnnouncementLoadStatus stopOrServiceStatus = TryAppendSpeechStep(
			stopOrServiceText,
			requestedVoice,
			"Stop/service TTS",
			segments,
			out string stopOrServiceMessage);
		if (stopOrServiceStatus == TransitAnnouncementLoadStatus.Failure)
		{
			message = stopOrServiceMessage;
			return TransitAnnouncementLoadStatus.Failure;
		}
		if (stopOrServiceStatus == TransitAnnouncementLoadStatus.Pending)
		{
			hasPendingStep = true;
			pendingMessage = string.IsNullOrWhiteSpace(pendingMessage)
				? stopOrServiceMessage
				: pendingMessage;
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

	private static TransitAnnouncementLoadStatus TryAppendSpeechStep(
		string text,
		string requestedVoice,
		string stepLabel,
		ICollection<TransitAnnouncementPlaybackSegment> segments,
		out string message)
	{
		string normalizedText = TransitAnnouncementTtsService.NormalizeSpeechText(text);
		if (string.IsNullOrWhiteSpace(normalizedText))
		{
			message = $"{stepLabel} not configured.";
			return TransitAnnouncementLoadStatus.NotConfigured;
		}

		TransitAnnouncementTtsSynthesisStatus synthStatus = TransitAnnouncementTtsService.TrySynthesizeClip(
			normalizedText,
			requestedVoice,
			out AudioClip ttsClip,
			out string synthMessage);
		switch (synthStatus)
		{
			case TransitAnnouncementTtsSynthesisStatus.Success:
				segments.Add(new TransitAnnouncementPlaybackSegment(ttsClip, SirenSfxProfile.CreateFallback()));
				message = string.IsNullOrWhiteSpace(synthMessage)
					? $"{stepLabel} synthesized."
					: $"{stepLabel} synthesized: {synthMessage}";
				return TransitAnnouncementLoadStatus.Success;
			case TransitAnnouncementTtsSynthesisStatus.Pending:
				message = string.IsNullOrWhiteSpace(synthMessage)
					? $"{stepLabel} is still loading."
					: $"{stepLabel} is still loading: {synthMessage}";
				return TransitAnnouncementLoadStatus.Pending;
			case TransitAnnouncementTtsSynthesisStatus.NotAvailable:
				message = string.IsNullOrWhiteSpace(synthMessage)
					? $"{stepLabel} unavailable."
					: $"{stepLabel} unavailable: {synthMessage}";
				return TransitAnnouncementLoadStatus.NotConfigured;
			default:
				// Keep sequence playback working even when TTS generation fails.
				message = string.IsNullOrWhiteSpace(synthMessage)
					? $"{stepLabel} failed."
					: $"{stepLabel} failed: {synthMessage}";
				return TransitAnnouncementLoadStatus.NotConfigured;
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

	// Keep transit speech dictionaries aligned with known slot/service keys.
	internal static bool NormalizeTransitAnnouncementSpeechSettings()
	{
		bool changed = false;

		HashSet<string> validSpeechTargets = new HashSet<string>(
			s_TransitAnnouncementLeadTargetKeys,
			StringComparer.OrdinalIgnoreCase);
		Dictionary<string, string> normalizedTextByTarget = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (KeyValuePair<string, string> pair in TransitAnnouncementConfig.TransitAnnouncementCustomTextByTarget)
		{
			string key = AudioReplacementDomainConfig.NormalizeTargetKey(pair.Key);
			if (string.IsNullOrWhiteSpace(key) || !validSpeechTargets.Contains(key))
			{
				changed = true;
				continue;
			}

			// Preserve raw options text so spaces are not stripped during live editing.
			string rawText = pair.Value ?? string.Empty;
			if (string.IsNullOrWhiteSpace(rawText))
			{
				changed = true;
				continue;
			}

			normalizedTextByTarget[key] = rawText;
		}

		if (!DictionaryEqualsIgnoreCaseKeysOrdinalValues(
			TransitAnnouncementConfig.TransitAnnouncementCustomTextByTarget,
			normalizedTextByTarget))
		{
			TransitAnnouncementConfig.TransitAnnouncementCustomTextByTarget = normalizedTextByTarget;
			changed = true;
		}

		HashSet<string> validServiceVoiceKeys = new HashSet<string>(
			s_TransitAnnouncementServiceVoiceKeys,
			StringComparer.OrdinalIgnoreCase);
		Dictionary<string, string> normalizedVoices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (KeyValuePair<string, string> pair in TransitAnnouncementConfig.TransitAnnouncementVoiceByService)
		{
			string key = AudioReplacementDomainConfig.NormalizeTargetKey(pair.Key);
			if (string.IsNullOrWhiteSpace(key) || !validServiceVoiceKeys.Contains(key))
			{
				changed = true;
				continue;
			}

			string voice = TransitAnnouncementTtsService.NormalizeVoiceSelection(pair.Value);
			if (string.IsNullOrWhiteSpace(voice))
			{
				changed = true;
				continue;
			}

			normalizedVoices[key] = voice;
		}

		if (!DictionaryEqualsIgnoreCaseKeysOrdinalValues(
			TransitAnnouncementConfig.TransitAnnouncementVoiceByService,
			normalizedVoices))
		{
			TransitAnnouncementConfig.TransitAnnouncementVoiceByService = normalizedVoices;
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

	internal static string GetTransitAnnouncementFallbackSpeech(TransitAnnouncementServiceType serviceType)
	{
		return $"{GetTransitAnnouncementServiceLabel(serviceType)} service";
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

	private static void EnsureTransitAnnouncementVoiceDropdownCacheCurrent()
	{
		if (s_TransitAnnouncementVoiceDropdownCacheVersion == OptionsVersion &&
			s_TransitAnnouncementVoiceDropdown.Length > 0)
		{
			return;
		}

		string[] installedVoices = TransitAnnouncementTtsService.GetInstalledVoiceNames();
		HashSet<string> installedVoiceSet = new HashSet<string>(installedVoices, StringComparer.OrdinalIgnoreCase);
		HashSet<string> configuredVoices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (string raw in TransitAnnouncementConfig.TransitAnnouncementVoiceByService.Values)
		{
			string voice = TransitAnnouncementTtsService.NormalizeVoiceSelection(raw);
			if (!string.IsNullOrWhiteSpace(voice))
			{
				configuredVoices.Add(voice);
			}
		}

		List<DropdownItem<string>> options = new List<DropdownItem<string>>
		{
			new DropdownItem<string>
			{
				value = string.Empty,
				displayName = "Default System Voice"
			}
		};

		for (int i = 0; i < installedVoices.Length; i++)
		{
			options.Add(new DropdownItem<string>
			{
				value = installedVoices[i],
				displayName = installedVoices[i]
			});
		}

		List<string> unavailableConfiguredVoices = configuredVoices
			.Where(voice => !installedVoiceSet.Contains(voice))
			.OrderBy(static voice => voice, StringComparer.OrdinalIgnoreCase)
			.ToList();
		for (int i = 0; i < unavailableConfiguredVoices.Count; i++)
		{
			string voice = unavailableConfiguredVoices[i];
			options.Add(new DropdownItem<string>
			{
				value = voice,
				displayName = $"{voice} (Unavailable)"
			});
		}

		if (installedVoices.Length == 0 && unavailableConfiguredVoices.Count == 0)
		{
			options.Add(new DropdownItem<string>
			{
				value = string.Empty,
				displayName = "No system voices found",
				disabled = true
			});
		}

		s_TransitAnnouncementVoiceDropdown = options.ToArray();
		s_TransitAnnouncementVoiceDropdownCacheVersion = OptionsVersion;
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
