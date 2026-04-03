using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.UI.Widgets;

namespace SirenChanger;

// City-bound sound-set registry management and active set switching.
public sealed partial class SirenChangerMod
{
	// Persistent city->sound-set registry and set metadata.
	private static CitySoundProfileRegistry s_CitySoundProfileRegistry = CitySoundProfileRegistry.CreateDefault();

	// Last detected loaded-city identity (GUID is the primary binding key).
	private static string s_CurrentCitySaveAssetGuid = string.Empty;

	// Human-readable city label shown in status text and binding lists.
	private static string s_CurrentCityDisplayName = string.Empty;

	// Session GUID read from loaded save metadata, used only for safe binding migration.
	private static string s_CurrentCitySessionGuid = string.Empty;

	// Last city sound-set status message shown in options UI.
	private static string s_CitySoundSetStatus = "No city sound-set decision has been made yet.";

	// Current selection in the saved-binding management dropdown.
	private static string s_SelectedCityBindingGuidForOptions = string.Empty;

	// Current options dropdown selection when it points to a module-provided sound-set profile key.
	private static string s_SelectedModuleSoundSetProfileKeyForOptions = string.Empty;

	// Active runtime sound-set selection identifier (local set id or module profile key).
	private static string s_ActiveSoundSetSelectionId = CitySoundProfileRegistry.DefaultSetId;

	// Initialize registry-backed sound-set state when the mod loads.
	private static void InitializeCitySoundProfiles()
	{
		string registryPath = CitySoundProfileRegistry.GetRegistryPath(SettingsDirectory);
		s_CitySoundProfileRegistry = CitySoundProfileRegistry.LoadOrCreate(registryPath, Log);
		string initialSetId = EnsureSoundSetExists(
			s_CitySoundProfileRegistry.ActiveSetId,
			CitySoundProfileRegistry.DefaultSetDisplayName);
		LoadSoundSetConfig(initialSetId);
		s_SelectedCityBindingGuidForOptions = GetFirstBindingGuidOrEmpty();
		s_SelectedModuleSoundSetProfileKeyForOptions = string.Empty;
		SaveCitySoundProfileRegistry();
		s_CitySoundSetStatus = $"Active sound set: {GetSoundSetDisplayName(initialSetId)}.";
	}

	// Persist city sound-set registry to disk.
	private static void SaveCitySoundProfileRegistry()
	{
		string registryPath = CitySoundProfileRegistry.GetRegistryPath(SettingsDirectory);
		CitySoundProfileRegistry.Save(registryPath, s_CitySoundProfileRegistry, Log);
	}

	// Resolve one settings file path within the selected sound-set directory.
	private static string GetSoundSetSettingsPath(string setId, string fileName, bool ensureDirectoryExists)
	{
		return CitySoundProfileRegistry.GetSetSettingsPath(
			SettingsDirectory,
			setId,
			fileName,
			ensureDirectoryExists);
	}

	// True when one selection key resolves to an available module-provided sound-set profile.
	private static bool TryNormalizeModuleSoundSetProfileKey(string selectionId, out string profileKey)
	{
		profileKey = SirenPathUtils.NormalizeProfileKey(selectionId ?? string.Empty);
		if (string.IsNullOrWhiteSpace(profileKey))
		{
			return false;
		}

		return TryGetAudioModuleSoundSetProfileDisplayName(profileKey, out _);
	}

	// True when one selection key points to a module-provided sound-set profile.
	private static bool IsModuleSoundSetProfileSelection(string selectionId)
	{
		return TryNormalizeModuleSoundSetProfileKey(selectionId, out _);
	}

	// Resolve selected options value, preferring module-profile selection when present.
	private static string GetSelectedCitySoundSetReferenceForOptions()
	{
		if (TryNormalizeModuleSoundSetProfileKey(s_SelectedModuleSoundSetProfileKeyForOptions, out string moduleProfileKey))
		{
			s_SelectedModuleSoundSetProfileKeyForOptions = moduleProfileKey;
			return moduleProfileKey;
		}

		s_SelectedModuleSoundSetProfileKeyForOptions = string.Empty;
		return s_CitySoundProfileRegistry.SelectedSetId;
	}

	// Resolve active runtime selection id (local set id or module profile key).
	private static string GetActiveSoundSetReferenceId()
	{
		if (TryNormalizeModuleSoundSetProfileKey(s_ActiveSoundSetSelectionId, out string moduleProfileKey))
		{
			s_ActiveSoundSetSelectionId = moduleProfileKey;
			return moduleProfileKey;
		}

		s_ActiveSoundSetSelectionId = EnsureSoundSetExists(s_CitySoundProfileRegistry.ActiveSetId, CitySoundProfileRegistry.DefaultSetDisplayName);
		return s_ActiveSoundSetSelectionId;
	}

	// Resolve settings-file read path from module snapshot or local set path.
	private static bool TryResolveSoundSetSettingsReadPath(string setOrProfileId, string fileName, out string filePath)
	{
		filePath = string.Empty;
		string normalizedFileName = Path.GetFileName((fileName ?? string.Empty).Trim());
		if (string.IsNullOrWhiteSpace(normalizedFileName))
		{
			return false;
		}

		if (TryNormalizeModuleSoundSetProfileKey(setOrProfileId, out string moduleProfileKey))
		{
			return TryGetAudioModuleSoundSetProfileSettingsFilePath(moduleProfileKey, normalizedFileName, out filePath);
		}

		string setId = EnsureSoundSetExists(setOrProfileId, setOrProfileId);
		string candidatePath = GetSoundSetSettingsPath(setId, normalizedFileName, ensureDirectoryExists: false);
		if (!File.Exists(candidatePath))
		{
			return false;
		}

		filePath = candidatePath;
		return true;
	}

	// Return the first valid bound city GUID so binding dropdowns always have a stable selection.
	private static string GetFirstBindingGuidOrEmpty()
	{
		for (int i = 0; i < s_CitySoundProfileRegistry.Bindings.Count; i++)
		{
			string guid = CitySoundProfileRegistry.NormalizeGuidKey(s_CitySoundProfileRegistry.Bindings[i].SaveAssetGuid);
			if (!string.IsNullOrWhiteSpace(guid))
			{
				return guid;
			}
		}

		return string.Empty;
	}

