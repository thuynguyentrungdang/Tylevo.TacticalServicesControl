using Comfort.Common;
using Cysharp.Threading.Tasks;
using EFT;
using EFT.Ballistics;
using System;
using System.Threading;
using Systems.Effects;
using UnityEngine;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public static class A10TracerPlayback
{
	// Host raycasts capped tracers at this range; segments that reached it hit
	// nothing, so no impact effect should spawn for them.
	private const float NoHitDistanceThreshold = 1395f;
	private const float ImpactRaycastSlack = 5f;
	private static bool s_impactEffectFailureLogged;

	public static void Play(
		A10TracerSegment[] segments,
		float fireStartNetworkTime,
		CancellationToken cancellationToken,
		bool spawnImpactEffects = false)
	{
		if (segments == null || segments.Length == 0)
		{
			return;
		}

		A10TracerSegment[] orderedSegments = new A10TracerSegment[segments.Length];
		Array.Copy(segments, orderedSegments, segments.Length);
		Array.Sort(orderedSegments, (left, right) => left.DelaySeconds.CompareTo(right.DelaySeconds));
		PlayAsync(orderedSegments, fireStartNetworkTime, cancellationToken, spawnImpactEffects).Forget();
	}

	private static async UniTaskVoid PlayAsync(
		A10TracerSegment[] segments,
		float fireStartNetworkTime,
		CancellationToken cancellationToken,
		bool spawnImpactEffects)
	{
		try
		{
			foreach (A10TracerSegment segment in segments)
			{
				if (!segment.IsValid)
				{
					continue;
				}

				float waitSeconds = fireStartNetworkTime + segment.DelaySeconds - Time.time;
				if (waitSeconds > 0f)
				{
					await UniTask.WaitForSeconds(waitSeconds, cancellationToken: cancellationToken);
				}

				if (cancellationToken.IsCancellationRequested)
				{
					return;
				}

				A10Behaviour.RenderVisualTracerSegment(segment);
				if (spawnImpactEffects)
				{
					SpawnImpactEffect(segment);
				}
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			FireSupportPlugin.LogSource?.LogWarning($"A-10 tracer playback failed. {ex}");
		}
	}

	// Non-host clients never simulate the host's ballistics, so they get no
	// impact effects from the real bullets. Recreate them locally: the segment
	// carries the host's hit point, and the map geometry matches, so a local
	// raycast finds the same surface and material.
	private static void SpawnImpactEffect(A10TracerSegment segment)
	{
		try
		{
			float hostHitDistance = Vector3.Distance(segment.ProjectileOrigin, segment.TracerEnd);
			if (hostHitDistance >= NoHitDistanceThreshold)
			{
				return;
			}

			Effects effects = Singleton<Effects>.Instance;
			if (effects == null)
			{
				return;
			}

			if (!Physics.Raycast(
				    segment.ProjectileOrigin,
				    segment.ProjectileDirection,
				    out RaycastHit hit,
				    hostHitDistance + ImpactRaycastSlack,
				    ~0,
				    QueryTriggerInteraction.Ignore))
			{
				return;
			}

			BallisticCollider ballisticCollider =
				hit.collider.GetComponent<BallisticCollider>() ??
				hit.collider.GetComponentInParent<BallisticCollider>();
			MaterialType material = ballisticCollider != null
				? ballisticCollider.TypeOfMaterial
				: MaterialType.Concrete;

			effects.Emit(
				material,
				ballisticCollider,
				hit.point,
				hit.normal,
				1f,
				isKnife: false,
				isHitPointVisible: true,
				EPointOfView.FirstPerson);
		}
		catch (Exception ex)
		{
			if (!s_impactEffectFailureLogged)
			{
				s_impactEffectFailureLogged = true;
				FireSupportPlugin.LogSource?.LogWarning($"A-10 impact effect spawn failed; visuals skipped. {ex}");
			}
		}
	}
}
