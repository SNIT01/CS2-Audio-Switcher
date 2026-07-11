using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Game.Effects;
using Game.Prefabs;
using Game.Prefabs.Effects;
using Game.UI.Widgets;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace SirenChanger;

// Engine, ambient, and building options/scanning helpers split from the core mod file.
public sealed partial class SirenChangerMod
{
	private enum AmbientTargetSection
	{
		None = 0,
		Primary = 1,
		World = 2,
		Disaster = 3
	}

	private enum BuildingTargetSection
	{
		None = 0,
		Primary = 1,
		Service = 2
	}

	private static string s_LastVehicleEnginePreviewStatus = "No vehicle engine preview has been played in this session.";

	private static string s_LastVehicleEngineEditorLinkStatus = "No advanced engine editor link action has been run in this session.";

	private static string s_LastAmbientPreviewStatus = "No ambient preview has been played in this session.";

	private static string s_LastBuildingPreviewStatus = "No building preview has been played in this session.";

	private static string s_LastUIToolPreviewStatus = "No UI/tool preview has been played in this session.";

	private static string s_SelectedAmbientDisasterTarget = string.Empty;

	private static string s_SelectedAmbientWorldTarget = string.Empty;

	private static string s_SelectedBuildingServiceTarget = string.Empty;

	private static int s_AmbientDisasterTargetDropdownCacheVersion = -1;

	private static DropdownItem<string>[] s_AmbientDisasterTargetDropdown = Array.Empty<DropdownItem<string>>();

	private static int s_AmbientWorldTargetDropdownCacheVersion = -1;

	private static DropdownItem<string>[] s_AmbientWorldTargetDropdown = Array.Empty<DropdownItem<string>>();

	private static int s_BuildingServiceTargetDropdownCacheVersion = -1;

	private static DropdownItem<string>[] s_BuildingServiceTargetDropdown = Array.Empty<DropdownItem<string>>();

	private static readonly string[] s_AmbientDisasterTokens =
	{
		"disaster",
		"flood",
		"tornado",
		"tsunami",
		"earthquake",
		"wildfire",
		"firestorm",
		"storm",
		"lightning",
		"thunder",
		"hail",
		"blizzard",
		"evac",
		"warning"
	};

	private static readonly string[] s_AmbientWorldTokens =
	{
		"world",
		"weather",
		"wind",
		"rain",
		"water",
		"ocean",
		"sea",
		"wave",
		"forest",
		"nature",
		"birds",
		"seagull",
		"environment",
		"day",
		"night",
		"season",
		"climate"
	};

	private static readonly string[] s_ServiceBuildingTokens =
	{
		"service",
		"police",
		"fire",
		"ambulance",
		"hospital",
		"clinic",
		"medical",
		"school",
		"college",
		"university",
		"welfare",
		"prison",
		"jail",
		"courthouse",
		"post",
		"mail",
		"telecom",
		"power",
		"substation",
		"water",
		"sewage",
		"waste",
		"garbage",
		"recycling",
		"landfill",
		"incinerator",
		"crematorium",
		"cemetery",
		"depot",
		"station",
		"airport",
		"harbor",
		"port",
		"maintenance",
		"snow",
		"shelter"
	};

	// Build vehicle-engine profile dropdown cache when options version changes.
	private static void EnsureVehicleEngineDropdownCacheCurrent()
	{
		if (s_EngineDropdownCacheVersion == OptionsVersion &&
			s_EngineDropdownWithDefault.Length > 0 &&
			s_EngineDropdownWithoutDefault.Length > 0)
		{
			return;
		}

		List<string> keys = VehicleEngineConfig.CustomProfiles.Keys
			.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
			.ToList();
		BuildDomainDropdownCache(
			keys,
			"No custom engine sounds found",
			out s_EngineDropdownWithDefault,
			out s_EngineDropdownWithoutDefault);
		s_EngineDropdownCacheVersion = OptionsVersion;
	}

	// Build ambient profile dropdown cache when options version changes.
	private static void EnsureAmbientDropdownCacheCurrent()
	{
		if (s_AmbientDropdownCacheVersion == OptionsVersion &&
			s_AmbientDropdownWithDefault.Length > 0 &&
			s_AmbientDropdownWithoutDefault.Length > 0)
		{
			return;
		}

		List<string> keys = AmbientConfig.CustomProfiles.Keys
			.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
			.ToList();
		BuildDomainDropdownCache(
			keys,
			"No custom ambient sounds found",
			out s_AmbientDropdownWithDefault,
			out s_AmbientDropdownWithoutDefault);
		s_AmbientDropdownCacheVersion = OptionsVersion;
	}

	// Build building profile dropdown cache when options version changes.
	private static void EnsureBuildingDropdownCacheCurrent()
	{
		if (s_BuildingDropdownCacheVersion == OptionsVersion &&
			s_BuildingDropdownWithDefault.Length > 0 &&
			s_BuildingDropdownWithoutDefault.Length > 0)
		{
			return;
		}

		List<string> keys = BuildingConfig.CustomProfiles.Keys
			.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
			.ToList();
		BuildDomainDropdownCache(
			keys,
			"No custom building sounds found",
			out s_BuildingDropdownWithDefault,
			out s_BuildingDropdownWithoutDefault);
		s_BuildingDropdownCacheVersion = OptionsVersion;
	}

	// Build UI/tool profile dropdown cache when options version changes.
	private static void EnsureUIToolDropdownCacheCurrent()
	{
		if (s_UIToolDropdownCacheVersion == OptionsVersion &&
			s_UIToolDropdownWithDefault.Length > 0 &&
			s_UIToolDropdownWithoutDefault.Length > 0)
		{
			return;
		}

		List<string> keys = UIToolConfig.CustomProfiles.Keys
			.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
			.ToList();
		BuildDomainDropdownCache(
			keys,
			"No custom UI/tool sounds found",
			out s_UIToolDropdownWithDefault,
			out s_UIToolDropdownWithoutDefault);
		s_UIToolDropdownCacheVersion = OptionsVersion;
	}

	// Shared dropdown-item builder for engine/ambient/building custom file lists.
	private static void BuildDomainDropdownCache(
		List<string> keys,
		string emptyMessage,
		out DropdownItem<string>[] withDefault,
		out DropdownItem<string>[] withoutDefault)
	{
		List<DropdownItem<string>> withDefaultList = new List<DropdownItem<string>>(keys.Count + 1)
		{
			new DropdownItem<string>
			{
				value = SirenReplacementConfig.DefaultSelectionToken,
				displayName = "Default"
			}
		};

		List<DropdownItem<string>> withoutDefaultList = new List<DropdownItem<string>>(keys.Count);
		for (int i = 0; i < keys.Count; i++)
		{
			string key = keys[i];
			DropdownItem<string> item = new DropdownItem<string>
			{
				value = key,
				displayName = FormatSirenDisplayName(key)
			};

			withDefaultList.Add(item);
			withoutDefaultList.Add(item);
		}

		if (withoutDefaultList.Count == 0)
		{
			withoutDefaultList.Add(new DropdownItem<string>
			{
				value = string.Empty,
				displayName = emptyMessage,
				disabled = true
			});
		}

		withDefault = withDefaultList.ToArray();
		withoutDefault = withoutDefaultList.ToArray();
	}

	// Rebuild discovered vehicle-engine target dropdown cache when options version changes.
	private static void EnsureVehicleEnginePrefabDropdownCurrent()
	{
		if (s_VehicleEnginePrefabDropdownCacheVersion == OptionsVersion && s_VehicleEnginePrefabDropdown.Length > 0)
		{
			return;
		}

		if (s_DiscoveredVehicleEnginePrefabs.Length == 0)
		{
			s_VehicleEnginePrefabDropdown = new[]
			{
				new DropdownItem<string>
				{
					value = string.Empty,
					displayName = "No vehicle engine targets detected",
					disabled = true
				}
			};
			s_VehicleEnginePrefabDropdownCacheVersion = OptionsVersion;
			return;
		}

		List<DropdownItem<string>> options = new List<DropdownItem<string>>(s_DiscoveredVehicleEnginePrefabs.Length);
		for (int i = 0; i < s_DiscoveredVehicleEnginePrefabs.Length; i++)
		{
			string prefabName = s_DiscoveredVehicleEnginePrefabs[i];
			options.Add(new DropdownItem<string>
			{
				value = prefabName,
				displayName = prefabName
			});
		}

		s_VehicleEnginePrefabDropdown = options.ToArray();
		s_VehicleEnginePrefabDropdownCacheVersion = OptionsVersion;
	}