	// Resolve one persisted city binding by GUID.
	private static bool TryGetBindingByGuid(string saveAssetGuid, out CitySoundProfileBinding binding)
	{
		binding = null!;
		string normalizedGuid = CitySoundProfileRegistry.NormalizeGuidKey(saveAssetGuid);
		if (string.IsNullOrWhiteSpace(normalizedGuid))
		{
			return false;
		}

		for (int i = 0; i < s_CitySoundProfileRegistry.Bindings.Count; i++)
		{
			CitySoundProfileBinding candidate = s_CitySoundProfileRegistry.Bindings[i];
			if (!string.Equals(candidate.SaveAssetGuid, normalizedGuid, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			binding = candidate;
			return true;
		}

		return false;
	}

	// Normalize a candidate binding key and drop it when the binding no longer exists.
	private static string NormalizeExistingBindingGuid(string saveAssetGuid)
	{
		string normalizedGuid = CitySoundProfileRegistry.NormalizeGuidKey(saveAssetGuid);
		if (string.IsNullOrWhiteSpace(normalizedGuid))
		{
			return string.Empty;
		}

		return TryGetBindingByGuid(normalizedGuid, out _)
			? normalizedGuid
			: string.Empty;
	}

	// Prefer a stored city name for UI labels and fall back to a neutral placeholder.
	private static string GetBindingCityDisplayName(CitySoundProfileBinding binding)
	{
		if (binding == null)
		{
			return "Unknown City";
		}

		string displayName = (binding.LastSeenDisplayName ?? string.Empty).Trim();
		if (!string.IsNullOrWhiteSpace(displayName))
		{
			return displayName;
		}

		return "Unknown City";
	}

	// Ensure a set exists and return a valid set ID with Default fallback.
	private static string EnsureSoundSetExists(string setId, string displayName)
	{
		string normalized = s_CitySoundProfileRegistry.EnsureSet(setId, displayName);
		return string.IsNullOrWhiteSpace(normalized)
			? CitySoundProfileRegistry.DefaultSetId
			: normalized;
	}

	// Load siren/engine/ambient/transit-announcement configs for one set and update active-selection metadata.
	private static void LoadSoundSetConfig(string setId)
	{
		if (TryNormalizeModuleSoundSetProfileKey(setId, out string moduleProfileKey))
		{
			SirenReplacementConfig moduleSirenConfig = SirenReplacementConfig.CreateDefault();
			if (TryResolveSoundSetSettingsReadPath(moduleProfileKey, SirenReplacementConfig.SettingsFileName, out string moduleSirenSettingsPath))
			{
				moduleSirenConfig = SirenReplacementConfig.LoadOrCreateFromPath(moduleSirenSettingsPath, Log);
			}

			AudioReplacementDomainConfig moduleVehicleEngineConfig = AudioReplacementDomainConfig.CreateDefault(VehicleEngineCustomFolderName);
			if (TryResolveSoundSetSettingsReadPath(moduleProfileKey, VehicleEngineSettingsFileName, out string moduleEngineSettingsPath))
			{
				moduleVehicleEngineConfig = AudioReplacementDomainConfig.LoadOrCreate(
					moduleEngineSettingsPath,
					VehicleEngineCustomFolderName,
					Log);
			}

			AudioReplacementDomainConfig moduleAmbientConfig = AudioReplacementDomainConfig.CreateDefault(AmbientCustomFolderName);
			if (TryResolveSoundSetSettingsReadPath(moduleProfileKey, AmbientSettingsFileName, out string moduleAmbientSettingsPath))
			{
				moduleAmbientConfig = AudioReplacementDomainConfig.LoadOrCreate(
					moduleAmbientSettingsPath,
					AmbientCustomFolderName,
					Log);
			}

			AudioReplacementDomainConfig moduleTransitConfig = AudioReplacementDomainConfig.CreateDefault(TransitAnnouncementCustomFolderName);
			if (TryResolveSoundSetSettingsReadPath(moduleProfileKey, TransitAnnouncementSettingsFileName, out string moduleTransitSettingsPath))
			{
				moduleTransitConfig = AudioReplacementDomainConfig.LoadOrCreate(
					moduleTransitSettingsPath,
					TransitAnnouncementCustomFolderName,
					Log);
			}

			Config = moduleSirenConfig;
			VehicleEngineConfig = moduleVehicleEngineConfig;
			AmbientConfig = moduleAmbientConfig;
			TransitAnnouncementConfig = moduleTransitConfig;
			Config.Normalize();
			VehicleEngineConfig.Normalize(VehicleEngineCustomFolderName);
			AmbientConfig.Normalize(AmbientCustomFolderName);
			TransitAnnouncementConfig.Normalize(TransitAnnouncementCustomFolderName);
			NormalizeTransitAnnouncementTargets();
			NormalizeTransitAnnouncementSpeechSettings();

			// Module profiles are runtime-only and are never persisted as active/bound registry sets.
			s_ActiveSoundSetSelectionId = moduleProfileKey;
			s_CitySoundProfileRegistry.ActiveSetId = EnsureSoundSetExists(
				s_CitySoundProfileRegistry.ActiveSetId,
				CitySoundProfileRegistry.DefaultSetDisplayName);
			s_CitySoundProfileRegistry.SelectedSetId = s_CitySoundProfileRegistry.ContainsSet(s_CitySoundProfileRegistry.SelectedSetId)
				? CitySoundProfileRegistry.NormalizeSetId(s_CitySoundProfileRegistry.SelectedSetId)
				: s_CitySoundProfileRegistry.ActiveSetId;
			return;
		}

		string normalizedSet = EnsureSoundSetExists(setId, setId);
		string sirenSettingsPath = GetSoundSetSettingsPath(
			normalizedSet,
			SirenReplacementConfig.SettingsFileName,
			ensureDirectoryExists: true);
		Config = SirenReplacementConfig.LoadOrCreateFromPath(sirenSettingsPath, Log);

		string engineSettingsPath = GetSoundSetSettingsPath(
			normalizedSet,
			VehicleEngineSettingsFileName,
			ensureDirectoryExists: true);
		VehicleEngineConfig = AudioReplacementDomainConfig.LoadOrCreate(
			engineSettingsPath,
			VehicleEngineCustomFolderName,
			Log);

		string ambientSettingsPath = GetSoundSetSettingsPath(
			normalizedSet,
			AmbientSettingsFileName,
			ensureDirectoryExists: true);
		AmbientConfig = AudioReplacementDomainConfig.LoadOrCreate(
			ambientSettingsPath,
			AmbientCustomFolderName,
			Log);

		string transitAnnouncementSettingsPath = GetSoundSetSettingsPath(
			normalizedSet,
			TransitAnnouncementSettingsFileName,
			ensureDirectoryExists: true);
		TransitAnnouncementConfig = AudioReplacementDomainConfig.LoadOrCreate(
			transitAnnouncementSettingsPath,
			TransitAnnouncementCustomFolderName,
			Log);

		Config.Normalize();
		VehicleEngineConfig.Normalize(VehicleEngineCustomFolderName);
		AmbientConfig.Normalize(AmbientCustomFolderName);
		TransitAnnouncementConfig.Normalize(TransitAnnouncementCustomFolderName);
		NormalizeTransitAnnouncementTargets();
		NormalizeTransitAnnouncementSpeechSettings();

		s_CitySoundProfileRegistry.ActiveSetId = normalizedSet;
		s_ActiveSoundSetSelectionId = normalizedSet;
		s_CitySoundProfileRegistry.SelectedSetId = s_CitySoundProfileRegistry.ContainsSet(s_CitySoundProfileRegistry.SelectedSetId)
			? CitySoundProfileRegistry.NormalizeSetId(s_CitySoundProfileRegistry.SelectedSetId)
			: normalizedSet;
		if (s_CitySoundProfileRegistry.Sets.TryGetValue(normalizedSet, out CitySoundProfileSet set))
		{
			set.LastUsedUtcTicks = DateTime.UtcNow.Ticks;
		}
	}

	// Switch runtime state to one set and refresh catalogs/dropdowns as needed.
	private static bool ActivateSoundSetInternal(string setId, string reason, bool forceReload)
	{
		string normalizedSet = TryNormalizeModuleSoundSetProfileKey(setId, out string moduleProfileKey)
			? moduleProfileKey
			: EnsureSoundSetExists(setId, setId);
		if (!forceReload &&
			string.Equals(GetActiveSoundSetReferenceId(), normalizedSet, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		LoadSoundSetConfig(normalizedSet);
		LoadKnownVehiclePrefabsFromConfig();
		LoadKnownVehicleEnginePrefabsFromConfig();
		LoadKnownAmbientTargetsFromConfig();

		SyncCustomSirenCatalog(saveIfChanged: true);
		SyncCustomVehicleEngineCatalog(saveIfChanged: true);
		SyncCustomAmbientCatalog(saveIfChanged: true);
		SyncCustomTransitAnnouncementCatalog(saveIfChanged: true);

		ConfigVersion++;
		NotifyOptionsCatalogChanged();
		SaveCitySoundProfileRegistry();

		s_CitySoundSetStatus =
			$"Switched to sound set '{GetSoundSetDisplayName(normalizedSet)}' ({normalizedSet}) for {reason}.";
		Log.Info(s_CitySoundSetStatus);
		return true;
	}

	// Resolve display name for one set ID with safe fallback text.
	private static string GetSoundSetDisplayName(string setId)
	{
		if (TryNormalizeModuleSoundSetProfileKey(setId, out string moduleProfileKey) &&
			TryGetAudioModuleSoundSetProfileDisplayName(moduleProfileKey, out string moduleDisplayName))
		{
			return moduleDisplayName;
		}

		string normalized = CitySoundProfileRegistry.NormalizeSetId(setId);
		if (s_CitySoundProfileRegistry.TryGetSetDisplayName(normalized, out string displayName) &&
			!string.IsNullOrWhiteSpace(displayName))
		{
			return displayName;
		}

		return string.Equals(normalized, CitySoundProfileRegistry.DefaultSetId, StringComparison.OrdinalIgnoreCase)
			? CitySoundProfileRegistry.DefaultSetDisplayName
			: normalized;
	}

	// Build a readable label for the currently detected city identity.
	private static string GetCurrentCityLabel()
	{
		if (!HasCurrentCityIdentity())
		{
			return "no loaded city";
		}

		if (!string.IsNullOrWhiteSpace(s_CurrentCityDisplayName))
		{
			return $"'{s_CurrentCityDisplayName}'";
		}

		if (!string.IsNullOrWhiteSpace(s_CurrentCitySaveAssetGuid))
		{
			return $"save GUID {s_CurrentCitySaveAssetGuid}";
		}

		return "no loaded city";
	}

	// Apply the bound set for the currently detected city, if one is available.
	private static void ApplyBoundSetForCurrentCity(string reason)
	{
		if (!HasCurrentCityIdentity())
		{
			s_CitySoundSetStatus = $"No city identity is available, keeping active sound set '{GetSoundSetDisplayName(GetActiveSoundSetReferenceId())}'.";
			OptionsVersion++;
			return;
		}

		string targetSetId = s_CitySoundProfileRegistry.ResolveBoundSetId(s_CurrentCitySaveAssetGuid);
		bool hadDirectBinding = s_CitySoundProfileRegistry.HasBindingForCity(s_CurrentCitySaveAssetGuid);
		bool migratedBinding = false;
		if (!hadDirectBinding && TryAutoBindCurrentCityBySessionGuid(out string migratedSetId))
		{
			targetSetId = migratedSetId;
			migratedBinding = true;
		}

		string applyReason = migratedBinding
			? $"{reason}; session-guid binding migration"
			: reason;
		bool switched = ActivateSoundSetInternal(targetSetId, applyReason, forceReload: false);
		if (!switched)
		{
			if (migratedBinding)
			{
				s_CitySoundSetStatus =
					$"City {GetCurrentCityLabel()} binding migrated to this save GUID and uses sound set '{GetSoundSetDisplayName(targetSetId)}'.";
			}
			else
			{
				s_CitySoundSetStatus =
					$"City {GetCurrentCityLabel()} uses sound set '{GetSoundSetDisplayName(targetSetId)}'.";
			}

			OptionsVersion++;
		}
	}

	// Resolve one session GUID to exactly one bound set; ambiguous session matches are rejected.
	private static bool TryGetUniqueSetForSessionGuid(string sessionGuid, out string setId, out int matchedBindingCount)
	{
		setId = CitySoundProfileRegistry.DefaultSetId;
		matchedBindingCount = 0;
		string normalizedSessionGuid = CitySoundProfileRegistry.NormalizeSessionGuid(sessionGuid);
		if (string.IsNullOrWhiteSpace(normalizedSessionGuid))
		{
			return false;
		}

		HashSet<string> matchedSetIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		for (int i = 0; i < s_CitySoundProfileRegistry.Bindings.Count; i++)
		{
			CitySoundProfileBinding binding = s_CitySoundProfileRegistry.Bindings[i];
			string bindingSessionGuid = CitySoundProfileRegistry.NormalizeSessionGuid(binding.SaveSessionGuid);
			if (!string.Equals(bindingSessionGuid, normalizedSessionGuid, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			matchedBindingCount++;
			matchedSetIds.Add(CitySoundProfileRegistry.NormalizeSetId(binding.SetId));
		}

		if (matchedSetIds.Count != 1)
		{
			return false;
		}

		setId = EnsureSoundSetExists(matchedSetIds.First(), matchedSetIds.First());
		return true;
	}

	// Auto-create a GUID binding for the currently loaded save using existing session-guid metadata.
	private static bool TryAutoBindCurrentCityBySessionGuid(out string migratedSetId)
	{
		migratedSetId = CitySoundProfileRegistry.DefaultSetId;
		if (!HasCurrentCityIdentity() || string.IsNullOrWhiteSpace(s_CurrentCitySessionGuid))
		{
			return false;
		}

		if (!TryGetUniqueSetForSessionGuid(s_CurrentCitySessionGuid, out string resolvedSetId, out int matchCount))
		{
			if (matchCount > 1)
			{
				Log.Warn(
					$"Session-guid migration skipped for city {GetCurrentCityLabel()} because session GUID '{s_CurrentCitySessionGuid}' maps to multiple sets.");
			}

			return false;
		}

		bool changed = s_CitySoundProfileRegistry.UpsertBinding(
			s_CurrentCitySaveAssetGuid,
			resolvedSetId,
			s_CurrentCityDisplayName,
			string.Empty,
			s_CurrentCitySessionGuid);
		if (!changed)
		{
			return false;
		}

		migratedSetId = resolvedSetId;
		s_SelectedCityBindingGuidForOptions = s_CurrentCitySaveAssetGuid;
		SaveCitySoundProfileRegistry();
		NotifyOptionsCatalogChanged();
		Log.Info(
			$"Migrated city sound-set binding to save GUID {s_CurrentCitySaveAssetGuid} using session GUID {s_CurrentCitySessionGuid}.");
		return true;
	}

	// Backfill metadata for existing bindings when display/session information changes.
	private static void RefreshCurrentCityBindingMetadata()
	{
		if (!HasCurrentCityIdentity() || !s_CitySoundProfileRegistry.HasBindingForCity(s_CurrentCitySaveAssetGuid))
		{
			return;
		}

		string boundSet = s_CitySoundProfileRegistry.ResolveBoundSetId(s_CurrentCitySaveAssetGuid);
		if (!s_CitySoundProfileRegistry.UpsertBinding(
			s_CurrentCitySaveAssetGuid,
			boundSet,
			s_CurrentCityDisplayName,
			string.Empty,
			s_CurrentCitySessionGuid))
		{
			return;
		}

		SaveCitySoundProfileRegistry();
		NotifyOptionsCatalogChanged();
	}

	// Receive city identity updates from runtime load detection and trigger auto-apply flow.
	internal static void UpdateCurrentCityContext(string saveAssetGuid, string displayName, string saveSessionGuid = "")
	{
		string normalizedGuid = CitySoundProfileRegistry.NormalizeGuidKey(saveAssetGuid);
		string normalizedDisplayName = (displayName ?? string.Empty).Trim();
		string normalizedSessionGuid = CitySoundProfileRegistry.NormalizeSessionGuid(saveSessionGuid);

		bool changed =
			!string.Equals(s_CurrentCitySaveAssetGuid, normalizedGuid, StringComparison.OrdinalIgnoreCase) ||
			!string.Equals(s_CurrentCityDisplayName, normalizedDisplayName, StringComparison.Ordinal) ||
			!string.Equals(s_CurrentCitySessionGuid, normalizedSessionGuid, StringComparison.OrdinalIgnoreCase);
		if (!changed)
		{
			return;
		}

		s_CurrentCitySaveAssetGuid = normalizedGuid;
		s_CurrentCityDisplayName = normalizedDisplayName;
		s_CurrentCitySessionGuid = normalizedSessionGuid;
		RefreshCurrentCityBindingMetadata();

		if (s_CitySoundProfileRegistry.AutoApplyByCity)
		{
			ApplyBoundSetForCurrentCity($"city {GetCurrentCityLabel()}");
		}
		else
		{
			s_CitySoundSetStatus =
				$"Detected city {GetCurrentCityLabel()}, auto-apply is off. Active sound set is '{GetSoundSetDisplayName(GetActiveSoundSetReferenceId())}'.";
			OptionsVersion++;
		}
	}

	internal static bool GetAutoApplyCitySoundSets()
	{
		return s_CitySoundProfileRegistry.AutoApplyByCity;
	}

	// Toggle automatic city binding application and reconcile active set when enabled.
	internal static void SetAutoApplyCitySoundSets(bool enabled)
	{
		if (s_CitySoundProfileRegistry.AutoApplyByCity == enabled)
		{
			return;
		}

		s_CitySoundProfileRegistry.AutoApplyByCity = enabled;
		SaveCitySoundProfileRegistry();
		OptionsVersion++;
		if (enabled)
		{
			ApplyBoundSetForCurrentCity("enabling city auto-apply");
		}
		else
		{
			s_CitySoundSetStatus =
				$"City auto-apply disabled. Active sound set is '{GetSoundSetDisplayName(GetActiveSoundSetReferenceId())}'.";
		}
	}

	// Build dropdown options for selectable sound sets (Default, local sets, then module profiles).
	internal static DropdownItem<string>[] BuildCitySoundSetDropdownItems()
	{
		List<DropdownItem<string>> items = new List<DropdownItem<string>>();
		if (s_CitySoundProfileRegistry.Sets.TryGetValue(CitySoundProfileRegistry.DefaultSetId, out CitySoundProfileSet defaultSet))
		{
			items.Add(new DropdownItem<string>
			{
				value = CitySoundProfileRegistry.DefaultSetId,
				displayName = defaultSet.DisplayName
			});
		}

		IEnumerable<KeyValuePair<string, CitySoundProfileSet>> customSets = s_CitySoundProfileRegistry.Sets
			.Where(static pair => !string.Equals(pair.Key, CitySoundProfileRegistry.DefaultSetId, StringComparison.OrdinalIgnoreCase))
			.OrderBy(static pair => pair.Value?.DisplayName ?? pair.Key, StringComparer.OrdinalIgnoreCase);
		foreach (KeyValuePair<string, CitySoundProfileSet> pair in customSets)
		{
			CitySoundProfileSet set = pair.Value ?? new CitySoundProfileSet();
			items.Add(new DropdownItem<string>
			{
				value = pair.Key,
				displayName = string.IsNullOrWhiteSpace(set.DisplayName) ? pair.Key : set.DisplayName
			});
		}

		string[] moduleProfileKeys = GetAudioModuleSoundSetProfileKeys();
		for (int i = 0; i < moduleProfileKeys.Length; i++)
		{
			string profileKey = moduleProfileKeys[i];
			string displayName = profileKey;
			if (TryGetAudioModuleSoundSetProfileDisplayName(profileKey, out string resolvedDisplayName) &&
				!string.IsNullOrWhiteSpace(resolvedDisplayName))
			{
				displayName = resolvedDisplayName;
			}

			items.Add(new DropdownItem<string>
			{
				value = profileKey,
				displayName = $"{displayName} (Module Profile)"
			});
		}

		if (items.Count == 0)
		{
			items.Add(new DropdownItem<string>
			{
				value = CitySoundProfileRegistry.DefaultSetId,
				displayName = CitySoundProfileRegistry.DefaultSetDisplayName
			});
		}

		return items.ToArray();
	}

	internal static DropdownItem<string>[] BuildCitySoundSetBindingDropdownItems()
	{
		// Build a human-friendly list like: CityName (GUID) -> SetName.
		List<DropdownItem<string>> items = s_CitySoundProfileRegistry.Bindings
			.Where(static binding => !string.IsNullOrWhiteSpace(CitySoundProfileRegistry.NormalizeGuidKey(binding.SaveAssetGuid)))
			.OrderBy(static binding => (binding.LastSeenDisplayName ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase)
			.ThenBy(static binding => binding.SaveAssetGuid, StringComparer.OrdinalIgnoreCase)
			.Select(binding =>
			{
				string guid = CitySoundProfileRegistry.NormalizeGuidKey(binding.SaveAssetGuid);
				string cityLabel = GetBindingCityDisplayName(binding);
				string setLabel = GetSoundSetDisplayName(binding.SetId);
				return new DropdownItem<string>
				{
					value = guid,
					displayName = $"{cityLabel} ({guid}) -> {setLabel}"
				};
			})
			.ToList();

		if (items.Count == 0)
		{
			items.Add(new DropdownItem<string>
			{
				value = string.Empty,
				displayName = "No city bindings",
				disabled = true
			});
		}

		return items.ToArray();
	}

	internal static string GetSelectedCitySoundSetForOptions()
	{
		return GetSelectedCitySoundSetReferenceForOptions();
	}

	// Update selected-set UI state and persist only when value changes.
	internal static void SetSelectedCitySoundSetForOptions(string setId)
	{
		if (TryNormalizeModuleSoundSetProfileKey(setId, out string moduleProfileKey))
		{
			if (string.Equals(s_SelectedModuleSoundSetProfileKeyForOptions, moduleProfileKey, StringComparison.OrdinalIgnoreCase))
			{
				return;
			}

			s_SelectedModuleSoundSetProfileKeyForOptions = moduleProfileKey;
			OptionsVersion++;
			return;
		}

		string normalized = CitySoundProfileRegistry.NormalizeSetId(setId);
		if (!s_CitySoundProfileRegistry.ContainsSet(normalized))
		{
			normalized = CitySoundProfileRegistry.DefaultSetId;
		}

		bool hadModuleSelection = !string.IsNullOrWhiteSpace(s_SelectedModuleSoundSetProfileKeyForOptions);
		s_SelectedModuleSoundSetProfileKeyForOptions = string.Empty;

		if (string.Equals(s_CitySoundProfileRegistry.SelectedSetId, normalized, StringComparison.OrdinalIgnoreCase))
		{
			if (hadModuleSelection)
			{
				OptionsVersion++;
			}

			return;
		}

		s_CitySoundProfileRegistry.SelectedSetId = normalized;
		SaveCitySoundProfileRegistry();
		OptionsVersion++;
	}

	internal static string GetSelectedCitySoundSetBindingGuidForOptions()
	{
		// Recover to the first available binding when the selected key is stale.
		string normalized = NormalizeExistingBindingGuid(s_SelectedCityBindingGuidForOptions);
		if (string.IsNullOrWhiteSpace(normalized))
		{
			normalized = GetFirstBindingGuidOrEmpty();
		}

		s_SelectedCityBindingGuidForOptions = normalized;
		return normalized;
	}

	internal static void SetSelectedCitySoundSetBindingGuidForOptions(string saveAssetGuid)
	{
		// Keep selected binding key normalized and in sync with current binding rows.
		string normalized = NormalizeExistingBindingGuid(saveAssetGuid);
		if (string.Equals(s_SelectedCityBindingGuidForOptions, normalized, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}

		s_SelectedCityBindingGuidForOptions = normalized;
		OptionsVersion++;
	}

	internal static string GetNewCitySoundSetNameForOptions()
	{
		return s_CitySoundProfileRegistry.PendingNewSetName;
	}

	// Keep shared create/rename set-name input in memory until an explicit action runs.
	internal static void SetNewCitySoundSetNameForOptions(string name)
	{
		string normalized = (name ?? string.Empty).Trim();
		if (string.Equals(s_CitySoundProfileRegistry.PendingNewSetName, normalized, StringComparison.Ordinal))
		{
			return;
		}

		s_CitySoundProfileRegistry.PendingNewSetName = normalized;
	}

	// Activate whichever set is selected in the General tab dropdown.
	internal static void ActivateSelectedCitySoundSetFromOptions()
	{
		string setId = GetSelectedCitySoundSetReferenceForOptions();
		if (!ActivateSoundSetInternal(setId, "manual selection", forceReload: false))
		{
			s_CitySoundSetStatus = $"Sound set '{GetSoundSetDisplayName(setId)}' is already active.";
			OptionsVersion++;
		}
	}

	// Persist current runtime configs into the selected set without switching active set.
	internal static void UpdateSelectedCitySoundSetFromOptions()
	{
		string selectedReference = GetSelectedCitySoundSetReferenceForOptions();
		if (IsModuleSoundSetProfileSelection(selectedReference))
		{
			s_CitySoundSetStatus = "Module sound-set profiles are read-only. Duplicate the selected profile to create an editable local copy.";
			OptionsVersion++;
			return;
		}

		string selectedSetId = CitySoundProfileRegistry.NormalizeSetId(selectedReference);
		if (!s_CitySoundProfileRegistry.ContainsSet(selectedSetId))
		{
			s_CitySoundSetStatus = "Selected sound set does not exist.";
			OptionsVersion++;
			return;
		}

		SaveConfigSetFiles(selectedSetId);
		SaveCitySoundProfileRegistry();

		string selectedDisplayName = GetSoundSetDisplayName(selectedSetId);
		string activeReference = GetActiveSoundSetReferenceId();
		if (string.Equals(selectedSetId, activeReference, StringComparison.OrdinalIgnoreCase))
		{
			s_CitySoundSetStatus = $"Updated sound set '{selectedDisplayName}' ({selectedSetId}) from current settings.";
		}
		else
		{
			string activeDisplayName = GetSoundSetDisplayName(activeReference);
			s_CitySoundSetStatus =
				$"Updated sound set '{selectedDisplayName}' ({selectedSetId}) using current active settings '{activeDisplayName}'.";
		}

		OptionsVersion++;
	}

	internal static void RenameSelectedCitySoundSetFromOptions()
	{
		// Rename only changes display text; set ID remains stable for bindings and file paths.
		string selectedReference = GetSelectedCitySoundSetReferenceForOptions();
		if (IsModuleSoundSetProfileSelection(selectedReference))
		{
			s_CitySoundSetStatus = "Module sound-set profiles are read-only and cannot be renamed.";
			OptionsVersion++;
			return;
		}

		string selected = CitySoundProfileRegistry.NormalizeSetId(selectedReference);
		if (string.Equals(selected, CitySoundProfileRegistry.DefaultSetId, StringComparison.OrdinalIgnoreCase))
		{
			s_CitySoundSetStatus = "The Default sound set cannot be renamed.";
			OptionsVersion++;
			return;
		}

		if (!s_CitySoundProfileRegistry.Sets.TryGetValue(selected, out CitySoundProfileSet set) || set == null)
		{
			s_CitySoundSetStatus = "Selected sound set does not exist.";
			OptionsVersion++;
			return;
		}

		string requestedName = (s_CitySoundProfileRegistry.PendingNewSetName ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(requestedName))
		{
			s_CitySoundSetStatus = "Enter a set name before renaming the selected set.";
			OptionsVersion++;
			return;
		}

		string normalizedDisplayName = CitySoundProfileRegistry.NormalizeDisplayName(requestedName, selected);
		if (string.Equals(set.DisplayName, normalizedDisplayName, StringComparison.Ordinal))
		{
			s_CitySoundSetStatus = $"Sound set '{selected}' is already named '{normalizedDisplayName}'.";
			OptionsVersion++;
			return;
		}

		string previousDisplayName = set.DisplayName;
		set.DisplayName = normalizedDisplayName;
		s_CitySoundProfileRegistry.PendingNewSetName = string.Empty;
		SaveCitySoundProfileRegistry();
		NotifyOptionsCatalogChanged();
		s_CitySoundSetStatus = $"Renamed sound set '{previousDisplayName}' ({selected}) to '{normalizedDisplayName}'.";
	}

	internal static void DuplicateSelectedCitySoundSetFromOptions()
	{
		// Duplicate selected set contents without switching the currently active runtime set.
		string sourceSelectionId = GetSelectedCitySoundSetReferenceForOptions();
		bool sourceIsModuleProfile = TryNormalizeModuleSoundSetProfileKey(sourceSelectionId, out string sourceModuleProfileKey);
		string sourceSetId = sourceIsModuleProfile
			? sourceModuleProfileKey
			: CitySoundProfileRegistry.NormalizeSetId(sourceSelectionId);
		if (!sourceIsModuleProfile && !s_CitySoundProfileRegistry.ContainsSet(sourceSetId))
		{
			s_CitySoundSetStatus = "Selected sound set does not exist.";
			OptionsVersion++;
			return;
		}

		string sourceDisplayName = GetSoundSetDisplayName(sourceSetId);
		string requestedName = $"{sourceDisplayName} Copy";
		string duplicatedSetId = s_CitySoundProfileRegistry.CreateUniqueSetId(requestedName);
		string duplicatedDisplayName = CitySoundProfileRegistry.NormalizeDisplayName(requestedName, duplicatedSetId);
		s_CitySoundProfileRegistry.EnsureSet(duplicatedSetId, duplicatedDisplayName);
		DuplicateSoundSetConfigFiles(sourceSetId, duplicatedSetId);
		s_CitySoundProfileRegistry.SelectedSetId = duplicatedSetId;
		SaveCitySoundProfileRegistry();
		NotifyOptionsCatalogChanged();
		s_SelectedModuleSoundSetProfileKeyForOptions = string.Empty;
		s_CitySoundSetStatus =
			sourceIsModuleProfile
				? $"Copied module sound-set profile '{sourceDisplayName}' to editable local set '{duplicatedDisplayName}' ({duplicatedSetId})."
				: $"Duplicated sound set '{sourceDisplayName}' to '{duplicatedDisplayName}' ({duplicatedSetId}).";
	}

	// Create a new set by snapshotting the currently loaded runtime configs.
	internal static void CreateCitySoundSetFromOptions()
	{
		string requestedName = (s_CitySoundProfileRegistry.PendingNewSetName ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(requestedName))
		{
			s_CitySoundSetStatus = "Enter a name before creating a new sound set.";
			OptionsVersion++;
			return;
		}

		string setId = s_CitySoundProfileRegistry.CreateUniqueSetId(requestedName);
		string displayName = CitySoundProfileRegistry.NormalizeDisplayName(requestedName, setId);
		s_CitySoundProfileRegistry.EnsureSet(setId, displayName);
		s_CitySoundProfileRegistry.SelectedSetId = setId;
		s_SelectedModuleSoundSetProfileKeyForOptions = string.Empty;
		s_CitySoundProfileRegistry.PendingNewSetName = string.Empty;
		SaveConfigSetFiles(setId);
		SaveCitySoundProfileRegistry();
		NotifyOptionsCatalogChanged();
		s_CitySoundSetStatus = $"Created sound set '{displayName}' ({setId}) from current settings.";
	}

	// Delete selected set, clean on-disk files, and keep active/runtime state valid.
	internal static void DeleteSelectedCitySoundSetFromOptions()
	{
		string selectedReference = GetSelectedCitySoundSetReferenceForOptions();
		if (IsModuleSoundSetProfileSelection(selectedReference))
		{
			s_CitySoundSetStatus = "Module sound-set profiles are read-only and cannot be deleted.";
			OptionsVersion++;
			return;
		}

		string selected = CitySoundProfileRegistry.NormalizeSetId(selectedReference);
		if (string.Equals(selected, CitySoundProfileRegistry.DefaultSetId, StringComparison.OrdinalIgnoreCase))
		{
			s_CitySoundSetStatus = "The Default sound set cannot be deleted.";
			OptionsVersion++;
			return;
		}

		if (!s_CitySoundProfileRegistry.ContainsSet(selected))
		{
			s_CitySoundSetStatus = "Selected sound set does not exist.";
			OptionsVersion++;
			return;
		}

		string activeReference = GetActiveSoundSetReferenceId();
		bool deletingActiveSet =
			!IsModuleSoundSetProfileSelection(activeReference) &&
			string.Equals(activeReference, selected, StringComparison.OrdinalIgnoreCase);

		string deletedDisplayName = GetSoundSetDisplayName(selected);
		s_CitySoundProfileRegistry.RemoveSet(selected);
		try
		{
			string setDirectory = CitySoundProfileRegistry.GetSetDirectoryPath(
				SettingsDirectory,
				selected,
				ensureExists: false);
			if (Directory.Exists(setDirectory))
			{
				Directory.Delete(setDirectory, recursive: true);
			}
		}
		catch (Exception ex)
		{
			Log.Warn($"Failed to delete sound-set directory for '{selected}': {ex.Message}");
		}

		if (deletingActiveSet)
		{
			ActivateSoundSetInternal(CitySoundProfileRegistry.DefaultSetId, "deleting active sound set", forceReload: true);
		}
		else
		{
			SaveCitySoundProfileRegistry();
			NotifyOptionsCatalogChanged();
		}

		s_CitySoundSetStatus = $"Deleted sound set '{deletedDisplayName}' ({selected}).";
	}

	// Bind current city to whichever set is selected in the General tab.
	internal static void BindCurrentCityToSelectedSoundSetFromOptions()
	{
		string selectedReference = GetSelectedCitySoundSetReferenceForOptions();
		if (IsModuleSoundSetProfileSelection(selectedReference))
		{
			s_CitySoundSetStatus = "Module sound-set profiles are read-only and cannot be auto-applied by city binding. Duplicate one to bind an editable local copy.";
			OptionsVersion++;
			return;
		}

		string selectedSet = EnsureSoundSetExists(
			selectedReference,
			selectedReference);
		BindCurrentCityToSoundSetFromOptions(selectedSet);
	}

	internal static void RemoveSelectedCitySoundSetBindingFromOptions()
	{
		// Remove one persisted binding by GUID from the management dropdown.
		string selectedBindingGuid = GetSelectedCitySoundSetBindingGuidForOptions();
		if (string.IsNullOrWhiteSpace(selectedBindingGuid))
		{
			s_CitySoundSetStatus = "No saved city binding is selected.";
			OptionsVersion++;
			return;
		}

		string cityLabel = "Unknown City";
		if (TryGetBindingByGuid(selectedBindingGuid, out CitySoundProfileBinding existingBinding))
		{
			cityLabel = GetBindingCityDisplayName(existingBinding);
		}

		bool removingCurrentCityBinding = string.Equals(
			s_CurrentCitySaveAssetGuid,
			selectedBindingGuid,
			StringComparison.OrdinalIgnoreCase);
		if (!s_CitySoundProfileRegistry.RemoveBindingForCity(selectedBindingGuid))
		{
			s_CitySoundSetStatus = $"No city binding found for GUID {selectedBindingGuid}.";
			OptionsVersion++;
			return;
		}

		s_SelectedCityBindingGuidForOptions = GetFirstBindingGuidOrEmpty();
		SaveCitySoundProfileRegistry();
		s_CitySoundSetStatus = $"Removed saved city binding '{cityLabel}' ({selectedBindingGuid}).";
		if (removingCurrentCityBinding && s_CitySoundProfileRegistry.AutoApplyByCity)
		{
			ApplyBoundSetForCurrentCity("binding removed from saved city list");
		}
		else
		{
			OptionsVersion++;
		}
	}

	// Remove binding for the currently loaded city identity.
	internal static void ClearCurrentCitySoundSetBindingFromOptions()
	{
		if (!HasCurrentCityIdentity())
		{
			s_CitySoundSetStatus = "No loaded city identity is available yet. Load a city first.";
			OptionsVersion++;
			return;
		}

		if (!s_CitySoundProfileRegistry.RemoveBindingForCity(s_CurrentCitySaveAssetGuid))
		{
			s_CitySoundSetStatus = $"City {GetCurrentCityLabel()} has no custom sound-set binding.";
			OptionsVersion++;
			return;
		}

		s_SelectedCityBindingGuidForOptions = GetFirstBindingGuidOrEmpty();
		SaveCitySoundProfileRegistry();
		s_CitySoundSetStatus = $"Removed city sound-set binding for {GetCurrentCityLabel()}.";
		if (s_CitySoundProfileRegistry.AutoApplyByCity)
		{
			ApplyBoundSetForCurrentCity("binding removed");
		}
		else
		{
			OptionsVersion++;
		}
	}

	// True when runtime has a valid loaded city GUID to bind against.
	internal static bool HasCurrentCityIdentity()
	{
		return !string.IsNullOrWhiteSpace(s_CurrentCitySaveAssetGuid);
	}

	// True when current loaded city has an explicit saved binding.
	internal static bool HasCurrentCitySoundSetBinding()
	{
		return s_CitySoundProfileRegistry.HasBindingForCity(s_CurrentCitySaveAssetGuid);
	}

	// True when selected set is the built-in Default set.
	internal static bool IsSelectedCitySoundSetDefault()
	{
		if (IsSelectedCitySoundSetModuleProfile())
		{
			return false;
		}

		return string.Equals(
			CitySoundProfileRegistry.NormalizeSetId(GetSelectedCitySoundSetReferenceForOptions()),
			CitySoundProfileRegistry.DefaultSetId,
			StringComparison.OrdinalIgnoreCase);
	}

	// True when selected set is a module-provided profile entry.
	internal static bool IsSelectedCitySoundSetModuleProfile()
	{
		return IsModuleSoundSetProfileSelection(GetSelectedCitySoundSetReferenceForOptions());
	}

	// True when selected set is not editable in-place.
	internal static bool IsSelectedCitySoundSetReadOnly()
	{
		return IsSelectedCitySoundSetDefault() || IsSelectedCitySoundSetModuleProfile();
	}

	// Build multiline status text displayed in the General tab.
	internal static string GetCitySoundSetStatusText()
	{
		string activeSetId = GetActiveSoundSetReferenceId();
		string activeLabel = GetSoundSetDisplayName(activeSetId);
		string cityLabel = GetCurrentCityLabel();
		string bindingLabel = HasCurrentCityIdentity()
			? GetSoundSetDisplayName(s_CitySoundProfileRegistry.ResolveBoundSetId(s_CurrentCitySaveAssetGuid))
			: "n/a";
		return
			$"Active Set: {activeLabel} ({activeSetId})\n" +
			$"Current City: {cityLabel}\n" +
			$"Bound Set: {bindingLabel}\n" +
			$"{s_CitySoundSetStatus}";
	}

	// True when binding-management dropdown has a valid selected row.
	internal static bool HasSelectedCitySoundSetBindingForOptions()
	{
		return !string.IsNullOrWhiteSpace(GetSelectedCitySoundSetBindingGuidForOptions());
	}

	private static void BindCurrentCityToSoundSetFromOptions(string setId)
	{
		// Shared bind routine used by the "bind current city to selected set" action.
		if (!HasCurrentCityIdentity())
		{
			s_CitySoundSetStatus = "No loaded city identity is available yet. Load a city first.";
			OptionsVersion++;
			return;
		}

		if (IsModuleSoundSetProfileSelection(setId))
		{
			s_CitySoundSetStatus = "Module sound-set profiles cannot be used for city auto-apply bindings.";
			OptionsVersion++;
			return;
		}

		string targetSet = EnsureSoundSetExists(setId, setId);
		bool changed = s_CitySoundProfileRegistry.UpsertBinding(
			s_CurrentCitySaveAssetGuid,
			targetSet,
			s_CurrentCityDisplayName,
			string.Empty,
			s_CurrentCitySessionGuid);
		s_SelectedCityBindingGuidForOptions = s_CurrentCitySaveAssetGuid;
		SaveCitySoundProfileRegistry();
		if (changed)
		{
			s_CitySoundSetStatus =
				$"Bound city {GetCurrentCityLabel()} to sound set '{GetSoundSetDisplayName(targetSet)}'.";
		}
		else
		{
			s_CitySoundSetStatus =
				$"City {GetCurrentCityLabel()} is already bound to '{GetSoundSetDisplayName(targetSet)}'.";
		}

		if (s_CitySoundProfileRegistry.AutoApplyByCity)
		{
			ApplyBoundSetForCurrentCity("binding update");
		}
		else
		{
			OptionsVersion++;
		}
	}

	private static void DuplicateSoundSetConfigFiles(string sourceSetId, string targetSetId)
	{
		string normalizedTargetSet = EnsureSoundSetExists(targetSetId, targetSetId);
		bool sourceIsModuleProfile = TryNormalizeModuleSoundSetProfileKey(sourceSetId, out string sourceModuleProfileKey);
		string normalizedSourceSet = sourceIsModuleProfile
			? sourceModuleProfileKey
			: EnsureSoundSetExists(sourceSetId, sourceSetId);

		// Ensure on-disk files are current before copying from an active local set.
		if (!sourceIsModuleProfile &&
			string.Equals(normalizedSourceSet, GetActiveSoundSetReferenceId(), StringComparison.OrdinalIgnoreCase))
		{
			SaveConfig();
		}

		DuplicateSirenSettingsFile(normalizedSourceSet, normalizedTargetSet);
		DuplicateDomainSettingsFile(normalizedSourceSet, normalizedTargetSet, VehicleEngineSettingsFileName, VehicleEngineCustomFolderName);
		DuplicateDomainSettingsFile(normalizedSourceSet, normalizedTargetSet, AmbientSettingsFileName, AmbientCustomFolderName);
		DuplicateDomainSettingsFile(normalizedSourceSet, normalizedTargetSet, TransitAnnouncementSettingsFileName, TransitAnnouncementCustomFolderName);
	}

	private static void DuplicateSirenSettingsFile(string sourceSetId, string targetSetId)
	{
		// Copy siren config file verbatim when present, otherwise seed with defaults.
		string targetPath = GetSoundSetSettingsPath(targetSetId, SirenReplacementConfig.SettingsFileName, ensureDirectoryExists: true);
		try
		{
			if (TryResolveSoundSetSettingsReadPath(sourceSetId, SirenReplacementConfig.SettingsFileName, out string sourcePath) &&
				File.Exists(sourcePath))
			{
				File.Copy(sourcePath, targetPath, overwrite: true);
			}
			else
			{
				SirenReplacementConfig.Save(targetPath, SirenReplacementConfig.CreateDefault(), Log);
			}
		}
		catch (Exception ex)
		{
			Log.Warn($"Failed to duplicate siren settings from '{sourceSetId}' to '{targetSetId}': {ex.Message}");
			SirenReplacementConfig.Save(targetPath, SirenReplacementConfig.CreateDefault(), Log);
		}
	}

	private static void DuplicateDomainSettingsFile(
		string sourceSetId,
		string targetSetId,
		string settingsFileName,
		string customFolderName)
	{
		// Copy one non-siren domain settings file with default fallback on failure.
		string targetPath = GetSoundSetSettingsPath(targetSetId, settingsFileName, ensureDirectoryExists: true);
		try
		{
			if (TryResolveSoundSetSettingsReadPath(sourceSetId, settingsFileName, out string sourcePath) &&
				File.Exists(sourcePath))
			{
				File.Copy(sourcePath, targetPath, overwrite: true);
			}
			else
			{
				AudioReplacementDomainConfig.Save(
					targetPath,
					AudioReplacementDomainConfig.CreateDefault(customFolderName),
					Log);
			}
		}
		catch (Exception ex)
		{
			Log.Warn($"Failed to duplicate settings file '{settingsFileName}' from '{sourceSetId}' to '{targetSetId}': {ex.Message}");
			AudioReplacementDomainConfig.Save(
				targetPath,
				AudioReplacementDomainConfig.CreateDefault(customFolderName),
			Log);
		}
	}

	// Save current runtime configs into the specified set directory.
	private static void SaveConfigSetFiles(string setId)
	{
		if (IsModuleSoundSetProfileSelection(setId))
		{
			return;
		}

		string normalizedSet = EnsureSoundSetExists(setId, setId);
		string sirenSettingsPath = GetSoundSetSettingsPath(
			normalizedSet,
			SirenReplacementConfig.SettingsFileName,
			ensureDirectoryExists: true);
		SirenReplacementConfig.Save(sirenSettingsPath, Config, Log);

		string engineSettingsPath = GetSoundSetSettingsPath(
			normalizedSet,
			VehicleEngineSettingsFileName,
			ensureDirectoryExists: true);
		AudioReplacementDomainConfig.Save(engineSettingsPath, VehicleEngineConfig, Log);

		string ambientSettingsPath = GetSoundSetSettingsPath(
			normalizedSet,
			AmbientSettingsFileName,
			ensureDirectoryExists: true);
		AudioReplacementDomainConfig.Save(ambientSettingsPath, AmbientConfig, Log);

		string transitAnnouncementSettingsPath = GetSoundSetSettingsPath(
			normalizedSet,
			TransitAnnouncementSettingsFileName,
			ensureDirectoryExists: true);
		AudioReplacementDomainConfig.Save(transitAnnouncementSettingsPath, TransitAnnouncementConfig, Log);
	}

	// Expose currently active set ID for runtime systems that need set context.
	internal static string GetActiveSoundSetId()
	{
		return GetActiveSoundSetReferenceId();
	}
}
