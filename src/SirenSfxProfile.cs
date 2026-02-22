using System;
using System.Runtime.Serialization;
using Game.Prefabs.Effects;
using Unity.Mathematics;
using UnityEngine;

namespace SirenChanger;

[DataContract]
// Serializable set of SFX parameters copied to/from game siren prefabs.
public sealed class SirenSfxProfile
{
	private const float kComparisonEpsilon = 0.0001f;

	[DataMember(Order = 1)]
	public float Volume { get; set; } = 1f;

	[DataMember(Order = 2)]
	public float Pitch { get; set; } = 1f;

	[DataMember(Order = 3)]
	public float SpatialBlend { get; set; } = 1f;

	[DataMember(Order = 4)]
	public float Doppler { get; set; } = 1f;

	[DataMember(Order = 5)]
	public float Spread { get; set; } = 0f;

	[DataMember(Order = 6)]
	public float MinDistance { get; set; } = 1f;

	[DataMember(Order = 7)]
	public float MaxDistance { get; set; } = 200f;

	[DataMember(Order = 8)]
	public bool Loop { get; set; } = true;

	[DataMember(Order = 9)]
	public AudioRolloffMode RolloffMode { get; set; } = AudioRolloffMode.Linear;

	[DataMember(Order = 10)]
	public float FadeInSeconds { get; set; }

	[DataMember(Order = 11)]
	public float FadeOutSeconds { get; set; }

	[DataMember(Order = 12)]
	public bool RandomStartTime { get; set; }

	// Safe baseline if source prefab values are unavailable.
	public static SirenSfxProfile CreateFallback()
	{
		return new SirenSfxProfile().ClampCopy();
	}

	// Snapshot runtime SFX values into a serializable profile.
	public static SirenSfxProfile FromSfx(SFX sfx)
	{
		return new SirenSfxProfile
		{
			Volume = sfx.m_Volume,
			Pitch = sfx.m_Pitch,
			SpatialBlend = sfx.m_SpatialBlend,
			Doppler = sfx.m_Doppler,
			Spread = sfx.m_Spread,
			MinDistance = sfx.m_MinMaxDistance.x,
			MaxDistance = sfx.m_MinMaxDistance.y,
			Loop = sfx.m_Loop,
			RolloffMode = sfx.m_RolloffMode,
			FadeInSeconds = sfx.m_FadeTimes.x,
			FadeOutSeconds = sfx.m_FadeTimes.y,
			RandomStartTime = sfx.m_RandomStartTime
		}.ClampCopy();
	}

	// Apply profile values to the live SFX component.
	public void ApplyTo(SFX sfx)
	{
		ClampInPlace();
		sfx.m_Volume = Volume;
		sfx.m_Pitch = Pitch;
		sfx.m_SpatialBlend = SpatialBlend;
		sfx.m_Doppler = Doppler;
		sfx.m_Spread = Spread;
		sfx.m_MinMaxDistance = new float2(MinDistance, MaxDistance);
		sfx.m_Loop = Loop;
		sfx.m_RolloffMode = RolloffMode;
		sfx.m_FadeTimes = new float2(FadeInSeconds, FadeOutSeconds);
		sfx.m_RandomStartTime = RandomStartTime;
	}

	// Duplicate this profile instance.
	public SirenSfxProfile Clone()
	{
		return new SirenSfxProfile
		{
			Volume = Volume,
			Pitch = Pitch,
			SpatialBlend = SpatialBlend,
			Doppler = Doppler,
			Spread = Spread,
			MinDistance = MinDistance,
			MaxDistance = MaxDistance,
			Loop = Loop,
			RolloffMode = RolloffMode,
			FadeInSeconds = FadeInSeconds,
			FadeOutSeconds = FadeOutSeconds,
			RandomStartTime = RandomStartTime
		};
	}

	// Duplicate and normalize values into legal ranges.
	public SirenSfxProfile ClampCopy()
	{
		SirenSfxProfile clone = Clone();
		clone.ClampInPlace();
		return clone;
	}

	// Enforce safe bounds before applying to Unity audio fields.
	public void ClampInPlace()
	{
		Volume = Mathf.Clamp01(Volume);
		Pitch = Mathf.Clamp(Pitch, -3f, 3f);
		SpatialBlend = Mathf.Clamp01(SpatialBlend);
		Doppler = Mathf.Clamp01(Doppler);
		Spread = Mathf.Clamp(Spread, 0f, 360f);
		MinDistance = Mathf.Max(0f, MinDistance);
		MaxDistance = Mathf.Max(MinDistance + 0.01f, MaxDistance);
		FadeInSeconds = Mathf.Max(0f, FadeInSeconds);
		FadeOutSeconds = Mathf.Max(0f, FadeOutSeconds);
	}

	// Compare two profiles with epsilon tolerance for float fields.
	internal bool ApproximatelyEquals(SirenSfxProfile? other)
	{
		if (other == null)
		{
			return false;
		}

		return Mathf.Abs(Volume - other.Volume) <= kComparisonEpsilon &&
			Mathf.Abs(Pitch - other.Pitch) <= kComparisonEpsilon &&
			Mathf.Abs(SpatialBlend - other.SpatialBlend) <= kComparisonEpsilon &&
			Mathf.Abs(Doppler - other.Doppler) <= kComparisonEpsilon &&
			Mathf.Abs(Spread - other.Spread) <= kComparisonEpsilon &&
			Mathf.Abs(MinDistance - other.MinDistance) <= kComparisonEpsilon &&
			Mathf.Abs(MaxDistance - other.MaxDistance) <= kComparisonEpsilon &&
			Loop == other.Loop &&
			RolloffMode == other.RolloffMode &&
			Mathf.Abs(FadeInSeconds - other.FadeInSeconds) <= kComparisonEpsilon &&
			Mathf.Abs(FadeOutSeconds - other.FadeOutSeconds) <= kComparisonEpsilon &&
			RandomStartTime == other.RandomStartTime;
	}
}

// Captures original siren state so replacements can be reverted cleanly.
internal sealed class SirenSfxSnapshot
{
	public AudioClip Clip { get; }

	public SirenSfxProfile Profile { get; }

	private SirenSfxSnapshot(AudioClip clip, SirenSfxProfile profile)
	{
		Clip = clip;
		Profile = profile;
	}

	// Build a snapshot from a live prefab SFX component.
	public static SirenSfxSnapshot FromSfx(SFX sfx)
	{
		return new SirenSfxSnapshot(sfx.m_AudioClip, SirenSfxProfile.FromSfx(sfx));
	}

	// Restore original clip and SFX tuning values.
	public void Restore(SFX sfx)
	{
		Profile.ApplyTo(sfx);
		sfx.m_AudioClip = Clip;
	}
}
