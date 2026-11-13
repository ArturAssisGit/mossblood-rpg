using System.Collections;
using UnityEngine;

// CampSystem: the setting-up sequence. Fade to black, the camp goes up, then three slow shots of it in its surroundings
// (wide, close on the fire, a rising view over the tent), each fading in and out, and back to you by the fire.
// [Space] skips. Getting hurt cuts it short; the camp stays.
public partial class CampSystem
{
    struct Shot
    {
        public float startAngle, endAngle, startDistance, endDistance, startHeight, endHeight;
        public Vector3 lookFrom, lookTo;
    }

    Transform shotPlayer;
    CampSite shotSite;

    IEnumerator SetUpRoutine(CampPlan plan)
    {
        CinematicActive = true;
        Transform player = PlayerRef.Transform;
        PlayerStats stats = PlayerRef.Stats;
        shotPlayer = player;
        float lastHealth = stats != null ? stats.currentHealth : 0f;
        bool hurt = false, died = false, built = false;
        try
        {
            SoundManager.Play(Sfx.UseItem, player != null ? player.position : Vector3.zero);
            yield return FadeTo(1f, fadeSeconds);
            if (player == null || stats == null || stats.IsDead) { died = stats != null && stats.IsDead; yield break; }

            foreach (PlayerCombat combat in player.GetComponentsInChildren<PlayerCombat>()) combat.ResetCombatState();
            CampSite site = Build(plan);
            if (site == null) yield break;
            built = true;
            shotSite = site;
            if (site.Tent != null) TentSpawn.Selected = site.Tent;
            PlaceAtFire(player, plan, site);
            site.RefreshBounds();

            Shot[] shots = Shots(site);
            float shotSeconds = Mathf.Max(1.5f, (sequenceSeconds - fadeSeconds * 2f) / shots.Length);
            captionLine.text = CaptionLine();
            bool skip = false;
            for (int i = 0; i < shots.Length && !skip && !hurt; i++)
            {
                float t = 0f;
                float edge = Mathf.Min(0.55f, shotSeconds * 0.25f);
                while (t < shotSeconds)
                {
                    t += Time.deltaTime;
                    float k = Mathf.Clamp01(t / shotSeconds);
                    ApplyShot(shots[i], k * k * (3f - 2f * k), site);
                    // Each shot fades in from black and out to black (the first one also lowers the bars)
                    float a = t < edge ? 1f - t / edge : t > shotSeconds - edge ? (t - (shotSeconds - edge)) / edge : 0f;
                    fade.alpha = Mathf.Clamp01(a);
                    bars.alpha = 1f;
                    float barHeight = Mathf.Lerp(0f, 96f, Mathf.Clamp01((i * shotSeconds + t) / 0.8f));
                    topBar.sizeDelta = bottomBar.sizeDelta = new Vector2(0f, barHeight);
                    caption.alpha = i == 0 ? Mathf.Clamp01(Mathf.Min((t - 0.4f) / 0.6f, (shotSeconds - 0.3f - t) / 0.5f)) : 0f;
                    skipHint.gameObject.SetActive(t > 0.6f || i > 0);

                    if (stats == null || stats.IsDead) { died = true; break; }
                    if (stats.currentHealth < lastHealth - 0.01f) { hurt = true; break; }
                    lastHealth = stats.currentHealth;
                    if (Time.timeScale > 0f && (i > 0 || t > 0.6f) && Input.GetKeyDown(KeyCode.Space)) { skip = true; break; }
                    yield return null;
                }
                if (died) break;
            }

            if (died) yield break;
            // Back to the player: black, camera home, then fade in (fast when something attacked you)
            caption.alpha = 0f;
            float outSeconds = hurt ? 0.15f : skip ? 0.3f : 0.2f;
            yield return FadeTo(1f, outSeconds);
            CameraController.ClearOverride();
            bars.alpha = 0f;
            topBar.sizeDelta = bottomBar.sizeDelta = Vector2.zero;
            yield return null; // one frame for the camera to settle behind the player
            yield return FadeTo(0f, hurt ? 0.25f : fadeSeconds + 0.1f);
        }
        finally
        {
            EndCinematic();
            if (died && fade != null) fade.alpha = 0f; // the death screen takes over
        }

        if (!built || died) yield break;
        if (hurt) CenterMessage.Show("Something attacked you!", 2.5f);
        else GameplayHint.Show("Camp set up. Walk up to it and press E to sleep, craft or cook.", 5f);
        if (!hurt && saveWhenPitched && WorldSaveGameManager.instance != null) WorldSaveGameManager.instance.SaveGame();
    }

    IEnumerator PackUpRoutine(CampSite site)
    {
        CinematicActive = true;
        try
        {
            CraftingWindow.CloseIfOpen();
            Transform player = PlayerRef.Transform;
            SoundManager.Play(Sfx.UseItem, player != null ? player.position : site.Center);
            yield return FadeTo(1f, 0.45f);
            if (site != null) Destroy(site.gameObject);
            yield return null;
            yield return FadeTo(0f, 0.55f);
        }
        finally { EndCinematic(); }
        GameplayHint.Show("Camp packed away. Set it up again from your inventory.", 4f);
    }

    void EndCinematic()
    {
        CameraController.ClearOverride();
        CinematicActive = false;
        shotSite = null;
        shotPlayer = null;
        if (bars != null) bars.alpha = 0f;
        if (topBar != null) topBar.sizeDelta = bottomBar.sizeDelta = Vector2.zero;
        if (caption != null) caption.alpha = 0f;
        if (fade != null && !isActiveAndEnabled) fade.alpha = 0f;
    }

