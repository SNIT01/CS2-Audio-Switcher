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

	// Enumerate module-provided city sound-set profile keys.
	internal static string[] GetAudioModuleSoundSetProfileKeys()
	{
		return AudioModuleCatalog.GetSoundSetProfileKeys();
	}

	// Read a module profile template used to seed per-selection SFX settings.
	internal static bool TryGetAudioModuleProfileTemplate(DeveloperAudioDomain domain, string profileKey, out SirenSfxProfile profile)
	{
		return AudioModuleCatalog.TryGetProfileTemplate(domain, profileKey, out profile);
	}

	// Resolve user-facing display labels for module-provided city sound-set profiles.
	internal static bool TryGetAudioModuleSoundSetProfileDisplayName(string profileKey, out string displayName)
	{
		return AudioModuleCatalog.TryGetSoundSetProfileDisplayName(profileKey, out displayName);
	}

	// Resolve one module-provided city sound-set settings file.
	internal static bool TryGetAudioModuleSoundSetProfileSettingsFilePath(string profileKey, string fileName, out string filePath)
	{
		return AudioModuleCatalog.TryGetSoundSetProfileSettingsFilePath(profileKey, fileName, out filePath);
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
