using System;
using Game.Settings;
using Game.UI.Widgets;
using UnityEngine;
using UnityEngine.Scripting;

namespace SirenChanger;

// Public-transport-tab controls for per-mode transit arrival/departure announcements.
public sealed partial class SirenChangerSettings
{
	private const string kTransitAnnouncementButtonGroup = "Transit Announcement Actions";

	[SettingsUISection(kPublicTransportTab, kTransitAnnouncementGroup)]
	[SettingsUIDisplayName(overrideValue: "Enable Transit Station Announcements")]
	[SettingsUIDescription(overrideValue: "Enable custom arrival/departure sounds for train, bus, metro, and tram lines.")]
	public bool TransitAnnouncementsEnabled
	{
		get => SirenChangerMod.TransitAnnouncementConfig.Enabled;
		set => SirenChangerMod.TransitAnnouncementConfig.Enabled = value;
	}

	[SettingsUISection(kPublicTransportTab, kTransitAnnouncementGroup)]
	[SettingsUISlider(min = 0f, max = 100f, step = 1f, unit = "percentageSingleFraction", scalarMultiplier = 100f, updateOnDragEnd = true)]
	[SettingsUIDisplayName(overrideValue: "Announcement Volume")]
	[SettingsUIDescription(overrideValue: "Global volume for all public transport arrival/departure announcements.")]
	public float TransitAnnouncementGlobalVolume
	{
		get => SirenChangerMod.TransitAnnouncementConfig.GlobalAnnouncementVolume;
		set => SirenChangerMod.TransitAnnouncementConfig.GlobalAnnouncementVolume = Mathf.Clamp01(value);
	}

	[SettingsUISection(kPublicTransportTab, kTransitAnnouncementGroup)]
	[SettingsUISlider(min = 0f, max = 250f, step = 0.5f, unit = "floatSingleFraction", updateOnDragEnd = true)]
	[SettingsUIDisplayName(overrideValue: "Announcement Min Distance")]
	[SettingsUIDescription(overrideValue: "Global distance where announcement attenuation begins.")]
	public float TransitAnnouncementGlobalMinDistance
	{
		get => SirenChangerMod.TransitAnnouncementConfig.GlobalAnnouncementMinDistance;
		set
		{
			AudioReplacementDomainConfig config = SirenChangerMod.TransitAnnouncementConfig;
			config.GlobalAnnouncementMinDistance = Mathf.Max(0f, value);
			if (config.GlobalAnnouncementMaxDistance < config.GlobalAnnouncementMinDistance + 0.01f)
			{
				// Keep distance constraints valid while users drag either slider.
				config.GlobalAnnouncementMaxDistance = config.GlobalAnnouncementMinDistance + 0.01f;
			}
		}
	}

	[SettingsUISection(kPublicTransportTab, kTransitAnnouncementGroup)]
	[SettingsUISlider(min = 1f, max = 500f, step = 1f, unit = "floatSingleFraction", updateOnDragEnd = true)]
	[SettingsUIDisplayName(overrideValue: "Announcement Max Distance")]
	[SettingsUIDescription(overrideValue: "Global distance where announcements reach minimum audible level.")]
	public float TransitAnnouncementGlobalMaxDistance
	{
		get => SirenChangerMod.TransitAnnouncementConfig.GlobalAnnouncementMaxDistance;
		set
		{
			AudioReplacementDomainConfig config = SirenChangerMod.TransitAnnouncementConfig;
			config.GlobalAnnouncementMaxDistance = Mathf.Max(config.GlobalAnnouncementMinDistance + 0.01f, value);
		}
	}

	[SettingsUISection(kPublicTransportTab, kTransitAnnouncementGroup)]
	[SettingsUIButton]
	[SettingsUIButtonGroup(kTransitAnnouncementButtonGroup)]
	[SettingsUIDisplayName(overrideValue: "Rescan Custom Announcement Files")]
	[SettingsUIDescription(overrideValue: "Rescan the Custom Announcements folder for .wav and .ogg files and refresh dropdown options.")]
	public bool UpdateCustomTransitAnnouncements
	{
		set => SirenChangerMod.RefreshCustomTransitAnnouncementsFromOptions();
	}