	// Rebuild discovered ambient-target dropdown cache when options version changes.
	private static void EnsureAmbientTargetDropdownCurrent()
	{
		if (s_AmbientTargetDropdownCacheVersion == OptionsVersion && s_AmbientTargetDropdown.Length > 0)
		{
			return;
		}

		s_AmbientTargetDropdown = BuildFilteredTargetDropdownItems(
			s_DiscoveredAmbientTargets,
			static targetName => ClassifyAmbientTargetSection(targetName) == AmbientTargetSection.Primary,
			"No ambient targets detected");
		s_AmbientTargetDropdownCacheVersion = OptionsVersion;
	}

	// Rebuild discovered building-target dropdown cache when options version changes.
	private static void EnsureBuildingTargetDropdownCurrent()
	{
		if (s_BuildingTargetDropdownCacheVersion == OptionsVersion && s_BuildingTargetDropdown.Length > 0)
		{
			return;
		}

		s_BuildingTargetDropdown = BuildFilteredTargetDropdownItems(
			s_DiscoveredBuildingTargets,
			static targetName => ClassifyBuildingTargetSection(targetName) == BuildingTargetSection.Primary,
			"No building targets detected");
		s_BuildingTargetDropdownCacheVersion = OptionsVersion;
	}

	// Rebuild discovered disaster ambient-target dropdown cache when options version changes.
	private static void EnsureAmbientDisasterTargetDropdownCurrent()
	{
		if (s_AmbientDisasterTargetDropdownCacheVersion == OptionsVersion && s_AmbientDisasterTargetDropdown.Length > 0)
		{
			return;
		}

		s_AmbientDisasterTargetDropdown = BuildFilteredTargetDropdownItems(
			s_DiscoveredAmbientTargets,
			static targetName => ClassifyAmbientTargetSection(targetName) == AmbientTargetSection.Disaster,
			"No disaster ambient targets detected");
		s_AmbientDisasterTargetDropdownCacheVersion = OptionsVersion;
	}

	// Rebuild discovered world ambient-target dropdown cache when options version changes.
	private static void EnsureAmbientWorldTargetDropdownCurrent()
	{
		if (s_AmbientWorldTargetDropdownCacheVersion == OptionsVersion && s_AmbientWorldTargetDropdown.Length > 0)
		{
			return;
		}

		s_AmbientWorldTargetDropdown = BuildFilteredTargetDropdownItems(
			s_DiscoveredAmbientTargets,
			static targetName => ClassifyAmbientTargetSection(targetName) == AmbientTargetSection.World,
			"No world ambient targets detected");
		s_AmbientWorldTargetDropdownCacheVersion = OptionsVersion;
	}

	// Rebuild discovered service-building target dropdown cache when options version changes.
	private static void EnsureBuildingServiceTargetDropdownCurrent()
	{
		if (s_BuildingServiceTargetDropdownCacheVersion == OptionsVersion && s_BuildingServiceTargetDropdown.Length > 0)
		{
			return;
		}

		s_BuildingServiceTargetDropdown = BuildFilteredTargetDropdownItems(
			s_DiscoveredBuildingTargets,
			static targetName => ClassifyBuildingTargetSection(targetName) == BuildingTargetSection.Service,
			"No service building targets detected");
		s_BuildingServiceTargetDropdownCacheVersion = OptionsVersion;
	}

	private static DropdownItem<string>[] BuildFilteredTargetDropdownItems(
		IReadOnlyList<string> sourceTargets,
		Func<string, bool> includeTarget,
		string emptyMessage)
	{
		List<DropdownItem<string>> options = new List<DropdownItem<string>>(sourceTargets.Count);
		for (int i = 0; i < sourceTargets.Count; i++)
		{
			string targetName = sourceTargets[i];
			if (!includeTarget(targetName))
			{
				continue;
			}

			options.Add(new DropdownItem<string>
			{
				value = targetName,
				displayName = targetName
			});
		}

		if (options.Count == 0)
		{
			options.Add(new DropdownItem<string>
			{
				value = string.Empty,
				displayName = emptyMessage,
				disabled = true
			});
		}

		return options.ToArray();
	}

	// Rebuild discovered UI/tool-target dropdown cache when options version changes.
	private static void EnsureUIToolTargetDropdownCurrent()
	{
		if (s_UIToolTargetDropdownCacheVersion == OptionsVersion && s_UIToolTargetDropdown.Length > 0)
		{
			return;
		}

		if (s_DiscoveredUIToolTargets.Length == 0)
		{
			s_UIToolTargetDropdown = new[]
			{
				new DropdownItem<string>
				{
					value = string.Empty,
					displayName = "No UI/tool targets detected",
					disabled = true
				}
			};
			s_UIToolTargetDropdownCacheVersion = OptionsVersion;
			return;
		}

		List<DropdownItem<string>> options = new List<DropdownItem<string>>(s_DiscoveredUIToolTargets.Length);
		for (int i = 0; i < s_DiscoveredUIToolTargets.Length; i++)
		{
			string targetName = s_DiscoveredUIToolTargets[i];
			options.Add(new DropdownItem<string>
			{
				value = targetName,
				displayName = targetName
			});
		}

		s_UIToolTargetDropdown = options.ToArray();
		s_UIToolTargetDropdownCacheVersion = OptionsVersion;
	}

	// Set selected vehicle-engine target key in options UI.
	internal static void SetVehicleEngineTargetSelectionTargetFromOptions(string vehiclePrefabName)
	{
		string previous = VehicleEngineConfig.TargetSelectionTarget;
		VehicleEngineConfig.SetTargetSelectionTarget(vehiclePrefabName);
		if (!string.Equals(previous, VehicleEngineConfig.TargetSelectionTarget, StringComparison.Ordinal))
		{
			OptionsVersion++;
		}
	}

	// Get selected vehicle-engine override for the currently selected target.
	internal static string GetSelectedVehicleEngineTargetSelectionForOptions()
	{
		string key = VehicleEngineConfig.TargetSelectionTarget;
		if (string.IsNullOrWhiteSpace(key))
		{
			return SirenReplacementConfig.DefaultSelectionToken;
		}

		return VehicleEngineConfig.GetTargetSelection(key);
	}

	// Set vehicle-engine override for the currently selected target.
	internal static void SetSelectedVehicleEngineTargetSelectionFromOptions(string selection)
	{
		string key = VehicleEngineConfig.TargetSelectionTarget;
		if (string.IsNullOrWhiteSpace(key))
		{
			return;
		}

		if (VehicleEngineConfig.SetTargetSelection(key, selection))
		{
			OptionsVersion++;
		}
	}

	// Read-only status text for vehicle-engine override controls.
	internal static string GetSelectedVehicleEngineOverrideStatusText()
	{
		if (s_DiscoveredVehicleEnginePrefabs.Length == 0)
		{
			return "No vehicle engine targets detected yet. Click Rescan Vehicle Engine Prefabs in a loaded map/editor session.";
		}

		string key = VehicleEngineConfig.TargetSelectionTarget;
		if (string.IsNullOrWhiteSpace(key))
		{
			return "Select a vehicle prefab to edit its engine sound override.";
		}

		string selection = VehicleEngineConfig.GetTargetSelection(key);
		if (AudioReplacementDomainConfig.IsDefaultSelection(selection))
		{
			return $"'{key}' uses the engine default selection.";
		}

		return $"'{key}' override: {FormatSirenDisplayName(selection)}";
	}

	// Set selected ambient target key in options UI.
	internal static void SetAmbientTargetSelectionTargetFromOptions(string targetName)
	{
		string normalized = AudioReplacementDomainConfig.NormalizeTargetKey(targetName);
		if (!IsAmbientPrimaryTargetKey(normalized))
		{
			normalized = string.Empty;
		}

		string previous = AmbientConfig.TargetSelectionTarget;
		AmbientConfig.SetTargetSelectionTarget(normalized);
		if (!string.Equals(previous, normalized, StringComparison.Ordinal))
		{
			OptionsVersion++;
		}
	}

	// Get selected ambient target key for the non-disaster override section.
	internal static string GetAmbientTargetSelectionTargetForOptions()
	{
		return GetActiveAmbientPrimaryTargetKey();
	}

	// Get selected ambient override for the currently selected target.
	internal static string GetSelectedAmbientTargetSelectionForOptions()
	{
		string key = GetActiveAmbientPrimaryTargetKey();
		if (string.IsNullOrWhiteSpace(key))
		{
			return SirenReplacementConfig.DefaultSelectionToken;
		}

		return AmbientConfig.GetTargetSelection(key);
	}

	// Set ambient override for the currently selected target.
	internal static void SetSelectedAmbientTargetSelectionFromOptions(string selection)
	{
		string key = GetActiveAmbientPrimaryTargetKey();
		if (string.IsNullOrWhiteSpace(key))
		{
			return;
		}

		if (AmbientConfig.SetTargetSelection(key, selection))
		{
			OptionsVersion++;
		}
	}

