using TMPro;
using UnityEngine;

public partial class MapSystem
{
    [Header("Guiding wind destination")]
    public Color waypointColor = new Color(.2f, .14f, .09f, 1f);
    [Tooltip("Horizontal distance in meters at which the destination is reached.")]
    public float waypointArrivalRadius = 5f;
    public bool HasWaypoint { get; private set; }
    public Vector3 WaypointPosition { get; private set; }
    RectTransform waypointIcon, minimapWaypoint;
    TextMeshProUGUI waypointWords;
    Vector3 mapPressScreen;
    bool waypointPressed, waypointDragged;
    float waypointReadoutAt;

    public void ClearWaypoint()
    {
        HasWaypoint = false;
        waypointPressed = false;
        if (waypointIcon != null) waypointIcon.gameObject.SetActive(false);
        if (minimapWaypoint != null) minimapWaypoint.gameObject.SetActive(false);
    }

    public bool SetWaypoint(Vector3 position)
    {
        if (!hasMap || !RuntimeValues.IsFinite(position)) return false;
        Vector2 uv = WorldToUV(position);
        if (!GuidingWindMath.InMap(uv)) return false;
        WaypointPosition = position;
        HasWaypoint = true;
        waypointReadoutAt = 0f;
        GameplayHint.Show("Destination marked. Follow the wind.", 3f);
        return true;
    }

    void HandleWaypointInput(bool insideViewport)
    {
        // Test against the map image too: the viewport also contains margins around the map.
        bool inside = insideViewport && RectTransformUtility.RectangleContainsScreenPoint(bigMapRoot, Input.mousePosition, null);
        if (inside && Input.GetMouseButtonDown(1)) ClearWaypoint();
        if (inside && Input.GetMouseButtonDown(0))
        {
            mapPressScreen = Input.mousePosition;
            waypointPressed = true;
            waypointDragged = false;
        }
        if (waypointPressed && (Input.mousePosition - mapPressScreen).sqrMagnitude > 36f) waypointDragged = true;
        if (!Input.GetMouseButtonUp(0)) return;
        if (waypointPressed && !waypointDragged && inside && Mathf.Abs(Input.mouseScrollDelta.y) < .001f
            && RectTransformUtility.ScreenPointToLocalPointInRectangle(bigMapRoot, Input.mousePosition, null, out Vector2 local))
        {
            // Inverse of WorldToUV; RectTransformUtility accounts for pan, zoom and canvas scale.
            Vector2 uv = new Vector2(local.x / bigMapSize + .5f, local.y / bigMapSize + .5f);
            if (GuidingWindMath.InMap(uv))
            {
                Vector3 world = GuidingWindMath.MapPoint(uv, mapMin, mapWorldSize);
                if (WorldUtil.TerrainAt(world, out Terrain ground) && ground != null)
                    world.y = ground.GetPosition().y + ground.SampleHeight(world);
                SetWaypoint(world);
            }
        }
        waypointPressed = false;
    }

    void UpdateWaypointIcons(Vector2 playerUV)
    {
        if (!HasWaypoint) return;
        if (waypointIcon == null)
        {
            waypointIcon = CreateImage("Wind destination", bigMapRoot, waypointColor, dotSprite).GetComponent<RectTransform>();
            waypointIcon.sizeDelta = Vector2.one * 19f;
            var core = CreateImage("Center", waypointIcon, parchmentStyle ? new Color(.9f, .84f, .7f) : new Color(.13f, .1f, .04f), dotSprite).GetComponent<RectTransform>();
            core.sizeDelta = Vector2.one * 8f;
            waypointWords = CreateDotLabel(waypointIcon, "", waypointColor);
            waypointWords.raycastTarget = false;
            waypointWords.gameObject.SetActive(true);
            minimapWaypoint = CreateImage("Wind destination", minimapRoot, waypointColor, dotSprite).GetComponent<RectTransform>();
            minimapWaypoint.sizeDelta = Vector2.one * 11f;
        }
        waypointIcon.gameObject.SetActive(bigMapOpen && journalTab == JournalTab.Map && !journalSubwindow);
        waypointIcon.anchoredPosition = (WorldToUV(WaypointPosition) - Vector2.one * .5f) * bigMapSize;
        waypointIcon.localScale = Vector3.one / journalMapZoom;
        waypointIcon.SetAsLastSibling();
        if (waypointIcon.gameObject.activeInHierarchy && Time.unscaledTime >= waypointReadoutAt)
        {
            waypointReadoutAt = Time.unscaledTime + .2f;
            float distance = GuidingWindMath.FlatDistance(player.position, WaypointPosition);
            waypointWords.text = $"Destination · {distance:0} m";
        }
        waypointWords.gameObject.SetActive(true);
        bool mini = minimapRoot.gameObject.activeSelf;
        minimapWaypoint.gameObject.SetActive(mini);
        if (mini)
        {
            Vector2 p = (WorldToUV(WaypointPosition) - playerUV) * mapWorldSize / minimapWorldSize * minimapSize;
            float limit = minimapSize * .5f - 9f;
            float span = Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.y));
            if (span > limit) p *= limit / span;
            minimapWaypoint.anchoredPosition = p;
        }
    }
}
