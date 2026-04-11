using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using Colossal.Logging;
using Colossal.UI.Binding;
using Game;
using Game.SceneFlow;
using Game.UI;
using Unity.Entities;
using UnityEngine.Scripting;

namespace SirenChanger;

// Gameface binding surface for first-run tutorial and release changelog panels.
internal sealed partial class SirenChangerGuidanceUISystem : UISystemBase
{
	private const string kGroup = "sirenChangerGuidance";

	private const string kGuidanceStateFileName = "SirenChangerGuidanceState.json";

	private const string kTutorialTitle = "Audio Switcher Quick Start";

	private const string kChangelogTitlePrefix = "What's New in Audio Switcher";

	private static readonly string s_CurrentReleaseVersion = "2.5.2";

	private static readonly GuidanceTutorialPage[] s_TutorialPages = BuildTutorialPages();

	private static readonly GuidanceReleaseEntry[] s_ChangelogReleases = BuildChangelogReleases();

	private static readonly string s_TutorialPagesJson = SerializeJson(s_TutorialPages);

	private static readonly string s_ChangelogReleasesJson = SerializeJson(s_ChangelogReleases);

	private static readonly string s_TutorialBody = BuildTutorialFallbackBody(s_TutorialPages);

	private static readonly string s_ChangelogBody = BuildCurrentChangelogFallbackBody(s_ChangelogReleases);

	private static readonly string s_CurrentChangelogSignature = BuildCurrentChangelogSignature();

	private static readonly object s_RequestSync = new object();

	private static bool s_OpenTutorialRequested;

	private static bool s_OpenChangelogRequested;

	private ValueBinding<bool> m_TutorialVisibleBinding = null!;

	private ValueBinding<bool> m_ChangelogVisibleBinding = null!;

	private ValueBinding<string> m_TutorialTitleBinding = null!;

	private ValueBinding<string> m_TutorialBodyBinding = null!;

	private ValueBinding<string> m_TutorialPagesBinding = null!;

	private ValueBinding<string> m_ChangelogTitleBinding = null!;

	private ValueBinding<string> m_ChangelogVersionBinding = null!;

	private ValueBinding<string> m_ChangelogBodyBinding = null!;

	private ValueBinding<string> m_ChangelogReleasesBinding = null!;

	private SirenChangerGuidanceState m_State = SirenChangerGuidanceState.CreateDefault();

	private string m_StatePath = string.Empty;

	private bool m_AutoShowTutorialPending;

	private bool m_AutoShowChangelogPending;

	private ChangelogOpenMode m_ChangelogOpenMode;

	internal static void RequestOpenTutorial()
	{
		EnsureSystemCreated();
		lock (s_RequestSync)
		{
			s_OpenTutorialRequested = true;
		}
	}

	internal static void RequestOpenChangelog()
	{
		EnsureSystemCreated();
		lock (s_RequestSync)
		{
			s_OpenChangelogRequested = true;
		}
	}

	[Preserve]
	protected override void OnCreate()
	{
		base.OnCreate();
		m_StatePath = ResolveStatePath();
		m_State = SirenChangerGuidanceState.LoadOrCreateFromPath(m_StatePath, SirenChangerMod.Log);

		bool showTutorial = ShouldAutoShowTutorial(m_State);
		bool showChangelog = ShouldAutoShowChangelog(m_State);
		m_AutoShowTutorialPending = showTutorial;
		m_AutoShowChangelogPending = showChangelog;
		m_ChangelogOpenMode = showTutorial && showChangelog
			? ChangelogOpenMode.AutoAfterTutorial
			: ChangelogOpenMode.None;

		// Guidance panels are shown only in a loaded gameplay session.
		AddBinding(m_TutorialVisibleBinding = new ValueBinding<bool>(kGroup, "tutorialVisible", false));
		AddBinding(m_ChangelogVisibleBinding = new ValueBinding<bool>(kGroup, "changelogVisible", false));
		AddBinding(m_TutorialTitleBinding = new ValueBinding<string>(kGroup, "tutorialTitle", kTutorialTitle));
		AddBinding(m_TutorialBodyBinding = new ValueBinding<string>(kGroup, "tutorialBody", s_TutorialBody));
		AddBinding(m_TutorialPagesBinding = new ValueBinding<string>(kGroup, "tutorialPages", s_TutorialPagesJson));
		AddBinding(m_ChangelogTitleBinding = new ValueBinding<string>(kGroup, "changelogTitle", kChangelogTitlePrefix));
		AddBinding(m_ChangelogVersionBinding = new ValueBinding<string>(kGroup, "changelogVersion", s_CurrentReleaseVersion));
		AddBinding(m_ChangelogBodyBinding = new ValueBinding<string>(kGroup, "changelogBody", s_ChangelogBody));
		AddBinding(m_ChangelogReleasesBinding = new ValueBinding<string>(kGroup, "changelogReleases", s_ChangelogReleasesJson));
		AddBinding(new TriggerBinding(kGroup, "closeTutorial", CloseTutorial));
		AddBinding(new TriggerBinding(kGroup, "closeTutorialDontShowAgain", CloseTutorialDontShowAgain));
		AddBinding(new TriggerBinding(kGroup, "closeChangelog", CloseChangelog));
		AddBinding(new TriggerBinding(kGroup, "openTutorial", OpenTutorial));
		AddBinding(new TriggerBinding(kGroup, "openChangelog", OpenChangelog));
	}

