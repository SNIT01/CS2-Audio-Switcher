using System;
using System.Collections.Generic;
using Game;
using Game.Common;
using Game.Prefabs;
using Game.Routes;
using Game.SceneFlow;
using Game.UI;
using Game.Vehicles;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Audio;

namespace SirenChanger;

// Watches public-transport stop state transitions and plays one-shot announcements.
public sealed partial class TransitAnnouncementSystem : GameSystemBase
{
	private const float kMinimumArrivalIntervalSeconds = 1.5f;

	private const float kMinimumDepartureIntervalSeconds = 1.5f;

	private const float kErrorLogCooldownSeconds = 10f;

	private const float kDeferredAnnouncementTimeoutSeconds = 12f;

	private const int kMaxDeferredAnnouncementsPerSlot = 16;

	private EntityQuery m_PublicTransportQuery = default;

	private EntityQuery m_TransportLineQuery = default;

	private ComponentLookup<Target> m_TargetData = default;

	private ComponentLookup<Connected> m_ConnectedData = default;

	private ComponentLookup<Game.Objects.Transform> m_TransformData = default;

	private ComponentLookup<Owner> m_OwnerData = default;

	private ComponentLookup<PublicTransportVehicleData> m_PublicTransportVehicleData = default;

	private ComponentLookup<Controller> m_ControllerData = default;

	private ComponentLookup<CurrentRoute> m_CurrentRouteData = default;

	private ComponentLookup<RouteNumber> m_RouteNumberData = default;

	private ComponentLookup<TransportLineData> m_TransportLineData = default;

	private NameSystem? m_NameSystem;

	private bool m_WasLoading = true;

	private readonly Dictionary<Entity, VehicleAnnouncementState> m_StateByVehicle = new Dictionary<Entity, VehicleAnnouncementState>();

	private readonly HashSet<Entity> m_SeenVehicles = new HashSet<Entity>();

	private readonly List<Entity> m_StaleVehicles = new List<Entity>();

	private readonly Dictionary<TransitAnnouncementSlot, AnnouncementErrorState> m_LastErrorBySlot = new Dictionary<TransitAnnouncementSlot, AnnouncementErrorState>();

	private readonly Dictionary<PendingAnnouncementQueueKey, List<DeferredAnnouncementRequest>> m_PendingAnnouncementsByQueueKey = new Dictionary<PendingAnnouncementQueueKey, List<DeferredAnnouncementRequest>>();

	private readonly List<PendingAnnouncementQueueKey> m_PendingAnnouncementKeysScratch = new List<PendingAnnouncementQueueKey>();

	// Per-vehicle transition memory used to detect clean edge transitions.
	private struct VehicleAnnouncementState
	{
		public PublicTransportFlags LastFlags;

		public Entity LastStopEntity;

		public Entity BoardingStopEntity;

		public float LastArrivalRealtime;

		public float LastDepartureRealtime;
	}

	// Per-slot error throttle so misconfigured files do not spam logs every frame.
	private struct AnnouncementErrorState
	{
		public float LastLoggedRealtime;

		public string LastMessage;
	}

	// Deferred playback request payload kept in a per slot+line queue while async load is pending.
	private struct DeferredAnnouncementRequest
	{
		public Vector3 WorldPosition;

		public float RequestedRealtime;
	}

	// Queue key for pending announcement loads. Isolation is per slot + line identity.
	private readonly struct PendingAnnouncementQueueKey : IEquatable<PendingAnnouncementQueueKey>
	{
		public PendingAnnouncementQueueKey(TransitAnnouncementSlot slot, string lineKey)
		{
			Slot = slot;
			LineKey = SirenChangerMod.NormalizeTransitLineIdentity(lineKey);
		}

		public TransitAnnouncementSlot Slot { get; }

		public string LineKey { get; }

		public bool Equals(PendingAnnouncementQueueKey other)
		{
			return Slot == other.Slot &&
				string.Equals(LineKey, other.LineKey, StringComparison.OrdinalIgnoreCase);
		}

		public override bool Equals(object? obj)
		{
			return obj is PendingAnnouncementQueueKey other && Equals(other);
		}

		public override int GetHashCode()
		{
			unchecked
			{
				int hash = (int)Slot * 397;
				hash ^= StringComparer.OrdinalIgnoreCase.GetHashCode(LineKey ?? string.Empty);
				return hash;
			}
		}
	}

	protected override void OnCreate()
	{
		base.OnCreate();
		m_PublicTransportQuery = GetEntityQuery(
			ComponentType.ReadOnly<Game.Vehicles.PublicTransport>(),
			ComponentType.ReadOnly<PrefabRef>());
		m_TransportLineQuery = GetEntityQuery(
			ComponentType.ReadOnly<Route>(),
			ComponentType.ReadOnly<PrefabRef>());
		m_TargetData = GetComponentLookup<Target>(isReadOnly: true);
		m_ConnectedData = GetComponentLookup<Connected>(isReadOnly: true);
		m_TransformData = GetComponentLookup<Game.Objects.Transform>(isReadOnly: true);
		m_OwnerData = GetComponentLookup<Owner>(isReadOnly: true);
		m_PublicTransportVehicleData = GetComponentLookup<PublicTransportVehicleData>(isReadOnly: true);
		m_ControllerData = GetComponentLookup<Controller>(isReadOnly: true);
		m_CurrentRouteData = GetComponentLookup<CurrentRoute>(isReadOnly: true);
		m_RouteNumberData = GetComponentLookup<RouteNumber>(isReadOnly: true);
		m_TransportLineData = GetComponentLookup<TransportLineData>(isReadOnly: true);
		m_NameSystem = World.GetExistingSystemManaged<NameSystem>();
	}

