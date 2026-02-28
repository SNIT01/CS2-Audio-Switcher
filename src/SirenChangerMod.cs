using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Colossal;
using Colossal.Logging;
using Colossal.Localization;
using Game;
using Game.Modding;
using Game.Prefabs;
using Game.Prefabs.Effects;
using Game.SceneFlow;
using Game.UI.Widgets;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace SirenChanger;

// Mod entry point responsible for settings lifecycle, catalog sync, and UI helpers.
public sealed partial class SirenChangerMod : IMod
{
	public static readonly ILog Log = LogManager.GetLogger($"{nameof(SirenChanger)}.{nameof(SirenChangerMod)}").SetShowsErrorsInUI(false);

	private SirenChangerSettings? m_Settings;

	internal static string ModRootPath { get; private set; } = AppDomain.CurrentDomain.BaseDirectory;

	internal static string SettingsDirectory { get; private set; } = string.Empty;

	internal static SirenReplacementConfig Config { get; private set; } = SirenReplacementConfig.CreateDefault();

	internal static int ConfigVersion { get; private set; } = 1;

	internal static int OptionsVersion { get; private set; } = 1;

	internal static SirenSfxProfile CustomProfileTemplate { get; private set; } = SirenSfxProfile.CreateFallback();
	internal const string VehicleEngineSettingsFileName = "VehicleEngineSettings.json";

	internal const string AmbientSettingsFileName = "AmbientSettings.json";

	internal const string VehicleEngineCustomFolderName = "Custom Engines";

	internal const string AmbientCustomFolderName = "Custom Ambient";

	internal static AudioReplacementDomainConfig VehicleEngineConfig { get; private set; } = AudioReplacementDomainConfig.CreateDefault(VehicleEngineCustomFolderName);

	internal static AudioReplacementDomainConfig AmbientConfig { get; private set; } = AudioReplacementDomainConfig.CreateDefault(AmbientCustomFolderName);

	internal static SirenSfxProfile VehicleEngineProfileTemplate { get; private set; } = SirenSfxProfile.CreateFallback();

	internal static SirenSfxProfile AmbientProfileTemplate { get; private set; } = SirenSfxProfile.CreateFallback();

	private static AudioClip? s_DefaultSirenPreviewClip;

	private static AudioClip? s_DefaultVehicleEnginePreviewClip;

	private static AudioClip? s_DefaultAmbientPreviewClip;

	private static int s_DropdownCacheVersion = -1;

	private static DropdownItem<string>[] s_VehicleDropdownWithDefault = Array.Empty<DropdownItem<string>>();

	private static DropdownItem<string>[] s_ProfileDropdownWithoutDefault = Array.Empty<DropdownItem<string>>();

	private static string[] s_DiscoveredVehiclePrefabs = Array.Empty<string>();

	private static int s_VehiclePrefabDropdownCacheVersion = -1;

	private static DropdownItem<string>[] s_VehiclePrefabDropdown = Array.Empty<DropdownItem<string>>();

	private static GameObject? s_PreviewAudioObject;

	private static AudioSource? s_PreviewAudioSource;

	private static PreviewAutoStopper? s_PreviewAutoStopper;

	private const float kPreviewTimeoutSeconds = 5f;

	private static string s_LastPreviewStatus = "No preview has been played in this session.";

	private static string s_LastVehiclePrefabScanStatus = "No scan run yet. Click Rescan Emergency Vehicle Prefabs in a loaded map/editor session.";
	private static string[] s_DiscoveredVehicleEnginePrefabs = Array.Empty<string>();

	private static DropdownItem<string>[] s_VehicleEnginePrefabDropdown = Array.Empty<DropdownItem<string>>();

	private static int s_VehicleEnginePrefabDropdownCacheVersion = -1;

	private static string s_LastVehicleEnginePrefabScanStatus = "No scan run yet. Click Rescan Vehicle Engine Prefabs in a loaded map/editor session.";

	private static string[] s_DiscoveredAmbientTargets = Array.Empty<string>();

	private static DropdownItem<string>[] s_AmbientTargetDropdown = Array.Empty<DropdownItem<string>>();

	private static int s_AmbientTargetDropdownCacheVersion = -1;

	private static string s_LastAmbientTargetScanStatus = "No scan run yet. Click Rescan Ambient Targets in a loaded map/editor session.";

	private static int s_EngineDropdownCacheVersion = -1;

	private static DropdownItem<string>[] s_EngineDropdownWithDefault = Array.Empty<DropdownItem<string>>();

	private static DropdownItem<string>[] s_EngineDropdownWithoutDefault = Array.Empty<DropdownItem<string>>();

	private static int s_AmbientDropdownCacheVersion = -1;

	private static DropdownItem<string>[] s_AmbientDropdownWithDefault = Array.Empty<DropdownItem<string>>();

	private static DropdownItem<string>[] s_AmbientDropdownWithoutDefault = Array.Empty<DropdownItem<string>>();

	private const string kOptionsPanelDisplayName = "Audio Switcher";

	private static readonly Dictionary<string, IDictionarySource> s_OptionsPanelNameSources = new Dictionary<string, IDictionarySource>(StringComparer.OrdinalIgnoreCase);

	// Called once when the mod is loaded by the game.
	public void OnLoad(UpdateSystem updateSystem)
	{
		if (GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset))
		{
			ModRootPath = asset.path;
		}

		SettingsDirectory = SirenPathUtils.GetSettingsDirectory(ensureExists: true);
		InitializeCitySoundProfiles();

		LoadKnownVehiclePrefabsFromConfig();
		LoadKnownVehicleEnginePrefabsFromConfig();
		LoadKnownAmbientTargetsFromConfig();
		SyncCustomSirenCatalog(saveIfChanged: true);
		SyncCustomVehicleEngineCatalog(saveIfChanged: true);
		SyncCustomAmbientCatalog(saveIfChanged: true);
		SyncCustomTransitAnnouncementCatalog(saveIfChanged: true);

		m_Settings = new SirenChangerSettings(this);
		RegisterOptionsPanelLocalization(m_Settings);
		m_Settings.RegisterInOptionsUI();

		updateSystem.UpdateAfter<CitySoundProfileRuntimeSystem>(SystemUpdatePhase.GameSimulation);
		updateSystem.UpdateAfter<SirenReplacementSystem>(SystemUpdatePhase.GameSimulation);
		updateSystem.UpdateAfter<VehicleEngineReplacementSystem>(SystemUpdatePhase.GameSimulation);
		updateSystem.UpdateAfter<AmbientReplacementSystem>(SystemUpdatePhase.GameSimulation);
		updateSystem.UpdateAfter<TransitAnnouncementSystem>(SystemUpdatePhase.GameSimulation);