	[Preserve]
	protected override void OnUpdate()
	{
		if (!IsGameplaySessionReady())
		{
			HideGuidancePanelsWhileOutOfGame();
			base.OnUpdate();
			return;
		}

		TryShowPendingAutoPanels();
		ConsumeOpenRequests();
		base.OnUpdate();
	}

	private void OpenTutorial()
	{
		OpenTutorialInternal();
	}

	private void OpenChangelog()
	{
		OpenChangelogInternal();
	}

	private void CloseTutorial()
	{
		CloseTutorialInternal(dontShowAgain: false);
	}

	private void CloseTutorialDontShowAgain()
	{
		CloseTutorialInternal(dontShowAgain: true);
	}

	private void CloseTutorialInternal(bool dontShowAgain)
	{
		bool changed = false;
		bool wasSeenBefore = m_State.HasSeenTutorial;
		if (!m_State.HasSeenTutorial)
		{
			m_State.HasSeenTutorial = true;
			changed = true;
		}

		if (dontShowAgain)
		{
			if (!m_State.SuppressTutorial)
			{
				m_State.SuppressTutorial = true;
				changed = true;
			}

			if (m_State.RepeatTutorialUntilSuppressed)
			{
				m_State.RepeatTutorialUntilSuppressed = false;
				changed = true;
			}
		}
		else if (!wasSeenBefore && !m_State.RepeatTutorialUntilSuppressed)
		{
			// New users keep seeing the tutorial until they explicitly disable it.
			m_State.RepeatTutorialUntilSuppressed = true;
			changed = true;
		}

		if (changed)
		{
			SaveState();
		}

		if (m_TutorialVisibleBinding.value)
		{
			m_TutorialVisibleBinding.Update(newValue: false);
		}

		if (m_ChangelogOpenMode != ChangelogOpenMode.None)
		{
			bool shouldOpenChangelog = m_ChangelogOpenMode == ChangelogOpenMode.ForcedAfterTutorial || ShouldAutoShowChangelog(m_State);
			m_ChangelogOpenMode = ChangelogOpenMode.None;
			if (shouldOpenChangelog)
			{
				m_ChangelogVisibleBinding.Update(newValue: true);
			}
		}
	}

	private void CloseChangelog()
	{
		if (m_ChangelogVisibleBinding.value)
		{
			m_ChangelogVisibleBinding.Update(newValue: false);
		}

		MarkChangelogSeen();
	}

	private void OpenTutorialInternal(bool clearChangelogMode = true)
	{
		if (clearChangelogMode)
		{
			m_ChangelogOpenMode = ChangelogOpenMode.None;
		}

		if (m_ChangelogVisibleBinding.value)
		{
			m_ChangelogVisibleBinding.Update(newValue: false);
		}

		if (!m_TutorialVisibleBinding.value)
		{
			m_TutorialVisibleBinding.Update(newValue: true);
		}
	}

	private void OpenChangelogInternal()
	{
		if (m_TutorialVisibleBinding.value)
		{
			m_ChangelogOpenMode = ChangelogOpenMode.ForcedAfterTutorial;
			return;
		}

		m_ChangelogOpenMode = ChangelogOpenMode.None;
		if (!m_ChangelogVisibleBinding.value)
		{
			m_ChangelogVisibleBinding.Update(newValue: true);
		}
	}

