using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MobileUDB
{
    // Only the dialog moves: opening the IME must not pan or resize the map.
    public sealed class MobileKeyboardLayout : MonoBehaviour
    {
        RectTransform overlay;
        InputField lastField;
        float lastFraction = -1;
        readonly Vector3[] corners = new Vector3[4];
        RectTransform card, scrollRect, footerRect, feedbackRect;
        Text heading;
        RectState cardState, footerState, feedbackState;
        float normalBottom;
        bool compact;
        struct RectState
        {
            public Vector2 min, max; Vector2 pivot, low, high;
            public RectState(RectTransform rect) { min=rect.anchorMin; max=rect.anchorMax; pivot=rect.pivot; low=rect.offsetMin; high=rect.offsetMax; }
            public void Restore(RectTransform rect) { rect.anchorMin=min; rect.anchorMax=max; rect.pivot=pivot; rect.offsetMin=low; rect.offsetMax=high; }
        }
#if UNITY_ANDROID && !UNITY_EDITOR
        AndroidJavaClass bridge;
        int token;
        bool unavailable;
#endif
        void Awake()
        {
            overlay = (RectTransform)transform;
            card = transform.Find("Dialog") as RectTransform;
            if (card == null) return;
            cardState = new RectState(card);
            scrollRect = card.Find("Scroll") as RectTransform;
            if (scrollRect != null) normalBottom = scrollRect.offsetMin.y;
            foreach (var button in card.GetComponentsInChildren<Button>())
            {
                var rect = (RectTransform)button.transform;
                if (rect.parent == card && rect.anchorMin.y == 0 && rect.anchorMax.y == 0) { footerRect = rect; footerState = new RectState(rect); break; }
            }
            foreach (var text in card.GetComponentsInChildren<Text>(true))
                if (text.transform.parent == card)
                {
                    if (text.name == "Apply feedback") { feedbackRect = text.rectTransform; feedbackState = new RectState(feedbackRect); }
                    else heading = text;
                }
        }
        void OnEnable() { StartSampling(); }
        void OnDisable() { StopSampling(); }
        void OnApplicationPause(bool pause) { if (pause) StopSampling(); else if (isActiveAndEnabled) StartSampling(); }
        void StartSampling()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (unavailable || bridge != null) return;
            try
            {
                bridge = new AndroidJavaClass("org.mobileudb.KeyboardBridge");
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                    token = bridge.CallStatic<int>("start", activity);
            }
            catch (Exception error) { StopSampling(); unavailable = true; Debug.LogWarning("Не удалось получить область клавиатуры Android: " + error.Message); }
#endif
        }
        void StopSampling()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (bridge == null) return;
            try { bridge.CallStatic("stop", token); }
            catch (Exception error) { Debug.LogWarning("Не удалось остановить наблюдение клавиатуры: " + error.Message); }
            finally { bridge.Dispose(); bridge = null; }
#endif
        }
        void LateUpdate()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (bridge != null) ApplyOcclusion(bridge.CallStatic<float>("bottomFraction", token));
#endif
        }
        // Native coordinates are relative to the Android content view, so a
        // resized window reports zero rather than subtracting the IME twice.
        internal void ApplyOcclusion(float fraction)
        {
            if (float.IsNaN(fraction) || float.IsInfinity(fraction)) fraction = 0;
            fraction = Mathf.Clamp01(fraction);
            var parent = overlay.parent as RectTransform;
            if (parent == null || Screen.height <= 0) return;
            parent.GetWorldCorners(corners);
            float bottom = RectTransformUtility.WorldToScreenPoint(null, corners[0]).y;
            float top = RectTransformUtility.WorldToScreenPoint(null, corners[2]).y;
            float hidden = Mathf.Clamp01((fraction * Screen.height - bottom) / Mathf.Max(1, top - bottom));
            bool resized = !Mathf.Approximately(lastFraction, hidden);
            if (resized) { overlay.anchorMin = new Vector2(0, hidden); lastFraction = hidden; }
            bool chromeChanged = AdaptChrome(hidden > 0 && parent.rect.height * (1-hidden) < 330);
            var selected = EventSystem.current == null ? null : EventSystem.current.currentSelectedGameObject;
            var field = selected == null ? null : selected.GetComponent<InputField>();
            if (field != null && (!field.isFocused || !field.transform.IsChildOf(transform))) field = null;
            if (resized || chromeChanged || field != lastField)
            {
                Canvas.ForceUpdateCanvases();
                if (field != null) Reveal(field);
            }
            lastField = field;
        }
        bool AdaptChrome(bool small)
        {
            if (card == null || scrollRect == null) return false;
            bool changed = small != compact;
            if (small)
            {
                card.anchorMin = new Vector2(cardState.min.x, 0); card.anchorMax = new Vector2(cardState.max.x, 1);
                // In landscape the footer shares one row with Close. Keeping
                // both at 56 units leaves room for a complete numeric field.
                if (footerRect != null)
                {
                    footerRect.anchorMin = new Vector2(0,1); footerRect.anchorMax = Vector2.one;
                    footerRect.pivot = new Vector2(.5f,1); footerRect.sizeDelta = new Vector2(-124,56); footerRect.anchoredPosition = new Vector2(-58,-4);
                    if (heading != null) heading.gameObject.SetActive(false);
                }
                float bottom = feedbackRect != null && feedbackRect.gameObject.activeSelf ? 36 : 12;
                changed |= !Mathf.Approximately(scrollRect.offsetMin.y,bottom);
                scrollRect.offsetMin = new Vector2(scrollRect.offsetMin.x,bottom); scrollRect.offsetMax = new Vector2(scrollRect.offsetMax.x,-64);
                if (feedbackRect != null)
                {
                    feedbackRect.anchorMin = Vector2.zero; feedbackRect.anchorMax = new Vector2(1,0);
                    feedbackRect.pivot = new Vector2(.5f,0); feedbackRect.sizeDelta = new Vector2(-24,32); feedbackRect.anchoredPosition = Vector2.zero;
                }
            }
            else if (compact)
            {
                cardState.Restore(card);
                if (footerRect != null) footerState.Restore(footerRect);
                if (heading != null) heading.gameObject.SetActive(true);
                if (feedbackRect != null) feedbackState.Restore(feedbackRect);
                scrollRect.offsetMin = new Vector2(scrollRect.offsetMin.x,feedbackRect != null && feedbackRect.gameObject.activeSelf ? 120 : normalBottom);
                scrollRect.offsetMax = new Vector2(scrollRect.offsetMax.x,-68);
            }
            compact = small; return changed;
        }
        void Reveal(InputField field)
        {
            var scroll = field.GetComponentInParent<ScrollRect>();
            if (scroll == null || !scroll.transform.IsChildOf(transform) || scroll.content == null || scroll.viewport == null) return;
            var viewport = scroll.viewport;
            ((RectTransform)field.transform).GetWorldCorners(corners);
            float low = viewport.InverseTransformPoint(corners[0]).y;
            float high = viewport.InverseTransformPoint(corners[2]).y;
            float min = viewport.rect.yMin + 4, max = viewport.rect.yMax - 4;
            float shift = low < min ? min - low : high > max ? max - high : 0;
            var position = scroll.content.anchoredPosition;
            position.y = Mathf.Clamp(position.y + shift, 0, Mathf.Max(0, scroll.content.rect.height - viewport.rect.height));
            scroll.StopMovement(); scroll.content.anchoredPosition = position;
        }
    }
}
