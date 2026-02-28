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

// Engine and ambient options/scanning helpers split from the core mod file.
public sealed partial class SirenChangerMod
{
	private static string s_LastVehicleEnginePreviewStatus = "No vehicle engine preview has been played in this session.";

	private static string s_LastAmbientPreviewStatus = "No ambient preview has been played in this session.";

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

	// Shared dropdown-item builder for engine/ambient custom file lists.
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

		if (s_DiscoveredAmbientTargets.Length == 0)
		{
			s_AmbientTargetDropdown = new[]
			{
				new DropdownItem<string>
				{
					value = string.Empty,
					displayName = "No ambient targets detected",
					disabled = true
				}
			};
			s_AmbientTargetDropdownCacheVersion = OptionsVersion;
			return;
		}

		List<DropdownItem<string>> options = new List<DropdownItem<string>>(s_DiscoveredAmbientTargets.Length);
		for (int i = 0; i < s_DiscoveredAmbientTargets.Length; i++)
		{
			string targetName = s_DiscoveredAmbientTargets[i];
			options.Add(new DropdownItem<string>
			{
				value = targetName,
				displayName = targetName
			});
		}

		s_AmbientTargetDropdown = options.ToArray();
		s_AmbientTargetDropdownCacheVersion = OptionsVersion;
	}

	// Set selected vehicle-engine target key in options UI.
	internal static void SetVehicleEngineTargetSelectionTargetFromOptions(string vehiclePrefabName)
	{
		VehicleEngineConfig.SetTargetSelectionTarget(vehiclePrefabName);
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

		VehicleEngineConfig.SetTargetSelection(key, selection);
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
		AmbientConfig.SetTargetSelectionTarget(targetName);
	}

	// Get selected ambient override for the currently selected target.
	internal static string GetSelectedAmbientTargetSelectionForOptions()
	{
		string key = AmbientConfig.TargetSelectionTarget;
		if (string.IsNullOrWhiteSpace(key))
		{
			return SirenReplacementConfig.DefaultSelectionToken;
		}

		return AmbientConfig.GetTargetSelection(key);
	}

	// Set ambient override for the currently selected target.
	internal static void SetSelectedAmbientTargetSelectionFromOptions(string selection)
	{
		string key = AmbientConfig.TargetSelectionTarget;
		if (string.IsNullOrWhiteSpace(key))
		{
			return;
		}

		AmbientConfig.SetTargetSelection(key, selection);
	}

	// Read-only status text for ambient override controls.
	internal static string GetSelectedAmbientOverrideStatusText()
	{
		if (s_DiscoveredAmbientTargets.Length == 0)
		{
			return "No ambient targets detected yet. Click Rescan Ambient Targets in a loaded map/editor session.";
		}

		string key = AmbientConfig.TargetSelectionTarget;
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

	// Scan loaded prefabs for vehicle-engine targets and refresh per-vehicle options.
	internal static void RefreshVehicleEnginePrefabsFromOptions()
	{
		if (!TryScanVehicleEnginePrefabs(out List<string> discovered, out string status))
		{
			s_LastVehicleEnginePrefabScanStatus = status;
			OptionsVersion++;
			return;
		}

		SetDiscoveredVehicleEnginePrefabs(discovered);
		s_LastVehicleEnginePrefabScanStatus = discovered.Count > 0
			? $"{status}\nDetected: {discovered.Count} prefab(s)."
			: $"{status}\nNo vehicle engine prefabs were found in the active world.";
		OptionsVersion++;
	}

	// Scan loaded prefabs for ambient targets and refresh per-target options.
	internal static void RefreshAmbientTargetsFromOptions()
	{
		if (!TryScanAmbientTargets(out List<string> discovered, out string status))
		{
			s_LastAmbientTargetScanStatus = status;
			OptionsVersion++;
			return;
		}

		SetDiscoveredAmbientTargets(discovered);
		s_LastAmbientTargetScanStatus = discovered.Count > 0
			? $"{status}\nDetected: {discovered.Count} target(s)."
			: $"{status}\nNo ambient targets were found in the active world.";
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

			SaveConfig();
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
}