	private void TryShowPendingAutoPanels()
	{
		if (m_AutoShowTutorialPending)
		{
			m_AutoShowTutorialPending = false;
			OpenTutorialInternal(clearChangelogMode: false);
			return;
		}

		if (m_AutoShowChangelogPending)
		{
			m_AutoShowChangelogPending = false;
			OpenChangelogInternal();
		}
	}

	private static bool IsGameplaySessionReady()
	{
		GameManager? manager = GameManager.instance;
		return manager != null &&
			!manager.isGameLoading &&
			manager.gameMode == GameMode.Game;
	}

	private void HideGuidancePanelsWhileOutOfGame()
	{
		if (m_TutorialVisibleBinding.value)
		{
			m_TutorialVisibleBinding.Update(newValue: false);
		}

		if (m_ChangelogVisibleBinding.value)
		{
			m_ChangelogVisibleBinding.Update(newValue: false);
		}
	}

	private void ConsumeOpenRequests()
	{
		bool openTutorial;
		bool openChangelog;
		lock (s_RequestSync)
		{
			openTutorial = s_OpenTutorialRequested;
			openChangelog = s_OpenChangelogRequested;
			s_OpenTutorialRequested = false;
			s_OpenChangelogRequested = false;
		}

		if (openTutorial)
		{
			OpenTutorialInternal();
		}

		if (openChangelog)
		{
			OpenChangelogInternal();
		}
	}

