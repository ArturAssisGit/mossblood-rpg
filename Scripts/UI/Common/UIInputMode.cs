using System;
using UnityEngine;

// Mouse or keys / gamepad? A button that is only "selected" (the menu's first button, or the last one clicked) must not
// look hovered while you use the mouse: that look belongs to the button under the pointer. The selection shows only
// while you move through the menus with the arrow keys, Tab or a gamepad; moving or clicking the mouse hides it again.
// Creates itself, one tiny check per frame.
public sealed class UIInputMode : MonoBehaviour
{
    // true = moving with keys / gamepad (the selected button lights up); false = using the mouse
    public static bool Keyboard { get; private set; }
    // Raised when Keyboard changes (buttons refresh their look)
    public static event Action Changed;
    static UIInputMode instance;
    Vector3 lastMouse;
    bool hasMouse;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Keyboard = false; Changed = null; instance = null; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (instance != null) return;
        var go = new GameObject("UI input mode");
        go.hideFlags = HideFlags.HideInHierarchy;
        DontDestroyOnLoad(go);
        instance = go.AddComponent<UIInputMode>();
    }

    void Update()
    {
        Vector3 mouse = Input.mousePosition;
        bool pointer = Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Mathf.Abs(Input.mouseScrollDelta.y) > .01f
                       || (hasMouse && (mouse - lastMouse).sqrMagnitude > 9f);
        lastMouse = mouse; hasMouse = true;
        if (pointer) { Set(false); return; }
        if (NavigationPressed()) Set(true);
    }

    // Arrows, Tab and the gamepad everywhere; W / S / A / D only on the title screen (in the game they walk)
    static bool NavigationPressed()
    {
        if (!Input.anyKeyDown) return false;
        if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.LeftArrow)
            || Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.Tab)) return true;
        for (KeyCode k = KeyCode.JoystickButton0; k <= KeyCode.JoystickButton19; k++) if (Input.GetKeyDown(k)) return true;
        return TitleScreenManager.IsOpen && (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.D));
    }

    static void Set(bool keyboard)
    {
        if (Keyboard == keyboard) return;
        Keyboard = keyboard;
        Changed?.Invoke();
    }
}