	// Read-only status text for ambient override controls.
	internal static string GetSelectedAmbientOverrideStatusText()
	{
		if (!HasDiscoveredAmbientPrimaryTargets())
		{
			return "No ambient targets detected yet. Click Rescan Ambient Targets in a loaded map/editor session.";
		}

		string key = GetActiveAmbientPrimaryTargetKey();
		if (string.IsNullOrWhiteSpace(key))
		{
			return "Select an ambient target to edit its sound override.";
		}

		string selection = AmbientConfig.GetTargetSelection(key);
		if (AudioReplacementDomainConfig.IsDefaultSelection(selection))
		{
			return $"'{key}' uses the ambient default selection.";
		}

		return $"'{key}' override: {FormatSirenDisplayName(selection)}";
	}

	// Link the advanced engine profile editor to the currently selected vehicle override profile.
	internal static void LinkVehicleEngineEditorToSelectedOverrideFromOptions()
	{
		string targetKey = AudioReplacementDomainConfig.NormalizeTargetKey(VehicleEngineConfig.TargetSelectionTarget);
		if (string.IsNullOrWhiteSpace(targetKey))
		{
			s_LastVehicleEngineEditorLinkStatus = "Select a vehicle prefab first, then choose an override sound to edit.";
			OptionsVersion++;
			return;
		}

		string selection = AudioReplacementDomainConfig.NormalizeProfileKey(VehicleEngineConfig.GetTargetSelection(targetKey));
		if (AudioReplacementDomainConfig.IsDefaultSelection(selection))
		{
			s_LastVehicleEngineEditorLinkStatus = $"'{targetKey}' currently uses Default. Pick a concrete override sound first.";
			OptionsVersion++;
			return;
		}

		if (string.IsNullOrWhiteSpace(selection) || !VehicleEngineConfig.TryGetProfile(selection, out _))
		{
			s_LastVehicleEngineEditorLinkStatus = $"Cannot link advanced editor: override profile '{selection}' is unavailable.";
			OptionsVersion++;
			return;
		}

		bool changed = false;
		if (!string.Equals(VehicleEngineConfig.EditProfileSelection, selection, StringComparison.Ordinal))
		{
			VehicleEngineConfig.EditProfileSelection = selection;
			changed = true;
		}

		string copySource = AudioReplacementDomainConfig.NormalizeProfileKey(VehicleEngineConfig.CopyFromProfileSelection);
		if (string.IsNullOrWhiteSpace(copySource) || !VehicleEngineConfig.TryGetProfile(copySource, out _))
		{
			VehicleEngineConfig.CopyFromProfileSelection = selection;
			changed = true;
		}

		int linkedVehicleCount = CountVehicleEngineOverridesUsingProfile(selection);
		string displayName = FormatSirenDisplayName(selection);
		s_LastVehicleEngineEditorLinkStatus =
			$"Advanced editor now targets '{displayName}'. This profile is used by {linkedVehicleCount} vehicle override(s).";
		if (changed)
		{
			SaveAudioDomainConfig(DeveloperAudioDomain.VehicleEngine);
			MarkAudioDomainConfigChanged(DeveloperAudioDomain.VehicleEngine);
		}

		OptionsVersion++;
	}

	// Read-only status text for the latest advanced-editor link action.
	internal static string GetVehicleEngineEditorLinkStatusText()
	{
		return s_LastVehicleEngineEditorLinkStatus;
	}

	// Count how many vehicle-prefab overrides currently reference one profile key.
	private static int CountVehicleEngineOverridesUsingProfile(string profileKey)
	{
		if (string.IsNullOrWhiteSpace(profileKey) || VehicleEngineConfig.TargetSelections == null)
		{
			return 0;
		}

		string normalizedProfileKey = AudioReplacementDomainConfig.NormalizeProfileKey(profileKey);
		int count = 0;
		foreach (KeyValuePair<string, string> pair in VehicleEngineConfig.TargetSelections)
		{
			string normalizedSelection = AudioReplacementDomainConfig.NormalizeProfileKey(pair.Value);
			if (string.Equals(normalizedSelection, normalizedProfileKey, StringComparison.OrdinalIgnoreCase))
			{
				count++;
			}
		}

		return count;
	}

	// Set selected building target key in options UI.
	internal static void SetBuildingTargetSelectionTargetFromOptions(string targetName)
	{
		string normalized = AudioReplacementDomainConfig.NormalizeTargetKey(targetName);
		if (!IsBuildingPrimaryTargetKey(normalized))
		{
			normalized = string.Empty;
		}

		string previous = BuildingConfig.TargetSelectionTarget;
		BuildingConfig.SetTargetSelectionTarget(normalized);
		if (!string.Equals(previous, normalized, StringComparison.Ordinal))
		{
			OptionsVersion++;
		}
	}

	// Get selected building target key for the non-service override section.
	internal static string GetBuildingTargetSelectionTargetForOptions()
	{
		return GetActiveBuildingPrimaryTargetKey();
	}

	// Get selected building override for the currently selected target.
	internal static string GetSelectedBuildingTargetSelectionForOptions()
	{
		string key = GetActiveBuildingPrimaryTargetKey();
		if (string.IsNullOrWhiteSpace(key))
		{
			return SirenReplacementConfig.DefaultSelectionToken;
		}

		return BuildingConfig.GetTargetSelection(key);
	}

	// Set building override for the currently selected target.
	internal static void SetSelectedBuildingTargetSelectionFromOptions(string selection)
	{
		string key = GetActiveBuildingPrimaryTargetKey();
		if (string.IsNullOrWhiteSpace(key))
		{
			return;
		}

		if (BuildingConfig.SetTargetSelection(key, selection))
		{
			OptionsVersion++;
		}
	}

	// Read-only status text for building override controls.
	internal static string GetSelectedBuildingOverrideStatusText()
	{
		if (!HasDiscoveredBuildingPrimaryTargets())
		{
			return "No building targets detected yet. Click Rescan Building Targets in a loaded map/editor session.";
		}

		string key = GetActiveBuildingPrimaryTargetKey();
		if (string.IsNullOrWhiteSpace(key))
		{
			return "Select a building target to edit its sound override.";
		}

		string selection = BuildingConfig.GetTargetSelection(key);
		if (AudioReplacementDomainConfig.IsDefaultSelection(selection))
		{
			return $"'{key}' uses the building default selection.";
		}

		return $"'{key}' override: {FormatSirenDisplayName(selection)}";
	}

	// Build dropdown data for discovered service-building target selectors.
	internal static DropdownItem<string>[] BuildBuildingServiceTargetDropdownItems()
	{
		EnsureBuildingServiceTargetDropdownCurrent();
		return s_BuildingServiceTargetDropdown;
	}

	// Set selected service-building target key in options UI.
	internal static void SetBuildingServiceTargetSelectionTargetFromOptions(string targetName)
	{
		string normalized = AudioReplacementDomainConfig.NormalizeTargetKey(targetName);
		if (!IsServiceBuildingTargetKey(normalized))
		{
			normalized = string.Empty;
		}

		if (!string.Equals(s_SelectedBuildingServiceTarget, normalized, StringComparison.Ordinal))
		{
			s_SelectedBuildingServiceTarget = normalized;
			OptionsVersion++;
		}
	}

	// Get selected service-building target key for options UI.
	internal static string GetBuildingServiceTargetSelectionTargetForOptions()
	{
		return GetActiveBuildingServiceTargetKey();
	}

	// Get selected building override for the currently selected service-building target.
	internal static string GetSelectedBuildingServiceTargetSelectionForOptions()
	{
		string key = GetActiveBuildingServiceTargetKey();
		if (string.IsNullOrWhiteSpace(key))
		{
			return SirenReplacementConfig.DefaultSelectionToken;
		}

		return BuildingConfig.GetTargetSelection(key);
	}

	// Set building override for the currently selected service-building target.
	internal static void SetSelectedBuildingServiceTargetSelectionFromOptions(string selection)
	{
		string key = GetActiveBuildingServiceTargetKey();
		if (string.IsNullOrWhiteSpace(key))
		{
			return;
		}

		if (BuildingConfig.SetTargetSelection(key, selection))
		{
			OptionsVersion++;
		}
	}

	// Read-only status text for service-building override controls.
	internal static string GetSelectedBuildingServiceOverrideStatusText()
	{
		if (!HasDiscoveredBuildingServiceTargets())
		{
			return "No service building targets detected yet. Click Rescan Building Targets in a loaded map/editor session.";
		}

		string key = GetActiveBuildingServiceTargetKey();
		if (string.IsNullOrWhiteSpace(key))
		{
			return "Select a service building target to edit its sound override.";
		}

		string selection = BuildingConfig.GetTargetSelection(key);
		if (AudioReplacementDomainConfig.IsDefaultSelection(selection))
		{
			return $"'{key}' uses the building default selection.";
		}

		return $"'{key}' override: {FormatSirenDisplayName(selection)}";
	}

