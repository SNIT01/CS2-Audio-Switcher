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

	[SettingsUISection(kPublicTransportTab, kTransitAnnouncementLineGroup)]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetTransitAnnouncementLineServiceOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Line Service")]
	[SettingsUIDescription(overrideValue: "Select which service's discovered lines are shown in the line override editor.")]
	public string TransitAnnouncementLineOverrideService
	{
		get => SirenChangerMod.GetTransitAnnouncementLineEditorService();
		set => SirenChangerMod.SetTransitAnnouncementLineEditorService(value);
	}

	[SettingsUISection(kPublicTransportTab, kTransitAnnouncementLineGroup)]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetTransitAnnouncementLineOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Transit Line")]
	[SettingsUIDescription(overrideValue: "Choose one discovered line to edit per-line arrival/departure overrides.")]
	public string TransitAnnouncementSelectedLineOverride
	{
		get => SirenChangerMod.GetTransitAnnouncementSelectedLineForOptions();
		set => SirenChangerMod.SetTransitAnnouncementSelectedLineForOptions(value);
	}

	[SettingsUISection(kPublicTransportTab, kTransitAnnouncementLineGroup)]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(IsTransitAnnouncementLineOverrideDisabled))]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetTransitAnnouncementSelectionOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Line Arrival Override")]
	[SettingsUIDescription(overrideValue: "Clip used for arrivals of the selected line. Default falls back to the service arrival clip.")]
	public string TransitAnnouncementLineArrivalOverride
	{
		get => SirenChangerMod.GetTransitAnnouncementLineArrivalSelectionForOptions();
		set => SirenChangerMod.SetTransitAnnouncementLineArrivalSelectionForOptions(value);
	}

	[SettingsUISection(kPublicTransportTab, kTransitAnnouncementLineGroup)]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(IsTransitAnnouncementLineOverrideDisabled))]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetTransitAnnouncementSelectionOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Line Departure Override")]
	[SettingsUIDescription(overrideValue: "Clip used for departures of the selected line. Default falls back to the service departure clip.")]
	public string TransitAnnouncementLineDepartureOverride
	{
		get => SirenChangerMod.GetTransitAnnouncementLineDepartureSelectionForOptions();
		set => SirenChangerMod.SetTransitAnnouncementLineDepartureSelectionForOptions(value);
	}

	[SettingsUISection(kPublicTransportTab, kTransitAnnouncementLineGroup)]
	[SettingsUIMultilineText]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIWarning(typeof(SirenChangerSettings), nameof(ShowTransitAnnouncementLineWarning))]
	[SettingsUIDisplayName(overrideValue: "Line Override Status")]
	[SettingsUIDescription(overrideValue: "Shows the selected line and its current arrival/departure overrides.")]
	public string TransitAnnouncementLineOverrideStatus => SirenChangerMod.GetSelectedTransitAnnouncementLineStatusText();

	[SettingsUISection(kPublicTransportTab, kTransitAnnouncementLineGroup)]
	[SettingsUIButton]
	[SettingsUIDisplayName(overrideValue: "Prune Stale Lines")]
	[SettingsUIDescription(overrideValue: "Remove discovered lines that are not observed in this session and have no line-specific overrides.")]
	public bool PruneStaleTransitAnnouncementLines
	{
		set => SirenChangerMod.PruneStaleTransitAnnouncementLinesFromOptions();
	}

	[SettingsUISection(kPublicTransportTab, kTransitAnnouncementTrainGroup)]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetTransitAnnouncementSelectionOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Train Arrival Primary Audio")]
	[SettingsUIDescription(overrideValue: "Default clip used when a train arrives.")]
	public string TrainArrivalAnnouncement
	{
		get => SirenChangerMod.GetTransitAnnouncementLeadSelection(TransitAnnouncementSlot.TrainArrival);
		set => SirenChangerMod.SetTransitAnnouncementLeadSelection(TransitAnnouncementSlot.TrainArrival, value);
	}

	[SettingsUISection(kPublicTransportTab, kTransitAnnouncementTrainGroup)]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetTransitAnnouncementSelectionOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Train Arrival Tail Audio")]
	[SettingsUIDescription(overrideValue: "Optional tail clip played after the primary train arrival clip.")]
	public string TrainArrivalTailAnnouncement
	{
		get => SirenChangerMod.GetTransitAnnouncementTailSelection(TransitAnnouncementSlot.TrainArrival);
		set => SirenChangerMod.SetTransitAnnouncementTailSelection(TransitAnnouncementSlot.TrainArrival, value);
	}

	[SettingsUISection(kPublicTransportTab, kTransitAnnouncementTrainGroup)]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetTransitAnnouncementSelectionOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Train Departure Primary Audio")]
	[SettingsUIDescription(overrideValue: "Default clip used when a train departs.")]
	public string TrainDepartureAnnouncement
	{
		get => SirenChangerMod.GetTransitAnnouncementLeadSelection(TransitAnnouncementSlot.TrainDeparture);
		set => SirenChangerMod.SetTransitAnnouncementLeadSelection(TransitAnnouncementSlot.TrainDeparture, value);
	}

	[SettingsUISection(kPublicTransportTab, kTransitAnnouncementTrainGroup)]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetTransitAnnouncementSelectionOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Train Departure Tail Audio")]
	[SettingsUIDescription(overrideValue: "Optional tail clip played after the primary train departure clip.")]
	public string TrainDepartureTailAnnouncement
	{
		get => SirenChangerMod.GetTransitAnnouncementTailSelection(TransitAnnouncementSlot.TrainDeparture);
		set => SirenChangerMod.SetTransitAnnouncementTailSelection(TransitAnnouncementSlot.TrainDeparture, value);
	}

	[SettingsUISection(kPublicTransportTab, kTransitAnnouncementBusGroup)]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetTransitAnnouncementSelectionOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Bus Arrival Primary Audio")]
	[SettingsUIDescription(overrideValue: "Default clip used when a bus arrives.")]
	public string BusArrivalAnnouncement
	{
		get => SirenChangerMod.GetTransitAnnouncementLeadSelection(TransitAnnouncementSlot.BusArrival);
		set => SirenChangerMod.SetTransitAnnouncementLeadSelection(TransitAnnouncementSlot.BusArrival, value);
	}

	[SettingsUISection(kPublicTransportTab, kTransitAnnouncementBusGroup)]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetTransitAnnouncementSelectionOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Bus Arrival Tail Audio")]
	[SettingsUIDescription(overrideValue: "Optional tail clip played after the primary bus arrival clip.")]
	public string BusArrivalTailAnnouncement
	{
		get => SirenChangerMod.GetTransitAnnouncementTailSelection(TransitAnnouncementSlot.BusArrival);
		set => SirenChangerMod.SetTransitAnnouncementTailSelection(TransitAnnouncementSlot.BusArrival, value);
	}

	[SettingsUISection(kPublicTransportTab, kTransitAnnouncementBusGroup)]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetTransitAnnouncementSelectionOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Bus Departure Primary Audio")]
	[SettingsUIDescription(overrideValue: "Default clip used when a bus departs.")]
	public string BusDepartureAnnouncement
	{
		get => SirenChangerMod.GetTransitAnnouncementLeadSelection(TransitAnnouncementSlot.BusDeparture);
		set => SirenChangerMod.SetTransitAnnouncementLeadSelection(TransitAnnouncementSlot.BusDeparture, value);
	}

	[SettingsUISection(kPublicTransportTab, kTransitAnnouncementBusGroup)]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetTransitAnnouncementSelectionOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Bus Departure Tail Audio")]
	[SettingsUIDescription(overrideValue: "Optional tail clip played after the primary bus departure clip.")]
	public string BusDepartureTailAnnouncement
	{
		get => SirenChangerMod.GetTransitAnnouncementTailSelection(TransitAnnouncementSlot.BusDeparture);
		set => SirenChangerMod.SetTransitAnnouncementTailSelection(TransitAnnouncementSlot.BusDeparture, value);
	}

	[SettingsUISection(kPublicTransportTab, kTransitAnnouncementMetroGroup)]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetTransitAnnouncementSelectionOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Metro Arrival Primary Audio")]
	[SettingsUIDescription(overrideValue: "Default clip used when a metro train arrives.")]
	public string MetroArrivalAnnouncement
	{
		get => SirenChangerMod.GetTransitAnnouncementLeadSelection(TransitAnnouncementSlot.MetroArrival);
		set => SirenChangerMod.SetTransitAnnouncementLeadSelection(TransitAnnouncementSlot.MetroArrival, value);
	}

	[SettingsUISection(kPublicTransportTab, kTransitAnnouncementMetroGroup)]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetTransitAnnouncementSelectionOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Metro Arrival Tail Audio")]
	[SettingsUIDescription(overrideValue: "Optional tail clip played after the primary metro arrival clip.")]
	public string MetroArrivalTailAnnouncement
	{
		get => SirenChangerMod.GetTransitAnnouncementTailSelection(TransitAnnouncementSlot.MetroArrival);
		set => SirenChangerMod.SetTransitAnnouncementTailSelection(TransitAnnouncementSlot.MetroArrival, value);
	}

	[SettingsUISection(kPublicTransportTab, kTransitAnnouncementMetroGroup)]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetTransitAnnouncementSelectionOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Metro Departure Primary Audio")]
	[SettingsUIDescription(overrideValue: "Default clip used when a metro train departs.")]
	public string MetroDepartureAnnouncement
	{
		get => SirenChangerMod.GetTransitAnnouncementLeadSelection(TransitAnnouncementSlot.MetroDeparture);
		set => SirenChangerMod.SetTransitAnnouncementLeadSelection(TransitAnnouncementSlot.MetroDeparture, value);
	}

	[SettingsUISection(kPublicTransportTab, kTransitAnnouncementMetroGroup)]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetTransitAnnouncementSelectionOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Metro Departure Tail Audio")]
	[SettingsUIDescription(overrideValue: "Optional tail clip played after the primary metro departure clip.")]
	public string MetroDepartureTailAnnouncement
	{
		get => SirenChangerMod.GetTransitAnnouncementTailSelection(TransitAnnouncementSlot.MetroDeparture);
		set => SirenChangerMod.SetTransitAnnouncementTailSelection(TransitAnnouncementSlot.MetroDeparture, value);
	}

	[SettingsUISection(kPublicTransportTab, kTransitAnnouncementTramGroup)]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetTransitAnnouncementSelectionOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Tram Arrival Primary Audio")]
	[SettingsUIDescription(overrideValue: "Default clip used when a tram arrives.")]
	public string TramArrivalAnnouncement
	{
		get => SirenChangerMod.GetTransitAnnouncementLeadSelection(TransitAnnouncementSlot.TramArrival);
		set => SirenChangerMod.SetTransitAnnouncementLeadSelection(TransitAnnouncementSlot.TramArrival, value);
	}

	[SettingsUISection(kPublicTransportTab, kTransitAnnouncementTramGroup)]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetTransitAnnouncementSelectionOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Tram Arrival Tail Audio")]
	[SettingsUIDescription(overrideValue: "Optional tail clip played after the primary tram arrival clip.")]
	public string TramArrivalTailAnnouncement
	{
		get => SirenChangerMod.GetTransitAnnouncementTailSelection(TransitAnnouncementSlot.TramArrival);
		set => SirenChangerMod.SetTransitAnnouncementTailSelection(TransitAnnouncementSlot.TramArrival, value);
	}

	[SettingsUISection(kPublicTransportTab, kTransitAnnouncementTramGroup)]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetTransitAnnouncementSelectionOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Tram Departure Primary Audio")]
	[SettingsUIDescription(overrideValue: "Default clip used when a tram departs.")]
	public string TramDepartureAnnouncement
	{
		get => SirenChangerMod.GetTransitAnnouncementLeadSelection(TransitAnnouncementSlot.TramDeparture);
		set => SirenChangerMod.SetTransitAnnouncementLeadSelection(TransitAnnouncementSlot.TramDeparture, value);
	}

	[SettingsUISection(kPublicTransportTab, kTransitAnnouncementTramGroup)]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetTransitAnnouncementSelectionOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Tram Departure Tail Audio")]
	[SettingsUIDescription(overrideValue: "Optional tail clip played after the primary tram departure clip.")]
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

	public bool IsTransitAnnouncementLineOverrideDisabled()
	{
		return SirenChangerMod.IsTransitAnnouncementLineEditorDisabled();
	}

	public bool ShowTransitAnnouncementLineWarning()
	{
		return IsTransitAnnouncementLineOverrideDisabled();
	}

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
	public static DropdownItem<string>[] GetTransitAnnouncementLineServiceOptions()
	{
		return SirenChangerMod.BuildTransitAnnouncementLineServiceDropdownItems();
	}

	[Preserve]
	public static DropdownItem<string>[] GetTransitAnnouncementLineOptions()
	{
		return SirenChangerMod.BuildTransitAnnouncementLineDropdownItems();
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
		config.TransitAnnouncementLineSelections.Clear();
		config.TransitAnnouncementKnownLines.Clear();
		config.TransitAnnouncementSelectedLine = string.Empty;
		config.TransitAnnouncementLineDisplayByKey.Clear();
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
		SirenChangerMod.SetTransitAnnouncementLineEditorService("train");
		config.Normalize(SirenChangerMod.TransitAnnouncementCustomFolderName);
	}
}
