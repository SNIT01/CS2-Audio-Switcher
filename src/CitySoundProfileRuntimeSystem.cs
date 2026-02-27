using Colossal.IO.AssetDatabase;
using Game;
using Game.Common;
using Game.SceneFlow;
using Game.Serialization;

namespace SirenChanger;

// Detects loaded city identity and applies bound sound sets after loading finishes.
public sealed partial class CitySoundProfileRuntimeSystem : GameSystemBase
{
	private LoadGameSystem m_LoadGameSystem = null!;

	// True while a game load is in progress so we can apply once when loading finishes.
	private bool m_WasLoading = true;

	// Resolve required systems once when this runtime system is created.
	protected override void OnCreate()
	{
		base.OnCreate();
		m_LoadGameSystem = World.GetOrCreateSystemManaged<LoadGameSystem>();
	}

	// Detect load completion, read the loaded save identity, and hand it to city-profile logic.
	protected override void OnUpdate()
	{
		if (GameManager.instance.isGameLoading)
		{
			m_WasLoading = true;
			return;
		}

		if (!m_WasLoading)
		{
			return;
		}

		m_WasLoading = false;
		if (GameManager.instance.gameMode != GameMode.Game)
		{
			SirenChangerMod.UpdateCurrentCityContext(string.Empty, string.Empty);
			return;
		}

		string saveGuid = string.Empty;
		var context = m_LoadGameSystem.context;
		if (context.instigatorGuid.isValid)
		{
			saveGuid = context.instigatorGuid.ToString();
		}

		string displayName = string.Empty;
		AsyncReadDescriptor descriptor = m_LoadGameSystem.dataDescriptor;
		if (!string.IsNullOrWhiteSpace(descriptor.name))
		{
			displayName = descriptor.name.Trim();
		}
		else if (!string.IsNullOrWhiteSpace(descriptor.path))
		{
			displayName = descriptor.path.Trim();
		}

		SirenChangerMod.UpdateCurrentCityContext(saveGuid, displayName);
	}
}
