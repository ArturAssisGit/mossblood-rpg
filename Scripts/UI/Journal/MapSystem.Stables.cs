using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Stables are always drawn on the big map, even under the fog: the journey really starts with a horse, so a new
// player always has a first place to head for. Until you own a horse the stable is highlighted (soft pulsing halo,
// its name always written) and the Objectives page offers to mark it as your destination (the guiding wind follows).
public partial class MapSystem
{
    [Header("Stables (always on the map, even unexplored)")]
    public Color stableColor = new Color(0.86f, 0.58f, 0.24f, 1f);

    readonly List<RectTransform> stableDots = new List<RectTransform>();
    readonly List<TextMeshProUGUI> stableLabels = new List<TextMeshProUGUI>();
    readonly List<Image> stableHalos = new List<Image>();
    static bool stableHintShown;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStableHint() => stableHintShown = false;
    float stableHintAt = -1f;

    // No horse yet while stables sell them: guide the player there
    public static bool NeedsFirstHorse => HorseOwnership.Required && !HorseOwnership.OwnsAny && HorseStable.All.Count > 0;

    void UpdateStableDots()
    {
        bool guide = NeedsFirstHorse;
        int used = 0;
        float pulse = 0.35f + 0.3f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 3.2f));
        foreach (HorseStable stable in HorseStable.All)
        {
            if (stable == null || !stable.isActiveAndEnabled) continue;
            if (used >= stableDots.Count) CreateStableDot();
            RectTransform dot = stableDots[used];
            string name = string.IsNullOrWhiteSpace(stable.stableName) ? "Stable" : stable.stableName;
            string text = guide ? name + "\n<size=80%>Get your horse here</size>" : name;
            if (stableLabels[used].text != text) stableLabels[used].text = text;
            Image halo = stableHalos[used];
            halo.gameObject.SetActive(guide);
            if (guide) halo.color = new Color(stableColor.r, stableColor.g, stableColor.b, pulse);
            PlaceIcon(dot, stable.transform.position, (uv) => (uv - new Vector2(0.5f, 0.5f)) * bigMapSize, bigMapSize * 0.5f);
            dot.localScale = Vector3.one / Mathf.Max(1f, journalMapZoom);
            used++;
        }
        for (int i = used; i < stableDots.Count; i++) stableDots[i].gameObject.SetActive(false);
    }

    void CreateStableDot()
    {
        // A dark ring with a warm amber centre, a little bigger than the other dots: it's the one to look for
        RectTransform ring = CreateImage("StableIcon", bigMapRoot, new Color(0.12f, 0.08f, 0.04f, 0.95f), dotSprite).GetComponent<RectTransform>();
        ring.sizeDelta = Vector2.one * (mapDotSize + 4f);
        RectTransform halo = CreateImage("Halo", ring, stableColor, dotSprite).GetComponent<RectTransform>();
        halo.sizeDelta = Vector2.one * (mapDotSize * 2.6f);
        halo.SetAsFirstSibling();
        halo.GetComponent<Image>().raycastTarget = false;
        RectTransform core = CreateImage("Dot", ring, stableColor, dotSprite).GetComponent<RectTransform>();
        core.sizeDelta = Vector2.one * (mapDotSize - 1f);
        TextMeshProUGUI label = CreateDotLabel(ring, "", stableColor);
        stableDots.Add(ring);
        stableLabels.Add(label);
        stableHalos.Add(halo.GetComponent<Image>());
    }

    // While guiding, the stable's name stays written; afterwards it shows on hover like every other place
    void StableLabelsAfterHover(bool inside, Vector2 mouse, ref float bestDistance, ref RectTransform bestDot, ref TextMeshProUGUI bestLabel)
    {
        if (NeedsFirstHorse)
        {
            for (int i = 0; i < stableDots.Count && i < stableLabels.Count; i++)
                stableLabels[i].gameObject.SetActive(stableDots[i].gameObject.activeSelf);
            return;
        }
        HoverCheck(stableDots, stableLabels, inside, mouse, ref bestDistance, ref bestDot, ref bestLabel);
    }

    // The stable nearest to the player (for the Objectives "mark it" button)
    public static bool NearestStable(Vector3 from, out Vector3 position)
    {
        position = default;
        float best = float.MaxValue;
        foreach (HorseStable stable in HorseStable.All)
        {
            if (stable == null || !stable.isActiveAndEnabled) continue;
            float d = (stable.transform.position - from).sqrMagnitude;
            if (d < best) { best = d; position = stable.transform.position; }
        }
        return best < float.MaxValue;
    }

    public void MarkNearestStable()
    {
        if (player == null || !NearestStable(player.position, out Vector3 stable)) return;
        SetWaypoint(stable);
    }

    // A single nudge a few seconds into a new journey, never again once you own a horse
    void TickStableHint()
    {
        if (stableHintShown || !NeedsFirstHorse || WorldActivitySystem.Loading || TitleScreenManager.IsOpen || ScreenFade.IsActive) { stableHintAt = -1f; return; }
        if (stableHintAt < 0f) { stableHintAt = Time.unscaledTime + 8f; return; }
        if (Time.unscaledTime < stableHintAt) return;
        stableHintShown = true;
        GameplayHint.Show("Find the stable and get a horse. It's marked on your map (M).", 6f);
    }
}
