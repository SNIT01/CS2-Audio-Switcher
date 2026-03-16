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

	private const int kMaxDeferredAnnouncementsPerSlot = 4;

	private EntityQuery m_PublicTransportQuery = default;

	private ComponentLookup<Target> m_TargetData = default;

	private ComponentLookup<Connected> m_ConnectedData = default;

	private ComponentLookup<Game.Objects.Transform> m_TransformData = default;

	private ComponentLookup<Owner> m_OwnerData = default;

	private ComponentLookup<PublicTransportVehicleData> m_PublicTransportVehicleData = default;

	private ComponentLookup<Controller> m_ControllerData = default;

	private ComponentLookup<CurrentRoute> m_CurrentRouteData = default;

	private ComponentLookup<RouteNumber> m_RouteNumberData = default;

	private NameSystem? m_NameSystem;

	private bool m_WasLoading = true;

	private readonly Dictionary<Entity, VehicleAnnouncementState> m_StateByVehicle = new Dictionary<Entity, VehicleAnnouncementState>();

	private readonly HashSet<Entity> m_SeenVehicles = new HashSet<Entity>();

	private readonly List<Entity> m_StaleVehicles = new List<Entity>();

	private readonly Dictionary<TransitAnnouncementSlot, AnnouncementErrorState> m_LastErrorBySlot = new Dictionary<TransitAnnouncementSlot, AnnouncementErrorState>();

	private readonly Dictionary<TransitAnnouncementSlot, List<DeferredAnnouncementRequest>> m_PendingAnnouncementsBySlot = new Dictionary<TransitAnnouncementSlot, List<DeferredAnnouncementRequest>>();

	private readonly List<TransitAnnouncementSlot> m_PendingAnnouncementSlotsScratch = new List<TransitAnnouncementSlot>();

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

	// Deferred playback request payload kept in a per-slot queue while async load is pending.
	private struct DeferredAnnouncementRequest
	{
		public Vector3 WorldPosition;

		public float RequestedRealtime;

		public TransitAnnouncementServiceType ServiceType;

		public string StopOrServiceText;
	}

	protected override void OnCreate()
	{
		base.OnCreate();
		m_PublicTransportQuery = GetEntityQuery(
			ComponentType.ReadOnly<Game.Vehicles.PublicTransport>(),
			ComponentType.ReadOnly<PrefabRef>());
		m_TargetData = GetComponentLookup<Target>(isReadOnly: true);
		m_ConnectedData = GetComponentLookup<Connected>(isReadOnly: true);
		m_TransformData = GetComponentLookup<Game.Objects.Transform>(isReadOnly: true);
		m_OwnerData = GetComponentLookup<Owner>(isReadOnly: true);
		m_PublicTransportVehicleData = GetComponentLookup<PublicTransportVehicleData>(isReadOnly: true);
		m_ControllerData = GetComponentLookup<Controller>(isReadOnly: true);
		m_CurrentRouteData = GetComponentLookup<CurrentRoute>(isReadOnly: true);
		m_RouteNumberData = GetComponentLookup<RouteNumber>(isReadOnly: true);
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

	// Reset transition memory when loading/game-mode transitions happen.
	private void ResetSessionState()
	{
		m_StateByVehicle.Clear();
		m_SeenVehicles.Clear();
		m_StaleVehicles.Clear();
		m_LastErrorBySlot.Clear();
		m_PendingAnnouncementsBySlot.Clear();
		m_PendingAnnouncementSlotsScratch.Clear();
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

		string stopOrServiceText = ResolveStopOrServiceText(slot, vehicle, stopEntity, serviceType);
		TransitAnnouncementLoadStatus loadStatus = SirenChangerMod.TryBuildTransitAnnouncementSequence(
			slot,
			serviceType,
			stopOrServiceText,
			out List<TransitAnnouncementPlaybackSegment> segments,
			out string statusMessage);
		if (loadStatus == TransitAnnouncementLoadStatus.NotConfigured ||
			loadStatus == TransitAnnouncementLoadStatus.Pending)
		{
			if (loadStatus == TransitAnnouncementLoadStatus.Pending)
			{
				// Queue deferred requests so concurrent arrivals/departures on one slot are preserved.
				EnqueuePendingAnnouncement(
					slot,
					new Vector3(worldPosition.x, worldPosition.y, worldPosition.z),
					now,
					serviceType,
					stopOrServiceText);
			}
			else
			{
				m_PendingAnnouncementsBySlot.Remove(slot);
			}
			return;
		}

		if (loadStatus == TransitAnnouncementLoadStatus.Failure)
		{
			m_PendingAnnouncementsBySlot.Remove(slot);
			LogSlotError(slot, statusMessage, now);
			return;
		}

		m_PendingAnnouncementsBySlot.Remove(slot);
		Vector3 position = new Vector3(worldPosition.x, worldPosition.y, worldPosition.z);
		if (!TransitAnnouncementAudioPlayer.TryPlaySequence(segments, position, out string playError))
		{
			LogSlotError(slot, $"Playback failed: {playError}", now);
		}
	}

	// Queue one deferred request while limiting per-slot backlog size.
	private void EnqueuePendingAnnouncement(
		TransitAnnouncementSlot slot,
		Vector3 worldPosition,
		float requestedRealtime,
		TransitAnnouncementServiceType serviceType,
		string stopOrServiceText)
	{
		if (!m_PendingAnnouncementsBySlot.TryGetValue(slot, out List<DeferredAnnouncementRequest>? requests) || requests == null)
		{
			requests = new List<DeferredAnnouncementRequest>(kMaxDeferredAnnouncementsPerSlot);
			m_PendingAnnouncementsBySlot[slot] = requests;
		}

		requests.Add(new DeferredAnnouncementRequest
		{
			WorldPosition = worldPosition,
			RequestedRealtime = requestedRealtime,
			ServiceType = serviceType,
			StopOrServiceText = stopOrServiceText ?? string.Empty
		});
		if (requests.Count > kMaxDeferredAnnouncementsPerSlot)
		{
			requests.RemoveAt(0);
		}
	}

	// Retry pending async loads so the first event after selecting an OGG can still play.
	private void ProcessPendingAnnouncements(float now)
	{
		if (m_PendingAnnouncementsBySlot.Count == 0)
		{
			return;
		}

		m_PendingAnnouncementSlotsScratch.Clear();
		foreach (KeyValuePair<TransitAnnouncementSlot, List<DeferredAnnouncementRequest>> pair in m_PendingAnnouncementsBySlot)
		{
			m_PendingAnnouncementSlotsScratch.Add(pair.Key);
		}

		for (int i = 0; i < m_PendingAnnouncementSlotsScratch.Count; i++)
		{
			TransitAnnouncementSlot slot = m_PendingAnnouncementSlotsScratch[i];
			if (!m_PendingAnnouncementsBySlot.TryGetValue(slot, out List<DeferredAnnouncementRequest>? requests) ||
				requests == null ||
				requests.Count == 0)
			{
				m_PendingAnnouncementsBySlot.Remove(slot);
				continue;
			}

			// Drop stale requests to avoid replaying long after the in-game event happened.
			for (int requestIndex = requests.Count - 1; requestIndex >= 0; requestIndex--)
			{
				if (now - requests[requestIndex].RequestedRealtime > kDeferredAnnouncementTimeoutSeconds)
				{
					requests.RemoveAt(requestIndex);
				}
			}

			if (requests.Count == 0)
			{
				m_PendingAnnouncementsBySlot.Remove(slot);
				continue;
			}

			for (int requestIndex = requests.Count - 1; requestIndex >= 0; requestIndex--)
			{
				DeferredAnnouncementRequest request = requests[requestIndex];
				TransitAnnouncementLoadStatus loadStatus = SirenChangerMod.TryBuildTransitAnnouncementSequence(
					slot,
					request.ServiceType,
					request.StopOrServiceText,
					out List<TransitAnnouncementPlaybackSegment> segments,
					out string message);
				if (loadStatus == TransitAnnouncementLoadStatus.Pending)
				{
					continue;
				}

				if (loadStatus == TransitAnnouncementLoadStatus.Success)
				{
					if (!TransitAnnouncementAudioPlayer.TryPlaySequence(segments, request.WorldPosition, out string playError))
					{
						LogSlotError(slot, $"Playback failed: {playError}", now);
					}
				}
				else if (loadStatus == TransitAnnouncementLoadStatus.Failure)
				{
					LogSlotError(slot, message, now);
				}

				requests.RemoveAt(requestIndex);
			}

			if (requests.Count == 0)
			{
				m_PendingAnnouncementsBySlot.Remove(slot);
			}
		}

		m_PendingAnnouncementSlotsScratch.Clear();
	}

	// Resolve speech label for each slot, including line-first behavior for arriving trains.
	private string ResolveStopOrServiceText(
		TransitAnnouncementSlot slot,
		Entity vehicle,
		Entity stopEntity,
		TransitAnnouncementServiceType serviceType)
	{
		// For arriving trains, speak the train line first.
		if (slot == TransitAnnouncementSlot.TrainArrival &&
			TryResolveRouteSpeechText(vehicle, out string arrivingTrainLine))
		{
			return arrivingTrainLine;
		}

		// Prefer parent station/building names over child platform/waypoint labels.
		string stopLabel = GetOwnerDisplayName(stopEntity);
		if (string.IsNullOrWhiteSpace(stopLabel))
		{
			stopLabel = GetEntityDisplayName(stopEntity);
		}

		if (!string.IsNullOrWhiteSpace(stopLabel))
		{
			return stopLabel;
		}

		if (TryResolveRouteSpeechText(vehicle, out string routeSpeechText))
		{
			return routeSpeechText;
		}

		return SirenChangerMod.GetTransitAnnouncementFallbackSpeech(serviceType);
	}

	// Resolve route speech text from line label or line number.
	private bool TryResolveRouteSpeechText(Entity vehicle, out string routeSpeechText)
	{
		routeSpeechText = string.Empty;
		if (!m_CurrentRouteData.TryGetComponent(vehicle, out CurrentRoute currentRoute) ||
			currentRoute.m_Route == Entity.Null)
		{
			return false;
		}

		string routeLabel = GetOwnerDisplayName(currentRoute.m_Route);
		if (string.IsNullOrWhiteSpace(routeLabel))
		{
			routeLabel = GetEntityDisplayName(currentRoute.m_Route);
		}

		if (!string.IsNullOrWhiteSpace(routeLabel))
		{
			routeSpeechText = routeLabel;
			return true;
		}

		if (TryGetRouteNumberWithOwnerFallback(currentRoute.m_Route, out int routeNumber))
		{
			routeSpeechText = $"Line {routeNumber}";
			return true;
		}

		return false;
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
			return TransitAnnouncementTtsService.NormalizeSpeechText(nameSystem.GetRenderedLabelName(entity));
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

	private static readonly List<ActiveSequence> s_ActiveSequences = new List<ActiveSequence>(kSourcePoolSize);

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
		AudioSource source = GetNextAudioSource();
		if (source == null)
		{
			error = "No announcement audio source is available.";
			return false;
		}

		source.transform.position = position;
		source.Stop();
		RemoveActiveSequenceForSource(source);

		ActiveSequence active = new ActiveSequence
		{
			Source = source,
			Segments = timeline,
			SegmentIndex = 0
		};
		s_ActiveSequences.Add(active);
		StartSegmentOnSource(source, timeline[0]);
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

	// Prefer idle sources, then fall back to round-robin replacement.
	private static AudioSource GetNextAudioSource()
	{
		if (s_AudioSources.Count == 0)
		{
			return null!;
		}

		for (int i = 0; i < s_AudioSources.Count; i++)
		{
			int index = (s_NextSourceIndex + i) % s_AudioSources.Count;
			AudioSource source = s_AudioSources[index];
			if (source != null && !source.isPlaying)
			{
				s_NextSourceIndex = (index + 1) % s_AudioSources.Count;
				return source;
			}
		}

		AudioSource fallback = s_AudioSources[s_NextSourceIndex];
		s_NextSourceIndex = (s_NextSourceIndex + 1) % s_AudioSources.Count;
		return fallback;
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