		Log.Info($"Loaded. Mod path: {ModRootPath}");
	}

	// Called when the mod is being unloaded/disposed.
	public void OnDispose()
	{
		if (m_Settings != null)
		{
			m_Settings.UnregisterInOptionsUI();
			m_Settings = null;
		}

		UnregisterOptionsPanelLocalization();

		ClearAllDetectedDeveloperAudio();
		ReleasePreviewAudio();
		s_DefaultSirenPreviewClip = null;
		s_DefaultVehicleEnginePreviewClip = null;
		s_DefaultAmbientPreviewClip = null;
		TransitAnnouncementAudioPlayer.Release();
		WaveClipLoader.ReleaseLoadedClips();
	}

	// Persist the template used for newly discovered custom sirens.
	internal static void SetCustomProfileTemplate(SirenSfxProfile template)
	{
		if (template == null)
		{
			return;
		}

		CustomProfileTemplate = template.ClampCopy();
	}

		// Persist template for newly discovered custom vehicle engine profiles.
	internal static void SetVehicleEngineProfileTemplate(SirenSfxProfile template)
	{
		if (template == null)
		{
			return;
		}

		VehicleEngineProfileTemplate = template.ClampCopy();
	}

	// Persist template for newly discovered custom ambient profiles.
	internal static void SetAmbientProfileTemplate(SirenSfxProfile template)
	{
		if (template == null)
		{
			return;
		}

		AmbientProfileTemplate = template.ClampCopy();
	}

	// Store a representative built-in siren clip for default preview playback.
	internal static void SetSirenDefaultPreviewClip(AudioClip? clip)
	{
		s_DefaultSirenPreviewClip = clip;
	}

	// Store a representative built-in vehicle engine clip for default preview playback.
	internal static void SetVehicleEngineDefaultPreviewClip(AudioClip? clip)
	{
		s_DefaultVehicleEnginePreviewClip = clip;
	}

	// Store a representative built-in ambient clip for default preview playback.
	internal static void SetAmbientDefaultPreviewClip(AudioClip? clip)
	{
		s_DefaultAmbientPreviewClip = clip;
	}

	// Sync vehicle engine catalog with config profiles and optionally save changes.
	internal static bool SyncCustomVehicleEngineCatalog(bool saveIfChanged, bool forceStatusRefresh = false)
	{
		bool moduleCatalogChanged = RefreshAudioModuleCatalog();
		AudioDomainCatalogSyncResult result = AudioDomainCatalogSync.Synchronize(
			VehicleEngineConfig,
			SettingsDirectory,
			VehicleEngineCustomFolderName,
			VehicleEngineProfileTemplate,
			Log,
			GetAudioModuleProfileKeys(DeveloperAudioDomain.VehicleEngine),
			key => TryGetAudioModuleProfileTemplate(DeveloperAudioDomain.VehicleEngine, key, out SirenSfxProfile profile) ? profile : null);
		bool catalogChanged = result.ConfigChanged;
		bool implicitModuleProfilesChanged = RefreshImplicitModuleTemplateProfiles(
			DeveloperAudioDomain.VehicleEngine,
			VehicleEngineConfig.CustomProfiles,
			VehicleEngineProfileTemplate);
		bool scanMetadataChanged = UpdateDomainCatalogScanMetadata(VehicleEngineConfig, result, forceStatusRefresh);
		if (catalogChanged || implicitModuleProfilesChanged || scanMetadataChanged || moduleCatalogChanged)
		{
			if (saveIfChanged && (catalogChanged || implicitModuleProfilesChanged || scanMetadataChanged))
			{
				SaveConfig();
			}

			if (catalogChanged || implicitModuleProfilesChanged)
			{
				ConfigVersion++;
			}

			NotifyOptionsCatalogChanged();
		}

		return catalogChanged || implicitModuleProfilesChanged;
	}


	// Sync ambient catalog with config profiles and optionally save changes.
	internal static bool SyncCustomAmbientCatalog(bool saveIfChanged, bool forceStatusRefresh = false)
	{
		bool moduleCatalogChanged = RefreshAudioModuleCatalog();
		AudioDomainCatalogSyncResult result = AudioDomainCatalogSync.Synchronize(
			AmbientConfig,
			SettingsDirectory,
			AmbientCustomFolderName,
			AmbientProfileTemplate,
			Log,
			GetAudioModuleProfileKeys(DeveloperAudioDomain.Ambient),
			key => TryGetAudioModuleProfileTemplate(DeveloperAudioDomain.Ambient, key, out SirenSfxProfile profile) ? profile : null);
		bool catalogChanged = result.ConfigChanged;
		bool implicitModuleProfilesChanged = RefreshImplicitModuleTemplateProfiles(
			DeveloperAudioDomain.Ambient,
			AmbientConfig.CustomProfiles,
			AmbientProfileTemplate);
		bool scanMetadataChanged = UpdateDomainCatalogScanMetadata(AmbientConfig, result, forceStatusRefresh);
		if (catalogChanged || implicitModuleProfilesChanged || scanMetadataChanged || moduleCatalogChanged)
		{
			if (saveIfChanged && (catalogChanged || implicitModuleProfilesChanged || scanMetadataChanged))
			{
				SaveConfig();
			}

			if (catalogChanged || implicitModuleProfilesChanged)
			{
				ConfigVersion++;
			}

			NotifyOptionsCatalogChanged();
		}

		return catalogChanged || implicitModuleProfilesChanged;
	}

	// Sync file system catalog with config profiles and optionally save changes.
	internal static bool SyncCustomSirenCatalog(bool saveIfChanged, bool forceStatusRefresh = false)
	{
		bool moduleCatalogChanged = RefreshAudioModuleCatalog();
		SirenCatalogSyncResult result = SirenCatalogSync.Synchronize(
			Config,
			SettingsDirectory,
			CustomProfileTemplate,
			Log,
			GetAudioModuleProfileKeys(DeveloperAudioDomain.Siren),
			key => TryGetAudioModuleProfileTemplate(DeveloperAudioDomain.Siren, key, out SirenSfxProfile profile) ? profile : null);
		bool catalogChanged = result.ConfigChanged;
		bool implicitModuleProfilesChanged = RefreshImplicitModuleTemplateProfiles(
			DeveloperAudioDomain.Siren,
			Config.CustomSirenProfiles,
			CustomProfileTemplate);
		bool scanMetadataChanged = UpdateCatalogScanMetadata(result, forceStatusRefresh);
		if (catalogChanged || implicitModuleProfilesChanged || scanMetadataChanged || moduleCatalogChanged)
		{
			if (saveIfChanged && (catalogChanged || implicitModuleProfilesChanged || scanMetadataChanged))
			{
				SaveConfig();
			}

			if (catalogChanged || implicitModuleProfilesChanged)
			{
				ConfigVersion++;
			}

			NotifyOptionsCatalogChanged();
		}

		return catalogChanged || implicitModuleProfilesChanged;
	}

	// Keep module entries without explicit profiles aligned with the current domain template.
	private static bool RefreshImplicitModuleTemplateProfiles(
		DeveloperAudioDomain domain,
		IDictionary<string, SirenSfxProfile> profiles,
		SirenSfxProfile template)
	{
		if (profiles == null || profiles.Count == 0)
		{
			return false;
		}

		SirenSfxProfile inheritedTemplate = (template ?? SirenSfxProfile.CreateFallback()).ClampCopy();
		List<string> moduleKeys = profiles.Keys
			.Where(static key => AudioModuleCatalog.IsModuleSelection(key))
			.OrderBy(static key => key, StringComparer.OrdinalIgnoreCase)
			.ToList();
		bool changed = false;
		for (int i = 0; i < moduleKeys.Count; i++)
		{
			string key = moduleKeys[i];
			if (AudioModuleCatalog.TryGetProfileTemplate(domain, key, out _))
			{
				// Manifest provided an explicit profile template; preserve that.
				continue;
			}

			if (profiles.TryGetValue(key, out SirenSfxProfile existing) &&
				existing != null &&
				existing.ApproximatelyEquals(inheritedTemplate))
			{
				continue;
			}

			profiles[key] = inheritedTemplate.ClampCopy();
			changed = true;
		}

		return changed;
	}

	// Notify runtime systems that settings values were changed in the options UI.
	internal static void NotifyRuntimeConfigChanged(bool saveToDisk)
	{
		Config.Normalize();
		VehicleEngineConfig.Normalize(VehicleEngineCustomFolderName);
		AmbientConfig.Normalize(AmbientCustomFolderName);
		TransitAnnouncementConfig.Normalize(TransitAnnouncementCustomFolderName);
		NormalizeTransitAnnouncementTargets();
		if (saveToDisk)
		{
			SaveConfig();
		}

		ConfigVersion++;
		OptionsVersion++;
	}

	// Build dropdown data used by vehicle/profile selectors.
	internal static DropdownItem<string>[] BuildSirenDropdownItems(bool includeDefault)
	{
		EnsureDropdownCacheCurrent();
		return includeDefault ? s_VehicleDropdownWithDefault : s_ProfileDropdownWithoutDefault;
	}

	// Build dropdown data for specific emergency vehicle prefabs discovered at runtime.
	internal static DropdownItem<string>[] BuildVehiclePrefabDropdownItems()
	{
		EnsureVehiclePrefabDropdownCurrent();
		return s_VehiclePrefabDropdown;
	}

		// Build dropdown data used by vehicle engine selectors.
	internal static DropdownItem<string>[] BuildVehicleEngineDropdownItems(bool includeDefault)
	{
		EnsureVehicleEngineDropdownCacheCurrent();
		return includeDefault ? s_EngineDropdownWithDefault : s_EngineDropdownWithoutDefault;
	}

	// Build dropdown data for discovered vehicle engine prefab selectors.
	internal static DropdownItem<string>[] BuildVehicleEnginePrefabDropdownItems()
	{
		EnsureVehicleEnginePrefabDropdownCurrent();
		return s_VehicleEnginePrefabDropdown;
	}

	// Build dropdown data used by ambient selectors.
	internal static DropdownItem<string>[] BuildAmbientDropdownItems(bool includeDefault)
	{
		EnsureAmbientDropdownCacheCurrent();
		return includeDefault ? s_AmbientDropdownWithDefault : s_AmbientDropdownWithoutDefault;
	}

	// Build dropdown data for discovered ambient target selectors.
	internal static DropdownItem<string>[] BuildAmbientTargetDropdownItems()
	{
		EnsureAmbientTargetDropdownCurrent();
		return s_AmbientTargetDropdown;
	}