	protected override void OnUpdate()
	{
		if (GameManager.instance.isGameLoading)
		{
			m_WasLoading = true;
			return;
		}

		if (m_WasLoading)
		{
			ResetSessionState();
			m_WasLoading = false;
		}

		if (GameManager.instance.gameMode != GameMode.Game)
		{
			ResetSessionState();
			return;
		}

		if (m_PublicTransportQuery.IsEmptyIgnoreFilter)
		{
			ResetSessionState();
			return;
		}

		WaveClipLoader.PollAsyncLoads();
		TransitAnnouncementAudioPlayer.UpdateActiveSequences();
		m_NameSystem ??= World.GetExistingSystemManaged<NameSystem>();

		m_TargetData.Update(this);
		m_ConnectedData.Update(this);
		m_TransformData.Update(this);
		m_OwnerData.Update(this);
		m_PublicTransportVehicleData.Update(this);
		m_ControllerData.Update(this);
		m_CurrentRouteData.Update(this);
		m_RouteNumberData.Update(this);

		float now = UnityEngine.Time.unscaledTime;
		ProcessPendingAnnouncements(now);
		bool playbackEnabled = SirenChangerMod.TransitAnnouncementConfig.Enabled;
		m_SeenVehicles.Clear();

		using (NativeArray<Entity> entities = m_PublicTransportQuery.ToEntityArray(Allocator.Temp))
		using (NativeArray<Game.Vehicles.PublicTransport> transports = m_PublicTransportQuery.ToComponentDataArray<Game.Vehicles.PublicTransport>(Allocator.Temp))
		using (NativeArray<PrefabRef> prefabs = m_PublicTransportQuery.ToComponentDataArray<PrefabRef>(Allocator.Temp))
		{
			for (int i = 0; i < entities.Length; i++)
			{
				Entity vehicle = entities[i];
				m_SeenVehicles.Add(vehicle);

				// Ignore child cars/carriages to avoid duplicate announcements per vehicle set.
				if (m_ControllerData.TryGetComponent(vehicle, out Controller controller) &&
					controller.m_Controller != Entity.Null &&
					controller.m_Controller != vehicle)
				{
					continue;
				}

				if (!m_PublicTransportVehicleData.TryGetComponent(prefabs[i].m_Prefab, out PublicTransportVehicleData vehicleData))
				{
					continue;
				}

				if (!TryMapTransportType(
					vehicleData.m_TransportType,
					out TransitAnnouncementServiceType serviceType,
					out TransitAnnouncementSlot arrivalSlot,
					out TransitAnnouncementSlot departureSlot))
				{
					continue;
				}

				if (TryResolveRouteIdentity(serviceType, vehicle, out string observedLineKey, out string observedLineDisplayName))
				{
					SirenChangerMod.RegisterTransitLineObservation(observedLineKey, observedLineDisplayName);
				}

				Game.Vehicles.PublicTransport transport = transports[i];
				bool hasExistingState = m_StateByVehicle.TryGetValue(vehicle, out VehicleAnnouncementState state);

				bool wasArriving = (state.LastFlags & PublicTransportFlags.Arriving) != 0;
				bool wasBoarding = (state.LastFlags & PublicTransportFlags.Boarding) != 0;
				bool isArriving = (transport.m_State & PublicTransportFlags.Arriving) != 0;
				bool isBoarding = (transport.m_State & PublicTransportFlags.Boarding) != 0;

				Entity currentStop = ResolveCurrentStopEntity(vehicle);
				if (currentStop != Entity.Null)
				{
					state.LastStopEntity = currentStop;
				}

				// Seed transition memory for newly seen vehicles so current-state flags do not
				// generate fake arrival/departure edges on the first observed frame.
				if (!hasExistingState)
				{
					state.LastFlags = transport.m_State;
					if (isBoarding)
					{
						state.BoardingStopEntity = currentStop != Entity.Null
							? currentStop
							: state.LastStopEntity;
					}

					m_StateByVehicle[vehicle] = state;
					continue;
				}

				if (!wasArriving && isArriving &&
					playbackEnabled &&
					now - state.LastArrivalRealtime >= kMinimumArrivalIntervalSeconds)
				{
					PlayAnnouncement(arrivalSlot, serviceType, vehicle, state.LastStopEntity, now);
					state.LastArrivalRealtime = now;
				}

				if (!wasBoarding && isBoarding)
				{
					state.BoardingStopEntity = currentStop != Entity.Null
						? currentStop
						: state.LastStopEntity;
				}

				if (wasBoarding && !isBoarding &&
					playbackEnabled &&
					now - state.LastDepartureRealtime >= kMinimumDepartureIntervalSeconds)
				{
					Entity departureStop = state.BoardingStopEntity != Entity.Null
						? state.BoardingStopEntity
						: (currentStop != Entity.Null ? currentStop : state.LastStopEntity);
					PlayAnnouncement(departureSlot, serviceType, vehicle, departureStop, now);
					state.LastDepartureRealtime = now;
					state.BoardingStopEntity = Entity.Null;
				}

				state.LastFlags = transport.m_State;
				m_StateByVehicle[vehicle] = state;
			}
		}

		RemoveStaleVehicleStates();
	}

