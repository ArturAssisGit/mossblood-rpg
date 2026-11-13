using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// New Game only: a short tutorial, one thing at a time, in a small panel near the bottom of the screen. Each step waits for
// you to do it (walk, run, jump or dodge, draw the sword, strike, open the journal, the quick wheel), ticks itself off and
// the next one comes. It hides while a menu, a conversation, sleep or the death screen is up. F1 skips the rest.
// Started by WorldSaveGameManager after the opening letter; gone on any load / new scene.
public sealed class NewGameTutorial : MonoBehaviour
{
    static NewGameTutorial instance;
    public static bool Running => instance != null;
    const KeyCode SkipKey = KeyCode.F1;

    CanvasGroup group;
    TextMeshProUGUI header, body, footer;
    Image bar, barBack;
    float shownAlpha;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => instance = null;

    public static void Begin()
    {
        if (instance != null) Destroy(instance.gameObject);
        var go = new GameObject("New game tutorial");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<NewGameTutorial>();
    }

    public static void Stop() { if (instance != null) Destroy(instance.gameObject); }

    // ---------------- The steps ----------------
    delegate bool Done(Step s);
    class Step
    {
        public string text; public Done done; public float need; // need > 0: a bar fills (seconds of doing it)
        public float progress; public Vector3 from; public int count;
    }

    static string Key(string k) => $"<color=#e8cf8c><b>[{k}]</b></color>";

    Step[] steps;

