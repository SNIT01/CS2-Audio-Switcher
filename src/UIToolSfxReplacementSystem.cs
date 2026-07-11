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

// Runtime ECS system that applies configured custom UI/tool sound selections.
public sealed partial class UIToolSfxReplacementSystem : GameSystemBase
{
	// Prefab index and runtime lookup cache for UI/tool SFX targets.
	private PrefabSystem m_PrefabSystem = null!;

	private EntityQuery m_ToolSoundQuery = default;

	private readonly Dictionary<string, SFX> m_UIToolSfxByTarget = new Dictionary<string, SFX>(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<string, SirenSfxSnapshot> m_DefaultUIToolSfxByTarget = new Dictionary<string, SirenSfxSnapshot>(StringComparer.OrdinalIgnoreCase);

	private readonly List<string> m_SortedUIToolTargets = new List<string>();

	private bool m_TargetsBuilt;

	private bool m_WasLoading = true;

	private int m_LastAppliedConfigVersion = -1;

	private int m_LastAppliedAudioLoadVersion = -1;

	private bool m_HasPendingAudioLoads;

	protected override void OnCreate()
	{
		base.OnCreate();
		m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
		m_ToolSoundQuery = GetEntityQuery(ComponentType.ReadOnly<ToolUXSoundSettingsData>());
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
		int currentConfigVersion = SirenChangerMod.GetAudioDomainConfigVersion(DeveloperAudioDomain.UITool);
		// Re-apply only when config values changed or async clip loading finished.
		if (m_LastAppliedConfigVersion == currentConfigVersion)
		{
			if (!m_HasPendingAudioLoads)
			{
				m_LastAppliedAudioLoadVersion = currentAudioLoadVersion;
				return;
			}

			if (m_LastAppliedAudioLoadVersion == currentAudioLoadVersion)
			{
				return;
			}
		}

		ApplyConfiguredUITool();
		m_LastAppliedConfigVersion = currentConfigVersion;
		m_LastAppliedAudioLoadVersion = currentAudioLoadVersion;
	}

	private void ResetSessionState()
	{
		// Keep prefab references valid across map/editor transitions.
		m_TargetsBuilt = false;
		SirenChangerMod.ResetDetectedAudioDomain(DeveloperAudioDomain.UITool);
		m_UIToolSfxByTarget.Clear();
		m_DefaultUIToolSfxByTarget.Clear();
		m_SortedUIToolTargets.Clear();
		m_LastAppliedConfigVersion = -1;
		m_LastAppliedAudioLoadVersion = -1;
		m_HasPendingAudioLoads = false;
	}

	private void BuildTargetCache()
	{
		// Build one deterministic list of UI/tool targets and snapshots for default restore.
		m_UIToolSfxByTarget.Clear();
		m_DefaultUIToolSfxByTarget.Clear();
		m_SortedUIToolTargets.Clear();
		SirenChangerMod.BeginDetectedAudioCollection(DeveloperAudioDomain.UITool);

		SirenSfxProfile template = SirenSfxProfile.CreateFallback();
		bool templateSet = false;
		AudioClip? defaultPreviewClip = null;
		HashSet<string> discoveredTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		if (!m_ToolSoundQuery.IsEmptyIgnoreFilter)
		{
			using (NativeArray<ToolUXSoundSettingsData> soundSettingsArray =
				m_ToolSoundQuery.ToComponentDataArray<ToolUXSoundSettingsData>(Allocator.Temp))
			{
				for (int settingsIndex = 0; settingsIndex < soundSettingsArray.Length; settingsIndex++)
				{
					ToolUXSoundSettingsData soundSettings = soundSettingsArray[settingsIndex];
					for (int i = 0; i < UIToolSoundSettingsFields.EntityFields.Length; i++)
					{
						var field = UIToolSoundSettingsFields.EntityFields[i];
						object? rawValue = field.GetValue(soundSettings);
						if (!(rawValue is Entity soundEntity) || soundEntity == Entity.Null)
						{
							continue;
						}

						if (!TryGetPrefab(soundEntity, out PrefabBase prefab))
						{
							continue;
						}

						SFX sfx = prefab.GetComponent<SFX>();
						if (sfx == null || sfx.m_AudioClip == null)
						{
							continue;
						}

						string targetKey = BuildUIToolTargetKey(field.Name, prefab.name);
						if (string.IsNullOrWhiteSpace(targetKey))
						{
							continue;
						}

						SirenChangerMod.RegisterDetectedAudioEntry(DeveloperAudioDomain.UITool, targetKey, sfx);
						m_UIToolSfxByTarget[targetKey] = sfx;
						discoveredTargets.Add(targetKey);
						if (!m_DefaultUIToolSfxByTarget.ContainsKey(targetKey))
						{
							m_DefaultUIToolSfxByTarget[targetKey] = SirenSfxSnapshot.FromSfx(sfx);
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
			}
		}

		List<string> discovered = new List<string>(discoveredTargets);
		discovered.Sort(StringComparer.OrdinalIgnoreCase);
		m_SortedUIToolTargets.AddRange(discovered);
		SirenChangerMod.SetDiscoveredUIToolTargets(discovered);
		SirenChangerMod.SetUIToolProfileTemplate(template);
		SirenChangerMod.SetUIToolDefaultPreviewClip(defaultPreviewClip);
		SirenChangerMod.CompleteDetectedAudioCollection(DeveloperAudioDomain.UITool);
		SirenChangerMod.SyncCustomUIToolCatalog(saveIfChanged: true);

		if (discovered.Count == 0)
		{
			SirenChangerMod.Log.Warn("No UI/tool SFX targets were found in loaded Tool UX sound settings.");
		}

		// Mark built even when empty to avoid rescanning and logging every frame.
		m_TargetsBuilt = true;
	}

	private void ApplyConfiguredUITool()
	{
		AudioReplacementDomainConfig config = SirenChangerMod.UIToolConfig;
		config.Normalize(SirenChangerMod.UIToolCustomFolderName);
		m_HasPendingAudioLoads = false;

		// Always restore defaults first so toggles/fallbacks never stack stale overrides.
		RestoreAllTargetDefaults();
		if (!config.Enabled)
		{
			SirenChangerMod.Log.Info("UI/tool apply skipped because UI/tool replacement is disabled.");
			return;
		}

		if (config.MuteAllTargets)
		{
			int mutedCount = MuteAllTargets();
			SirenChangerMod.Log.Info($"UI/tool apply complete. Enabled={config.Enabled}, Muted={mutedCount}, Replaced=0.");
			return;
		}

		Dictionary<string, SelectionLoadResult> selectionLoadCache = new Dictionary<string, SelectionLoadResult>(StringComparer.OrdinalIgnoreCase);
		int appliedCount = 0;
		for (int i = 0; i < m_SortedUIToolTargets.Count; i++)
		{
			string target = m_SortedUIToolTargets[i];
			if (!m_UIToolSfxByTarget.TryGetValue(target, out SFX sfx) || sfx == null)
			{
				continue;
			}

			string selection = config.GetTargetSelection(target);
			if (AudioReplacementDomainConfig.IsDefaultSelection(selection))
			{
				selection = config.DefaultSelection;
			}

			ResolvedSelection resolved = ResolveSelection(selection, config, selectionLoadCache, $"UIToolTarget:{target}");
			if (!ApplyResolvedSelectionToSfx(sfx, resolved))
			{
				continue;
			}

			appliedCount++;
		}

		SirenChangerMod.Log.Info($"UI/tool apply complete. Enabled={config.Enabled}, Replaced={appliedCount}.");
	}

	private int MuteAllTargets()
	{
		int mutedCount = 0;
		foreach (SFX sfx in m_UIToolSfxByTarget.Values)
		{
			if (sfx == null)
			{
				continue;
			}

			sfx.m_Volume = 0f;
			mutedCount++;
		}

		return mutedCount;
	}

	private void RestoreAllTargetDefaults()
	{
		// Restore captured startup SFX state for every detected UI/tool target.
		foreach (KeyValuePair<string, SirenSfxSnapshot> pair in m_DefaultUIToolSfxByTarget)
		{
			if (m_UIToolSfxByTarget.TryGetValue(pair.Key, out SFX sfx) && sfx != null)
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
			m_HasPendingAudioLoads = true;
			return ResolvedSelection.Default();
		}

		SirenChangerMod.Log.Warn($"Primary UI/tool selection failed for {contextLabel}: '{selectionKey}'. {primaryResult.Error}");
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
			SirenChangerMod.Log.Warn($"Alternate UI/tool fallback is configured for {contextLabel}, but Alternate is set to Default.");
			return ResolvedSelection.Default();
		}

		if (string.Equals(alternateSelection, failedSelectionKey, StringComparison.OrdinalIgnoreCase))
		{
			SirenChangerMod.Log.Warn($"Alternate UI/tool fallback for {contextLabel} points to same selection '{alternateSelection}'.");
			return ResolvedSelection.Default();
		}

		if (!TryGetSelectionLoadResult(alternateSelection, config, selectionLoadCache, out SelectionLoadResult alternateResult))
		{
			if (alternateResult.IsPending)
			{
				m_HasPendingAudioLoads = true;
				return ResolvedSelection.Default();
			}

			SirenChangerMod.Log.Warn($"Alternate UI/tool fallback failed for {contextLabel}: '{alternateSelection}'. {alternateResult.Error}");
			return ResolvedSelection.Default();
		}

		SirenChangerMod.Log.Info($"Applied alternate UI/tool fallback '{alternateSelection}' for {contextLabel} after '{failedSelectionKey}' failed.");
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
			DeveloperAudioDomain.UITool,
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

		public string SourcePath { get; set; } = string.Empty;

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
				Profile = profile.ClampCopy(),
				SourcePath = replacementPath ?? string.Empty
			};
		}
	}

	// One-pass selection lookup result cached per resolved key.
	private sealed class SelectionLoadResult
	{
		public bool Success { get; set; }

		public bool IsPending { get; set; }

		public string Error { get; set; } = string.Empty;

		public AudioClip? Clip { get; set; }

		public SirenSfxProfile? Profile { get; set; }

		public string FilePath { get; set; } = string.Empty;
	}
}