	// Explicit options action: scan current public transport vehicles and register route identities as known lines.
	internal bool TryScanTransitLinesForOptions(
		out int scannedVehicleCount,
		out int observedLineCount,
		out string status)
	{
		scannedVehicleCount = 0;
		observedLineCount = 0;
		status = string.Empty;
		int scannedRouteCount = 0;

		if (GameManager.instance.isGameLoading)
		{
			status = "Game is still loading. Wait for the city to finish loading and retry.";
			return false;
		}

		if (GameManager.instance.gameMode != GameMode.Game)
		{
			status = "Transit line scan requires a loaded city simulation.";
			return false;
		}

		m_NameSystem ??= World.GetExistingSystemManaged<NameSystem>();
		m_PublicTransportVehicleData.Update(this);
		m_ControllerData.Update(this);
		m_CurrentRouteData.Update(this);
		m_RouteNumberData.Update(this);
		m_OwnerData.Update(this);
		m_TransportLineData.Update(this);

		HashSet<string> seenLines = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		using (NativeArray<Entity> routeEntities = m_TransportLineQuery.ToEntityArray(Allocator.Temp))
		using (NativeArray<PrefabRef> routePrefabs = m_TransportLineQuery.ToComponentDataArray<PrefabRef>(Allocator.Temp))
		{
			scannedRouteCount = routeEntities.Length;
			for (int i = 0; i < routeEntities.Length; i++)
			{
				Entity routeEntity = routeEntities[i];
				if (routeEntity == Entity.Null ||
					!m_TransportLineData.TryGetComponent(routePrefabs[i].m_Prefab, out TransportLineData lineData) ||
					!lineData.m_PassengerTransport ||
					!TryMapTransportType(
						lineData.m_TransportType,
						out TransitAnnouncementServiceType serviceType,
						out _,
						out _) ||
					!TryResolveRouteIdentityFromRouteEntity(
						serviceType,
						routeEntity,
						out string observedLineKey,
						out string observedLineDisplayName))
				{
					continue;
				}

				seenLines.Add(observedLineKey);
				SirenChangerMod.RegisterTransitLineObservation(observedLineKey, observedLineDisplayName);
			}
		}

		using (NativeArray<Entity> entities = m_PublicTransportQuery.ToEntityArray(Allocator.Temp))
		using (NativeArray<PrefabRef> prefabs = m_PublicTransportQuery.ToComponentDataArray<PrefabRef>(Allocator.Temp))
		{
			scannedVehicleCount = entities.Length;
			for (int i = 0; i < entities.Length; i++)
			{
				Entity vehicle = entities[i];
				if (m_ControllerData.TryGetComponent(vehicle, out Controller controller) &&
					controller.m_Controller != Entity.Null &&
					controller.m_Controller != vehicle)
				{
					continue;
				}

				if (!m_PublicTransportVehicleData.TryGetComponent(prefabs[i].m_Prefab, out PublicTransportVehicleData vehicleData))
				{
					continue;
				}

				if (!TryMapTransportType(
						vehicleData.m_TransportType,
						out TransitAnnouncementServiceType serviceType,
						out _,
						out _))
				{
					continue;
				}

				if (!TryResolveRouteIdentity(serviceType, vehicle, out string observedLineKey, out string observedLineDisplayName))
				{
					continue;
				}

				seenLines.Add(observedLineKey);
				SirenChangerMod.RegisterTransitLineObservation(observedLineKey, observedLineDisplayName);
			}
		}

		observedLineCount = seenLines.Count;
		status = $"Scanned routes: {scannedRouteCount}\nScanned vehicles: {scannedVehicleCount}\nObserved lines: {observedLineCount}";
		return true;
	}

	// Reset transition memory when loading/game-mode transitions happen.
	private void ResetSessionState()
	{
		m_StateByVehicle.Clear();
		m_SeenVehicles.Clear();
		m_StaleVehicles.Clear();
		m_LastErrorBySlot.Clear();
		m_PendingAnnouncementsByQueueKey.Clear();
		m_PendingAnnouncementKeysScratch.Clear();
		SirenChangerMod.ResetTransitLineObservationSession();
	}

	// Remove state rows for vehicles no longer present in the active query.
	private void RemoveStaleVehicleStates()
	{
		if (m_StateByVehicle.Count == 0)
		{
			return;
		}

		m_StaleVehicles.Clear();
		foreach (KeyValuePair<Entity, VehicleAnnouncementState> pair in m_StateByVehicle)
		{
			if (!m_SeenVehicles.Contains(pair.Key))
			{
				m_StaleVehicles.Add(pair.Key);
			}
		}

		for (int i = 0; i < m_StaleVehicles.Count; i++)
		{
			m_StateByVehicle.Remove(m_StaleVehicles[i]);
		}
	}

