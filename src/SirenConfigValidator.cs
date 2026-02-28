using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SirenChanger;

// Aggregated validation result shown in the options diagnostics panel.
internal sealed class SirenValidationResult
{
	public int ErrorCount { get; set; }

	public int WarningCount { get; set; }

	public string ReportText { get; set; } = string.Empty;
}

// Lightweight configuration/file checks that avoid mutating runtime audio state.
internal static class SirenConfigValidator
{
	// Validate all configurable audio domains and produce a multiline report for the user.
	public static SirenValidationResult Validate(
		SirenReplacementConfig sirenConfig,
		AudioReplacementDomainConfig vehicleEngineConfig,
		AudioReplacementDomainConfig ambientConfig,
		AudioReplacementDomainConfig transitAnnouncementConfig,
		string settingsDirectory)
	{
		SirenValidationResult result = new SirenValidationResult();
		List<string> lines = new List<string>();

		void AddError(string message)
		{
			result.ErrorCount++;
			lines.Add($"ERROR: {message}");
		}

		void AddWarning(string message)
		{
			result.WarningCount++;
			lines.Add($"WARNING: {message}");
		}

		// Shared file/profile validation for one non-default siren selection.
		void ValidateSirenSelection(string label, string selectionKey)
		{
			if (SirenReplacementConfig.IsDefaultSelection(selectionKey))
			{
				return;
			}

			string normalized = SirenPathUtils.NormalizeProfileKey(selectionKey);
			if (string.IsNullOrWhiteSpace(normalized))
			{
				AddError($"{label} selection is invalid: '{selectionKey}'.");
				return;
			}

			if (!sirenConfig.TryGetProfile(normalized, out SirenSfxProfile profile))
			{
				AddError($"{label} selection '{normalized}' has no profile.");
				return;
			}

			SirenSfxProfile clamped = profile.ClampCopy();
			if (!profile.ApproximatelyEquals(clamped))
			{
				AddWarning($"{label} profile '{normalized}' has out-of-range values that will be clamped.");
			}

			if (!SirenChangerMod.TryResolveAudioProfilePath(
				DeveloperAudioDomain.Siren,
				sirenConfig.CustomSirensFolderName,
				normalized,
				out string filePath))
			{
				AddError($"{label} selection '{normalized}' file was not found.");
				return;
			}

			ValidateAudioFile(label, normalized, filePath);
		}

		// Shared file/profile validation for one non-default generic-domain selection.
		void ValidateDomainSelection(
			string label,
			DeveloperAudioDomain domain,
			AudioReplacementDomainConfig domainConfig,
			string selectionKey)
		{
			if (AudioReplacementDomainConfig.IsDefaultSelection(selectionKey))
			{
				return;
			}

			string normalized = AudioReplacementDomainConfig.NormalizeProfileKey(selectionKey);
			if (string.IsNullOrWhiteSpace(normalized))
			{
				AddError($"{label} selection is invalid: '{selectionKey}'.");
				return;
			}

			if (!domainConfig.TryGetProfile(normalized, out SirenSfxProfile profile))
			{
				AddError($"{label} selection '{normalized}' has no profile.");
				return;
			}

			SirenSfxProfile clamped = profile.ClampCopy();
			if (!profile.ApproximatelyEquals(clamped))
			{
				AddWarning($"{label} profile '{normalized}' has out-of-range values that will be clamped.");
			}

			if (!SirenChangerMod.TryResolveAudioProfilePath(
				domain,
				domainConfig.CustomFolderName,
				normalized,
				out string filePath))
			{
				AddError($"{label} selection '{normalized}' file was not found.");
				return;
			}

			ValidateAudioFile(label, normalized, filePath);
		}

		// Validate on-disk file format and size once path resolution succeeded.
		void ValidateAudioFile(string label, string normalizedSelection, string filePath)
		{
			string extension = Path.GetExtension(filePath);
			if (!SirenPathUtils.IsSupportedCustomSirenExtension(extension))
			{
				AddError($"{label} selection '{normalizedSelection}' has unsupported format '{extension}'.");
				return;
			}

			FileInfo info = new FileInfo(filePath);
			if (!info.Exists)
			{
				AddError($"{label} selection '{normalizedSelection}' file does not exist.");
				return;
			}

			if (info.Length <= 0)
			{
				AddError($"{label} selection '{normalizedSelection}' file is empty.");
			}
		}

		// Validate generic-domain defaults/targets/fallbacks plus stale profiles.
		void ValidateGenericDomain(
			string domainLabel,
			DeveloperAudioDomain domain,
			AudioReplacementDomainConfig domainConfig)
		{
			ValidateDomainSelection($"{domainLabel} default", domain, domainConfig, domainConfig.DefaultSelection);

			List<string> targetKeys = domainConfig.TargetSelections.Keys
				.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
				.ToList();
			for (int i = 0; i < targetKeys.Count; i++)
			{
				string target = targetKeys[i];
				ValidateDomainSelection(
					$"{domainLabel} target '{target}'",
					domain,
					domainConfig,
					domainConfig.GetTargetSelection(target));
			}

			if (domainConfig.MissingSelectionFallbackBehavior == SirenFallbackBehavior.AlternateCustomSiren)
			{
				if (AudioReplacementDomainConfig.IsDefaultSelection(domainConfig.AlternateFallbackSelection))
				{
					AddWarning($"{domainLabel} fallback is set to Alternate Custom Sound File, but Alternate is set to Default.");
				}
				else
				{
					ValidateDomainSelection(
						$"{domainLabel} alternate fallback",
						domain,
						domainConfig,
						domainConfig.AlternateFallbackSelection);
				}
			}

			List<string> knownKeys = domainConfig.CustomProfiles.Keys
				.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
				.ToList();
			for (int i = 0; i < knownKeys.Count; i++)
			{
				string key = knownKeys[i];
				if (!SirenChangerMod.TryResolveAudioProfilePath(domain, domainConfig.CustomFolderName, key, out _))
				{
					AddWarning($"{domainLabel} profile '{key}' is registered but its file is missing.");
				}
			}
		}

		ValidateSirenSelection("Police NA", sirenConfig.GetSelection(EmergencySirenVehicleType.Police, SirenRegion.NorthAmerica));
		ValidateSirenSelection("Police EU", sirenConfig.GetSelection(EmergencySirenVehicleType.Police, SirenRegion.Europe));
		ValidateSirenSelection("Fire Truck NA", sirenConfig.GetSelection(EmergencySirenVehicleType.Fire, SirenRegion.NorthAmerica));
		ValidateSirenSelection("Fire Truck EU", sirenConfig.GetSelection(EmergencySirenVehicleType.Fire, SirenRegion.Europe));
		ValidateSirenSelection("Ambulance NA", sirenConfig.GetSelection(EmergencySirenVehicleType.Ambulance, SirenRegion.NorthAmerica));
		ValidateSirenSelection("Ambulance EU", sirenConfig.GetSelection(EmergencySirenVehicleType.Ambulance, SirenRegion.Europe));

		List<string> specificVehicleOverrides = sirenConfig.VehiclePrefabSelections.Keys
			.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
			.ToList();
		for (int i = 0; i < specificVehicleOverrides.Count; i++)
		{
			string vehiclePrefab = specificVehicleOverrides[i];
			ValidateSirenSelection($"Specific vehicle '{vehiclePrefab}'", sirenConfig.GetVehiclePrefabSelection(vehiclePrefab));
		}

		if (sirenConfig.MissingSirenFallbackBehavior == SirenFallbackBehavior.AlternateCustomSiren)
		{
			if (SirenReplacementConfig.IsDefaultSelection(sirenConfig.AlternateFallbackSelection))
			{
				AddWarning("Siren fallback is set to Alternate Custom Siren, but Alternate Siren is set to Default.");
			}
			else
			{
				ValidateSirenSelection("Siren alternate fallback", sirenConfig.AlternateFallbackSelection);
			}
		}

		List<string> knownSirenKeys = sirenConfig.CustomSirenProfiles.Keys
			.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
			.ToList();
		for (int i = 0; i < knownSirenKeys.Count; i++)
		{
			string key = knownSirenKeys[i];
			if (!SirenChangerMod.TryResolveAudioProfilePath(DeveloperAudioDomain.Siren, sirenConfig.CustomSirensFolderName, key, out _))
			{
				AddWarning($"Siren profile '{key}' is registered but its file is missing.");
			}
		}

		ValidateGenericDomain("Vehicle engine", DeveloperAudioDomain.VehicleEngine, vehicleEngineConfig);
		ValidateGenericDomain("Ambient", DeveloperAudioDomain.Ambient, ambientConfig);
		ValidateGenericDomain("Transit announcement", DeveloperAudioDomain.TransitAnnouncement, transitAnnouncementConfig);

		StringBuilder reportBuilder = new StringBuilder();
		reportBuilder.Append("Errors: ").Append(result.ErrorCount)
			.Append(", Warnings: ").Append(result.WarningCount).Append('\n');

		if (lines.Count == 0)
		{
			reportBuilder.Append("No issues found.");
		}
		else
		{
			for (int i = 0; i < lines.Count; i++)
			{
				reportBuilder.Append("- ").Append(lines[i]).Append('\n');
			}
		}

		reportBuilder.Append("Supported formats: ").Append(SirenPathUtils.GetSupportedCustomSirenExtensionsLabel()).Append('.');
		result.ReportText = reportBuilder.ToString().TrimEnd();
		return result;
	}
}
