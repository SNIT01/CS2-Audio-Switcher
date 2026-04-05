namespace SirenChanger;

// Entry points used by options actions to open guidance and release-note panels.
public sealed partial class SirenChangerMod
{
	internal static void OpenGuidanceTutorialFromOptions()
	{
		SirenChangerGuidanceUISystem.RequestOpenTutorial();
	}

	internal static void OpenGuidanceChangelogFromOptions()
	{
		SirenChangerGuidanceUISystem.RequestOpenChangelog();
	}
}