	// Resolve the current station/stop entity from the vehicle target path node.
	private Entity ResolveCurrentStopEntity(Entity vehicle)
	{
		if (!m_TargetData.TryGetComponent(vehicle, out Target target) || target.m_Target == Entity.Null)
		{
			return Entity.Null;
		}

		Entity stop = target.m_Target;
		if (m_ConnectedData.TryGetComponent(stop, out Connected connected) && connected.m_Connected != Entity.Null)
		{
			stop = connected.m_Connected;
		}

		return stop;
	}

	// Map transport type to the fixed arrival/departure slot IDs.
	private static bool TryMapTransportType(
		TransportType transportType,
		out TransitAnnouncementServiceType serviceType,
		out TransitAnnouncementSlot arrivalSlot,
		out TransitAnnouncementSlot departureSlot)
	{
		switch (transportType)
		{
			case TransportType.Train:
				serviceType = TransitAnnouncementServiceType.Train;
				arrivalSlot = TransitAnnouncementSlot.TrainArrival;
				departureSlot = TransitAnnouncementSlot.TrainDeparture;
				return true;
			case TransportType.Bus:
				serviceType = TransitAnnouncementServiceType.Bus;
				arrivalSlot = TransitAnnouncementSlot.BusArrival;
				departureSlot = TransitAnnouncementSlot.BusDeparture;
				return true;
			case TransportType.Subway:
				serviceType = TransitAnnouncementServiceType.Metro;
				arrivalSlot = TransitAnnouncementSlot.MetroArrival;
				departureSlot = TransitAnnouncementSlot.MetroDeparture;
				return true;
			case TransportType.Tram:
				serviceType = TransitAnnouncementServiceType.Tram;
				arrivalSlot = TransitAnnouncementSlot.TramArrival;
				departureSlot = TransitAnnouncementSlot.TramDeparture;
				return true;
			case TransportType.Ferry:
			case TransportType.Ship:
				serviceType = TransitAnnouncementServiceType.Ferry;
				arrivalSlot = TransitAnnouncementSlot.FerryArrival;
				departureSlot = TransitAnnouncementSlot.FerryDeparture;
				return true;
			default:
				serviceType = default;
				arrivalSlot = default;
				departureSlot = default;
				return false;
		}
	}

	// Build and play one configured announcement sequence at the resolved stop world position.
	private void PlayAnnouncement(
		TransitAnnouncementSlot slot,
		TransitAnnouncementServiceType serviceType,
		Entity vehicle,
		Entity stopEntity,
		float now)
	{
		if (stopEntity == Entity.Null || !TryResolveWorldPosition(stopEntity, out float3 worldPosition))
		{
			LogSlotError(slot, "Could not resolve stop world position for announcement playback.", now);
			return;
		}

		string lineKey = ResolveAnnouncementLineKey(serviceType, vehicle);
		PendingAnnouncementQueueKey queueKey = new PendingAnnouncementQueueKey(slot, lineKey);
		TransitAnnouncementLoadStatus loadStatus = SirenChangerMod.TryBuildTransitAnnouncementSequence(
			slot,
			lineKey,
			out List<TransitAnnouncementPlaybackSegment> segments,
			out string statusMessage);
		if (loadStatus == TransitAnnouncementLoadStatus.NotConfigured ||
			loadStatus == TransitAnnouncementLoadStatus.Pending)
		{
			if (loadStatus == TransitAnnouncementLoadStatus.Pending)
			{
				// Queue deferred requests so concurrent arrivals/departures on one slot are preserved.
				if (!EnqueuePendingAnnouncement(
						queueKey,
						new Vector3(worldPosition.x, worldPosition.y, worldPosition.z),
						now))
				{
					LogSlotError(slot, "Announcement queue is full; newest pending event was dropped.", now);
				}
			}
			else
			{
				m_PendingAnnouncementsByQueueKey.Remove(queueKey);
			}
			return;
		}

		if (loadStatus == TransitAnnouncementLoadStatus.Failure)
		{
			m_PendingAnnouncementsByQueueKey.Remove(queueKey);
			LogSlotError(slot, statusMessage, now);
			return;
		}

		m_PendingAnnouncementsByQueueKey.Remove(queueKey);
		Vector3 position = new Vector3(worldPosition.x, worldPosition.y, worldPosition.z);
		if (!TransitAnnouncementAudioPlayer.TryPlaySequence(segments, position, out string playError))
		{
			LogSlotError(slot, $"Playback failed: {playError}", now);
		}
	}

