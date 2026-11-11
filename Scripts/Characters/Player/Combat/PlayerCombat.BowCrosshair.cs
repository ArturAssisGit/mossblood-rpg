using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// PlayerCombat: Bow crosshair (split out of PlayerCombat.cs; same class, same behaviour)
public partial class PlayerCombat
{
    // ---------- Bow crosshair ----------

    void UpdateCrosshair(bool visible)
    {
        if (!visible)
        {
            if (crosshair != null) crosshair.enabled = false;
            if (arrowLabel != null) arrowLabel.enabled = false;
            return;
        }

        if (crosshair == null)
        {
            GameObject canvasObj = new GameObject("Crosshair Canvas");
            Canvas canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;

            GameObject dot = new GameObject("Crosshair");
            dot.transform.SetParent(canvasObj.transform, false);
            crosshair = dot.AddComponent<Image>();
            crosshair.raycastTarget = false;
            RectTransform rect = crosshair.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;

            arrowLabel = UIKit.CreateText("Arrows", canvasObj.transform, "", 20f, Color.white, UIStyle.Font);
            arrowLabel.raycastTarget = false;
            RectTransform label = arrowLabel.rectTransform;
            label.anchorMin = label.anchorMax = label.pivot = new Vector2(0.5f, 0.5f);
            label.anchoredPosition = new Vector2(0f, -34f);
            label.sizeDelta = new Vector2(220f, 30f);
        }

        crosshair.sprite = crosshairSprite != null ? crosshairSprite : GetDotSprite();
        crosshair.color = crosshairColor;
        crosshair.rectTransform.sizeDelta = Vector2.one * Mathf.Max(1f, crosshairSize);
        crosshair.enabled = true;
        UpdateArrowLabel();
    }

    // "18 / 30" under the crosshair; red when empty, with the time until the next arrow
    TextMeshProUGUI arrowLabel;
    int shownArrows = -1, shownSeconds = -1;

    void UpdateArrowLabel()
    {
        if (arrowLabel == null) return;
        arrowLabel.enabled = true;
        int count = ArrowCount, seconds = count == 0 ? Mathf.CeilToInt(NextArrowIn) : -1;
        if (count == shownArrows && seconds == shownSeconds) return; // text only changes when the numbers do
        shownArrows = count; shownSeconds = seconds;
        arrowLabel.text = count > 0 ? $"{count} / {MaxArrows}" : $"0 / {MaxArrows}  ({seconds}s)";
        arrowLabel.color = count > 0 ? new Color(1f, 1f, 1f, 0.85f) : new Color(1f, 0.45f, 0.4f, 0.95f);
    }

    static Sprite GetDotSprite()
    {
        if (dotSprite != null) return dotSprite;
        const int size = 32;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        float r = size * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(r - Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r)))));
        tex.Apply();
        return dotSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }

    void OnDestroy()
    {
        DestroySwordAbilityHUD();
        if (crosshair != null) Destroy(crosshair.canvas.gameObject);
        foreach (ParticleSystem ps in sparkPool) if (ps != null) Destroy(ps.gameObject);
        foreach (ParticleSystem ps in activeSparks) if (ps != null) Destroy(ps.gameObject);
    }

    void SpawnSparks(Vector3 position, int count)
    {
        ParticleSystem ps = null;
        while (sparkPool.Count > 0 && ps == null) ps = sparkPool.Pop();
        if (ps == null) ps = CreateSparkSystem();

        ps.transform.position = position;
        ps.gameObject.SetActive(true);
        sparkBurst[0] = new ParticleSystem.Burst(0f, (short)count);
        var emission = ps.emission;
        emission.SetBursts(sparkBurst);
        ps.Clear();
        ps.Play();

        activeSparks.Add(ps);
        sparkReleaseAt.Add(Time.unscaledTime + 1f);
    }

    void ReleaseSparks()
    {
        for (int i = activeSparks.Count - 1; i >= 0; i--)
        {
            ParticleSystem ps = activeSparks[i];
            if (ps != null && Time.unscaledTime < sparkReleaseAt[i]) continue;
            if (ps != null)
            {
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ps.gameObject.SetActive(false);
                sparkPool.Push(ps);
            }
            activeSparks.RemoveAt(i);
            sparkReleaseAt.RemoveAt(i);
        }
    }

    ParticleSystem CreateSparkSystem()
    {
        GameObject go = new GameObject("HitSparks");
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.playOnAwake = false;
        main.duration = 0.2f;
        main.loop = false;
        main.useUnscaledTime = true; // keeps flying during the freeze frame
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.08f, 0.18f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.004f, 0.01f);
        main.startColor = sparkColor;
        main.gravityModifier = 1.5f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 40;

        var emission = ps.emission;
        emission.rateOverTime = 0f;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.02f;

        ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.velocityScale = 0.015f;
        renderer.lengthScale = 0.5f;
        if (sparkMaterial == null) sparkMaterial = ParticleMaterials.Create("HitSparks", ParticleMaterials.SoftDot(), true);
        renderer.sharedMaterial = sparkMaterial;

        go.SetActive(false);
        return ps;
    }

    Vector3 HitCenter() => attackPoint != null ? attackPoint.position : transform.position + transform.forward * attackDistance + Vector3.up * attackHeight;

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(HitCenter(), attackRadius);
    }
}