	// Build dropdown data for discovered disaster ambient target selectors.
	internal static DropdownItem<string>[] BuildAmbientDisasterTargetDropdownItems()
	{
		EnsureAmbientDisasterTargetDropdownCurrent();
		return s_AmbientDisasterTargetDropdown;
	}

	// Set selected disaster ambient target key in options UI.
	internal static void SetAmbientDisasterTargetSelectionTargetFromOptions(string targetName)
	{
		string normalized = AudioReplacementDomainConfig.NormalizeTargetKey(targetName);
		if (!IsDisasterAmbientTargetKey(normalized))
		{
			normalized = string.Empty;
		}

		if (!string.Equals(s_SelectedAmbientDisasterTarget, normalized, StringComparison.Ordinal))
		{
			s_SelectedAmbientDisasterTarget = normalized;
			OptionsVersion++;
		}
	}

	// Get selected disaster ambient target key for options UI.
	internal static string GetAmbientDisasterTargetSelectionTargetForOptions()
	{
		return GetActiveAmbientDisasterTargetKey();
	}

	// Get selected ambient override for the currently selected disaster target.
	internal static string GetSelectedAmbientDisasterTargetSelectionForOptions()
	{
		string key = GetActiveAmbientDisasterTargetKey();
		if (string.IsNullOrWhiteSpace(key))
		{
			return SirenReplacementConfig.DefaultSelectionToken;
		}

		return AmbientConfig.GetTargetSelection(key);
	}

	// Set ambient override for the currently selected disaster target.
	internal static void SetSelectedAmbientDisasterTargetSelectionFromOptions(string selection)
	{
		string key = GetActiveAmbientDisasterTargetKey();
		if (string.IsNullOrWhiteSpace(key))
		{
			return;
		}

		if (AmbientConfig.SetTargetSelection(key, selection))
		{
			OptionsVersion++;
		}
	}

	// Read-only status text for disaster ambient override controls.
	internal static string GetSelectedAmbientDisasterOverrideStatusText()
	{
		if (!HasDiscoveredAmbientDisasterTargets())
		{
			return "No disaster ambient targets detected yet. Click Rescan Ambient Targets in a loaded map/editor session.";
		}

		string key = GetActiveAmbientDisasterTargetKey();
		if (string.IsNullOrWhiteSpace(key))
		{
			return "Select a disaster ambient target to edit its sound override.";
		}

		string selection = AmbientConfig.GetTargetSelection(key);
		if (AudioReplacementDomainConfig.IsDefaultSelection(selection))
		{
			return $"'{key}' uses the ambient default selection.";
		}

		return $"'{key}' override: {FormatSirenDisplayName(selection)}";
	}

	// Build dropdown data for discovered world ambient target selectors.
	internal static DropdownItem<string>[] BuildAmbientWorldTargetDropdownItems()
	{
		EnsureAmbientWorldTargetDropdownCurrent();
		return s_AmbientWorldTargetDropdown;
	}

	// Set selected world ambient target key in options UI.
	internal static void SetAmbientWorldTargetSelectionTargetFromOptions(string targetName)
	{
		string normalized = AudioReplacementDomainConfig.NormalizeTargetKey(targetName);
		if (!IsWorldAmbientTargetKey(normalized))
		{
			normalized = string.Empty;
		}

		if (!string.Equals(s_SelectedAmbientWorldTarget, normalized, StringComparison.Ordinal))
		{
			s_SelectedAmbientWorldTarget = normalized;
			OptionsVersion++;
		}
	}

	// Get selected world ambient target key for options UI.
	internal static string GetAmbientWorldTargetSelectionTargetForOptions()
	{
		return GetActiveAmbientWorldTargetKey();
	}

	// Get selected ambient override for the currently selected world target.
	internal static string GetSelectedAmbientWorldTargetSelectionForOptions()
	{
		string key = GetActiveAmbientWorldTargetKey();
		if (string.IsNullOrWhiteSpace(key))
		{
			return SirenReplacementConfig.DefaultSelectionToken;
		}

		return AmbientConfig.GetTargetSelection(key);
	}

	// Set ambient override for the currently selected world target.
	internal static void SetSelectedAmbientWorldTargetSelectionFromOptions(string selection)
	{
		string key = GetActiveAmbientWorldTargetKey();
		if (string.IsNullOrWhiteSpace(key))
		{
			return;
		}

		if (AmbientConfig.SetTargetSelection(key, selection))
		{
			OptionsVersion++;
		}
	}

	// Read-only status text for world ambient override controls.
	internal static string GetSelectedAmbientWorldOverrideStatusText()
	{
		if (!HasDiscoveredAmbientWorldTargets())
		{
			return "No world ambient targets detected yet. Click Rescan Ambient Targets in a loaded map/editor session.";
		}

		string key = GetActiveAmbientWorldTargetKey();
		if (string.IsNullOrWhiteSpace(key))
		{
			return "Select a world ambient target to edit its sound override.";
		}

		string selection = AmbientConfig.GetTargetSelection(key);
		if (AudioReplacementDomainConfig.IsDefaultSelection(selection))
		{
			return $"'{key}' uses the ambient default selection.";
		}

		return $"'{key}' override: {FormatSirenDisplayName(selection)}";
	}

	// Returns true when at least one ambient target outside world/disaster sections is available.
	internal static bool HasDiscoveredAmbientPrimaryTargets()
	{
		for (int i = 0; i < s_DiscoveredAmbientTargets.Length; i++)
		{
			if (ClassifyAmbientTargetSection(s_DiscoveredAmbientTargets[i]) == AmbientTargetSection.Primary)
			{
				return true;
			}
		}

		return false;
	}

	// Returns true when at least one disaster ambient target is available.
	internal static bool HasDiscoveredAmbientDisasterTargets()
	{
		for (int i = 0; i < s_DiscoveredAmbientTargets.Length; i++)
		{
			if (ClassifyAmbientTargetSection(s_DiscoveredAmbientTargets[i]) == AmbientTargetSection.Disaster)
			{
				return true;
			}
		}

		return false;
	}

	// Returns true when at least one world ambient target is available.
	internal static bool HasDiscoveredAmbientWorldTargets()
	{
		for (int i = 0; i < s_DiscoveredAmbientTargets.Length; i++)
		{
			if (ClassifyAmbientTargetSection(s_DiscoveredAmbientTargets[i]) == AmbientTargetSection.World)
			{
				return true;
			}
		}

		return false;
	}

	// Returns true when at least one non-service building target is available.
	internal static bool HasDiscoveredBuildingPrimaryTargets()
	{
		for (int i = 0; i < s_DiscoveredBuildingTargets.Length; i++)
		{
			if (ClassifyBuildingTargetSection(s_DiscoveredBuildingTargets[i]) == BuildingTargetSection.Primary)
			{
				return true;
			}
		}

		return false;
	}

	// Returns true when at least one service-building target is available.
	internal static bool HasDiscoveredBuildingServiceTargets()
	{
		for (int i = 0; i < s_DiscoveredBuildingTargets.Length; i++)
		{
			if (ClassifyBuildingTargetSection(s_DiscoveredBuildingTargets[i]) == BuildingTargetSection.Service)
			{
				return true;
			}
		}

		return false;
	}

	private static string GetActiveAmbientPrimaryTargetKey()
	{
		string key = AudioReplacementDomainConfig.NormalizeTargetKey(AmbientConfig.TargetSelectionTarget);
		return ClassifyAmbientTargetSection(key) == AmbientTargetSection.Primary ? key : string.Empty;
	}

	private static string GetActiveAmbientDisasterTargetKey()
	{
		string key = AudioReplacementDomainConfig.NormalizeTargetKey(s_SelectedAmbientDisasterTarget);
		return ClassifyAmbientTargetSection(key) == AmbientTargetSection.Disaster ? key : string.Empty;
	}

	private static string GetActiveAmbientWorldTargetKey()
	{
		string key = AudioReplacementDomainConfig.NormalizeTargetKey(s_SelectedAmbientWorldTarget);
		return ClassifyAmbientTargetSection(key) == AmbientTargetSection.World ? key : string.Empty;
	}

	private static string GetActiveBuildingPrimaryTargetKey()
	{
		string key = AudioReplacementDomainConfig.NormalizeTargetKey(BuildingConfig.TargetSelectionTarget);
		return ClassifyBuildingTargetSection(key) == BuildingTargetSection.Primary ? key : string.Empty;
	}

	private static string GetActiveBuildingServiceTargetKey()
	{
		string key = AudioReplacementDomainConfig.NormalizeTargetKey(s_SelectedBuildingServiceTarget);
		return ClassifyBuildingTargetSection(key) == BuildingTargetSection.Service ? key : string.Empty;
	}