	// Queue one deferred request while limiting total backlog size per slot across all lines.
	private bool EnqueuePendingAnnouncement(
		PendingAnnouncementQueueKey queueKey,
		Vector3 worldPosition,
		float requestedRealtime)
	{
		if (string.IsNullOrWhiteSpace(queueKey.LineKey))
		{
			return false;
		}

		int pendingCountForSlot = 0;
		foreach (KeyValuePair<PendingAnnouncementQueueKey, List<DeferredAnnouncementRequest>> pair in m_PendingAnnouncementsByQueueKey)
		{
			if (pair.Key.Slot != queueKey.Slot || pair.Value == null)
			{
				continue;
			}

			pendingCountForSlot += pair.Value.Count;
			if (pendingCountForSlot >= kMaxDeferredAnnouncementsPerSlot)
			{
				return false;
			}
		}

		if (!m_PendingAnnouncementsByQueueKey.TryGetValue(queueKey, out List<DeferredAnnouncementRequest>? requests) || requests == null)
		{
			requests = new List<DeferredAnnouncementRequest>(kMaxDeferredAnnouncementsPerSlot);
			m_PendingAnnouncementsByQueueKey[queueKey] = requests;
		}

		requests.Add(new DeferredAnnouncementRequest
		{
			WorldPosition = worldPosition,
			RequestedRealtime = requestedRealtime
		});
		return true;
	}

	// Retry pending async loads so the first event after selecting an OGG can still play.
	private void ProcessPendingAnnouncements(float now)
	{
		if (m_PendingAnnouncementsByQueueKey.Count == 0)
		{
			return;
		}

		m_PendingAnnouncementKeysScratch.Clear();
		foreach (KeyValuePair<PendingAnnouncementQueueKey, List<DeferredAnnouncementRequest>> pair in m_PendingAnnouncementsByQueueKey)
		{
			m_PendingAnnouncementKeysScratch.Add(pair.Key);
		}

		for (int i = 0; i < m_PendingAnnouncementKeysScratch.Count; i++)
		{
			PendingAnnouncementQueueKey queueKey = m_PendingAnnouncementKeysScratch[i];
			if (!m_PendingAnnouncementsByQueueKey.TryGetValue(queueKey, out List<DeferredAnnouncementRequest>? requests) ||
				requests == null ||
				requests.Count == 0)
			{
				m_PendingAnnouncementsByQueueKey.Remove(queueKey);
				continue;
			}

			// Drop stale requests from queue front to preserve strict FIFO for remaining items.
			while (requests.Count > 0 &&
				now - requests[0].RequestedRealtime > kDeferredAnnouncementTimeoutSeconds)
			{
				requests.RemoveAt(0);
			}

			if (requests.Count == 0)
			{
				m_PendingAnnouncementsByQueueKey.Remove(queueKey);
				continue;
			}

			while (requests.Count > 0)
			{
				DeferredAnnouncementRequest request = requests[0];
				TransitAnnouncementLoadStatus loadStatus = SirenChangerMod.TryBuildTransitAnnouncementSequence(
					queueKey.Slot,
					queueKey.LineKey,
					out List<TransitAnnouncementPlaybackSegment> segments,
					out string message);
				if (loadStatus == TransitAnnouncementLoadStatus.Pending)
				{
					// Preserve strict FIFO order: wait for the front request to become ready.
					break;
				}

				if (loadStatus == TransitAnnouncementLoadStatus.Success)
				{
					if (!TransitAnnouncementAudioPlayer.TryPlaySequence(segments, request.WorldPosition, out string playError))
					{
						LogSlotError(queueKey.Slot, $"Playback failed: {playError}", now);
					}
				}
				else if (loadStatus == TransitAnnouncementLoadStatus.Failure)
				{
					LogSlotError(queueKey.Slot, message, now);
				}

				requests.RemoveAt(0);
			}

			if (requests.Count == 0)
			{
				m_PendingAnnouncementsByQueueKey.Remove(queueKey);
			}
		}

		m_PendingAnnouncementKeysScratch.Clear();
	}

	// Resolve the line identity key used for per-line announcement overrides.
	private string ResolveAnnouncementLineKey(TransitAnnouncementServiceType serviceType, Entity vehicle)
	{
		if (!TryResolveRouteIdentity(serviceType, vehicle, out string lineKey, out _))
		{
			return string.Empty;
		}

		return lineKey;
	}

	// Resolve stable line identity plus a user-facing display label.
	private bool TryResolveRouteIdentity(
		TransitAnnouncementServiceType serviceType,
		Entity vehicle,
		out string lineKey,
		out string displayName)
	{
		lineKey = string.Empty;
		displayName = string.Empty;
		if (!TryResolveRouteDescriptor(vehicle, out string stableLineId, out string routeDisplayName))
		{
			return false;
		}

		string key = SirenChangerMod.BuildTransitLineIdentity(serviceType, stableLineId);
		if (string.IsNullOrWhiteSpace(key))
		{
			return false;
		}

		lineKey = key;
		displayName = routeDisplayName;
		return true;
	}

	private bool TryResolveRouteIdentityFromRouteEntity(
		TransitAnnouncementServiceType serviceType,
		Entity routeEntity,
		out string lineKey,
		out string displayName)
	{
		lineKey = string.Empty;
		displayName = string.Empty;
		if (!TryResolveRouteDescriptorFromRouteEntity(routeEntity, out string stableLineId, out string routeDisplayName))
		{
			return false;
		}

		string key = SirenChangerMod.BuildTransitLineIdentity(serviceType, stableLineId);
		if (string.IsNullOrWhiteSpace(key))
		{
			return false;
		}

		lineKey = key;
		displayName = routeDisplayName;
		return true;
	}

