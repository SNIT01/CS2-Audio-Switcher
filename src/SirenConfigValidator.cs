using System;
using System.Collections.Generic;
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
	// Validate current config and produce a multiline report for the user.
	public static SirenValidationResult Validate(SirenReplacementConfig config, string settingsDirectory)
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

			// Validate one selected siren reference from key -> profile -> file.
			void ValidateSelection(string label, string selectionKey)
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

			if (!config.TryGetProfile(normalized, out SirenSfxProfile profile))
			{
				AddError($"{label} selection '{normalized}' has no profile.");
				return;
			}

			SirenSfxProfile clamped = profile.ClampCopy();
			if (!profile.ApproximatelyEquals(clamped))
			{
				AddWarning($"{label} profile '{normalized}' has out-of-range values that will be clamped.");
			}

				if (!SirenChangerMod.TryResolveAudioProfilePath(DeveloperAudioDomain.Siren, config.CustomSirensFolderName, normalized, out string filePath))
				{
					AddError($"{label} selection '{normalized}' file was not found.");
					return;
				}

				string extension = System.IO.Path.GetExtension(filePath);
				if (!SirenPathUtils.IsSupportedCustomSirenExtension(extension))
				{
					AddError($"{label} selection '{normalized}' has unsupported format '{extension}'.");
					return;
				}

				System.IO.FileInfo info = new System.IO.FileInfo(filePath);
				if (!info.Exists)
				{
					AddError($"{label} selection '{normalized}' file does not exist.");
					return;
				}

				if (info.Length <= 0)
				{
					AddError($"{label} selection '{normalized}' file is empty.");
				}
			}

			// Validate all per-region emergency vehicle selections.
			ValidateSelection("Police NA", config.GetSelection(EmergencySirenVehicleType.Police, SirenRegion.NorthAmerica));
			ValidateSelection("Police EU", config.GetSelection(EmergencySirenVehicleType.Police, SirenRegion.Europe));
			ValidateSelection("Fire Truck NA", config.GetSelection(EmergencySirenVehicleType.Fire, SirenRegion.NorthAmerica));
		ValidateSelection("Fire Truck EU", config.GetSelection(EmergencySirenVehicleType.Fire, SirenRegion.Europe));
		ValidateSelection("Ambulance NA", config.GetSelection(EmergencySirenVehicleType.Ambulance, SirenRegion.NorthAmerica));
		ValidateSelection("Ambulance EU", config.GetSelection(EmergencySirenVehicleType.Ambulance, SirenRegion.Europe));

		List<string> specificVehicleOverrides = config.VehiclePrefabSelections.Keys
			.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
			.ToList();
		for (int i = 0; i < specificVehicleOverrides.Count; i++)
		{
			string vehiclePrefab = specificVehicleOverrides[i];
			ValidateSelection($"Specific vehicle '{vehiclePrefab}'", config.GetVehiclePrefabSelection(vehiclePrefab));
		}

		if (config.MissingSirenFallbackBehavior == SirenFallbackBehavior.AlternateCustomSiren)
		{
			if (SirenReplacementConfig.IsDefaultSelection(config.AlternateFallbackSelection))
			{
				AddWarning("Fallback is set to Alternate Custom Siren, but Alternate Siren is set to Default.");
			}
			else
			{
				ValidateSelection("Alternate fallback", config.AlternateFallbackSelection);
				}
			}

			// Flag stale profile entries so the user can clean up removed files.
			List<string> knownKeys = config.CustomSirenProfiles.Keys
				.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
				.ToList();
		for (int i = 0; i < knownKeys.Count; i++)
		{
			string key = knownKeys[i];
			if (!SirenChangerMod.TryResolveAudioProfilePath(DeveloperAudioDomain.Siren, config.CustomSirensFolderName, key, out _))
			{
				AddWarning($"Profile '{key}' is registered but its file is missing.");
				}
			}

			// Build a compact human-readable report for options UI.
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

