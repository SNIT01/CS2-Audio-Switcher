using System;
using System.Collections.Generic;
using Game.Modding;
using Game.Settings;
using Game.UI.Widgets;
using UnityEngine;
using UnityEngine.Scripting;

namespace SirenChanger;

[SettingsUITabOrder(kSirensTab, kVehiclesTab, kAmbientTab, kDeveloperTab)]
[SettingsUIGroupOrder(kGeneralGroup, kVehicleGroup, kVehicleOverrideGroup, kFallbackGroup, kProfileGroup, kDiagnosticsGroup, kVehicleSetupGroup, kVehicleOverrideTargetGroup, kVehicleFallbackGroup, kVehicleProfileGroup, kVehicleDiagnosticsGroup, kAmbientSetupGroup, kAmbientTargetGroup, kAmbientFallbackGroup, kAmbientProfileGroup, kAmbientDiagnosticsGroup, kDeveloperSirenGroup, kDeveloperEngineGroup, kDeveloperAmbientGroup, kDeveloperModuleGroup)]
[SettingsUIShowGroupName]
// Options UI binding surface for all configurable siren changer behavior.
public sealed partial class SirenChangerSettings : ModSetting
{
	public const string kSirensTab = "Sirens";

	public const string kVehiclesTab = "Vehicle Engines";

	public const string kAmbientTab = "Ambient Sounds";

	public const string kGeneralGroup = "Siren Setup";

	public const string kVehicleGroup = "Siren Defaults";

	public const string kVehicleOverrideGroup = "Specific Vehicle Siren Overrides";

	public const string kFallbackGroup = "Missing Siren Behavior";

	public const string kProfileGroup = "Siren Profile Editor";

	public const string kDiagnosticsGroup = "Siren Diagnostics";

	private const string kSirenRescanButtonGroup = "Siren Scan Actions";

	public SirenChangerSettings(IMod mod)
		: base(mod)
	{
	}

	// General enable/disable and catalog controls.
	[SettingsUISection(kSirensTab, kGeneralGroup)]
	[SettingsUIDisplayName(overrideValue: "Enable Siren Replacement")]
	[SettingsUIDescription(overrideValue: "Enable or disable custom siren sound replacement.")]
	public bool Enabled
	{
		get => SirenChangerMod.Config.Enabled;
		set => SirenChangerMod.Config.Enabled = value;
	}

	[SettingsUISection(kSirensTab, kGeneralGroup)]
	[SettingsUIButton]
	[SettingsUIButtonGroup(kSirenRescanButtonGroup)]
	[SettingsUIDisplayName(overrideValue: "Rescan Custom Siren Files")]
	[SettingsUIDescription(overrideValue: "Rescan the Custom Sirens folder for .wav and .ogg files and refresh dropdown options.")]
	public bool UpdateCustomSirens
	{
		set => SirenChangerMod.RefreshCustomSirensFromOptions();
	}

	[SettingsUISection(kSirensTab, kGeneralGroup)]
	[SettingsUIButton]
	[SettingsUIButtonGroup(kSirenRescanButtonGroup)]
	[SettingsUIDisplayName(overrideValue: "Rescan Emergency Vehicle Prefabs")]
	[SettingsUIDescription(overrideValue: "Scan currently loaded prefabs and refresh the emergency-vehicle siren override targets.")]
	public bool UpdateEmergencyVehiclePrefabs
	{
		set => SirenChangerMod.RefreshEmergencyVehiclePrefabsFromOptions();
	}

	[SettingsUISection(kSirensTab, kGeneralGroup)]
	[SettingsUIMultilineText]
	[SettingsUIDisplayName(overrideValue: "Emergency Vehicle Prefab Scan Status")]
	[SettingsUIDescription(overrideValue: "Shows the last emergency vehicle prefab scan time and summary.")]
	[SettingsUIWarning(typeof(SirenChangerSettings), nameof(ShowEmergencyVehicleScanWarning))]
	public string EmergencyVehiclePrefabScanStatus => SirenChangerMod.GetVehiclePrefabScanStatusText();

	// Per-vehicle, per-region siren selection dropdowns.
	[SettingsUISection(kSirensTab, kVehicleGroup)]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetVehicleSelectionOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Police Default (NA)")]
	[SettingsUIDescription(overrideValue: "Default siren used by North American police vehicles.")]
	public string PoliceSirenNA
	{
		get => SirenChangerMod.Config.GetSelection(EmergencySirenVehicleType.Police, SirenRegion.NorthAmerica);
		set => SirenChangerMod.Config.SetSelection(EmergencySirenVehicleType.Police, SirenRegion.NorthAmerica, value);
	}