	// Resolve a stable route ID and a display label for options UI from one vehicle.
	private bool TryResolveRouteDescriptor(
		Entity vehicle,
		out string stableLineId,
		out string displayName)
	{
		stableLineId = string.Empty;
		displayName = string.Empty;
		if (!m_CurrentRouteData.TryGetComponent(vehicle, out CurrentRoute currentRoute) ||
			currentRoute.m_Route == Entity.Null)
		{
			return false;
		}

		return TryResolveRouteDescriptorFromRouteEntity(currentRoute.m_Route, out stableLineId, out displayName);
	}

	// Resolve a stable route ID from one route entity and a display label for options UI.
	private bool TryResolveRouteDescriptorFromRouteEntity(
		Entity routeEntity,
		out string stableLineId,
		out string displayName)
	{
		stableLineId = string.Empty;
		displayName = string.Empty;
		if (routeEntity == Entity.Null)
		{
			return false;
		}

		bool hasRouteNumber = TryGetRouteNumberWithOwnerFallback(routeEntity, out int routeNumber);
		stableLineId = hasRouteNumber && routeNumber > 0
			? $"number:{routeNumber}"
			: $"route:{routeEntity.Index}:{routeEntity.Version}:0";

		// Prefer the route entity name; owner labels are often generic and collapse distinct lines.
		string routeLabel = GetEntityDisplayName(routeEntity);
		if (string.IsNullOrWhiteSpace(routeLabel))
		{
			routeLabel = GetOwnerDisplayName(routeEntity);
		}

		if (string.IsNullOrWhiteSpace(routeLabel))
		{
			routeLabel = hasRouteNumber && routeNumber > 0
				? $"Line {routeNumber}"
				: $"Route {routeEntity.Index}";
		}

		displayName = routeLabel;
		return !string.IsNullOrWhiteSpace(stableLineId);
	}

	// Query user-facing name text through the game's NameSystem when available.
	private string GetEntityDisplayName(Entity entity)
	{
		if (entity == Entity.Null)
		{
			return string.Empty;
		}

		NameSystem? nameSystem = m_NameSystem ?? World.GetExistingSystemManaged<NameSystem>();
		if (nameSystem == null)
		{
			return string.Empty;
		}

		try
		{
			m_NameSystem = nameSystem;
			return AudioReplacementDomainConfig.NormalizeTransitDisplayText(nameSystem.GetRenderedLabelName(entity));
		}
		catch
		{
			return string.Empty;
		}
	}

	// Resolve a user-facing label from owner entities first.
	private string GetOwnerDisplayName(Entity entity)
	{
		if (entity == Entity.Null || !m_OwnerData.TryGetComponent(entity, out Owner owner))
		{
			return string.Empty;
		}

		Entity current = owner.m_Owner;
		for (int depth = 0; depth < 8 && current != Entity.Null; depth++)
		{
			string label = GetEntityDisplayName(current);
			if (!string.IsNullOrWhiteSpace(label))
			{
				return label;
			}

			if (!m_OwnerData.TryGetComponent(current, out Owner currentOwner) ||
				currentOwner.m_Owner == Entity.Null ||
				currentOwner.m_Owner == current)
			{
				break;
			}

			current = currentOwner.m_Owner;
		}

		return string.Empty;
	}

	// Resolve route number from route entity or owner chain so line numbers still work for nested route entities.
	private bool TryGetRouteNumberWithOwnerFallback(Entity entity, out int routeNumber)
	{
		routeNumber = 0;
		Entity current = entity;
		for (int depth = 0; depth < 8 && current != Entity.Null; depth++)
		{
			if (m_RouteNumberData.TryGetComponent(current, out RouteNumber route))
			{
				routeNumber = route.m_Number;
				return true;
			}

			if (!m_OwnerData.TryGetComponent(current, out Owner owner) ||
				owner.m_Owner == Entity.Null ||
				owner.m_Owner == current)
			{
				break;
			}

			current = owner.m_Owner;
		}

		return false;
	}

	// Walk owner chain to locate a transform for the target entity.
	private bool TryResolveWorldPosition(Entity entity, out float3 position)
	{
		Entity current = entity;
		for (int i = 0; i < 8 && current != Entity.Null; i++)
		{
			if (m_TransformData.TryGetComponent(current, out Game.Objects.Transform transform))
			{
				position = transform.m_Position;
				return true;
			}

			if (!m_OwnerData.TryGetComponent(current, out Owner owner) ||
				owner.m_Owner == Entity.Null ||
				owner.m_Owner == current)
			{
				break;
			}

			current = owner.m_Owner;
		}

		position = default;
		return false;
	}

