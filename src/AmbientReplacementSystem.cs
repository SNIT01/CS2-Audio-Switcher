using System;
using System.Collections.Generic;
using Game;
using Game.Common;
using Game.Effects;
using Game.Prefabs;
using Game.Prefabs.Effects;
using Game.SceneFlow;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace SirenChanger;

// Runtime ECS system that applies configured custom ambient audio selections.
public sealed partial class AmbientReplacementSystem : GameSystemBase
{
	// Prefab index and runtime lookup cache for ambient-capable SFX prefabs.
	private PrefabSystem m_PrefabSystem = null!;

	private EntityQuery m_PrefabQuery = default;

	private readonly Dictionary<string, SFX> m_AmbientSfxByPrefab = new Dictionary<string, SFX>(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<string, SirenSfxSnapshot> m_DefaultAmbientSfxByPrefab = new Dictionary<string, SirenSfxSnapshot>(StringComparer.OrdinalIgnoreCase);

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

		ApplyConfiguredAmbient();
		m_LastAppliedConfigVersion = SirenChangerMod.ConfigVersion;
		m_LastAppliedAudioLoadVersion = currentAudioLoadVersion;
	}

	private void ResetSessionState()
	{
		// Keep prefab references valid across map/editor transitions.
		m_TargetsBuilt = false;
		SirenChangerMod.ResetDetectedAudioDomain(DeveloperAudioDomain.Ambient);
		m_AmbientSfxByPrefab.Clear();
		m_DefaultAmbientSfxByPrefab.Clear();
		m_LastAppliedConfigVersion = -1;
		m_LastAppliedAudioLoadVersion = -1;
	}

	private void BuildTargetCache()
	{
		// Build one deterministic list of ambient targets and snapshots for default restore.
		m_AmbientSfxByPrefab.Clear();
		m_DefaultAmbientSfxByPrefab.Clear();
		SirenChangerMod.BeginDetectedAudioCollection(DeveloperAudioDomain.Ambient);

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

				string prefabName = prefab.name ?? string.Empty;
				SFX sfx = prefab.GetComponent<SFX>();
				if (sfx == null || sfx.m_AudioClip == null || !IsAmbientTarget(prefabName, sfx))
				{
					continue;
				}
				SirenChangerMod.RegisterDetectedAudioEntry(DeveloperAudioDomain.Ambient, prefabName, sfx);

				m_AmbientSfxByPrefab[prefabName] = sfx;
				discoveredTargets.Add(prefabName);
				if (!m_DefaultAmbientSfxByPrefab.ContainsKey(prefabName))
				{
					m_DefaultAmbientSfxByPrefab[prefabName] = SirenSfxSnapshot.FromSfx(sfx);
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
		}

		if (skippedInvalidPrefabEntities > 0)
		{
			SirenChangerMod.Log.Warn(
				$"Skipped {skippedInvalidPrefabEntities} prefab entities while building ambient cache because PrefabData was invalid.");
		}

		if (m_AmbientSfxByPrefab.Count == 0)
		{
			SirenChangerMod.CompleteDetectedAudioCollection(DeveloperAudioDomain.Ambient);
			SirenChangerMod.SetAmbientDefaultPreviewClip(null);
			SirenChangerMod.Log.Warn("No ambient SFX prefabs were found in loaded prefabs.");
			return;
		}

		List<string> discovered = new List<string>(discoveredTargets);
		discovered.Sort(StringComparer.OrdinalIgnoreCase);
		SirenChangerMod.SetDiscoveredAmbientTargets(discovered);
		SirenChangerMod.SetAmbientProfileTemplate(template);
		SirenChangerMod.SetAmbientDefaultPreviewClip(defaultPreviewClip);
		SirenChangerMod.CompleteDetectedAudioCollection(DeveloperAudioDomain.Ambient);
		SirenChangerMod.SyncCustomAmbientCatalog(saveIfChanged: true);
		m_TargetsBuilt = true;
	}
	private static bool IsAmbientTarget(string prefabName, SFX sfx)
	{
		// Prefer explicit mixer groups, then fallback to name-based heuristics.
		if (sfx.m_MixerGroup == MixerGroup.Ambient ||
			sfx.m_MixerGroup == MixerGroup.AudioGroups ||
			sfx.m_MixerGroup == MixerGroup.Disasters)
		{
			return true;
		}

		if (string.IsNullOrWhiteSpace(prefabName))
		{
			return false;
		}

		return prefabName.IndexOf("ambient", StringComparison.OrdinalIgnoreCase) >= 0 ||
			prefabName.IndexOf("rain", StringComparison.OrdinalIgnoreCase) >= 0 ||
			prefabName.IndexOf("water", StringComparison.OrdinalIgnoreCase) >= 0 ||
			prefabName.IndexOf("forest", StringComparison.OrdinalIgnoreCase) >= 0 ||
			prefabName.IndexOf("wind", StringComparison.OrdinalIgnoreCase) >= 0 ||
			prefabName.IndexOf("birds", StringComparison.OrdinalIgnoreCase) >= 0 ||
			prefabName.IndexOf("seagull", StringComparison.OrdinalIgnoreCase) >= 0 ||
			prefabName.IndexOf("nature", StringComparison.OrdinalIgnoreCase) >= 0;
	}

