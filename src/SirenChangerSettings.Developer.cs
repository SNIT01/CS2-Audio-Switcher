using System;
using Game.Settings;
using Game.UI.Menu;
using Game.UI.Widgets;
using UnityEngine.Scripting;

namespace SirenChanger;

// Developer tab with read-only inspection tools for detected runtime audio.
public sealed partial class SirenChangerSettings
{
	public const string kDeveloperTab = "Developer";

	public const string kDeveloperSirenGroup = "Detected Sirens";

	public const string kDeveloperEngineGroup = "Detected Vehicle Engines";

	public const string kDeveloperAmbientGroup = "Detected Ambient Sounds";

	public const string kDeveloperModuleGroup = "Module Creation & Upload";

	private const string kDeveloperModuleSirenButtonGroup = "Sirens";

	private const string kDeveloperModuleEngineButtonGroup = "Engines";

	private const string kDeveloperModuleAmbientButtonGroup = "Ambient";

	private const string kDeveloperModuleTransitButtonGroup = "Transit";

	private const string kDeveloperModuleBulkButtonGroup = "Selection";

	private const string kDeveloperModuleBuildButtonGroup = "Build";

	private const string kDeveloperModuleUploadButtonGroup = "Upload";

	[SettingsUISection(kDeveloperTab, kDeveloperSirenGroup)]
	[SettingsUIWarning(typeof(SirenChangerSettings), nameof(ShowDeveloperSirenWarning))]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetDetectedSirenSoundOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Detected Siren Sound")]
	[SettingsUIDescription(overrideValue: "Select a detected in-game siren sound source.")]
	public string DeveloperSirenSelection
	{
		get => SirenChangerMod.GetDeveloperSelection(DeveloperAudioDomain.Siren);
		set => SirenChangerMod.SetDeveloperSelection(DeveloperAudioDomain.Siren, value);
	}

	[SettingsUISection(kDeveloperTab, kDeveloperSirenGroup)]
	[SettingsUIButton]
	[SettingsUIDisplayName(overrideValue: "Preview Selected Siren")]
	[SettingsUIDescription(overrideValue: "Play the currently selected detected siren sound.")]
	public bool PreviewDetectedSiren
	{
		set => SirenChangerMod.PreviewDeveloperSelection(DeveloperAudioDomain.Siren);
	}

	[SettingsUISection(kDeveloperTab, kDeveloperSirenGroup)]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Last Action Result")]
	[SettingsUIDescription(overrideValue: "Shows the result of the last preview action for detected sirens.")]
	public string DeveloperSirenActionStatus => SirenChangerMod.GetDeveloperActionStatusText(DeveloperAudioDomain.Siren);

