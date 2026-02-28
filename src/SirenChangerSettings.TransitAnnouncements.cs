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

	[SettingsUISection(kPublicTransportTab, kTransitAnnouncementGroup)]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetTransitAnnouncementSelectionOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Train Arrival")]
	[SettingsUIDescription(overrideValue: "Custom sound played when a train arrives at a station.")]
	public string TrainArrivalAnnouncement
	{
		get => SirenChangerMod.GetTransitAnnouncementSelection(TransitAnnouncementSlot.TrainArrival);
		set => SirenChangerMod.SetTransitAnnouncementSelection(TransitAnnouncementSlot.TrainArrival, value);
	}

	[SettingsUISection(kPublicTransportTab, kTransitAnnouncementGroup)]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetTransitAnnouncementSelectionOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Train Departure")]
	[SettingsUIDescription(overrideValue: "Custom sound played when a train departs a station.")]
	public string TrainDepartureAnnouncement
	{
		get => SirenChangerMod.GetTransitAnnouncementSelection(TransitAnnouncementSlot.TrainDeparture);
		set => SirenChangerMod.SetTransitAnnouncementSelection(TransitAnnouncementSlot.TrainDeparture, value);
	}

	[SettingsUISection(kPublicTransportTab, kTransitAnnouncementGroup)]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetTransitAnnouncementSelectionOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Bus Arrival")]
	[SettingsUIDescription(overrideValue: "Custom sound played when a bus arrives at a stop.")]
	public string BusArrivalAnnouncement
	{
		get => SirenChangerMod.GetTransitAnnouncementSelection(TransitAnnouncementSlot.BusArrival);
		set => SirenChangerMod.SetTransitAnnouncementSelection(TransitAnnouncementSlot.BusArrival, value);
	}

	[SettingsUISection(kPublicTransportTab, kTransitAnnouncementGroup)]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetTransitAnnouncementSelectionOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Bus Departure")]
	[SettingsUIDescription(overrideValue: "Custom sound played when a bus departs a stop.")]
	public string BusDepartureAnnouncement
	{
		get => SirenChangerMod.GetTransitAnnouncementSelection(TransitAnnouncementSlot.BusDeparture);
		set => SirenChangerMod.SetTransitAnnouncementSelection(TransitAnnouncementSlot.BusDeparture, value);
	}

	[SettingsUISection(kPublicTransportTab, kTransitAnnouncementGroup)]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetTransitAnnouncementSelectionOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Metro Arrival")]
	[SettingsUIDescription(overrideValue: "Custom sound played when a metro train arrives at a station.")]
	public string MetroArrivalAnnouncement
	{
		get => SirenChangerMod.GetTransitAnnouncementSelection(TransitAnnouncementSlot.MetroArrival);
		set => SirenChangerMod.SetTransitAnnouncementSelection(TransitAnnouncementSlot.MetroArrival, value);
	}

	[SettingsUISection(kPublicTransportTab, kTransitAnnouncementGroup)]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetTransitAnnouncementSelectionOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Metro Departure")]
	[SettingsUIDescription(overrideValue: "Custom sound played when a metro train departs a station.")]
	public string MetroDepartureAnnouncement
	{
		get => SirenChangerMod.GetTransitAnnouncementSelection(TransitAnnouncementSlot.MetroDeparture);
		set => SirenChangerMod.SetTransitAnnouncementSelection(TransitAnnouncementSlot.MetroDeparture, value);
	}

	[SettingsUISection(kPublicTransportTab, kTransitAnnouncementGroup)]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetTransitAnnouncementSelectionOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Tram Arrival")]
	[SettingsUIDescription(overrideValue: "Custom sound played when a tram arrives at a stop.")]
	public string TramArrivalAnnouncement
	{
		get => SirenChangerMod.GetTransitAnnouncementSelection(TransitAnnouncementSlot.TramArrival);
		set => SirenChangerMod.SetTransitAnnouncementSelection(TransitAnnouncementSlot.TramArrival, value);
	}

	[SettingsUISection(kPublicTransportTab, kTransitAnnouncementGroup)]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetTransitAnnouncementSelectionOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Tram Departure")]
	[SettingsUIDescription(overrideValue: "Custom sound played when a tram departs a stop.")]
	public string TramDepartureAnnouncement
	{
		get => SirenChangerMod.GetTransitAnnouncementSelection(TransitAnnouncementSlot.TramDeparture);
		set => SirenChangerMod.SetTransitAnnouncementSelection(TransitAnnouncementSlot.TramDeparture, value);
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
		config.Normalize(SirenChangerMod.TransitAnnouncementCustomFolderName);
	}
}

