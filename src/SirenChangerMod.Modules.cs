namespace SirenChanger;

// Module-catalog wrappers shared by preview/runtime systems.
public sealed partial class SirenChangerMod
{
	internal static bool RefreshAudioModuleCatalog()
	{
		return AudioModuleCatalog.Refresh(Log, ModRootPath);
	}

	internal static string[] GetAudioModuleProfileKeys(DeveloperAudioDomain domain)
	{
		return AudioModuleCatalog.GetProfileKeys(domain);
	}

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
		if (AudioModuleCatalog.TryGetFilePath(domain, profileKey, out filePath))
		{
			return true;
		}

		return SirenPathUtils.TryGetCustomSirenFilePath(SettingsDirectory, localFolderName, profileKey, out filePath);
	}

	internal static bool TryGetAudioModuleDisplayName(string profileKey, out string displayName)
	{
		return AudioModuleCatalog.TryGetDisplayName(profileKey, out displayName);
	}
}
