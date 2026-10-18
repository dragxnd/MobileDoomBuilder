// GPL-3.0. Shared display-only controls for the 2D and 3D action toolbars.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        readonly List<Button> displayLightingButtons=new List<Button>();
        readonly List<WallFlagStateGraphic> displayLightingChecks=new List<WallFlagStateGraphic>();
        public bool ThingsVisible {get;private set;}=true;
        readonly List<Button> displayThingButtons=new List<Button>();
        readonly List<WallFlagStateGraphic> displayThingChecks=new List<WallFlagStateGraphic>();
        void BuildDisplayThingsToggle(Transform parent)
        {
            var button=IconButton(parent,"Показывать объекты · включить или выключить",EditorIcon.Symbol.Thing,ToggleDisplayThings);
            var icon=button.GetComponentInChildren<EditorIcon>().rectTransform;icon.sizeDelta=new Vector2(26,32);icon.anchoredPosition=new Vector2(-6,0);
            var square=Rect("Показ объектов · флажок",button.transform);square.anchorMin=square.anchorMax=square.pivot=Vector2.one;square.sizeDelta=new Vector2(20,20);square.anchoredPosition=new Vector2(-2,-2);
            var check=square.gameObject.AddComponent<WallFlagStateGraphic>();check.color=new Color32(255,218,127,255);check.raycastTarget=false;
            displayThingButtons.Add(button);displayThingChecks.Add(check);RefreshDisplayThingsToggles();
        }
        void RefreshDisplayThingsToggles()
        {
            for(int i=0;i<displayThingButtons.Count;i++)
            {displayThingButtons[i].GetComponent<Image>().color=ThingsVisible?Accent:Panel;displayThingChecks[i].State=ThingsVisible?1:0;}
        }
        bool HiddenThingInteraction {get{return !ThingsVisible&&Kind==MobileUDB.Core.ElementKind.Thing&&(Tool==MobileTool.Select||Tool==MobileTool.Move||Tool==MobileTool.Transform);}}
        void ToggleDisplayThings()
        {
            CancelDrag();ThingsVisible=!ThingsVisible;View.SetVerticesDirty();
            if(visualView!=null)visualView.RefreshThingsVisibility();
            RefreshDisplayThingsToggles();Notify(ThingsVisible?"Объекты показаны.":"Объекты скрыты.");
        }
        void BuildDisplayLightingToggle(Transform parent)
        {
            var button=IconButton(parent,"Затемнение карты по яркости секторов · включить или выключить",EditorIcon.Symbol.DisplayLighting,ToggleDisplayBrightness);
            var icon=button.GetComponentInChildren<EditorIcon>().rectTransform;icon.sizeDelta=new Vector2(26,32);icon.anchoredPosition=new Vector2(-6,0);
            var square=Rect("Затемнение карты · флажок",button.transform);square.anchorMin=square.anchorMax=square.pivot=Vector2.one;square.sizeDelta=new Vector2(20,20);square.anchoredPosition=new Vector2(-2,-2);
            var check=square.gameObject.AddComponent<WallFlagStateGraphic>();check.color=new Color32(255,218,127,255);check.raycastTarget=false;
            displayLightingButtons.Add(button);displayLightingChecks.Add(check);RefreshDisplayLightingToggles();
        }
        void RefreshDisplayLightingToggles()
        {
            RefreshDisplayThingsToggles();if(View==null)return;bool dimmed=!View.fullBright;
            for(int i=0;i<displayLightingButtons.Count;i++)
            {displayLightingButtons[i].GetComponent<Image>().color=dimmed?Accent:Panel;displayLightingChecks[i].State=dimmed?1:0;}
        }
        void ToggleDisplayBrightness()
        {
            View.fullBright=!View.fullBright;View.RefreshFill();
            if(visualView!=null)visualView.RequestRebuild();
            RefreshDisplayLightingToggles();Notify(View.fullBright?"Затемнение карты выключено.":"Затемнение карты включено.");
        }
    }
}
