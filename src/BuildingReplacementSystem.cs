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

// Runtime ECS system that applies configured custom building audio selections.
public sealed partial class BuildingReplacementSystem : GameSystemBase
{
	// Prefab index and runtime caches used to restore defaults and apply overrides.
	private PrefabSystem m_PrefabSystem = null!;

	private EntityQuery m_PrefabQuery = default;

	private readonly Dictionary<string, List<BuildingTargetDefinition>> m_TargetsByPrefabName = new Dictionary<string, List<BuildingTargetDefinition>>(StringComparer.OrdinalIgnoreCase);

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
		// Rebuild all runtime bindings after game loading completes.
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
		// Re-apply only when config values changed or async clip loading finished.
		if (m_LastAppliedConfigVersion == SirenChangerMod.ConfigVersion &&
			m_LastAppliedAudioLoadVersion == currentAudioLoadVersion)
		{
			return;
		}

		ApplyConfiguredBuildings();
		m_LastAppliedConfigVersion = SirenChangerMod.ConfigVersion;
		m_LastAppliedAudioLoadVersion = currentAudioLoadVersion;
	}

	private void ResetSessionState()
	{
		// Fully unwind temporary overrides before rebuilding discovery caches.
		RestoreAllEffectBindings(disposeOverrides: true);
		m_TargetsBuilt = false;
		SirenChangerMod.ResetDetectedAudioDomain(DeveloperAudioDomain.Building);
		m_TargetsByPrefabName.Clear();
		m_LastAppliedConfigVersion = -1;
		m_LastAppliedAudioLoadVersion = -1;
	}

	private void BuildTargetCache()
	{
		// Discover building SFX sources from direct components and EffectSource entries.
		RestoreAllEffectBindings(disposeOverrides: true);
		m_TargetsByPrefabName.Clear();
		SirenChangerMod.BeginDetectedAudioCollection(DeveloperAudioDomain.Building);

		SirenSfxProfile template = SirenSfxProfile.CreateFallback();
		bool templateSet = false;
		AudioClip? defaultPreviewClip = null;
		HashSet<string> discoveredTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		int skippedInvalidPrefabEntities = 0;

		using (NativeArray<Entity> prefabEntities = m_PrefabQuery.ToEntityArray(Allocator.Temp))
		{
			for (int i = 0; i < prefabEntities.Length; i++)
			{
				if (!TryGetPrefab(prefabEntities[i], out PrefabBase prefab))
				{
					skippedInvalidPrefabEntities++;
					continue;
				}

				if (!IsSupportedBuildingPrefab(prefab))
				{
					continue;
				}

				string prefabName = AudioReplacementDomainConfig.NormalizeTargetKey(prefab.name ?? string.Empty);
				if (string.IsNullOrWhiteSpace(prefabName))
				{
					continue;
				}

				List<DirectSfxTargetDefinition> directTargets = new List<DirectSfxTargetDefinition>();
				List<BuildingEffectTargetDefinition> effectTargets = new List<BuildingEffectTargetDefinition>();

				SFX directSfx = prefab.GetComponent<SFX>();
				if (directSfx != null && directSfx.m_AudioClip != null)
				{
					directTargets.Add(new DirectSfxTargetDefinition(directSfx, SirenSfxSnapshot.FromSfx(directSfx)));
					SirenChangerMod.RegisterDetectedAudioEntry(DeveloperAudioDomain.Building, prefabName, directSfx);
					if (!templateSet)
					{
						template = SirenSfxProfile.FromSfx(directSfx);
						templateSet = true;
					}

					if (defaultPreviewClip == null)
					{
						defaultPreviewClip = directSfx.m_AudioClip;
					}
				}

				EffectSource effectSource = prefab.GetComponent<EffectSource>();
				if (effectSource != null && effectSource.m_Effects != null && effectSource.m_Effects.Count > 0)
				{
					for (int j = 0; j < effectSource.m_Effects.Count; j++)
					{
						EffectSource.EffectSettings effectSettings = effectSource.m_Effects[j];
						if (effectSettings == null || effectSettings.m_Effect == null)
						{
							continue;
						}

						EffectPrefab effectPrefab = effectSettings.m_Effect;
						SFX effectSfx = effectPrefab.GetComponent<SFX>();
						if (effectSfx == null || effectSfx.m_AudioClip == null)
						{
							continue;
						}

						string effectPrefabName = effectPrefab.name ?? string.Empty;
						effectTargets.Add(
							new BuildingEffectTargetDefinition(
								prefab,
								prefabName,
								effectPrefabName,
								effectSource,
								j,
								effectPrefab,
								SirenSfxSnapshot.FromSfx(effectSfx)));
						SirenChangerMod.RegisterDetectedAudioEntry(DeveloperAudioDomain.Building, prefabName, effectSfx);
						if (!templateSet)
						{
							template = SirenSfxProfile.FromSfx(effectSfx);
							templateSet = true;
						}

						if (defaultPreviewClip == null)
						{
							defaultPreviewClip = effectSfx.m_AudioClip;
						}
					}
				}

				if (directTargets.Count == 0 && effectTargets.Count == 0)
				{
					continue;
				}

				if (!m_TargetsByPrefabName.TryGetValue(prefabName, out List<BuildingTargetDefinition>? targetList))
				{
					targetList = new List<BuildingTargetDefinition>();
					m_TargetsByPrefabName[prefabName] = targetList;
				}

				targetList.Add(new BuildingTargetDefinition(prefab, prefabName, directTargets, effectTargets));
				discoveredTargets.Add(prefabName);
			}
		}

		if (skippedInvalidPrefabEntities > 0)
		{
			SirenChangerMod.Log.Warn(
				$"Skipped {skippedInvalidPrefabEntities} prefab entities while building building-sound cache because PrefabData was invalid.");
		}

		List<string> discovered = new List<string>(discoveredTargets);
		discovered.Sort(StringComparer.OrdinalIgnoreCase);
		SirenChangerMod.SetDiscoveredBuildingTargets(discovered);
		SirenChangerMod.SetBuildingProfileTemplate(template);
		SirenChangerMod.SetBuildingDefaultPreviewClip(defaultPreviewClip);
		SirenChangerMod.CompleteDetectedAudioCollection(DeveloperAudioDomain.Building);
		SirenChangerMod.SyncCustomBuildingCatalog(saveIfChanged: true);

		if (discovered.Count == 0)
		{
			SirenChangerMod.Log.Warn("No building audio targets were found in loaded prefabs.");
		}

		// Mark built even when empty to avoid rescanning and logging every frame.
		m_TargetsBuilt = true;
	}

	private void ApplyConfiguredBuildings()
	{
		AudioReplacementDomainConfig config = SirenChangerMod.BuildingConfig;
		config.Normalize(SirenChangerMod.BuildingCustomFolderName);

		// Start from defaults every pass so settings changes are deterministic.
		RestoreAllTargetDefaults();
		RestoreAllEffectBindings(disposeOverrides: false);

		if (!config.Enabled)
		{
			SirenChangerMod.Log.Info("Building apply skipped because building replacement is disabled.");
			return;
		}

		int directAppliedCount = 0;
		int effectAppliedCount = 0;
		if (config.MuteAllTargets)
		{
			MuteAllTargets(ref directAppliedCount, ref effectAppliedCount);
			SirenChangerMod.Log.Info(
				$"Building apply complete. Enabled={config.Enabled}, MutedDirect={directAppliedCount}, MutedEffects={effectAppliedCount}, Replaced=0.");
			return;
		}

		Dictionary<string, SelectionLoadResult> selectionLoadCache = new Dictionary<string, SelectionLoadResult>(StringComparer.OrdinalIgnoreCase);
		List<string> targetNames = new List<string>(m_TargetsByPrefabName.Keys);
		targetNames.Sort(StringComparer.OrdinalIgnoreCase);
		for (int i = 0; i < targetNames.Count; i++)
		{
			string targetName = targetNames[i];
			string selection = config.GetTargetSelection(targetName);
			if (AudioReplacementDomainConfig.IsDefaultSelection(selection))
			{
				selection = config.DefaultSelection;
			}

			ResolvedSelection resolved = ResolveSelection(selection, config, selectionLoadCache, $"BuildingTarget:{targetName}");
			if (resolved.Outcome == ResolvedSelectionOutcome.Default)
			{
				continue;
			}

			if (!m_TargetsByPrefabName.TryGetValue(targetName, out List<BuildingTargetDefinition>? targets) || targets == null)
			{
				continue;
			}

			for (int j = 0; j < targets.Count; j++)
			{
				directAppliedCount += ApplyResolvedSelectionToDirectTargets(targets[j].DirectTargets, resolved);
				effectAppliedCount += ApplyResolvedSelectionToEffectTargets(targets[j].EffectTargets, resolved);
			}
		}

		SirenChangerMod.Log.Info(
			$"Building apply complete. Enabled={config.Enabled}, DirectReplacements={directAppliedCount}, EffectReplacements={effectAppliedCount}.");
	}

	private void MuteAllTargets(ref int directMutedCount, ref int effectMutedCount)
	{
		ResolvedSelection muteSelection = ResolvedSelection.Mute();
		foreach (KeyValuePair<string, List<BuildingTargetDefinition>> pair in m_TargetsByPrefabName)
		{
			List<BuildingTargetDefinition> targets = pair.Value;
			for (int i = 0; i < targets.Count; i++)
			{
				directMutedCount += ApplyResolvedSelectionToDirectTargets(targets[i].DirectTargets, muteSelection);
				effectMutedCount += ApplyResolvedSelectionToEffectTargets(targets[i].EffectTargets, muteSelection);
			}
		}
	}

	private void RestoreAllTargetDefaults()
	{
		// Restore captured startup SFX state for directly attached building targets.
		foreach (KeyValuePair<string, List<BuildingTargetDefinition>> pair in m_TargetsByPrefabName)
		{
			List<BuildingTargetDefinition> targets = pair.Value;
			for (int i = 0; i < targets.Count; i++)
			{
				List<DirectSfxTargetDefinition> directTargets = targets[i].DirectTargets;
				for (int j = 0; j < directTargets.Count; j++)
				{
					DirectSfxTargetDefinition target = directTargets[j];
					if (target.Sfx == null)
					{
						continue;
					}

					target.Snapshot.Restore(target.Sfx);
				}
			}
		}
	}

	private int ApplyResolvedSelectionToDirectTargets(List<DirectSfxTargetDefinition> targets, ResolvedSelection resolved)
	{
		int applied = 0;
		for (int i = 0; i < targets.Count; i++)
		{
			DirectSfxTargetDefinition target = targets[i];
			if (target.Sfx == null)
			{
				continue;
			}

			switch (resolved.Outcome)
			{
				case ResolvedSelectionOutcome.CustomClip:
					resolved.Profile!.ApplyTo(target.Sfx);
					target.Sfx.m_AudioClip = resolved.Clip!;
					applied++;
					break;
				case ResolvedSelectionOutcome.Mute:
					target.Snapshot.Profile.ApplyTo(target.Sfx);
					target.Sfx.m_AudioClip = target.Snapshot.Clip;
					target.Sfx.m_Volume = 0f;
					applied++;
					break;
			}
		}

		return applied;
	}

	private int ApplyResolvedSelectionToEffectTargets(List<BuildingEffectTargetDefinition> targets, ResolvedSelection resolved)
	{
		int applied = 0;
		for (int i = 0; i < targets.Count; i++)
		{
			BuildingEffectTargetDefinition target = targets[i];
			switch (resolved.Outcome)
			{
				case ResolvedSelectionOutcome.CustomClip:
					if (!EnsureEffectOverrideSfx(target, out SFX customSfx))
					{
						continue;
					}

					resolved.Profile!.ApplyTo(customSfx);
					customSfx.m_AudioClip = resolved.Clip!;
					applied++;
					break;
				case ResolvedSelectionOutcome.Mute:
					if (!EnsureEffectOverrideSfx(target, out SFX muteSfx))
					{
						continue;
					}

					target.DefaultSnapshot.Profile.ApplyTo(muteSfx);
					muteSfx.m_AudioClip = target.DefaultSnapshot.Clip;
					muteSfx.m_Volume = 0f;
					applied++;
					break;
			}
		}

		return applied;
	}

	private bool EnsureEffectOverrideSfx(BuildingEffectTargetDefinition target, out SFX sfx)
	{
		sfx = null!;
		if (!TryGetWritableEffectSettings(target, out EffectSource.EffectSettings effectSettings))
		{
			return false;
		}

		EffectPrefab? overrideEffect = target.OverrideEffectPrefab;
		if (overrideEffect == null)
		{
			string overrideName = BuildBuildingOverrideEffectName(target.BuildingPrefabName, target.EffectPrefabName, target.EffectIndex);
			overrideEffect = CloneEffectPrefab(target.OriginalEffectPrefab, overrideName);
			if (overrideEffect == null)
			{
				SirenChangerMod.Log.Warn(
					$"Building override skipped for '{target.BuildingPrefabName}' because effect prefab clone failed.");
				return false;
			}

			target.OverrideEffectPrefab = overrideEffect;
		}

		sfx = overrideEffect.GetComponent<SFX>();
		if (sfx == null)
		{
			SirenChangerMod.Log.Warn(
				$"Building override skipped for '{target.BuildingPrefabName}' because cloned effect has no SFX component.");
			return false;
		}

		bool changed = !ReferenceEquals(effectSettings.m_Effect, overrideEffect);
		effectSettings.m_Effect = overrideEffect;
		if (changed)
		{
			// Notify PrefabSystem so runtime instances pick up the modified binding.
			m_PrefabSystem.UpdatePrefab(target.BuildingPrefab);
		}

		return true;
	}

	// Resolve a writable EffectSettings entry, cloning inherited EffectSource only when an override is actually applied.
	private bool TryGetWritableEffectSettings(
		BuildingEffectTargetDefinition target,
		out EffectSource.EffectSettings effectSettings)
	{
		effectSettings = null!;
		if (target.OriginalEffectPrefab == null || target.EffectSource == null)
		{
			return false;
		}

		EffectSource uniqueEffectSource;
		try
		{
			uniqueEffectSource = EnsureUniqueEffectSourceComponent(target.BuildingPrefab, target.EffectSource);
		}
		catch (Exception ex)
		{
			SirenChangerMod.Log.Warn(
				$"Building override skipped for '{target.BuildingPrefabName}' because EffectSource clone failed: {ex.Message}");
			return false;
		}

		if (uniqueEffectSource.m_Effects == null ||
			target.EffectIndex < 0 ||
			target.EffectIndex >= uniqueEffectSource.m_Effects.Count)
		{
			SirenChangerMod.Log.Warn(
				$"Building override skipped for '{target.BuildingPrefabName}' because EffectSource index {target.EffectIndex} was unavailable.");
			return false;
		}

		target.EffectSource = uniqueEffectSource;
		effectSettings = uniqueEffectSource.m_Effects[target.EffectIndex];
		if (effectSettings == null)
		{
			SirenChangerMod.Log.Warn(
				$"Building override skipped for '{target.BuildingPrefabName}' because EffectSettings at index {target.EffectIndex} was null.");
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
			SirenChangerMod.Log.Warn($"Failed to duplicate building effect prefab '{source.name}' for '{cloneName}': {ex.Message}");
			return null;
		}

		EffectPrefab? clonedEffect = cloned as EffectPrefab;
		if (clonedEffect == null)
		{
			// Cleanup defensive path in case DuplicatePrefab returns an unexpected type.
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
			SirenChangerMod.Log.Warn($"Duplicated building effect prefab '{cloneName}' was not registered in PrefabSystem.");
			m_PrefabSystem.RemovePrefab(clonedEffect);
			UnityEngine.Object.Destroy(clonedEffect);
			return null;
		}

		return clonedEffect;
	}

	private void RestoreAllEffectBindings(bool disposeOverrides)
	{
		// Return every building effect binding to its original prefab; optionally destroy clones.
		foreach (KeyValuePair<string, List<BuildingTargetDefinition>> pair in m_TargetsByPrefabName)
		{
			List<BuildingTargetDefinition> targets = pair.Value;
			for (int i = 0; i < targets.Count; i++)
			{
				BuildingTargetDefinition buildingTarget = targets[i];
				bool restoredAny = false;
				List<BuildingEffectTargetDefinition> effectTargets = buildingTarget.EffectTargets;
				for (int j = 0; j < effectTargets.Count; j++)
				{
					BuildingEffectTargetDefinition target = effectTargets[j];
					if (target.OriginalEffectPrefab != null &&
						target.TryGetEffectSettings(out EffectSource.EffectSettings effectSettings) &&
						!ReferenceEquals(effectSettings.m_Effect, target.OriginalEffectPrefab))
					{
						effectSettings.m_Effect = target.OriginalEffectPrefab;
						restoredAny = true;
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

				if (restoredAny)
				{
					m_PrefabSystem.UpdatePrefab(buildingTarget.BuildingPrefab);
				}
			}
		}
	}

	private static string BuildBuildingOverrideEffectName(string buildingPrefabName, string effectPrefabName, int effectIndex)
	{
		string cleanBuilding = (buildingPrefabName ?? string.Empty).Replace(' ', '_');
		string cleanEffect = (effectPrefabName ?? string.Empty).Replace(' ', '_');
		return $"SC_Building_{cleanBuilding}_{cleanEffect}_{effectIndex}";
	}

	private ResolvedSelection ResolveSelection(
		string selectionKey,
		AudioReplacementDomainConfig config,
		Dictionary<string, SelectionLoadResult> selectionLoadCache,
		string contextLabel)
	{
		// Resolve requested selection first, then fallback according to configured policy.
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

		SirenChangerMod.Log.Warn($"Primary building selection failed for {contextLabel}: '{selectionKey}'. {primaryResult.Error}");
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
		// Guard against invalid fallback loops before loading alternate audio.
		string alternateSelection = config.AlternateFallbackSelection;
		if (AudioReplacementDomainConfig.IsDefaultSelection(alternateSelection))
		{
			SirenChangerMod.Log.Warn($"Alternate building fallback is configured for {contextLabel}, but Alternate is set to Default.");
			return ResolvedSelection.Default();
		}

		if (string.Equals(alternateSelection, failedSelectionKey, StringComparison.OrdinalIgnoreCase))
		{
			SirenChangerMod.Log.Warn($"Alternate building fallback for {contextLabel} points to same selection '{alternateSelection}'.");
			return ResolvedSelection.Default();
		}

		if (!TryGetSelectionLoadResult(alternateSelection, config, selectionLoadCache, out SelectionLoadResult alternateResult))
		{
			if (alternateResult.IsPending)
			{
				return ResolvedSelection.Default();
			}

			SirenChangerMod.Log.Warn($"Alternate building fallback failed for {contextLabel}: '{alternateSelection}'. {alternateResult.Error}");
			return ResolvedSelection.Default();
		}

		SirenChangerMod.Log.Info($"Applied alternate building fallback '{alternateSelection}' for {contextLabel} after '{failedSelectionKey}' failed.");
		return ResolvedSelection.Custom(alternateResult.Clip!, alternateResult.Profile!, alternateResult.FilePath);
	}

	private static bool TryGetSelectionLoadResult(
		string selectionKey,
		AudioReplacementDomainConfig config,
		Dictionary<string, SelectionLoadResult> selectionLoadCache,
		out SelectionLoadResult result)
	{
		// Cache avoids repeated disk/decoder work for the same key in one pass.
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
			DeveloperAudioDomain.Building,
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

	// Guard against transient PrefabData entries whose prefab indices are invalid during world/prefab churn.
	private bool TryGetPrefab(Entity prefabEntity, out PrefabBase prefab)
	{
		prefab = null!;
		try
		{
			prefab = m_PrefabSystem.GetPrefab<PrefabBase>(prefabEntity);
			return prefab != null;
		}
		catch (ArgumentOutOfRangeException)
		{
			return false;
		}
	}

	private static bool IsSupportedBuildingPrefab(PrefabBase prefab)
	{
		return prefab is BuildingPrefab || prefab is BuildingExtensionPrefab;
	}

	private static EffectSource EnsureUniqueEffectSourceComponent(PrefabBase prefab, EffectSource source)
	{
		// If the EffectSource is inherited, clone it so per-building edits do not leak to sibling prefabs.
		ComponentBase? exact = prefab.GetComponentExactly(typeof(EffectSource));
		if (exact is EffectSource exactEffectSource)
		{
			return exactEffectSource;
		}

		return (EffectSource)prefab.AddComponentFrom(source);
	}

	private enum ResolvedSelectionOutcome
	{
		Default,
		Mute,
		CustomClip
	}

	// Lightweight tagged union describing what should be applied to a target.
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

	// Memoized result for one selection key during the current apply cycle.
	private sealed class SelectionLoadResult
	{
		public bool Success { get; set; }

		public bool IsPending { get; set; }

		public AudioClip? Clip { get; set; }

		public SirenSfxProfile? Profile { get; set; }

		public string FilePath { get; set; } = string.Empty;

		public string Error { get; set; } = string.Empty;
	}

	// Captures one building prefab and all SFX bindings that should respond to this target key.
	private sealed class BuildingTargetDefinition
	{
		public PrefabBase BuildingPrefab { get; }

		public string BuildingPrefabName { get; }

		public List<DirectSfxTargetDefinition> DirectTargets { get; }

		public List<BuildingEffectTargetDefinition> EffectTargets { get; }

		public BuildingTargetDefinition(
			PrefabBase buildingPrefab,
			string buildingPrefabName,
			List<DirectSfxTargetDefinition> directTargets,
			List<BuildingEffectTargetDefinition> effectTargets)
		{
			BuildingPrefab = buildingPrefab;
			BuildingPrefabName = buildingPrefabName;
			DirectTargets = directTargets;
			EffectTargets = effectTargets;
		}
	}

	// Direct SFX component attached to a building prefab plus original snapshot state.
	private sealed class DirectSfxTargetDefinition
	{
		public SFX Sfx { get; }

		public SirenSfxSnapshot Snapshot { get; }

		public DirectSfxTargetDefinition(SFX sfx, SirenSfxSnapshot snapshot)
		{
			Sfx = sfx;
			Snapshot = snapshot;
		}
	}

	// Captures where a building's effect binding lives and tracks any runtime clone.
	private sealed class BuildingEffectTargetDefinition
	{
		public PrefabBase BuildingPrefab { get; }

		public string BuildingPrefabName { get; }

		public string EffectPrefabName { get; }

		public EffectSource EffectSource { get; set; }

		public int EffectIndex { get; }

		public EffectPrefab OriginalEffectPrefab { get; }

		public SirenSfxSnapshot DefaultSnapshot { get; }

		public EffectPrefab? OverrideEffectPrefab { get; set; }

		public BuildingEffectTargetDefinition(
			PrefabBase buildingPrefab,
			string buildingPrefabName,
			string effectPrefabName,
			EffectSource effectSource,
			int effectIndex,
			EffectPrefab originalEffectPrefab,
			SirenSfxSnapshot defaultSnapshot)
		{
			BuildingPrefab = buildingPrefab;
			BuildingPrefabName = buildingPrefabName;
			EffectPrefabName = effectPrefabName;
			EffectSource = effectSource;
			EffectIndex = effectIndex;
			OriginalEffectPrefab = originalEffectPrefab;
			DefaultSnapshot = defaultSnapshot;
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

