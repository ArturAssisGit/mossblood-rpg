using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

// Independent, non-interactive feedback for item use and navigation. No hotbar or input bindings.
public sealed class GameplayHint : MonoBehaviour
{
    static GameplayHint instance;
    TextMeshProUGUI label;
    Coroutine animation;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => instance = null;
    public static void Show(string message, float seconds = 1.5f)
    {
        if (string.IsNullOrEmpty(message)) { if (instance != null) instance.Clear(); return; }
        if (instance == null) WorldUtil.CreatePersistent<GameplayHint>("Gameplay feedback");
        if (instance == null) return;
        instance.Clear();
        instance.label.text = message;
        instance.animation = instance.StartCoroutine(instance.Display(Mathf.Clamp(seconds, .3f, 8f)));
    }
    void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        var canvas = UIKit.CreateCanvas("Gameplay feedback canvas", transform, 195);
        label = UIKit.CreateText("Message", canvas.transform, "", 22f, UIStyle.Cream, LootWindow.GameFont);
        label.textWrappingMode = TextWrappingModes.Normal;
        label.raycastTarget = false;
        label.outlineWidth = .2f; label.outlineColor = Color.black; label.alpha = 0;
        UIKit.SetRect(label.rectTransform, new Vector2(.15f, 0), new Vector2(.85f, 0), new Vector2(0, 90), new Vector2(0, 150));
        SceneManager.sceneLoaded += SceneLoaded;
    }
    IEnumerator Display(float duration)
    {
        float start = Time.unscaledTime;
        while (Time.unscaledTime - start < duration)
        {
            float t = Time.unscaledTime - start;
            bool hidden = TitleScreenManager.IsOpen || RespawnSystem.IsDeathScreenOpen || SleepSystem.IsSleeping;
            label.alpha = hidden ? 0 : Mathf.Min(Mathf.Clamp01(t / .12f), Mathf.Clamp01((duration - t) / .25f));
            yield return null;
        }
        label.alpha = 0; animation = null;
    }
    void Clear() { if (animation != null) StopCoroutine(animation); animation = null; if (label != null) { label.text = ""; label.alpha = 0; } }
    void SceneLoaded(Scene scene, LoadSceneMode mode) => Clear();
    void OnDestroy() { SceneManager.sceneLoaded -= SceneLoaded; if (instance == this) instance = null; }
}
