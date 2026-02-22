using System;
using System.Collections.Generic;
using Game;
using Game.Common;
using Game.Prefabs;
using Game.Prefabs.Effects;
using Game.SceneFlow;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace SirenChanger;

// Runtime ECS system that applies configured custom engine audio selections.
public sealed partial class VehicleEngineReplacementSystem : GameSystemBase
{
	private PrefabSystem m_PrefabSystem = null!;

	private EntityQuery m_PrefabQuery = default;

	private readonly Dictionary<string, SFX> m_EngineSfxByPrefab = new Dictionary<string, SFX>(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<string, SirenSfxSnapshot> m_DefaultEngineSfxByPrefab = new Dictionary<string, SirenSfxSnapshot>(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<string, List<VehicleEngineTargetDefinition>> m_VehicleTargetsByPrefab = new Dictionary<string, List<VehicleEngineTargetDefinition>>(StringComparer.OrdinalIgnoreCase);

	private static readonly string[] s_EngineEffectTokens =
	{
		"engine",
		"motor",
		"exhaust",
		"idle",
		"rpm",
		"rev",
		"diesel",
		"electric",
		"ev",
		"hybrid"
	};

	private static readonly string[] s_NonEngineEffectTokens =
	{
		"siren",
		"alarm",
		"horn",
		"radio",
		"music",
		"door",
		"beep",
		"blink",
		"indicator",
		"brake",
		"impact",
		"collision",
		"crash"
	};

	private bool m_TargetsBuilt;

	private bool m_WasLoading = true;

	private int m_LastAppliedConfigVersion = -1;

	private int m_LastAppliedAudioLoadVersion = -1;

	protected override void OnCreate()
	{
		base.OnCreate();
		m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
		m_PrefabQuery = GetEntityQuery(ComponentType.ReadOnly<PrefabData>());
	}

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

		ApplyConfiguredEngines();
		m_LastAppliedConfigVersion = SirenChangerMod.ConfigVersion;
		m_LastAppliedAudioLoadVersion = currentAudioLoadVersion;
	}

	private void ResetSessionState()
	{
		RestoreAllVehicleEffectBindings(disposeOverrides: true);
		m_TargetsBuilt = false;
		SirenChangerMod.ResetDetectedAudioDomain(DeveloperAudioDomain.VehicleEngine);
		m_EngineSfxByPrefab.Clear();
		m_DefaultEngineSfxByPrefab.Clear();
		m_VehicleTargetsByPrefab.Clear();
		m_LastAppliedConfigVersion = -1;
		m_LastAppliedAudioLoadVersion = -1;
	}

	private void BuildTargetCache()
	{
		RestoreAllVehicleEffectBindings(disposeOverrides: true);
		m_EngineSfxByPrefab.Clear();
		m_DefaultEngineSfxByPrefab.Clear();
		m_VehicleTargetsByPrefab.Clear();
		SirenChangerMod.BeginDetectedAudioCollection(DeveloperAudioDomain.VehicleEngine);

		SirenSfxProfile template = SirenSfxProfile.CreateFallback();
		bool templateSet = false;
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
				VehicleSFX vehicleSfx = prefab.GetComponent<VehicleSFX>();
				if (sfx != null && vehicleSfx != null && sfx.m_AudioClip != null)
				{
					SirenChangerMod.RegisterDetectedAudioEntry(DeveloperAudioDomain.VehicleEngine, prefabName, sfx);
					m_EngineSfxByPrefab[prefabName] = sfx;
					if (!m_DefaultEngineSfxByPrefab.ContainsKey(prefabName))
					{
						m_DefaultEngineSfxByPrefab[prefabName] = SirenSfxSnapshot.FromSfx(sfx);
					}

					if (defaultPreviewClip == null)
					{
						defaultPreviewClip = sfx.m_AudioClip;
					}

					if (!templateSet)
					{
						template = SirenSfxProfile.FromSfx(sfx);
						templateSet = true;
					}
				}

				TryRegisterVehicleTargets(prefab, prefabName, discoveredVehiclePrefabs);
			}
		}