	private void MarkChangelogSeen()
	{
		if (string.Equals(m_State.LastSeenChangelogVersion, s_CurrentChangelogSignature, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}

		m_State.LastSeenChangelogVersion = s_CurrentChangelogSignature;
		SaveState();
	}

	private void SaveState()
	{
		SirenChangerGuidanceState.Save(m_StatePath, m_State, SirenChangerMod.Log);
	}

	private static bool ShouldAutoShowTutorial(SirenChangerGuidanceState state)
	{
		if (state.SuppressTutorial)
		{
			return false;
		}

		if (!state.HasSeenTutorial)
		{
			return true;
		}

		return state.RepeatTutorialUntilSuppressed;
	}

	private static bool ShouldAutoShowChangelog(SirenChangerGuidanceState state)
	{
		return !string.Equals(state.LastSeenChangelogVersion, s_CurrentChangelogSignature, StringComparison.OrdinalIgnoreCase);
	}

	private static string ResolveStatePath()
	{
		string settingsDirectory = SirenChangerMod.SettingsDirectory;
		if (string.IsNullOrWhiteSpace(settingsDirectory))
		{
			settingsDirectory = SirenPathUtils.GetSettingsDirectory(ensureExists: true);
		}
		else
		{
			Directory.CreateDirectory(settingsDirectory);
		}

		return Path.Combine(settingsDirectory, kGuidanceStateFileName);
	}

	private static void EnsureSystemCreated()
	{
		World? world = World.DefaultGameObjectInjectionWorld;
		if (world == null)
		{
			return;
		}

		world.GetOrCreateSystemManaged<SirenChangerGuidanceUISystem>();
	}

	private static string ResolveCurrentReleaseVersion()
	{
		try
		{
			System.Version? version = typeof(SirenChangerMod).Assembly.GetName().Version;
			if (version != null)
			{
				int major = Math.Max(0, version.Major);
				int minor = Math.Max(0, version.Minor);
				int patch = Math.Max(0, version.Build);
				return $"{major}.{minor}.{patch}";
			}
		}
		catch
		{
			// Fall through to default.
		}

		return "0.0.0";
	}

	private static string BuildCurrentChangelogSignature()
	{
		string payload = $"{s_CurrentReleaseVersion}\n{s_ChangelogReleasesJson}";
		try
		{
			byte[] bytes = Encoding.UTF8.GetBytes(payload);
			using SHA256 sha = SHA256.Create();
			byte[] hash = sha.ComputeHash(bytes);
			StringBuilder builder = new StringBuilder(16);
			for (int i = 0; i < 8 && i < hash.Length; i++)
			{
				builder.Append(hash[i].ToString("x2"));
			}

			return builder.ToString();
		}
		catch
		{
			return payload;
		}
	}

	private static GuidanceTutorialPage[] BuildTutorialPages()
	{
		return new[]
		{
			new GuidanceTutorialPage
			{
				Title = "What Audio Switcher Covers",
				ImagePath = "Images/Screen_Gen.jpg",
				Body = string.Join(
					"\n",
					"Audio Switcher manages multiple audio systems from one mod.",
					"",
					"- Sirens, Vehicle Engines, Ambient Sounds, Building Sounds, Public Transport Announcements, and City Sound Sets.",
					"- Open Options > Audio Switcher and use the tab that matches the audio domain you want to edit.",
					"- Use the General tab for City Sound Sets plus Quick Start and What's New guidance panels.",
					"- Use the Developer tab for runtime diagnostics and module build/upload workflows.")
			},
			new GuidanceTutorialPage
			{
				Title = "Sirens: Defaults and Vehicle Overrides",
				ImagePath = "Images/Screen_Siren.jpg",
				Body = string.Join(
					"\n",
					"- Set regional defaults for Police, Fire, and Ambulance (NA and EU).",
					"- Apply per-prefab siren overrides when a specific vehicle needs a unique sound.",
					"- Use Rescan Custom Siren Files and Rescan Emergency Vehicle Prefabs when content changes.",
					"- Use the Siren Profile Editor to preview, copy, and tune SFX parameters.",
					"- Run Siren Setup Validation and review Validation Report before shipping a configuration.",
					"- Optional debug detection reports can be enabled in Siren Diagnostics for deeper troubleshooting.")
			},
			new GuidanceTutorialPage
			{
				Title = "Vehicle Engines",
				ImagePath = "Images/Screen_Engine.jpg",
				Body = string.Join(
					"\n",
					"- Enable Vehicle Engine Replacement to apply custom engine audio.",
					"- Set an engine default and optional per-vehicle engine overrides.",
					"- Rescan Custom Engine Files and Vehicle Engine Prefabs to refresh selectable targets.",
					"- Use Engine Profile Editor preview/copy/reset controls for detailed tuning.")
			},
			new GuidanceTutorialPage
			{
				Title = "Ambient Sounds",
				ImagePath = "Images/Screen_Ambient.jpg",
				Body = string.Join(
					"\n",
					"- Enable Ambient Replacement to swap map ambience and effect loops.",
					"- Set a global ambient default and optional per-target ambient overrides.",
					"- Rescan Custom Ambient Files and Ambient Targets after adding content or changing saves.",
					"- Use Ambient Profile Editor controls to tune distance, fades, loop, pitch, and more.")
			},
			new GuidanceTutorialPage
			{
				Title = "Building Sounds",
				ImagePath = "Images/Screen_Dev.jpg",
				Body = string.Join(
					"\n",
					"- Enable Building Replacement to apply custom audio across detected buildings with sound.",
					"- Set a building default and optional per-building overrides for specific structures.",
					"- Use Mute Building Targets to silence detected building targets while keeping assignments intact.",
					"- Rescan Custom Building Files and Building Targets after adding files or loading a different city.",
					"- Use Building Profile Editor preview/copy/reset controls for detailed tuning.")
			},
			new GuidanceTutorialPage
			{
				Title = "Public Transport Announcements",
				ImagePath = "Images/Screen_PT.jpg",
				Body = string.Join(
					"\n",
					"- Announcements are configured per station and per line (arrival and departure independently).",
					"- Set global transit volume and min/max distance for announcement playback.",
					"- Use the Line Service selector to filter station-line editing by train, bus, metro, tram, or ferry.",
					"- Run Scan Transit Lines in a loaded city to discover stations, lines, and station-line pairs.",
					"- Use Prune Stale Lines to clean obsolete discovered entries that are no longer active.")
			},
			new GuidanceTutorialPage
			{
				Title = "Profiles and Advanced Tuning",
				ImagePath = "Images/Screen_Prof.jpg",
				Body = string.Join(
					"\n",
					"- Every audio domain supports editable profiles with preview and status feedback.",
					"- Copy settings from another profile, then fine-tune volume, pitch, spatial blend, doppler, spread, loop, and rolloff.",
					"- Distance and fade controls (min/max, fade-in, fade-out, random start) are available for precise behavior.",
					"- Reset actions restore template values captured from detected in-game SFX.",
					"- Module-provided profiles are read-only templates and should be copied before editing.")
			},
			new GuidanceTutorialPage
			{
				Title = "Missing File Fallback Behavior",
				ImagePath = "Images/Screen_Siren2.jpg",
				Body = string.Join(
					"\n",
					"- Each domain supports fallback handling when a selected clip cannot be loaded.",
					"- Choose behavior such as using default behavior, muting, or using an alternate selection.",
					"- Configure Alternate selections only when alternate fallback mode is enabled.")
			},
			new GuidanceTutorialPage
			{
				Title = "City Sound Sets and City Mapping",
				ImagePath = "Images/Screen_Gen.jpg",
				Body = string.Join(
					"\n",
					"- Save complete presets as City Sound Sets, then activate, update, duplicate, rename, or delete local sets.",
					"- Bind a saved city to a selected set and optionally auto-apply that mapping on load.",
					"- Module-provided sets are available to use but do not auto-apply when installed.",
					"- Module-provided sets are read-only templates: they can be activated or copied, but not edited/deleted in place.")
			},
			new GuidanceTutorialPage
			{
				Title = "Module Builder",
				ImagePath = "Images/Screen_Dev.jpg",
				Body = string.Join(
					"\n",
					"- Set Module Display Name (spaces allowed), Module ID (supports periods), and Module Version (numbers + periods).",
					"- Build modules from selected local Siren, Engine, Ambient, Transit, and optional Sound Set profile content.",
					"- Use Select All / Clear actions to manage inclusion lists quickly.",
					"- Build Local creates a local package for testing.",
					"- Build + Upload creates an asset package for PDX Mods publishing.")
			},
			new GuidanceTutorialPage
			{
				Title = "PDX Upload Workflow",
				ImagePath = "Images/Screen_Dev2.jpg",
				Body = string.Join(
					"\n",
					"- Build + Upload uses the asset upload path and requires confirmation before upload starts.",
					"- Choose Visibility (Public/Private/Unlisted) and Publish Mode (Create New or Update Existing).",
					"- For Update Existing, enter the Existing Mod ID of the published module.",
					"- Set page description, optional additional dependencies, and thumbnail directory/selection.",
					"- Run Refresh Thumbnails and review Pipeline/Build/Upload status fields for diagnostics.",
					"- Audio Switcher dependency is always injected automatically for auto-uploaded modules.")
			},
			new GuidanceTutorialPage
			{
				Title = "Developer and Diagnostics",
				ImagePath = "Images/Screen_Dev3.jpg",
				Body = string.Join(
					"\n",
					"- Developer tab provides detected runtime Siren/Engine/Ambient/Building source lists with live preview.",
					"- Read-only SFX parameter views help validate what the game is actually playing.",
					"- Scan status, validation status, override status, preview status, and upload status fields provide troubleshooting signals.",
					"- Use status output before rescanning, rebuilding modules, or changing fallback behavior.")
			}
		};
	}

	private static GuidanceReleaseEntry[] BuildChangelogReleases()
	{
		List<GuidanceReleaseEntry> releases = new List<GuidanceReleaseEntry>
		{
			new GuidanceReleaseEntry
			{
				Version = "2.5.2",
				Title = "Released 2026-04-09",
				Body = string.Join(
					"\n",
					"- Fixed broken UI images.",
					"- Fixed \"Don't show again\" tick box.",
					"- Performance pass.")
			},
			new GuidanceReleaseEntry
			{
				Version = "2.5.0",
				Title = "Released 2026-04-05",
				Body = string.Join(
					"\n",
					"- New Buildings tab with building SFX.",
					"- Profiles are now supported in modules.",
					"- Public transport announcements now work on a per-station and per-line basis.",
					"- New guide UI.",
					"- Other quality of life improvements.")
			},
			new GuidanceReleaseEntry
			{
				Version = "2.1.0",
				Title = "Released 2026-03-30",
				Body = string.Join(
					"\n",
					"- Added Ferry to public transport announcements.",
					"- Fixed issues with Audio Switcher not being correctly set as dependency.",
					"- Added the ability for users to set version numbers and firmed up cross-update persistence.",
					"- Bug fixes.")
			},
			new GuidanceReleaseEntry
			{
				Version = "2.0.1",
				Title = "Released 2026-03-29",
				Body = string.Join(
					"\n",
					"- Bug fixes.")
			}
		};

		return releases.ToArray();
	}

	private static string SerializeJson<T>(T value)
	{
		try
		{
			return JsonDataSerializer.Serialize(value);
		}
		catch
		{
			return string.Empty;
		}
	}

	private static string BuildTutorialFallbackBody(IReadOnlyList<GuidanceTutorialPage> pages)
	{
		if (pages == null || pages.Count == 0)
		{
			return string.Empty;
		}

		StringBuilder builder = new StringBuilder();
		for (int i = 0; i < pages.Count; i++)
		{
			GuidanceTutorialPage page = pages[i] ?? new GuidanceTutorialPage();
			if (!string.IsNullOrWhiteSpace(page.Title))
			{
				builder.Append(page.Title.Trim());
				builder.Append('\n');
			}

			if (!string.IsNullOrWhiteSpace(page.Body))
			{
				builder.Append(page.Body.Trim());
			}

			if (i + 1 < pages.Count)
			{
				builder.Append('\n');
				builder.Append('\n');
			}
		}

		return builder.ToString().Trim();
	}

	private static string BuildCurrentChangelogFallbackBody(IReadOnlyList<GuidanceReleaseEntry> releases)
	{
		if (releases == null || releases.Count == 0)
		{
			return string.Empty;
		}

		string body = releases[0]?.Body ?? string.Empty;
		return body.Trim();
	}

	[Preserve]
	public SirenChangerGuidanceUISystem()
	{
	}

	private enum ChangelogOpenMode
	{
		None = 0,
		AutoAfterTutorial = 1,
		ForcedAfterTutorial = 2
	}

	[DataContract]
	private sealed class GuidanceTutorialPage
	{
		[DataMember(Order = 1)]
		public string Title { get; set; } = string.Empty;

		[DataMember(Order = 2)]
		public string Body { get; set; } = string.Empty;

		[DataMember(Order = 3)]
		public string ImagePath { get; set; } = string.Empty;
	}

	[DataContract]
	private sealed class GuidanceReleaseEntry
	{
		[DataMember(Order = 1)]
		public string Version { get; set; } = string.Empty;

		[DataMember(Order = 2)]
		public string Title { get; set; } = string.Empty;

		[DataMember(Order = 3)]
		public string Body { get; set; } = string.Empty;
	}
}

[DataContract]
internal sealed class SirenChangerGuidanceState
{
	[DataMember(Order = 1)]
	public bool HasSeenTutorial { get; set; }

