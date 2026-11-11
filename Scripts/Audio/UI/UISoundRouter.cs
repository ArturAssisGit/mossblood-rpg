using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Hover and click sounds for EVERY button of the game without touching each window: while the mouse cursor is
// visible, it looks at what is under the pointer (only when the mouse moves) and
//  - plays a soft hover tick when the pointer reaches a new button / dialogue option / slot / tab,
//  - plays a click when a button is clicked, unless that click already made a more specific sound (a tab, equipping,
//    buying, looting...). Disabled buttons (e.g. the tab already open) stay silent.
// One raycast through the UI per mouse movement; nothing at all while the cursor is hidden (playing).
public sealed class UISoundRouter : MonoBehaviour
{
    static UISoundRouter instance;
    readonly List<RaycastResult> results = new List<RaycastResult>();
    PointerEventData pointer;
    EventSystem pointerSystem;
    GameObject hovered, pressed;
    Vector3 lastMouse = new Vector3(-1f, -1f, 0f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => instance = null;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap() { if (instance == null && FindAnyObjectByType<UISoundRouter>() == null) WorldUtil.CreatePersistent<UISoundRouter>("UI sounds"); }

    void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
    }
    void OnDestroy() { if (instance == this) instance = null; }

    void LateUpdate()
    {
        EventSystem events = EventSystem.current;
        if (events == null || !Cursor.visible || UISounds.Library == null) { hovered = pressed = null; return; }

        Vector3 mouse = Input.mousePosition;
        bool moved = (mouse - lastMouse).sqrMagnitude > .5f;
        bool down = Input.GetMouseButtonDown(0), up = Input.GetMouseButtonUp(0);
        if (moved || down || up)
        {
            lastMouse = mouse;
            GameObject target = Interactive(events, mouse);
            if (target != hovered)
            {
                hovered = target;
                if (target != null && moved && !Input.GetMouseButton(0)) UISounds.Hover();
            }
        }
        if (down) pressed = hovered;
        if (up)
        {
            // the click happened this frame (EventSystem runs before LateUpdate): a specific sound already answered?
            if (pressed != null && pressed == hovered && UISounds.LastActionFrame != Time.frameCount) UISounds.Click();
            pressed = null;
        }
    }

    // The button / option / slot under the pointer (null = none, or a disabled one)
    GameObject Interactive(EventSystem events, Vector3 mouse)
    {
        if (pointer == null || pointerSystem != events) { pointer = new PointerEventData(events); pointerSystem = events; }
        pointer.position = mouse;
        results.Clear();
        events.RaycastAll(pointer, results);
        if (results.Count == 0) return null;
        GameObject top = results[0].gameObject;
        if (top == null) return null;

        Selectable selectable = top.GetComponentInParent<Selectable>();
        if (selectable != null)
        {
            if (selectable is Scrollbar) return null; // dragging a scroll bar is not a button
            return selectable.IsInteractable() && selectable.isActiveAndEnabled ? selectable.gameObject : null;
        }
        // code-made rows and slots that react to clicks without being a Button
        IPointerClickHandler clickable = top.GetComponentInParent<IPointerClickHandler>();
        return clickable is Component c && c != null ? c.gameObject : null;
    }
}
