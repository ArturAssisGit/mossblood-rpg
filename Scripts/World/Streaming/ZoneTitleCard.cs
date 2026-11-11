using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Auto-created. When you set foot on a World Zone (its land), its name rises on screen like a chapter title:
// a small line above, the name in wide letters, two thin gold strokes drawing out from it, then it all fades.
// Waits for a clear screen (no black fade, menus, conversations, sleep or camp sequence). The same zone is not
// announced again for a while (walking back and forth along the shore doesn't repeat it).
public sealed class ZoneTitleCard : MonoBehaviour
{
    static ZoneTitleCard instance;

    [Tooltip("Seconds before the same zone can be announced again")] public float repeatAfter = 120f;
    [Tooltip("How far you must leave a zone (meters past its shore) before it counts as leaving")] public float leaveDistance = 25f;

    WorldZone current, pending;
    float pendingSince, nextCheck;
    readonly Dictionary<WorldZone, float> shownAt = new Dictionary<WorldZone, float>();
    CanvasGroup group;
    RectTransform leftLine, rightLine;
    TextMeshProUGUI title, subtitle;
    Coroutine showing;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => instance = null;

    public static void Ensure()
    {
        if (instance == null) instance = WorldUtil.CreatePersistent<ZoneTitleCard>("Zone Title Card");
    }

    void Awake()
    {
        if (instance != null && instance != this) { Destroy(this); return; }
        instance = this;
    }

    void Start() => BuildUI();

    void Update()
    {
        if (group == null || Time.unscaledTime < nextCheck) return;
        nextCheck = Time.unscaledTime + 0.3f;
        WorldSaveGameManager save = WorldSaveGameManager.instance;
        Transform player = PlayerRef.Transform;
        if (player == null || (save != null && save.IsLoading) || TitleScreenManager.IsOpen) { current = null; pending = null; return; }

        Vector3 at = player.position;
        if (current != null && (!current.isActiveAndEnabled || current.DistanceTo(at) > leaveDistance)) current = null;

        WorldZone here = null;
        foreach (WorldZone zone in WorldZone.All)
            if (zone != null && zone.DistanceTo(at) <= 0.01f) { here = zone; break; }
        if (here != null && here != current)
        {
            current = here;
            bool recent = shownAt.TryGetValue(here, out float last) && Time.unscaledTime - last < repeatAfter;
            if (here.showTitle && !string.IsNullOrWhiteSpace(here.zoneName) && !recent) { pending = here; pendingSince = Time.unscaledTime; }
        }

        if (pending == null) return;
        if (Time.unscaledTime - pendingSince > 20f || pending.DistanceTo(at) > leaveDistance) { pending = null; return; }
        if (!ScreenClear()) return;
        shownAt[pending] = Time.unscaledTime;
        if (showing != null) StopCoroutine(showing);
        showing = StartCoroutine(Show(pending.zoneName, pending.subtitle));
        pending = null;
    }

    static bool ScreenClear() =>
        !ScreenFade.IsActive && !RespawnSystem.IsDeathScreenOpen && !SleepSystem.IsSleeping && !CampSystem.CinematicActive
        && !MapSystem.IsMapOpen && !NpcDialogue.IsOpen && !PauseMenu.IsOpen && !MenuWindow.AnyOpen && !CraftingWindow.IsOpen
        && !BossEncounterDirector.IsIntroducing && !MultiplayerSession.PanelOpen && Time.timeScale > 0f;

    // ---------- The card ----------

