using System.Collections.Generic;
using MobileUDB.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.EventSystems;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace MobileUDB
{
    // Real fingers and Alt use the same identity-stable gesture transitions.
    public sealed class TouchNavigation : MonoBehaviour
    {
        public MobileEditor editor;
        public MapViewport view;
        readonly HashSet<int> tracked = new HashSet<int>();
        readonly HashSet<int> observed = new HashSet<int>();
        readonly List<PointerContact> contacts = new List<PointerContact>();
        readonly PointerGestureState gesture = new PointerGestureState();
        readonly List<RaycastResult> raycasts = new List<RaycastResult>();
        bool altHeld, mouseAccepted, altAccepted, backLatched, focused = true, paused;
        Vector2 anchor;
        static MapPoint Point(Vector2 p) { return new MapPoint(p.x,p.y); }
        static Vector2 Screen(MapPoint p) { return new Vector2(p.x,p.y); }
        void OnEnable() { EnhancedTouchSupport.Enable(); CancelAll(); }
        void OnDisable() { EnhancedTouchSupport.Disable(); CancelAll(); }
        void OnApplicationFocus(bool focus) { focused = focus; if (!focus) CancelAll(); }
        void OnApplicationPause(bool pause) { paused = pause; if (pause) CancelAll(); }
        public void CancelForLayout() { CancelAll(); }
        void CancelAll()
        {
            if (editor != null) editor.CancelDrag();
            tracked.Clear(); contacts.Clear(); gesture.CancelUntilRelease();
            altHeld = mouseAccepted = altAccepted = false;
            if (view != null && view.fingers.Count > 0) { view.fingers.Clear(); view.SetVerticesDirty(); }
        }
        bool StartsOnMap(Vector2 position)
        {
            if (!view.ScreenRect.Contains(position)) return false;
            if (EventSystem.current == null) return true;
            raycasts.Clear(); EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = position }, raycasts);
            return raycasts.Count > 0 && (raycasts[0].gameObject == view.gameObject || editor.IsVisualViewport(raycasts[0].gameObject));
        }
        void Update()
        {
            if (view == null || editor == null) return;
            bool hadFingers = view.fingers.Count > 0;
            contacts.Clear(); view.fingers.Clear();
            if (!focused || paused) { CancelAll(); if (hadFingers) view.SetVerticesDirty(); return; }
            var keyboard = Keyboard.current;
            bool backPressed = keyboard != null && keyboard.escapeKey.wasPressedThisFrame;
            bool backHeld = keyboard != null && keyboard.escapeKey.isPressed;
            if (!backPressed && !backHeld) backLatched = false;
            if (backPressed && !backLatched)
            { backLatched = true; editor.HandleBack(); CancelAll(); if (hadFingers) view.SetVerticesDirty(); return; }
            if (editor.ModalOpen) { CancelAll(); if (hadFingers) view.SetVerticesDirty(); return; }
            var touches = Touch.activeTouches;
            var mouse = Mouse.current;
            bool alt = keyboard != null && (keyboard.leftAltKey.isPressed || keyboard.rightAltKey.isPressed);
            bool physicalHeld = alt || backHeld || (mouse != null && mouse.leftButton.isPressed);
            foreach (var touch in touches)
                if (touch.phase != UnityEngine.InputSystem.TouchPhase.Ended && touch.phase != UnityEngine.InputSystem.TouchPhase.Canceled) physicalHeld = true;
            if (!gesture.AcceptFrame(physicalHeld)) { if (hadFingers) view.SetVerticesDirty(); return; }
            int? releasedId = null; MapPoint releasePosition = default(MapPoint);
            if (touches.Count > 0)
            {
                foreach (var touch in touches)
                {
                    int id = touch.touchId;
                    if (touch.phase == UnityEngine.InputSystem.TouchPhase.Canceled && tracked.Contains(id))
                    { CancelAll(); if (hadFingers) view.SetVerticesDirty(); return; }
                    if (touch.phase == UnityEngine.InputSystem.TouchPhase.Began && StartsOnMap(touch.screenPosition)) tracked.Add(id);
                    bool ended = touch.phase == UnityEngine.InputSystem.TouchPhase.Ended || touch.phase == UnityEngine.InputSystem.TouchPhase.Canceled;
                    if (ended)
                    {
                        if (tracked.Contains(id) && gesture.Single && id == gesture.FirstId && touch.phase == UnityEngine.InputSystem.TouchPhase.Ended)
                        { releasedId = id; releasePosition = Point(touch.screenPosition); }
                        tracked.Remove(id); continue;
                    }
                    if (tracked.Contains(id)) contacts.Add(new PointerContact(id,Point(touch.screenPosition)));
                }
                // A reused vanished ID needs a new Began event. Reuse buffers;
                // avoid allocating a closure per contact on every phone frame.
                observed.Clear(); foreach (var contact in contacts) observed.Add(contact.id);
                tracked.IntersectWith(observed);
                altHeld = mouseAccepted = altAccepted = false;
            }
            else
            {
                tracked.Clear();
                if (mouse != null)
                {
                    Vector2 p = mouse.position.ReadValue();
                    if (alt && !altHeld) { anchor = p; altAccepted = StartsOnMap(p); }
                    if (mouse.leftButton.wasPressedThisFrame) mouseAccepted = StartsOnMap(p);
                    if (mouse.leftButton.wasReleasedThisFrame && gesture.Single && gesture.FirstId == -1 && !alt)
                    { releasedId = -1; releasePosition = Point(p); }
                    if (!mouse.leftButton.isPressed) mouseAccepted = false;
                    if (alt && altAccepted) { contacts.Add(new PointerContact(-2,Point(anchor))); contacts.Add(new PointerContact(-1,Point(p))); }
                    else if (!alt && mouse.leftButton.isPressed && mouseAccepted) contacts.Add(new PointerContact(-1,Point(p)));
                    altHeld = alt;
                    if (!alt && !mouse.leftButton.isPressed && !gesture.Pair) editor.PreviewDrawingPointer(p);
                    if (!alt && !mouse.leftButton.isPressed && !gesture.Pair && StartsOnMap(p))
                    {
                        float wheel = mouse.scroll.ReadValue().y;
                        if (Mathf.Abs(wheel) > 0.01f) editor.WheelNavigation(p,wheel);
                    }
                }
            }
            var step = gesture.Advance(contacts,releasedId,releasePosition);
            switch (step.action)
            {
                case PointerAction.Begin: editor.BeginPointer(Screen(step.position)); break;
                case PointerAction.Move: editor.MovePointer(Screen(step.previous),Screen(step.position)); break;
                case PointerAction.End: editor.EndPointer(Screen(step.position)); break;
                case PointerAction.Cancel: editor.CancelDrag(); break;
            }
            if (step.pair)
            {
                if (step.navigate) editor.GestureNavigation(Screen(step.previous),Screen(step.previousB),Screen(step.position),Screen(step.positionB));
                view.fingers.Add(Screen(step.position)); view.fingers.Add(Screen(step.positionB));
            }
            if (hadFingers || view.fingers.Count > 0) view.SetVerticesDirty();
        }
    }
}