	[SettingsUISection(kDeveloperTab, kDeveloperSirenGroup)]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "SFX Parameters (Read-only)")]
	[SettingsUIDescription(overrideValue: "Displays SFX values captured from the selected detected siren source.")]
	public string DeveloperSirenSfxParameters => SirenChangerMod.GetDeveloperSfxParametersText(DeveloperAudioDomain.Siren);

	[SettingsUISection(kDeveloperTab, kDeveloperEngineGroup)]
	[SettingsUIWarning(typeof(SirenChangerSettings), nameof(ShowDeveloperEngineWarning))]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetDetectedEngineSoundOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Detected Engine Sound")]
	[SettingsUIDescription(overrideValue: "Select a detected in-game vehicle engine sound source.")]
	public string DeveloperEngineSelection
	{
		get => SirenChangerMod.GetDeveloperSelection(DeveloperAudioDomain.VehicleEngine);
		set => SirenChangerMod.SetDeveloperSelection(DeveloperAudioDomain.VehicleEngine, value);
	}

	[SettingsUISection(kDeveloperTab, kDeveloperEngineGroup)]
	[SettingsUIButton]
	[SettingsUIDisplayName(overrideValue: "Preview Selected Engine")]
	[SettingsUIDescription(overrideValue: "Play the currently selected detected vehicle engine sound.")]
	public bool PreviewDetectedEngine
	{
		set => SirenChangerMod.PreviewDeveloperSelection(DeveloperAudioDomain.VehicleEngine);
	}

	[SettingsUISection(kDeveloperTab, kDeveloperEngineGroup)]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Last Action Result")]
	[SettingsUIDescription(overrideValue: "Shows the result of the last preview action for detected vehicle engines.")]
	public string DeveloperEngineActionStatus => SirenChangerMod.GetDeveloperActionStatusText(DeveloperAudioDomain.VehicleEngine);

	[SettingsUISection(kDeveloperTab, kDeveloperEngineGroup)]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "SFX Parameters (Read-only)")]
	[SettingsUIDescription(overrideValue: "Displays SFX values captured from the selected detected engine source.")]
	public string DeveloperEngineSfxParameters => SirenChangerMod.GetDeveloperSfxParametersText(DeveloperAudioDomain.VehicleEngine);

	[SettingsUISection(kDeveloperTab, kDeveloperAmbientGroup)]
	[SettingsUIWarning(typeof(SirenChangerSettings), nameof(ShowDeveloperAmbientWarning))]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetDetectedAmbientSoundOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Detected Ambient Sound")]
	[SettingsUIDescription(overrideValue: "Select a detected in-game ambient sound source.")]
	public string DeveloperAmbientSelection
	{
		get => SirenChangerMod.GetDeveloperSelection(DeveloperAudioDomain.Ambient);
		set => SirenChangerMod.SetDeveloperSelection(DeveloperAudioDomain.Ambient, value);
	}

	[SettingsUISection(kDeveloperTab, kDeveloperAmbientGroup)]
	[SettingsUIButton]
	[SettingsUIDisplayName(overrideValue: "Preview Selected Ambient")]
	[SettingsUIDescription(overrideValue: "Play the currently selected detected ambient sound.")]
	public bool PreviewDetectedAmbient
	{
		set => SirenChangerMod.PreviewDeveloperSelection(DeveloperAudioDomain.Ambient);
	}

	[SettingsUISection(kDeveloperTab, kDeveloperAmbientGroup)]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Last Action Result")]
	[SettingsUIDescription(overrideValue: "Shows the result of the last preview action for detected ambient sounds.")]
	public string DeveloperAmbientActionStatus => SirenChangerMod.GetDeveloperActionStatusText(DeveloperAudioDomain.Ambient);

	[SettingsUISection(kDeveloperTab, kDeveloperAmbientGroup)]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "SFX Parameters (Read-only)")]
	[SettingsUIDescription(overrideValue: "Displays SFX values captured from the selected detected ambient source.")]
	public string DeveloperAmbientSfxParameters => SirenChangerMod.GetDeveloperSfxParametersText(DeveloperAudioDomain.Ambient);

	[SettingsUISection(kDeveloperTab, kDeveloperModuleGroup)]
	[SettingsUITextInput]
	[SettingsUIDisplayName(overrideValue: "Module Display Name")]
	[SettingsUIDescription(overrideValue: "Name used in generated manifests and upload metadata. Spaces are supported.")]
	public string DeveloperModuleDisplayName
	{
		get => SirenChangerMod.GetDeveloperModuleDisplayName();
		set => SirenChangerMod.SetDeveloperModuleDisplayName(value);
	}

	[SettingsUISection(kDeveloperTab, kDeveloperModuleGroup)]
	[SettingsUITextInput]
	[SettingsUIDisplayName(overrideValue: "Module ID")]
	[SettingsUIDescription(overrideValue: "Stable ID used in AudioSwitcherModule.json and uploads. Supports letters, numbers, periods, dashes, and underscores.")]
	public string DeveloperModuleId
	{
		get => SirenChangerMod.GetDeveloperModuleId();
		set => SirenChangerMod.SetDeveloperModuleId(value);
	}

	[SettingsUISection(kDeveloperTab, kDeveloperModuleGroup)]
	[SettingsUIDirectoryPicker]
	[SettingsUIDisplayName(overrideValue: "Export Directory")]
	[SettingsUIDescription(overrideValue: "Destination folder for generated module packages.")]
	public string DeveloperModuleExportDirectory
	{
		get => SirenChangerMod.GetDeveloperModuleExportDirectory();
		set => SirenChangerMod.SetDeveloperModuleExportDirectory(value);
	}

	[SettingsUISection(kDeveloperTab, kDeveloperModuleGroup)]
	[SettingsUITextInput]
	[SettingsUIDisplayName(overrideValue: "Package Folder Name")]
	[SettingsUIDescription(overrideValue: "Folder name created under Export Directory.")]
	public string DeveloperModuleFolderName
	{
		get => SirenChangerMod.GetDeveloperModuleFolderName();
		set => SirenChangerMod.SetDeveloperModuleFolderName(value);
	}

	[SettingsUISection(kDeveloperTab, kDeveloperModuleGroup)]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetModuleBuilderLocalSirenOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Local Siren File")]
	[SettingsUIDescription(overrideValue: "Choose a local siren file to add or remove from this package.")]
	public string DeveloperModuleLocalSirenSelection
	{
		get => SirenChangerMod.GetDeveloperModuleLocalAudioSelection(DeveloperAudioDomain.Siren);
		set => SirenChangerMod.SetDeveloperModuleLocalAudioSelection(DeveloperAudioDomain.Siren, value);
	}

	[SettingsUISection(kDeveloperTab, kDeveloperModuleGroup)]
	[SettingsUIButton]
	[SettingsUIButtonGroup(kDeveloperModuleSirenButtonGroup)]
	[SettingsUIDisplayName(overrideValue: "Add Selected Siren")]
	[SettingsUIDescription(overrideValue: "Add the selected local siren file to this package.")]
	public bool IncludeSelectedModuleSiren
	{
		set => SirenChangerMod.IncludeSelectedLocalAudioInModule(DeveloperAudioDomain.Siren);
	}

	[SettingsUISection(kDeveloperTab, kDeveloperModuleGroup)]
	[SettingsUIButton]
	[SettingsUIButtonGroup(kDeveloperModuleSirenButtonGroup)]
	[SettingsUIDisplayName(overrideValue: "Remove Selected Siren")]
	[SettingsUIDescription(overrideValue: "Remove the selected local siren file from this package.")]
	public bool ExcludeSelectedModuleSiren
	{
		set => SirenChangerMod.ExcludeSelectedLocalAudioFromModule(DeveloperAudioDomain.Siren);
	}

	[SettingsUISection(kDeveloperTab, kDeveloperModuleGroup)]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetModuleBuilderLocalEngineOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Local Engine File")]
	[SettingsUIDescription(overrideValue: "Choose a local engine file to add or remove from this package.")]
	public string DeveloperModuleLocalEngineSelection
	{
		get => SirenChangerMod.GetDeveloperModuleLocalAudioSelection(DeveloperAudioDomain.VehicleEngine);
		set => SirenChangerMod.SetDeveloperModuleLocalAudioSelection(DeveloperAudioDomain.VehicleEngine, value);
	}

	[SettingsUISection(kDeveloperTab, kDeveloperModuleGroup)]
	[SettingsUIButton]
	[SettingsUIButtonGroup(kDeveloperModuleEngineButtonGroup)]
	[SettingsUIDisplayName(overrideValue: "Add Selected Engine")]
	[SettingsUIDescription(overrideValue: "Add the selected local engine file to this package.")]
	public bool IncludeSelectedModuleEngine
	{
		set => SirenChangerMod.IncludeSelectedLocalAudioInModule(DeveloperAudioDomain.VehicleEngine);
	}

	[SettingsUISection(kDeveloperTab, kDeveloperModuleGroup)]
	[SettingsUIButton]
	[SettingsUIButtonGroup(kDeveloperModuleEngineButtonGroup)]
	[SettingsUIDisplayName(overrideValue: "Remove Selected Engine")]
	[SettingsUIDescription(overrideValue: "Remove the selected local engine file from this package.")]
	public bool ExcludeSelectedModuleEngine
	{
		set => SirenChangerMod.ExcludeSelectedLocalAudioFromModule(DeveloperAudioDomain.VehicleEngine);
	}

	[SettingsUISection(kDeveloperTab, kDeveloperModuleGroup)]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetModuleBuilderLocalAmbientOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Local Ambient File")]
	[SettingsUIDescription(overrideValue: "Choose a local ambient file to add or remove from this package.")]
	public string DeveloperModuleLocalAmbientSelection
	{
		get => SirenChangerMod.GetDeveloperModuleLocalAudioSelection(DeveloperAudioDomain.Ambient);
		set => SirenChangerMod.SetDeveloperModuleLocalAudioSelection(DeveloperAudioDomain.Ambient, value);
	}

	[SettingsUISection(kDeveloperTab, kDeveloperModuleGroup)]
	[SettingsUIButton]
	[SettingsUIButtonGroup(kDeveloperModuleAmbientButtonGroup)]
	[SettingsUIDisplayName(overrideValue: "Add Selected Ambient")]
	[SettingsUIDescription(overrideValue: "Add the selected local ambient file to this package.")]
	public bool IncludeSelectedModuleAmbient
	{
		set => SirenChangerMod.IncludeSelectedLocalAudioInModule(DeveloperAudioDomain.Ambient);
	}

	[SettingsUISection(kDeveloperTab, kDeveloperModuleGroup)]
	[SettingsUIButton]
	[SettingsUIButtonGroup(kDeveloperModuleAmbientButtonGroup)]
	[SettingsUIDisplayName(overrideValue: "Remove Selected Ambient")]
	[SettingsUIDescription(overrideValue: "Remove the selected local ambient file from this package.")]
	public bool ExcludeSelectedModuleAmbient
	{
		set => SirenChangerMod.ExcludeSelectedLocalAudioFromModule(DeveloperAudioDomain.Ambient);
	}

	[SettingsUISection(kDeveloperTab, kDeveloperModuleGroup)]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetModuleBuilderLocalTransitAnnouncementOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Local Line Announcement File")]
	[SettingsUIDescription(overrideValue: "Choose a local line announcement file to add or remove from this package.")]
	public string DeveloperModuleLocalTransitAnnouncementSelection
	{
		get => SirenChangerMod.GetDeveloperModuleLocalAudioSelection(DeveloperAudioDomain.TransitAnnouncement);
		set => SirenChangerMod.SetDeveloperModuleLocalAudioSelection(DeveloperAudioDomain.TransitAnnouncement, value);
	}

	[SettingsUISection(kDeveloperTab, kDeveloperModuleGroup)]
	[SettingsUIButton]
	[SettingsUIButtonGroup(kDeveloperModuleTransitButtonGroup)]
	[SettingsUIDisplayName(overrideValue: "Add Selected Line Announcement")]
	[SettingsUIDescription(overrideValue: "Add the selected line announcement file to this package.")]
	public bool IncludeSelectedModuleTransitAnnouncement
	{
		set => SirenChangerMod.IncludeSelectedLocalAudioInModule(DeveloperAudioDomain.TransitAnnouncement);
	}

	[SettingsUISection(kDeveloperTab, kDeveloperModuleGroup)]
	[SettingsUIButton]
	[SettingsUIButtonGroup(kDeveloperModuleTransitButtonGroup)]
	[SettingsUIDisplayName(overrideValue: "Remove Selected Line Announcement")]
	[SettingsUIDescription(overrideValue: "Remove the selected line announcement file from this package.")]
	public bool ExcludeSelectedModuleTransitAnnouncement
	{
		set => SirenChangerMod.ExcludeSelectedLocalAudioFromModule(DeveloperAudioDomain.TransitAnnouncement);
	}

	[SettingsUISection(kDeveloperTab, kDeveloperModuleGroup)]
	[SettingsUIButton]
	[SettingsUIButtonGroup(kDeveloperModuleBulkButtonGroup)]
	[SettingsUIDisplayName(overrideValue: "Select All Local Audio")]
	[SettingsUIDescription(overrideValue: "Add every eligible local audio file to this package.")]
	public bool IncludeAllLocalAudioInModule
	{
		set => SirenChangerMod.IncludeAllLocalAudioInModule();
	}

	[SettingsUISection(kDeveloperTab, kDeveloperModuleGroup)]
	[SettingsUIButton]
	[SettingsUIButtonGroup(kDeveloperModuleBulkButtonGroup)]
	[SettingsUIDisplayName(overrideValue: "Clear Audio Selection")]
	[SettingsUIDescription(overrideValue: "Remove all currently selected local audio files from this package.")]
	public bool ClearLocalAudioModuleInclusions
	{
		set => SirenChangerMod.ClearLocalAudioModuleInclusions();
	}

	[SettingsUISection(kDeveloperTab, kDeveloperModuleGroup)]
	[SettingsUIMultilineText]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Audio Selection Summary")]
	[SettingsUIDescription(overrideValue: "Shows the local files currently selected for package generation.")]
	public string DeveloperModuleIncludedAudioSummary => SirenChangerMod.GetDeveloperModuleInclusionSummaryText();

	[SettingsUISection(kDeveloperTab, kDeveloperModuleGroup)]
	[SettingsUIButton]
	[SettingsUIButtonGroup(kDeveloperModuleBuildButtonGroup)]
	[SettingsUIDisplayName(overrideValue: "Build Local")]
	[SettingsUIDescription(overrideValue: "Build a local code-mod style module with AudioSwitcherModule.json at the root.")]
	public bool CreateModuleFromLocalAudio
	{
		set => SirenChangerMod.CreateDeveloperModuleFromLocalAudio();
	}

	[SettingsUISection(kDeveloperTab, kDeveloperModuleGroup)]
	[SettingsUIButton]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(IsDeveloperModuleUploadControlsDisabled))]
	[SettingsUIButtonGroup(kDeveloperModuleBuildButtonGroup)]
	[SettingsUIConfirmation(overrideConfirmMessageValue: "Build a fresh upload package and upload it to PDX Mods now?")]
	[SettingsUIDisplayName(overrideValue: "Build + Upload")]
	[SettingsUIDescription(overrideValue: "Build a fresh asset package and immediately upload it to PDX Mods using the settings below.")]
	public bool BuildAndUploadAssetModule
	{
		set => SirenChangerMod.BuildAndUploadDeveloperAssetModuleToPdxMods();
	}

	[SettingsUISection(kDeveloperTab, kDeveloperModuleGroup)]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(IsDeveloperModuleUploadControlsDisabled))]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetDeveloperModuleUploadAccessLevelOptions))]
	[SettingsUIDisplayName(overrideValue: "Visibility")]
	[SettingsUIDescription(overrideValue: "Visibility level for the uploaded module on PDX Mods.")]
	public int DeveloperModuleUploadAccessLevel
	{
		get => SirenChangerMod.GetDeveloperModuleUploadAccessLevel();
		set => SirenChangerMod.SetDeveloperModuleUploadAccessLevel(value);
	}

	[SettingsUISection(kDeveloperTab, kDeveloperModuleGroup)]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(IsDeveloperModuleUploadControlsDisabled))]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetDeveloperModuleUploadPublishModeOptions))]
	[SettingsUIDisplayName(overrideValue: "Publish Mode")]
	[SettingsUIDescription(overrideValue: "Create a new listing or update an existing listing.")]
	public int DeveloperModuleUploadPublishMode
	{
		get => SirenChangerMod.GetDeveloperModuleUploadPublishMode();
		set => SirenChangerMod.SetDeveloperModuleUploadPublishMode(value);
	}

	[SettingsUISection(kDeveloperTab, kDeveloperModuleGroup)]
	[SettingsUITextInput]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(IsDeveloperModuleExistingPublishedIdDisabled))]
	[SettingsUIDisplayName(overrideValue: "Existing Mod ID")]
	[SettingsUIDescription(overrideValue: "Required only for Update Existing. Enter the published numeric ID.")]
	public string DeveloperModuleUploadExistingPublishedId
	{
		get => SirenChangerMod.GetDeveloperModuleUploadExistingPublishedIdText();
		set => SirenChangerMod.SetDeveloperModuleUploadExistingPublishedIdText(value);
	}

	[SettingsUISection(kDeveloperTab, kDeveloperModuleGroup)]
	[SettingsUITextInput]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(IsDeveloperModuleUploadControlsDisabled))]
	[SettingsUIDisplayName(overrideValue: "PDX Page Description")]
	[SettingsUIDescription(overrideValue: "Optional description shown on the PDX Mods page. Leave blank to use an auto-generated description.")]
	public string DeveloperModuleUploadDescription
	{
		get => SirenChangerMod.GetDeveloperModuleUploadDescription();
		set => SirenChangerMod.SetDeveloperModuleUploadDescription(value);
	}

	[SettingsUISection(kDeveloperTab, kDeveloperModuleGroup)]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(IsDeveloperModuleUploadControlsDisabled))]
	[SettingsUIDirectoryPicker]
	[SettingsUIDisplayName(overrideValue: "Thumbnail Directory")]
	[SettingsUIDescription(overrideValue: "Optional folder scanned for .png/.jpg/.jpeg files to use as upload thumbnails.")]
	public string DeveloperModuleUploadThumbnailDirectory
	{
		get => SirenChangerMod.GetDeveloperModuleUploadThumbnailDirectory();
		set => SirenChangerMod.SetDeveloperModuleUploadThumbnailDirectory(value);
	}

	[SettingsUISection(kDeveloperTab, kDeveloperModuleGroup)]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(IsDeveloperModuleUploadControlsDisabled))]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetDeveloperModuleUploadThumbnailOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Thumbnail")]
	[SettingsUIDescription(overrideValue: "Optional preview image for upload. Auto uses thumbnail.png or a generated default.")]
	public string DeveloperModuleUploadThumbnailPath
	{
		get => SirenChangerMod.GetDeveloperModuleUploadThumbnailPath();
		set => SirenChangerMod.SetDeveloperModuleUploadThumbnailPath(value);
	}

	[SettingsUISection(kDeveloperTab, kDeveloperModuleGroup)]
	[SettingsUIButton]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(IsDeveloperModuleUploadControlsDisabled))]
	[SettingsUIButtonGroup(kDeveloperModuleUploadButtonGroup)]
	[SettingsUIDisplayName(overrideValue: "Refresh Thumbnails")]
	[SettingsUIDescription(overrideValue: "Rescan the latest upload package and Thumbnail Directory for thumbnail choices.")]
	public bool ScanDeveloperModuleUploadThumbnails
	{
		set => SirenChangerMod.RefreshDeveloperModuleUploadThumbnailOptions();
	}

	[SettingsUISection(kDeveloperTab, kDeveloperModuleGroup)]
	[SettingsUIMultilineText("Media/Misc/Warning.svg")]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Pipeline Status")]
	[SettingsUIDescription(overrideValue: "Shows the active upload backend and mode.")]
	public string DeveloperModuleUploadModeStatus => SirenChangerMod.GetDeveloperModuleUploadModeStatusText();

	[SettingsUISection(kDeveloperTab, kDeveloperModuleGroup)]
	[SettingsUIMultilineText]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Build Status")]
	[SettingsUIDescription(overrideValue: "Result of the last module build action.")]
	public string DeveloperModuleBuildStatus => SirenChangerMod.GetDeveloperModuleStatusText();

	[SettingsUISection(kDeveloperTab, kDeveloperModuleGroup)]
	[SettingsUIMultilineText]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Upload Status")]
	[SettingsUIDescription(overrideValue: "Result of the last upload action.")]
	public string DeveloperModuleUploadStatus => SirenChangerMod.GetDeveloperModuleUploadStatusText();

	// Expand the editable PDX description input to a multiline textbox.
	public override AutomaticSettings.SettingPageData GetPageData(string id, bool addPrefix)
	{
		AutomaticSettings.SettingPageData pageData = base.GetPageData(id, addPrefix);
		for (int tabIndex = 0; tabIndex < pageData.tabs.Count; tabIndex++)
		{
			AutomaticSettings.SettingTabData tab = pageData.tabs[tabIndex];
			if (!string.Equals(tab.id, kDeveloperTab, StringComparison.Ordinal))
			{
				continue;
			}

			foreach (AutomaticSettings.SettingItemData item in tab.items)
			{
				if (!string.Equals(item.property.name, nameof(DeveloperModuleUploadDescription), StringComparison.Ordinal))
				{
					continue;
				}

				if (item.widget is StringInputField inputField)
				{
					// Make the page description editor visibly larger than a standard single-line input.
					inputField.multiline = Math.Max(StringInputField.kDefaultMultilines, 8);
					// Keep UI and backend truncation limits aligned.
					inputField.maxLength = 4000;
				}

				return pageData;
			}

			break;
		}

		return pageData;
	}

	// Warn when the siren domain has no detected runtime SFX entries yet.
	public bool ShowDeveloperSirenWarning()
	{
		return !SirenChangerMod.HasDetectedDeveloperAudio(DeveloperAudioDomain.Siren);
	}

	// Warn when the vehicle-engine domain has no detected runtime SFX entries yet.
	public bool ShowDeveloperEngineWarning()
	{
		return !SirenChangerMod.HasDetectedDeveloperAudio(DeveloperAudioDomain.VehicleEngine);
	}

	// Warn when the ambient domain has no detected runtime SFX entries yet.
	public bool ShowDeveloperAmbientWarning()
	{
		return !SirenChangerMod.HasDetectedDeveloperAudio(DeveloperAudioDomain.Ambient);
	}

	public bool IsDeveloperModuleExistingPublishedIdDisabled()
	{
		return !SirenChangerMod.IsDeveloperModuleUploadUpdateExistingEnabled() ||
			SirenChangerMod.IsDeveloperModuleUploadInProgress();
	}

	public bool IsDeveloperModuleUploadControlsDisabled()
	{
		return SirenChangerMod.IsDeveloperModuleUploadInProgress();
	}

	[Preserve]
	public static DropdownItem<string>[] GetDetectedSirenSoundOptions()
	{
		return SirenChangerMod.BuildDeveloperDetectedDropdown(DeveloperAudioDomain.Siren);
	}

	[Preserve]
	public static DropdownItem<string>[] GetDetectedEngineSoundOptions()
	{
		return SirenChangerMod.BuildDeveloperDetectedDropdown(DeveloperAudioDomain.VehicleEngine);
	}

	[Preserve]
	public static DropdownItem<string>[] GetDetectedAmbientSoundOptions()
	{
		return SirenChangerMod.BuildDeveloperDetectedDropdown(DeveloperAudioDomain.Ambient);
	}

	[Preserve]
	public static DropdownItem<string>[] GetModuleBuilderLocalSirenOptions()
	{
		return SirenChangerMod.BuildDeveloperModuleLocalAudioDropdown(DeveloperAudioDomain.Siren);
	}

	[Preserve]
	public static DropdownItem<string>[] GetModuleBuilderLocalEngineOptions()
	{
		return SirenChangerMod.BuildDeveloperModuleLocalAudioDropdown(DeveloperAudioDomain.VehicleEngine);
	}

	[Preserve]
	public static DropdownItem<string>[] GetModuleBuilderLocalAmbientOptions()
	{
		return SirenChangerMod.BuildDeveloperModuleLocalAudioDropdown(DeveloperAudioDomain.Ambient);
	}

	[Preserve]
	public static DropdownItem<string>[] GetModuleBuilderLocalTransitAnnouncementOptions()
	{
		return SirenChangerMod.BuildDeveloperModuleLocalAudioDropdown(DeveloperAudioDomain.TransitAnnouncement);
	}

	[Preserve]
	public static DropdownItem<string>[] GetDeveloperModuleUploadThumbnailOptions()
	{
		return SirenChangerMod.BuildDeveloperModuleUploadThumbnailDropdown();
	}

	[Preserve]
	public static DropdownItem<int>[] GetDeveloperModuleUploadPublishModeOptions()
	{
		return new[]
		{
			new DropdownItem<int> { value = 0, displayName = "Create New" },
			new DropdownItem<int> { value = 1, displayName = "Update Existing" }
		};
	}

	[Preserve]
	public static DropdownItem<int>[] GetDeveloperModuleUploadAccessLevelOptions()
	{
		return new[]
		{
			new DropdownItem<int> { value = 0, displayName = "Public" },
			new DropdownItem<int> { value = 1, displayName = "Private" },
			new DropdownItem<int> { value = 2, displayName = "Unlisted" }
		};
	}
}