	// Throttle repeated slot errors to keep logs actionable.
	private void LogSlotError(TransitAnnouncementSlot slot, string message, float now)
	{
		if (string.IsNullOrWhiteSpace(message))
		{
			return;
		}

		if (m_LastErrorBySlot.TryGetValue(slot, out AnnouncementErrorState previous) &&
			string.Equals(previous.LastMessage, message, StringComparison.Ordinal) &&
			now - previous.LastLoggedRealtime < kErrorLogCooldownSeconds)
		{
			return;
		}

		m_LastErrorBySlot[slot] = new AnnouncementErrorState
		{
			LastLoggedRealtime = now,
			LastMessage = message
		};
		SirenChangerMod.Log.Warn($"Transit announcement ({SirenChangerMod.GetTransitAnnouncementSlotLabel(slot)}): {message}");
	}
}

// Unity AudioSource pool used for one-shot station announcements.
internal static class TransitAnnouncementAudioPlayer
{
	private const int kSourcePoolSize = 8;

	private const int kMaxPendingSequenceQueue = 32;

	private const float kMixerResolveCooldownSeconds = 2f;

	private const int kMaxMixerResolveAttempts = 3;

	private static GameObject? s_RootObject;

	private static readonly List<AudioSource> s_AudioSources = new List<AudioSource>(kSourcePoolSize);

	private static int s_NextSourceIndex;

	private static AudioMixerGroup? s_OutputMixerGroup;

	private static float s_NextMixerResolveRealtime;

	private static int s_MixerResolveAttempts;

	private sealed class ActiveSequence
	{
		public AudioSource Source { get; set; } = null!;

		public List<TransitAnnouncementPlaybackSegment> Segments { get; set; } = null!;

		public int SegmentIndex { get; set; }
	}

	private sealed class QueuedSequence
	{
		public List<TransitAnnouncementPlaybackSegment> Segments { get; set; } = null!;

		public Vector3 Position { get; set; }
	}

	private static readonly List<ActiveSequence> s_ActiveSequences = new List<ActiveSequence>(kSourcePoolSize);

	private static readonly List<QueuedSequence> s_PendingSequences = new List<QueuedSequence>(kMaxPendingSequenceQueue);

	// Stop and destroy all pooled audio sources on mod unload.
	internal static void Release()
	{
		if (s_RootObject != null)
		{
			UnityEngine.Object.Destroy(s_RootObject);
			s_RootObject = null;
		}

		s_AudioSources.Clear();
		s_NextSourceIndex = 0;
		s_OutputMixerGroup = null;
		s_NextMixerResolveRealtime = 0f;
		s_MixerResolveAttempts = 0;
		s_ActiveSequences.Clear();
		s_PendingSequences.Clear();
	}

	// Play one clip in world space using clamped SFX profile parameters.
	internal static bool TryPlay(AudioClip clip, SirenSfxProfile profile, Vector3 position, out string error)
	{
		List<TransitAnnouncementPlaybackSegment> singleStep = new List<TransitAnnouncementPlaybackSegment>(1)
		{
			new TransitAnnouncementPlaybackSegment(clip, profile)
		};
		return TryPlaySequence(singleStep, position, out error);
	}

	// Advance active sequences and chain to the next segment when a clip finishes.
	internal static void UpdateActiveSequences()
	{
		for (int i = s_ActiveSequences.Count - 1; i >= 0; i--)
		{
			ActiveSequence sequence = s_ActiveSequences[i];
			AudioSource source = sequence.Source;
			if (source == null)
			{
				s_ActiveSequences.RemoveAt(i);
				continue;
			}

			if (source.isPlaying)
			{
				continue;
			}

			int nextIndex = sequence.SegmentIndex + 1;
			if (nextIndex >= sequence.Segments.Count)
			{
				source.clip = null;
				s_ActiveSequences.RemoveAt(i);
				continue;
			}

			sequence.SegmentIndex = nextIndex;
			StartSegmentOnSource(source, sequence.Segments[nextIndex]);
		}

		DispatchQueuedSequences();
	}

	// Play a multi-step sequence in world space using one source to preserve spacing/timing.
	internal static bool TryPlaySequence(
		ICollection<TransitAnnouncementPlaybackSegment> segments,
		Vector3 position,
		out string error)
	{
		error = string.Empty;
		if (segments == null || segments.Count == 0)
		{
			error = "No announcement segments were provided.";
			return false;
		}

		List<TransitAnnouncementPlaybackSegment> timeline = new List<TransitAnnouncementPlaybackSegment>(segments.Count);
		foreach (TransitAnnouncementPlaybackSegment segment in segments)
		{
			if (segment.Clip == null)
			{
				error = "Announcement sequence contained a null clip.";
				return false;
			}

			timeline.Add(segment);
		}

		EnsureAudioSourcePool();
		TryResolveOutputMixerGroup();
		if (s_PendingSequences.Count >= kMaxPendingSequenceQueue)
		{
			error = "Announcement playback queue is full.";
			return false;
		}

		s_PendingSequences.Add(new QueuedSequence
		{
			Segments = timeline,
			Position = position
		});
		DispatchQueuedSequences();
		return true;
	}

