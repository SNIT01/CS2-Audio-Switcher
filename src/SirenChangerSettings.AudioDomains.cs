using System;
using System.Collections.Generic;
using Game.Settings;
using Game.UI.Widgets;
using UnityEngine;
using UnityEngine.Scripting;

namespace SirenChanger;

// Vehicles, ambient, and building tabs with options and profile editing helpers.
public sealed partial class SirenChangerSettings
{
	public const string kVehicleSetupGroup = "Engine Setup";
	public const string kVehicleDefaultsGroup = "Engine Defaults";
	public const string kVehicleOverrideTargetGroup = "Specific Vehicle Engine Overrides";
	public const string kVehicleFallbackGroup = "Missing Engine Sound Behavior";
	public const string kVehicleProfileGroup = "Engine Profile Editor";

	private const string kVehicleRescanButtonGroup = "Engine Scan Actions";

	public const string kAmbientSetupGroup = "Ambient Setup";
	public const string kAmbientDefaultsGroup = "Ambient Defaults";
	public const string kAmbientTargetGroup = "Specific Ambient Target Overrides";
	public const string kAmbientFallbackGroup = "Missing Ambient Sound Behavior";
	public const string kAmbientProfileGroup = "Ambient Profile Editor";

	private const string kAmbientRescanButtonGroup = "Ambient Scan Actions";

	public const string kBuildingSetupGroup = "Building Setup";
	public const string kBuildingDefaultsGroup = "Building Defaults";
	public const string kBuildingTargetGroup = "Specific Building Overrides";
	public const string kBuildingFallbackGroup = "Missing Building Sound Behavior";
	public const string kBuildingProfileGroup = "Building Profile Editor";

	private const string kBuildingRescanButtonGroup = "Building Scan Actions";

	[SettingsUISection(kVehiclesTab, kVehicleSetupGroup)]
	[SettingsUIDisplayName(overrideValue: "Enable Vehicle Engine Replacement")]
	[SettingsUIDescription(overrideValue: "Enable or disable custom vehicle engine sound replacement.")]
	public bool VehicleEngineEnabled
	{
		get => SirenChangerMod.VehicleEngineConfig.Enabled;
		set => SirenChangerMod.VehicleEngineConfig.Enabled = value;
	}

	[SettingsUISection(kVehiclesTab, kVehicleSetupGroup)]
	[SettingsUIButton]
	[SettingsUIButtonGroup(kVehicleRescanButtonGroup)]
	[SettingsUIDisplayName(overrideValue: "Rescan Custom Engine Files")]
	[SettingsUIDescription(overrideValue: "Rescan the Custom Engines folder and refresh dropdowns.")]
	public bool UpdateCustomVehicleEngines
	{
		set => SirenChangerMod.RefreshCustomVehicleEnginesFromOptions();
	}

	[SettingsUISection(kVehiclesTab, kVehicleSetupGroup)]
	[SettingsUIButton]
	[SettingsUIButtonGroup(kVehicleRescanButtonGroup)]
	[SettingsUIDisplayName(overrideValue: "Rescan Vehicle Engine Prefabs")]
	[SettingsUIDescription(overrideValue: "Scan currently loaded prefabs and refresh vehicle-engine override targets.")]
	public bool UpdateVehicleEnginePrefabs
	{
		set => SirenChangerMod.RefreshVehicleEnginePrefabsFromOptions();
	}

	[SettingsUISection(kVehiclesTab, kVehicleSetupGroup)]
	[SettingsUIMultilineText]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Vehicle Engine Prefab Scan Status")]
	[SettingsUIDescription(overrideValue: "Shows the last vehicle-engine prefab scan result.")]
	[SettingsUIWarning(typeof(SirenChangerSettings), nameof(ShowVehicleEngineScanWarning))]
	public string VehicleEnginePrefabScanStatus => SirenChangerMod.GetVehicleEnginePrefabScanStatusText();

	[SettingsUISection(kVehiclesTab, kVehicleOverrideTargetGroup)]
	[SettingsUIWarning(typeof(SirenChangerSettings), nameof(ShowVehicleEngineOverrideWarning))]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetVehicleEngineTargetOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Vehicle Engine Prefab")]
	[SettingsUIDescription(overrideValue: "Choose a specific vehicle prefab to override.")]
	public string VehicleEngineOverrideTarget
	{
		get => SirenChangerMod.VehicleEngineConfig.TargetSelectionTarget;
		set => SirenChangerMod.SetVehicleEngineTargetSelectionTargetFromOptions(value);
	}