// Returns true when at least one emergency vehicle prefab was mapped to a siren target.
	internal static bool HasDiscoveredVehiclePrefabs()
	{
		return s_DiscoveredVehiclePrefabs.Length > 0;
	}

		// Returns true when at least one engine-capable vehicle prefab was discovered.
	internal static bool HasDiscoveredVehicleEnginePrefabs()
	{
		return s_DiscoveredVehicleEnginePrefabs.Length > 0;
	}

	// Returns true when at least one ambient target prefab was discovered.
	internal static bool HasDiscoveredAmbientTargets()
	{
		return s_DiscoveredAmbientTargets.Length > 0;
	}

	// Update discovered engine vehicle prefab keys and synchronize related config fields.
	internal static void SetDiscoveredVehicleEnginePrefabs(ICollection<string> vehiclePrefabNames)
	{
		List<string> normalized = new List<string>();
		HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		if (vehiclePrefabNames != null)
		{
			foreach (string raw in vehiclePrefabNames)
			{
				string key = AudioReplacementDomainConfig.NormalizeTargetKey(raw);
				if (string.IsNullOrWhiteSpace(key) || !seen.Add(key))
				{
					continue;
				}

				normalized.Add(key);
			}
		}

		normalized.Sort(StringComparer.OrdinalIgnoreCase);
		bool listChanged = !SequenceEqualsIgnoreCase(s_DiscoveredVehicleEnginePrefabs, normalized);
		if (listChanged)
		{
			s_DiscoveredVehicleEnginePrefabs = normalized.ToArray();
			s_LastVehicleEnginePrefabScanStatus = $"Detected {normalized.Count} engine-capable vehicle prefab(s) from loaded prefabs.";
		}

		bool configChanged = VehicleEngineConfig.SynchronizeTargets(normalized);
		if (configChanged)
		{
			SaveConfig();
			ConfigVersion++;
		}

		if (listChanged || configChanged)
		{
			OptionsVersion++;
			s_VehicleEnginePrefabDropdownCacheVersion = -1;
			s_VehicleEnginePrefabDropdown = Array.Empty<DropdownItem<string>>();
		}
	}

	// Update discovered ambient target prefab keys and synchronize related config fields.
	internal static void SetDiscoveredAmbientTargets(ICollection<string> targetNames)
	{
		List<string> normalized = new List<string>();
		HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		if (targetNames != null)
		{
			foreach (string raw in targetNames)
			{
				string key = AudioReplacementDomainConfig.NormalizeTargetKey(raw);
				if (string.IsNullOrWhiteSpace(key) || !seen.Add(key))
				{
					continue;
				}

				normalized.Add(key);
			}
		}

		normalized.Sort(StringComparer.OrdinalIgnoreCase);
		bool listChanged = !SequenceEqualsIgnoreCase(s_DiscoveredAmbientTargets, normalized);
		if (listChanged)
		{
			s_DiscoveredAmbientTargets = normalized.ToArray();
			s_LastAmbientTargetScanStatus = $"Detected {normalized.Count} ambient target prefab(s) from loaded prefabs.";
		}

		bool configChanged = AmbientConfig.SynchronizeTargets(normalized);
		if (configChanged)
		{
			SaveConfig();
			ConfigVersion++;
		}

		if (listChanged || configChanged)
		{
			OptionsVersion++;
			s_AmbientTargetDropdownCacheVersion = -1;
			s_AmbientTargetDropdown = Array.Empty<DropdownItem<string>>();
		}
	}
// Update discovered vehicle prefab keys and synchronize related config fields.
	internal static void SetDiscoveredVehiclePrefabs(ICollection<string> vehiclePrefabNames)
	{
		List<string> normalized = new List<string>();
		HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		if (vehiclePrefabNames != null)
		{
			foreach (string raw in vehiclePrefabNames)
			{
				string key = SirenReplacementConfig.NormalizeVehiclePrefabKey(raw);
				if (string.IsNullOrWhiteSpace(key) || !seen.Add(key))
				{
					continue;
				}

				normalized.Add(key);
			}
		}

		normalized.Sort(StringComparer.OrdinalIgnoreCase);
		bool listChanged = !SequenceEqualsIgnoreCase(s_DiscoveredVehiclePrefabs, normalized);
		if (listChanged)
		{
			s_DiscoveredVehiclePrefabs = normalized.ToArray();
			s_LastVehiclePrefabScanStatus = $"Detected {normalized.Count} emergency vehicle prefab(s) from loaded prefabs.";
		}

		bool configChanged = Config.SynchronizeVehiclePrefabSelections(normalized);
		if (configChanged)
		{
			SaveConfig();
			ConfigVersion++;
		}

		if (listChanged || configChanged)
		{
			OptionsVersion++;
			s_VehiclePrefabDropdownCacheVersion = -1;
			s_VehiclePrefabDropdown = Array.Empty<DropdownItem<string>>();
		}
	}

	// Load previously discovered emergency vehicle prefabs so options are populated in main menu.
	private static void LoadKnownVehiclePrefabsFromConfig()
	{
		List<string> known = Config.KnownEmergencyVehiclePrefabs ?? new List<string>();
		if (known.Count == 0)
		{
			s_DiscoveredVehiclePrefabs = Array.Empty<string>();
			s_LastVehiclePrefabScanStatus = "No stored emergency vehicle prefabs yet. Click Rescan Emergency Vehicle Prefabs in a loaded map/editor session.";
			s_VehiclePrefabDropdownCacheVersion = -1;
			s_VehiclePrefabDropdown = Array.Empty<DropdownItem<string>>();
			return;
		}

		List<string> normalized = new List<string>(known.Count);
		HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		for (int i = 0; i < known.Count; i++)
		{
			string key = SirenReplacementConfig.NormalizeVehiclePrefabKey(known[i]);
			if (string.IsNullOrWhiteSpace(key) || !seen.Add(key))
			{
				continue;
			}

			normalized.Add(key);
		}

		normalized.Sort(StringComparer.OrdinalIgnoreCase);
		s_DiscoveredVehiclePrefabs = normalized.ToArray();
		Config.SynchronizeVehiclePrefabSelections(normalized);
		s_LastVehiclePrefabScanStatus = $"Loaded {normalized.Count} known emergency vehicle prefab(s) from settings.";
		s_VehiclePrefabDropdownCacheVersion = -1;
		s_VehiclePrefabDropdown = Array.Empty<DropdownItem<string>>();
	}

		// Load previously discovered vehicle engine prefabs so options are populated in main menu.
	private static void LoadKnownVehicleEnginePrefabsFromConfig()
	{
		List<string> known = VehicleEngineConfig.KnownTargets ?? new List<string>();
		if (known.Count == 0)
		{
			s_DiscoveredVehicleEnginePrefabs = Array.Empty<string>();
			s_LastVehicleEnginePrefabScanStatus = "No stored vehicle engine prefabs yet. Click Rescan Vehicle Engine Prefabs in a loaded map/editor session.";
			s_VehicleEnginePrefabDropdownCacheVersion = -1;
			s_VehicleEnginePrefabDropdown = Array.Empty<DropdownItem<string>>();
			return;
		}

		List<string> normalized = new List<string>(known.Count);
		HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		for (int i = 0; i < known.Count; i++)
		{
			string key = AudioReplacementDomainConfig.NormalizeTargetKey(known[i]);
			if (string.IsNullOrWhiteSpace(key) || !seen.Add(key))
			{
				continue;
			}

			normalized.Add(key);
		}

		normalized.Sort(StringComparer.OrdinalIgnoreCase);
		s_DiscoveredVehicleEnginePrefabs = normalized.ToArray();
		VehicleEngineConfig.SynchronizeTargets(normalized);
		s_LastVehicleEnginePrefabScanStatus = $"Loaded {normalized.Count} known vehicle engine prefab(s) from settings.";
		s_VehicleEnginePrefabDropdownCacheVersion = -1;
		s_VehicleEnginePrefabDropdown = Array.Empty<DropdownItem<string>>();
	}

	// Load previously discovered ambient target prefabs so options are populated in main menu.
	private static void LoadKnownAmbientTargetsFromConfig()
	{
		List<string> known = AmbientConfig.KnownTargets ?? new List<string>();
		if (known.Count == 0)
		{
			s_DiscoveredAmbientTargets = Array.Empty<string>();
			s_LastAmbientTargetScanStatus = "No stored ambient targets yet. Click Rescan Ambient Targets in a loaded map/editor session.";
			s_AmbientTargetDropdownCacheVersion = -1;
			s_AmbientTargetDropdown = Array.Empty<DropdownItem<string>>();
			return;
		}

		List<string> normalized = new List<string>(known.Count);
		HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		for (int i = 0; i < known.Count; i++)
		{
			string key = AudioReplacementDomainConfig.NormalizeTargetKey(known[i]);
			if (string.IsNullOrWhiteSpace(key) || !seen.Add(key))
			{
				continue;
			}

			normalized.Add(key);
		}

		normalized.Sort(StringComparer.OrdinalIgnoreCase);
		s_DiscoveredAmbientTargets = normalized.ToArray();
		AmbientConfig.SynchronizeTargets(normalized);
		s_LastAmbientTargetScanStatus = $"Loaded {normalized.Count} known ambient target(s) from settings.";
		s_AmbientTargetDropdownCacheVersion = -1;
		s_AmbientTargetDropdown = Array.Empty<DropdownItem<string>>();
	}