    void BuildSteps()
    {
        steps = new[]
        {
            new Step { text = $"Walk with {Key("W")} {Key("A")} {Key("S")} {Key("D")}, look around with the mouse.", done = s => Walked(s, 6f) },
            new Step { text = $"Hold {Key("Shift")} while walking to run. It costs stamina.", need = 1.5f, done = s => Held(s, Input.GetKey(KeyCode.LeftShift) && Moving()) },
            new Step { text = $"{Key("Space")} jumps, {Key("Left Alt")} dodges, {Key("C")} slides while running.", done = s => Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.LeftAlt) || Input.GetKeyDown(KeyCode.C) },
            new Step { text = $"Draw your sword with {Key("1")}. {Key("2")} takes the bow.", done = s => Armed() },
            new Step { text = $"Strike: {Key("Left mouse")} quick, {Key("Right mouse")} heavy. Hold {Key("Q")} to block.", done = s => Counted(s, Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Q), 3) },
            new Step { text = $"{Key("E")} picks things up, opens chests and talks to people.", need = 4f, done = s => Held(s, true) },
            new Step { text = $"Open your journal: {Key("M")} the map, {Key("I")} the inventory.", done = s => MapSystem.IsMapOpen },
            new Step { text = $"Hold {Key("Tab")} for the quick wheel: food, potions and tools at hand.", done = s => QuickWheel.IsOpen || Held(s, false, 8f) },
            new Step { text = "Your father went looking for the five Onistones. Follow his trail: Journal > Objectives, and his letter in the Diary.", need = 7f, done = s => Held(s, true) },
        };
    }

    int index;
    const int JournalStep = 6;
    float stepTime, doneTime = -1f;

    // ---------------- Checks ----------------
    static bool Moving() => Mathf.Abs(Input.GetAxisRaw("Horizontal")) > .1f || Mathf.Abs(Input.GetAxisRaw("Vertical")) > .1f;

    static bool Walked(Step s, float meters)
    {
        Transform p = PlayerRef.Transform;
        if (p == null) return false;
        if (s.count == 0) { s.from = p.position; s.count = 1; }
        Vector3 d = p.position - s.from; d.y = 0f;
        s.progress = Mathf.Clamp01(d.magnitude / meters);
        return d.magnitude >= meters;
    }

    // Done after doing it for 'need' seconds (or after 'timeout' seconds whatever happens)
    bool Held(Step s, bool doing, float timeout = 0f)
    {
        if (doing && s.need > 0f) s.progress = Mathf.Clamp01(s.progress + Time.unscaledDeltaTime / s.need);
        return (s.need > 0f && s.progress >= 1f) || (timeout > 0f && stepTime >= timeout);
    }

    static bool Counted(Step s, bool now, int times)
    {
        if (now) s.count++;
        s.progress = Mathf.Clamp01(s.count / (float)times);
        return s.count >= times;
    }

    static bool Armed()
    {
        Transform p = PlayerRef.Transform;
        var combat = p != null ? p.GetComponentInChildren<PlayerCombat>() : null;
        return combat != null && combat.IsArmed;
    }

    // Nothing to teach while something else is on screen
    static bool Hidden => TitleScreenManager.IsOpen || PauseMenu.IsOpen || RespawnSystem.IsDeathScreenOpen || SleepSystem.IsSleeping
        || ScreenFade.IsActive || NpcDialogue.IsOpen || MenuWindow.AnyOpen || LootWindow.IsOpen;

    // ---------------- Life ----------------
    void Awake()
    {
        BuildSteps();
        var canvas = UIKit.CreateCanvas("Tutorial canvas", transform, 190);
        var panel = UIKit.CreateImage("Panel", canvas.transform, new Color(.05f, .04f, .03f, .78f), LootWindow.RoundedSprite(18));
        panel.GetComponent<Image>().raycastTarget = false;
        var r = panel.GetComponent<RectTransform>();
        r.anchorMin = r.anchorMax = new Vector2(.5f, 0f);
        r.pivot = new Vector2(.5f, 0f);
        r.sizeDelta = new Vector2(820f, 118f);
        r.anchoredPosition = new Vector2(0f, 170f);
        group = panel.AddComponent<CanvasGroup>();
        group.alpha = 0f; group.interactable = false; group.blocksRaycasts = false;

        header = UIKit.CreateText("Header", r, "", 15f, new Color(.73f, .66f, .52f), LootWindow.GameFont, TextAlignmentOptions.MidlineLeft);
        header.characterSpacing = 4f; header.raycastTarget = false;
        UIKit.SetRect(header.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(26f, -34f), new Vector2(-26f, -10f));
        footer = UIKit.CreateText("Skip", r, $"{SkipKey}  skip", 14f, new Color(.6f, .55f, .45f), LootWindow.GameFont, TextAlignmentOptions.MidlineRight);
        footer.raycastTarget = false;
        UIKit.SetRect(footer.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(26f, -34f), new Vector2(-26f, -10f));
        body = UIKit.CreateText("Text", r, "", 23f, UIStyle.Cream, LootWindow.GameFont, TextAlignmentOptions.MidlineLeft);
        body.richText = true; body.raycastTarget = false; body.textWrappingMode = TextWrappingModes.Normal;
        body.enableAutoSizing = true; body.fontSizeMin = 16f; body.fontSizeMax = 23f;
        UIKit.SetRect(body.rectTransform, Vector2.zero, Vector2.one, new Vector2(26f, 18f), new Vector2(-26f, -36f));
        barBack = UIKit.CreateImage("Bar back", r, new Color(1f, 1f, 1f, .08f), LootWindow.RoundedSprite(3)).GetComponent<Image>();
        barBack.raycastTarget = false;
        UIKit.SetRect(barBack.rectTransform, Vector2.zero, new Vector2(1f, 0f), new Vector2(26f, 10f), new Vector2(-26f, 14f));
        bar = UIKit.CreateImage("Bar", barBack.rectTransform, new Color(.91f, .78f, .45f, .9f), LootWindow.RoundedSprite(3)).GetComponent<Image>();
        bar.raycastTarget = false;
        UIKit.SetRect(bar.rectTransform, Vector2.zero, new Vector2(0f, 1f), Vector2.zero, Vector2.zero);
        SceneManager.sceneLoaded += SceneLoaded;
        stepTime = -1.5f; // a breath after the world appears
        Show();
    }

    void SceneLoaded(Scene s, LoadSceneMode m) { if (m == LoadSceneMode.Single) Destroy(gameObject); } // a load or another new game: this one is over
    void OnDestroy() { SceneManager.sceneLoaded -= SceneLoaded; if (instance == this) instance = null; }

    void Show()
    {
        header.text = $"FIRST STEPS   {index + 1} / {steps.Length}";
        body.text = steps[index].text;
        body.color = UIStyle.Cream;
    }

    void Update()
    {
        if (Input.GetKeyDown(SkipKey)) { Destroy(gameObject); return; }
        bool covered = Hidden || MapSystem.IsMapOpen; // never drawn over a menu or the journal
        shownAlpha = Mathf.MoveTowards(shownAlpha, covered || stepTime < 0f ? 0f : 1f, Time.unscaledDeltaTime * 3f);
        group.alpha = shownAlpha;
        Step s = steps[index];
        bar.rectTransform.anchorMax = new Vector2(s.progress, 1f);
        barBack.gameObject.SetActive(s.progress > 0f);

        // Ticked off: green for a moment, then the next one
        if (doneTime >= 0f)
        {
            doneTime += Time.unscaledDeltaTime;
            if (doneTime < .9f) return;
            doneTime = -1f;
            index++;
            if (index >= steps.Length) { Destroy(gameObject); return; }
            stepTime = 0f;
            Show();
            return;
        }

        // The journal step ticks even with the journal open; every other step waits while something covers the screen
        if (Hidden && index != JournalStep) return;
        stepTime += Time.unscaledDeltaTime;
        if (stepTime < .6f) return; // never ticked by a key still held from the step before
        if (s.done(s))
        {
            s.progress = 1f;
            bar.rectTransform.anchorMax = Vector2.one;
            body.text = "<color=#73d160>Done.</color>  " + s.text;
            UISounds.Tab();
            doneTime = 0f;
        }
    }
}
