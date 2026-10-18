// GPL-3.0. App-level UI scale with an unobtrusive live selector.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
namespace MobileUDB
{
    public enum InterfaceScalePreset { Large, Medium, Small }
    public sealed partial class MobileEditor
    {
        public const string InterfaceScalePreferenceKey="MobileUDB.InterfaceScale";
        public InterfaceScalePreset InterfaceSize {get;private set;}
        public float InterfaceScaleFactor {get{return InterfaceSize==InterfaceScalePreset.Medium?.8f:InterfaceSize==InterfaceScalePreset.Small?.65f:1f;}}
        RectTransform interfaceScaleCard;
        readonly Dictionary<InterfaceScalePreset,Button> interfaceScaleChoices=new Dictionary<InterfaceScalePreset,Button>();
        void LoadInterfaceScale()
        {
            int saved=PlayerPrefs.GetInt(InterfaceScalePreferenceKey,0);
            InterfaceSize=saved>=0&&saved<=2?(InterfaceScalePreset)saved:InterfaceScalePreset.Large;
        }
        void SetInterfaceScale(InterfaceScalePreset preset)
        {
            if(!Enum.IsDefined(typeof(InterfaceScalePreset),preset))throw new ArgumentOutOfRangeException("preset");
            float previous=InterfaceScaleFactor;InterfaceSize=preset;
            PlayerPrefs.SetInt(InterfaceScalePreferenceKey,(int)preset);PlayerPrefs.Save();
            // Compensate the map's canvas units while keeping the world centre.
            // Respect the existing stored-view zoom limits at extreme zoom levels.
            if(View!=null&&!Mathf.Approximately(previous,InterfaceScaleFactor))
                View.zoom=Mathf.Clamp(View.zoom*previous/InterfaceScaleFactor,.025f,20);
            ApplyToolbarLayout();RefreshInterfaceScaleChoices();
        }
        void ScaleDialogBounds(RectTransform rect,float fill)
        {
            float half=fill*InterfaceScaleFactor*.5f;
            rect.anchorMin=Vector2.one*(.5f-half);rect.anchorMax=Vector2.one*(.5f+half);rect.offsetMin=rect.offsetMax=Vector2.zero;
        }
        void InterfaceScaleMenu()
        {
            CancelDrag();CloseDialog();
            modal=Rect("Interface scale overlay",safeRoot);Stretch(modal);
            modal.gameObject.AddComponent<Image>().color=new Color(0,0,0,.06f);RefreshVisualTexturePanelVisibility();
            interfaceScaleCard=Rect("Interface scale card",modal);interfaceScaleCard.gameObject.AddComponent<Image>().color=Background;
            var heading=Label(interfaceScaleCard,"Масштаб интерфейса",18);var hr=heading.rectTransform;hr.anchorMin=new Vector2(0,1);hr.anchorMax=Vector2.one;hr.pivot=new Vector2(.5f,1);hr.sizeDelta=new Vector2(-124,52);hr.anchoredPosition=new Vector2(-54,-4);heading.resizeTextForBestFit=true;heading.resizeTextMinSize=12;heading.resizeTextMaxSize=18;
            var close=Button(interfaceScaleCard,"ЗАКРЫТЬ",CloseDialog);close.name="Close interface scale";var cr=close.GetComponent<RectTransform>();cr.anchorMin=cr.anchorMax=cr.pivot=Vector2.one;cr.sizeDelta=new Vector2(106,52);cr.anchoredPosition=new Vector2(-8,-8);
            var row=Rect("Scale choices",interfaceScaleCard);row.anchorMin=Vector2.zero;row.anchorMax=new Vector2(1,0);row.pivot=new Vector2(.5f,0);row.sizeDelta=new Vector2(-16,72);row.anchoredPosition=new Vector2(0,10);
            var layout=row.gameObject.AddComponent<HorizontalLayoutGroup>();layout.spacing=6;layout.childControlHeight=layout.childControlWidth=true;layout.childForceExpandHeight=layout.childForceExpandWidth=true;
            string[] names={"Большой","Средний","Маленький"};string[] percent={"100%","80%","65%"};interfaceScaleChoices.Clear();
            for(int i=0;i<names.Length;i++)
            {
                var preset=(InterfaceScalePreset)i;var button=Button(row,names[i]+"\n"+percent[i],()=>SetInterfaceScale(preset));button.name="InterfaceScale."+preset;interfaceScaleChoices.Add(preset,button);
                var label=button.GetComponentInChildren<Text>();label.resizeTextMinSize=11;label.resizeTextMaxSize=16;label.rectTransform.offsetMin=new Vector2(2,0);label.rectTransform.offsetMax=new Vector2(-2,-18);
                var checkRect=Rect("Selected scale",button.transform);checkRect.anchorMin=checkRect.anchorMax=checkRect.pivot=Vector2.one;checkRect.sizeDelta=new Vector2(18,18);checkRect.anchoredPosition=new Vector2(-6,-4);
                var check=checkRect.gameObject.AddComponent<WallFlagStateGraphic>();check.raycastTarget=false;check.color=new Color32(255,218,127,255);
            }
            LayoutInterfaceScaleMenu();RefreshInterfaceScaleChoices();
        }
        void LayoutInterfaceScaleMenu()
        {
            if(interfaceScaleCard==null||!interfaceScaleCard.gameObject.activeInHierarchy)return;
            interfaceScaleCard.anchorMin=interfaceScaleCard.anchorMax=interfaceScaleCard.pivot=new Vector2(.5f,.5f);interfaceScaleCard.anchoredPosition=Vector2.zero;
            interfaceScaleCard.sizeDelta=new Vector2(Mathf.Min(336,Mathf.Max(1,safeRoot.rect.width-16)),Mathf.Min(152,Mathf.Max(1,safeRoot.rect.height-16)));
        }
        void RefreshInterfaceScaleChoices()
        {
            foreach(var pair in interfaceScaleChoices)if(pair.Value!=null)
            {bool selected=pair.Key==InterfaceSize;pair.Value.GetComponentInChildren<WallFlagStateGraphic>().State=selected?1:0;pair.Value.GetComponent<Image>().color=selected?Accent:Panel;}
        }
    }
}

