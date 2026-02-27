using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using Game;
using Game.Common;
using Game.Prefabs;
using Game.Prefabs.Effects;
using Game.SceneFlow;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace SirenChanger;

// Runtime ECS system that applies configured siren replacements to target prefabs.
public sealed partial class SirenReplacementSystem : GameSystemBase
{
	private static readonly string[] s_FallbackSirenTokens = { "siren", "alarm", "emergency" };

	private static readonly TargetDefinition[] s_TargetDefinitions =
	{
		new TargetDefinition("PoliceCarSirenNA", EmergencySirenVehicleType.Police, SirenRegion.NorthAmerica),
		new TargetDefinition("PoliceCarSirenEU", EmergencySirenVehicleType.Police, SirenRegion.Europe),
		new TargetDefinition("FireTruckSirenNA", EmergencySirenVehicleType.Fire, SirenRegion.NorthAmerica),
		new TargetDefinition("FireTruckSirenEU", EmergencySirenVehicleType.Fire, SirenRegion.Europe),
		new TargetDefinition("AmbulanceSirenNA", EmergencySirenVehicleType.Ambulance, SirenRegion.NorthAmerica),
		new TargetDefinition("AmbulanceSirenEU", EmergencySirenVehicleType.Ambulance, SirenRegion.Europe)
	};

	private PrefabSystem m_PrefabSystem = null!;

	private EntityQuery m_PrefabQuery = default;

