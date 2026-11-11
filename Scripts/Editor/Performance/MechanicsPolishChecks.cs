using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;

public static class MechanicsPolishChecks
{
    const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
    static int passed;
    static void Check(bool ok, string label)
    {
        if (!ok) throw new InvalidOperationException(label);
        passed++;
        Debug.Log("MECHANICS POLISH PASS: " + label);
    }
    static FieldInfo Field(object obj, string name) => obj.GetType().GetField(name, PrivateInstance);
    static void Set(object obj, string name, object value) => Field(obj, name).SetValue(obj, value);
    static T Get<T>(object obj, string name) => (T)Field(obj, name).GetValue(obj);
    static object Call(object obj, string method, params object[] args) =>
        obj.GetType().GetMethod(method, PrivateInstance).Invoke(obj, args);

    [MenuItem("Tools/Performance/Run Mechanics Polish Checks")]
    public static void Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Run these checks in Edit Mode.");
        float oldScale = Time.timeScale;
        bool oldPause = PauseMenu.IsOpen;
        MethodInfo pauseSetter = typeof(PauseMenu).GetProperty("IsOpen").GetSetMethod(true);
        var cornerField = typeof(LootWindow).GetField("cornerTexts", PrivateStatic);
        var corners = (IList)cornerField.GetValue(null);
        object[] oldCorners = new object[corners.Count]; corners.CopyTo(oldCorners, 0);
        FieldInfo orderField = typeof(LootWindow).GetField("cornerOrder", PrivateStatic);
        int oldOrder = (int)orderField.GetValue(null);
        FieldInfo cachedPlayerField = typeof(PlayerRef).GetField("cached", PrivateStatic);
        FieldInfo cachedStatsField = typeof(PlayerRef).GetField("stats", PrivateStatic);
        object oldPlayer = cachedPlayerField.GetValue(null), oldStats = cachedStatsField.GetValue(null);
        GameObject fixture = null;
        Material shared = null;
        passed = 0;
        try
        {
            Time.timeScale = 1f;
            pauseSetter.Invoke(null, new object[] { false });
            Check(PlayerActionTiming.CanJump(10f, 9.95f, 9.91f, false, 0.12f, 0.1f), "jump accepts a recent press just after leaving a ledge");
            Check(!PlayerActionTiming.CanJump(10f, 9.95f, 9.7f, false, 0.12f, 0.1f), "jump rejects an expired ledge grace period");
            Check(!PlayerActionTiming.CanJump(10f, 9.7f, 10f, false, 0.12f, 0.1f), "jump rejects an expired input buffer");
            Check(!PlayerActionTiming.CanJump(10f, 9.95f, 9.99f, true, 0.12f, 0.1f), "consuming a jump prevents a second jump during grace");
            Check(!PlayerActionTiming.Within(10f, 11f, 1f), "future timestamps never count as buffered input");
            Check(PlayerActionTiming.CanJump(10f, 9.9f, 10f, false, 0.12f, 0.1f), "a press just before landing remains usable");
            foreach (int fps in new[] { 15, 30, 60, 144 })
            {
                float t = 0f, distance = 0f;
                while (t < 0.5f)
                {
                    float next = Mathf.Min(0.5f, t + 1f / fps);
                    distance += 2f * (PlayerActionTiming.EaseOutDistance(next / 0.5f) - PlayerActionTiming.EaseOutDistance(t / 0.5f));
                    t = next;
                }
                Check(Mathf.Abs(distance - 2f) < 0.0001f, "dodge travels exactly 2m before collisions at " + fps + " FPS");
            }
            Check(PlayerActionTiming.EaseOutDistance(-1f) == 0f && PlayerActionTiming.EaseOutDistance(2f) == 1f, "movement easing clamps frame overshoot");

            fixture = new GameObject("Mechanics Polish Checks") { hideFlags = HideFlags.HideAndDontSave };
            fixture.transform.position = new Vector3(10000f, 10000f, 10000f);
            var inputStats = fixture.AddComponent<PlayerStats>(); inputStats.currentHealth = inputStats.maxHealth;
            cachedPlayerField.SetValue(null, fixture); cachedStatsField.SetValue(null, inputStats);
            GameObject player = new GameObject("Combat fixture"); player.transform.SetParent(fixture.transform, false); player.SetActive(false);
            PlayerCombat combat = player.AddComponent<PlayerCombat>();
            combat.anim = player.AddComponent<Animator>();
            Set(combat, "swingDamagePending", true); Set(combat, "heavyActive", true); Set(combat, "bufferedUntil", 10f);
            int version = Get<int>(combat, "swingVersion");
            combat.CancelAttack();
            Check(!Get<bool>(combat, "swingDamagePending") && !Get<bool>(combat, "heavyActive") && Get<float>(combat, "bufferedUntil") < 0f,
                "interruption clears pending damage, heavy-attack lock and queued clicks");
            Check(Get<int>(combat, "swingVersion") != version, "interruption invalidates old delayed hits");
            combat.hitReactionState = "";
            Set(combat, "swingDamagePending", true);
            combat.PlayHitReaction();
            Check(!Get<bool>(combat, "swingDamagePending"), "damage interrupts attacks even without a flinch animation");
            Set(combat, "isEquipped", true); Set(combat, "swingDamagePending", true); combat.useAnimationEvents = true;
            combat.DealDamageEvent(); combat.DealDamageEvent();
            Check(!Get<bool>(combat, "swingDamagePending"), "animation event consumes its damage window only once");
            Check(!(bool)Call(combat, "HasKickTarget"), "random kicks are rejected when the hit zone has no living target");

            Check(!PlayerActionTiming.CanChain(true, 0.5f, 0.2f), "combo waits for the current timed hit before chaining");
            Check(PlayerActionTiming.CanChain(false, 0.5f, 0.2f), "combo resumes as soon as the hit window is consumed");

            // Own all colliders in a remote fixture position. No existing scene objects are changed.
            for (int i = 0; i < 65; i++)
            {
                GameObject box = new GameObject("Query candidate " + i); box.layer = 2;
                box.transform.SetParent(fixture.transform, false);
                box.transform.localPosition = new Vector3((i % 5) * 0.05f, (i / 5) * 0.02f, 0f);
                box.AddComponent<BoxCollider>().size = Vector3.one * 0.05f;
            }
            Physics.SyncTransforms();
            MethodInfo queryMethod = typeof(PlayerCombat).GetMethod("QueryCombatCandidates", PrivateInstance);
            var query = (Func<Vector3, float, int, int>)Delegate.CreateDelegate(typeof(Func<Vector3, float, int, int>), combat, queryMethod);
            int count = query(fixture.transform.position, 3f, 1 << 2);
            Check(count == 65 && Get<Collider[]>(combat, "combatCandidates").Length >= 128, "overlap buffer expands and retains all 65 colliders in a crowded fight");
            for (int i = 0; i < 20; i++) query(fixture.transform.position, 3f, 1 << 2);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++) query(fixture.transform.position, 3f, 1 << 2);
            long queryBytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Check(queryBytes == 0, "100 warmed combat overlap queries allocate zero managed bytes (" + queryBytes + ")");

