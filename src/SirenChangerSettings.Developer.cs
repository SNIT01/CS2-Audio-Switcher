using Game.Settings;
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

	public const string kDeveloperModuleGroup = "Module Builder";

	private const string kDeveloperModuleSirenButtonGroup = "Siren Include Actions";

	private const string kDeveloperModuleEngineButtonGroup = "Engine Include Actions";

	private const string kDeveloperModuleAmbientButtonGroup = "Ambient Include Actions";

	private const string kDeveloperModuleBulkButtonGroup = "Module Selection Actions";

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
	[SettingsUIDescription(overrideValue: "Display name written into the generated module manifest.")]
	public string DeveloperModuleDisplayName
	{
		get => SirenChangerMod.GetDeveloperModuleDisplayName();
		set => SirenChangerMod.SetDeveloperModuleDisplayName(value);
	}

	[SettingsUISection(kDeveloperTab, kDeveloperModuleGroup)]
	[SettingsUITextInput]
	[SettingsUIDisplayName(overrideValue: "Module ID")]
	[SettingsUIDescription(overrideValue: "Stable module identifier written into AudioSwitcherModule.json.")]
	public string DeveloperModuleId
	{
		get => SirenChangerMod.GetDeveloperModuleId();
		set => SirenChangerMod.SetDeveloperModuleId(value);
	}

	[SettingsUISection(kDeveloperTab, kDeveloperModuleGroup)]
	[SettingsUIDirectoryPicker]
	[SettingsUIDisplayName(overrideValue: "Export Directory")]
	[SettingsUIDescription(overrideValue: "Directory where the module folder will be created.")]
	public string DeveloperModuleExportDirectory
	{
		get => SirenChangerMod.GetDeveloperModuleExportDirectory();
		set => SirenChangerMod.SetDeveloperModuleExportDirectory(value);
	}

	[SettingsUISection(kDeveloperTab, kDeveloperModuleGroup)]
	[SettingsUITextInput]
	[SettingsUIDisplayName(overrideValue: "Output Folder Name")]
	[SettingsUIDescription(overrideValue: "Folder created under the selected export directory for the generated module.")]
	public string DeveloperModuleFolderName
	{
		get => SirenChangerMod.GetDeveloperModuleFolderName();
		set => SirenChangerMod.SetDeveloperModuleFolderName(value);
	}

	[SettingsUISection(kDeveloperTab, kDeveloperModuleGroup)]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetModuleBuilderLocalSirenOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Local Siren File")]
	[SettingsUIDescription(overrideValue: "Select a local custom siren file to include or exclude from the module.")]
	public string DeveloperModuleLocalSirenSelection
	{
		get => SirenChangerMod.GetDeveloperModuleLocalAudioSelection(DeveloperAudioDomain.Siren);
		set => SirenChangerMod.SetDeveloperModuleLocalAudioSelection(DeveloperAudioDomain.Siren, value);
	}

	[SettingsUISection(kDeveloperTab, kDeveloperModuleGroup)]
	[SettingsUIButton]
	[SettingsUIButtonGroup(kDeveloperModuleSirenButtonGroup)]
	[SettingsUIDisplayName(overrideValue: "Include Selected Siren")]
	[SettingsUIDescription(overrideValue: "Add the selected local siren file to the module include list.")]
	public bool IncludeSelectedModuleSiren
	{
		set => SirenChangerMod.IncludeSelectedLocalAudioInModule(DeveloperAudioDomain.Siren);
	}

	[SettingsUISection(kDeveloperTab, kDeveloperModuleGroup)]
	[SettingsUIButton]
	[SettingsUIButtonGroup(kDeveloperModuleSirenButtonGroup)]
	[SettingsUIDisplayName(overrideValue: "Exclude Selected Siren")]
	[SettingsUIDescription(overrideValue: "Remove the selected local siren file from the module include list.")]
	public bool ExcludeSelectedModuleSiren
	{
		set => SirenChangerMod.ExcludeSelectedLocalAudioFromModule(DeveloperAudioDomain.Siren);
	}

	[SettingsUISection(kDeveloperTab, kDeveloperModuleGroup)]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetModuleBuilderLocalEngineOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Local Engine File")]
	[SettingsUIDescription(overrideValue: "Select a local custom engine file to include or exclude from the module.")]
	public string DeveloperModuleLocalEngineSelection
	{
		get => SirenChangerMod.GetDeveloperModuleLocalAudioSelection(DeveloperAudioDomain.VehicleEngine);
		set => SirenChangerMod.SetDeveloperModuleLocalAudioSelection(DeveloperAudioDomain.VehicleEngine, value);
	}

	[SettingsUISection(kDeveloperTab, kDeveloperModuleGroup)]
	[SettingsUIButton]
	[SettingsUIButtonGroup(kDeveloperModuleEngineButtonGroup)]
	[SettingsUIDisplayName(overrideValue: "Include Selected Engine")]
	[SettingsUIDescription(overrideValue: "Add the selected local engine file to the module include list.")]
	public bool IncludeSelectedModuleEngine
	{
		set => SirenChangerMod.IncludeSelectedLocalAudioInModule(DeveloperAudioDomain.VehicleEngine);
	}

	[SettingsUISection(kDeveloperTab, kDeveloperModuleGroup)]
	[SettingsUIButton]
	[SettingsUIButtonGroup(kDeveloperModuleEngineButtonGroup)]
	[SettingsUIDisplayName(overrideValue: "Exclude Selected Engine")]
	[SettingsUIDescription(overrideValue: "Remove the selected local engine file from the module include list.")]
	public bool ExcludeSelectedModuleEngine
	{
		set => SirenChangerMod.ExcludeSelectedLocalAudioFromModule(DeveloperAudioDomain.VehicleEngine);
	}

	[SettingsUISection(kDeveloperTab, kDeveloperModuleGroup)]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetModuleBuilderLocalAmbientOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Local Ambient File")]
	[SettingsUIDescription(overrideValue: "Select a local custom ambient file to include or exclude from the module.")]
	public string DeveloperModuleLocalAmbientSelection
	{
		get => SirenChangerMod.GetDeveloperModuleLocalAudioSelection(DeveloperAudioDomain.Ambient);
		set => SirenChangerMod.SetDeveloperModuleLocalAudioSelection(DeveloperAudioDomain.Ambient, value);
	}

	[SettingsUISection(kDeveloperTab, kDeveloperModuleGroup)]
	[SettingsUIButton]
	[SettingsUIButtonGroup(kDeveloperModuleAmbientButtonGroup)]
	[SettingsUIDisplayName(overrideValue: "Include Selected Ambient")]
	[SettingsUIDescription(overrideValue: "Add the selected local ambient file to the module include list.")]
	public bool IncludeSelectedModuleAmbient
	{
		set => SirenChangerMod.IncludeSelectedLocalAudioInModule(DeveloperAudioDomain.Ambient);
	}

	[SettingsUISection(kDeveloperTab, kDeveloperModuleGroup)]
	[SettingsUIButton]
	[SettingsUIButtonGroup(kDeveloperModuleAmbientButtonGroup)]
	[SettingsUIDisplayName(overrideValue: "Exclude Selected Ambient")]
	[SettingsUIDescription(overrideValue: "Remove the selected local ambient file from the module include list.")]
	public bool ExcludeSelectedModuleAmbient
	{
		set => SirenChangerMod.ExcludeSelectedLocalAudioFromModule(DeveloperAudioDomain.Ambient);
	}

	[SettingsUISection(kDeveloperTab, kDeveloperModuleGroup)]
	[SettingsUIButton]
	[SettingsUIButtonGroup(kDeveloperModuleBulkButtonGroup)]
	[SettingsUIDisplayName(overrideValue: "Include All Local Audio")]
	[SettingsUIDescription(overrideValue: "Include every eligible local custom audio file in the module.")]
	public bool IncludeAllLocalAudioInModule
	{
		set => SirenChangerMod.IncludeAllLocalAudioInModule();
	}

	[SettingsUISection(kDeveloperTab, kDeveloperModuleGroup)]
	[SettingsUIButton]
	[SettingsUIButtonGroup(kDeveloperModuleBulkButtonGroup)]
	[SettingsUIDisplayName(overrideValue: "Clear Included Audio")]
	[SettingsUIDescription(overrideValue: "Remove all local audio files from the module include list.")]
	public bool ClearLocalAudioModuleInclusions
	{
		set => SirenChangerMod.ClearLocalAudioModuleInclusions();
	}

	[SettingsUISection(kDeveloperTab, kDeveloperModuleGroup)]
	[SettingsUIMultilineText]
	[SettingsUIDisplayName(overrideValue: "Included Local Audio")]
	[SettingsUIDescription(overrideValue: "Shows which local files are currently selected for module export.")]
	public string DeveloperModuleIncludedAudioSummary => SirenChangerMod.GetDeveloperModuleInclusionSummaryText();

	[SettingsUISection(kDeveloperTab, kDeveloperModuleGroup)]
	[SettingsUIButton]
	[SettingsUIDisplayName(overrideValue: "Create Module From Local Audio")]
	[SettingsUIDescription(overrideValue: "Create a standalone module using selected local custom siren, engine, and ambient files plus current SFX profiles.")]
	public bool CreateModuleFromLocalAudio
	{
		set => SirenChangerMod.CreateDeveloperModuleFromLocalAudio();
	}

	[SettingsUISection(kDeveloperTab, kDeveloperModuleGroup)]
	[SettingsUIMultilineText]
	[SettingsUIDisplayName(overrideValue: "Module Build Status")]
	[SettingsUIDescription(overrideValue: "Shows the result of the last local-audio module build action.")]
	public string DeveloperModuleBuildStatus => SirenChangerMod.GetDeveloperModuleStatusText();

	public bool ShowDeveloperSirenWarning()
	{
		return !SirenChangerMod.HasDetectedDeveloperAudio(DeveloperAudioDomain.Siren);
	}

	public bool ShowDeveloperEngineWarning()
	{
		return !SirenChangerMod.HasDetectedDeveloperAudio(DeveloperAudioDomain.VehicleEngine);
	}

	public bool ShowDeveloperAmbientWarning()
	{
		return !SirenChangerMod.HasDetectedDeveloperAudio(DeveloperAudioDomain.Ambient);
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
}