	private void ApplyConfiguredAmbient()
	{
		AudioReplacementDomainConfig config = SirenChangerMod.AmbientConfig;
		config.Normalize(SirenChangerMod.AmbientCustomFolderName);

		// Always restore defaults first so toggles/fallbacks never stack stale overrides.
		RestoreAllTargetDefaults();
		if (!config.Enabled)
		{
			SirenChangerMod.Log.Info("Ambient apply skipped because ambient replacement is disabled.");
			return;
		}

		Dictionary<string, SelectionLoadResult> selectionLoadCache = new Dictionary<string, SelectionLoadResult>(StringComparer.OrdinalIgnoreCase);
		int appliedCount = 0;
		List<string> targets = new List<string>(m_AmbientSfxByPrefab.Keys);
		targets.Sort(StringComparer.OrdinalIgnoreCase);
		for (int i = 0; i < targets.Count; i++)
		{
			string target = targets[i];
			if (!m_AmbientSfxByPrefab.TryGetValue(target, out SFX sfx) || sfx == null)
			{
				continue;
			}

			string selection = config.GetTargetSelection(target);
			if (AudioReplacementDomainConfig.IsDefaultSelection(selection))
			{
				selection = config.DefaultSelection;
			}

			ResolvedSelection resolved = ResolveSelection(selection, config, selectionLoadCache, $"AmbientTarget:{target}");
			if (!ApplyResolvedSelectionToSfx(sfx, resolved))
			{
				continue;
			}

			appliedCount++;
		}

		SirenChangerMod.Log.Info($"Ambient apply complete. Enabled={config.Enabled}, Replaced={appliedCount}.");
	}

	private void RestoreAllTargetDefaults()
	{
		// Restore captured startup SFX state for every detected ambient target.
		foreach (KeyValuePair<string, SirenSfxSnapshot> pair in m_DefaultAmbientSfxByPrefab)
		{
			if (m_AmbientSfxByPrefab.TryGetValue(pair.Key, out SFX sfx) && sfx != null)
			{
				pair.Value.Restore(sfx);
			}
		}
	}

	private static bool ApplyResolvedSelectionToSfx(SFX sfx, ResolvedSelection resolved)
	{
		// Default means "leave original snapshot as-is"; non-default mutates live SFX.
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

	private ResolvedSelection ResolveSelection(
		string selectionKey,
		AudioReplacementDomainConfig config,
		Dictionary<string, SelectionLoadResult> selectionLoadCache,
		string contextLabel)
	{
		// Resolve requested selection first, then fallback according to user policy.
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

		SirenChangerMod.Log.Warn($"Primary ambient selection failed for {contextLabel}: '{selectionKey}'. {primaryResult.Error}");
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
		// Guard against invalid fallback loops before attempting alternate load.
		string alternateSelection = config.AlternateFallbackSelection;
		if (AudioReplacementDomainConfig.IsDefaultSelection(alternateSelection))
		{
			SirenChangerMod.Log.Warn($"Alternate ambient fallback is configured for {contextLabel}, but Alternate is set to Default.");
			return ResolvedSelection.Default();
		}

		if (string.Equals(alternateSelection, failedSelectionKey, StringComparison.OrdinalIgnoreCase))
		{
			SirenChangerMod.Log.Warn($"Alternate ambient fallback for {contextLabel} points to same selection '{alternateSelection}'.");
			return ResolvedSelection.Default();
		}

		if (!TryGetSelectionLoadResult(alternateSelection, config, selectionLoadCache, out SelectionLoadResult alternateResult))
		{
			if (alternateResult.IsPending)
			{
				return ResolvedSelection.Default();
			}

			SirenChangerMod.Log.Warn($"Alternate ambient fallback failed for {contextLabel}: '{alternateSelection}'. {alternateResult.Error}");
			return ResolvedSelection.Default();
		}

		SirenChangerMod.Log.Info($"Applied alternate ambient fallback '{alternateSelection}' for {contextLabel} after '{failedSelectionKey}' failed.");
		return ResolvedSelection.Custom(alternateResult.Clip!, alternateResult.Profile!, alternateResult.FilePath);
	}

	private static bool TryGetSelectionLoadResult(
		string selectionKey,
		AudioReplacementDomainConfig config,
		Dictionary<string, SelectionLoadResult> selectionLoadCache,
		out SelectionLoadResult result)
	{
		// Cache avoids repeatedly decoding the same custom file during one apply pass.
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
			DeveloperAudioDomain.Ambient,
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

	private enum ResolvedSelectionOutcome
	{
		Default,
		Mute,
		CustomClip
	}

	// Lightweight tagged union for resolved target behavior.
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

	// Per-selection load result memoized during one apply pass.
	private sealed class SelectionLoadResult
	{
		public bool Success { get; set; }

		public bool IsPending { get; set; }

		public AudioClip? Clip { get; set; }

		public SirenSfxProfile? Profile { get; set; }

		public string FilePath { get; set; } = string.Empty;

		public string Error { get; set; } = string.Empty;
	}
}