	private static bool IsAmbientPrimaryTargetKey(string targetKey)
	{
		return ClassifyAmbientTargetSection(targetKey) == AmbientTargetSection.Primary;
	}

	private static bool IsDisasterAmbientTargetKey(string targetKey)
	{
		return ClassifyAmbientTargetSection(targetKey) == AmbientTargetSection.Disaster;
	}

	private static bool IsWorldAmbientTargetKey(string targetKey)
	{
		return ClassifyAmbientTargetSection(targetKey) == AmbientTargetSection.World;
	}

	private static bool IsBuildingPrimaryTargetKey(string targetKey)
	{
		return ClassifyBuildingTargetSection(targetKey) == BuildingTargetSection.Primary;
	}

	private static bool IsServiceBuildingTargetKey(string targetKey)
	{
		return ClassifyBuildingTargetSection(targetKey) == BuildingTargetSection.Service;
	}

	private static AmbientTargetSection ClassifyAmbientTargetSection(string targetKey)
	{
		string normalized = AudioReplacementDomainConfig.NormalizeTargetKey(targetKey);
		if (!IsKnownAmbientTargetKey(normalized))
		{
			return AmbientTargetSection.None;
		}

		if (ContainsAnyToken(normalized, s_AmbientDisasterTokens))
		{
			return AmbientTargetSection.Disaster;
		}

		if (ContainsAnyToken(normalized, s_AmbientWorldTokens))
		{
			return AmbientTargetSection.World;
		}

		return AmbientTargetSection.Primary;
	}

	private static BuildingTargetSection ClassifyBuildingTargetSection(string targetKey)
	{
		string normalized = AudioReplacementDomainConfig.NormalizeTargetKey(targetKey);
		if (!IsKnownBuildingTargetKey(normalized))
		{
			return BuildingTargetSection.None;
		}

		if (ContainsAnyToken(normalized, s_ServiceBuildingTokens))
		{
			return BuildingTargetSection.Service;
		}

		return BuildingTargetSection.Primary;
	}

	private static bool IsKnownAmbientTargetKey(string targetKey)
	{
		return !string.IsNullOrWhiteSpace(targetKey) &&
			Array.Exists(s_DiscoveredAmbientTargets, existing => string.Equals(existing, targetKey, StringComparison.OrdinalIgnoreCase));
	}

	private static bool IsKnownBuildingTargetKey(string targetKey)
	{
		return !string.IsNullOrWhiteSpace(targetKey) &&
			Array.Exists(s_DiscoveredBuildingTargets, existing => string.Equals(existing, targetKey, StringComparison.OrdinalIgnoreCase));
	}

	private static bool ContainsAnyToken(string value, IReadOnlyList<string> tokens)
	{
		for (int i = 0; i < tokens.Count; i++)
		{
			if (ContainsTextToken(value, tokens[i]))
			{
				return true;
			}
		}

		return false;
	}

	// Set selected UI/tool target key in options UI.
	internal static void SetUIToolTargetSelectionTargetFromOptions(string targetName)
	{
		string previous = UIToolConfig.TargetSelectionTarget;
		UIToolConfig.SetTargetSelectionTarget(targetName);
		if (!string.Equals(previous, UIToolConfig.TargetSelectionTarget, StringComparison.Ordinal))
		{
			OptionsVersion++;
		}
	}

	// Get selected UI/tool override for the currently selected target.
	internal static string GetSelectedUIToolTargetSelectionForOptions()
	{
		string key = UIToolConfig.TargetSelectionTarget;
		if (string.IsNullOrWhiteSpace(key))
		{
			return SirenReplacementConfig.DefaultSelectionToken;
		}

		return UIToolConfig.GetTargetSelection(key);
	}

	// Set UI/tool override for the currently selected target.
	internal static void SetSelectedUIToolTargetSelectionFromOptions(string selection)
	{
		string key = UIToolConfig.TargetSelectionTarget;
		if (string.IsNullOrWhiteSpace(key))
		{
			return;
		}

		if (UIToolConfig.SetTargetSelection(key, selection))
		{
			OptionsVersion++;
		}
	}

	// Read-only status text for UI/tool override controls.
	internal static string GetSelectedUIToolOverrideStatusText()
	{
		if (s_DiscoveredUIToolTargets.Length == 0)
		{
			return "No UI/tool targets detected yet. Click Rescan UI/Tool Targets in a loaded map/editor session.";
		}

		string key = UIToolConfig.TargetSelectionTarget;
		if (string.IsNullOrWhiteSpace(key))
		{
			return "Select a UI/tool target to edit its sound override.";
		}

		string selection = UIToolConfig.GetTargetSelection(key);
		if (AudioReplacementDomainConfig.IsDefaultSelection(selection))
		{
			return $"'{key}' uses the UI/tool default selection.";
		}

		return $"'{key}' override: {FormatSirenDisplayName(selection)}";
	}

	// Rescan custom engine files and refresh options state.
	internal static void RefreshCustomVehicleEnginesFromOptions()
	{
		SyncCustomVehicleEngineCatalog(saveIfChanged: true, forceStatusRefresh: true);
	}

	// Rescan custom ambient files and refresh options state.
	internal static void RefreshCustomAmbientFromOptions()
	{
		SyncCustomAmbientCatalog(saveIfChanged: true, forceStatusRefresh: true);
	}

	// Rescan custom building files and refresh options state.
	internal static void RefreshCustomBuildingsFromOptions()
	{
		SyncCustomBuildingCatalog(saveIfChanged: true, forceStatusRefresh: true);
	}

	// Rescan custom UI/tool files and refresh options state.
	internal static void RefreshCustomUIToolFromOptions()
	{
		SyncCustomUIToolCatalog(saveIfChanged: true, forceStatusRefresh: true);
	}

	// Scan loaded prefabs for vehicle-engine targets and refresh per-vehicle options.
	internal static void RefreshVehicleEnginePrefabsFromOptions()
	{
		if (!TryScanVehicleEnginePrefabs(out List<string> discovered, out string status))
		{
			s_LastVehicleEnginePrefabScanStatus = status;
			if (UpdateDomainTargetScanMetadata(VehicleEngineConfig, s_LastVehicleEnginePrefabScanStatus, forceTimestampRefresh: true))
			{
				SaveAudioDomainConfig(DeveloperAudioDomain.VehicleEngine);
			}

			OptionsVersion++;
			return;
		}

		SetDiscoveredVehicleEnginePrefabs(discovered);
		s_LastVehicleEnginePrefabScanStatus = discovered.Count > 0
			? $"{status}\nDetected: {discovered.Count} prefab(s)."
			: $"{status}\nNo vehicle engine prefabs were found in the active world.";
		if (UpdateDomainTargetScanMetadata(VehicleEngineConfig, s_LastVehicleEnginePrefabScanStatus, forceTimestampRefresh: true))
		{
			SaveAudioDomainConfig(DeveloperAudioDomain.VehicleEngine);
		}

		OptionsVersion++;
	}

	// Scan loaded prefabs for ambient targets and refresh per-target options.
	internal static void RefreshAmbientTargetsFromOptions()
	{
		if (!TryScanAmbientTargets(out List<string> discovered, out string status))
		{
			s_LastAmbientTargetScanStatus = status;
			if (UpdateDomainTargetScanMetadata(AmbientConfig, s_LastAmbientTargetScanStatus, forceTimestampRefresh: true))
			{
				SaveAudioDomainConfig(DeveloperAudioDomain.Ambient);
			}

			OptionsVersion++;
			return;
		}

		SetDiscoveredAmbientTargets(discovered);
		s_LastAmbientTargetScanStatus = discovered.Count > 0
			? $"{status}\nDetected: {discovered.Count} target(s)."
			: $"{status}\nNo ambient targets were found in the active world.";
		if (UpdateDomainTargetScanMetadata(AmbientConfig, s_LastAmbientTargetScanStatus, forceTimestampRefresh: true))
		{
			SaveAudioDomainConfig(DeveloperAudioDomain.Ambient);
		}

		OptionsVersion++;
	}

	// Scan loaded prefabs for building targets and refresh per-target options.
	internal static void RefreshBuildingTargetsFromOptions()
	{
		if (!TryScanBuildingTargets(out List<string> discovered, out string status))
		{
			s_LastBuildingTargetScanStatus = status;
			if (UpdateDomainTargetScanMetadata(BuildingConfig, s_LastBuildingTargetScanStatus, forceTimestampRefresh: true))
			{
				SaveAudioDomainConfig(DeveloperAudioDomain.Building);
			}

			OptionsVersion++;
			return;
		}

		SetDiscoveredBuildingTargets(discovered);
		s_LastBuildingTargetScanStatus = discovered.Count > 0
			? $"{status}\nDetected: {discovered.Count} target(s)."
			: $"{status}\nNo building targets were found in the active world.";
		if (UpdateDomainTargetScanMetadata(BuildingConfig, s_LastBuildingTargetScanStatus, forceTimestampRefresh: true))
		{
			SaveAudioDomainConfig(DeveloperAudioDomain.Building);
		}

		OptionsVersion++;
	}