            Time.timeScale = 0f;
            Set(combat, "ownsHitStop", true); Set(combat, "appliedHitStopScale", 0.05f); Set(combat, "hitStopRestoreScale", 1f);
            Call(combat, "RestoreHitStop");
            Check(Time.timeScale == 0f, "hit-stop completion cannot undo a pause");
            Time.timeScale = 0.05f;
            Set(combat, "ownsHitStop", true); Set(combat, "hitStopRestoreScale", 0.75f);
            Call(combat, "RestoreHitStop");
            Check(Mathf.Approximately(Time.timeScale, 0.75f), "hit stop restores the previous speed instead of forcing 1");
            Time.timeScale = 1f;
            pauseSetter.Invoke(null, new object[] { true });
            Check(PlayerRef.InputBlocked, "an open pause menu blocks gameplay input even if time keeps running");
            pauseSetter.Invoke(null, new object[] { false });
            PlayerDodge dodge = player.AddComponent<PlayerDodge>();
            Set(dodge, "dodging", true); Set(dodge, "invulnerableUntil", Time.time + 100f);
            Call(dodge, "Awake"); Call(dodge, "OnDisable");
            Check(!PlayerDodge.IsDodging && !PlayerDodge.IsInvulnerable, "disabling dodge clears invulnerability and the singleton");

