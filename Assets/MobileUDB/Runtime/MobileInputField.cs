// GPL-3.0. Finish native IME cancellation before Back can reach the surrounding dialog.
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
namespace MobileUDB
{
    public sealed class MobileInputField : InputField
    {
        public MobileEditor owner;
        bool suppressActivation;
        public override void OnSelect(BaseEventData data){suppressActivation=false;base.OnSelect(data);}
        public override void OnPointerClick(PointerEventData data)
        {if(data.button==PointerEventData.InputButton.Left)suppressActivation=false;base.OnPointerClick(data);}
        public override void OnUpdateSelected(BaseEventData data)
        {
            if(owner!=null&&Keyboard.current!=null&&Keyboard.current.escapeKey.wasPressedThisFrame)
            {owner.HandleBack();data.Use();return;}
            if(!suppressActivation)base.OnUpdateSelected(data);
        }
        protected override void LateUpdate()
        {
            if(suppressActivation)return;
            var keyboard=touchScreenKeyboard;
            if(isFocused&&keyboard!=null&&keyboard.status!=TouchScreenKeyboard.Status.Visible)
            {FinishKeyboardStatus(keyboard.status);return;}
            base.LateUpdate();
        }
        internal void FinishKeyboardStatus(TouchScreenKeyboard.Status status)
        {
            if(status==TouchScreenKeyboard.Status.Visible||suppressActivation)return;
            if(status==TouchScreenKeyboard.Status.Done)onSubmit.Invoke(text);
            bool back=status==TouchScreenKeyboard.Status.Canceled||status==TouchScreenKeyboard.Status.LostFocus;
            Dismiss();if(back&&owner!=null)owner.TextKeyboardBackHandled();
        }
        public override void OnDeselect(BaseEventData data)
        {
            var keyboard=touchScreenKeyboard;
            bool canceled=!suppressActivation&&isFocused&&(wasCanceled||
                keyboard!=null&&(keyboard.status==TouchScreenKeyboard.Status.Canceled||keyboard.status==TouchScreenKeyboard.Status.LostFocus));
            // Selection may move before native keyboard polling. Stop any queued
            // activation, and retain the draft if uGUI marks the edit canceled.
            string pending=text;suppressActivation=true;base.OnDeselect(data);
            if(canceled){SetTextWithoutNotify(pending);if(owner!=null)owner.TextKeyboardBackHandled();}
        }
        internal void Dismiss()
        {
            // uGUI defers activation to LateUpdate. DeactivateInputField alone
            // cannot cancel an activation queued by a tap in the same frame.
            suppressActivation=true;string pending=text;DeactivateInputField();SetTextWithoutNotify(pending);
            if(EventSystem.current!=null&&EventSystem.current.currentSelectedGameObject==gameObject)EventSystem.current.SetSelectedGameObject(null);
        }
    }
}