	// Scan loaded prefabs for UI/tool targets and refresh per-target options.
	internal static void RefreshUIToolTargetsFromOptions()
	{
		if (!TryScanUIToolTargets(out List<string> discovered, out string status))
		{
			s_LastUIToolTargetScanStatus = status;
			if (UpdateDomainTargetScanMetadata(UIToolConfig, s_LastUIToolTargetScanStatus, forceTimestampRefresh: true))
			{
				SaveAudioDomainConfig(DeveloperAudioDomain.UITool);
			}

			OptionsVersion++;
			return;
		}

		SetDiscoveredUIToolTargets(discovered);
		s_LastUIToolTargetScanStatus = discovered.Count > 0
			? $"{status}\nDetected: {discovered.Count} target(s)."
			: $"{status}\nNo UI/tool targets were found in the active world.";
		if (UpdateDomainTargetScanMetadata(UIToolConfig, s_LastUIToolTargetScanStatus, forceTimestampRefresh: true))
		{
			SaveAudioDomainConfig(DeveloperAudioDomain.UITool);
		}

		OptionsVersion++;
	}

	// Status text for vehicle-engine target scans.
	internal static string GetVehicleEnginePrefabScanStatusText()
	{
		return s_LastVehicleEnginePrefabScanStatus;
	}

	// Status text for ambient target scans.
	internal static string GetAmbientTargetScanStatusText()
	{
		return s_LastAmbientTargetScanStatus;
	}

	// Status text for building target scans.
	internal static string GetBuildingTargetScanStatusText()
	{
		return s_LastBuildingTargetScanStatus;
	}

	// Status text for UI/tool target scans.
	internal static string GetUIToolTargetScanStatusText()
	{
		return s_LastUIToolTargetScanStatus;
	}

	// Status text for vehicle-engine custom file scans.
	internal static string GetVehicleEngineCatalogScanStatusText()
	{
		return BuildDomainCatalogScanStatusText(VehicleEngineConfig, "Rescan Custom Engine Files");
	}

	// Status text for ambient custom file scans.
	internal static string GetAmbientCatalogScanStatusText()
	{
		return BuildDomainCatalogScanStatusText(AmbientConfig, "Rescan Custom Ambient Files");
	}

	// Status text for building custom file scans.
	internal static string GetBuildingCatalogScanStatusText()
	{
		return BuildDomainCatalogScanStatusText(BuildingConfig, "Rescan Custom Building Files");
	}

	// Status text for UI/tool custom file scans.
	internal static string GetUIToolCatalogScanStatusText()
	{
		return BuildDomainCatalogScanStatusText(UIToolConfig, "Rescan Custom UI/Tool Files");
	}

	// Preview status text for vehicle-engine profile preview action.
	internal static string GetVehicleEnginePreviewStatusText()
	{
		return s_LastVehicleEnginePreviewStatus;
	}

	// Preview status text for ambient profile preview action.
	internal static string GetAmbientPreviewStatusText()
	{
		return s_LastAmbientPreviewStatus;
	}

	// Preview status text for building profile preview action.
	internal static string GetBuildingPreviewStatusText()
	{
		return s_LastBuildingPreviewStatus;
	}

	// Preview status text for UI/tool profile preview action.
	internal static string GetUIToolPreviewStatusText()
	{
		return s_LastUIToolPreviewStatus;
	}

	// Play the currently selected vehicle-engine profile once.
	internal static void PreviewSelectedVehicleEngineProfileFromOptions()
	{
		PreviewDomainProfileFromOptions(
			DeveloperAudioDomain.VehicleEngine,
			VehicleEngineConfig,
			VehicleEngineConfig.CustomFolderName,
			"vehicle engine",
			s_DefaultVehicleEnginePreviewClip,
			VehicleEngineProfileTemplate,
			ref s_LastVehicleEnginePreviewStatus);
	}

	// Play the currently selected ambient profile once.
	internal static void PreviewSelectedAmbientProfileFromOptions()
	{
		PreviewDomainProfileFromOptions(
			DeveloperAudioDomain.Ambient,
			AmbientConfig,
			AmbientConfig.CustomFolderName,
			"ambient",
			s_DefaultAmbientPreviewClip,
			AmbientProfileTemplate,
			ref s_LastAmbientPreviewStatus);
	}

	// Play the currently selected building profile once.
	internal static void PreviewSelectedBuildingProfileFromOptions()
	{
		PreviewDomainProfileFromOptions(
			DeveloperAudioDomain.Building,
			BuildingConfig,
			BuildingConfig.CustomFolderName,
			"building",
			s_DefaultBuildingPreviewClip,
			BuildingProfileTemplate,
			ref s_LastBuildingPreviewStatus);
	}

	// Play the currently selected UI/tool profile once.
	internal static void PreviewSelectedUIToolProfileFromOptions()
	{
		PreviewDomainProfileFromOptions(
			DeveloperAudioDomain.UITool,
			UIToolConfig,
			UIToolConfig.CustomFolderName,
			"UI/tool",
			s_DefaultUIToolPreviewClip,
			UIToolProfileTemplate,
			ref s_LastUIToolPreviewStatus);
	}

	// Shared preview player for non-siren profile editors.
	private static void PreviewDomainProfileFromOptions(
		DeveloperAudioDomain domain,
		AudioReplacementDomainConfig config,
		string folderName,
		string domainLabel,
		AudioClip? defaultClip,
		SirenSfxProfile defaultProfile,
		ref string statusField)
	{
		string key = AudioReplacementDomainConfig.NormalizeProfileKey(config.EditProfileSelection);
		if (AudioReplacementDomainConfig.IsDefaultSelection(key))
		{
			if (TryPlayDefaultPreviewClip(defaultClip, defaultProfile, domainLabel, out string defaultStatus))
			{
				statusField = defaultStatus;
				Log.Info(statusField);
			}
			else
			{
				statusField = defaultStatus;
				Log.Warn(statusField);
			}

			OptionsVersion++;
			return;
		}

		if (!config.TryGetProfile(key, out SirenSfxProfile profile))
		{
			key = GetFirstAvailableProfileKey(config.CustomProfiles.Keys);
			if (string.IsNullOrWhiteSpace(key) || !config.TryGetProfile(key, out profile))
			{
				if (TryPlayDefaultPreviewClip(defaultClip, defaultProfile, domainLabel, out string defaultStatus))
				{
					statusField = defaultStatus;
					Log.Info(statusField);
				}
				else
				{
					statusField = defaultStatus;
					Log.Warn(statusField);
				}

				OptionsVersion++;
				return;
			}

			config.EditProfileSelection = key;
			if (!config.TryGetProfile(config.CopyFromProfileSelection, out _))
			{
				config.CopyFromProfileSelection = key;
			}

			SaveAudioDomainConfig(domain);
		}

		if (!TryResolveAudioProfilePath(domain, folderName, key, out string path))
		{
			statusField = $"Cannot find file for {domainLabel} profile '{key}'.";
			Log.Warn(statusField);
			OptionsVersion++;
			return;
		}

		string previewLabel = FormatSirenDisplayName(key);

		WaveClipLoader.AudioLoadStatus loadStatus = WaveClipLoader.LoadAudio(path, out AudioClip clip, out string error);
		if (loadStatus == WaveClipLoader.AudioLoadStatus.Pending)
		{
			statusField = $"Preview is loading for {domainLabel} profile '{previewLabel}'. Click Preview again in a moment.";
			Log.Info(statusField);
			OptionsVersion++;
			return;
		}

		if (loadStatus != WaveClipLoader.AudioLoadStatus.Success)
		{
			statusField = $"Preview load failed for {domainLabel} profile '{key}': {error}";
			Log.Warn(statusField);
			OptionsVersion++;
			return;
		}

		if (!TryPlayPreviewClip(clip, profile, out string sourceError))
		{
			statusField = $"Preview failed: {sourceError}";
			Log.Warn(statusField);
			OptionsVersion++;
			return;
		}

		statusField = $"Previewing '{previewLabel}'.";
		Log.Info($"Previewing {domainLabel} profile: {key}");
		OptionsVersion++;
	}
	// Build shared catalog-scan status text for one non-siren audio domain.
	private static string BuildDomainCatalogScanStatusText(AudioReplacementDomainConfig config, string updateButtonLabel)
	{
		if (config.LastCatalogScanUtcTicks <= 0)
		{
			return $"No scan run yet. Click {updateButtonLabel}.";
		}

		DateTime localTime = new DateTime(config.LastCatalogScanUtcTicks, DateTimeKind.Utc).ToLocalTime();
		StringBuilder builder = new StringBuilder();
		builder.Append("Last scan: ").Append(localTime.ToString("yyyy-MM-dd HH:mm:ss")).Append('\n');
		builder.Append("Files found: ").Append(config.LastCatalogScanFileCount).Append('\n');
		builder.Append("Added: ").Append(config.LastCatalogScanAddedCount)
			.Append(", Removed: ").Append(config.LastCatalogScanRemovedCount).Append('\n');

		if (config.LastCatalogScanChangedFiles.Count == 0)
		{
			builder.Append("Changed files: none");
			return builder.ToString();
		}

		builder.Append("Changed files:");
		int shown = Math.Min(config.LastCatalogScanChangedFiles.Count, 12);
		for (int i = 0; i < shown; i++)
		{
			builder.Append('\n').Append(" - ").Append(config.LastCatalogScanChangedFiles[i]);
		}

		if (shown < config.LastCatalogScanChangedFiles.Count)
		{
			builder.Append('\n').Append(" - ...").Append(config.LastCatalogScanChangedFiles.Count - shown).Append(" more");
		}

		return builder.ToString();
	}