	[SettingsUISection(kPublicTransportTab, kTransitAnnouncementTrainGroup)]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetTransitAnnouncementVoiceOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Train TTS Voice")]
	[SettingsUIDescription(overrideValue: "Voice used for train custom and stop/service TTS steps.")]
	public string TrainAnnouncementVoice
	{
		get => SirenChangerMod.GetTransitAnnouncementServiceVoice(TransitAnnouncementServiceType.Train);
		set => SirenChangerMod.SetTransitAnnouncementServiceVoice(TransitAnnouncementServiceType.Train, value);
	}

	[SettingsUISection(kPublicTransportTab, kTransitAnnouncementBusGroup)]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetTransitAnnouncementVoiceOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Bus TTS Voice")]
	[SettingsUIDescription(overrideValue: "Voice used for bus custom and stop/service TTS steps.")]
	public string BusAnnouncementVoice
	{
		get => SirenChangerMod.GetTransitAnnouncementServiceVoice(TransitAnnouncementServiceType.Bus);
		set => SirenChangerMod.SetTransitAnnouncementServiceVoice(TransitAnnouncementServiceType.Bus, value);
	}

	[SettingsUISection(kPublicTransportTab, kTransitAnnouncementMetroGroup)]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetTransitAnnouncementVoiceOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Metro TTS Voice")]
	[SettingsUIDescription(overrideValue: "Voice used for metro custom and stop/service TTS steps.")]
	public string MetroAnnouncementVoice
	{
		get => SirenChangerMod.GetTransitAnnouncementServiceVoice(TransitAnnouncementServiceType.Metro);
		set => SirenChangerMod.SetTransitAnnouncementServiceVoice(TransitAnnouncementServiceType.Metro, value);
	}

	[SettingsUISection(kPublicTransportTab, kTransitAnnouncementTramGroup)]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetTransitAnnouncementVoiceOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Tram TTS Voice")]
	[SettingsUIDescription(overrideValue: "Voice used for tram custom and stop/service TTS steps.")]
	public string TramAnnouncementVoice
	{
		get => SirenChangerMod.GetTransitAnnouncementServiceVoice(TransitAnnouncementServiceType.Tram);
		set => SirenChangerMod.SetTransitAnnouncementServiceVoice(TransitAnnouncementServiceType.Tram, value);
	}

	[SettingsUISection(kPublicTransportTab, kTransitAnnouncementTrainGroup)]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetTransitAnnouncementSelectionOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Train Arrival Lead Audio")]
	[SettingsUIDescription(overrideValue: "Step 1: custom audio played before TTS when a train arrives.")]
	public string TrainArrivalAnnouncement
	{
		get => SirenChangerMod.GetTransitAnnouncementLeadSelection(TransitAnnouncementSlot.TrainArrival);
		set => SirenChangerMod.SetTransitAnnouncementLeadSelection(TransitAnnouncementSlot.TrainArrival, value);
	}

	[SettingsUISection(kPublicTransportTab, kTransitAnnouncementTrainGroup)]
	[SettingsUITextInput]
	[SettingsUIDisplayName(overrideValue: "Train Arrival Custom TTS")]
	[SettingsUIDescription(overrideValue: "Step 2 (optional): custom spoken text before the stop/service TTS.")]
	public string TrainArrivalCustomTts
	{
		get => SirenChangerMod.GetTransitAnnouncementCustomText(TransitAnnouncementSlot.TrainArrival);
		set => SirenChangerMod.SetTransitAnnouncementCustomText(TransitAnnouncementSlot.TrainArrival, value);
	}

	[SettingsUISection(kPublicTransportTab, kTransitAnnouncementTrainGroup)]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetTransitAnnouncementSelectionOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Train Arrival Tail Audio")]
	[SettingsUIDescription(overrideValue: "Step 4 (optional): custom audio played after stop/service TTS.")]
	public string TrainArrivalTailAnnouncement
	{
		get => SirenChangerMod.GetTransitAnnouncementTailSelection(TransitAnnouncementSlot.TrainArrival);
		set => SirenChangerMod.SetTransitAnnouncementTailSelection(TransitAnnouncementSlot.TrainArrival, value);
	}

