using System.Collections.Generic;
using UnityEngine;

// Only active impacts are ticked. Disabled completely when no enemy is reacting.
[DefaultExecutionOrder(-50)]
public class EnemyHitReactionDriver : MonoBehaviour
{
    static EnemyHitReactionDriver instance;
    readonly List<EnemyAI_01> active = new List<EnemyAI_01>();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => instance = null;
    public static void Add(EnemyAI_01 enemy)
    {
        if (instance == null) instance = WorldUtil.CreatePersistent<EnemyHitReactionDriver>("Enemy hit reactions");
        if (!instance.active.Contains(enemy)) instance.active.Add(enemy);
        instance.enabled = true;
    }
    public static void Remove(EnemyAI_01 enemy)
    {
        enemy.ClearHitLean();
        if (instance == null) return;
        instance.active.Remove(enemy);
        if (instance.active.Count == 0) instance.enabled = false;
    }
    void Update()
    {
        // Remove only our previous angular offsets before the next Animator evaluation.
        foreach (var enemy in active) if (enemy != null) enemy.RestoreHitLeanPose();
    }
    void LateUpdate()
    {
        for (int i = active.Count - 1; i >= 0; i--)
        {
            var enemy = active[i];
            if (enemy == null || !enemy.StepHitLean(Time.deltaTime)) active.RemoveAt(i);
        }
        if (active.Count == 0) enabled = false;
    }
    void OnDisable()
    {
        foreach (var enemy in active) if (enemy != null) enemy.ClearHitLean();
        active.Clear();
    }
    void OnDestroy() { if (instance == this) instance = null; }
}
