using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MobileUDB
{
    // Holding reveals a label without executing the command on release.
    public sealed class MobileIconButton : Button
    {
        public string hint;
        public Action<string> showHint;
        public Action holdAction;
        public float holdSeconds = .55f;
        bool held, revealed;
        float pressedAt;
        public override void OnPointerDown(PointerEventData data)
        {
            base.OnPointerDown(data);
            if (data.button != PointerEventData.InputButton.Left || !IsInteractable()) return;
            held = true; revealed = false; pressedAt = Time.unscaledTime;
        }
        void Update()
        {
            if (!held || revealed || Time.unscaledTime - pressedAt <= holdSeconds) return;
            revealed = true; if (holdAction != null) holdAction(); else if (showHint != null) showHint(hint);
        }
        public override void OnPointerUp(PointerEventData data) { held = false; base.OnPointerUp(data); }
        public override void OnPointerExit(PointerEventData data) { held = false; base.OnPointerExit(data); }
        void OnApplicationFocus(bool focus) { if (!focus) { held = false; revealed = true; } }
        void OnApplicationPause(bool pause) { if (pause) { held = false; revealed = true; } }
        public override void OnPointerClick(PointerEventData data)
        {
            if (revealed) { revealed = false; return; } base.OnPointerClick(data);
        }
        protected override void DoStateTransition(SelectionState state, bool instant)
        {
            base.DoStateTransition(state, instant);
            var icon = GetComponentInChildren<EditorIcon>();
            if (icon != null) icon.color = new Color32(255, 255, 255, state == SelectionState.Disabled ? (byte)80 : (byte)255);
        }
        protected override void OnDisable() { held = false; revealed = false; base.OnDisable(); }
    }
}