	private readonly Dictionary<string, SFX> m_TargetSfxByPrefab = new Dictionary<string, SFX>(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<string, SirenSfxSnapshot> m_DefaultSfxByPrefab = new Dictionary<string, SirenSfxSnapshot>(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<string, VehicleTargetDefinition> m_VehicleTargetsByPrefab = new Dictionary<string, VehicleTargetDefinition>(StringComparer.OrdinalIgnoreCase);

	private bool m_TargetsBuilt;

	private bool m_WasLoading = true;

	private int m_LastAppliedConfigVersion = -1;

	private int m_LastAppliedAudioLoadVersion = -1;

	// Initialize prefab query dependencies.
	protected override void OnCreate()
	{
		base.OnCreate();
		m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
		m_PrefabQuery = GetEntityQuery(ComponentType.ReadOnly<PrefabData>());
	}

	// Poll game state, cache targets, and apply new config when needed.
	protected override void OnUpdate()
	{
		if (GameManager.instance.isGameLoading)
		{
			m_WasLoading = true;
			return;
		}

		if (m_WasLoading)
		{
			ResetSessionState();
			m_WasLoading = false;
		}

		if (m_PrefabQuery.IsEmptyIgnoreFilter)
		{
			return;
		}

		if (!m_TargetsBuilt)
		{
			BuildTargetCache();
		}

		if (!m_TargetsBuilt)
		{
			return;
		}

		WaveClipLoader.PollAsyncLoads();
		int currentAudioLoadVersion = WaveClipLoader.AsyncCompletionVersion;
		if (m_LastAppliedConfigVersion == SirenChangerMod.ConfigVersion &&
			m_LastAppliedAudioLoadVersion == currentAudioLoadVersion)
		{
			return;
		}

		ApplyConfiguredSirens();
		m_LastAppliedConfigVersion = SirenChangerMod.ConfigVersion;
		m_LastAppliedAudioLoadVersion = currentAudioLoadVersion;
	}

	// Reset session state after loading transitions.
	private void ResetSessionState()
	{
		RestoreAllVehicleEffectBindings(disposeOverrides: true);
		m_TargetsBuilt = false;
		SirenChangerMod.ResetDetectedAudioDomain(DeveloperAudioDomain.Siren);
		m_TargetSfxByPrefab.Clear();
		m_DefaultSfxByPrefab.Clear();
		m_VehicleTargetsByPrefab.Clear();
		m_LastAppliedConfigVersion = -1;
		m_LastAppliedAudioLoadVersion = -1;
	}

	// Locate target siren prefabs and snapshot defaults.
	private void BuildTargetCache()
	{
		RestoreAllVehicleEffectBindings(disposeOverrides: true);
		m_TargetSfxByPrefab.Clear();
		m_DefaultSfxByPrefab.Clear();
		m_VehicleTargetsByPrefab.Clear();
		SirenChangerMod.BeginDetectedAudioCollection(DeveloperAudioDomain.Siren);

		SirenSfxProfile template = SirenSfxProfile.CreateFallback();
		AudioClip? defaultPreviewClip = null;
		HashSet<string> discoveredVehiclePrefabs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		using (NativeArray<Entity> prefabEntities = m_PrefabQuery.ToEntityArray(Allocator.Temp))
		{
			for (int i = 0; i < prefabEntities.Length; i++)
			{
				PrefabBase prefab = m_PrefabSystem.GetPrefab<PrefabBase>(prefabEntities[i]);
				if (prefab == null)
				{
					continue;
				}

				string prefabName = prefab.name ?? string.Empty;
				SFX sfx = prefab.GetComponent<SFX>();
				if (sfx != null &&
					 sfx.m_AudioClip != null &&
					 IsTargetPrefab(prefabName))
				{
					SirenChangerMod.RegisterDetectedAudioEntry(DeveloperAudioDomain.Siren, prefabName, sfx);
					m_TargetSfxByPrefab[prefabName] = sfx;
					if (!m_DefaultSfxByPrefab.ContainsKey(prefabName))
					{
						m_DefaultSfxByPrefab[prefabName] = SirenSfxSnapshot.FromSfx(sfx);
					}

					if (defaultPreviewClip == null)
					{
						defaultPreviewClip = sfx.m_AudioClip;
					}

					if (string.Equals(prefabName, "PoliceCarSirenNA", StringComparison.OrdinalIgnoreCase))
					{
						template = SirenSfxProfile.FromSfx(sfx);
						defaultPreviewClip = sfx.m_AudioClip;
					}
				}

				TryRegisterVehicleTarget(prefab, prefabName, discoveredVehiclePrefabs);
			}
		}

		if (m_TargetSfxByPrefab.Count == 0)
		{
			SirenChangerMod.CompleteDetectedAudioCollection(DeveloperAudioDomain.Siren);
			SirenChangerMod.SetSirenDefaultPreviewClip(null);
			SirenChangerMod.Log.Warn("No emergency vehicle siren prefabs were found in loaded prefabs.");
			return;
		}

		List<string> discovered = new List<string>(discoveredVehiclePrefabs);
		discovered.Sort(StringComparer.OrdinalIgnoreCase);
		SirenChangerMod.SetDiscoveredVehiclePrefabs(discovered);

		SirenChangerMod.SetCustomProfileTemplate(template);
		SirenChangerMod.SetSirenDefaultPreviewClip(defaultPreviewClip);
		SirenChangerMod.CompleteDetectedAudioCollection(DeveloperAudioDomain.Siren);
		SirenChangerMod.SyncCustomSirenCatalog(saveIfChanged: true);
		m_TargetsBuilt = true;
	}
	// Apply selections for every target prefab according to current config.
	private void ApplyConfiguredSirens()
	{
		SirenReplacementConfig config = SirenChangerMod.Config;
		config.Normalize();

		RestoreAllTargetDefaults();
		RestoreAllVehicleEffectBindings(disposeOverrides: false);

		int targetReplacementCount = 0;
		int vehicleOverrideCount = 0;
		Dictionary<string, string> appliedReplacementPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		Dictionary<string, string> targetSelectionKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		Dictionary<string, string> targetSelectionSources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		Dictionary<string, SelectionLoadResult> selectionLoadCache = new Dictionary<string, SelectionLoadResult>(StringComparer.OrdinalIgnoreCase);

		for (int i = 0; i < s_TargetDefinitions.Length; i++)
		{
			TargetDefinition target = s_TargetDefinitions[i];
			targetSelectionKeys[target.PrefabName] = config.GetSelection(target.VehicleType, target.Region);
			targetSelectionSources[target.PrefabName] = $"VehicleSelection:{target.VehicleType}.{GetRegionCode(target.Region)}";
		}

		if (config.Enabled)
		{
			for (int i = 0; i < s_TargetDefinitions.Length; i++)
			{
				TargetDefinition target = s_TargetDefinitions[i];
				targetSelectionKeys.TryGetValue(target.PrefabName, out string selectionKey);
				targetSelectionSources.TryGetValue(target.PrefabName, out string selectionSource);
				targetReplacementCount += ApplyTargetSelection(
					target,
					selectionKey ?? SirenReplacementConfig.DefaultSelectionToken,
					selectionSource ?? $"VehicleSelection:{target.VehicleType}.{GetRegionCode(target.Region)}",
					config,
					selectionLoadCache,
					appliedReplacementPaths);
			}

			vehicleOverrideCount = ApplySpecificVehiclePrefabOverrides(config, selectionLoadCache);
		}

		if (config.DumpDetectedSirens)
		{
			WriteDetectedSirens(appliedReplacementPaths, targetSelectionSources);
		}

		int totalReplacementCount = targetReplacementCount + vehicleOverrideCount;
		SirenChangerMod.Log.Info(
			$"Siren apply complete. Enabled={config.Enabled}, Replaced={totalReplacementCount}, " +
			$"TargetReplacements={targetReplacementCount}, VehicleOverrides={vehicleOverrideCount}.");
	}

	// Restore all tracked target prefabs to original state before reapplying.
	private void RestoreAllTargetDefaults()
	{
		foreach (KeyValuePair<string, SirenSfxSnapshot> pair in m_DefaultSfxByPrefab)
		{
			if (m_TargetSfxByPrefab.TryGetValue(pair.Key, out SFX sfx) && sfx != null)
			{
				pair.Value.Restore(sfx);
			}
		}
	}

	// Apply per-vehicle-prefab overrides by assigning a cloned effect prefab per overridden vehicle.
	private int ApplySpecificVehiclePrefabOverrides(
		SirenReplacementConfig config,
		Dictionary<string, SelectionLoadResult> selectionLoadCache)
	{
		if (m_VehicleTargetsByPrefab.Count == 0)
		{
			return 0;
		}

		int appliedCount = 0;
		List<string> vehiclePrefabs = new List<string>(m_VehicleTargetsByPrefab.Keys);
		vehiclePrefabs.Sort(StringComparer.OrdinalIgnoreCase);
		for (int i = 0; i < vehiclePrefabs.Count; i++)
		{
			string vehiclePrefab = vehiclePrefabs[i];
			VehicleTargetDefinition vehicleTarget = m_VehicleTargetsByPrefab[vehiclePrefab];
			string overrideSelection = config.GetVehiclePrefabSelection(vehiclePrefab);
			string selectedKey = overrideSelection;
			string selectionSourceLabel = $"VehiclePrefab:{vehiclePrefab}";
			if (SirenReplacementConfig.IsDefaultSelection(selectedKey))
			{
				bool handledByGlobalTarget = m_TargetSfxByPrefab.ContainsKey(vehicleTarget.SirenPrefabName);
				if (handledByGlobalTarget)
				{
					continue;
				}

				selectedKey = config.GetSelection(vehicleTarget.VehicleType, vehicleTarget.Region);
				if (SirenReplacementConfig.IsDefaultSelection(selectedKey))
				{
					continue;
				}

				selectionSourceLabel = $"VehicleSelection:{vehicleTarget.VehicleType}.{GetRegionCode(vehicleTarget.Region)}";
			}

			TargetDefinition target = new TargetDefinition(
				vehicleTarget.SirenPrefabName,
				vehicleTarget.VehicleType,
				vehicleTarget.Region);
			ResolvedSelection resolved = ResolveSelection(
				target,
				selectedKey,
				selectionSourceLabel,
				config,
				selectionLoadCache);
			if (!ApplyResolvedSelectionToVehicleOverride(vehicleTarget, resolved))
			{
				continue;
			}

			appliedCount++;
		}

		return appliedCount;
	}

	// Apply a resolved selection directly to one vehicle's cloned effect prefab.
	private bool ApplyResolvedSelectionToVehicleOverride(VehicleTargetDefinition vehicleTarget, ResolvedSelection resolved)
	{
		switch (resolved.Outcome)
		{
			case ResolvedSelectionOutcome.CustomClip:
				if (!EnsureVehicleOverrideSfx(vehicleTarget, out SFX customSfx))
				{
					return false;
				}

				resolved.Profile!.ApplyTo(customSfx);
				customSfx.m_AudioClip = resolved.Clip!;
				return true;
			case ResolvedSelectionOutcome.Mute:
				if (!EnsureVehicleOverrideSfx(vehicleTarget, out SFX muteSfx))
				{
					return false;
				}

				if (m_DefaultSfxByPrefab.TryGetValue(vehicleTarget.SirenPrefabName, out SirenSfxSnapshot snapshot))
				{
					snapshot.Profile.ApplyTo(muteSfx);
					muteSfx.m_AudioClip = snapshot.Clip;
				}

				muteSfx.m_Volume = 0f;
				return true;
			default:
				return false;
		}
	}

	// Ensure one cloned effect prefab exists for this vehicle and return its SFX component.
	private bool EnsureVehicleOverrideSfx(VehicleTargetDefinition vehicleTarget, out SFX sfx)
	{
		sfx = null!;
		if (vehicleTarget.OriginalEffectPrefab == null ||
			!vehicleTarget.TryGetEffectSettings(out EffectSource.EffectSettings effectSettings))
		{
			return false;
		}

		EffectPrefab? overrideEffect = vehicleTarget.OverrideEffectPrefab;
		if (overrideEffect == null)
		{
			string overrideName = BuildVehicleOverrideEffectName(vehicleTarget.VehiclePrefabName, vehicleTarget.SirenPrefabName);
			overrideEffect = CloneEffectPrefab(vehicleTarget.OriginalEffectPrefab, overrideName);
			if (overrideEffect == null)
			{
				SirenChangerMod.Log.Warn(
					$"Specific-vehicle override skipped for '{vehicleTarget.VehiclePrefabName}' because effect prefab clone failed.");
				return false;
			}

			vehicleTarget.OverrideEffectPrefab = overrideEffect;
		}

		sfx = overrideEffect.GetComponent<SFX>();
		if (sfx == null)
		{
			SirenChangerMod.Log.Warn(
				$"Specific-vehicle override skipped for '{vehicleTarget.VehiclePrefabName}' because cloned effect has no SFX component.");
			return false;
		}

		bool changed = !ReferenceEquals(effectSettings.m_Effect, overrideEffect);
		effectSettings.m_Effect = overrideEffect;
		if (changed)
		{
			m_PrefabSystem.UpdatePrefab(vehicleTarget.VehiclePrefab);
		}

		return true;
	}

	// Clone/register an effect prefab so it can resolve through PrefabSystem and be used by EffectSource.
	private EffectPrefab? CloneEffectPrefab(EffectPrefab source, string cloneName)
	{
		if (source == null)
		{
			return null;
		}

		PrefabBase cloned;
		try
		{
			cloned = m_PrefabSystem.DuplicatePrefab(source, cloneName);
		}
		catch (Exception ex)
		{
			SirenChangerMod.Log.Warn($"Failed to duplicate effect prefab '{source.name}' for '{cloneName}': {ex.Message}");
			return null;
		}

		EffectPrefab? clonedEffect = cloned as EffectPrefab;
		if (clonedEffect == null)
		{
			if (cloned != null)
			{
				m_PrefabSystem.RemovePrefab(cloned);
				UnityEngine.Object.Destroy(cloned);
			}

			return null;
		}

		clonedEffect.name = cloneName;
		if (!m_PrefabSystem.TryGetEntity(clonedEffect, out _))
		{
			SirenChangerMod.Log.Warn($"Duplicated effect prefab '{cloneName}' was not registered in PrefabSystem.");
			m_PrefabSystem.RemovePrefab(clonedEffect);
			UnityEngine.Object.Destroy(clonedEffect);
			return null;
		}

		return clonedEffect;
	}

	// Restore all vehicle effect links to their original effect prefabs and optionally destroy clones.
	private void RestoreAllVehicleEffectBindings(bool disposeOverrides)
	{
		foreach (KeyValuePair<string, VehicleTargetDefinition> pair in m_VehicleTargetsByPrefab)
		{
			VehicleTargetDefinition vehicleTarget = pair.Value;
			bool restored = false;
			if (vehicleTarget.OriginalEffectPrefab != null &&
				vehicleTarget.TryGetEffectSettings(out EffectSource.EffectSettings effectSettings) &&
				!ReferenceEquals(effectSettings.m_Effect, vehicleTarget.OriginalEffectPrefab))
			{
				effectSettings.m_Effect = vehicleTarget.OriginalEffectPrefab;
				restored = true;
			}

			if (restored)
			{
				m_PrefabSystem.UpdatePrefab(vehicleTarget.VehiclePrefab);
			}

			if (!disposeOverrides || vehicleTarget.OverrideEffectPrefab == null)
			{
				continue;
			}

			if (m_PrefabSystem.TryGetEntity(vehicleTarget.OverrideEffectPrefab, out _))
			{
				m_PrefabSystem.RemovePrefab(vehicleTarget.OverrideEffectPrefab);
			}

			UnityEngine.Object.Destroy(vehicleTarget.OverrideEffectPrefab);
			vehicleTarget.OverrideEffectPrefab = null;
		}
	}
	// Build stable names for runtime-cloned effect prefabs used by specific-vehicle overrides.
	private static string BuildVehicleOverrideEffectName(string vehiclePrefabName, string sirenPrefabName)
	{
		string cleanVehicle = (vehiclePrefabName ?? string.Empty).Replace(' ', '_');
		return $"SC_{sirenPrefabName}_{cleanVehicle}";
	}

	// Apply one resolved selection to a specific prefab target.
	private int ApplyTargetSelection(
		TargetDefinition target,
		string selectionKey,
		string selectionSourceLabel,
		SirenReplacementConfig config,
		Dictionary<string, SelectionLoadResult> selectionLoadCache,
		Dictionary<string, string> replacementPaths)
	{
		if (!m_TargetSfxByPrefab.TryGetValue(target.PrefabName, out SFX sfx) || sfx == null)
		{
			return 0;
		}

		ResolvedSelection resolved = ResolveSelection(target, selectionKey, selectionSourceLabel, config, selectionLoadCache);
		switch (resolved.Outcome)
		{
			case ResolvedSelectionOutcome.CustomClip:
				resolved.Profile!.ApplyTo(sfx);
				sfx.m_AudioClip = resolved.Clip!;
				replacementPaths[target.PrefabName] = resolved.ReplacementPath;
				return 1;
			case ResolvedSelectionOutcome.Mute:
				sfx.m_Volume = 0f;
				replacementPaths[target.PrefabName] = "[FallbackMute]";
				return 1;
			default:
					return 0;
			}
	}

	// Resolve primary selection with configured fallback behavior.
	private ResolvedSelection ResolveSelection(
		TargetDefinition target,
		string selectionKey,
		string selectionSourceLabel,
		SirenReplacementConfig config,
		Dictionary<string, SelectionLoadResult> selectionLoadCache)
	{
		if (SirenReplacementConfig.IsDefaultSelection(selectionKey))
		{
			return ResolvedSelection.Default();
		}

			if (TryGetSelectionLoadResult(selectionKey, config, selectionLoadCache, out SelectionLoadResult primaryResult))
			{
				return ResolvedSelection.Custom(primaryResult.Clip!, primaryResult.Profile!, primaryResult.FilePath);
			}

			if (primaryResult.IsPending)
			{
				return ResolvedSelection.Default();
			}

			string targetLabel = $"{target.VehicleType}.{GetRegionCode(target.Region)} via {selectionSourceLabel}";
			SirenChangerMod.Log.Warn(
				$"Primary siren selection failed for {targetLabel}: '{selectionKey}'. {primaryResult.Error}");

		switch (config.MissingSirenFallbackBehavior)
		{
			case SirenFallbackBehavior.Mute:
				return ResolvedSelection.Mute();
			case SirenFallbackBehavior.AlternateCustomSiren:
				return ResolveAlternateFallback(target, selectionKey, config, selectionLoadCache);
			default:
				return ResolvedSelection.Default();
			}
	}

	// Resolve alternate custom siren fallback when primary selection fails.
	private ResolvedSelection ResolveAlternateFallback(
		TargetDefinition target,
		string failedSelectionKey,
		SirenReplacementConfig config,
		Dictionary<string, SelectionLoadResult> selectionLoadCache)
	{
		string alternateSelection = config.AlternateFallbackSelection;
		if (SirenReplacementConfig.IsDefaultSelection(alternateSelection))
		{
			SirenChangerMod.Log.Warn(
				$"Alternate fallback is configured for {target.PrefabName}, but Alternate Siren is set to Default.");
			return ResolvedSelection.Default();
		}

		if (string.Equals(alternateSelection, failedSelectionKey, StringComparison.OrdinalIgnoreCase))
		{
			SirenChangerMod.Log.Warn(
				$"Alternate fallback for {target.PrefabName} points to the same selection '{alternateSelection}'.");
			return ResolvedSelection.Default();
		}

			if (!TryGetSelectionLoadResult(alternateSelection, config, selectionLoadCache, out SelectionLoadResult alternateResult))
			{
				if (alternateResult.IsPending)
				{
					return ResolvedSelection.Default();
				}

				SirenChangerMod.Log.Warn(
					$"Alternate fallback failed for {target.PrefabName}: '{alternateSelection}'. {alternateResult.Error}");
				return ResolvedSelection.Default();
			}

		SirenChangerMod.Log.Info(
			$"Applied alternate fallback '{alternateSelection}' for {target.PrefabName} after '{failedSelectionKey}' failed.");
		return ResolvedSelection.Custom(alternateResult.Clip!, alternateResult.Profile!, alternateResult.FilePath);
	}

	// Load and cache one selection key into clip/profile payload.
	private static bool TryGetSelectionLoadResult(
		string selectionKey,
		SirenReplacementConfig config,
		Dictionary<string, SelectionLoadResult> selectionLoadCache,
		out SelectionLoadResult result)
	{
		string normalizedSelection = SirenPathUtils.NormalizeProfileKey(selectionKey);
		if (selectionLoadCache.TryGetValue(normalizedSelection, out result))
		{
			return result.Success;
		}

		result = new SelectionLoadResult();
		if (string.IsNullOrWhiteSpace(normalizedSelection) || SirenReplacementConfig.IsDefaultSelection(normalizedSelection))
		{
			result.Error = "Selection is empty or set to Default.";
			selectionLoadCache[normalizedSelection] = result;
			return false;
		}

		if (!config.TryGetProfile(normalizedSelection, out SirenSfxProfile profile))
		{
			result.Error = $"No profile entry exists for '{normalizedSelection}'.";
			selectionLoadCache[normalizedSelection] = result;
			return false;
		}

		if (!SirenChangerMod.TryResolveAudioProfilePath(
			DeveloperAudioDomain.Siren,
			config.CustomSirensFolderName,
			normalizedSelection,
			out string filePath))
		{
			result.Error = $"Custom siren file was not found for '{normalizedSelection}'.";
			selectionLoadCache[normalizedSelection] = result;
			return false;
		}

			WaveClipLoader.AudioLoadStatus loadStatus = WaveClipLoader.LoadAudio(filePath, out AudioClip clip, out string loadError);
			if (loadStatus != WaveClipLoader.AudioLoadStatus.Success)
			{
				result.IsPending = loadStatus == WaveClipLoader.AudioLoadStatus.Pending;
				result.Error = result.IsPending
					? $"Audio file is still loading: {loadError}"
					: $"Audio file could not be loaded: {loadError}";
				selectionLoadCache[normalizedSelection] = result;
				return false;
			}

		result.Success = true;
		result.Clip = clip;
		result.Profile = profile.ClampCopy();
		result.FilePath = filePath;
		selectionLoadCache[normalizedSelection] = result;
		return true;
	}

	// Write detected/applied siren details to diagnostics JSON.
	private void WriteDetectedSirens(
		Dictionary<string, string> replacementPaths,
		Dictionary<string, string> targetSelectionSources)
	{
		try
		{
			SirenReplacementConfig config = SirenChangerMod.Config;
			List<DetectedSirenClip> entries = config.DumpAllSirenCandidates
				? CollectAllDetectedSirens(config, replacementPaths)
				: CollectTargetDetectedSirens(replacementPaths, targetSelectionSources);

			string outputPath = Path.Combine(SirenChangerMod.SettingsDirectory, SirenReplacementConfig.DetectedSirensFileName);
			File.WriteAllText(outputPath, JsonDataSerializer.Serialize(entries));
		}
		catch (Exception ex)
		{
			SirenChangerMod.Log.Warn($"Failed to write siren report: {ex.Message}");
		}
	}

	// Collect diagnostics for strict target prefabs only.
	private List<DetectedSirenClip> CollectTargetDetectedSirens(
		Dictionary<string, string> replacementPaths,
		Dictionary<string, string> targetSelectionSources)
	{
		List<DetectedSirenClip> entries = new List<DetectedSirenClip>(m_TargetSfxByPrefab.Count);
		for (int i = 0; i < s_TargetDefinitions.Length; i++)
		{
			TargetDefinition definition = s_TargetDefinitions[i];
			if (!m_TargetSfxByPrefab.TryGetValue(definition.PrefabName, out SFX sfx) || sfx == null || sfx.m_AudioClip == null)
			{
				continue;
			}

			bool applied = replacementPaths.TryGetValue(definition.PrefabName, out string replacementPath);
			string matchRule = applied
				? (targetSelectionSources.TryGetValue(definition.PrefabName, out string sourceRule)
					? sourceRule
					: $"VehicleSelection:{definition.VehicleType}.{GetRegionCode(definition.Region)}")
				: "TargetPrefab";
			entries.Add(DetectedSirenClip.From(
				definition.PrefabName,
				sfx.m_AudioClip.name ?? string.Empty,
				sfx.m_AudioClip,
				applied,
				matchRule,
				replacementPath ?? string.Empty));
		}

		return entries;
	}

	// Collect diagnostics for broader siren-token matches.
	private List<DetectedSirenClip> CollectAllDetectedSirens(SirenReplacementConfig config, Dictionary<string, string> replacementPaths)
	{
		List<DetectedSirenClip> entries = new List<DetectedSirenClip>();
		List<string> tokens = (config.SirenTokens != null && config.SirenTokens.Count > 0)
			? config.SirenTokens
			: new List<string>(s_FallbackSirenTokens);

		using (NativeArray<Entity> prefabEntities = m_PrefabQuery.ToEntityArray(Allocator.Temp))
		{
			for (int i = 0; i < prefabEntities.Length; i++)
			{
				PrefabBase prefab = m_PrefabSystem.GetPrefab<PrefabBase>(prefabEntities[i]);
				if (prefab == null)
				{
					continue;
				}

				SFX sfx = prefab.GetComponent<SFX>();
				if (sfx == null || sfx.m_AudioClip == null)
				{
					continue;
				}

				string prefabName = prefab.name ?? string.Empty;
				string clipName = sfx.m_AudioClip.name ?? string.Empty;
				if (!IsSirenCandidate(prefabName, clipName, tokens))
				{
					continue;
				}

				bool applied = false;
				string matchRule = "SirenToken";
				string replacementPath = string.Empty;
				if (TryGetVehicleType(prefabName, out EmergencySirenVehicleType vehicleType) &&
					replacementPaths.TryGetValue(prefabName, out replacementPath))
				{
					applied = true;
					matchRule = $"VehicleSelection:{vehicleType}";
				}

				entries.Add(DetectedSirenClip.From(prefabName, clipName, sfx.m_AudioClip, applied, matchRule, replacementPath));
			}
		}

		return entries;
	}

	// Discover emergency vehicle prefab -> siren prefab relationship from EffectSource links.
	private void TryRegisterVehicleTarget(PrefabBase prefab, string prefabName, ISet<string> discoveredVehiclePrefabs)
	{
		if (string.IsNullOrWhiteSpace(prefabName))
		{
			return;
		}

		EffectSource effectSource = prefab.GetComponent<EffectSource>();
		if (effectSource == null || effectSource.m_Effects == null || effectSource.m_Effects.Count == 0)
		{
			return;
		}

		for (int i = 0; i < effectSource.m_Effects.Count; i++)
		{
			EffectSource.EffectSettings effect = effectSource.m_Effects[i];
			if (effect == null || effect.m_Effect == null)
			{
				continue;
			}

			string effectPrefabName = effect.m_Effect.name ?? string.Empty;
			if (!TryResolveVehicleTargetDefinition(prefab, prefabName, effect.m_Effect, effectPrefabName, out TargetDefinition targetDefinition))
			{
				continue;
			}

			EffectSource uniqueEffectSource = EnsureUniqueEffectSourceComponent(prefab, effectSource);
			if (uniqueEffectSource.m_Effects == null || i < 0 || i >= uniqueEffectSource.m_Effects.Count)
			{
				SirenChangerMod.Log.Warn($"Specific-vehicle override binding skipped for '{prefabName}' because EffectSource index {i} was unavailable after clone.");
				continue;
			}

			EffectSource.EffectSettings uniqueEffect = uniqueEffectSource.m_Effects[i];
			if (uniqueEffect == null || uniqueEffect.m_Effect == null)
			{
				SirenChangerMod.Log.Warn($"Specific-vehicle override binding skipped for '{prefabName}' because EffectSettings at index {i} was null after clone.");
				continue;
			}

			m_VehicleTargetsByPrefab[prefabName] = new VehicleTargetDefinition(
				prefab,
				prefabName,
				targetDefinition.PrefabName,
				targetDefinition.VehicleType,
				targetDefinition.Region,
				uniqueEffectSource,
				i,
				uniqueEffect.m_Effect);
			discoveredVehiclePrefabs.Add(prefabName);
			return;
		}
	}

	// Ensure the prefab has its own editable EffectSource component so per-vehicle overrides do not mutate siblings.
	private static EffectSource EnsureUniqueEffectSourceComponent(PrefabBase prefab, EffectSource source)
	{
		ComponentBase? exact = prefab.GetComponentExactly(typeof(EffectSource));
		if (exact is EffectSource exactEffectSource)
		{
			return exactEffectSource;
		}

		return (EffectSource)prefab.AddComponentFrom(source);
	}

	// Token-based candidate matching helper.
	private static bool IsSirenCandidate(string prefabName, string clipName, List<string> tokens)
	{
		for (int i = 0; i < tokens.Count; i++)
		{
			string token = tokens[i];
			if (string.IsNullOrWhiteSpace(token))
			{
				continue;
			}

			if (prefabName.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0 ||
				clipName.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return true;
			}
		}

		return false;
	}

	// Compact region label used in logs and report tags.
	private static string GetRegionCode(SirenRegion region)
	{
		return region == SirenRegion.Europe ? "EU" : "NA";
	}

	// True when prefab belongs to configured siren targets.
	private static bool IsTargetPrefab(string prefabName)
	{
		return TryGetTargetDefinition(prefabName, out _);
	}

	// Resolve vehicle target category for one effect reference, including non-standard police/agent siren prefabs.
	private static bool TryResolveVehicleTargetDefinition(
		PrefabBase vehiclePrefab,
		string vehiclePrefabName,
		EffectPrefab effectPrefab,
		string effectPrefabName,
		out TargetDefinition definition)
	{
		if (TryGetTargetDefinition(effectPrefabName, out definition))
		{
			return true;
		}

		if (!IsLikelySirenEffectName(effectPrefabName))
		{
			definition = default;
			return false;
		}

		// Do not bind siren overrides to vehicle-engine effect prefabs.
		if (effectPrefab != null && effectPrefab.GetComponent<VehicleSFX>() != null)
		{
			definition = default;
			return false;
		}

		if (!TryInferVehicleType(vehiclePrefab, vehiclePrefabName, out EmergencySirenVehicleType vehicleType) &&
			!TryInferVehicleTypeFromName(effectPrefabName, out vehicleType))
		{
			definition = default;
			return false;
		}

		definition = new TargetDefinition(effectPrefabName, vehicleType, InferRegion(vehiclePrefabName, effectPrefabName));
		return true;
	}

	// Conservative siren-effect name match used for dynamic agent/police target discovery.
	private static bool IsLikelySirenEffectName(string effectPrefabName)
	{
		if (string.IsNullOrWhiteSpace(effectPrefabName))
		{
			return false;
		}

		return TryGetTargetDefinition(effectPrefabName, out _) ||
			ContainsTextToken(effectPrefabName, "siren") ||
			ContainsTextToken(effectPrefabName, "alarm") ||
			ContainsTextToken(effectPrefabName, "emergency") ||
			ContainsTextToken(effectPrefabName, "warning");
	}

	// Infer emergency vehicle category from prefab components first, then by prefab-name hints.
	private static bool TryInferVehicleType(
		PrefabBase vehiclePrefab,
		string vehiclePrefabName,
		out EmergencySirenVehicleType vehicleType)
	{
		if (vehiclePrefab.GetComponent<Game.Prefabs.PoliceCar>() != null)
		{
			vehicleType = EmergencySirenVehicleType.Police;
			return true;
		}

		if (vehiclePrefab.GetComponent<Game.Prefabs.FireEngine>() != null)
		{
			vehicleType = EmergencySirenVehicleType.Fire;
			return true;
		}

		if (vehiclePrefab.GetComponent<Game.Prefabs.Ambulance>() != null)
		{
			vehicleType = EmergencySirenVehicleType.Ambulance;
			return true;
		}

		return TryInferVehicleTypeFromName(vehiclePrefabName, out vehicleType);
	}

	// Infer emergency vehicle category from naming for modded/variant prefabs.
	private static bool TryInferVehicleTypeFromName(string prefabName, out EmergencySirenVehicleType vehicleType)
	{
		if (ContainsTextToken(prefabName, "police") ||
			ContainsTextToken(prefabName, "patrol") ||
			ContainsTextToken(prefabName, "agent") ||
			ContainsTextToken(prefabName, "administration") ||
			ContainsTextToken(prefabName, "admin") ||
			ContainsTextToken(prefabName, "sheriff"))
		{
			vehicleType = EmergencySirenVehicleType.Police;
			return true;
		}

		if (ContainsTextToken(prefabName, "fire") ||
			ContainsTextToken(prefabName, "engine") ||
			ContainsTextToken(prefabName, "rescue"))
		{
			vehicleType = EmergencySirenVehicleType.Fire;
			return true;
		}

		if (ContainsTextToken(prefabName, "ambulance") ||
			ContainsTextToken(prefabName, "medic") ||
			ContainsTextToken(prefabName, "paramedic") ||
			ContainsTextToken(prefabName, "ems"))
		{
			vehicleType = EmergencySirenVehicleType.Ambulance;
			return true;
		}

		vehicleType = EmergencySirenVehicleType.Police;
		return false;
	}

	// Infer region from prefab/effect naming conventions (NA_/EU_ and ...SirenNA/...SirenEU).
	private static SirenRegion InferRegion(string vehiclePrefabName, string effectPrefabName)
	{
		if (StartsWithRegionToken(vehiclePrefabName, "EU") ||
			StartsWithRegionToken(effectPrefabName, "EU") ||
			effectPrefabName.IndexOf("SirenEU", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return SirenRegion.Europe;
		}

		if (StartsWithRegionToken(vehiclePrefabName, "NA") ||
			StartsWithRegionToken(effectPrefabName, "NA") ||
			effectPrefabName.IndexOf("SirenNA", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return SirenRegion.NorthAmerica;
		}

		return SirenRegion.NorthAmerica;
	}

	// Prefix token check for region-coded prefab names.
	private static bool StartsWithRegionToken(string value, string token)
	{
		if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(token))
		{
			return false;
		}

		return value.StartsWith(token + "_", StringComparison.OrdinalIgnoreCase);
	}

	// Case-insensitive contains helper for simple name-token inference.
	private static bool ContainsTextToken(string value, string token)
	{
		if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(token))
		{
			return false;
		}

		return value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
	}

	// Map prefab name to emergency vehicle type.
	private static bool TryGetVehicleType(string prefabName, out EmergencySirenVehicleType vehicleType)
	{
		if (TryGetTargetDefinition(prefabName, out TargetDefinition definition))
		{
			vehicleType = definition.VehicleType;
			return true;
		}

		return TryInferVehicleTypeFromName(prefabName, out vehicleType);
	}

	// Resolve static target definition for a prefab name.
	private static bool TryGetTargetDefinition(string prefabName, out TargetDefinition definition)
	{
		for (int i = 0; i < s_TargetDefinitions.Length; i++)
		{
			if (string.Equals(s_TargetDefinitions[i].PrefabName, prefabName, StringComparison.OrdinalIgnoreCase))
			{
				definition = s_TargetDefinitions[i];
				return true;
			}
		}

		definition = default;
		return false;
	}

	// Resolved relationship between one emergency vehicle prefab and a siren target/effect binding.
	private sealed class VehicleTargetDefinition
	{
		public PrefabBase VehiclePrefab { get; }

		public string VehiclePrefabName { get; }

		public string SirenPrefabName { get; }

		public EmergencySirenVehicleType VehicleType { get; }

		public SirenRegion Region { get; }

		public EffectSource EffectSource { get; }

		public int EffectIndex { get; }

		public EffectPrefab OriginalEffectPrefab { get; }

		public EffectPrefab? OverrideEffectPrefab { get; set; }

		public VehicleTargetDefinition(
			PrefabBase vehiclePrefab,
			string vehiclePrefabName,
			string sirenPrefabName,
			EmergencySirenVehicleType vehicleType,
			SirenRegion region,
			EffectSource effectSource,
			int effectIndex,
			EffectPrefab originalEffectPrefab)
		{
			VehiclePrefab = vehiclePrefab;
			VehiclePrefabName = vehiclePrefabName;
			SirenPrefabName = sirenPrefabName;
			VehicleType = vehicleType;
			Region = region;
			EffectSource = effectSource;
			EffectIndex = effectIndex;
			OriginalEffectPrefab = originalEffectPrefab;
		}

		public bool TryGetEffectSettings(out EffectSource.EffectSettings effectSettings)
		{
			// Resolve effect slot currently pointed to by this target definition.
			effectSettings = null!;
			if (EffectSource == null || EffectSource.m_Effects == null)
			{
				return false;
			}

			if (EffectIndex < 0 || EffectIndex >= EffectSource.m_Effects.Count)
			{
				return false;
			}

			effectSettings = EffectSource.m_Effects[EffectIndex];
			return effectSettings != null;
		}
	}

	// Static description of one target siren prefab.
	private readonly struct TargetDefinition
	{
		public string PrefabName { get; }

		public EmergencySirenVehicleType VehicleType { get; }

		public SirenRegion Region { get; }

		public TargetDefinition(string prefabName, EmergencySirenVehicleType vehicleType, SirenRegion region)
		{
			PrefabName = prefabName;
			VehicleType = vehicleType;
			Region = region;
		}
	}

	// Result state for one resolved selection path.
	private enum ResolvedSelectionOutcome
	{
		Default,
		Mute,
		CustomClip
	}

	// Payload returned from selection resolution.
	private sealed class ResolvedSelection
	{
		public ResolvedSelectionOutcome Outcome { get; set; }

		public AudioClip? Clip { get; set; }

		public SirenSfxProfile? Profile { get; set; }

		public string ReplacementPath { get; set; } = string.Empty;

		public static ResolvedSelection Default()
		{
			// Keep the original siren clip/profile for this target.
			return new ResolvedSelection
			{
				Outcome = ResolvedSelectionOutcome.Default
			};
		}

		public static ResolvedSelection Mute()
		{
			// Force zero volume on the resolved target.
			return new ResolvedSelection
			{
				Outcome = ResolvedSelectionOutcome.Mute
			};
		}

		public static ResolvedSelection Custom(AudioClip clip, SirenSfxProfile profile, string replacementPath)
		{
			// Apply custom clip and profile parameters to the resolved target.
			return new ResolvedSelection
			{
				Outcome = ResolvedSelectionOutcome.CustomClip,
				Clip = clip,
				Profile = profile,
				ReplacementPath = replacementPath
			};
		}
	}

	// Cached clip/profile lookup result keyed by selection string.
	private sealed class SelectionLoadResult
	{
		public bool Success { get; set; }

		public bool IsPending { get; set; }

		public AudioClip? Clip { get; set; }

		public SirenSfxProfile? Profile { get; set; }

		public string FilePath { get; set; } = string.Empty;

		public string Error { get; set; } = string.Empty;
	}

	[DataContract]
	// Diagnostics record written into DetectedSirens.json.
	private sealed class DetectedSirenClip
	{
		[DataMember(Order = 1)]
		public string PrefabName { get; set; } = string.Empty;

		[DataMember(Order = 2)]
		public string ClipName { get; set; } = string.Empty;

		[DataMember(Order = 3)]
		public float LengthSeconds { get; set; }

		[DataMember(Order = 4)]
		public int Frequency { get; set; }

		[DataMember(Order = 5)]
		public int Channels { get; set; }

		[DataMember(Order = 6)]
		public bool AppliedReplacement { get; set; }

		[DataMember(Order = 7)]
		public string MatchRule { get; set; } = string.Empty;

		[DataMember(Order = 8)]
		public string ReplacementPath { get; set; } = string.Empty;

		public static DetectedSirenClip From(
			string prefabName,
			string clipName,
			AudioClip clip,
			bool appliedReplacement,
			string matchRule,
			string replacementPath)
		{
			return new DetectedSirenClip
			{
				PrefabName = prefabName,
				ClipName = clipName,
				LengthSeconds = clip.length,
				Frequency = clip.frequency,
				Channels = clip.channels,
				AppliedReplacement = appliedReplacement,
				MatchRule = matchRule,
				ReplacementPath = replacementPath
			};
		}
	}
}






