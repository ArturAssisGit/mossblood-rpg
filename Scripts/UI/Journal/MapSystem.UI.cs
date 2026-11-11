using TMPro;
using UnityEngine;
using UnityEngine.UI;

// MapSystem: UI (split out of MapSystem.cs; same class, same behaviour)
public partial class MapSystem
{
    // ---------- UI ----------

    void BuildUI()
    {
        GameObject canvasObject = UIKit.CreateCanvas("MapCanvas", transform, 40);
        Sprite dot = CreateDotSprite();
        dotSprite = dot;

        // Minimap (top-right corner)
        GameObject frame = CreateImage("Minimap", canvasObject.transform, new Color(0f, 0f, 0f, 0.6f), null);
        minimapRoot = frame.GetComponent<RectTransform>();
        minimapRoot.anchorMin = minimapRoot.anchorMax = minimapRoot.pivot = Vector2.one;
        minimapRoot.anchoredPosition = -minimapMargin;
        minimapRoot.sizeDelta = Vector2.one * (minimapSize + 6f);
        frame.AddComponent<RectMask2D>();

        GameObject mapObject = new GameObject("MinimapImage", typeof(RectTransform), typeof(RawImage));
        mapObject.transform.SetParent(frame.transform, false);
        minimapImage = mapObject.GetComponent<RawImage>();
        minimapImage.raycastTarget = false;
        mapObject.GetComponent<RectTransform>().sizeDelta = Vector2.one * minimapSize;
        minimapFog = CreateFogImage("MinimapExplored", frame.transform, minimapSize);

        minimapTent = CreateImage("TentIcon", frame.transform, tentColor, dot).GetComponent<RectTransform>();
        minimapTent.sizeDelta = Vector2.one * 14f;
        minimapDelivery = CreateImage("DeliveryIcon", frame.transform, deliveryColor, dot).GetComponent<RectTransform>();
        minimapDelivery.sizeDelta = Vector2.one * 14f;
        minimapArrow = CreateImage("PlayerDot", frame.transform, playerColor, dot).GetComponent<RectTransform>();
        minimapArrow.sizeDelta = Vector2.one * playerIconSize;

        // Big map (center of the screen): optional background image, the map, optional frame on top
        LootWindow style = LootWindow.Instance;
        float pad = style != null ? style.mapFramePadding : 40f;
        bigMapWindow = new GameObject("BigMapWindow", typeof(RectTransform));
        bigMapWindow.transform.SetParent(canvasObject.transform, false);
        bigMapWindow.GetComponent<RectTransform>().sizeDelta = Vector2.one * bigMapSize;

        if (style != null && style.mapBackgroundSprite != null)
        {
            // (UIKit.CreateImage 9-slices it when the sprite has borders)
            CreateImage("MapBackground", bigMapWindow.transform, style.windowSpriteTint, style.mapBackgroundSprite).GetComponent<RectTransform>().sizeDelta = Vector2.one * (bigMapSize + pad * 2f);
        }

        GameObject big = new GameObject("BigMap", typeof(RectTransform), typeof(RawImage));
        big.transform.SetParent(bigMapWindow.transform, false);
        bigMapRoot = big.GetComponent<RectTransform>();
        bigMapRoot.sizeDelta = Vector2.one * bigMapSize;
        big.GetComponent<RawImage>().raycastTarget = false;
        bigMapFog = CreateFogImage("BigMapExplored", big.transform, bigMapSize);

        bigMapTent = CreateImage("TentIcon", big.transform, tentColor, dot).GetComponent<RectTransform>();
        bigMapTent.sizeDelta = Vector2.one * mapDotSize;
        bigMapDelivery = CreateImage("DeliveryIcon", big.transform, deliveryColor, dot).GetComponent<RectTransform>();
        bigMapDelivery.sizeDelta = Vector2.one * mapDotSize;
        bigMapArrow = CreateImage("PlayerDot", big.transform, playerColor, dot).GetComponent<RectTransform>();
        bigMapArrow.sizeDelta = Vector2.one * (playerIconSize + 6f);

        if (style != null && style.mapFrameSprite != null)
            LootWindow.AddBorder(bigMapWindow.GetComponent<RectTransform>(), style.mapFrameSprite, style.windowBorderColor, pad);
        BuildJournalShell();
        bigMapWindow.SetActive(false);
    }

    // Dark layer over the map, cleared where you have been
    RawImage CreateFogImage(string name, Transform parent, float size)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(RawImage));
        obj.transform.SetParent(parent, false);
        obj.GetComponent<RectTransform>().sizeDelta = Vector2.one * size;
        RawImage image = obj.GetComponent<RawImage>();
        image.raycastTarget = false;
        image.color = Color.white;
        EnsureFog();
        image.texture = fogTexture;
        return image;
    }

    // Image that ignores clicks
    static GameObject CreateImage(string name, Transform parent, Color color, Sprite sprite)
    {
        GameObject obj = UIKit.CreateImage(name, parent, color, sprite);
        obj.GetComponent<Image>().raycastTarget = false;
        return obj;
    }

    static Sprite CreateDotSprite()
    {
        const int size = 32;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(16f, 16f));
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(15f - d)));
            }
        texture.Apply();
        return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }
}