	[SettingsUISection(kPublicTransportTab, kTransitAnnouncementTrainGroup)]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetTransitAnnouncementSelectionOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Train Departure Lead Audio")]
	[SettingsUIDescription(overrideValue: "Step 1: custom audio played before TTS when a train departs.")]
	public string TrainDepartureAnnouncement
	{
		get => SirenChangerMod.GetTransitAnnouncementLeadSelection(TransitAnnouncementSlot.TrainDeparture);
		set => SirenChangerMod.SetTransitAnnouncementLeadSelection(TransitAnnouncementSlot.TrainDeparture, value);
	}

	[SettingsUISection(kPublicTransportTab, kTransitAnnouncementTrainGroup)]
	[SettingsUITextInput]
	[SettingsUIDisplayName(overrideValue: "Train Departure Custom TTS")]
	[SettingsUIDescription(overrideValue: "Step 2 (optional): custom spoken text before the stop/service TTS.")]
	public string TrainDepartureCustomTts
	{
		get => SirenChangerMod.GetTransitAnnouncementCustomText(TransitAnnouncementSlot.TrainDeparture);
		set => SirenChangerMod.SetTransitAnnouncementCustomText(TransitAnnouncementSlot.TrainDeparture, value);
	}

	[SettingsUISection(kPublicTransportTab, kTransitAnnouncementTrainGroup)]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetTransitAnnouncementSelectionOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Train Departure Tail Audio")]
	[SettingsUIDescription(overrideValue: "Step 4 (optional): custom audio played after stop/service TTS.")]
	public string TrainDepartureTailAnnouncement
	{
		get => SirenChangerMod.GetTransitAnnouncementTailSelection(TransitAnnouncementSlot.TrainDeparture);
		set => SirenChangerMod.SetTransitAnnouncementTailSelection(TransitAnnouncementSlot.TrainDeparture, value);
	}

	[SettingsUISection(kPublicTransportTab, kTransitAnnouncementBusGroup)]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetTransitAnnouncementSelectionOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Bus Arrival Lead Audio")]
	[SettingsUIDescription(overrideValue: "Step 1: custom audio played before TTS when a bus arrives.")]
	public string BusArrivalAnnouncement
	{
		get => SirenChangerMod.GetTransitAnnouncementLeadSelection(TransitAnnouncementSlot.BusArrival);
		set => SirenChangerMod.SetTransitAnnouncementLeadSelection(TransitAnnouncementSlot.BusArrival, value);
	}

	[SettingsUISection(kPublicTransportTab, kTransitAnnouncementBusGroup)]
	[SettingsUITextInput]
	[SettingsUIDisplayName(overrideValue: "Bus Arrival Custom TTS")]
	[SettingsUIDescription(overrideValue: "Step 2 (optional): custom spoken text before the stop/service TTS.")]
	public string BusArrivalCustomTts
	{
		get => SirenChangerMod.GetTransitAnnouncementCustomText(TransitAnnouncementSlot.BusArrival);
		set => SirenChangerMod.SetTransitAnnouncementCustomText(TransitAnnouncementSlot.BusArrival, value);
	}

	[SettingsUISection(kPublicTransportTab, kTransitAnnouncementBusGroup)]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetTransitAnnouncementSelectionOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Bus Arrival Tail Audio")]
	[SettingsUIDescription(overrideValue: "Step 4 (optional): custom audio played after stop/service TTS.")]
	public string BusArrivalTailAnnouncement
	{
		get => SirenChangerMod.GetTransitAnnouncementTailSelection(TransitAnnouncementSlot.BusArrival);
		set => SirenChangerMod.SetTransitAnnouncementTailSelection(TransitAnnouncementSlot.BusArrival, value);
	}

	[SettingsUISection(kPublicTransportTab, kTransitAnnouncementBusGroup)]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetTransitAnnouncementSelectionOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Bus Departure Lead Audio")]
	[SettingsUIDescription(overrideValue: "Step 1: custom audio played before TTS when a bus departs.")]
	public string BusDepartureAnnouncement
	{
		get => SirenChangerMod.GetTransitAnnouncementLeadSelection(TransitAnnouncementSlot.BusDeparture);
		set => SirenChangerMod.SetTransitAnnouncementLeadSelection(TransitAnnouncementSlot.BusDeparture, value);
	}

