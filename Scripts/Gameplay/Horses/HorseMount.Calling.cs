using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Animations;
using UnityEngine.Playables;
using static WorldUtil;

// HorseMount: Calling (split out of HorseMount.cs; same class, same behaviour)
public partial class HorseMount
{
    // ---------- Calling ----------

    // The neigh when you call the horse or get on it: 30% quieter than the clip (it was too loud)
    const float NeighVolume = .7f;

    public bool IsRidden => Ridden == this;

    public bool IsPresent => !hidden && isActiveAndEnabled;

    public Bounds AtmosphereBounds => body!=null?body.bounds:new Bounds(transform.position+Vector3.up,new Vector3(1.2f,2,2.4f));

    // The stats of a bought horse (1.0 = a normal horse)

    public void ApplyStats(float speedMultiplier, float staminaMultiplier, float agilityMultiplier)

    {

        walkSpeed *= speedMultiplier;

        trotSpeed *= speedMultiplier;

        gallopSpeed *= speedMultiplier;

        maxStamina *= staminaMultiplier;

        stamina = maxStamina;

        turnSpeedSlow *= agilityMultiplier;

        turnSpeedFast *= agilityMultiplier;

    }

    // Used by HorseSpawner: call a horse that was just created

    public void RequestCall() { pendingCall = true; }

    void CallHorse(bool forceSpawn = false)

    {

        if (Time.time < nextCallAllowed) return;
        // Up a "leave your horse" path the horse can't come
        HorseBarrierZone barrier = player != null ? HorseBarrierZone.At(player.position, out _) : null;
        if (barrier != null) // also a horse just brought from the stable (forceSpawn): it would appear up the path
        {
            nextCallAllowed = Time.time + 1f;
            ShowMessage(barrier.callMessage);
            return;
        }

        nextCallAllowed = Time.time + callCooldown;

        if (whistleSound != null) AudioSource.PlayClipAtPoint(whistleSound, player.position, Volume());

        float distance = Flat(transform.position - player.position).magnitude;

        if (forceSpawn || hidden || distance > comeWithoutTeleport || !gameObject.activeInHierarchy)

        {

            if (!FindSpawnPoint(out Vector3 point))

            {

                ShowMessage(HorseOwnership.NameOf(ownershipKey) + " can't reach you here");

                return;

            }

            Teleport(point, Mathf.Atan2(player.position.x - point.x, player.position.z - point.z) * Mathf.Rad2Deg);

        }

        else if (distance <= arriveDistance + 0.5f)

        {

            return; // already here

        }

        state = State.Coming;

        comingSince = Time.time;

        lastProgressTime = Time.time;

        bestDistance = float.MaxValue;

        nextPathTime = 0f;

        ShowMessage(HorseOwnership.NameOf(ownershipKey) + " is coming");

    }

    void ComeInput(ref float wantedSpeed, ref float wantedHeading)

    {

        Vector3 toPlayer = Flat(player.position - transform.position);

        float distance = toPlayer.magnitude;

        if (distance <= arriveDistance)

        {

            state = State.Idle;

            if (neighSound != null) audioSource.PlayOneShot(neighSound, Volume() * NeighVolume);

            return;

        }

        // Gave up: stuck behind something for a while

        if (distance < bestDistance - 0.5f) { bestDistance = distance; lastProgressTime = Time.time; }

        if (Time.time - lastProgressTime > 4f || Time.time - comingSince > 40f)

        {

            state = State.Idle;

            return;

        }

        if (Time.time >= nextPathTime) RecalculatePath();

        Vector3 target = player.position;

        while (cornerIndex < corners.Count)

        {

            Vector3 corner = corners[cornerIndex];

            if (Flat(corner - transform.position).magnitude > 1.2f || cornerIndex == corners.Count - 1) { target = corner; break; }

            cornerIndex++;

        }

        Vector3 to = Flat(target - transform.position);

        if (to.sqrMagnitude > 0.01f) wantedHeading = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;

        wantedSpeed = distance > 20f ? gallopSpeed * 0.85f : distance > 7f ? trotSpeed : walkSpeed;

        // Brake near the player

        wantedSpeed = Mathf.Min(wantedSpeed, Mathf.Max(walkSpeed * 0.6f, (distance - arriveDistance) * 1.5f));

        float off = Mathf.Abs(Mathf.DeltaAngle(heading, wantedHeading));

        if (off > 70f) wantedSpeed *= 0.4f;

    }