    IEnumerator FadeTo(float target, float seconds)
    {
        float speed = 1f / Mathf.Max(0.05f, seconds);
        while (fade != null && !Mathf.Approximately(fade.alpha, target))
        {
            // Pausing the game pauses the sequence too
            if (Time.timeScale > 0f) fade.alpha = Mathf.MoveTowards(fade.alpha, target, Time.unscaledDeltaTime * speed);
            yield return null;
        }
    }

    // While the screen is black: you stand by the fire, facing it
    void PlaceAtFire(Transform player, CampPlan plan, CampSite site)
    {
        if (player == null) return;
        Vector3 toFire = Vector3.ProjectOnPlane(site.Center - plan.stand, Vector3.up);
        Quaternion facing = toFire.sqrMagnitude > 0.01f ? Quaternion.LookRotation(toFire.normalized) : player.rotation;
        Vector3 position = plan.moveStand ? plan.stand + Vector3.up * 0.05f : player.position;
        CharacterController controller = player.GetComponent<CharacterController>();
        if (controller != null) controller.enabled = false;
        player.SetPositionAndRotation(position, facing);
        Physics.SyncTransforms();
        if (controller != null) controller.enabled = true;
    }

    string CaptionLine()
    {
        float hour = WorldUtil.CurrentHour;
        string part = hour < 5f ? "Deep night" : hour < 8f ? "Dawn" : hour < 12f ? "Morning" : hour < 17f ? "Afternoon" : hour < 20f ? "Dusk" : "Night";
        int h = Mathf.FloorToInt(hour) % 24, m = Mathf.FloorToInt((hour - Mathf.Floor(hour)) * 60f) % 60;
        return $"{part}  ·  {h:00}:{m:00}";
    }

    // ---------- Camera ----------

    Shot[] Shots(CampSite site)
    {
        Bounds b = site.WorldBounds();
        float size = Mathf.Clamp(Mathf.Max(b.extents.x, b.extents.z), 1.5f, 8f);
        Vector3 fire = site.Center;
        Vector3 tent = site.TentPart != null ? site.PartBounds(0).center : fire;
        Vector3 table = site.TablePart != null ? site.PartBounds(2).center : fire;
        // Angles around the fire: 0 = from where you stand (behind you), 90 = from the table side, 180 = from behind the tent
        return new[]
        {
            // 1. Wide: the whole camp in its surroundings, drifting slowly round
            new Shot { startAngle = -38f, endAngle = -18f, startDistance = size * 2.2f + 5.5f, endDistance = size * 2f + 4.2f,
                       startHeight = 3.6f, endHeight = 2.9f, lookFrom = fire + Vector3.up * 0.7f, lookTo = fire + Vector3.up * 0.6f },
            // 2. Low and close: the fire, the table behind it
            new Shot { startAngle = 58f, endAngle = 80f, startDistance = 2.6f, endDistance = 2.1f,
                       startHeight = 0.65f, endHeight = 0.85f, lookFrom = fire + Vector3.up * 0.3f, lookTo = Vector3.Lerp(fire, table, 0.35f) + Vector3.up * 0.45f },
            // 3. From behind the tent, rising to show the land around the camp
            new Shot { startAngle = 165f, endAngle = 140f, startDistance = size * 1.4f + 4f, endDistance = size * 2.2f + 7f,
                       startHeight = 1.6f, endHeight = 6f, lookFrom = Vector3.Lerp(tent, fire, 0.6f) + Vector3.up * 0.6f, lookTo = fire + Vector3.up * 0.4f },
        };
    }

    void ApplyShot(Shot shot, float k, CampSite site)
    {
        float yaw = site.Yaw + Mathf.Lerp(shot.startAngle, shot.endAngle, k);
        float distance = Mathf.Lerp(shot.startDistance, shot.endDistance, k);
        float height = Mathf.Lerp(shot.startHeight, shot.endHeight, k);
        Vector3 target = Vector3.Lerp(shot.lookFrom, shot.lookTo, k);
        Vector3 fire = site.Center;
        Vector3 wanted = fire + Quaternion.Euler(0f, yaw, 0f) * Vector3.back * distance + Vector3.up * height;
        Vector3 position = KeepClear(target, wanted, site);
        Vector3 dir = target - position;
        Quaternion rotation = dir.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(dir.normalized) : Quaternion.identity;
        CameraController.SetOverride(position, rotation);
    }

    // The camera never ends up inside a hill, a tree or a rock: it slides in towards the camp instead
    Vector3 KeepClear(Vector3 target, Vector3 wanted, CampSite site)
    {
        if (RespawnSystem.FindGround(wanted + Vector3.up * 30f, out Vector3 ground) && wanted.y < ground.y + 0.45f) wanted.y = ground.y + 0.45f;
        Vector3 delta = wanted - target;
        float distance = delta.magnitude;
        if (distance < 0.1f) return wanted;
        Vector3 dir = delta / distance;
        RaycastHit[] hits = WorldUtil.HitBuffer; // every frame of the camp scene: no new array
        int count = Physics.SphereCastNonAlloc(target, 0.25f, dir, hits, distance, ~0, QueryTriggerInteraction.Ignore);
        float nearest = distance;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = hits[i];
            Transform t = hit.collider.transform;
            if (t.IsChildOf(site.transform) || (shotPlayer != null && t.IsChildOf(shotPlayer))) continue;
            if (hit.collider.GetComponentInParent<CharacterController>() != null) continue;
            if (hit.distance > 0f && hit.distance < nearest) nearest = hit.distance;
        }
        return target + dir * Mathf.Max(1.2f, nearest - 0.3f);
    }
}