	[SettingsUISection(kPublicTransportTab, kTransitAnnouncementBusGroup)]
	[SettingsUITextInput]
	[SettingsUIDisplayName(overrideValue: "Bus Departure Custom TTS")]
	[SettingsUIDescription(overrideValue: "Step 2 (optional): custom spoken text before the stop/service TTS.")]
	public string BusDepartureCustomTts
	{
		get => SirenChangerMod.GetTransitAnnouncementCustomText(TransitAnnouncementSlot.BusDeparture);
		set => SirenChangerMod.SetTransitAnnouncementCustomText(TransitAnnouncementSlot.BusDeparture, value);
	}

	[SettingsUISection(kPublicTransportTab, kTransitAnnouncementBusGroup)]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetTransitAnnouncementSelectionOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Bus Departure Tail Audio")]
	[SettingsUIDescription(overrideValue: "Step 4 (optional): custom audio played after stop/service TTS.")]
	public string BusDepartureTailAnnouncement
	{
		get => SirenChangerMod.GetTransitAnnouncementTailSelection(TransitAnnouncementSlot.BusDeparture);
		set => SirenChangerMod.SetTransitAnnouncementTailSelection(TransitAnnouncementSlot.BusDeparture, value);
	}

	[SettingsUISection(kPublicTransportTab, kTransitAnnouncementMetroGroup)]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetTransitAnnouncementSelectionOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Metro Arrival Lead Audio")]
	[SettingsUIDescription(overrideValue: "Step 1: custom audio played before TTS when a metro train arrives.")]
	public string MetroArrivalAnnouncement
	{
		get => SirenChangerMod.GetTransitAnnouncementLeadSelection(TransitAnnouncementSlot.MetroArrival);
		set => SirenChangerMod.SetTransitAnnouncementLeadSelection(TransitAnnouncementSlot.MetroArrival, value);
	}

	[SettingsUISection(kPublicTransportTab, kTransitAnnouncementMetroGroup)]
	[SettingsUITextInput]
	[SettingsUIDisplayName(overrideValue: "Metro Arrival Custom TTS")]
	[SettingsUIDescription(overrideValue: "Step 2 (optional): custom spoken text before the stop/service TTS.")]
	public string MetroArrivalCustomTts
	{
		get => SirenChangerMod.GetTransitAnnouncementCustomText(TransitAnnouncementSlot.MetroArrival);
		set => SirenChangerMod.SetTransitAnnouncementCustomText(TransitAnnouncementSlot.MetroArrival, value);
	}

	[SettingsUISection(kPublicTransportTab, kTransitAnnouncementMetroGroup)]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetTransitAnnouncementSelectionOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Metro Arrival Tail Audio")]
	[SettingsUIDescription(overrideValue: "Step 4 (optional): custom audio played after stop/service TTS.")]
	public string MetroArrivalTailAnnouncement
	{
		get => SirenChangerMod.GetTransitAnnouncementTailSelection(TransitAnnouncementSlot.MetroArrival);
		set => SirenChangerMod.SetTransitAnnouncementTailSelection(TransitAnnouncementSlot.MetroArrival, value);
	}

	[SettingsUISection(kPublicTransportTab, kTransitAnnouncementMetroGroup)]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetTransitAnnouncementSelectionOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Metro Departure Lead Audio")]
	[SettingsUIDescription(overrideValue: "Step 1: custom audio played before TTS when a metro train departs.")]
	public string MetroDepartureAnnouncement
	{
		get => SirenChangerMod.GetTransitAnnouncementLeadSelection(TransitAnnouncementSlot.MetroDeparture);
		set => SirenChangerMod.SetTransitAnnouncementLeadSelection(TransitAnnouncementSlot.MetroDeparture, value);
	}