	// Create a small reusable pool to avoid allocating GameObjects per event.
	private static void EnsureAudioSourcePool()
	{
		if (s_RootObject != null && s_AudioSources.Count == kSourcePoolSize)
		{
			return;
		}

		if (s_RootObject == null)
		{
			s_RootObject = new GameObject("SirenChanger.TransitAnnouncements");
			UnityEngine.Object.DontDestroyOnLoad(s_RootObject);
		}

		TryResolveOutputMixerGroup(force: true);

		while (s_AudioSources.Count < kSourcePoolSize)
		{
			GameObject child = new GameObject($"TransitAnnouncementSource_{s_AudioSources.Count + 1}");
			child.transform.SetParent(s_RootObject.transform, worldPositionStays: false);
			AudioSource source = child.AddComponent<AudioSource>();
			source.playOnAwake = false;
			source.loop = false;
			source.outputAudioMixerGroup = s_OutputMixerGroup;
			s_AudioSources.Add(source);
		}
	}

	// Prefer idle sources only; busy sources are never interrupted.
	private static bool TryGetIdleAudioSource(out AudioSource source)
	{
		source = null!;
		if (s_AudioSources.Count == 0)
		{
			return false;
		}

		for (int i = 0; i < s_AudioSources.Count; i++)
		{
			int index = (s_NextSourceIndex + i) % s_AudioSources.Count;
			AudioSource candidate = s_AudioSources[index];
			if (candidate != null && !candidate.isPlaying)
			{
				s_NextSourceIndex = (index + 1) % s_AudioSources.Count;
				source = candidate;
				return true;
			}
		}

		return false;
	}

	// Drain queued sequences into currently idle sources.
	private static void DispatchQueuedSequences()
	{
		while (s_PendingSequences.Count > 0 && TryGetIdleAudioSource(out AudioSource source))
		{
			QueuedSequence queued = s_PendingSequences[0];
			s_PendingSequences.RemoveAt(0);
			StartQueuedSequence(source, queued);
		}
	}

	private static void StartQueuedSequence(AudioSource source, QueuedSequence queued)
	{
		source.transform.position = queued.Position;
		source.Stop();
		RemoveActiveSequenceForSource(source);

		ActiveSequence active = new ActiveSequence
		{
			Source = source,
			Segments = queued.Segments,
			SegmentIndex = 0
		};
		s_ActiveSequences.Add(active);
		StartSegmentOnSource(source, queued.Segments[0]);
	}

	// Apply profile and clip for one segment, then start playback immediately.
	private static void StartSegmentOnSource(AudioSource source, TransitAnnouncementPlaybackSegment segment)
	{
		SirenSfxProfile clamped = SirenChangerMod.BuildTransitAnnouncementPlaybackProfile(segment.Profile);
		source.clip = segment.Clip;
		source.volume = clamped.Volume;
		source.pitch = clamped.Pitch;
		source.spatialBlend = clamped.SpatialBlend;
		source.dopplerLevel = clamped.Doppler;
		source.spread = clamped.Spread;
		source.minDistance = clamped.MinDistance;
		source.maxDistance = clamped.MaxDistance;
		source.rolloffMode = clamped.RolloffMode;
		source.loop = false;
		source.Play();
	}

	// Ensure one source is tracked by at most one sequence entry.
	private static void RemoveActiveSequenceForSource(AudioSource source)
	{
		for (int i = s_ActiveSequences.Count - 1; i >= 0; i--)
		{
			if (!ReferenceEquals(s_ActiveSequences[i].Source, source))
			{
				continue;
			}

			s_ActiveSequences.RemoveAt(i);
		}
	}

	// Try to route announcement sources into the game's existing audio mixer graph.
	private static void TryResolveOutputMixerGroup(bool force = false)
	{
		float now = Time.unscaledTime;
		if (!force && s_OutputMixerGroup != null)
		{
			return;
		}

		if (!force && s_MixerResolveAttempts >= kMaxMixerResolveAttempts)
		{
			return;
		}

		if (!force && now < s_NextMixerResolveRealtime)
		{
			return;
		}

		s_NextMixerResolveRealtime = now + kMixerResolveCooldownSeconds;
		AudioSource[] allSources = UnityEngine.Object.FindObjectsOfType<AudioSource>();
		AudioMixerGroup? selected = null;
		for (int i = 0; i < allSources.Length; i++)
		{
			AudioSource candidate = allSources[i];
			if (candidate == null || candidate.outputAudioMixerGroup == null)
			{
				continue;
			}

			// Prefer an in-world 3D source if available; otherwise any routed source is fine.
			if (candidate.spatialBlend > 0f)
			{
				selected = candidate.outputAudioMixerGroup;
				break;
			}

			selected ??= candidate.outputAudioMixerGroup;
		}

		if (selected == null)
		{
			if (!force)
			{
				s_MixerResolveAttempts++;
			}

			return;
		}

		if (ReferenceEquals(s_OutputMixerGroup, selected))
		{
			return;
		}

		s_OutputMixerGroup = selected;
		s_MixerResolveAttempts = 0;
		for (int i = 0; i < s_AudioSources.Count; i++)
		{
			AudioSource source = s_AudioSources[i];
			if (source != null)
			{
				source.outputAudioMixerGroup = s_OutputMixerGroup;
			}
		}
	}
}