// Set the selected vehicle prefab used by options to edit per-vehicle overrides.
	internal static void SetVehiclePrefabSelectionTargetFromOptions(string vehiclePrefabName)
	{
		Config.SetVehiclePrefabSelectionTarget(vehiclePrefabName);
	}

	// Read override value for currently selected vehicle prefab in options UI.
	internal static string GetSelectedVehiclePrefabSelectionForOptions()
	{
		string key = Config.VehiclePrefabSelectionTarget;
		if (string.IsNullOrWhiteSpace(key))
		{
			return SirenReplacementConfig.DefaultSelectionToken;
		}

		return Config.GetVehiclePrefabSelection(key);
	}

	// Set override value for currently selected vehicle prefab in options UI.
	internal static void SetSelectedVehiclePrefabSelectionFromOptions(string selection)
	{
		string key = Config.VehiclePrefabSelectionTarget;
		if (string.IsNullOrWhiteSpace(key))
		{
			return;
		}

		Config.SetVehiclePrefabSelection(key, selection);
	}

	// Read-only status text shown under per-vehicle override controls.
	internal static string GetSelectedVehicleOverrideStatusText()
	{
		if (s_DiscoveredVehiclePrefabs.Length == 0)
		{
			return "No emergency vehicle prefabs detected yet. Click Rescan Emergency Vehicle Prefabs in a loaded map/editor session.";
		}

		string key = Config.VehiclePrefabSelectionTarget;
		if (string.IsNullOrWhiteSpace(key))
		{
			return "Select a vehicle prefab to edit its siren override.";
		}

		string selection = Config.GetVehiclePrefabSelection(key);
		if (SirenReplacementConfig.IsDefaultSelection(selection))
		{
			return $"'{key}' uses vehicle type/region defaults.";
		}

		return $"'{key}' override: {FormatSirenDisplayName(selection)}";
	}

	// Button action: rescan custom siren files and refresh options state.
	internal static void RefreshCustomSirensFromOptions()
	{
		SyncCustomSirenCatalog(saveIfChanged: true, forceStatusRefresh: true);
	}

	// Button action: scan loaded prefabs for emergency vehicles and refresh per-vehicle override options.
	internal static void RefreshEmergencyVehiclePrefabsFromOptions()
	{
		if (!TryScanEmergencyVehiclePrefabs(out List<string> discovered, out string status))
		{
			s_LastVehiclePrefabScanStatus = status;
			OptionsVersion++;
			return;
		}

		SetDiscoveredVehiclePrefabs(discovered);
		s_LastVehiclePrefabScanStatus = discovered.Count > 0
			? $"{status}\nDetected: {discovered.Count} prefab(s)."
			: $"{status}\nNo emergency vehicle prefabs were found in the active world.";
		OptionsVersion++;
	}

	// Text block shown in vehicle section for emergency vehicle prefab scan status.
	internal static string GetVehiclePrefabScanStatusText()
	{
		return s_LastVehiclePrefabScanStatus;
	}

	// Button action: run consistency checks and write report into config.
	internal static void RunValidationFromOptions()
	{
		Config.Normalize();
		VehicleEngineConfig.Normalize(VehicleEngineCustomFolderName);
		AmbientConfig.Normalize(AmbientCustomFolderName);
		TransitAnnouncementConfig.Normalize(TransitAnnouncementCustomFolderName);
		NormalizeTransitAnnouncementTargets();
		SirenValidationResult result = SirenConfigValidator.Validate(
			Config,
			VehicleEngineConfig,
			AmbientConfig,
			TransitAnnouncementConfig,
			SettingsDirectory);
		Config.LastValidationUtcTicks = DateTime.UtcNow.Ticks;
		Config.LastValidationReport = result.ReportText;
		SaveConfig();
		ConfigVersion++;
		OptionsVersion++;
		Log.Info($"Validation finished. Errors={result.ErrorCount}, Warnings={result.WarningCount}.");
	}

	// Text block shown in diagnostics section for validation output.
	internal static string GetValidationStatusText()
	{
		if (Config.LastValidationUtcTicks <= 0 || string.IsNullOrWhiteSpace(Config.LastValidationReport))
		{
			return "No validation report yet. Click Run Siren Setup Validation.";
		}

		DateTime localTime = new DateTime(Config.LastValidationUtcTicks, DateTimeKind.Utc).ToLocalTime();
		return $"Last validation: {localTime:yyyy-MM-dd HH:mm:ss}\n{Config.LastValidationReport}";
	}

	// Text block shown in diagnostics section for catalog scan status.
	internal static string GetCatalogScanStatusText()
	{
		if (Config.LastCatalogScanUtcTicks <= 0)
		{
			return "No scan run yet. Click Rescan Custom Siren Files.";
		}

		DateTime localTime = new DateTime(Config.LastCatalogScanUtcTicks, DateTimeKind.Utc).ToLocalTime();
		StringBuilder builder = new StringBuilder();
		builder.Append("Last scan: ").Append(localTime.ToString("yyyy-MM-dd HH:mm:ss")).Append('\n');
		builder.Append("Files found: ").Append(Config.LastCatalogScanFileCount).Append('\n');
		builder.Append("Added: ").Append(Config.LastCatalogScanAddedCount)
			.Append(", Removed: ").Append(Config.LastCatalogScanRemovedCount).Append('\n');

		if (Config.LastCatalogScanChangedFiles.Count == 0)
		{
			builder.Append("Changed files: none");
			return builder.ToString();
		}

		builder.Append("Changed files:");
		int shown = Math.Min(Config.LastCatalogScanChangedFiles.Count, 12);
		for (int i = 0; i < shown; i++)
		{
			builder.Append('\n').Append(" - ").Append(Config.LastCatalogScanChangedFiles[i]);
		}

		if (shown < Config.LastCatalogScanChangedFiles.Count)
		{
			builder.Append('\n').Append(" - ...").Append(Config.LastCatalogScanChangedFiles.Count - shown).Append(" more");
		}

		return builder.ToString();
	}

	// Text block shown in profile section for preview command feedback.
	internal static string GetPreviewStatusText()
	{
		return s_LastPreviewStatus;
	}

	// Button action: play currently selected profile, or fall back to built-in default sample preview.
	internal static void PreviewSelectedProfileFromOptions()
	{
		string key = SirenPathUtils.NormalizeProfileKey(Config.EditProfileSelection ?? string.Empty);
		if (SirenReplacementConfig.IsDefaultSelection(key))
		{
			if (TryPlayDefaultPreviewClip(s_DefaultSirenPreviewClip, CustomProfileTemplate, "siren", out string defaultStatus))
			{
				s_LastPreviewStatus = defaultStatus;
				Log.Info(s_LastPreviewStatus);
			}
			else
			{
				s_LastPreviewStatus = defaultStatus;
				Log.Warn(s_LastPreviewStatus);
			}

			OptionsVersion++;
			return;
		}

		if (!Config.TryGetProfile(key, out SirenSfxProfile profile))
		{
			key = GetFirstAvailableProfileKey(Config.CustomSirenProfiles.Keys);
			if (string.IsNullOrWhiteSpace(key) || !Config.TryGetProfile(key, out profile))
			{
				if (TryPlayDefaultPreviewClip(s_DefaultSirenPreviewClip, CustomProfileTemplate, "siren", out string defaultStatus))
				{
					s_LastPreviewStatus = defaultStatus;
					Log.Info(s_LastPreviewStatus);
				}
				else
				{
					s_LastPreviewStatus = defaultStatus;
					Log.Warn(s_LastPreviewStatus);
				}

				OptionsVersion++;
				return;
			}

			Config.EditProfileSelection = key;
			if (!Config.TryGetProfile(Config.CopyFromProfileSelection, out _))
			{
				Config.CopyFromProfileSelection = key;
			}

			SaveConfig();
		}

		if (!TryResolveAudioProfilePath(DeveloperAudioDomain.Siren, Config.CustomSirensFolderName, key, out string path))
		{
			s_LastPreviewStatus = $"Cannot find file for '{key}'.";
			Log.Warn(s_LastPreviewStatus);
			OptionsVersion++;
			return;
		}

		string previewLabel = FormatSirenDisplayName(key);

		WaveClipLoader.AudioLoadStatus loadStatus = WaveClipLoader.LoadAudio(path, out AudioClip clip, out string error);
		if (loadStatus == WaveClipLoader.AudioLoadStatus.Pending)
		{
			s_LastPreviewStatus = $"Preview is loading for '{previewLabel}'. Click Preview again in a moment.";
			Log.Info(s_LastPreviewStatus);
			OptionsVersion++;
			return;
		}

		if (loadStatus != WaveClipLoader.AudioLoadStatus.Success)
		{
			s_LastPreviewStatus = $"Preview load failed for '{key}': {error}";
			Log.Warn(s_LastPreviewStatus);
			OptionsVersion++;
			return;
		}

		if (!TryPlayPreviewClip(clip, profile, out string sourceError))
		{
			s_LastPreviewStatus = $"Preview failed: {sourceError}";
			Log.Warn(s_LastPreviewStatus);
			OptionsVersion++;
			return;
		}

		s_LastPreviewStatus = $"Previewing '{previewLabel}'.";
		Log.Info($"Previewing custom siren profile: {key}");
		OptionsVersion++;
	}
	// Persist all config files to disk.
	internal static void SaveConfig()
	{
		string activeSet = GetActiveSoundSetId();

		string sirenSettingsPath = GetSoundSetSettingsPath(
			activeSet,
			SirenReplacementConfig.SettingsFileName,
			ensureDirectoryExists: true);
		SirenReplacementConfig.Save(sirenSettingsPath, Config, Log);

		string engineSettingsPath = GetSoundSetSettingsPath(
			activeSet,
			VehicleEngineSettingsFileName,
			ensureDirectoryExists: true);
		AudioReplacementDomainConfig.Save(engineSettingsPath, VehicleEngineConfig, Log);

		string ambientSettingsPath = GetSoundSetSettingsPath(
			activeSet,
			AmbientSettingsFileName,
			ensureDirectoryExists: true);
		AudioReplacementDomainConfig.Save(ambientSettingsPath, AmbientConfig, Log);

		string transitAnnouncementSettingsPath = GetSoundSetSettingsPath(
			activeSet,
			TransitAnnouncementSettingsFileName,
			ensureDirectoryExists: true);
		AudioReplacementDomainConfig.Save(transitAnnouncementSettingsPath, TransitAnnouncementConfig, Log);

		SaveCitySoundProfileRegistry();
	}

	// Register a localized title for the top-level options panel used by this mod.
	private static void RegisterOptionsPanelLocalization(SirenChangerSettings settings)
	{
		LocalizationManager? localizationManager = GameManager.instance?.localizationManager;
		if (localizationManager == null)
		{
			Log.Warn("Localization manager was unavailable while registering options panel title.");
			return;
		}

		Dictionary<string, string> localizationEntries = BuildOptionsLocalizationMap(settings);
		if (localizationEntries.Count == 0)
		{
			Log.Warn("No options localization entries were generated.");
			return;
		}

		HashSet<string> localeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		AddLocaleId(localeIds, localizationManager.activeLocaleId);
		AddLocaleId(localeIds, localizationManager.fallbackLocaleId);

		string[] supportedLocales = localizationManager.GetSupportedLocales();
		for (int i = 0; i < supportedLocales.Length; i++)
		{
			AddLocaleId(localeIds, supportedLocales[i]);
		}

		foreach (string localeId in localeIds)
		{
			if (s_OptionsPanelNameSources.ContainsKey(localeId))
			{
				continue;
			}

			IDictionarySource source = new MemorySource(new Dictionary<string, string>(localizationEntries, StringComparer.Ordinal));
			localizationManager.AddSource(localeId, source);
			s_OptionsPanelNameSources[localeId] = source;
		}
	}

	// Build localization entries for options panel title, tab names, and group names.
	private static Dictionary<string, string> BuildOptionsLocalizationMap(SirenChangerSettings settings)
	{
		Dictionary<string, string> entries = new Dictionary<string, string>(32, StringComparer.Ordinal)
		{
			[settings.GetSettingsLocaleID()] = kOptionsPanelDisplayName
		};

		AddOptionTabLocalization(entries, settings, SirenChangerSettings.kGeneralTab);
		AddOptionTabLocalization(entries, settings, SirenChangerSettings.kPublicTransportTab);
		AddOptionTabLocalization(entries, settings, SirenChangerSettings.kSirensTab);
		AddOptionTabLocalization(entries, settings, SirenChangerSettings.kVehiclesTab);
		AddOptionTabLocalization(entries, settings, SirenChangerSettings.kAmbientTab);
		AddOptionTabLocalization(entries, settings, SirenChangerSettings.kDeveloperTab);

		AddOptionGroupLocalization(entries, settings, SirenChangerSettings.kGeneralGroup);
		AddOptionGroupLocalization(entries, settings, SirenChangerSettings.kCitySoundSetGroup);
		AddOptionGroupLocalization(entries, settings, SirenChangerSettings.kTransitAnnouncementGroup);
		AddOptionGroupLocalization(entries, settings, SirenChangerSettings.kVehicleGroup);
		AddOptionGroupLocalization(entries, settings, SirenChangerSettings.kVehicleOverrideGroup);
		AddOptionGroupLocalization(entries, settings, SirenChangerSettings.kFallbackGroup);
		AddOptionGroupLocalization(entries, settings, SirenChangerSettings.kProfileGroup);
		AddOptionGroupLocalization(entries, settings, SirenChangerSettings.kDiagnosticsGroup);

		AddOptionGroupLocalization(entries, settings, SirenChangerSettings.kVehicleSetupGroup);
		AddOptionGroupLocalization(entries, settings, SirenChangerSettings.kVehicleOverrideTargetGroup);
		AddOptionGroupLocalization(entries, settings, SirenChangerSettings.kVehicleFallbackGroup);
		AddOptionGroupLocalization(entries, settings, SirenChangerSettings.kVehicleProfileGroup);

		AddOptionGroupLocalization(entries, settings, SirenChangerSettings.kAmbientSetupGroup);
		AddOptionGroupLocalization(entries, settings, SirenChangerSettings.kAmbientTargetGroup);
		AddOptionGroupLocalization(entries, settings, SirenChangerSettings.kAmbientFallbackGroup);
		AddOptionGroupLocalization(entries, settings, SirenChangerSettings.kAmbientProfileGroup);

		AddOptionGroupLocalization(entries, settings, SirenChangerSettings.kDeveloperSirenGroup);
		AddOptionGroupLocalization(entries, settings, SirenChangerSettings.kDeveloperEngineGroup);
		AddOptionGroupLocalization(entries, settings, SirenChangerSettings.kDeveloperAmbientGroup);
		AddOptionGroupLocalization(entries, settings, SirenChangerSettings.kDeveloperModuleGroup);

		AddOptionGroupLocalization(entries, settings, "Siren Scan Actions");
		AddOptionGroupLocalization(entries, settings, "City Sound Set Actions");
		AddOptionGroupLocalization(entries, settings, "Transit Announcement Actions");
		AddOptionGroupLocalization(entries, settings, "Engine Scan Actions");
		AddOptionGroupLocalization(entries, settings, "Ambient Scan Actions");
		AddOptionGroupLocalization(entries, settings, "Siren Include Actions");
		AddOptionGroupLocalization(entries, settings, "Engine Include Actions");
		AddOptionGroupLocalization(entries, settings, "Ambient Include Actions");
		AddOptionGroupLocalization(entries, settings, "Transit Include Actions");
		AddOptionGroupLocalization(entries, settings, "Module Selection Actions");

		return entries;
	}

	private static void AddOptionTabLocalization(IDictionary<string, string> entries, SirenChangerSettings settings, string tabName)
	{
		// Register tab display text under the options tab locale key.
		if (string.IsNullOrWhiteSpace(tabName))
		{
			return;
		}

		entries[settings.GetOptionTabLocaleID(tabName)] = tabName;
	}

	private static void AddOptionGroupLocalization(IDictionary<string, string> entries, SirenChangerSettings settings, string groupName)
	{
		// Register group display text under the options group locale key.
		if (string.IsNullOrWhiteSpace(groupName))
		{
			return;
		}

		entries[settings.GetOptionGroupLocaleID(groupName)] = groupName;
	}

	// Remove localization entries added during load to avoid duplicate sources on hot-reload.
	private static void UnregisterOptionsPanelLocalization()
	{
		if (s_OptionsPanelNameSources.Count == 0)
		{
			return;
		}

		LocalizationManager? localizationManager = GameManager.instance?.localizationManager;
		if (localizationManager != null)
		{
			foreach (KeyValuePair<string, IDictionarySource> pair in s_OptionsPanelNameSources)
			{
				localizationManager.RemoveSource(pair.Key, pair.Value);
			}
		}

		s_OptionsPanelNameSources.Clear();
	}

	// Add non-empty locale ids to the set while filtering null/whitespace values.
	private static void AddLocaleId(ISet<string> localeIds, string localeId)
	{
		if (!string.IsNullOrWhiteSpace(localeId))
		{
			localeIds.Add(localeId);
		}
	}

	// Invalidate dropdown caches and bump options version when labels/catalog visibility changed.
	private static void NotifyOptionsCatalogChanged()
	{
		OptionsVersion++;
		InvalidateDropdownCaches();
	}

	// Clear all dropdown caches so the options UI rebuilds from current runtime state.
	private static void InvalidateDropdownCaches()
	{
		s_DropdownCacheVersion = -1;
		s_VehicleDropdownWithDefault = Array.Empty<DropdownItem<string>>();
		s_ProfileDropdownWithoutDefault = Array.Empty<DropdownItem<string>>();
		s_VehiclePrefabDropdownCacheVersion = -1;
		s_VehiclePrefabDropdown = Array.Empty<DropdownItem<string>>();
		s_EngineDropdownCacheVersion = -1;
		s_EngineDropdownWithDefault = Array.Empty<DropdownItem<string>>();
		s_EngineDropdownWithoutDefault = Array.Empty<DropdownItem<string>>();
		s_AmbientDropdownCacheVersion = -1;
		s_AmbientDropdownWithDefault = Array.Empty<DropdownItem<string>>();
		s_AmbientDropdownWithoutDefault = Array.Empty<DropdownItem<string>>();
		s_VehicleEnginePrefabDropdownCacheVersion = -1;
		s_VehicleEnginePrefabDropdown = Array.Empty<DropdownItem<string>>();
		s_AmbientTargetDropdownCacheVersion = -1;
		s_AmbientTargetDropdown = Array.Empty<DropdownItem<string>>();
		s_TransitAnnouncementDropdownCacheVersion = -1;
		s_TransitAnnouncementDropdownWithDefault = Array.Empty<DropdownItem<string>>();
	}

		// Update scan telemetry fields for one generic domain config.
	private static bool UpdateDomainCatalogScanMetadata(AudioReplacementDomainConfig config, AudioDomainCatalogSyncResult result, bool forceTimestampRefresh)
	{
		List<string> changes = new List<string>(result.AddedKeys.Count + result.RemovedKeys.Count);
		for (int i = 0; i < result.AddedKeys.Count; i++)
		{
			changes.Add($"+ {result.AddedKeys[i]}");
		}

		for (int i = 0; i < result.RemovedKeys.Count; i++)
		{
			changes.Add($"- {result.RemovedKeys[i]}");
		}

		changes.Sort(StringComparer.OrdinalIgnoreCase);
		bool contentChanged =
			config.LastCatalogScanFileCount != result.FoundFileCount ||
			config.LastCatalogScanAddedCount != result.AddedKeys.Count ||
			config.LastCatalogScanRemovedCount != result.RemovedKeys.Count ||
			!ListEqualsIgnoreCase(config.LastCatalogScanChangedFiles, changes);
		if (!contentChanged && !forceTimestampRefresh)
		{
			return false;
		}

		long nowTicks = DateTime.UtcNow.Ticks;
		bool changed = false;
		changed |= config.LastCatalogScanUtcTicks != nowTicks;
		changed |= contentChanged;
		config.LastCatalogScanUtcTicks = nowTicks;
		config.LastCatalogScanFileCount = result.FoundFileCount;
		config.LastCatalogScanAddedCount = result.AddedKeys.Count;
		config.LastCatalogScanRemovedCount = result.RemovedKeys.Count;
		config.LastCatalogScanChangedFiles = changes;
		return changed;
	}