	[SettingsUISection(kPublicTransportTab, kTransitAnnouncementMetroGroup)]
	[SettingsUITextInput]
	[SettingsUIDisplayName(overrideValue: "Metro Departure Custom TTS")]
	[SettingsUIDescription(overrideValue: "Step 2 (optional): custom spoken text before the stop/service TTS.")]
	public string MetroDepartureCustomTts
	{
		get => SirenChangerMod.GetTransitAnnouncementCustomText(TransitAnnouncementSlot.MetroDeparture);
		set => SirenChangerMod.SetTransitAnnouncementCustomText(TransitAnnouncementSlot.MetroDeparture, value);
	}

	[SettingsUISection(kPublicTransportTab, kTransitAnnouncementMetroGroup)]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetTransitAnnouncementSelectionOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Metro Departure Tail Audio")]
	[SettingsUIDescription(overrideValue: "Step 4 (optional): custom audio played after stop/service TTS.")]
	public string MetroDepartureTailAnnouncement
	{
		get => SirenChangerMod.GetTransitAnnouncementTailSelection(TransitAnnouncementSlot.MetroDeparture);
		set => SirenChangerMod.SetTransitAnnouncementTailSelection(TransitAnnouncementSlot.MetroDeparture, value);
	}

	[SettingsUISection(kPublicTransportTab, kTransitAnnouncementTramGroup)]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetTransitAnnouncementSelectionOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Tram Arrival Lead Audio")]
	[SettingsUIDescription(overrideValue: "Step 1: custom audio played before TTS when a tram arrives.")]
	public string TramArrivalAnnouncement
	{
		get => SirenChangerMod.GetTransitAnnouncementLeadSelection(TransitAnnouncementSlot.TramArrival);
		set => SirenChangerMod.SetTransitAnnouncementLeadSelection(TransitAnnouncementSlot.TramArrival, value);
	}

	[SettingsUISection(kPublicTransportTab, kTransitAnnouncementTramGroup)]
	[SettingsUITextInput]
	[SettingsUIDisplayName(overrideValue: "Tram Arrival Custom TTS")]
	[SettingsUIDescription(overrideValue: "Step 2 (optional): custom spoken text before the stop/service TTS.")]
	public string TramArrivalCustomTts
	{
		get => SirenChangerMod.GetTransitAnnouncementCustomText(TransitAnnouncementSlot.TramArrival);
		set => SirenChangerMod.SetTransitAnnouncementCustomText(TransitAnnouncementSlot.TramArrival, value);
	}

	[SettingsUISection(kPublicTransportTab, kTransitAnnouncementTramGroup)]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetTransitAnnouncementSelectionOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Tram Arrival Tail Audio")]
	[SettingsUIDescription(overrideValue: "Step 4 (optional): custom audio played after stop/service TTS.")]
	public string TramArrivalTailAnnouncement
	{
		get => SirenChangerMod.GetTransitAnnouncementTailSelection(TransitAnnouncementSlot.TramArrival);
		set => SirenChangerMod.SetTransitAnnouncementTailSelection(TransitAnnouncementSlot.TramArrival, value);
	}

	[SettingsUISection(kPublicTransportTab, kTransitAnnouncementTramGroup)]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetTransitAnnouncementSelectionOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Tram Departure Lead Audio")]
	[SettingsUIDescription(overrideValue: "Step 1: custom audio played before TTS when a tram departs.")]
	public string TramDepartureAnnouncement
	{
		get => SirenChangerMod.GetTransitAnnouncementLeadSelection(TransitAnnouncementSlot.TramDeparture);
		set => SirenChangerMod.SetTransitAnnouncementLeadSelection(TransitAnnouncementSlot.TramDeparture, value);
	}

	[SettingsUISection(kPublicTransportTab, kTransitAnnouncementTramGroup)]
	[SettingsUITextInput]
	[SettingsUIDisplayName(overrideValue: "Tram Departure Custom TTS")]
	[SettingsUIDescription(overrideValue: "Step 2 (optional): custom spoken text before the stop/service TTS.")]
	public string TramDepartureCustomTts
	{
		get => SirenChangerMod.GetTransitAnnouncementCustomText(TransitAnnouncementSlot.TramDeparture);
		set => SirenChangerMod.SetTransitAnnouncementCustomText(TransitAnnouncementSlot.TramDeparture, value);
	}