    void RecalculatePath()

    {

        nextPathTime = Time.time + 1f;

        corners.Clear();

        cornerIndex = 0;

        if (NavMesh.SamplePosition(transform.position, out NavMeshHit from, 4f, NavMesh.AllAreas) &&

            NavMesh.SamplePosition(player.position, out NavMeshHit to, 4f, NavMesh.AllAreas) &&

            NavMesh.CalculatePath(from.position, to.position, NavMesh.AllAreas, path) && path.status != NavMeshPathStatus.PathInvalid)

        {

            corners.AddRange(path.corners);

            if (corners.Count > 0) cornerIndex = 1; // corner 0 is where the horse already is

        }

        if (corners.Count == 0) corners.Add(player.position); // no NavMesh: straight to the player

    }

    // A spot 10-16 m away, on the ground, free of trees/rocks, reachable, preferably behind the camera

    bool FindSpawnPoint(out Vector3 point)

    {

        point = Vector3.zero;

        Transform cam = Camera.main != null ? Camera.main.transform : player;

        Vector3 look = Flat(cam.forward).normalized;

        bool navAvailable = NavMesh.SamplePosition(player.position, out NavMeshHit playerNav, 4f, NavMesh.AllAreas);

        float bestScore = float.MinValue;

        bool found = false;

        float start = Random.Range(0f, 360f);

        for (int i = 0; i < 24; i++)

        {

            Vector3 dir = Quaternion.Euler(0f, start + i * 15f, 0f) * Vector3.forward;

            float distance = Random.Range(spawnDistance.x, Mathf.Max(spawnDistance.x, spawnDistance.y));

            Vector3 candidate = player.position + dir * distance;

            if (!RespawnSystem.FindGround(candidate + Vector3.up * 40f, out Vector3 ground)) continue;

            if (Mathf.Abs(ground.y - player.position.y) > 6f) continue;

            if (navAvailable)

            {

                if (!NavMesh.SamplePosition(ground, out NavMeshHit nav, 2f, NavMesh.AllAreas)) continue;

                if (!NavMesh.CalculatePath(nav.position, playerNav.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) continue;

                ground = nav.position;

            }

            if (!SpaceIsFree(ground)) continue;

            float score = -Vector3.Dot(look, dir) + Random.Range(0f, 0.3f); // behind the camera is best

            if (score > bestScore) { bestScore = score; point = ground; found = true; }

        }

        return found;

    }

    bool SpaceIsFree(Vector3 ground)

    {

        int count = Physics.OverlapCapsuleNonAlloc(ground + Vector3.up * 0.8f, ground + Vector3.up * 1.6f, 0.7f, overlapBuffer, groundLayers, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)

        {

            Collider c = overlapBuffer[i];

            if (IsOwnOrPlayer(c) || c is TerrainCollider) continue;

            return false;

        }

        return true;

    }

    void Teleport(Vector3 point, float yaw)

    {

        SetHidden(false);

        body.enabled = false;

        heading = yaw;

        lastHeading = yaw;

        pitch = 0f;

        roll = 0f;

        turnSmooth = 0f;

        speed = 0f;

        verticalVelocity = 0f;

        transform.SetPositionAndRotation(point, NoseRotation(heading) * Quaternion.Euler(0f, -modelYawOffset, 0f));

        body.enabled = true;

        Physics.SyncTransforms();

    }

    void SetHidden(bool hide)

    {

        hidden = hide;

        foreach (Renderer r in renderers) if (r != null) r.enabled = !hide;

        if (body != null) body.enabled = !hide;

        if (animator != null) animator.enabled = !hide;

    }
}
