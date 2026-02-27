namespace SirenChanger;

// Module-catalog wrappers shared by preview/runtime systems.
public sealed partial class SirenChangerMod
{
	// Refresh discovered module manifests and in-memory catalogs.
	internal static bool RefreshAudioModuleCatalog()
	{
		return AudioModuleCatalog.Refresh(Log, ModRootPath);
	}

	// Enumerate module-backed selection keys for one audio domain.
	internal static string[] GetAudioModuleProfileKeys(DeveloperAudioDomain domain)
	{
		return AudioModuleCatalog.GetProfileKeys(domain);
	}

	// Read a module profile template used to seed per-selection SFX settings.
	internal static bool TryGetAudioModuleProfileTemplate(DeveloperAudioDomain domain, string profileKey, out SirenSfxProfile profile)
	{
		return AudioModuleCatalog.TryGetProfileTemplate(domain, profileKey, out profile);
	}

	internal static bool TryResolveAudioProfilePath(
		DeveloperAudioDomain domain,
		string localFolderName,
		string profileKey,
		out string filePath)
	{
		filePath = string.Empty;
		// Module selections take precedence; local custom folders are the fallback.
		if (AudioModuleCatalog.TryGetFilePath(domain, profileKey, out filePath))
		{
			return true;
		}

		return SirenPathUtils.TryGetCustomSirenFilePath(SettingsDirectory, localFolderName, profileKey, out filePath);
	}

	// Resolve user-facing display labels for module-backed selections.
	internal static bool TryGetAudioModuleDisplayName(string profileKey, out string displayName)
	{
		return AudioModuleCatalog.TryGetDisplayName(profileKey, out displayName);
	}
}