	[SettingsUISection(kPublicTransportTab, kTransitAnnouncementTramGroup)]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetTransitAnnouncementSelectionOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Tram Departure Tail Audio")]
	[SettingsUIDescription(overrideValue: "Step 4 (optional): custom audio played after stop/service TTS.")]
	public string TramDepartureTailAnnouncement
	{
		get => SirenChangerMod.GetTransitAnnouncementTailSelection(TransitAnnouncementSlot.TramDeparture);
		set => SirenChangerMod.SetTransitAnnouncementTailSelection(TransitAnnouncementSlot.TramDeparture, value);
	}

	[SettingsUISection(kPublicTransportTab, kTransitAnnouncementGroup)]
	[SettingsUIMultilineText]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIWarning(typeof(SirenChangerSettings), nameof(ShowTransitAnnouncementCatalogWarning))]
	[SettingsUIDisplayName(overrideValue: "Custom Announcement File Scan Status")]
	[SettingsUIDescription(overrideValue: "Shows the latest Custom Announcements folder scan summary and changed files.")]
	public string TransitAnnouncementCatalogScanStatus => SirenChangerMod.GetTransitAnnouncementCatalogScanStatusText();

	// Warning helper: indicate that no custom transit announcement files are currently available.
	public bool ShowTransitAnnouncementCatalogWarning()
	{
		return !SirenChangerMod.HasTransitAnnouncementProfiles();
	}

	[Preserve]
	public static DropdownItem<string>[] GetTransitAnnouncementSelectionOptions()
	{
		return SirenChangerMod.BuildTransitAnnouncementDropdownItems();
	}

	[Preserve]
	public static DropdownItem<string>[] GetTransitAnnouncementVoiceOptions()
	{
		return SirenChangerMod.BuildTransitAnnouncementVoiceDropdownItems();
	}

	[Preserve]
	public static string GetRescanCustomAnnouncementFilesLabel() => "Rescan Custom Announcement Files";

	[Preserve]
	public static string GetRescanCustomAnnouncementFilesDescription() => "Rescan the Custom Announcements folder for .wav and .ogg files and refresh dropdown options.";

	// Restore transit-announcement settings to defaults and rebuild the custom-file catalog.
	private static void ResetTransitAnnouncementDefaults()
	{
		AudioReplacementDomainConfig config = SirenChangerMod.TransitAnnouncementConfig;
		config.Enabled = true;
		config.CustomFolderName = SirenChangerMod.TransitAnnouncementCustomFolderName;
		config.DefaultSelection = SirenReplacementConfig.DefaultSelectionToken;
		config.EditProfileSelection = string.Empty;
		config.CopyFromProfileSelection = string.Empty;
		config.CustomProfiles.Clear();
		config.TargetSelections.Clear();
		config.TransitAnnouncementCustomTextByTarget.Clear();
		config.TransitAnnouncementVoiceByService.Clear();
		config.TargetSelectionTarget = string.Empty;
		config.KnownTargets.Clear();
		config.MissingSelectionFallbackBehavior = SirenFallbackBehavior.Default;
		config.AlternateFallbackSelection = SirenReplacementConfig.DefaultSelectionToken;
		config.GlobalAnnouncementVolume = 1f;
		config.GlobalAnnouncementMinDistance = 12f;
		config.GlobalAnnouncementMaxDistance = 120f;
		config.LastCatalogScanUtcTicks = 0;
		config.LastCatalogScanFileCount = 0;
		config.LastCatalogScanAddedCount = 0;
		config.LastCatalogScanRemovedCount = 0;
		config.LastCatalogScanChangedFiles.Clear();
		config.LastTargetScanUtcTicks = 0;
		config.LastTargetScanStatus = string.Empty;
		config.LastValidationUtcTicks = 0;
		config.LastValidationReport = string.Empty;

		SirenChangerMod.SyncCustomTransitAnnouncementCatalog(saveIfChanged: false);
		SirenChangerMod.NormalizeTransitAnnouncementTargets();
		SirenChangerMod.NormalizeTransitAnnouncementSpeechSettings();
		config.Normalize(SirenChangerMod.TransitAnnouncementCustomFolderName);
	}
}