    IEnumerator Show(string name, string line)
    {
        title.text = name.Trim().ToUpperInvariant();
        subtitle.text = string.IsNullOrWhiteSpace(line) ? "" : line.Trim().ToUpperInvariant();
        float half = Mathf.Min(title.GetPreferredValues(title.text).x, 1300f) * 0.5f;
        const float lineLength = 170f;
        leftLine.anchoredPosition = new Vector2(-half - 34f, 0f);
        rightLine.anchoredPosition = new Vector2(half + 34f, 0f);

        // In: the name settles from wide letters, the strokes draw outwards
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 1.6f)
        {
            float k = Ease(t);
            group.alpha = Mathf.Clamp01(t * 1.6f);
            title.characterSpacing = Mathf.Lerp(34f, 16f, k);
            subtitle.alpha = Mathf.Clamp01((t - 0.35f) / 0.5f);
            leftLine.sizeDelta = rightLine.sizeDelta = new Vector2(lineLength * Ease(Mathf.Clamp01((t - 0.2f) / 0.8f)), 1.6f);
            if (!ScreenClear()) break;
            yield return null;
        }
        group.alpha = 1f;
        title.characterSpacing = 16f;
        subtitle.alpha = 1f;
        leftLine.sizeDelta = rightLine.sizeDelta = new Vector2(lineLength, 1.6f);

        for (float held = 0f; held < 3.6f && ScreenClear(); held += Time.unscaledDeltaTime) yield return null;

        // Out: a slow fade (quick when something else needs the screen)
        float outSeconds = ScreenClear() ? 1.8f : 0.3f;
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / outSeconds)
        {
            group.alpha = 1f - Ease(t);
            title.characterSpacing = Mathf.Lerp(16f, 22f, t);
            yield return null;
        }
        group.alpha = 0f;
        showing = null;
    }

    static float Ease(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

    void BuildUI()
    {
        Transform canvas = UIKit.CreateCanvas("ZoneTitleCanvas", transform, 112).transform; // over the HUD, under the menus

        var root = new GameObject("Zone title", typeof(RectTransform), typeof(CanvasGroup));
        root.transform.SetParent(canvas, false);
        RectTransform rect = (RectTransform)root.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -190f);
        rect.sizeDelta = new Vector2(1600f, 160f);
        group = root.GetComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;
        group.interactable = false;

        // A soft dark haze behind the words, so they read over a bright sky or snow
        Image haze = UIKit.CreateImage("Haze", rect, new Color(0f, 0f, 0f, 0.42f)).GetComponent<Image>();
        haze.sprite = HazeSprite();
        haze.raycastTarget = false;
        UIKit.SetRect(haze.rectTransform, new Vector2(0.18f, 0f), new Vector2(0.82f, 1f), Vector2.zero, Vector2.zero);

        subtitle = UIKit.CreateText("Subtitle", rect, "", 19f, UIStyle.Tan, UIStyle.Font);
        subtitle.characterSpacing = 12f;
        UIKit.SetRect(subtitle.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, 34f), new Vector2(0f, 64f));

        title = UIKit.CreateText("Title", rect, "", 50f, new Color(0.95f, 0.9f, 0.8f), UIStyle.Font);
        title.characterSpacing = 16f;
        title.enableAutoSizing = true;
        title.fontSizeMin = 30f;
        title.fontSizeMax = 50f;
        UIKit.SetRect(title.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(40f, -34f), new Vector2(-40f, 34f));

        leftLine = Stroke("Left stroke", rect, 1f);
        rightLine = Stroke("Right stroke", rect, 0f);
    }

    static RectTransform Stroke(string name, RectTransform parent, float pivotX)
    {
        Color gold = UIStyle.Gold;
        gold.a = 0.75f;
        Image line = UIKit.CreateImage(name, parent, gold).GetComponent<Image>();
        line.raycastTarget = false;
        RectTransform rect = line.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(pivotX, 0.5f);
        rect.sizeDelta = new Vector2(0f, 1.6f);
        return rect;
    }

    static Sprite hazeSprite;
    static Sprite HazeSprite()
    {
        if (hazeSprite != null) return hazeSprite;
        const int w = 128, h = 32;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
        var pixels = new Color32[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w * 2f - 1f, v = (y + 0.5f) / h * 2f - 1f;
                float a = Mathf.Clamp01(1f - Mathf.Sqrt(u * u + v * v * 1.4f));
                a = a * a * (3f - 2f * a);
                pixels[y * w + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        tex.SetPixels32(pixels);
        tex.Apply(false, true);
        hazeSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
        hazeSprite.hideFlags = HideFlags.HideAndDontSave;
        return hazeSprite;
    }
}