		if (m_EngineSfxByPrefab.Count == 0)
		{
			SirenChangerMod.CompleteDetectedAudioCollection(DeveloperAudioDomain.VehicleEngine);
			SirenChangerMod.SetVehicleEngineDefaultPreviewClip(null);
			SirenChangerMod.Log.Warn("No vehicle engine SFX prefabs were found in loaded prefabs.");
			return;
		}

		List<string> discovered = new List<string>(discoveredVehiclePrefabs);
		discovered.Sort(StringComparer.OrdinalIgnoreCase);
		SirenChangerMod.SetDiscoveredVehicleEnginePrefabs(discovered);
		SirenChangerMod.SetVehicleEngineProfileTemplate(template);
		SirenChangerMod.SetVehicleEngineDefaultPreviewClip(defaultPreviewClip);
		SirenChangerMod.CompleteDetectedAudioCollection(DeveloperAudioDomain.VehicleEngine);
		SirenChangerMod.SyncCustomVehicleEngineCatalog(saveIfChanged: true);
		m_TargetsBuilt = true;
	}
	private void ApplyConfiguredEngines()
	{
		AudioReplacementDomainConfig config = SirenChangerMod.VehicleEngineConfig;
		config.Normalize(SirenChangerMod.VehicleEngineCustomFolderName);

		RestoreAllEngineTargetDefaults();
		RestoreAllVehicleEffectBindings(disposeOverrides: false);

		if (!config.Enabled)
		{
			SirenChangerMod.Log.Info("Vehicle engine apply skipped because engine replacement is disabled.");
			return;
		}

		Dictionary<string, SelectionLoadResult> selectionLoadCache = new Dictionary<string, SelectionLoadResult>(StringComparer.OrdinalIgnoreCase);
		int globalReplacementCount = 0;
		int vehicleOverrideCount = 0;

		string defaultSelection = config.DefaultSelection;
		if (!AudioReplacementDomainConfig.IsDefaultSelection(defaultSelection))
		{
			List<string> enginePrefabs = new List<string>(m_EngineSfxByPrefab.Keys);
			enginePrefabs.Sort(StringComparer.OrdinalIgnoreCase);
			for (int i = 0; i < enginePrefabs.Count; i++)
			{
				string prefabName = enginePrefabs[i];
				if (!m_EngineSfxByPrefab.TryGetValue(prefabName, out SFX sfx) || sfx == null)
				{
					continue;
				}

				ResolvedSelection resolved = ResolveSelection(defaultSelection, config, selectionLoadCache, $"EngineDefault:{prefabName}");
				if (ApplyResolvedSelectionToSfx(sfx, prefabName, resolved))
				{
					globalReplacementCount++;
				}
			}
		}

		List<string> vehiclePrefabs = new List<string>(m_VehicleTargetsByPrefab.Keys);
		vehiclePrefabs.Sort(StringComparer.OrdinalIgnoreCase);
		for (int i = 0; i < vehiclePrefabs.Count; i++)
		{
			string vehiclePrefab = vehiclePrefabs[i];
			string overrideSelection = config.GetTargetSelection(vehiclePrefab);
			if (AudioReplacementDomainConfig.IsDefaultSelection(overrideSelection))
			{
				continue;
			}

			ResolvedSelection resolved = ResolveSelection(overrideSelection, config, selectionLoadCache, $"EngineVehicle:{vehiclePrefab}");
			if (resolved.Outcome == ResolvedSelectionOutcome.Default)
			{
				continue;
			}

			if (!m_VehicleTargetsByPrefab.TryGetValue(vehiclePrefab, out List<VehicleEngineTargetDefinition>? targets) || targets == null)
			{
				continue;
			}

			for (int j = 0; j < targets.Count; j++)
			{
				if (ApplyResolvedSelectionToVehicleOverride(targets[j], resolved))
				{
					vehicleOverrideCount++;
				}
			}
		}

		SirenChangerMod.Log.Info(
			$"Vehicle engine apply complete. Enabled={config.Enabled}, GlobalReplacements={globalReplacementCount}, VehicleOverrides={vehicleOverrideCount}.");
	}

	private void RestoreAllEngineTargetDefaults()
	{
		foreach (KeyValuePair<string, SirenSfxSnapshot> pair in m_DefaultEngineSfxByPrefab)
		{
			if (m_EngineSfxByPrefab.TryGetValue(pair.Key, out SFX sfx) && sfx != null)
			{
				pair.Value.Restore(sfx);
			}
		}
	}

	private bool ApplyResolvedSelectionToSfx(SFX sfx, string targetLabel, ResolvedSelection resolved)
	{
		switch (resolved.Outcome)
		{
			case ResolvedSelectionOutcome.CustomClip:
				resolved.Profile!.ApplyTo(sfx);
				sfx.m_AudioClip = resolved.Clip!;
				return true;
			case ResolvedSelectionOutcome.Mute:
				sfx.m_Volume = 0f;
				return true;
			default:
				return false;
		}
	}

	private bool ApplyResolvedSelectionToVehicleOverride(VehicleEngineTargetDefinition vehicleTarget, ResolvedSelection resolved)
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

				if (m_DefaultEngineSfxByPrefab.TryGetValue(vehicleTarget.EnginePrefabName, out SirenSfxSnapshot snapshot))
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

	private bool EnsureVehicleOverrideSfx(VehicleEngineTargetDefinition vehicleTarget, out SFX sfx)
	{
		sfx = null!;
		if (!TryGetWritableVehicleEffectSettings(vehicleTarget, out EffectSource.EffectSettings effectSettings))
		{
			return false;
		}

		EffectPrefab? overrideEffect = vehicleTarget.OverrideEffectPrefab;
		if (overrideEffect == null)
		{
			string overrideName = BuildVehicleOverrideEffectName(vehicleTarget.VehiclePrefabName, vehicleTarget.EnginePrefabName);
			overrideEffect = CloneEffectPrefab(vehicleTarget.OriginalEffectPrefab, overrideName);
			if (overrideEffect == null)
			{
				SirenChangerMod.Log.Warn(
					$"Vehicle engine override skipped for '{vehicleTarget.VehiclePrefabName}' because effect prefab clone failed.");
				return false;
			}

			vehicleTarget.OverrideEffectPrefab = overrideEffect;
		}

		sfx = overrideEffect.GetComponent<SFX>();
		if (sfx == null)
		{
			SirenChangerMod.Log.Warn(
				$"Vehicle engine override skipped for '{vehicleTarget.VehiclePrefabName}' because cloned effect has no SFX component.");
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

	// Resolve a writable EffectSettings entry, cloning inherited EffectSource only when an override is actually applied.
	private bool TryGetWritableVehicleEffectSettings(
		VehicleEngineTargetDefinition vehicleTarget,
		out EffectSource.EffectSettings effectSettings)
	{
		effectSettings = null!;
		if (vehicleTarget.OriginalEffectPrefab == null || vehicleTarget.EffectSource == null)
		{
			return false;
		}

		EffectSource uniqueEffectSource;
		try
		{
			uniqueEffectSource = EnsureUniqueEffectSourceComponent(vehicleTarget.VehiclePrefab, vehicleTarget.EffectSource);
		}
		catch (Exception ex)
		{
			SirenChangerMod.Log.Warn(
				$"Vehicle engine override skipped for '{vehicleTarget.VehiclePrefabName}' because EffectSource clone failed: {ex.Message}");
			return false;
		}

		if (uniqueEffectSource.m_Effects == null ||
			vehicleTarget.EffectIndex < 0 ||
			vehicleTarget.EffectIndex >= uniqueEffectSource.m_Effects.Count)
		{
			SirenChangerMod.Log.Warn(
				$"Vehicle engine override skipped for '{vehicleTarget.VehiclePrefabName}' because EffectSource index {vehicleTarget.EffectIndex} was unavailable.");
			return false;
		}

		vehicleTarget.EffectSource = uniqueEffectSource;
		effectSettings = uniqueEffectSource.m_Effects[vehicleTarget.EffectIndex];
		if (effectSettings == null)
		{
			SirenChangerMod.Log.Warn(
				$"Vehicle engine override skipped for '{vehicleTarget.VehiclePrefabName}' because EffectSettings at index {vehicleTarget.EffectIndex} was null.");
			return false;
		}

		return true;
	}

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
			SirenChangerMod.Log.Warn($"Failed to duplicate engine effect prefab '{source.name}' for '{cloneName}': {ex.Message}");
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
			SirenChangerMod.Log.Warn($"Duplicated engine effect prefab '{cloneName}' was not registered in PrefabSystem.");
			m_PrefabSystem.RemovePrefab(clonedEffect);
			UnityEngine.Object.Destroy(clonedEffect);
			return null;
		}

		return clonedEffect;
	}

	private void RestoreAllVehicleEffectBindings(bool disposeOverrides)
	{
		foreach (KeyValuePair<string, List<VehicleEngineTargetDefinition>> pair in m_VehicleTargetsByPrefab)
		{
			List<VehicleEngineTargetDefinition> targets = pair.Value;
			for (int i = 0; i < targets.Count; i++)
			{
				VehicleEngineTargetDefinition target = targets[i];
				bool restored = false;
				if (target.OriginalEffectPrefab != null &&
					target.TryGetEffectSettings(out EffectSource.EffectSettings effectSettings) &&
					!ReferenceEquals(effectSettings.m_Effect, target.OriginalEffectPrefab))
				{
					effectSettings.m_Effect = target.OriginalEffectPrefab;
					restored = true;
				}

				if (restored)
				{
					m_PrefabSystem.UpdatePrefab(target.VehiclePrefab);
				}

				if (!disposeOverrides || target.OverrideEffectPrefab == null)
				{
					continue;
				}

				if (m_PrefabSystem.TryGetEntity(target.OverrideEffectPrefab, out _))
				{
					m_PrefabSystem.RemovePrefab(target.OverrideEffectPrefab);
				}

				UnityEngine.Object.Destroy(target.OverrideEffectPrefab);
				target.OverrideEffectPrefab = null;
			}
		}
	}

	private static string BuildVehicleOverrideEffectName(string vehiclePrefabName, string enginePrefabName)
	{
		string cleanVehicle = (vehiclePrefabName ?? string.Empty).Replace(' ', '_');
		return $"SC_Engine_{enginePrefabName}_{cleanVehicle}";
	}

	private void TryRegisterVehicleTargets(PrefabBase prefab, string prefabName, ISet<string> discoveredVehiclePrefabs)
	{
		if (string.IsNullOrWhiteSpace(prefabName) || !IsLikelyVehiclePrefab(prefab, prefabName))
		{
			return;
		}

		EffectSource effectSource = prefab.GetComponent<EffectSource>();
		if (effectSource == null || effectSource.m_Effects == null || effectSource.m_Effects.Count == 0)
		{
			return;
		}

		List<VehicleEngineTargetDefinition> fallbackTargets = new List<VehicleEngineTargetDefinition>();
		List<VehicleEngineTargetDefinition> preferredTargets = new List<VehicleEngineTargetDefinition>();
		for (int i = 0; i < effectSource.m_Effects.Count; i++)
		{
			EffectSource.EffectSettings effect = effectSource.m_Effects[i];
			if (effect == null || effect.m_Effect == null)
			{
				continue;
			}

			SFX? sfx = effect.m_Effect.GetComponent<SFX>();
			VehicleSFX? vehicleSfx = effect.m_Effect.GetComponent<VehicleSFX>();
			if (sfx == null || vehicleSfx == null || sfx.m_AudioClip == null)
			{
				continue;
			}

			string effectPrefabName = effect.m_Effect.name ?? string.Empty;
			string clipName = sfx.m_AudioClip.name ?? string.Empty;
			VehicleEngineTargetDefinition target = new VehicleEngineTargetDefinition(
				prefab,
				prefabName,
				effectPrefabName,
				effectSource,
				i,
				effect.m_Effect);
			fallbackTargets.Add(target);
			if (IsLikelyVehicleEngineEffect(effectPrefabName, clipName))
			{
				preferredTargets.Add(target);
			}
		}

		if (fallbackTargets.Count == 0)
		{
			return;
		}

		List<VehicleEngineTargetDefinition> targets = preferredTargets.Count > 0
			? preferredTargets
			: new List<VehicleEngineTargetDefinition>(1) { fallbackTargets[0] };
		m_VehicleTargetsByPrefab[prefabName] = targets;
		discoveredVehiclePrefabs.Add(prefabName);
	}

	private static bool IsLikelyVehicleEngineEffect(string effectPrefabName, string clipName)
	{
		if (ContainsAnyToken(effectPrefabName, s_NonEngineEffectTokens) ||
			ContainsAnyToken(clipName, s_NonEngineEffectTokens))
		{
			return false;
		}

		return ContainsAnyToken(effectPrefabName, s_EngineEffectTokens) ||
			ContainsAnyToken(clipName, s_EngineEffectTokens);
	}

	private static bool ContainsAnyToken(string value, string[] tokens)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return false;
		}

		for (int i = 0; i < tokens.Length; i++)
		{
			if (value.IndexOf(tokens[i], StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return true;
			}
		}

		return false;
	}

	private static EffectSource EnsureUniqueEffectSourceComponent(PrefabBase prefab, EffectSource source)
	{
		ComponentBase? exact = prefab.GetComponentExactly(typeof(EffectSource));
		if (exact is EffectSource exactEffectSource)
		{
			return exactEffectSource;
		}

		return (EffectSource)prefab.AddComponentFrom(source);
	}

	private static bool IsLikelyVehiclePrefab(PrefabBase prefab, string prefabName)
	{
		if (prefab is Game.Prefabs.VehiclePrefab)
		{
			return true;
		}

		return prefabName.IndexOf("car", StringComparison.OrdinalIgnoreCase) >= 0 ||
			prefabName.IndexOf("truck", StringComparison.OrdinalIgnoreCase) >= 0 ||
			prefabName.IndexOf("bus", StringComparison.OrdinalIgnoreCase) >= 0 ||
			prefabName.IndexOf("train", StringComparison.OrdinalIgnoreCase) >= 0 ||
			prefabName.IndexOf("tram", StringComparison.OrdinalIgnoreCase) >= 0 ||
			prefabName.IndexOf("taxi", StringComparison.OrdinalIgnoreCase) >= 0 ||
			prefabName.IndexOf("ambulance", StringComparison.OrdinalIgnoreCase) >= 0 ||
			prefabName.IndexOf("police", StringComparison.OrdinalIgnoreCase) >= 0 ||
			prefabName.IndexOf("fire", StringComparison.OrdinalIgnoreCase) >= 0 ||
			prefabName.IndexOf("hearse", StringComparison.OrdinalIgnoreCase) >= 0;
	}

	private ResolvedSelection ResolveSelection(
		string selectionKey,
		AudioReplacementDomainConfig config,
		Dictionary<string, SelectionLoadResult> selectionLoadCache,
		string contextLabel)
	{
		if (AudioReplacementDomainConfig.IsDefaultSelection(selectionKey))
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

		SirenChangerMod.Log.Warn($"Primary engine selection failed for {contextLabel}: '{selectionKey}'. {primaryResult.Error}");
		switch (config.MissingSelectionFallbackBehavior)
		{
			case SirenFallbackBehavior.Mute:
				return ResolvedSelection.Mute();
			case SirenFallbackBehavior.AlternateCustomSiren:
				return ResolveAlternateFallback(selectionKey, config, selectionLoadCache, contextLabel);
			default:
				return ResolvedSelection.Default();
		}
	}

	private ResolvedSelection ResolveAlternateFallback(
		string failedSelectionKey,
		AudioReplacementDomainConfig config,
		Dictionary<string, SelectionLoadResult> selectionLoadCache,
		string contextLabel)
	{
		string alternateSelection = config.AlternateFallbackSelection;
		if (AudioReplacementDomainConfig.IsDefaultSelection(alternateSelection))
		{
			SirenChangerMod.Log.Warn($"Alternate engine fallback is configured for {contextLabel}, but Alternate is set to Default.");
			return ResolvedSelection.Default();
		}

		if (string.Equals(alternateSelection, failedSelectionKey, StringComparison.OrdinalIgnoreCase))
		{
			SirenChangerMod.Log.Warn($"Alternate engine fallback for {contextLabel} points to same selection '{alternateSelection}'.");
			return ResolvedSelection.Default();
		}

		if (!TryGetSelectionLoadResult(alternateSelection, config, selectionLoadCache, out SelectionLoadResult alternateResult))
		{
			if (alternateResult.IsPending)
			{
				return ResolvedSelection.Default();
			}

			SirenChangerMod.Log.Warn($"Alternate engine fallback failed for {contextLabel}: '{alternateSelection}'. {alternateResult.Error}");
			return ResolvedSelection.Default();
		}

		SirenChangerMod.Log.Info($"Applied alternate engine fallback '{alternateSelection}' for {contextLabel} after '{failedSelectionKey}' failed.");
		return ResolvedSelection.Custom(alternateResult.Clip!, alternateResult.Profile!, alternateResult.FilePath);
	}

	private static bool TryGetSelectionLoadResult(
		string selectionKey,
		AudioReplacementDomainConfig config,
		Dictionary<string, SelectionLoadResult> selectionLoadCache,
		out SelectionLoadResult result)
	{
		string normalizedSelection = AudioReplacementDomainConfig.NormalizeProfileKey(selectionKey);
		if (selectionLoadCache.TryGetValue(normalizedSelection, out result))
		{
			return result.Success;
		}

		result = new SelectionLoadResult();
		if (string.IsNullOrWhiteSpace(normalizedSelection) || AudioReplacementDomainConfig.IsDefaultSelection(normalizedSelection))
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
			DeveloperAudioDomain.VehicleEngine,
			config.CustomFolderName,
			normalizedSelection,
			out string filePath))
		{
			result.Error = $"Custom audio file was not found for '{normalizedSelection}'.";
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

	private enum ResolvedSelectionOutcome
	{
		Default,
		Mute,
		CustomClip
	}

	private sealed class ResolvedSelection
	{
		public ResolvedSelectionOutcome Outcome { get; set; }

		public AudioClip? Clip { get; set; }

		public SirenSfxProfile? Profile { get; set; }

		public string ReplacementPath { get; set; } = string.Empty;

		public static ResolvedSelection Default()
		{
			return new ResolvedSelection
			{
				Outcome = ResolvedSelectionOutcome.Default
			};
		}

		public static ResolvedSelection Mute()
		{
			return new ResolvedSelection
			{
				Outcome = ResolvedSelectionOutcome.Mute
			};
		}

		public static ResolvedSelection Custom(AudioClip clip, SirenSfxProfile profile, string replacementPath)
		{
			return new ResolvedSelection
			{
				Outcome = ResolvedSelectionOutcome.CustomClip,
				Clip = clip,
				Profile = profile,
				ReplacementPath = replacementPath
			};
		}
	}

	private sealed class SelectionLoadResult
	{
		public bool Success { get; set; }

		public bool IsPending { get; set; }

		public AudioClip? Clip { get; set; }

		public SirenSfxProfile? Profile { get; set; }

		public string FilePath { get; set; } = string.Empty;

		public string Error { get; set; } = string.Empty;
	}

	private sealed class VehicleEngineTargetDefinition
	{
		public PrefabBase VehiclePrefab { get; }

		public string VehiclePrefabName { get; }

		public string EnginePrefabName { get; }

		public EffectSource EffectSource { get; set; }

		public int EffectIndex { get; }

		public EffectPrefab OriginalEffectPrefab { get; }

		public EffectPrefab? OverrideEffectPrefab { get; set; }

		public VehicleEngineTargetDefinition(
			PrefabBase vehiclePrefab,
			string vehiclePrefabName,
			string enginePrefabName,
			EffectSource effectSource,
			int effectIndex,
			EffectPrefab originalEffectPrefab)
		{
			VehiclePrefab = vehiclePrefab;
			VehiclePrefabName = vehiclePrefabName;
			EnginePrefabName = enginePrefabName;
			EffectSource = effectSource;
			EffectIndex = effectIndex;
			OriginalEffectPrefab = originalEffectPrefab;
		}

		public bool TryGetEffectSettings(out EffectSource.EffectSettings effectSettings)
		{
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
}