            // Temporary color changes must preserve shared materials and any pre-existing renderer overrides.
            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube); visual.SetActive(false);
            visual.transform.SetParent(fixture.transform, false);
            EnemyAI_01 enemy = visual.AddComponent<EnemyAI_01>();
            Renderer renderer = visual.GetComponent<Renderer>();
            Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Lit");
            Check(shader != null, "a color-capable shader is available for the material regression check");
            shared = new Material(shader);
            int colorId = shared.HasProperty("_BaseColor") ? Shader.PropertyToID("_BaseColor") : Shader.PropertyToID("_Color");
            renderer.sharedMaterial = shared;
            var originalBlock = new MaterialPropertyBlock(); originalBlock.SetColor(colorId, Color.blue); renderer.SetPropertyBlock(originalBlock, 0);
            Set(enemy, "renderers", new[] { renderer }); Set(enemy, "flashColorProperties", new[] { colorId });
            Set(enemy, "originalFlashBlocks", new[] { new MaterialPropertyBlock() }); Set(enemy, "flashBlock", new MaterialPropertyBlock());
            Call(enemy, "SetColor", Color.red);
            var probe = new MaterialPropertyBlock(); renderer.GetPropertyBlock(probe, 0);
            Check(renderer.sharedMaterial == shared && probe.GetColor(colorId) == Color.red, "hit flash changes color without cloning the enemy material");
            Call(enemy, "RestoreColors"); renderer.GetPropertyBlock(probe, 0);
            Check(renderer.sharedMaterial == shared && probe.GetColor(colorId) == Color.blue, "hit flash restores pre-existing property overrides exactly");
            renderer.SetPropertyBlock(null, 0); Call(enemy, "SetColor", Color.red); Call(enemy, "RestoreColors"); renderer.GetPropertyBlock(probe, 0);
            Check(probe.isEmpty, "hit flash removes its temporary override when the renderer originally had none");

            corners.Clear();
            GameObject textObject = new GameObject("Prompt", typeof(RectTransform)); textObject.SetActive(false); textObject.transform.SetParent(fixture.transform, false);
            var text = textObject.AddComponent<TextMeshProUGUI>();
            typeof(LootWindow).GetMethod("RegisterCornerText", PrivateStatic).Invoke(null, new object[] { textObject.GetComponent<RectTransform>(), text, 0 });
            var arrange = (Action)Delegate.CreateDelegate(typeof(Action), typeof(LootWindow).GetMethod("ArrangeCornerTexts", PrivateStatic));
            for (int i = 0; i < 20; i++) arrange();
            before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) arrange();
            long promptBytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Check(promptBytes == 0, "1000 warmed prompt layout passes allocate zero managed bytes (" + promptBytes + ")");
            Debug.Log("MECHANICS POLISH CHECKS PASSED: " + passed);
        }
        finally
        {
            pauseSetter.Invoke(null, new object[] { oldPause });
            Time.timeScale = oldScale;
            cachedPlayerField.SetValue(null, oldPlayer); cachedStatsField.SetValue(null, oldStats);
            corners.Clear(); foreach (object corner in oldCorners) corners.Add(corner); orderField.SetValue(null, oldOrder);
            if (fixture != null) UnityEngine.Object.DestroyImmediate(fixture);
            if (shared != null) UnityEngine.Object.DestroyImmediate(shared);
        }
    }

    public static void RunBatch()
    {
        try { Run(); EditorApplication.Exit(0); }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
    }
}
