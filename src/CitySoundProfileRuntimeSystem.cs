using System;
using Colossal;
using Colossal.IO.AssetDatabase;
using Colossal.Serialization.Entities;
using Game;
using Game.Assets;
using Game.SceneFlow;
using Game.Serialization;

namespace SirenChanger;

// Detect loaded city identity at load lifecycle boundaries and apply bound sound sets.
public sealed partial class CitySoundProfileRuntimeSystem : GameSystemBase
{
	private LoadGameSystem m_LoadGameSystem = null!;

	// Resolve required systems once when this runtime system is created.
	protected override void OnCreate()
	{
		base.OnCreate();
		m_LoadGameSystem = World.GetOrCreateSystemManaged<LoadGameSystem>();
		// This system relies on load callbacks instead of per-frame simulation updates.
		Enabled = false;
	}

	// Event-driven system: no polling work required per frame.
	protected override void OnUpdate()
	{
	}

	// Fired for every completed deserialize run, including save-to-save switches.
	protected override void OnGameLoaded(Context serializationContext)
	{
		ApplyCityContextFromLoad(serializationContext);
	}

	// Ensure city context is cleared whenever game mode exits active gameplay.
	protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
	{
		if (mode != GameMode.Game)
		{
			SirenChangerMod.UpdateCurrentCityContext(string.Empty, string.Empty);
		}
	}

	// Resolve one save GUID + display label from load systems and push to city-profile state.
	private void ApplyCityContextFromLoad(Context serializationContext)
	{
		if (GameManager.instance.gameMode != GameMode.Game)
		{
			SirenChangerMod.UpdateCurrentCityContext(string.Empty, string.Empty);
			return;
		}

		Hash128 saveAssetGuid = ResolveLoadedCityAssetGuid(serializationContext);
		string saveGuid = saveAssetGuid.isValid ? saveAssetGuid.ToString() : string.Empty;

		string displayName = ResolveLoadedCityDisplayName(m_LoadGameSystem.dataDescriptor);
		string sessionGuid = ResolveLoadedCitySessionGuid(saveAssetGuid);
		SirenChangerMod.UpdateCurrentCityContext(saveGuid, displayName, sessionGuid);
	}

	// Use one authoritative asset GUID for both GUID binding and session-guid metadata lookup.
	private Hash128 ResolveLoadedCityAssetGuid(Context serializationContext)
	{
		if (serializationContext.instigatorGuid.isValid)
		{
			return serializationContext.instigatorGuid;
		}

		if (m_LoadGameSystem.context.instigatorGuid.isValid)
		{
			// Fallback for edge cases where callback context omits instigator data.
			return m_LoadGameSystem.context.instigatorGuid;
		}

		return default;
	}

	// Prefer explicit save name, then descriptor path, for user-visible city context labels.
	private static string ResolveLoadedCityDisplayName(AsyncReadDescriptor descriptor)
	{
		if (!string.IsNullOrWhiteSpace(descriptor.name))
		{
			return descriptor.name.Trim();
		}

		if (!string.IsNullOrWhiteSpace(descriptor.path))
		{
			return descriptor.path.Trim();
		}

		return string.Empty;
	}

	// Resolve persisted save-session GUID from loaded save metadata to support GUID rebind migration.
	private static string ResolveLoadedCitySessionGuid(Hash128 saveAssetGuid)
	{
		if (!saveAssetGuid.isValid)
		{
			return string.Empty;
		}

		try
		{
			if (AssetDatabase.global.TryGetAsset(saveAssetGuid, out var asset) &&
				asset is SaveGameMetadata saveMetadata &&
				saveMetadata.target != null &&
				saveMetadata.target.sessionGuid != Guid.Empty)
			{
				return CitySoundProfileRegistry.NormalizeSessionGuid(saveMetadata.target.sessionGuid.ToString("D"));
			}
		}
		catch (Exception ex)
		{
			SirenChangerMod.Log.Warn($"Failed to resolve loaded save session GUID: {ex.Message}");
		}

		return string.Empty;
	}
}