	// Update per-domain target-scan telemetry stored in config.
	private static bool UpdateDomainTargetScanMetadata(
		AudioReplacementDomainConfig config,
		string statusText,
		bool forceTimestampRefresh)
	{
		string normalizedStatus = statusText ?? string.Empty;
		bool contentChanged = !string.Equals(config.LastTargetScanStatus, normalizedStatus, StringComparison.Ordinal);
		if (!contentChanged && !forceTimestampRefresh)
		{
			return false;
		}

		config.LastTargetScanStatus = normalizedStatus;
		config.LastTargetScanUtcTicks = DateTime.UtcNow.Ticks;
		return true;
	}

	// Scan all loaded worlds for vehicle prefabs that reference engine SFX effect prefabs.
	private static bool TryScanVehicleEnginePrefabs(out List<string> discovered, out string status)
	{
		discovered = new List<string>();
		status = string.Empty;

		HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		int scannedWorldCount = 0;
		int scannedPrefabCount = 0;

		var worlds = World.All;
		for (int i = 0; i < worlds.Count; i++)
		{
			World world = worlds[i];
			if (world == null || !world.IsCreated)
			{
				continue;
			}

			if (TryScanVehicleEnginePrefabsFromWorld(world, seen, discovered, out int worldPrefabCount))
			{
				scannedWorldCount++;
				scannedPrefabCount += worldPrefabCount;
			}
		}

		if (scannedWorldCount == 0)
		{
			status = "No world with prefab data is currently available. Open a map or fully loaded editor session and retry.";
			return false;
		}

		discovered.Sort(StringComparer.OrdinalIgnoreCase);
		status = $"Last scan: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\nScanned worlds: {scannedWorldCount}, prefabs: {scannedPrefabCount}";
		return true;
	}