	[SettingsUISection(kVehiclesTab, kVehicleOverrideTargetGroup)]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(IsVehicleEngineOverrideDisabled))]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetVehicleEngineSelectionOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Override Engine Sound")]
	[SettingsUIDescription(overrideValue: "Default means this vehicle uses the default engine selection.")]
	public string VehicleEngineOverrideSelection
	{
		get => SirenChangerMod.GetSelectedVehicleEngineTargetSelectionForOptions();
		set => SirenChangerMod.SetSelectedVehicleEngineTargetSelectionFromOptions(value);
	}

	[SettingsUISection(kVehiclesTab, kVehicleOverrideTargetGroup)]
	[SettingsUIMultilineText]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Engine Override Status")]
	[SettingsUIDescription(overrideValue: "Shows whether the selected vehicle has an override.")]
	public string VehicleEngineOverrideStatus => SirenChangerMod.GetSelectedVehicleEngineOverrideStatusText();

	[SettingsUISection(kVehiclesTab, kVehicleFallbackGroup)]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetFallbackBehaviorOptions))]
	[SettingsUIDisplayName(overrideValue: "If Selected Engine Sound Is Missing")]
	[SettingsUIDescription(overrideValue: "Fallback behavior when the selected engine sound cannot be loaded.")]
	public int MissingVehicleEngineFallbackBehavior
	{
		get => (int)SirenChangerMod.VehicleEngineConfig.MissingSelectionFallbackBehavior;
		set => SirenChangerMod.VehicleEngineConfig.MissingSelectionFallbackBehavior =
			Enum.IsDefined(typeof(SirenFallbackBehavior), value)
				? (SirenFallbackBehavior)value
				: SirenFallbackBehavior.Default;
	}

	[SettingsUISection(kVehiclesTab, kVehicleFallbackGroup)]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(IsVehicleEngineAlternateFallbackSelectionDisabled))]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetVehicleEngineSelectionOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Alternate Engine Sound")]
	[SettingsUIDescription(overrideValue: "Used only when fallback behavior is set to Alternate Custom Sound File.")]
	public string AlternateVehicleEngineFallbackSelection
	{
		get => SirenChangerMod.VehicleEngineConfig.AlternateFallbackSelection;
		set => SirenChangerMod.VehicleEngineConfig.AlternateFallbackSelection = NormalizeDomainSelection(value);
	}

	[SettingsUISection(kVehiclesTab, kVehicleProfileGroup)]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetVehicleEnginePreviewableProfileOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Engine Profile To Edit")]
	[SettingsUIDescription(overrideValue: "Select a custom engine profile to edit, or choose Default to preview the built-in game engine sample.")]
	public string EditVehicleEngineProfile
	{
		get => SirenChangerMod.VehicleEngineConfig.EditProfileSelection;
		set => SirenChangerMod.VehicleEngineConfig.EditProfileSelection = AudioReplacementDomainConfig.NormalizeProfileKey(value);
	}

	[SettingsUISection(kVehiclesTab, kVehicleProfileGroup)]
	[SettingsUIButton]
	[SettingsUIDisplayName(overrideValue: "Preview Selected Engine Sound")]
	[SettingsUIDescription(overrideValue: "Play the selected custom engine profile, or the built-in default sample when Default is selected.")]
	public bool PreviewSelectedVehicleEngineProfile
	{
		set => SirenChangerMod.PreviewSelectedVehicleEngineProfileFromOptions();
	}

	[SettingsUISection(kVehiclesTab, kVehicleProfileGroup)]
	[SettingsUIMultilineText]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Preview Status")]
	[SettingsUIDescription(overrideValue: "Shows the last engine-profile preview result.")]
	public string VehicleEnginePreviewStatus => SirenChangerMod.GetVehicleEnginePreviewStatusText();

	[SettingsUISection(kVehiclesTab, kVehicleProfileGroup)]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(NoVehicleEngineEditableProfile))]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetVehicleEngineCopySourceOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Copy Settings From Engine Profile")]
	[SettingsUIDescription(overrideValue: "Choose a source profile from custom engine sounds or detected engine SFX in this category.")]
	public string CopyFromVehicleEngineProfile
	{
		get => SirenChangerMod.VehicleEngineConfig.CopyFromProfileSelection;
		set => SirenChangerMod.VehicleEngineConfig.CopyFromProfileSelection = AudioReplacementDomainConfig.NormalizeProfileKey(value);
	}

	[SettingsUISection(kVehiclesTab, kVehicleProfileGroup)]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(IsVehicleEngineCopyProfileDisabled))]
	[SettingsUIButton]
	[SettingsUIDisplayName(overrideValue: "Copy Source Into Current Engine Profile")]
	[SettingsUIDescription(overrideValue: "Copy SFX parameters from source profile into the currently edited profile.")]
	public bool CopyVehicleEngineProfileIntoEditable
	{
		set
		{
			if (TryGetVehicleEngineEditableProfile(out SirenSfxProfile target) &&
				TryGetVehicleEngineCopySourceProfile(out SirenSfxProfile source))
			{
				CopyProfileValues(target, source);
				SirenChangerMod.NotifyRuntimeConfigChanged(saveToDisk: true);
			}
		}
	}

	[SettingsUISection(kVehiclesTab, kVehicleProfileGroup)]
	[SettingsUIButton]
	[SettingsUIDisplayName(overrideValue: "Reset Current Engine Profile To Template")]
	[SettingsUIDescription(overrideValue: "Reset the selected profile to template values captured from detected engine SFX.")]
	public bool ResetEditableVehicleEngineProfileToTemplate
	{
		set
		{
			if (TryGetVehicleEngineEditableProfile(out SirenSfxProfile target))
			{
				CopyProfileValues(target, SirenChangerMod.VehicleEngineProfileTemplate);
				SirenChangerMod.NotifyRuntimeConfigChanged(saveToDisk: true);
			}
		}
	}

	[SettingsUISection(kVehiclesTab, kVehicleProfileGroup)]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(NoVehicleEngineEditableProfile))]
	[SettingsUISlider(min = 0f, max = 100f, step = 1f, unit = "percentageSingleFraction", scalarMultiplier = 100f, updateOnDragEnd = true)]
	[SettingsUIDisplayName(overrideValue: "Volume")]
	[SettingsUIDescription(overrideValue: "Engine profile volume.")]
	public float VehicleEngineProfileVolume
	{
		get => GetVehicleEngineEditableProfile().Volume;
		set => SetVehicleEngineProfileValue(profile => profile.Volume = value, clamp: true);
	}

	[SettingsUISection(kVehiclesTab, kVehicleProfileGroup)]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(NoVehicleEngineEditableProfile))]
	[SettingsUISlider(min = -3f, max = 3f, step = 0.05f, unit = "floatSingleFraction", updateOnDragEnd = true)]
	[SettingsUIDisplayName(overrideValue: "Pitch")]
	[SettingsUIDescription(overrideValue: "Engine profile pitch.")]
	public float VehicleEngineProfilePitch
	{
		get => GetVehicleEngineEditableProfile().Pitch;
		set => SetVehicleEngineProfileValue(profile => profile.Pitch = value, clamp: true);
	}

	[SettingsUISection(kVehiclesTab, kVehicleProfileGroup)]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(NoVehicleEngineEditableProfile))]
	[SettingsUISlider(min = 0f, max = 100f, step = 1f, unit = "percentageSingleFraction", scalarMultiplier = 100f, updateOnDragEnd = true)]
	[SettingsUIDisplayName(overrideValue: "Spatial Blend")]
	[SettingsUIDescription(overrideValue: "Engine profile spatial blend.")]
	public float VehicleEngineProfileSpatialBlend
	{
		get => GetVehicleEngineEditableProfile().SpatialBlend;
		set => SetVehicleEngineProfileValue(profile => profile.SpatialBlend = value, clamp: true);
	}

	[SettingsUISection(kVehiclesTab, kVehicleProfileGroup)]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(NoVehicleEngineEditableProfile))]
	[SettingsUISlider(min = 0f, max = 100f, step = 1f, unit = "percentageSingleFraction", scalarMultiplier = 100f, updateOnDragEnd = true)]
	[SettingsUIDisplayName(overrideValue: "Doppler Level")]
	[SettingsUIDescription(overrideValue: "Controls doppler effect intensity for moving engine sources.")]
	public float VehicleEngineProfileDoppler
	{
		get => GetVehicleEngineEditableProfile().Doppler;
		set => SetVehicleEngineProfileValue(profile => profile.Doppler = value, clamp: true);
	}

	[SettingsUISection(kVehiclesTab, kVehicleProfileGroup)]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(NoVehicleEngineEditableProfile))]
	[SettingsUISlider(min = 0f, max = 360f, step = 1f, unit = "integer", updateOnDragEnd = true)]
	[SettingsUIDisplayName(overrideValue: "Stereo Spread")]
	[SettingsUIDescription(overrideValue: "Sets how widely stereo channels are spread in 3D space.")]
	public float VehicleEngineProfileSpread
	{
		get => GetVehicleEngineEditableProfile().Spread;
		set => SetVehicleEngineProfileValue(profile => profile.Spread = value, clamp: true);
	}

	[SettingsUISection(kVehiclesTab, kVehicleProfileGroup)]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(NoVehicleEngineEditableProfile))]
	[SettingsUISlider(min = 0f, max = 100f, step = 0.5f, unit = "floatSingleFraction", updateOnDragEnd = true)]
	[SettingsUIDisplayName(overrideValue: "Min Distance")]
	[SettingsUIDescription(overrideValue: "Distance where volume starts attenuating based on rolloff mode.")]
	public float VehicleEngineProfileMinDistance
	{
		get => GetVehicleEngineEditableProfile().MinDistance;
		set => SetVehicleEngineProfileValue(profile => profile.MinDistance = value, clamp: true);
	}

	[SettingsUISection(kVehiclesTab, kVehicleProfileGroup)]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(NoVehicleEngineEditableProfile))]
	[SettingsUISlider(min = 1f, max = 500f, step = 1f, unit = "floatSingleFraction", updateOnDragEnd = true)]
	[SettingsUIDisplayName(overrideValue: "Max Distance")]
	[SettingsUIDescription(overrideValue: "Distance where the engine sound reaches minimum audible level.")]
	public float VehicleEngineProfileMaxDistance
	{
		get => GetVehicleEngineEditableProfile().MaxDistance;
		set => SetVehicleEngineProfileValue(profile => profile.MaxDistance = value, clamp: true);
	}
	[SettingsUISection(kVehiclesTab, kVehicleProfileGroup)]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(NoVehicleEngineEditableProfile))]
	[SettingsUIDisplayName(overrideValue: "Loop")]
	[SettingsUIDescription(overrideValue: "When enabled, this engine profile loops.")]
	public bool VehicleEngineProfileLoop
	{
		get => GetVehicleEngineEditableProfile().Loop;
		set => SetVehicleEngineProfileValue(profile => profile.Loop = value, clamp: false);
	}

	[SettingsUISection(kVehiclesTab, kVehicleProfileGroup)]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(NoVehicleEngineEditableProfile))]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetRolloffModeOptions))]
	[SettingsUIDisplayName(overrideValue: "Rolloff Mode")]
	[SettingsUIDescription(overrideValue: "Selects how volume attenuates over distance.")]
	public int VehicleEngineProfileRolloffMode
	{
		get => (int)GetVehicleEngineEditableProfile().RolloffMode;
		set => SetVehicleEngineProfileValue(
			profile => profile.RolloffMode = Enum.IsDefined(typeof(AudioRolloffMode), value)
				? (AudioRolloffMode)value
				: AudioRolloffMode.Linear,
			clamp: false);
	}

	[SettingsUISection(kVehiclesTab, kVehicleProfileGroup)]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(NoVehicleEngineEditableProfile))]
	[SettingsUIDisplayName(overrideValue: "Random Start Time")]
	[SettingsUIDescription(overrideValue: "Start playback from a random clip position to reduce repetitive sync.")]
	public bool VehicleEngineProfileRandomStartTime
	{
		get => GetVehicleEngineEditableProfile().RandomStartTime;
		set => SetVehicleEngineProfileValue(profile => profile.RandomStartTime = value, clamp: false);
	}

	[SettingsUISection(kVehiclesTab, kVehicleProfileGroup)]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(NoVehicleEngineEditableProfile))]
	[SettingsUISlider(min = 0f, max = 10f, step = 0.05f, unit = "floatSingleFraction", updateOnDragEnd = true)]
	[SettingsUIDisplayName(overrideValue: "Fade In Seconds")]
	[SettingsUIDescription(overrideValue: "Time to ramp from silence to full volume when playback starts.")]
	public float VehicleEngineProfileFadeInSeconds
	{
		get => GetVehicleEngineEditableProfile().FadeInSeconds;
		set => SetVehicleEngineProfileValue(profile => profile.FadeInSeconds = value, clamp: true);
	}

	[SettingsUISection(kVehiclesTab, kVehicleProfileGroup)]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(NoVehicleEngineEditableProfile))]
	[SettingsUISlider(min = 0f, max = 10f, step = 0.05f, unit = "floatSingleFraction", updateOnDragEnd = true)]
	[SettingsUIDisplayName(overrideValue: "Fade Out Seconds")]
	[SettingsUIDescription(overrideValue: "Time to ramp from current volume to silence when playback stops.")]
	public float VehicleEngineProfileFadeOutSeconds
	{
		get => GetVehicleEngineEditableProfile().FadeOutSeconds;
		set => SetVehicleEngineProfileValue(profile => profile.FadeOutSeconds = value, clamp: true);
	}
	[SettingsUISection(kVehiclesTab, kVehicleSetupGroup)]
	[SettingsUIMultilineText]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Custom Engine File Scan Status")]
	[SettingsUIDescription(overrideValue: "Shows the latest custom engine folder scan summary and changed files.")]
	[SettingsUIWarning(typeof(SirenChangerSettings), nameof(ShowVehicleEngineCatalogWarning))]
	public string VehicleEngineCatalogScanStatus => SirenChangerMod.GetVehicleEngineCatalogScanStatusText();

	[SettingsUISection(kAmbientTab, kAmbientSetupGroup)]
	[SettingsUIDisplayName(overrideValue: "Enable Ambient Replacement")]
	[SettingsUIDescription(overrideValue: "Enable or disable custom ambient sound replacement.")]
	public bool AmbientEnabled
	{
		get => SirenChangerMod.AmbientConfig.Enabled;
		set => SirenChangerMod.AmbientConfig.Enabled = value;
	}

	[SettingsUISection(kAmbientTab, kAmbientSetupGroup)]
	[SettingsUIDisplayName(overrideValue: "Mute Ambient Targets")]
	[SettingsUIDescription(overrideValue: "Mute all detected ambient targets while keeping assignments intact.")]
	public bool AmbientMuteAllTargets
	{
		get => SirenChangerMod.AmbientConfig.MuteAllTargets;
		set => SirenChangerMod.AmbientConfig.MuteAllTargets = value;
	}

	[SettingsUISection(kAmbientTab, kAmbientSetupGroup)]
	[SettingsUIButton]
	[SettingsUIButtonGroup(kAmbientRescanButtonGroup)]
	[SettingsUIDisplayName(overrideValue: "Rescan Custom Ambient Files")]
	[SettingsUIDescription(overrideValue: "Rescan the Custom Ambient folder and refresh dropdowns.")]
	public bool UpdateCustomAmbient
	{
		set => SirenChangerMod.RefreshCustomAmbientFromOptions();
	}

	[SettingsUISection(kAmbientTab, kAmbientSetupGroup)]
	[SettingsUIButton]
	[SettingsUIButtonGroup(kAmbientRescanButtonGroup)]
	[SettingsUIDisplayName(overrideValue: "Rescan Ambient Targets")]
	[SettingsUIDescription(overrideValue: "Scan currently loaded prefabs and refresh ambient override targets.")]
	public bool UpdateAmbientTargets
	{
		set => SirenChangerMod.RefreshAmbientTargetsFromOptions();
	}

	[SettingsUISection(kAmbientTab, kAmbientSetupGroup)]
	[SettingsUIMultilineText]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Ambient Target Prefab Scan Status")]
	[SettingsUIDescription(overrideValue: "Shows the last ambient target scan result.")]
	[SettingsUIWarning(typeof(SirenChangerSettings), nameof(ShowAmbientTargetScanWarning))]
	public string AmbientTargetScanStatus => SirenChangerMod.GetAmbientTargetScanStatusText();

	[SettingsUISection(kAmbientTab, kAmbientTargetGroup)]
	[SettingsUIWarning(typeof(SirenChangerSettings), nameof(ShowAmbientOverrideWarning))]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetAmbientTargetOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Ambient Target Prefab")]
	[SettingsUIDescription(overrideValue: "Choose a specific ambient target prefab to override.")]
	public string AmbientOverrideTarget
	{
		get => SirenChangerMod.AmbientConfig.TargetSelectionTarget;
		set => SirenChangerMod.SetAmbientTargetSelectionTargetFromOptions(value);
	}

	[SettingsUISection(kAmbientTab, kAmbientTargetGroup)]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(IsAmbientOverrideDisabled))]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetAmbientSelectionOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Override Ambient Sound")]
	[SettingsUIDescription(overrideValue: "Default means this target uses the ambient default selection.")]
	public string AmbientOverrideSelection
	{
		get => SirenChangerMod.GetSelectedAmbientTargetSelectionForOptions();
		set => SirenChangerMod.SetSelectedAmbientTargetSelectionFromOptions(value);
	}

	[SettingsUISection(kAmbientTab, kAmbientTargetGroup)]
	[SettingsUIMultilineText]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Ambient Override Status")]
	[SettingsUIDescription(overrideValue: "Shows whether the selected ambient target has an override.")]
	public string AmbientOverrideStatus => SirenChangerMod.GetSelectedAmbientOverrideStatusText();

	[SettingsUISection(kAmbientTab, kAmbientFallbackGroup)]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetFallbackBehaviorOptions))]
	[SettingsUIDisplayName(overrideValue: "If Selected Ambient Sound Is Missing")]
	[SettingsUIDescription(overrideValue: "Fallback behavior when the selected ambient sound cannot be loaded.")]
	public int MissingAmbientFallbackBehavior
	{
		get => (int)SirenChangerMod.AmbientConfig.MissingSelectionFallbackBehavior;
		set => SirenChangerMod.AmbientConfig.MissingSelectionFallbackBehavior =
			Enum.IsDefined(typeof(SirenFallbackBehavior), value)
				? (SirenFallbackBehavior)value
				: SirenFallbackBehavior.Default;
	}

	[SettingsUISection(kAmbientTab, kAmbientFallbackGroup)]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(IsAmbientAlternateFallbackSelectionDisabled))]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetAmbientSelectionOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Alternate Ambient Sound")]
	[SettingsUIDescription(overrideValue: "Used only when fallback behavior is set to Alternate Custom Sound File.")]
	public string AlternateAmbientFallbackSelection
	{
		get => SirenChangerMod.AmbientConfig.AlternateFallbackSelection;
		set => SirenChangerMod.AmbientConfig.AlternateFallbackSelection = NormalizeDomainSelection(value);
	}

	[SettingsUISection(kAmbientTab, kAmbientProfileGroup)]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetAmbientPreviewableProfileOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Ambient Profile To Edit")]
	[SettingsUIDescription(overrideValue: "Select a custom ambient profile to edit, or choose Default to preview the built-in game ambient sample.")]
	public string EditAmbientProfile
	{
		get => SirenChangerMod.AmbientConfig.EditProfileSelection;
		set => SirenChangerMod.AmbientConfig.EditProfileSelection = AudioReplacementDomainConfig.NormalizeProfileKey(value);
	}

	[SettingsUISection(kAmbientTab, kAmbientProfileGroup)]
	[SettingsUIButton]
	[SettingsUIDisplayName(overrideValue: "Preview Selected Ambient Sound")]
	[SettingsUIDescription(overrideValue: "Play the selected custom ambient profile, or the built-in default sample when Default is selected.")]
	public bool PreviewSelectedAmbientProfile
	{
		set => SirenChangerMod.PreviewSelectedAmbientProfileFromOptions();
	}

	[SettingsUISection(kAmbientTab, kAmbientProfileGroup)]
	[SettingsUIMultilineText]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Preview Status")]
	[SettingsUIDescription(overrideValue: "Shows the last ambient-profile preview result.")]
	public string AmbientPreviewStatus => SirenChangerMod.GetAmbientPreviewStatusText();

	[SettingsUISection(kAmbientTab, kAmbientProfileGroup)]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(NoAmbientEditableProfile))]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetAmbientCopySourceOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Copy Settings From Ambient Profile")]
	[SettingsUIDescription(overrideValue: "Choose a source profile from custom ambient sounds or detected ambient SFX in this category.")]
	public string CopyFromAmbientProfile
	{
		get => SirenChangerMod.AmbientConfig.CopyFromProfileSelection;
		set => SirenChangerMod.AmbientConfig.CopyFromProfileSelection = AudioReplacementDomainConfig.NormalizeProfileKey(value);
	}

	[SettingsUISection(kAmbientTab, kAmbientProfileGroup)]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(IsAmbientCopyProfileDisabled))]
	[SettingsUIButton]
	[SettingsUIDisplayName(overrideValue: "Copy Source Into Current Ambient Profile")]
	[SettingsUIDescription(overrideValue: "Copy SFX parameters from source profile into the currently edited profile.")]
	public bool CopyAmbientProfileIntoEditable
	{
		set
		{
			if (TryGetAmbientEditableProfile(out SirenSfxProfile target) &&
				TryGetAmbientCopySourceProfile(out SirenSfxProfile source))
			{
				CopyProfileValues(target, source);
				SirenChangerMod.NotifyRuntimeConfigChanged(saveToDisk: true);
			}
		}
	}

	[SettingsUISection(kAmbientTab, kAmbientProfileGroup)]
	[SettingsUIButton]
	[SettingsUIDisplayName(overrideValue: "Reset Current Ambient Profile To Template")]
	[SettingsUIDescription(overrideValue: "Reset the selected profile to template values captured from detected ambient SFX.")]
	public bool ResetEditableAmbientProfileToTemplate
	{
		set
		{
			if (TryGetAmbientEditableProfile(out SirenSfxProfile target))
			{
				CopyProfileValues(target, SirenChangerMod.AmbientProfileTemplate);
				SirenChangerMod.NotifyRuntimeConfigChanged(saveToDisk: true);
			}
		}
	}

	[SettingsUISection(kAmbientTab, kAmbientProfileGroup)]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(NoAmbientEditableProfile))]
	[SettingsUISlider(min = 0f, max = 100f, step = 1f, unit = "percentageSingleFraction", scalarMultiplier = 100f, updateOnDragEnd = true)]
	[SettingsUIDisplayName(overrideValue: "Volume")]
	[SettingsUIDescription(overrideValue: "Ambient profile volume.")]
	public float AmbientProfileVolume
	{
		get => GetAmbientEditableProfile().Volume;
		set => SetAmbientProfileValue(profile => profile.Volume = value, clamp: true);
	}

	[SettingsUISection(kAmbientTab, kAmbientProfileGroup)]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(NoAmbientEditableProfile))]
	[SettingsUISlider(min = -3f, max = 3f, step = 0.05f, unit = "floatSingleFraction", updateOnDragEnd = true)]
	[SettingsUIDisplayName(overrideValue: "Pitch")]
	[SettingsUIDescription(overrideValue: "Ambient profile pitch.")]
	public float AmbientProfilePitch
	{
		get => GetAmbientEditableProfile().Pitch;
		set => SetAmbientProfileValue(profile => profile.Pitch = value, clamp: true);
	}

	[SettingsUISection(kAmbientTab, kAmbientProfileGroup)]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(NoAmbientEditableProfile))]
	[SettingsUISlider(min = 0f, max = 100f, step = 1f, unit = "percentageSingleFraction", scalarMultiplier = 100f, updateOnDragEnd = true)]
	[SettingsUIDisplayName(overrideValue: "Spatial Blend")]
	[SettingsUIDescription(overrideValue: "Ambient profile spatial blend.")]
	public float AmbientProfileSpatialBlend
	{
		get => GetAmbientEditableProfile().SpatialBlend;
		set => SetAmbientProfileValue(profile => profile.SpatialBlend = value, clamp: true);
	}

	[SettingsUISection(kAmbientTab, kAmbientProfileGroup)]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(NoAmbientEditableProfile))]
	[SettingsUISlider(min = 0f, max = 100f, step = 1f, unit = "percentageSingleFraction", scalarMultiplier = 100f, updateOnDragEnd = true)]
	[SettingsUIDisplayName(overrideValue: "Doppler Level")]
	[SettingsUIDescription(overrideValue: "Controls doppler effect intensity for moving ambient sources.")]
	public float AmbientProfileDoppler
	{
		get => GetAmbientEditableProfile().Doppler;
		set => SetAmbientProfileValue(profile => profile.Doppler = value, clamp: true);
	}

	[SettingsUISection(kAmbientTab, kAmbientProfileGroup)]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(NoAmbientEditableProfile))]
	[SettingsUISlider(min = 0f, max = 360f, step = 1f, unit = "integer", updateOnDragEnd = true)]
	[SettingsUIDisplayName(overrideValue: "Stereo Spread")]
	[SettingsUIDescription(overrideValue: "Sets how widely stereo channels are spread in 3D space.")]
	public float AmbientProfileSpread
	{
		get => GetAmbientEditableProfile().Spread;
		set => SetAmbientProfileValue(profile => profile.Spread = value, clamp: true);
	}

	[SettingsUISection(kAmbientTab, kAmbientProfileGroup)]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(NoAmbientEditableProfile))]
	[SettingsUISlider(min = 0f, max = 100f, step = 0.5f, unit = "floatSingleFraction", updateOnDragEnd = true)]
	[SettingsUIDisplayName(overrideValue: "Min Distance")]
	[SettingsUIDescription(overrideValue: "Distance where volume starts attenuating based on rolloff mode.")]
	public float AmbientProfileMinDistance
	{
		get => GetAmbientEditableProfile().MinDistance;
		set => SetAmbientProfileValue(profile => profile.MinDistance = value, clamp: true);
	}

	[SettingsUISection(kAmbientTab, kAmbientProfileGroup)]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(NoAmbientEditableProfile))]
	[SettingsUISlider(min = 1f, max = 500f, step = 1f, unit = "floatSingleFraction", updateOnDragEnd = true)]
	[SettingsUIDisplayName(overrideValue: "Max Distance")]
	[SettingsUIDescription(overrideValue: "Distance where the ambient sound reaches minimum audible level.")]
	public float AmbientProfileMaxDistance
	{
		get => GetAmbientEditableProfile().MaxDistance;
		set => SetAmbientProfileValue(profile => profile.MaxDistance = value, clamp: true);
	}
	[SettingsUISection(kAmbientTab, kAmbientProfileGroup)]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(NoAmbientEditableProfile))]
	[SettingsUIDisplayName(overrideValue: "Loop")]
	[SettingsUIDescription(overrideValue: "When enabled, this ambient profile loops.")]
	public bool AmbientProfileLoop
	{
		get => GetAmbientEditableProfile().Loop;
		set => SetAmbientProfileValue(profile => profile.Loop = value, clamp: false);
	}

	[SettingsUISection(kAmbientTab, kAmbientProfileGroup)]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(NoAmbientEditableProfile))]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetRolloffModeOptions))]
	[SettingsUIDisplayName(overrideValue: "Rolloff Mode")]
	[SettingsUIDescription(overrideValue: "Selects how volume attenuates over distance.")]
	public int AmbientProfileRolloffMode
	{
		get => (int)GetAmbientEditableProfile().RolloffMode;
		set => SetAmbientProfileValue(
			profile => profile.RolloffMode = Enum.IsDefined(typeof(AudioRolloffMode), value)
				? (AudioRolloffMode)value
				: AudioRolloffMode.Linear,
			clamp: false);
	}

	[SettingsUISection(kAmbientTab, kAmbientProfileGroup)]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(NoAmbientEditableProfile))]
	[SettingsUIDisplayName(overrideValue: "Random Start Time")]
	[SettingsUIDescription(overrideValue: "Start playback from a random clip position to reduce repetitive sync.")]
	public bool AmbientProfileRandomStartTime
	{
		get => GetAmbientEditableProfile().RandomStartTime;
		set => SetAmbientProfileValue(profile => profile.RandomStartTime = value, clamp: false);
	}

	[SettingsUISection(kAmbientTab, kAmbientProfileGroup)]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(NoAmbientEditableProfile))]
	[SettingsUISlider(min = 0f, max = 10f, step = 0.05f, unit = "floatSingleFraction", updateOnDragEnd = true)]
	[SettingsUIDisplayName(overrideValue: "Fade In Seconds")]
	[SettingsUIDescription(overrideValue: "Time to ramp from silence to full volume when playback starts.")]
	public float AmbientProfileFadeInSeconds
	{
		get => GetAmbientEditableProfile().FadeInSeconds;
		set => SetAmbientProfileValue(profile => profile.FadeInSeconds = value, clamp: true);
	}

	[SettingsUISection(kAmbientTab, kAmbientProfileGroup)]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(NoAmbientEditableProfile))]
	[SettingsUISlider(min = 0f, max = 10f, step = 0.05f, unit = "floatSingleFraction", updateOnDragEnd = true)]
	[SettingsUIDisplayName(overrideValue: "Fade Out Seconds")]
	[SettingsUIDescription(overrideValue: "Time to ramp from current volume to silence when playback stops.")]
	public float AmbientProfileFadeOutSeconds
	{
		get => GetAmbientEditableProfile().FadeOutSeconds;
		set => SetAmbientProfileValue(profile => profile.FadeOutSeconds = value, clamp: true);
	}
	[SettingsUISection(kAmbientTab, kAmbientSetupGroup)]
	[SettingsUIMultilineText]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Custom Ambient File Scan Status")]
	[SettingsUIDescription(overrideValue: "Shows the latest custom ambient folder scan summary and changed files.")]
	[SettingsUIWarning(typeof(SirenChangerSettings), nameof(ShowAmbientCatalogWarning))]
	public string AmbientCatalogScanStatus => SirenChangerMod.GetAmbientCatalogScanStatusText();

	[SettingsUISection(kBuildingsTab, kBuildingSetupGroup)]
	[SettingsUIDisplayName(overrideValue: "Enable Building Replacement")]
	[SettingsUIDescription(overrideValue: "Enable or disable custom building sound replacement.")]
	public bool BuildingEnabled
	{
		get => SirenChangerMod.BuildingConfig.Enabled;
		set => SirenChangerMod.BuildingConfig.Enabled = value;
	}

	[SettingsUISection(kBuildingsTab, kBuildingSetupGroup)]
	[SettingsUIDisplayName(overrideValue: "Mute Building Targets")]
	[SettingsUIDescription(overrideValue: "Mute all detected building targets while keeping assignments intact.")]
	public bool BuildingMuteAllTargets
	{
		get => SirenChangerMod.BuildingConfig.MuteAllTargets;
		set => SirenChangerMod.BuildingConfig.MuteAllTargets = value;
	}

	[SettingsUISection(kBuildingsTab, kBuildingSetupGroup)]
	[SettingsUIButton]
	[SettingsUIButtonGroup(kBuildingRescanButtonGroup)]
	[SettingsUIDisplayName(overrideValue: "Rescan Custom Building Files")]
	[SettingsUIDescription(overrideValue: "Rescan the Custom Buildings folder and refresh dropdowns.")]
	public bool UpdateCustomBuildings
	{
		set => SirenChangerMod.RefreshCustomBuildingsFromOptions();
	}

	[SettingsUISection(kBuildingsTab, kBuildingSetupGroup)]
	[SettingsUIButton]
	[SettingsUIButtonGroup(kBuildingRescanButtonGroup)]
	[SettingsUIDisplayName(overrideValue: "Rescan Building Targets")]
	[SettingsUIDescription(overrideValue: "Scan currently loaded prefabs and refresh building override targets.")]
	public bool UpdateBuildingTargets
	{
		set => SirenChangerMod.RefreshBuildingTargetsFromOptions();
	}

	[SettingsUISection(kBuildingsTab, kBuildingSetupGroup)]
	[SettingsUIMultilineText]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Building Target Prefab Scan Status")]
	[SettingsUIDescription(overrideValue: "Shows the last building target scan result.")]
	[SettingsUIWarning(typeof(SirenChangerSettings), nameof(ShowBuildingTargetScanWarning))]
	public string BuildingTargetScanStatus => SirenChangerMod.GetBuildingTargetScanStatusText();

	[SettingsUISection(kBuildingsTab, kBuildingTargetGroup)]
	[SettingsUIWarning(typeof(SirenChangerSettings), nameof(ShowBuildingOverrideWarning))]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetBuildingTargetOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Building Target Prefab")]
	[SettingsUIDescription(overrideValue: "Choose a specific building prefab to override.")]
	public string BuildingOverrideTarget
	{
		get => SirenChangerMod.BuildingConfig.TargetSelectionTarget;
		set => SirenChangerMod.SetBuildingTargetSelectionTargetFromOptions(value);
	}

	[SettingsUISection(kBuildingsTab, kBuildingTargetGroup)]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(IsBuildingOverrideDisabled))]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetBuildingSelectionOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Override Building Sound")]
	[SettingsUIDescription(overrideValue: "Default means this building uses the building default selection.")]
	public string BuildingOverrideSelection
	{
		get => SirenChangerMod.GetSelectedBuildingTargetSelectionForOptions();
		set => SirenChangerMod.SetSelectedBuildingTargetSelectionFromOptions(value);
	}

	[SettingsUISection(kBuildingsTab, kBuildingTargetGroup)]
	[SettingsUIMultilineText]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Building Override Status")]
	[SettingsUIDescription(overrideValue: "Shows whether the selected building target has an override.")]
	public string BuildingOverrideStatus => SirenChangerMod.GetSelectedBuildingOverrideStatusText();

	[SettingsUISection(kBuildingsTab, kBuildingFallbackGroup)]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetFallbackBehaviorOptions))]
	[SettingsUIDisplayName(overrideValue: "If Selected Building Sound Is Missing")]
	[SettingsUIDescription(overrideValue: "Fallback behavior when the selected building sound cannot be loaded.")]
	public int MissingBuildingFallbackBehavior
	{
		get => (int)SirenChangerMod.BuildingConfig.MissingSelectionFallbackBehavior;
		set => SirenChangerMod.BuildingConfig.MissingSelectionFallbackBehavior =
			Enum.IsDefined(typeof(SirenFallbackBehavior), value)
				? (SirenFallbackBehavior)value
				: SirenFallbackBehavior.Default;
	}

	[SettingsUISection(kBuildingsTab, kBuildingFallbackGroup)]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(IsBuildingAlternateFallbackSelectionDisabled))]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetBuildingSelectionOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Alternate Building Sound")]
	[SettingsUIDescription(overrideValue: "Used only when fallback behavior is set to Alternate Custom Sound File.")]
	public string AlternateBuildingFallbackSelection
	{
		get => SirenChangerMod.BuildingConfig.AlternateFallbackSelection;
		set => SirenChangerMod.BuildingConfig.AlternateFallbackSelection = NormalizeDomainSelection(value);
	}

	[SettingsUISection(kBuildingsTab, kBuildingProfileGroup)]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetBuildingPreviewableProfileOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Building Profile To Edit")]
	[SettingsUIDescription(overrideValue: "Select a custom building profile to edit, or choose Default to preview the built-in game building sample.")]
	public string EditBuildingProfile
	{
		get => SirenChangerMod.BuildingConfig.EditProfileSelection;
		set => SirenChangerMod.BuildingConfig.EditProfileSelection = AudioReplacementDomainConfig.NormalizeProfileKey(value);
	}

	[SettingsUISection(kBuildingsTab, kBuildingProfileGroup)]
	[SettingsUIButton]
	[SettingsUIDisplayName(overrideValue: "Preview Selected Building Sound")]
	[SettingsUIDescription(overrideValue: "Play the selected custom building profile, or the built-in default sample when Default is selected.")]
	public bool PreviewSelectedBuildingProfile
	{
		set => SirenChangerMod.PreviewSelectedBuildingProfileFromOptions();
	}

	[SettingsUISection(kBuildingsTab, kBuildingProfileGroup)]
	[SettingsUIMultilineText]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Preview Status")]
	[SettingsUIDescription(overrideValue: "Shows the last building-profile preview result.")]
	public string BuildingPreviewStatus => SirenChangerMod.GetBuildingPreviewStatusText();

	[SettingsUISection(kBuildingsTab, kBuildingProfileGroup)]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(NoBuildingEditableProfile))]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetBuildingCopySourceOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Copy Settings From Building Profile")]
	[SettingsUIDescription(overrideValue: "Choose a source profile from custom building sounds.")]
	public string CopyFromBuildingProfile
	{
		get => SirenChangerMod.BuildingConfig.CopyFromProfileSelection;
		set => SirenChangerMod.BuildingConfig.CopyFromProfileSelection = AudioReplacementDomainConfig.NormalizeProfileKey(value);
	}

	[SettingsUISection(kBuildingsTab, kBuildingProfileGroup)]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(IsBuildingCopyProfileDisabled))]
	[SettingsUIButton]
	[SettingsUIDisplayName(overrideValue: "Copy Source Into Current Building Profile")]
	[SettingsUIDescription(overrideValue: "Copy SFX parameters from source profile into the currently edited profile.")]
	public bool CopyBuildingProfileIntoEditable
	{
		set
		{
			if (TryGetBuildingEditableProfile(out SirenSfxProfile target) &&
				TryGetBuildingCopySourceProfile(out SirenSfxProfile source))
			{
				CopyProfileValues(target, source);
				SirenChangerMod.NotifyRuntimeConfigChanged(saveToDisk: true);
			}
		}
	}

	[SettingsUISection(kBuildingsTab, kBuildingProfileGroup)]
	[SettingsUIButton]
	[SettingsUIDisplayName(overrideValue: "Reset Current Building Profile To Template")]
	[SettingsUIDescription(overrideValue: "Reset the selected profile to template values captured from detected building SFX.")]
	public bool ResetEditableBuildingProfileToTemplate
	{
		set
		{
			if (TryGetBuildingEditableProfile(out SirenSfxProfile target))
			{
				CopyProfileValues(target, SirenChangerMod.BuildingProfileTemplate);
				SirenChangerMod.NotifyRuntimeConfigChanged(saveToDisk: true);
			}
		}
	}

	[SettingsUISection(kBuildingsTab, kBuildingProfileGroup)]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(NoBuildingEditableProfile))]
	[SettingsUISlider(min = 0f, max = 100f, step = 1f, unit = "percentageSingleFraction", scalarMultiplier = 100f, updateOnDragEnd = true)]
	[SettingsUIDisplayName(overrideValue: "Volume")]
	[SettingsUIDescription(overrideValue: "Building profile volume.")]
	public float BuildingProfileVolume
	{
		get => GetBuildingEditableProfile().Volume;
		set => SetBuildingProfileValue(profile => profile.Volume = value, clamp: true);
	}

	[SettingsUISection(kBuildingsTab, kBuildingProfileGroup)]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(NoBuildingEditableProfile))]
	[SettingsUISlider(min = -3f, max = 3f, step = 0.05f, unit = "floatSingleFraction", updateOnDragEnd = true)]
	[SettingsUIDisplayName(overrideValue: "Pitch")]
	[SettingsUIDescription(overrideValue: "Building profile pitch.")]
	public float BuildingProfilePitch
	{
		get => GetBuildingEditableProfile().Pitch;
		set => SetBuildingProfileValue(profile => profile.Pitch = value, clamp: true);
	}

	[SettingsUISection(kBuildingsTab, kBuildingProfileGroup)]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(NoBuildingEditableProfile))]
	[SettingsUISlider(min = 0f, max = 100f, step = 1f, unit = "percentageSingleFraction", scalarMultiplier = 100f, updateOnDragEnd = true)]
	[SettingsUIDisplayName(overrideValue: "Spatial Blend")]
	[SettingsUIDescription(overrideValue: "Building profile spatial blend.")]
	public float BuildingProfileSpatialBlend
	{
		get => GetBuildingEditableProfile().SpatialBlend;
		set => SetBuildingProfileValue(profile => profile.SpatialBlend = value, clamp: true);
	}

	[SettingsUISection(kBuildingsTab, kBuildingProfileGroup)]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(NoBuildingEditableProfile))]
	[SettingsUISlider(min = 0f, max = 100f, step = 1f, unit = "percentageSingleFraction", scalarMultiplier = 100f, updateOnDragEnd = true)]
	[SettingsUIDisplayName(overrideValue: "Doppler Level")]
	[SettingsUIDescription(overrideValue: "Controls doppler effect intensity for moving building sources.")]
	public float BuildingProfileDoppler
	{
		get => GetBuildingEditableProfile().Doppler;
		set => SetBuildingProfileValue(profile => profile.Doppler = value, clamp: true);
	}

	[SettingsUISection(kBuildingsTab, kBuildingProfileGroup)]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(NoBuildingEditableProfile))]
	[SettingsUISlider(min = 0f, max = 360f, step = 1f, unit = "integer", updateOnDragEnd = true)]
	[SettingsUIDisplayName(overrideValue: "Stereo Spread")]
	[SettingsUIDescription(overrideValue: "Sets how widely stereo channels are spread in 3D space.")]
	public float BuildingProfileSpread
	{
		get => GetBuildingEditableProfile().Spread;
		set => SetBuildingProfileValue(profile => profile.Spread = value, clamp: true);
	}

	[SettingsUISection(kBuildingsTab, kBuildingProfileGroup)]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(NoBuildingEditableProfile))]
	[SettingsUISlider(min = 0f, max = 100f, step = 0.5f, unit = "floatSingleFraction", updateOnDragEnd = true)]
	[SettingsUIDisplayName(overrideValue: "Min Distance")]
	[SettingsUIDescription(overrideValue: "Distance where volume starts attenuating based on rolloff mode.")]
	public float BuildingProfileMinDistance
	{
		get => GetBuildingEditableProfile().MinDistance;
		set => SetBuildingProfileValue(profile => profile.MinDistance = value, clamp: true);
	}

	[SettingsUISection(kBuildingsTab, kBuildingProfileGroup)]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(NoBuildingEditableProfile))]
	[SettingsUISlider(min = 1f, max = 500f, step = 1f, unit = "floatSingleFraction", updateOnDragEnd = true)]
	[SettingsUIDisplayName(overrideValue: "Max Distance")]
	[SettingsUIDescription(overrideValue: "Distance where the building sound reaches minimum audible level.")]
	public float BuildingProfileMaxDistance
	{
		get => GetBuildingEditableProfile().MaxDistance;
		set => SetBuildingProfileValue(profile => profile.MaxDistance = value, clamp: true);
	}

	[SettingsUISection(kBuildingsTab, kBuildingProfileGroup)]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(NoBuildingEditableProfile))]
	[SettingsUIDisplayName(overrideValue: "Loop")]
	[SettingsUIDescription(overrideValue: "When enabled, this building profile loops.")]
	public bool BuildingProfileLoop
	{
		get => GetBuildingEditableProfile().Loop;
		set => SetBuildingProfileValue(profile => profile.Loop = value, clamp: false);
	}

	[SettingsUISection(kBuildingsTab, kBuildingProfileGroup)]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(NoBuildingEditableProfile))]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetRolloffModeOptions))]
	[SettingsUIDisplayName(overrideValue: "Rolloff Mode")]
	[SettingsUIDescription(overrideValue: "Selects how volume attenuates over distance.")]
	public int BuildingProfileRolloffMode
	{
		get => (int)GetBuildingEditableProfile().RolloffMode;
		set => SetBuildingProfileValue(
			profile => profile.RolloffMode = Enum.IsDefined(typeof(AudioRolloffMode), value)
				? (AudioRolloffMode)value
				: AudioRolloffMode.Linear,
			clamp: false);
	}

	[SettingsUISection(kBuildingsTab, kBuildingProfileGroup)]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(NoBuildingEditableProfile))]
	[SettingsUIDisplayName(overrideValue: "Random Start Time")]
	[SettingsUIDescription(overrideValue: "Start playback from a random clip position to reduce repetitive sync.")]
	public bool BuildingProfileRandomStartTime
	{
		get => GetBuildingEditableProfile().RandomStartTime;
		set => SetBuildingProfileValue(profile => profile.RandomStartTime = value, clamp: false);
	}

	[SettingsUISection(kBuildingsTab, kBuildingProfileGroup)]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(NoBuildingEditableProfile))]
	[SettingsUISlider(min = 0f, max = 10f, step = 0.05f, unit = "floatSingleFraction", updateOnDragEnd = true)]
	[SettingsUIDisplayName(overrideValue: "Fade In Seconds")]
	[SettingsUIDescription(overrideValue: "Time to ramp from silence to full volume when playback starts.")]
	public float BuildingProfileFadeInSeconds
	{
		get => GetBuildingEditableProfile().FadeInSeconds;
		set => SetBuildingProfileValue(profile => profile.FadeInSeconds = value, clamp: true);
	}

	[SettingsUISection(kBuildingsTab, kBuildingProfileGroup)]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(NoBuildingEditableProfile))]
	[SettingsUISlider(min = 0f, max = 10f, step = 0.05f, unit = "floatSingleFraction", updateOnDragEnd = true)]
	[SettingsUIDisplayName(overrideValue: "Fade Out Seconds")]
	[SettingsUIDescription(overrideValue: "Time to ramp from current volume to silence when playback stops.")]
	public float BuildingProfileFadeOutSeconds
	{
		get => GetBuildingEditableProfile().FadeOutSeconds;
		set => SetBuildingProfileValue(profile => profile.FadeOutSeconds = value, clamp: true);
	}

	[SettingsUISection(kBuildingsTab, kBuildingSetupGroup)]
	[SettingsUIMultilineText]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Custom Building File Scan Status")]
	[SettingsUIDescription(overrideValue: "Shows the latest custom building folder scan summary and changed files.")]
	[SettingsUIWarning(typeof(SirenChangerSettings), nameof(ShowBuildingCatalogWarning))]
	public string BuildingCatalogScanStatus => SirenChangerMod.GetBuildingCatalogScanStatusText();

	// Disable vehicle-target override controls until targets are discovered and selected.
	public bool IsVehicleEngineOverrideDisabled()
	{
		return !SirenChangerMod.HasDiscoveredVehicleEnginePrefabs() ||
			string.IsNullOrWhiteSpace(SirenChangerMod.VehicleEngineConfig.TargetSelectionTarget);
	}

	// Alternate fallback dropdown is only active when the corresponding policy is selected.
	public bool IsVehicleEngineAlternateFallbackSelectionDisabled()
	{
		return SirenChangerMod.VehicleEngineConfig.MissingSelectionFallbackBehavior != SirenFallbackBehavior.AlternateCustomSiren;
	}

	// Disable ambient-target override controls until targets are discovered and selected.
	public bool IsAmbientOverrideDisabled()
	{
		return !SirenChangerMod.HasDiscoveredAmbientTargets() ||
			string.IsNullOrWhiteSpace(SirenChangerMod.AmbientConfig.TargetSelectionTarget);
	}

	// Alternate fallback dropdown is only active when the corresponding policy is selected.
	public bool IsAmbientAlternateFallbackSelectionDisabled()
	{
		return SirenChangerMod.AmbientConfig.MissingSelectionFallbackBehavior != SirenFallbackBehavior.AlternateCustomSiren;
	}

	// Disable building-target override controls until targets are discovered and selected.
	public bool IsBuildingOverrideDisabled()
	{
		return !SirenChangerMod.HasDiscoveredBuildingTargets() ||
			string.IsNullOrWhiteSpace(SirenChangerMod.BuildingConfig.TargetSelectionTarget);
	}

	// Alternate fallback dropdown is only active when the corresponding policy is selected.
	public bool IsBuildingAlternateFallbackSelectionDisabled()
	{
		return SirenChangerMod.BuildingConfig.MissingSelectionFallbackBehavior != SirenFallbackBehavior.AlternateCustomSiren;
	}

	// Editable profile controls require a concrete custom profile selection.
	public bool NoVehicleEngineEditableProfile()
	{
		return !TryGetVehicleEngineEditableProfile(out _);
	}

	// Copy is disabled when source/target are unavailable or identical.
	public bool IsVehicleEngineCopyProfileDisabled()
	{
		if (!TryGetVehicleEngineEditableProfile(out _) || !TryGetVehicleEngineCopySourceProfile(out _))
		{
			return true;
		}

		return string.Equals(
			SirenChangerMod.VehicleEngineConfig.CopyFromProfileSelection,
			SirenChangerMod.VehicleEngineConfig.EditProfileSelection,
			StringComparison.OrdinalIgnoreCase);
	}

	public bool NoAmbientEditableProfile()
	{
		return !TryGetAmbientEditableProfile(out _);
	}

	// Copy is disabled when source/target are unavailable or identical.
	public bool IsAmbientCopyProfileDisabled()
	{
		if (!TryGetAmbientEditableProfile(out _) || !TryGetAmbientCopySourceProfile(out _))
		{
			return true;
		}

		return string.Equals(
			SirenChangerMod.AmbientConfig.CopyFromProfileSelection,
			SirenChangerMod.AmbientConfig.EditProfileSelection,
			StringComparison.OrdinalIgnoreCase);
	}

	public bool NoBuildingEditableProfile()
	{
		return !TryGetBuildingEditableProfile(out _);
	}

	// Copy is disabled when source/target are unavailable or identical.
	public bool IsBuildingCopyProfileDisabled()
	{
		if (!TryGetBuildingEditableProfile(out _) || !TryGetBuildingCopySourceProfile(out _))
		{
			return true;
		}

		return string.Equals(
			SirenChangerMod.BuildingConfig.CopyFromProfileSelection,
			SirenChangerMod.BuildingConfig.EditProfileSelection,
			StringComparison.OrdinalIgnoreCase);
	}

	// Warning helper: indicate that engine target scans have not produced targets yet.
	public bool ShowVehicleEngineScanWarning()
	{
		return !SirenChangerMod.HasDiscoveredVehicleEnginePrefabs();
	}

	// Warning helper: indicate that engine override selection is incomplete.
	public bool ShowVehicleEngineOverrideWarning()
	{
		return IsVehicleEngineOverrideDisabled();
	}

	// Warning helper: indicate that no custom engine profiles are currently available.
	public bool ShowVehicleEngineCatalogWarning()
	{
		return SirenChangerMod.VehicleEngineConfig.CustomProfiles.Count == 0;
	}

	// Warning helper: indicate that ambient target scans have not produced targets yet.
	public bool ShowAmbientTargetScanWarning()
	{
		return !SirenChangerMod.HasDiscoveredAmbientTargets();
	}

	// Warning helper: indicate that ambient override selection is incomplete.
	public bool ShowAmbientOverrideWarning()
	{
		return IsAmbientOverrideDisabled();
	}

	// Warning helper: indicate that no custom ambient profiles are currently available.
	public bool ShowAmbientCatalogWarning()
	{
		return SirenChangerMod.AmbientConfig.CustomProfiles.Count == 0;
	}

	// Warning helper: indicate that building target scans have not produced targets yet.
	public bool ShowBuildingTargetScanWarning()
	{
		return !SirenChangerMod.HasDiscoveredBuildingTargets();
	}

	// Warning helper: indicate that building override selection is incomplete.
	public bool ShowBuildingOverrideWarning()
	{
		return IsBuildingOverrideDisabled();
	}

	// Warning helper: indicate that no custom building profiles are currently available.
	public bool ShowBuildingCatalogWarning()
	{
		return SirenChangerMod.BuildingConfig.CustomProfiles.Count == 0;
	}

	// Centralized labels/descriptions for key actions to simplify localization.
	[Preserve]
	public static string GetRescanCustomEngineFilesLabel() => "Rescan Custom Engine Files";

	[Preserve]
	public static string GetRescanCustomEngineFilesDescription() => "Rescan the Custom Engines folder and refresh dropdowns.";

	[Preserve]
	public static string GetRescanVehicleEnginePrefabsLabel() => "Rescan Vehicle Engine Prefabs";

	[Preserve]
	public static string GetRescanVehicleEnginePrefabsDescription() => "Scan currently loaded prefabs and refresh vehicle-engine override targets.";

	[Preserve]
	public static string GetVehicleEnginePrefabScanStatusLabel() => "Vehicle Engine Prefab Scan Status";

	[Preserve]
	public static string GetVehicleEnginePrefabScanStatusDescription() => "Shows the last vehicle-engine prefab scan result.";

	[Preserve]
	public static string GetRescanCustomAmbientFilesLabel() => "Rescan Custom Ambient Files";

	[Preserve]
	public static string GetRescanCustomAmbientFilesDescription() => "Rescan the Custom Ambient folder and refresh dropdowns.";

	[Preserve]
	public static string GetRescanAmbientTargetsLabel() => "Rescan Ambient Targets";

	[Preserve]
	public static string GetRescanAmbientTargetsDescription() => "Scan currently loaded prefabs and refresh ambient override targets.";

	[Preserve]
	public static string GetAmbientTargetPrefabScanStatusLabel() => "Ambient Target Prefab Scan Status";

	[Preserve]
	public static string GetAmbientTargetPrefabScanStatusDescription() => "Shows the last ambient target scan result.";

	[Preserve]
	public static string GetRescanCustomBuildingFilesLabel() => "Rescan Custom Building Files";

	[Preserve]
	public static string GetRescanCustomBuildingFilesDescription() => "Rescan the Custom Buildings folder and refresh dropdowns.";

	[Preserve]
	public static string GetRescanBuildingTargetsLabel() => "Rescan Building Targets";

	[Preserve]
	public static string GetRescanBuildingTargetsDescription() => "Scan currently loaded prefabs and refresh building override targets.";

	[Preserve]
	public static string GetBuildingTargetPrefabScanStatusLabel() => "Building Target Prefab Scan Status";

	[Preserve]
	public static string GetBuildingTargetPrefabScanStatusDescription() => "Shows the last building target scan result.";

	[Preserve]
	public static DropdownItem<string>[] GetVehicleEngineSelectionOptions()
	{
		return SirenChangerMod.BuildVehicleEngineDropdownItems(includeDefault: true);
	}

	[Preserve]
	public static DropdownItem<string>[] GetVehicleEnginePreviewableProfileOptions()
	{
		return SirenChangerMod.BuildVehicleEngineDropdownItems(includeDefault: true);
	}
	[Preserve]
	public static DropdownItem<string>[] GetVehicleEngineEditableProfileOptions()
	{
		return SirenChangerMod.BuildVehicleEngineDropdownItems(includeDefault: false);
	}
	[Preserve]
	public static DropdownItem<string>[] GetVehicleEngineCopySourceOptions()
	{
		return BuildCopySourceOptions(
			"Default (Detected Engine Template)",
			SirenChangerMod.BuildVehicleEngineDropdownItems(includeDefault: false),
			SirenChangerMod.BuildDetectedCopySourceDropdown(DeveloperAudioDomain.VehicleEngine));
	}

	[Preserve]
	public static DropdownItem<string>[] GetVehicleEngineTargetOptions()
	{
		return SirenChangerMod.BuildVehicleEnginePrefabDropdownItems();
	}

	[Preserve]
	public static DropdownItem<string>[] GetAmbientSelectionOptions()
	{
		return SirenChangerMod.BuildAmbientDropdownItems(includeDefault: true);
	}

	[Preserve]
	public static DropdownItem<string>[] GetAmbientPreviewableProfileOptions()
	{
		return SirenChangerMod.BuildAmbientDropdownItems(includeDefault: true);
	}
	[Preserve]
	public static DropdownItem<string>[] GetAmbientEditableProfileOptions()
	{
		return SirenChangerMod.BuildAmbientDropdownItems(includeDefault: false);
	}
	[Preserve]
	public static DropdownItem<string>[] GetAmbientCopySourceOptions()
	{
		return BuildCopySourceOptions(
			"Default (Detected Ambient Template)",
			SirenChangerMod.BuildAmbientDropdownItems(includeDefault: false),
			SirenChangerMod.BuildDetectedCopySourceDropdown(DeveloperAudioDomain.Ambient));
	}

	[Preserve]
	public static DropdownItem<string>[] GetAmbientTargetOptions()
	{
		return SirenChangerMod.BuildAmbientTargetDropdownItems();
	}

	[Preserve]
	public static DropdownItem<string>[] GetBuildingSelectionOptions()
	{
		return SirenChangerMod.BuildBuildingDropdownItems(includeDefault: true);
	}

	[Preserve]
	public static DropdownItem<string>[] GetBuildingPreviewableProfileOptions()
	{
		return SirenChangerMod.BuildBuildingDropdownItems(includeDefault: true);
	}

	[Preserve]
	public static DropdownItem<string>[] GetBuildingEditableProfileOptions()
	{
		return SirenChangerMod.BuildBuildingDropdownItems(includeDefault: false);
	}

	[Preserve]
	public static DropdownItem<string>[] GetBuildingCopySourceOptions()
	{
		return BuildCopySourceOptions(
			"Default (Detected Building Template)",
			SirenChangerMod.BuildBuildingDropdownItems(includeDefault: false),
			Array.Empty<DropdownItem<string>>());
	}

	[Preserve]
	public static DropdownItem<string>[] GetBuildingTargetOptions()
	{
		return SirenChangerMod.BuildBuildingTargetDropdownItems();
	}

	// Reset all non-siren domain settings when options are reset to defaults.
	private static void ResetExtendedDomainDefaults()
	{
		ResetVehicleEngineDefaults();
		ResetAmbientDefaults();
		ResetBuildingDefaults();
		ResetTransitAnnouncementDefaults();
	}

	// Restore engine-domain configuration and re-seed profile values from the detected template.
	private static void ResetVehicleEngineDefaults()
	{
		AudioReplacementDomainConfig config = SirenChangerMod.VehicleEngineConfig;
		config.Enabled = true;
		config.CustomFolderName = SirenChangerMod.VehicleEngineCustomFolderName;
		config.DefaultSelection = SirenReplacementConfig.DefaultSelectionToken;
		config.EditProfileSelection = string.Empty;
		config.CopyFromProfileSelection = string.Empty;
			config.CustomProfiles.Clear();
			config.TargetSelections.Clear();
			config.TargetSelectionTarget = string.Empty;
			config.KnownTargets.Clear();
			config.MuteAllTargets = false;
			config.MissingSelectionFallbackBehavior = SirenFallbackBehavior.Default;
			config.AlternateFallbackSelection = SirenReplacementConfig.DefaultSelectionToken;
		config.LastCatalogScanUtcTicks = 0;
		config.LastCatalogScanFileCount = 0;
		config.LastCatalogScanAddedCount = 0;
		config.LastCatalogScanRemovedCount = 0;
		config.LastCatalogScanChangedFiles.Clear();
		config.LastTargetScanUtcTicks = 0;
		config.LastTargetScanStatus = string.Empty;
		config.LastValidationUtcTicks = 0;
		config.LastValidationReport = string.Empty;

		SirenChangerMod.SyncCustomVehicleEngineCatalog(saveIfChanged: false);
		SirenSfxProfile seed = SirenChangerMod.VehicleEngineProfileTemplate.ClampCopy();
		List<string> profileKeys = new List<string>(config.CustomProfiles.Keys);
		for (int i = 0; i < profileKeys.Count; i++)
		{
			config.CustomProfiles[profileKeys[i]] = seed.ClampCopy();
		}

		config.EnsureSelectionsValid(new HashSet<string>(config.CustomProfiles.Keys, StringComparer.OrdinalIgnoreCase));
		config.Normalize(SirenChangerMod.VehicleEngineCustomFolderName);
	}

	// Restore ambient-domain configuration and re-seed profile values from the detected template.
	private static void ResetAmbientDefaults()
	{
		AudioReplacementDomainConfig config = SirenChangerMod.AmbientConfig;
		config.Enabled = true;
		config.CustomFolderName = SirenChangerMod.AmbientCustomFolderName;
		config.DefaultSelection = SirenReplacementConfig.DefaultSelectionToken;
		config.EditProfileSelection = string.Empty;
		config.CopyFromProfileSelection = string.Empty;
			config.CustomProfiles.Clear();
			config.TargetSelections.Clear();
			config.TargetSelectionTarget = string.Empty;
			config.KnownTargets.Clear();
			config.MuteAllTargets = false;
			config.MissingSelectionFallbackBehavior = SirenFallbackBehavior.Default;
			config.AlternateFallbackSelection = SirenReplacementConfig.DefaultSelectionToken;
		config.LastCatalogScanUtcTicks = 0;
		config.LastCatalogScanFileCount = 0;
		config.LastCatalogScanAddedCount = 0;
		config.LastCatalogScanRemovedCount = 0;
		config.LastCatalogScanChangedFiles.Clear();
		config.LastTargetScanUtcTicks = 0;
		config.LastTargetScanStatus = string.Empty;
		config.LastValidationUtcTicks = 0;
		config.LastValidationReport = string.Empty;

		SirenChangerMod.SyncCustomAmbientCatalog(saveIfChanged: false);
		SirenSfxProfile seed = SirenChangerMod.AmbientProfileTemplate.ClampCopy();
		List<string> profileKeys = new List<string>(config.CustomProfiles.Keys);
		for (int i = 0; i < profileKeys.Count; i++)
		{
			config.CustomProfiles[profileKeys[i]] = seed.ClampCopy();
		}

		config.EnsureSelectionsValid(new HashSet<string>(config.CustomProfiles.Keys, StringComparer.OrdinalIgnoreCase));
		config.Normalize(SirenChangerMod.AmbientCustomFolderName);
	}

	// Restore building-domain configuration and re-seed profile values from the detected template.
	private static void ResetBuildingDefaults()
	{
		AudioReplacementDomainConfig config = SirenChangerMod.BuildingConfig;
		config.Enabled = true;
		config.CustomFolderName = SirenChangerMod.BuildingCustomFolderName;
		config.DefaultSelection = SirenReplacementConfig.DefaultSelectionToken;
		config.EditProfileSelection = string.Empty;
		config.CopyFromProfileSelection = string.Empty;
		config.CustomProfiles.Clear();
		config.TargetSelections.Clear();
		config.TargetSelectionTarget = string.Empty;
		config.KnownTargets.Clear();
		config.MuteAllTargets = false;
		config.MissingSelectionFallbackBehavior = SirenFallbackBehavior.Default;
		config.AlternateFallbackSelection = SirenReplacementConfig.DefaultSelectionToken;
		config.LastCatalogScanUtcTicks = 0;
		config.LastCatalogScanFileCount = 0;
		config.LastCatalogScanAddedCount = 0;
		config.LastCatalogScanRemovedCount = 0;
		config.LastCatalogScanChangedFiles.Clear();
		config.LastTargetScanUtcTicks = 0;
		config.LastTargetScanStatus = string.Empty;
		config.LastValidationUtcTicks = 0;
		config.LastValidationReport = string.Empty;

		SirenChangerMod.SyncCustomBuildingCatalog(saveIfChanged: false);
		SirenSfxProfile seed = SirenChangerMod.BuildingProfileTemplate.ClampCopy();
		List<string> profileKeys = new List<string>(config.CustomProfiles.Keys);
		for (int i = 0; i < profileKeys.Count; i++)
		{
			config.CustomProfiles[profileKeys[i]] = seed.ClampCopy();
		}

		config.EnsureSelectionsValid(new HashSet<string>(config.CustomProfiles.Keys, StringComparer.OrdinalIgnoreCase));
		config.Normalize(SirenChangerMod.BuildingCustomFolderName);
	}

	// Normalize user dropdown value, mapping empty/default back to canonical token.
	private static string NormalizeDomainSelection(string selection)
	{
		if (AudioReplacementDomainConfig.IsDefaultSelection(selection))
		{
			return SirenReplacementConfig.DefaultSelectionToken;
		}

		string normalized = AudioReplacementDomainConfig.NormalizeProfileKey(selection);
		return string.IsNullOrWhiteSpace(normalized)
			? SirenReplacementConfig.DefaultSelectionToken
			: normalized;
	}

	// Resolve current editable engine profile or fallback to template when none is selected.
	private static SirenSfxProfile GetVehicleEngineEditableProfile()
	{
		if (TryGetVehicleEngineEditableProfile(out SirenSfxProfile profile))
		{
			return profile;
		}

		return SirenChangerMod.VehicleEngineProfileTemplate;
	}

	// Try resolve current editable engine profile from custom profile map.
	private static bool TryGetVehicleEngineEditableProfile(out SirenSfxProfile profile)
	{
		profile = null!;
		string key = SirenChangerMod.VehicleEngineConfig.EditProfileSelection;
		if (string.IsNullOrWhiteSpace(key))
		{
			return false;
		}

		return SirenChangerMod.VehicleEngineConfig.TryGetProfile(key, out profile);
	}

	// Resolve copy-source profile for engine editor from default, detected, or custom entries.
	private static bool TryGetVehicleEngineCopySourceProfile(out SirenSfxProfile profile)
	{
		profile = null!;
		string key = SirenChangerMod.VehicleEngineConfig.CopyFromProfileSelection;
		if (string.IsNullOrWhiteSpace(key))
		{
			return false;
		}

		if (AudioReplacementDomainConfig.IsDefaultSelection(key))
		{
			profile = SirenChangerMod.VehicleEngineProfileTemplate;
			return true;
		}

		if (SirenChangerMod.TryGetDetectedCopySourceProfile(DeveloperAudioDomain.VehicleEngine, key, out profile))
		{
			return true;
		}

		return SirenChangerMod.VehicleEngineConfig.TryGetProfile(key, out profile);
	}

	// Apply an update action to the current editable engine profile.
	private static void SetVehicleEngineProfileValue(Action<SirenSfxProfile> updater, bool clamp)
	{
		if (!TryGetVehicleEngineEditableProfile(out SirenSfxProfile profile))
		{
			return;
		}

		updater(profile);
		if (clamp)
		{
			profile.ClampInPlace();
		}
	}

	// Resolve current editable ambient profile or fallback to template when none is selected.
	private static SirenSfxProfile GetAmbientEditableProfile()
	{
		if (TryGetAmbientEditableProfile(out SirenSfxProfile profile))
		{
			return profile;
		}

		return SirenChangerMod.AmbientProfileTemplate;
	}

	// Try resolve current editable ambient profile from custom profile map.
	private static bool TryGetAmbientEditableProfile(out SirenSfxProfile profile)
	{
		profile = null!;
		string key = SirenChangerMod.AmbientConfig.EditProfileSelection;
		if (string.IsNullOrWhiteSpace(key))
		{
			return false;
		}

		return SirenChangerMod.AmbientConfig.TryGetProfile(key, out profile);
	}

	// Resolve copy-source profile for ambient editor from default, detected, or custom entries.
	private static bool TryGetAmbientCopySourceProfile(out SirenSfxProfile profile)
	{
		profile = null!;
		string key = SirenChangerMod.AmbientConfig.CopyFromProfileSelection;
		if (string.IsNullOrWhiteSpace(key))
		{
			return false;
		}

		if (AudioReplacementDomainConfig.IsDefaultSelection(key))
		{
			profile = SirenChangerMod.AmbientProfileTemplate;
			return true;
		}

		if (SirenChangerMod.TryGetDetectedCopySourceProfile(DeveloperAudioDomain.Ambient, key, out profile))
		{
			return true;
		}

		return SirenChangerMod.AmbientConfig.TryGetProfile(key, out profile);
	}

	// Apply an update action to the current editable ambient profile.
	private static void SetAmbientProfileValue(Action<SirenSfxProfile> updater, bool clamp)
	{
		if (!TryGetAmbientEditableProfile(out SirenSfxProfile profile))
		{
			return;
		}

		updater(profile);
		if (clamp)
		{
			profile.ClampInPlace();
		}
	}

	// Resolve current editable building profile or fallback to template when none is selected.
	private static SirenSfxProfile GetBuildingEditableProfile()
	{
		if (TryGetBuildingEditableProfile(out SirenSfxProfile profile))
		{
			return profile;
		}

		return SirenChangerMod.BuildingProfileTemplate;
	}

	// Try resolve current editable building profile from custom profile map.
	private static bool TryGetBuildingEditableProfile(out SirenSfxProfile profile)
	{
		profile = null!;
		string key = SirenChangerMod.BuildingConfig.EditProfileSelection;
		if (string.IsNullOrWhiteSpace(key))
		{
			return false;
		}

		return SirenChangerMod.BuildingConfig.TryGetProfile(key, out profile);
	}

	// Resolve copy-source profile for building editor from default or custom entries.
	private static bool TryGetBuildingCopySourceProfile(out SirenSfxProfile profile)
	{
		profile = null!;
		string key = SirenChangerMod.BuildingConfig.CopyFromProfileSelection;
		if (string.IsNullOrWhiteSpace(key))
		{
			return false;
		}

		if (AudioReplacementDomainConfig.IsDefaultSelection(key))
		{
			profile = SirenChangerMod.BuildingProfileTemplate;
			return true;
		}

		return SirenChangerMod.BuildingConfig.TryGetProfile(key, out profile);
	}

	// Apply an update action to the current editable building profile.
	private static void SetBuildingProfileValue(Action<SirenSfxProfile> updater, bool clamp)
	{
		if (!TryGetBuildingEditableProfile(out SirenSfxProfile profile))
		{
			return;
		}

		updater(profile);
		if (clamp)
		{
			profile.ClampInPlace();
		}
	}

	// Copy all editable SFX fields from source to target using clamped source values.
	private static void CopyProfileValues(SirenSfxProfile target, SirenSfxProfile source)
	{
		SirenSfxProfile clone = source.ClampCopy();
		target.Volume = clone.Volume;
		target.Pitch = clone.Pitch;
		target.SpatialBlend = clone.SpatialBlend;
		target.Doppler = clone.Doppler;
		target.Spread = clone.Spread;
		target.MinDistance = clone.MinDistance;
		target.MaxDistance = clone.MaxDistance;
		target.Loop = clone.Loop;
		target.RolloffMode = clone.RolloffMode;
		target.FadeInSeconds = clone.FadeInSeconds;
		target.FadeOutSeconds = clone.FadeOutSeconds;
		target.RandomStartTime = clone.RandomStartTime;
	}
}