	[SettingsUISection(kSirensTab, kVehicleGroup)]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetVehicleSelectionOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Police Default (EU)")]
	[SettingsUIDescription(overrideValue: "Default siren used by European police vehicles.")]
	public string PoliceSirenEU
	{
		get => SirenChangerMod.Config.GetSelection(EmergencySirenVehicleType.Police, SirenRegion.Europe);
		set => SirenChangerMod.Config.SetSelection(EmergencySirenVehicleType.Police, SirenRegion.Europe, value);
	}

	[SettingsUISection(kSirensTab, kVehicleGroup)]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetVehicleSelectionOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Fire Truck Default (NA)")]
	[SettingsUIDescription(overrideValue: "Default siren used by North American fire trucks.")]
	public string FireTruckSirenNA
	{
		get => SirenChangerMod.Config.GetSelection(EmergencySirenVehicleType.Fire, SirenRegion.NorthAmerica);
		set => SirenChangerMod.Config.SetSelection(EmergencySirenVehicleType.Fire, SirenRegion.NorthAmerica, value);
	}

	[SettingsUISection(kSirensTab, kVehicleGroup)]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetVehicleSelectionOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Fire Truck Default (EU)")]
	[SettingsUIDescription(overrideValue: "Default siren used by European fire trucks.")]
	public string FireTruckSirenEU
	{
		get => SirenChangerMod.Config.GetSelection(EmergencySirenVehicleType.Fire, SirenRegion.Europe);
		set => SirenChangerMod.Config.SetSelection(EmergencySirenVehicleType.Fire, SirenRegion.Europe, value);
	}

	[SettingsUISection(kSirensTab, kVehicleGroup)]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetVehicleSelectionOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Ambulance Default (NA)")]
	[SettingsUIDescription(overrideValue: "Default siren used by North American ambulances.")]
	public string AmbulanceSirenNA
	{
		get => SirenChangerMod.Config.GetSelection(EmergencySirenVehicleType.Ambulance, SirenRegion.NorthAmerica);
		set => SirenChangerMod.Config.SetSelection(EmergencySirenVehicleType.Ambulance, SirenRegion.NorthAmerica, value);
	}

	[SettingsUISection(kSirensTab, kVehicleGroup)]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetVehicleSelectionOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Ambulance Default (EU)")]
	[SettingsUIDescription(overrideValue: "Default siren used by European ambulances.")]
	public string AmbulanceSirenEU
	{
		get => SirenChangerMod.Config.GetSelection(EmergencySirenVehicleType.Ambulance, SirenRegion.Europe);
		set => SirenChangerMod.Config.SetSelection(EmergencySirenVehicleType.Ambulance, SirenRegion.Europe, value);
	}

	[SettingsUISection(kSirensTab, kVehicleOverrideGroup)]
	[SettingsUIWarning(typeof(SirenChangerSettings), nameof(ShowSpecificVehicleOverrideWarning))]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetSpecificVehiclePrefabOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Emergency Vehicle Prefab")]
	[SettingsUIDescription(overrideValue: "Choose a specific emergency vehicle prefab, such as NA_PoliceCar_01.")]
	public string SpecificVehiclePrefab
	{
		get => SirenChangerMod.Config.VehiclePrefabSelectionTarget;
		set => SirenChangerMod.SetVehiclePrefabSelectionTargetFromOptions(value);
	}