// Update scan telemetry fields while avoiding unnecessary churn.
	private static bool UpdateCatalogScanMetadata(SirenCatalogSyncResult result, bool forceTimestampRefresh)
	{
		List<string> changes = new List<string>(result.AddedKeys.Count + result.RemovedKeys.Count);
		for (int i = 0; i < result.AddedKeys.Count; i++)
		{
			changes.Add($"+ {result.AddedKeys[i]}");
		}

		for (int i = 0; i < result.RemovedKeys.Count; i++)
		{
			changes.Add($"- {result.RemovedKeys[i]}");
		}

		changes.Sort(StringComparer.OrdinalIgnoreCase);

		bool contentChanged =
			Config.LastCatalogScanFileCount != result.FoundFileCount ||
			Config.LastCatalogScanAddedCount != result.AddedKeys.Count ||
			Config.LastCatalogScanRemovedCount != result.RemovedKeys.Count ||
			!ListEqualsIgnoreCase(Config.LastCatalogScanChangedFiles, changes);
		if (!contentChanged && !forceTimestampRefresh)
		{
			return false;
		}

		long nowTicks = DateTime.UtcNow.Ticks;
		bool changed = false;
		changed |= Config.LastCatalogScanUtcTicks != nowTicks;
		changed |= contentChanged;

		Config.LastCatalogScanUtcTicks = nowTicks;
		Config.LastCatalogScanFileCount = result.FoundFileCount;
		Config.LastCatalogScanAddedCount = result.AddedKeys.Count;
		Config.LastCatalogScanRemovedCount = result.RemovedKeys.Count;
		Config.LastCatalogScanChangedFiles = changes;
		return changed;
	}

	// Rebuild cached dropdown item arrays when options version changes.
	private static void EnsureDropdownCacheCurrent()
	{
		if (s_DropdownCacheVersion == OptionsVersion &&
			s_VehicleDropdownWithDefault.Length > 0 &&
			s_ProfileDropdownWithoutDefault.Length > 0)
		{
			return;
		}

		List<string> keys = Config.CustomSirenProfiles.Keys
			.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
			.ToList();

		List<DropdownItem<string>> withDefault = new List<DropdownItem<string>>(keys.Count + 1)
		{
			new DropdownItem<string>
			{
				value = SirenReplacementConfig.DefaultSelectionToken,
				displayName = "Default"
			}
		};

		List<DropdownItem<string>> withoutDefault = new List<DropdownItem<string>>(keys.Count);
		for (int i = 0; i < keys.Count; i++)
		{
			string key = keys[i];
			DropdownItem<string> item = new DropdownItem<string>
			{
				value = key,
				displayName = FormatSirenDisplayName(key)
			};

			withDefault.Add(item);
			withoutDefault.Add(item);
		}

		if (withoutDefault.Count == 0)
		{
			withoutDefault.Add(new DropdownItem<string>
			{
				value = string.Empty,
				displayName = "No custom sirens found",
				disabled = true
			});
		}

		s_VehicleDropdownWithDefault = withDefault.ToArray();
		s_ProfileDropdownWithoutDefault = withoutDefault.ToArray();
		s_DropdownCacheVersion = OptionsVersion;
	}

	// Rebuild discovered-vehicle dropdown cache when options version changes.
	private static void EnsureVehiclePrefabDropdownCurrent()
	{
		if (s_VehiclePrefabDropdownCacheVersion == OptionsVersion && s_VehiclePrefabDropdown.Length > 0)
		{
			return;
		}

		if (s_DiscoveredVehiclePrefabs.Length == 0)
		{
			s_VehiclePrefabDropdown = new[]
			{
				new DropdownItem<string>
				{
					value = string.Empty,
					displayName = "No emergency vehicle prefabs detected",
					disabled = true
				}
			};
			s_VehiclePrefabDropdownCacheVersion = OptionsVersion;
			return;
		}

		List<DropdownItem<string>> options = new List<DropdownItem<string>>(s_DiscoveredVehiclePrefabs.Length);
		for (int i = 0; i < s_DiscoveredVehiclePrefabs.Length; i++)
		{
			string prefabName = s_DiscoveredVehiclePrefabs[i];
			options.Add(new DropdownItem<string>
			{
				value = prefabName,
				displayName = prefabName
			});
		}

		s_VehiclePrefabDropdown = options.ToArray();
		s_VehiclePrefabDropdownCacheVersion = OptionsVersion;
	}

	// Scan all currently loaded ECS worlds for emergency vehicle prefabs so options can work in editor/main-menu contexts when data is available.
	private static bool TryScanEmergencyVehiclePrefabs(out List<string> discovered, out string status)
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

			if (TryScanEmergencyVehiclePrefabsFromWorld(world, seen, discovered, out int worldPrefabCount))
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

	// Scan one world for prefabs that reference known emergency siren effect prefabs.
	private static bool TryScanEmergencyVehiclePrefabsFromWorld(
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
						if (!TryGetPrefabSafe(prefabSystem, prefabEntities[i], out PrefabBase prefab) ||
							!IsEmergencyVehiclePrefab(prefab))
						{
							continue;
						}

						string prefabName = SirenReplacementConfig.NormalizeVehiclePrefabKey(prefab.name ?? string.Empty);
						if (string.IsNullOrWhiteSpace(prefabName) || !seen.Add(prefabName))
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
			Log.Warn($"Emergency vehicle prefab scan skipped world '{world.Name}': {ex.Message}");
			return false;
		}
	}

	// Resolve PrefabData entity handles defensively; invalid indices can appear transiently during world churn.
	private static bool TryGetPrefabSafe(PrefabSystem prefabSystem, Entity prefabEntity, out PrefabBase prefab)
	{
		prefab = null!;
		try
		{
			prefab = prefabSystem.GetPrefab<PrefabBase>(prefabEntity);
			return prefab != null;
		}
		catch (ArgumentOutOfRangeException)
		{
			return false;
		}
	}

	// Identify emergency vehicle prefabs by siren effect links that map to a resolvable emergency vehicle type.
	private static bool IsEmergencyVehiclePrefab(PrefabBase prefab)
	{
		if (prefab == null)
		{
			return false;
		}

		string prefabName = prefab.name ?? string.Empty;
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

			string effectName = effect.m_Effect.name ?? string.Empty;
			if (IsTargetSirenEffectName(effectName))
			{
				return true;
			}

			// Ignore engine-tagged effects when scanning siren-target vehicle prefabs.
			if (effect.m_Effect.GetComponent<VehicleSFX>() != null)
			{
				continue;
			}

			if (!IsLikelySirenEffectName(effectName))
			{
				continue;
			}

			if (TryInferEmergencyVehicleType(prefab, prefabName, out _) ||
				TryInferEmergencyVehicleType(prefab, effectName, out _))
			{
				return true;
			}
		}

		return false;
	}

	// Infer emergency vehicle type from components, then from prefab-name hints for modded variants.
	private static bool TryInferEmergencyVehicleType(
		PrefabBase prefab,
		string prefabName,
		out EmergencySirenVehicleType vehicleType)
	{
		if (prefab.GetComponent<Game.Prefabs.PoliceCar>() != null ||
			ContainsTextToken(prefabName, "police") ||
			ContainsTextToken(prefabName, "patrol") ||
			ContainsTextToken(prefabName, "agent") ||
			ContainsTextToken(prefabName, "administration") ||
			ContainsTextToken(prefabName, "admin") ||
			ContainsTextToken(prefabName, "sheriff"))
		{
			vehicleType = EmergencySirenVehicleType.Police;
			return true;
		}

		if (prefab.GetComponent<Game.Prefabs.FireEngine>() != null ||
			ContainsTextToken(prefabName, "fire") ||
			ContainsTextToken(prefabName, "engine") ||
			ContainsTextToken(prefabName, "rescue"))
		{
			vehicleType = EmergencySirenVehicleType.Fire;
			return true;
		}

		if (prefab.GetComponent<Game.Prefabs.Ambulance>() != null ||
			ContainsTextToken(prefabName, "ambulance") ||
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

	// Known emergency siren target prefabs used by the replacement system.
	private static bool IsTargetSirenEffectName(string prefabName)
	{
		return string.Equals(prefabName, "PoliceCarSirenNA", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(prefabName, "PoliceCarSirenEU", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(prefabName, "FireTruckSirenNA", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(prefabName, "FireTruckSirenEU", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(prefabName, "AmbulanceSirenNA", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(prefabName, "AmbulanceSirenEU", StringComparison.OrdinalIgnoreCase);
	}

	// Conservative siren-effect name match for non-standard vehicle packs.
	private static bool IsLikelySirenEffectName(string prefabName)
	{
		if (string.IsNullOrWhiteSpace(prefabName))
		{
			return false;
		}

		return IsTargetSirenEffectName(prefabName) ||
			ContainsTextToken(prefabName, "siren") ||
			ContainsTextToken(prefabName, "alarm") ||
			ContainsTextToken(prefabName, "emergency") ||
			ContainsTextToken(prefabName, "warning");
	}

	// Case-insensitive contains helper for simple token matching.
	private static bool ContainsTextToken(string value, string token)
	{
		if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(token))
		{
			return false;
		}

		return value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
	}

	// Format a readable dropdown label from a profile key path.
	private static string FormatSirenDisplayName(string key)
	{
		if (TryGetAudioModuleDisplayName(key, out string moduleDisplayName))
		{
			return moduleDisplayName;
		}

		string normalized = (key ?? string.Empty).Replace('\\', '/');
		string baseName = Path.GetFileNameWithoutExtension(normalized);
		if (string.IsNullOrWhiteSpace(baseName))
		{
			return normalized;
		}

		string? directory = Path.GetDirectoryName(normalized);
		if (string.IsNullOrWhiteSpace(directory))
		{
			return baseName;
		}

		string compactDirectory = directory.Replace('\\', '/');
		return $"{baseName} [{compactDirectory}]";
	}

	// Return the first available normalized profile key in case-insensitive alphabetical order.
	private static string GetFirstAvailableProfileKey(IEnumerable<string> keys)
	{
		if (keys == null)
		{
			return string.Empty;
		}

		string first = string.Empty;
		foreach (string raw in keys)
		{
			string key = SirenPathUtils.NormalizeProfileKey(raw ?? string.Empty);
			if (string.IsNullOrWhiteSpace(key))
			{
				continue;
			}

			if (string.IsNullOrWhiteSpace(first) || StringComparer.OrdinalIgnoreCase.Compare(key, first) < 0)
			{
				first = key;
			}
		}

		return first;
	}

	// Play one built-in sample clip with the provided template profile.
	private static bool TryPlayDefaultPreviewClip(
		AudioClip? clip,
		SirenSfxProfile templateProfile,
		string domainLabel,
		out string status)
	{
		if (clip == null)
		{
			status = $"No built-in {domainLabel} sound is available to preview yet. Load into a map or editor, then run the related rescan action.";
			return false;
		}

		if (!TryPlayPreviewClip(clip, templateProfile, out string error))
		{
			status = $"Preview failed: {error}";
			return false;
		}

		string clipName = string.IsNullOrWhiteSpace(clip.name) ? "Unknown" : clip.name;
		status = $"Previewing built-in {domainLabel} sound '{clipName}'.";
		return true;
	}

	// Apply one clip/profile pair to the shared preview source and play once.
	private static bool TryPlayPreviewClip(AudioClip clip, SirenSfxProfile profile, out string error)
	{
		error = string.Empty;
		if (clip == null)
		{
			error = "Audio clip is unavailable.";
			return false;
		}

		if (!EnsurePreviewAudioSource(out AudioSource source, out string sourceError))
		{
			error = sourceError;
			return false;
		}

		SirenSfxProfile clampedProfile = profile.ClampCopy();
		source.Stop();
		source.clip = clip;
		source.volume = clampedProfile.Volume;
		source.pitch = clampedProfile.Pitch;
		source.spatialBlend = clampedProfile.SpatialBlend;
		source.dopplerLevel = clampedProfile.Doppler;
		source.spread = clampedProfile.Spread;
		source.minDistance = clampedProfile.MinDistance;
		source.maxDistance = clampedProfile.MaxDistance;
		source.rolloffMode = clampedProfile.RolloffMode;
		source.loop = false;
		PositionPreviewSourceAtListener(source);
		source.Play();
		s_PreviewAutoStopper?.Arm(source, kPreviewTimeoutSeconds);
		return true;
	}
	// Move preview source onto the active listener to avoid menu/editor attenuation silence.
	private static void PositionPreviewSourceAtListener(AudioSource source)
	{
		if (source == null)
		{
			return;
		}

		AudioListener listener = UnityEngine.Object.FindObjectOfType<AudioListener>();
		if (listener == null)
		{
			return;
		}

		Transform listenerTransform = listener.transform;
		source.transform.position = listenerTransform.position;
		source.transform.rotation = listenerTransform.rotation;
	}
	// Lazily create a persistent preview audio source.
	private static bool EnsurePreviewAudioSource(out AudioSource source, out string error)
	{
		error = string.Empty;
		source = null!;

		try
		{
			if (s_PreviewAudioObject == null)
			{
				s_PreviewAudioObject = new GameObject("SirenChangerPreviewAudio");
				UnityEngine.Object.DontDestroyOnLoad(s_PreviewAudioObject);
			}

			if (s_PreviewAudioSource == null)
			{
				s_PreviewAudioSource = s_PreviewAudioObject.GetComponent<AudioSource>();
				if (s_PreviewAudioSource == null)
				{
					s_PreviewAudioSource = s_PreviewAudioObject.AddComponent<AudioSource>();
				}

				s_PreviewAudioSource.playOnAwake = false;
				s_PreviewAudioSource.ignoreListenerPause = true;
			}

			s_PreviewAudioSource.playOnAwake = false;
			s_PreviewAudioSource.ignoreListenerPause = true;

			if (s_PreviewAutoStopper == null)
			{
				s_PreviewAutoStopper = s_PreviewAudioObject.GetComponent<PreviewAutoStopper>();
				if (s_PreviewAutoStopper == null)
				{
					s_PreviewAutoStopper = s_PreviewAudioObject.AddComponent<PreviewAutoStopper>();
				}
			}

			source = s_PreviewAudioSource;
			return true;
		}
		catch (Exception ex)
		{
			error = ex.Message;
			return false;
		}
	}

	// Clean up preview source/gameobject on dispose.
	private static void ReleasePreviewAudio()
	{
		if (s_PreviewAudioSource != null)
		{
			s_PreviewAudioSource.Stop();
			s_PreviewAudioSource = null;
		}

		if (s_PreviewAudioObject != null)
		{
			UnityEngine.Object.Destroy(s_PreviewAudioObject);
			s_PreviewAudioObject = null;
		}

		s_PreviewAutoStopper = null;
	}

	// Auto-stop helper for preview playback so long loops do not run indefinitely.
	private sealed class PreviewAutoStopper : MonoBehaviour
	{
		private AudioSource? m_Source;

		private float m_StopAtUnscaledTime = -1f;

		public void Arm(AudioSource source, float timeoutSeconds)
		{
			// Use unscaled time so pauses/time-scale changes do not stall auto-stop behavior.
			m_Source = source;
			m_StopAtUnscaledTime = timeoutSeconds <= 0f
				? -1f
				: Time.unscaledTime + timeoutSeconds;
			enabled = m_Source != null && m_StopAtUnscaledTime > 0f;
		}

		private void Update()
		{
			// Stop playback once timeout is reached or source has naturally stopped.
			if (m_Source == null)
			{
				enabled = false;
				return;
			}

			if (!m_Source.isPlaying)
			{
				enabled = false;
				return;
			}

			if (m_StopAtUnscaledTime > 0f && Time.unscaledTime >= m_StopAtUnscaledTime)
			{
				m_Source.Stop();
				enabled = false;
			}
		}
	}
	// Case-insensitive list equality helper for scan metadata updates.
	private static bool ListEqualsIgnoreCase(List<string> left, List<string> right)
	{
		if (left.Count != right.Count)
		{
			return false;
		}

		for (int i = 0; i < left.Count; i++)
		{
			if (!string.Equals(left[i], right[i], StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}
		}

		return true;
	}

	// Case-insensitive sequence equality helper for list/array comparisons.
	private static bool SequenceEqualsIgnoreCase(IReadOnlyList<string> left, IReadOnlyList<string> right)
	{
		if (ReferenceEquals(left, right))
		{
			return true;
		}

		if (left.Count != right.Count)
		{
			return false;
		}

		for (int i = 0; i < left.Count; i++)
		{
			if (!string.Equals(left[i], right[i], StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}
		}

		return true;
	}
}





