	[DataMember(Order = 2)]
	public bool SuppressTutorial { get; set; }

	[DataMember(Order = 3)]
	public string LastSeenChangelogVersion { get; set; } = string.Empty;

	[DataMember(Order = 4)]
	public bool RepeatTutorialUntilSuppressed { get; set; }

	internal static SirenChangerGuidanceState CreateDefault()
	{
		return new SirenChangerGuidanceState().Normalize();
	}

	internal static SirenChangerGuidanceState LoadOrCreateFromPath(string path, ILog log)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return CreateDefault();
		}

		if (!File.Exists(path))
		{
			SirenChangerGuidanceState created = CreateDefault();
			Save(path, created, log);
			return created;
		}

		try
		{
			string json = File.ReadAllText(path);
			if (!JsonDataSerializer.TryDeserialize(json, out SirenChangerGuidanceState? parsed, out string error) ||
				parsed == null)
			{
				log.Warn($"Guidance state parse failed at {path}. {error}");
				SirenChangerGuidanceState created = CreateDefault();
				Save(path, created, log);
				return created;
			}

			return parsed.Normalize();
		}
		catch (Exception ex)
		{
			log.Warn($"Guidance state read failed at {path}. {ex.Message}");
			SirenChangerGuidanceState created = CreateDefault();
			Save(path, created, log);
			return created;
		}
	}

	internal static void Save(string path, SirenChangerGuidanceState state, ILog log)
	{
		if (string.IsNullOrWhiteSpace(path) || state == null)
		{
			return;
		}

		try
		{
			string directory = Path.GetDirectoryName(path) ?? string.Empty;
			if (!string.IsNullOrWhiteSpace(directory))
			{
				Directory.CreateDirectory(directory);
			}

			string json = JsonDataSerializer.Serialize(state.Normalize());
			File.WriteAllText(path, json);
		}
		catch (Exception ex)
		{
			log.Warn($"Guidance state write failed at {path}. {ex.Message}");
		}
	}

	internal SirenChangerGuidanceState Normalize()
	{
		LastSeenChangelogVersion = (LastSeenChangelogVersion ?? string.Empty).Trim();
		return this;
	}
}
