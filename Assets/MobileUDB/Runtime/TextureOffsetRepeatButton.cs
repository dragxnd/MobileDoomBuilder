using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace MobileUDB
{
    public sealed class TextureOffsetRepeatButton : Button
    {
        public Action begin;
        public Func<bool> valid;
        public Func<bool,bool> move;
        public float delay=.35f,interval=.1f;
        bool held,repeated,recordNeeded;
        float next;
        public override void OnPointerDown(PointerEventData data)
        {
            base.OnPointerDown(data);if(data.button!=PointerEventData.InputButton.Left||!IsInteractable())return;
            begin();held=true;repeated=false;recordNeeded=true;next=Time.unscaledTime+delay;
        }
        void Update()
        {
            if(!held)return;
            if(!IsInteractable()||!valid()){held=false;return;}
            if(Time.unscaledTime<next)return;
            repeated=true;next=Time.unscaledTime+Mathf.Max(.05f,interval);
            if(move(recordNeeded))recordNeeded=false;else held=false;
        }
        public override void OnPointerClick(PointerEventData data)
        {if(repeated){repeated=false;return;}if(valid!=null&&!valid())return;base.OnPointerClick(data);}
        public override void OnPointerUp(PointerEventData data){held=false;base.OnPointerUp(data);}
        public override void OnPointerExit(PointerEventData data){held=false;base.OnPointerExit(data);}
        protected override void OnDisable(){held=false;repeated=true;base.OnDisable();}
        void OnApplicationFocus(bool focus){if(!focus){held=false;repeated=true;}}
        void OnApplicationPause(bool pause){if(pause){held=false;repeated=true;}}
        protected override void DoStateTransition(SelectionState state,bool instant)
        {base.DoStateTransition(state,instant);var icon=GetComponentInChildren<EditorIcon>();if(icon!=null)icon.color=new Color32(255,255,255,state==SelectionState.Disabled?(byte)80:(byte)255);}
    }
}