	[SettingsUISection(kSirensTab, kVehicleOverrideGroup)]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(IsSpecificVehicleOverrideDisabled))]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetVehicleSelectionOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Override Siren")]
	[SettingsUIDescription(overrideValue: "Default means this prefab uses the vehicle default selection from Vehicle Defaults.")]
	public string SpecificVehicleSirenOverride
	{
		get => SirenChangerMod.GetSelectedVehiclePrefabSelectionForOptions();
		set => SirenChangerMod.SetSelectedVehiclePrefabSelectionFromOptions(value);
	}

	[SettingsUISection(kSirensTab, kVehicleOverrideGroup)]
	[SettingsUIMultilineText]
	[SettingsUIDisplayName(overrideValue: "Siren Override Status")]
	[SettingsUIDescription(overrideValue: "Shows whether this prefab uses defaults or a specific siren override.")]
	public string SpecificVehicleOverrideStatus => SirenChangerMod.GetSelectedVehicleOverrideStatusText();

	// Fallback behavior controls when selected custom files fail.
	[SettingsUISection(kSirensTab, kFallbackGroup)]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetFallbackBehaviorOptions))]
	[SettingsUIDisplayName(overrideValue: "If Selected Siren Is Missing")]
	[SettingsUIDescription(overrideValue: "Choose what happens when a selected custom siren file or profile cannot be loaded.")]
	public int MissingSirenFallbackBehavior
	{
		get => (int)SirenChangerMod.Config.MissingSirenFallbackBehavior;
		set
		{
			if (Enum.IsDefined(typeof(SirenFallbackBehavior), value))
			{
				SirenChangerMod.Config.MissingSirenFallbackBehavior = (SirenFallbackBehavior)value;
			}
			else
			{
				SirenChangerMod.Config.MissingSirenFallbackBehavior = SirenFallbackBehavior.Default;
			}
		}
	}

	[SettingsUISection(kSirensTab, kFallbackGroup)]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(IsAlternateFallbackSelectionDisabled))]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetVehicleSelectionOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Alternate Siren")]
	[SettingsUIDescription(overrideValue: "Used only when fallback behavior is set to Alternate Custom Sound File.")]
	public string AlternateFallbackSiren
	{
		get => SirenChangerMod.Config.AlternateFallbackSelection;
		set => SirenChangerMod.Config.AlternateFallbackSelection = SirenPathUtils.NormalizeProfileKey(value ?? string.Empty);
	}

	// Profile editing tools and SFX sliders.
	[SettingsUISection(kSirensTab, kProfileGroup)]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetPreviewableProfileOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Profile To Edit")]
	[SettingsUIDescription(overrideValue: "Select a custom profile to edit, or choose Default to preview the built-in game siren sample.")]
	public string EditProfile
	{
		get => SirenChangerMod.Config.EditProfileSelection;
		set => SirenChangerMod.Config.EditProfileSelection = SirenPathUtils.NormalizeProfileKey(value ?? string.Empty);
	}

	[SettingsUISection(kSirensTab, kProfileGroup)]
	[SettingsUIButton]
	[SettingsUIDisplayName(overrideValue: "Preview Selected Sound")]
	[SettingsUIDescription(overrideValue: "Play the selected custom profile, or the built-in default sample when Default is selected.")]
	public bool PreviewSelectedProfile
	{
		set => SirenChangerMod.PreviewSelectedProfileFromOptions();
	}

	[SettingsUISection(kSirensTab, kProfileGroup)]
	[SettingsUIMultilineText]
	[SettingsUIDisplayName(overrideValue: "Preview Status")]
	[SettingsUIDescription(overrideValue: "Shows the result of the last preview action.")]
	public string PreviewStatus => SirenChangerMod.GetPreviewStatusText();

	[SettingsUISection(kSirensTab, kProfileGroup)]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(NoEditableProfile))]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetSirenCopySourceOptions))]
	[SettingsUIValueVersion(typeof(SirenChangerSettings), nameof(GetDropdownVersion))]
	[SettingsUIDisplayName(overrideValue: "Copy Settings From Profile")]
	[SettingsUIDescription(overrideValue: "Choose a source profile from custom sirens or detected siren SFX in this category.")]
	public string CopyFromProfile
	{
		get => SirenChangerMod.Config.CopyFromProfileSelection;
		set => SirenChangerMod.Config.CopyFromProfileSelection = SirenPathUtils.NormalizeProfileKey(value ?? string.Empty);
	}

	[SettingsUISection(kSirensTab, kProfileGroup)]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(IsCopyProfileDisabled))]
	[SettingsUIButton]
	[SettingsUIDisplayName(overrideValue: "Copy Source Profile Into Current Profile")]
	[SettingsUIDescription(overrideValue: "Copy all SFX parameters from the selected source profile into the profile being edited.")]
	public bool CopyProfileIntoEditable
	{
		set
		{
			if (TryGetEditableProfile(out SirenSfxProfile target) && TryGetCopySourceProfile(out SirenSfxProfile source))
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
				SirenChangerMod.NotifyRuntimeConfigChanged(saveToDisk: true);
			}
		}
	}

	[SettingsUISection(kSirensTab, kProfileGroup)]
	[SettingsUIButton]
	[SettingsUIDisplayName(overrideValue: "Reset Current Profile To Police Template")]
	[SettingsUIDescription(overrideValue: "Reset the selected profile to template values captured from PoliceCarSirenNA.")]
	public bool ResetEditableProfileToTemplate
	{
		set
		{
			if (TryGetEditableProfile(out SirenSfxProfile target))
			{
				SirenSfxProfile template = SirenChangerMod.CustomProfileTemplate.ClampCopy();
				target.Volume = template.Volume;
				target.Pitch = template.Pitch;
				target.SpatialBlend = template.SpatialBlend;
				target.Doppler = template.Doppler;
				target.Spread = template.Spread;
				target.MinDistance = template.MinDistance;
				target.MaxDistance = template.MaxDistance;
				target.Loop = template.Loop;
				target.RolloffMode = template.RolloffMode;
				target.FadeInSeconds = template.FadeInSeconds;
				target.FadeOutSeconds = template.FadeOutSeconds;
				target.RandomStartTime = template.RandomStartTime;
				SirenChangerMod.NotifyRuntimeConfigChanged(saveToDisk: true);
			}
		}
	}

	[SettingsUISection(kSirensTab, kProfileGroup)]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(NoEditableProfile))]
	[SettingsUISlider(min = 0f, max = 100f, step = 1f, unit = "percentageSingleFraction", scalarMultiplier = 100f, updateOnDragEnd = true)]
	[SettingsUIDisplayName(overrideValue: "Volume")]
	[SettingsUIDescription(overrideValue: "Controls playback loudness for this custom siren profile.")]
	public float ProfileVolume
	{
		get => GetEditableProfile().Volume;
		set
		{
			if (TryGetEditableProfile(out SirenSfxProfile profile))
			{
				profile.Volume = value;
				profile.ClampInPlace();
			}
		}
	}

	[SettingsUISection(kSirensTab, kProfileGroup)]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(NoEditableProfile))]
	[SettingsUISlider(min = -3f, max = 3f, step = 0.05f, unit = "floatSingleFraction", updateOnDragEnd = true)]
	[SettingsUIDisplayName(overrideValue: "Pitch")]
	[SettingsUIDescription(overrideValue: "Changes siren playback speed and tone.")]
	public float ProfilePitch
	{
		get => GetEditableProfile().Pitch;
		set
		{
			if (TryGetEditableProfile(out SirenSfxProfile profile))
			{
				profile.Pitch = value;
				profile.ClampInPlace();
			}
		}
	}

	[SettingsUISection(kSirensTab, kProfileGroup)]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(NoEditableProfile))]
	[SettingsUISlider(min = 0f, max = 100f, step = 1f, unit = "percentageSingleFraction", scalarMultiplier = 100f, updateOnDragEnd = true)]
	[SettingsUIDisplayName(overrideValue: "Spatial Blend")]
	[SettingsUIDescription(overrideValue: "0 is 2D audio, 1 is fully 3D positional audio.")]
	public float ProfileSpatialBlend
	{
		get => GetEditableProfile().SpatialBlend;
		set
		{
			if (TryGetEditableProfile(out SirenSfxProfile profile))
			{
				profile.SpatialBlend = value;
				profile.ClampInPlace();
			}
		}
	}

	[SettingsUISection(kSirensTab, kProfileGroup)]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(NoEditableProfile))]
	[SettingsUISlider(min = 0f, max = 100f, step = 1f, unit = "percentageSingleFraction", scalarMultiplier = 100f, updateOnDragEnd = true)]
	[SettingsUIDisplayName(overrideValue: "Doppler Level")]
	[SettingsUIDescription(overrideValue: "Controls doppler effect intensity for moving siren sources.")]
	public float ProfileDoppler
	{
		get => GetEditableProfile().Doppler;
		set
		{
			if (TryGetEditableProfile(out SirenSfxProfile profile))
			{
				profile.Doppler = value;
				profile.ClampInPlace();
			}
		}
	}

	[SettingsUISection(kSirensTab, kProfileGroup)]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(NoEditableProfile))]
	[SettingsUISlider(min = 0f, max = 360f, step = 1f, unit = "integer", updateOnDragEnd = true)]
	[SettingsUIDisplayName(overrideValue: "Stereo Spread")]
	[SettingsUIDescription(overrideValue: "Sets how widely stereo channels are spread in 3D space.")]
	public float ProfileSpread
	{
		get => GetEditableProfile().Spread;
		set
		{
			if (TryGetEditableProfile(out SirenSfxProfile profile))
			{
				profile.Spread = value;
				profile.ClampInPlace();
			}
		}
	}

	[SettingsUISection(kSirensTab, kProfileGroup)]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(NoEditableProfile))]
	[SettingsUISlider(min = 0f, max = 100f, step = 0.5f, unit = "floatSingleFraction", updateOnDragEnd = true)]
	[SettingsUIDisplayName(overrideValue: "Min Distance")]
	[SettingsUIDescription(overrideValue: "Distance where volume starts attenuating based on rolloff mode.")]
	public float ProfileMinDistance
	{
		get => GetEditableProfile().MinDistance;
		set
		{
			if (TryGetEditableProfile(out SirenSfxProfile profile))
			{
				profile.MinDistance = value;
				profile.ClampInPlace();
			}
		}
	}

	[SettingsUISection(kSirensTab, kProfileGroup)]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(NoEditableProfile))]
	[SettingsUISlider(min = 1f, max = 500f, step = 1f, unit = "floatSingleFraction", updateOnDragEnd = true)]
	[SettingsUIDisplayName(overrideValue: "Max Distance")]
	[SettingsUIDescription(overrideValue: "Distance where the siren reaches minimum audible level.")]
	public float ProfileMaxDistance
	{
		get => GetEditableProfile().MaxDistance;
		set
		{
			if (TryGetEditableProfile(out SirenSfxProfile profile))
			{
				profile.MaxDistance = value;
				profile.ClampInPlace();
			}
		}
	}

	[SettingsUISection(kSirensTab, kProfileGroup)]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(NoEditableProfile))]
	[SettingsUIDisplayName(overrideValue: "Loop")]
	[SettingsUIDescription(overrideValue: "When enabled, the clip repeats continuously while the siren is active.")]
	public bool ProfileLoop
	{
		get => GetEditableProfile().Loop;
		set
		{
			if (TryGetEditableProfile(out SirenSfxProfile profile))
			{
				profile.Loop = value;
			}
		}
	}

	[SettingsUISection(kSirensTab, kProfileGroup)]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(NoEditableProfile))]
	[SettingsUIDropdown(typeof(SirenChangerSettings), nameof(GetRolloffModeOptions))]
	[SettingsUIDisplayName(overrideValue: "Rolloff Mode")]
	[SettingsUIDescription(overrideValue: "Selects how volume attenuates over distance.")]
	public int ProfileRolloffMode
	{
		get => (int)GetEditableProfile().RolloffMode;
		set
		{
			if (TryGetEditableProfile(out SirenSfxProfile profile))
			{
				if (Enum.IsDefined(typeof(AudioRolloffMode), value))
				{
					profile.RolloffMode = (AudioRolloffMode)value;
				}
				else
				{
					profile.RolloffMode = AudioRolloffMode.Linear;
				}
			}
		}
	}

	[SettingsUISection(kSirensTab, kProfileGroup)]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(NoEditableProfile))]
	[SettingsUIDisplayName(overrideValue: "Random Start Time")]
	[SettingsUIDescription(overrideValue: "Start playback from a random clip position to reduce repetitive sync.")]
	public bool ProfileRandomStartTime
	{
		get => GetEditableProfile().RandomStartTime;
		set
		{
			if (TryGetEditableProfile(out SirenSfxProfile profile))
			{
				profile.RandomStartTime = value;
			}
		}
	}

	[SettingsUISection(kSirensTab, kProfileGroup)]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(NoEditableProfile))]
	[SettingsUISlider(min = 0f, max = 10f, step = 0.05f, unit = "floatSingleFraction", updateOnDragEnd = true)]
	[SettingsUIDisplayName(overrideValue: "Fade In Seconds")]
	[SettingsUIDescription(overrideValue: "Time to ramp from silence to full volume when the siren starts.")]
	public float ProfileFadeInSeconds
	{
		get => GetEditableProfile().FadeInSeconds;
		set
		{
			if (TryGetEditableProfile(out SirenSfxProfile profile))
			{
				profile.FadeInSeconds = value;
				profile.ClampInPlace();
			}
		}
	}

	[SettingsUISection(kSirensTab, kProfileGroup)]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(NoEditableProfile))]
	[SettingsUISlider(min = 0f, max = 10f, step = 0.05f, unit = "floatSingleFraction", updateOnDragEnd = true)]
	[SettingsUIDisplayName(overrideValue: "Fade Out Seconds")]
	[SettingsUIDescription(overrideValue: "Time to ramp from current volume to silence when the siren stops.")]
	public float ProfileFadeOutSeconds
	{
		get => GetEditableProfile().FadeOutSeconds;
		set
		{
			if (TryGetEditableProfile(out SirenSfxProfile profile))
			{
				profile.FadeOutSeconds = value;
				profile.ClampInPlace();
			}
		}
	}

	// Diagnostics actions and read-only status reports.
	[SettingsUISection(kSirensTab, kDiagnosticsGroup)]
	[SettingsUIDisplayName(overrideValue: "Write Detection Report (Debug)")]
	[SettingsUIDescription(overrideValue: "Write DetectedSirens.json after sirens are applied.")]
	public bool DumpDetectedSirens
	{
		get => SirenChangerMod.Config.DumpDetectedSirens;
		set => SirenChangerMod.Config.DumpDetectedSirens = value;
	}

	[SettingsUISection(kSirensTab, kDiagnosticsGroup)]
	[SettingsUIDisableByCondition(typeof(SirenChangerSettings), nameof(IsDetectedSirenDumpDisabled))]
	[SettingsUIDisplayName(overrideValue: "Include Broad Siren Scan (Debug)")]
	[SettingsUIDescription(overrideValue: "When enabled, the report scans all SFX prefabs matching siren tokens, not only target sirens.")]
	public bool DumpAllSirenCandidates
	{
		get => SirenChangerMod.Config.DumpAllSirenCandidates;
		set => SirenChangerMod.Config.DumpAllSirenCandidates = value;
	}

	[SettingsUISection(kSirensTab, kDiagnosticsGroup)]
	[SettingsUIButton]
	[SettingsUIDisplayName(overrideValue: "Run Siren Setup Validation")]
	[SettingsUIDescription(overrideValue: "Checks selected sirens, fallback settings, and custom profile/file consistency.")]
	[SettingsUIWarning(typeof(SirenChangerSettings), nameof(ShowValidationWarning))]
	public bool ValidateSirenSetup
	{
		set => SirenChangerMod.RunValidationFromOptions();
	}

	[SettingsUISection(kSirensTab, kDiagnosticsGroup)]
	[SettingsUIMultilineText]
	[SettingsUIDisplayName(overrideValue: "Custom Siren File Scan Status")]
	[SettingsUIDescription(overrideValue: "Shows the latest custom siren folder scan summary and changed files.")]
	[SettingsUIWarning(typeof(SirenChangerSettings), nameof(ShowCustomSirenCatalogWarning))]
	public string CatalogScanStatus => SirenChangerMod.GetCatalogScanStatusText();

	[SettingsUISection(kSirensTab, kDiagnosticsGroup)]
	[SettingsUIMultilineText]
	[SettingsUIDisplayName(overrideValue: "Validation Report")]
	[SettingsUIDescription(overrideValue: "Shows the latest validation errors and warnings for your configuration.")]
	[SettingsUIWarning(typeof(SirenChangerSettings), nameof(ShowValidationWarning))]
	public string ValidationStatus => SirenChangerMod.GetValidationStatusText();

	// Apply button callback from the options framework.
	public override void Apply()
	{
		base.Apply();
		SirenChangerMod.NotifyRuntimeConfigChanged(saveToDisk: true);
	}

	// Reset all user-editable fields to defaults and reseed profile values.
	public override void SetDefaults()
	{
		SirenReplacementConfig config = SirenChangerMod.Config;
		config.Enabled = true;
		config.DumpDetectedSirens = false;
		config.DumpAllSirenCandidates = false;
		config.CustomSirensFolderName = SirenReplacementConfig.DefaultCustomSirensFolderName;
		config.SetSelection(EmergencySirenVehicleType.Police, SirenRegion.NorthAmerica, SirenReplacementConfig.DefaultSelectionToken);
		config.SetSelection(EmergencySirenVehicleType.Police, SirenRegion.Europe, SirenReplacementConfig.DefaultSelectionToken);
		config.SetSelection(EmergencySirenVehicleType.Fire, SirenRegion.NorthAmerica, SirenReplacementConfig.DefaultSelectionToken);
		config.SetSelection(EmergencySirenVehicleType.Fire, SirenRegion.Europe, SirenReplacementConfig.DefaultSelectionToken);
		config.SetSelection(EmergencySirenVehicleType.Ambulance, SirenRegion.NorthAmerica, SirenReplacementConfig.DefaultSelectionToken);
		config.SetSelection(EmergencySirenVehicleType.Ambulance, SirenRegion.Europe, SirenReplacementConfig.DefaultSelectionToken);
		config.VehiclePrefabSelections.Clear();
		config.VehiclePrefabSelectionTarget = string.Empty;
		config.KnownEmergencyVehiclePrefabs.Clear();
		config.MissingSirenFallbackBehavior = SirenFallbackBehavior.Default;
		config.AlternateFallbackSelection = SirenReplacementConfig.DefaultSelectionToken;
		config.CopyFromProfileSelection = string.Empty;
		config.LastCatalogScanUtcTicks = 0;
		config.LastCatalogScanFileCount = 0;
		config.LastCatalogScanAddedCount = 0;
		config.LastCatalogScanRemovedCount = 0;
		config.LastCatalogScanChangedFiles.Clear();
		config.LastValidationUtcTicks = 0;
		config.LastValidationReport = string.Empty;

		SirenChangerMod.SyncCustomSirenCatalog(saveIfChanged: false);
		SirenSfxProfile seed = SirenChangerMod.CustomProfileTemplate.ClampCopy();
		List<string> profileKeys = new List<string>(config.CustomSirenProfiles.Keys);
		for (int i = 0; i < profileKeys.Count; i++)
		{
			config.CustomSirenProfiles[profileKeys[i]] = seed.ClampCopy();
		}

		config.PendingTemplateProfileKeys.Clear();
		config.CustomProfileTemplateInitialized = true;
		config.EnsureSelectionsValid(new HashSet<string>(config.CustomSirenProfiles.Keys, StringComparer.OrdinalIgnoreCase));
		ResetExtendedDomainDefaults();
		config.Normalize();
	}

	// Condition helper: true when no profile is selected/available.
	public bool NoEditableProfile()
	{
		return !TryGetEditableProfile(out _);
	}

	// Condition helper for copy button state.
	public bool IsCopyProfileDisabled()
	{
		if (!TryGetEditableProfile(out _))
		{
			return true;
		}

		if (!TryGetCopySourceProfile(out _))
		{
			return true;
		}

		return string.Equals(
			SirenChangerMod.Config.CopyFromProfileSelection,
			SirenChangerMod.Config.EditProfileSelection,
			StringComparison.OrdinalIgnoreCase);
	}

	// Condition helper for debug toggle visibility/disable state.
	public bool IsDetectedSirenDumpDisabled()
	{
		return !SirenChangerMod.Config.DumpDetectedSirens;
	}

	// Condition helper for alternate fallback selector enable state.
	public bool IsAlternateFallbackSelectionDisabled()
	{
		return SirenChangerMod.Config.MissingSirenFallbackBehavior != SirenFallbackBehavior.AlternateCustomSiren;
	}

	// Condition helper for specific-vehicle override controls.
	public bool IsSpecificVehicleOverrideDisabled()
	{
		return !SirenChangerMod.HasDiscoveredVehiclePrefabs() ||
			string.IsNullOrWhiteSpace(SirenChangerMod.Config.VehiclePrefabSelectionTarget);
	}

	// Warning helper: indicate that emergency vehicle prefab scanning has not produced targets yet.
	public bool ShowEmergencyVehicleScanWarning()
	{
		return !SirenChangerMod.HasDiscoveredVehiclePrefabs();
	}

	// Warning helper: indicate that specific-vehicle override selection is incomplete.
	public bool ShowSpecificVehicleOverrideWarning()
	{
		return IsSpecificVehicleOverrideDisabled();
	}

	// Warning helper: indicate that no custom siren profiles are currently available.
	public bool ShowCustomSirenCatalogWarning()
	{
		return SirenChangerMod.Config.CustomSirenProfiles.Count == 0;
	}

	// Warning helper: indicate that the latest validation report contains at least one error.
	public bool ShowValidationWarning()
	{
		string report = SirenChangerMod.Config.LastValidationReport;
		return !string.IsNullOrWhiteSpace(report) &&
			report.IndexOf("ERROR:", StringComparison.OrdinalIgnoreCase) >= 0;
	}

	// Value version callback used to refresh dropdowns when catalog changes.
	[Preserve]
	public static int GetDropdownVersion()
	{
		return SirenChangerMod.OptionsVersion;
	}

	// Centralized labels/descriptions for key actions to simplify localization.
	[Preserve]
	public static string GetRescanCustomSirenFilesLabel() => "Rescan Custom Siren Files";

	[Preserve]
	public static string GetRescanCustomSirenFilesDescription() => "Rescan the Custom Sirens folder for .wav and .ogg files and refresh dropdown options.";

	[Preserve]
	public static string GetRescanEmergencyVehiclePrefabsLabel() => "Rescan Emergency Vehicle Prefabs";

	[Preserve]
	public static string GetRescanEmergencyVehiclePrefabsDescription() => "Scan currently loaded prefabs and refresh the emergency-vehicle siren override targets.";

	[Preserve]
	public static string GetEmergencyVehiclePrefabScanStatusLabel() => "Emergency Vehicle Prefab Scan Status";

	[Preserve]
	public static string GetEmergencyVehiclePrefabScanStatusDescription() => "Shows the last emergency vehicle prefab scan time and summary.";

	[Preserve]
	public static string GetRunSirenSetupValidationLabel() => "Run Siren Setup Validation";

	[Preserve]
	public static string GetRunSirenSetupValidationDescription() => "Checks selected sirens, fallback settings, and custom profile/file consistency.";

	// Dropdown source for vehicle assignment selectors.
	[Preserve]
	public static DropdownItem<string>[] GetVehicleSelectionOptions()
	{
		return SirenChangerMod.BuildSirenDropdownItems(includeDefault: true);
	}

	// Dropdown source for discovered emergency vehicle prefab selectors.
	[Preserve]
	public static DropdownItem<string>[] GetSpecificVehiclePrefabOptions()
	{
		return SirenChangerMod.BuildVehiclePrefabDropdownItems();
	}

	// Dropdown source for profile preview selectors (includes built-in Default sample).
	[Preserve]
	public static DropdownItem<string>[] GetPreviewableProfileOptions()
	{
		return SirenChangerMod.BuildSirenDropdownItems(includeDefault: true);
	}
	// Dropdown source for profile editor selectors.
	[Preserve]
	public static DropdownItem<string>[] GetEditableProfileOptions()
	{
		return SirenChangerMod.BuildSirenDropdownItems(includeDefault: false);
	}
	// Dropdown source for siren profile copy-source selector.
	[Preserve]
	public static DropdownItem<string>[] GetSirenCopySourceOptions()
	{
		return BuildCopySourceOptions(
			"Default (Detected Siren Template)",
			SirenChangerMod.BuildSirenDropdownItems(includeDefault: false),
			SirenChangerMod.BuildDetectedCopySourceDropdown(DeveloperAudioDomain.Siren));
	}

	// Build one copy-source list from default, custom profile options, and detected SFX options.
	private static DropdownItem<string>[] BuildCopySourceOptions(
		string defaultDisplayName,
		DropdownItem<string>[] customOptions,
		DropdownItem<string>[] detectedOptions)
	{
		List<DropdownItem<string>> combined = new List<DropdownItem<string>>();
		HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		TryAddCopySourceOption(
			combined,
			seen,
			new DropdownItem<string>
			{
				value = SirenReplacementConfig.DefaultSelectionToken,
				displayName = defaultDisplayName
			});

		TryAddCopySourceOptions(combined, seen, customOptions);
		TryAddCopySourceOptions(combined, seen, detectedOptions);

		if (combined.Count == 0)
		{
			combined.Add(new DropdownItem<string>
			{
				value = SirenReplacementConfig.DefaultSelectionToken,
				displayName = "Default",
				disabled = false
			});
		}

		return combined.ToArray();
	}

	private static void TryAddCopySourceOptions(
		ICollection<DropdownItem<string>> destination,
		ISet<string> seen,
		DropdownItem<string>[] options)
	{
		if (options == null)
		{
			return;
		}

		for (int i = 0; i < options.Length; i++)
		{
			TryAddCopySourceOption(destination, seen, options[i]);
		}
	}

	private static void TryAddCopySourceOption(
		ICollection<DropdownItem<string>> destination,
		ISet<string> seen,
		DropdownItem<string> option)
	{
		if (option.disabled)
		{
			return;
		}

		string value = option.value ?? string.Empty;
		if (string.IsNullOrWhiteSpace(value) || !seen.Add(value))
		{
			return;
		}

		destination.Add(new DropdownItem<string>
		{
			value = value,
			displayName = option.displayName,
			disabled = false
		});
	}

	// Dropdown source for fallback behavior selector.
	[Preserve]
	public static DropdownItem<int>[] GetFallbackBehaviorOptions()
	{
		return new[]
		{
			new DropdownItem<int> { value = (int)SirenFallbackBehavior.Default, displayName = "Use Default Sound" },
			new DropdownItem<int> { value = (int)SirenFallbackBehavior.Mute, displayName = "Mute Sound" },
			new DropdownItem<int> { value = (int)SirenFallbackBehavior.AlternateCustomSiren, displayName = "Use Alternate Custom Sound File" }
			};
	}

	// Dropdown source for AudioRolloffMode selector.
	[Preserve]
	public static DropdownItem<int>[] GetRolloffModeOptions()
	{
		return new[]
		{
			new DropdownItem<int> { value = (int)AudioRolloffMode.Logarithmic, displayName = "Logarithmic" },
			new DropdownItem<int> { value = (int)AudioRolloffMode.Linear, displayName = "Linear" },
			new DropdownItem<int> { value = (int)AudioRolloffMode.Custom, displayName = "Custom" }
			};
	}

		// Returns selected editable profile or template fallback.
	private static SirenSfxProfile GetEditableProfile()
	{
		if (TryGetEditableProfile(out SirenSfxProfile profile))
		{
			return profile;
		}

		return SirenChangerMod.CustomProfileTemplate;
	}

	// Resolve currently selected editable profile.
	private static bool TryGetEditableProfile(out SirenSfxProfile profile)
	{
		profile = null!;
		string key = SirenChangerMod.Config.EditProfileSelection;
		if (string.IsNullOrWhiteSpace(key))
		{
			return false;
		}

		return SirenChangerMod.Config.TryGetProfile(key, out profile);
	}

	// Resolve currently selected copy-source profile.
	private static bool TryGetCopySourceProfile(out SirenSfxProfile profile)
	{
		profile = null!;
		string key = SirenChangerMod.Config.CopyFromProfileSelection;
		if (string.IsNullOrWhiteSpace(key))
		{
			return false;
		}

		if (SirenReplacementConfig.IsDefaultSelection(key))
		{
			profile = SirenChangerMod.CustomProfileTemplate;
			return true;
		}

		if (SirenChangerMod.TryGetDetectedCopySourceProfile(DeveloperAudioDomain.Siren, key, out profile))
		{
			return true;
		}

		return SirenChangerMod.Config.TryGetProfile(key, out profile);
	}
}

