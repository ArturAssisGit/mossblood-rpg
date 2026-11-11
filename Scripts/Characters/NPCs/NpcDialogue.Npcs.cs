using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// NpcDialogue: NPCs (split out of NpcDialogue.cs; same class, same behaviour)
public partial class NpcDialogue
{
    // ---------- NPCs ----------

    static List<GameObject> AllNpcs()
    {
        npcs.Clear();
        seen.Clear();
        foreach (ShopNpc n in ShopNpc.All) AddNpc(n);
        foreach (HorseStable n in HorseStable.All) AddNpc(n);
        foreach (DeliveryNpc n in DeliveryNpc.All) AddNpc(n);
        foreach (NpcTalk n in NpcTalk.All) AddNpc(n);
        return npcs;
    }

    static void AddNpc(Component c)
    {
        if (c != null && c.gameObject.activeInHierarchy && seen.Add(c.gameObject)) npcs.Add(c.gameObject);
    }

    public static string NameOf(GameObject go)
    {
        if (go == null) return "";
        NpcTalk talk = go.GetComponent<NpcTalk>();
        if (talk != null && !string.IsNullOrEmpty(talk.npcName)) return talk.npcName;
        DeliveryNpc courier = go.GetComponent<DeliveryNpc>();
        if (courier != null && !string.IsNullOrEmpty(courier.npcName)) return courier.npcName;
        ShopNpc shop = go.GetComponent<ShopNpc>();
        if (shop != null && !string.IsNullOrEmpty(shop.merchantName)) return shop.merchantName;
        HorseStable stable = go.GetComponent<HorseStable>();
        if (stable != null && !string.IsNullOrEmpty(stable.stableName)) return stable.stableName;
        return go.name;
    }

    static float RangeOf(GameObject go)
    {
        NpcTalk talk = go.GetComponent<NpcTalk>();
        if (talk != null && talk.interactRange > 0f) return talk.interactRange;
        float range = 0f;
        ShopNpc shop = go.GetComponent<ShopNpc>();
        if (shop != null) range = Mathf.Max(range, shop.interactRange);
        HorseStable stable = go.GetComponent<HorseStable>();
        if (stable != null) range = Mathf.Max(range, stable.interactRange);
        DeliveryNpc courier = go.GetComponent<DeliveryNpc>();
        if (courier != null) range = Mathf.Max(range, courier.interactRange);
        return range > 0f ? range : 2.5f;
    }

    // Name + place, so two NPCs with the same name still differ (the same key a courier already uses for deliveries)
    public static string SaveKeyOf(GameObject go) => go != null ? KeyOf(go) : "";

    static string KeyOf(GameObject go)
    {
        if (keys.TryGetValue(go, out string key)) return key;
        DeliveryNpc courier = go.GetComponent<DeliveryNpc>();
        Vector3 home = NpcIdle.HomeOf(go.transform); // where it was placed, not where it is taking a few steps
        key = courier != null ? courier.SaveKey : NameOf(go) + "@" + Mathf.RoundToInt(home.x) + "," + Mathf.RoundToInt(home.z);
        keys[go] = key;
        return key;
    }

    static bool OffersTravel(GameObject go)
    {
        NpcTalk talk = go.GetComponent<NpcTalk>();
        return talk != null && talk.fastTravel;
    }

    static bool ShowsOnMap(GameObject go)
    {
        NpcTalk talk = go.GetComponent<NpcTalk>();
        if (talk != null) return talk.showOnMap;
        DeliveryNpc courier = go.GetComponent<DeliveryNpc>();
        return courier == null || courier.showOnMap;
    }

    public static bool IsDiscovered(GameObject go) => Instance != null && go != null && Instance.discovered.Contains(KeyOf(go));

    // The NPCs you have found, for the big map (the NPC of your delivery has its own yellow icon, so it is left out)
    public static void GetMapMarkers(List<MapMarker> into)
    {
        into.Clear();
        if (Instance == null) return;
        foreach (GameObject go in AllNpcs())
        {
            if (!ShowsOnMap(go) || !Instance.discovered.Contains(KeyOf(go))) continue;
            if (DeliverySystem.IsTarget(go.GetComponent<DeliveryNpc>())) continue;
            into.Add(new MapMarker { position = go.transform.position, name = NameOf(go), fastTravel = OffersTravel(go) });
        }
    }
}