	// Scan one ECS world for vehicle prefabs with engine effect links.
	private static bool TryScanVehicleEnginePrefabsFromWorld(
		World world,
		HashSet<string> seen,
		List<string> discovered,
		out int scannedPrefabCount)
	{
		scannedPrefabCount = 0;

		try
		{
			PrefabSystem? prefabSystem = world.GetExistingSystemManaged<PrefabSystem>();
			if (prefabSystem == null)
			{
				return false;
			}

			using (EntityQuery query = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<PrefabData>()))
			{
				if (query.IsEmptyIgnoreFilter)
				{
					return false;
				}

				using (NativeArray<Entity> prefabEntities = query.ToEntityArray(Allocator.Temp))
				{
					scannedPrefabCount = prefabEntities.Length;
					for (int i = 0; i < prefabEntities.Length; i++)
					{
						if (!TryGetPrefabSafe(prefabSystem, prefabEntities[i], out PrefabBase prefab))
						{
							continue;
						}

						string prefabName = AudioReplacementDomainConfig.NormalizeTargetKey(prefab.name ?? string.Empty);
						if (string.IsNullOrWhiteSpace(prefabName) || !IsLikelyVehiclePrefabForEngineScan(prefab, prefabName))
						{
							continue;
						}

						EffectSource effectSource = prefab.GetComponent<EffectSource>();
						if (effectSource == null || effectSource.m_Effects == null || effectSource.m_Effects.Count == 0)
						{
							continue;
						}

						for (int j = 0; j < effectSource.m_Effects.Count; j++)
						{
							EffectSource.EffectSettings effect = effectSource.m_Effects[j];
							if (effect == null || effect.m_Effect == null)
							{
								continue;
							}

							SFX sfx = effect.m_Effect.GetComponent<SFX>();
							VehicleSFX vehicleSfx = effect.m_Effect.GetComponent<VehicleSFX>();
							if (sfx == null || vehicleSfx == null || sfx.m_AudioClip == null)
							{
								continue;
							}

							if (!seen.Add(prefabName))
							{
								break;
							}

							discovered.Add(prefabName);
							break;
						}
					}
				}
			}

			return true;
		}
		catch (Exception ex)
		{
			Log.Warn($"Vehicle engine prefab scan skipped world '{world.Name}': {ex.Message}");
			return false;
		}
	}

	// Vehicle-prefab detection used by engine-target scanners.
	private static bool IsLikelyVehiclePrefabForEngineScan(PrefabBase prefab, string prefabName)
	{
		if (prefab is Game.Prefabs.VehiclePrefab)
		{
			return true;
		}

		return ContainsTextToken(prefabName, "car") ||
			ContainsTextToken(prefabName, "truck") ||
			ContainsTextToken(prefabName, "bus") ||
			ContainsTextToken(prefabName, "train") ||
			ContainsTextToken(prefabName, "tram") ||
			ContainsTextToken(prefabName, "taxi") ||
			ContainsTextToken(prefabName, "ambulance") ||
			ContainsTextToken(prefabName, "police") ||
			ContainsTextToken(prefabName, "fire") ||
			ContainsTextToken(prefabName, "hearse");
	}

	// Scan all loaded worlds for ambient SFX prefabs.
	private static bool TryScanAmbientTargets(out List<string> discovered, out string status)
	{
		discovered = new List<string>();
		status = string.Empty;

		HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		int scannedWorldCount = 0;
		int scannedPrefabCount = 0;

		var worlds = World.All;
		for (int i = 0; i < worlds.Count; i++)
		{
			World world = worlds[i];
			if (world == null || !world.IsCreated)
			{
				continue;
			}

			if (TryScanAmbientTargetsFromWorld(world, seen, discovered, out int worldPrefabCount))
			{
				scannedWorldCount++;
				scannedPrefabCount += worldPrefabCount;
			}
		}

		if (scannedWorldCount == 0)
		{
			status = "No world with prefab data is currently available. Open a map or fully loaded editor session and retry.";
			return false;
		}

		discovered.Sort(StringComparer.OrdinalIgnoreCase);
		status = $"Last scan: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\nScanned worlds: {scannedWorldCount}, prefabs: {scannedPrefabCount}";
		return true;
	}

	// Scan one ECS world for ambient-target SFX prefabs.
	private static bool TryScanAmbientTargetsFromWorld(
		World world,
		HashSet<string> seen,
		List<string> discovered,
		out int scannedPrefabCount)
	{
		scannedPrefabCount = 0;

		try
		{
			PrefabSystem? prefabSystem = world.GetExistingSystemManaged<PrefabSystem>();
			if (prefabSystem == null)
			{
				return false;
			}

			using (EntityQuery query = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<PrefabData>()))
			{
				if (query.IsEmptyIgnoreFilter)
				{
					return false;
				}

				using (NativeArray<Entity> prefabEntities = query.ToEntityArray(Allocator.Temp))
				{
					scannedPrefabCount = prefabEntities.Length;
					for (int i = 0; i < prefabEntities.Length; i++)
					{
						if (!TryGetPrefabSafe(prefabSystem, prefabEntities[i], out PrefabBase prefab))
						{
							continue;
						}

						string prefabName = AudioReplacementDomainConfig.NormalizeTargetKey(prefab.name ?? string.Empty);
						if (string.IsNullOrWhiteSpace(prefabName) || !IsAmbientTargetForScan(prefabName, prefab.GetComponent<SFX>()))
						{
							continue;
						}

						if (!seen.Add(prefabName))
						{
							continue;
						}

						discovered.Add(prefabName);
					}
				}
			}

			return true;
		}
		catch (Exception ex)
		{
			Log.Warn($"Ambient target scan skipped world '{world.Name}': {ex.Message}");
			return false;
		}
	}

	// Ambient-target identification used by runtime and options scanners.
	private static bool IsAmbientTargetForScan(string prefabName, SFX sfx)
	{
		if (sfx == null || sfx.m_AudioClip == null)
		{
			return false;
		}

		if (sfx.m_MixerGroup == MixerGroup.Ambient ||
			sfx.m_MixerGroup == MixerGroup.AudioGroups ||
			sfx.m_MixerGroup == MixerGroup.Disasters)
		{
			return true;
		}

		return ContainsTextToken(prefabName, "ambient") ||
			ContainsTextToken(prefabName, "rain") ||
			ContainsTextToken(prefabName, "water") ||
			ContainsTextToken(prefabName, "forest") ||
			ContainsTextToken(prefabName, "wind") ||
			ContainsTextToken(prefabName, "birds") ||
			ContainsTextToken(prefabName, "seagull") ||
			ContainsTextToken(prefabName, "nature");
	}

	// Scan all loaded worlds for building SFX prefabs.
	private static bool TryScanBuildingTargets(out List<string> discovered, out string status)
	{
		discovered = new List<string>();
		status = string.Empty;

		HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		int scannedWorldCount = 0;
		int scannedPrefabCount = 0;

		var worlds = World.All;
		for (int i = 0; i < worlds.Count; i++)
		{
			World world = worlds[i];
			if (world == null || !world.IsCreated)
			{
				continue;
			}

			if (TryScanBuildingTargetsFromWorld(world, seen, discovered, out int worldPrefabCount))
			{
				scannedWorldCount++;
				scannedPrefabCount += worldPrefabCount;
			}
		}

		if (scannedWorldCount == 0)
		{
			status = "No world with prefab data is currently available. Open a map or fully loaded editor session and retry.";
			return false;
		}

		discovered.Sort(StringComparer.OrdinalIgnoreCase);
		status = $"Last scan: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\nScanned worlds: {scannedWorldCount}, prefabs: {scannedPrefabCount}";
		return true;
	}

	// Scan one ECS world for building-target SFX prefabs.
	private static bool TryScanBuildingTargetsFromWorld(
		World world,
		HashSet<string> seen,
		List<string> discovered,
		out int scannedPrefabCount)
	{
		scannedPrefabCount = 0;

		try
		{
			PrefabSystem? prefabSystem = world.GetExistingSystemManaged<PrefabSystem>();
			if (prefabSystem == null)
			{
				return false;
			}

			using (EntityQuery query = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<PrefabData>()))
			{
				if (query.IsEmptyIgnoreFilter)
				{
					return false;
				}

				using (NativeArray<Entity> prefabEntities = query.ToEntityArray(Allocator.Temp))
				{
					scannedPrefabCount = prefabEntities.Length;
					for (int i = 0; i < prefabEntities.Length; i++)
					{
						if (!TryGetPrefabSafe(prefabSystem, prefabEntities[i], out PrefabBase prefab))
						{
							continue;
						}

						string prefabName = AudioReplacementDomainConfig.NormalizeTargetKey(prefab.name ?? string.Empty);
						if (string.IsNullOrWhiteSpace(prefabName))
						{
							continue;
						}

						SFX sfx = prefab.GetComponent<SFX>();
						if (!IsBuildingTargetForScan(prefab, sfx) || !seen.Add(prefabName))
						{
							continue;
						}

						discovered.Add(prefabName);
					}
				}
			}

			return true;
		}
		catch (Exception ex)
		{
			Log.Warn($"Building target scan skipped world '{world.Name}': {ex.Message}");
			return false;
		}
	}

	// Building-target identification used by runtime and options scanners.
	private static bool IsBuildingTargetForScan(PrefabBase prefab, SFX sfx)
	{
		if (prefab == null ||
			(prefab is not BuildingPrefab && prefab is not BuildingExtensionPrefab))
		{
			return false;
		}

		if (sfx != null && sfx.m_AudioClip != null)
		{
			return true;
		}

		EffectSource effectSource = prefab.GetComponent<EffectSource>();
		if (effectSource == null || effectSource.m_Effects == null || effectSource.m_Effects.Count == 0)
		{
			return false;
		}

		for (int i = 0; i < effectSource.m_Effects.Count; i++)
		{
			EffectSource.EffectSettings effect = effectSource.m_Effects[i];
			if (effect == null || effect.m_Effect == null)
			{
				continue;
			}

			SFX effectSfx = effect.m_Effect.GetComponent<SFX>();
			if (effectSfx != null && effectSfx.m_AudioClip != null)
			{
				return true;
			}
		}

		return false;
	}

	// Scan all loaded worlds for UI/tool SFX targets sourced from ToolUX sound settings.
	private static bool TryScanUIToolTargets(out List<string> discovered, out string status)
	{
		discovered = new List<string>();
		status = string.Empty;

		HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		int scannedWorldCount = 0;
		int scannedToolFieldCount = 0;

		var worlds = World.All;
		for (int i = 0; i < worlds.Count; i++)
		{
			World world = worlds[i];
			if (world == null || !world.IsCreated)
			{
				continue;
			}

			if (TryScanUIToolTargetsFromWorld(world, seen, discovered, out int worldToolFieldCount))
			{
				scannedWorldCount++;
				scannedToolFieldCount += worldToolFieldCount;
			}
		}

		if (scannedWorldCount == 0)
		{
			status = "No world with Tool UX sound data is currently available. Open a map or fully loaded editor session and retry.";
			return false;
		}

		discovered.Sort(StringComparer.OrdinalIgnoreCase);
		status = $"Last scan: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\nScanned worlds: {scannedWorldCount}, tool sound fields: {scannedToolFieldCount}";
		return true;
	}

	// Scan one ECS world for UI/tool-target SFX prefabs from ToolUXSoundSettingsData.
	private static bool TryScanUIToolTargetsFromWorld(
		World world,
		HashSet<string> seen,
		List<string> discovered,
		out int scannedToolFieldCount)
	{
		scannedToolFieldCount = 0;

		try
		{
			PrefabSystem? prefabSystem = world.GetExistingSystemManaged<PrefabSystem>();
			if (prefabSystem == null)
			{
				return false;
			}

			using (EntityQuery soundQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<ToolUXSoundSettingsData>()))
			{
				if (soundQuery.IsEmptyIgnoreFilter)
				{
					return false;
				}

				using (NativeArray<ToolUXSoundSettingsData> soundSettingsArray =
					soundQuery.ToComponentDataArray<ToolUXSoundSettingsData>(Allocator.Temp))
				{
					for (int settingsIndex = 0; settingsIndex < soundSettingsArray.Length; settingsIndex++)
					{
						ToolUXSoundSettingsData soundData = soundSettingsArray[settingsIndex];
						for (int i = 0; i < UIToolSoundSettingsFields.EntityFields.Length; i++)
						{
							var field = UIToolSoundSettingsFields.EntityFields[i];
							object? value = field.GetValue(soundData);
							if (!(value is Entity entity) || entity == Entity.Null)
							{
								continue;
							}

							scannedToolFieldCount++;
							if (!TryGetPrefabSafe(prefabSystem, entity, out PrefabBase prefab))
							{
								continue;
							}

							SFX sfx = prefab.GetComponent<SFX>();
							if (sfx == null || sfx.m_AudioClip == null)
							{
								continue;
							}

							string targetKey = BuildUIToolTargetKey(field.Name, prefab.name);
							if (string.IsNullOrWhiteSpace(targetKey) || !seen.Add(targetKey))
							{
								continue;
							}

							discovered.Add(targetKey);
						}
					}
				}
			}

			return true;
		}
		catch (Exception ex)
		{
			Log.Warn($"UI/tool target scan skipped world '{world.Name}': {ex.Message}");
			return false;
		}
	}

	// Build one stable UI/tool target key from ToolUX field and prefab names.
	private static string BuildUIToolTargetKey(string fieldName, string prefabName)
	{
		string normalizedField = NormalizeUIToolFieldName(fieldName);
		string normalizedPrefab = AudioReplacementDomainConfig.NormalizeTargetKey(prefabName ?? string.Empty);
		if (string.IsNullOrWhiteSpace(normalizedField) && string.IsNullOrWhiteSpace(normalizedPrefab))
		{
			return string.Empty;
		}

		if (string.IsNullOrWhiteSpace(normalizedPrefab))
		{
			return $"ui-tool/{normalizedField}";
		}

		return $"ui-tool/{normalizedField}/{normalizedPrefab}";
	}

	// Normalize ToolUX field names into readable/stable key segments.
	private static string NormalizeUIToolFieldName(string fieldName)
	{
		string value = (fieldName ?? string.Empty).Trim();
		if (value.StartsWith("m_", StringComparison.OrdinalIgnoreCase))
		{
			value = value.Substring(2);
		}

		if (value.EndsWith("Sound", StringComparison.OrdinalIgnoreCase) && value.Length > 5)
		{
			value = value.Substring(0, value.Length - 5);
		}

		return AudioReplacementDomainConfig.NormalizeTargetKey(value);
	}
}









