using System.Collections.Generic;
using UnityEngine;

// Footsteps of the enemies (and bosses), heard from a distance: one small manager for all of them.
// Each enemy's step comes from how far it actually moved (works for patrols, chases, knock-backs and for the copies
// of a co-op host's enemies). Only enemies within earshot (~28 m from the camera) are looked at closely; the ground
// under the foot is checked only on the frame a step sounds. Bigger enemies take longer, louder steps.
public sealed class EnemyFootsteps : MonoBehaviour
{
    static EnemyFootsteps instance;
    sealed class Walker { public Vector3 last; public float distance; public bool seen; }
    readonly Dictionary<EnemyAI_01, Walker> walkers = new Dictionary<EnemyAI_01, Walker>();
    readonly List<EnemyAI_01> gone = new List<EnemyAI_01>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => instance = null;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap() { if (instance == null && FindAnyObjectByType<EnemyFootsteps>() == null) WorldUtil.CreatePersistent<EnemyFootsteps>("Enemy footsteps"); }

    void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
    }
    void OnDestroy() { if (instance == this) instance = null; }

    void Update()
    {
        FootstepLibrary lib = Footsteps.Library;
        if (lib == null || TitleScreenManager.IsOpen || WorldActivitySystem.Loading) return;
        float dt = Time.deltaTime;
        if (dt <= 0f) return; // paused
        Vector3 ear = Footsteps.ListenerPosition();
        float earshot = lib.othersFar + 4f;

        foreach (var pair in walkers) pair.Value.seen = false;
        foreach (EnemyAI_01 enemy in EnemyAI_01.All)
        {
            if (enemy == null || !enemy.isActiveAndEnabled) continue;
            Vector3 position = enemy.transform.position;
            if (!walkers.TryGetValue(enemy, out Walker w)) { w = new Walker { last = position, distance = Random.value }; walkers[enemy] = w; }
            w.seen = true;
            Vector3 moved = position - w.last; moved.y = 0f;
            w.last = position;
            if (enemy.IsDead || (position - ear).sqrMagnitude > earshot * earshot) { w.distance = 0f; continue; }
            float step = moved.magnitude;
            if (step > 3f) { w.distance = 0f; continue; } // teleported / respawned: not a step
            float speed = step / dt;
            if (speed < .4f) { w.distance = Mathf.Min(w.distance, .3f); continue; } // standing: the next step comes soon after moving

            float size = Mathf.Clamp(enemy.transform.lossyScale.y, .7f, 2.5f);
            bool running = speed > 2.6f * size;
            w.distance += step;
            float stride = (running ? lib.othersRunStep : lib.othersWalkStep) * size;
            if (w.distance < stride) continue;
            w.distance = 0f;
            Footsteps.Distant(position, enemy.transform, running, Mathf.Lerp(1f, 1.6f, Mathf.InverseLerp(1f, 2.5f, size)));
        }

        // forget enemies that are gone
        gone.Clear();
        foreach (var pair in walkers) if (pair.Key == null || !pair.Value.seen) gone.Add(pair.Key);
        foreach (EnemyAI_01 e in gone) walkers.Remove(e);
    }
}
