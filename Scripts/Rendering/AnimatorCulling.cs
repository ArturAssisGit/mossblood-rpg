using System.Collections.Generic;
using UnityEngine;

// Villagers, animals and birds kept animating their whole skeleton every frame even when nobody could see them
// (behind you, behind a house, far away). This switches only those to "Cull Update Transforms": their state machine,
// timers and root motion keep running exactly as before, but the bones are only posed while they are on screen.
// What you see is identical. The player, enemies (their weapons hit with bones), horses and remote players are never touched.
public class AnimatorCulling : MonoBehaviour
{
    static AnimatorCulling instance;
    readonly HashSet<Animator> seen = new HashSet<Animator>();
    float nextScan;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => instance = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (instance != null) return;
        instance = WorldUtil.CreatePersistent<AnimatorCulling>("AnimatorCulling");
    }

    void Update()
    {
        if (Time.unscaledTime < nextScan) return;
        nextScan = Time.unscaledTime + 4f; // new villagers/animals/birds appear now and then
        foreach (Animator animator in FindObjectsByType<Animator>(FindObjectsInactive.Exclude))
        {
            if (animator == null || !seen.Add(animator)) continue;
            if (!Background(animator)) continue;
            if (animator.cullingMode == AnimatorCullingMode.AlwaysAnimate) animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
        }
        seen.RemoveWhere(a => a == null);
    }

    // Only scenery characters: never anything that fights, is ridden or is a player
    static bool Background(Animator a)
    {
        if (a.GetComponentInParent<PlayerMovement>() != null || a.GetComponentInParent<EnemyAI_01>() != null
            || a.GetComponentInParent<HorseMount>() != null || a.GetComponentInParent<RemotePlayerAvatar>() != null) return false;
        return a.GetComponentInParent<NpcDialogue>() != null || a.GetComponentInParent<ShopNpc>() != null
            || a.GetComponentInParent<DeliveryNpc>() != null || a.GetComponentInParent<NpcTalk>() != null
            || a.GetComponentInParent<FoxAI>() != null || a.GetComponentInParent<BirdAI>() != null
            || a.GetComponentInParent<GroveBirds>() != null || a.GetComponentInParent<DistantSkyBirds>() != null;
    }
}
