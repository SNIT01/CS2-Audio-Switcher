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

	// Last detected loaded-city identity (GUID-only matching key).
	private static string s_CurrentCitySaveAssetGuid = string.Empty;

	// Human-readable city label shown in status text and binding lists.
	private static string s_CurrentCityDisplayName = string.Empty;

	// Last city sound-set status message shown in options UI.
	private static string s_CitySoundSetStatus = "No city sound-set decision has been made yet.";

	// Current selection in the saved-binding management dropdown.
	private static string s_SelectedCityBindingGuidForOptions = string.Empty;

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

	// Load siren/engine/ambient configs for one set and update active-selection metadata.
	private static void LoadSoundSetConfig(string setId)
	{
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

		Config.Normalize();
		VehicleEngineConfig.Normalize(VehicleEngineCustomFolderName);
		AmbientConfig.Normalize(AmbientCustomFolderName);

		s_CitySoundProfileRegistry.ActiveSetId = normalizedSet;
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
		string normalizedSet = EnsureSoundSetExists(setId, setId);
		if (!forceReload &&
			string.Equals(s_CitySoundProfileRegistry.ActiveSetId, normalizedSet, StringComparison.OrdinalIgnoreCase))
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
			s_CitySoundSetStatus = $"No city identity is available, keeping active sound set '{GetSoundSetDisplayName(s_CitySoundProfileRegistry.ActiveSetId)}'.";
			OptionsVersion++;
			return;
		}

		string targetSetId = s_CitySoundProfileRegistry.ResolveBoundSetId(s_CurrentCitySaveAssetGuid);
		bool switched = ActivateSoundSetInternal(targetSetId, reason, forceReload: false);
		if (!switched)
		{
			s_CitySoundSetStatus =
				$"City {GetCurrentCityLabel()} uses sound set '{GetSoundSetDisplayName(targetSetId)}'.";
			OptionsVersion++;
		}
	}

	// Receive city identity updates from runtime load detection and trigger auto-apply flow.
	internal static void UpdateCurrentCityContext(string saveAssetGuid, string displayName)
	{
		string normalizedGuid = CitySoundProfileRegistry.NormalizeGuidKey(saveAssetGuid);
		string normalizedDisplayName = (displayName ?? string.Empty).Trim();

		bool changed =
			!string.Equals(s_CurrentCitySaveAssetGuid, normalizedGuid, StringComparison.OrdinalIgnoreCase) ||
			!string.Equals(s_CurrentCityDisplayName, normalizedDisplayName, StringComparison.Ordinal);
		if (!changed)
		{
			return;
		}

		s_CurrentCitySaveAssetGuid = normalizedGuid;
		s_CurrentCityDisplayName = normalizedDisplayName;

		if (s_CitySoundProfileRegistry.AutoApplyByCity)
		{
			ApplyBoundSetForCurrentCity($"city {GetCurrentCityLabel()}");
		}
		else
		{
			s_CitySoundSetStatus =
				$"Detected city {GetCurrentCityLabel()}, auto-apply is off. Active sound set is '{GetSoundSetDisplayName(s_CitySoundProfileRegistry.ActiveSetId)}'.";
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
				$"City auto-apply disabled. Active sound set is '{GetSoundSetDisplayName(s_CitySoundProfileRegistry.ActiveSetId)}'.";
		}
	}

	// Build dropdown options for selectable sound sets (Default first, then custom sets).
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
		return s_CitySoundProfileRegistry.SelectedSetId;
	}

	// Update selected-set UI state and persist only when value changes.
	internal static void SetSelectedCitySoundSetForOptions(string setId)
	{
		string normalized = CitySoundProfileRegistry.NormalizeSetId(setId);
		if (!s_CitySoundProfileRegistry.ContainsSet(normalized))
		{
			normalized = CitySoundProfileRegistry.DefaultSetId;
		}

		if (string.Equals(s_CitySoundProfileRegistry.SelectedSetId, normalized, StringComparison.OrdinalIgnoreCase))
		{
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
		string setId = s_CitySoundProfileRegistry.SelectedSetId;
		if (!ActivateSoundSetInternal(setId, "manual selection", forceReload: false))
		{
			s_CitySoundSetStatus = $"Sound set '{GetSoundSetDisplayName(setId)}' is already active.";
			OptionsVersion++;
		}
	}

	// Persist current runtime configs into the selected set without switching active set.
	internal static void UpdateSelectedCitySoundSetFromOptions()
	{
		string selectedSetId = CitySoundProfileRegistry.NormalizeSetId(s_CitySoundProfileRegistry.SelectedSetId);
		if (!s_CitySoundProfileRegistry.ContainsSet(selectedSetId))
		{
			s_CitySoundSetStatus = "Selected sound set does not exist.";
			OptionsVersion++;
			return;
		}

		SaveConfigSetFiles(selectedSetId);
		SaveCitySoundProfileRegistry();

		string selectedDisplayName = GetSoundSetDisplayName(selectedSetId);
		if (string.Equals(selectedSetId, s_CitySoundProfileRegistry.ActiveSetId, StringComparison.OrdinalIgnoreCase))
		{
			s_CitySoundSetStatus = $"Updated sound set '{selectedDisplayName}' ({selectedSetId}) from current settings.";
		}
		else
		{
			string activeDisplayName = GetSoundSetDisplayName(s_CitySoundProfileRegistry.ActiveSetId);
			s_CitySoundSetStatus =
				$"Updated sound set '{selectedDisplayName}' ({selectedSetId}) using current active settings '{activeDisplayName}'.";
		}

		OptionsVersion++;
	}

	internal static void RenameSelectedCitySoundSetFromOptions()
	{
		// Rename only changes display text; set ID remains stable for bindings and file paths.
		string selected = CitySoundProfileRegistry.NormalizeSetId(s_CitySoundProfileRegistry.SelectedSetId);
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
		string sourceSetId = CitySoundProfileRegistry.NormalizeSetId(s_CitySoundProfileRegistry.SelectedSetId);
		if (!s_CitySoundProfileRegistry.ContainsSet(sourceSetId))
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
		s_CitySoundSetStatus =
			$"Duplicated sound set '{sourceDisplayName}' to '{duplicatedDisplayName}' ({duplicatedSetId}).";
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
		s_CitySoundProfileRegistry.PendingNewSetName = string.Empty;
		SaveConfigSetFiles(setId);
		SaveCitySoundProfileRegistry();
		NotifyOptionsCatalogChanged();
		s_CitySoundSetStatus = $"Created sound set '{displayName}' ({setId}) from current settings.";
	}

	// Delete selected set, clean on-disk files, and keep active/runtime state valid.
	internal static void DeleteSelectedCitySoundSetFromOptions()
	{
		string selected = CitySoundProfileRegistry.NormalizeSetId(s_CitySoundProfileRegistry.SelectedSetId);
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

		bool deletingActiveSet = string.Equals(
			s_CitySoundProfileRegistry.ActiveSetId,
			selected,
			StringComparison.OrdinalIgnoreCase);

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
		string selectedSet = EnsureSoundSetExists(
			s_CitySoundProfileRegistry.SelectedSetId,
			s_CitySoundProfileRegistry.SelectedSetId);
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
		return string.Equals(
			s_CitySoundProfileRegistry.SelectedSetId,
			CitySoundProfileRegistry.DefaultSetId,
			StringComparison.OrdinalIgnoreCase);
	}

	// Build multiline status text displayed in the General tab.
	internal static string GetCitySoundSetStatusText()
	{
		string activeLabel = GetSoundSetDisplayName(s_CitySoundProfileRegistry.ActiveSetId);
		string cityLabel = GetCurrentCityLabel();
		string bindingLabel = HasCurrentCityIdentity()
			? GetSoundSetDisplayName(s_CitySoundProfileRegistry.ResolveBoundSetId(s_CurrentCitySaveAssetGuid))
			: "n/a";
		return
			$"Active Set: {activeLabel} ({s_CitySoundProfileRegistry.ActiveSetId})\n" +
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

		string targetSet = EnsureSoundSetExists(setId, setId);
		bool changed = s_CitySoundProfileRegistry.UpsertBinding(
			s_CurrentCitySaveAssetGuid,
			targetSet,
			s_CurrentCityDisplayName,
			string.Empty);
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
		// Ensure on-disk files are current before copying from the active set.
		string normalizedSourceSet = CitySoundProfileRegistry.NormalizeSetId(sourceSetId);
		string normalizedTargetSet = CitySoundProfileRegistry.NormalizeSetId(targetSetId);
		if (string.Equals(normalizedSourceSet, s_CitySoundProfileRegistry.ActiveSetId, StringComparison.OrdinalIgnoreCase))
		{
			SaveConfig();
		}

		DuplicateSirenSettingsFile(normalizedSourceSet, normalizedTargetSet);
		DuplicateDomainSettingsFile(normalizedSourceSet, normalizedTargetSet, VehicleEngineSettingsFileName, VehicleEngineCustomFolderName);
		DuplicateDomainSettingsFile(normalizedSourceSet, normalizedTargetSet, AmbientSettingsFileName, AmbientCustomFolderName);
	}

	private static void DuplicateSirenSettingsFile(string sourceSetId, string targetSetId)
	{
		// Copy siren config file verbatim when present, otherwise seed with defaults.
		string sourcePath = GetSoundSetSettingsPath(sourceSetId, SirenReplacementConfig.SettingsFileName, ensureDirectoryExists: false);
		string targetPath = GetSoundSetSettingsPath(targetSetId, SirenReplacementConfig.SettingsFileName, ensureDirectoryExists: true);
		try
		{
			if (File.Exists(sourcePath))
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
		string sourcePath = GetSoundSetSettingsPath(sourceSetId, settingsFileName, ensureDirectoryExists: false);
		string targetPath = GetSoundSetSettingsPath(targetSetId, settingsFileName, ensureDirectoryExists: true);
		try
		{
			if (File.Exists(sourcePath))
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
	}

	// Expose currently active set ID for runtime systems that need set context.
	internal static string GetActiveSoundSetId()
	{
		return s_CitySoundProfileRegistry.ActiveSetId;
	}
}
