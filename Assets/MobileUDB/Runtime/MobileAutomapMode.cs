// GPL-3.0. Touch UI for UDB AutomapMode's two binary linedef flags.
using System.Collections.Generic;
using MobileUDB.Core;
using UnityEngine;
using UnityEngine.UI;
namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        public bool AutomapActive {get;private set;}
        internal bool AutomapShowAll {get;private set;}
        AutomapLineAction automapAction;
        bool automapPointerActive,automapMoved;
        Vector2 automapPointerStart;
        OpenContext automapPointerContext;
        MobileTool automapPreviousTool;
        ElementKind automapPreviousKind;
        readonly List<ElementRef> automapPreviousSelection=new List<ElementRef>();
        SelectionBrush automapPreviousBrush;
        bool automapPreviousMass;
        RectTransform automapRoot;
        Button automapAllButton,automapCloseButton,automapVisibilityButton,automapTypeButton,automapVisibleSingleButton,automapVisibleTwoButton;
        WallFlagStateGraphic automapAllCheck,automapVisibilityCheck,automapTypeCheck,automapVisibleSingleCheck,automapVisibleTwoCheck;
        Text automapHint;
        readonly Dictionary<GameObject,bool> automapHiddenUI=new Dictionary<GameObject,bool>();
        void BuildAutomapUI()
        {
            automapRoot=Rect("Automap controls",safeRoot);Stretch(automapRoot);
            automapAllButton=Button(automapRoot,"Все линии",()=>{CancelAutomapPointer();AutomapShowAll=!AutomapShowAll;View.SetVerticesDirty();RefreshAutomapUI();});
            var all=automapAllButton.GetComponent<RectTransform>();all.anchorMin=all.anchorMax=all.pivot=new Vector2(0,1);all.sizeDelta=new Vector2(160,52);all.anchoredPosition=new Vector2(8,-8);
            automapAllCheck=AutomapCheck(automapAllButton);
            automapCloseButton=IconButton(automapRoot,"Закрыть режим автокарты",EditorIcon.Symbol.Delete,ExitAutomap);
            var close=automapCloseButton.GetComponent<RectTransform>();close.anchorMin=close.anchorMax=close.pivot=Vector2.one;close.sizeDelta=new Vector2(52,52);close.anchoredPosition=new Vector2(-8,-8);
            automapVisibilityButton=Button(automapRoot,"Видимость линии",()=>SetAutomapAction(AutomapLineAction.ToggleVisibility));
            automapTypeButton=Button(automapRoot,"Как односторонняя",()=>SetAutomapAction(AutomapLineAction.ToggleSingleSided));
            automapVisibilityCheck=AutomapCheck(automapVisibilityButton);automapTypeCheck=AutomapCheck(automapTypeButton);
            automapVisibleSingleButton=Button(automapRoot,"Видимая односторонняя",()=>SetAutomapAction(AutomapLineAction.VisibleSingleSided));
            automapVisibleTwoButton=Button(automapRoot,"Видимая двухсторонняя",()=>SetAutomapAction(AutomapLineAction.VisibleTwoSided));
            automapVisibleSingleCheck=AutomapCheck(automapVisibleSingleButton);automapVisibleTwoCheck=AutomapCheck(automapVisibleTwoButton);
            automapVisibleSingleButton.GetComponentInChildren<Text>().text="Видимая\nодносторонняя";
            automapVisibleTwoButton.GetComponentInChildren<Text>().text="Видимая\nдвухсторонняя";
            var buttons=new[]{automapVisibilityButton,automapTypeButton,automapVisibleSingleButton,automapVisibleTwoButton};
            for(int i=0;i<buttons.Length;i++)
            {var rect=buttons[i].GetComponent<RectTransform>();int column=i%2;rect.anchorMin=new Vector2(column*.5f,0);rect.anchorMax=new Vector2((column+1)*.5f,0);rect.pivot=new Vector2(.5f,0);rect.sizeDelta=new Vector2(-12,56);rect.anchoredPosition=new Vector2(0,i<2?72:8);var label=buttons[i].GetComponentInChildren<Text>();label.resizeTextMinSize=12;label.resizeTextMaxSize=16;}
            automapHint=Label(automapRoot,"",14);var note=automapHint.rectTransform;note.anchorMin=Vector2.zero;note.anchorMax=new Vector2(1,0);note.pivot=new Vector2(.5f,0);note.sizeDelta=new Vector2(-16,34);note.anchoredPosition=new Vector2(0,134);
            automapRoot.gameObject.SetActive(false);
        }
        WallFlagStateGraphic AutomapCheck(Button button)
        {
            var rect=Rect("Mode checkbox",button.transform);rect.anchorMin=rect.anchorMax=new Vector2(0,.5f);rect.pivot=new Vector2(0,.5f);rect.sizeDelta=new Vector2(20,20);rect.anchoredPosition=new Vector2(10,0);
            var check=rect.gameObject.AddComponent<WallFlagStateGraphic>();check.color=new Color32(255,218,127,255);check.raycastTarget=false;
            button.GetComponentInChildren<Text>().rectTransform.offsetMin=new Vector2(38,0);return check;
        }
        void EnterAutomap()
        {
            if(AutomapActive||SoundPropagationActive||BlockmapActive||VisualActive)return;
            if(Draft.Count>0||Tool==MobileTool.Paste||ThingAimActive||MakingSector||CurvingLines||BridgingLines){Notify("Завершите или отмените текущую операцию перед автокартой.");return;}
            CancelDrag();CloseDialog();CloseTexturePicker();EnsureClassicView();
            automapPreviousTool=Tool;automapPreviousKind=Kind;automapPreviousSelection.Clear();automapPreviousSelection.AddRange(Selection);
            automapPreviousBrush=selectionBrush;automapPreviousMass=massSelection;
            selectionBrush=SelectionBrush.Off;massSelection=false;Tool=MobileTool.Pan;
            AutomapActive=true;automapHiddenUI.Clear();
            foreach(Transform child in safeRoot)if(child!=viewportRoot&&child!=automapRoot)HideAutomapUI(child.gameObject);
            foreach(Transform child in viewportRoot)if(child!=View.transform)HideAutomapUI(child.gameObject);
            automapRoot.gameObject.SetActive(true);automapRoot.SetAsLastSibling();ApplyToolbarLayout();Changed(false);
        }
        void HideAutomapUI(GameObject target){automapHiddenUI[target]=target.activeSelf;target.SetActive(false);}
        void ExitAutomap()
        {
            if(!AutomapActive)return;CancelDrag();AutomapActive=false;
            automapRoot.gameObject.SetActive(false);foreach(var pair in automapHiddenUI)if(pair.Key!=null)pair.Key.SetActive(pair.Value);automapHiddenUI.Clear();
            Tool=automapPreviousTool;Kind=automapPreviousKind;Selection.Clear();
            foreach(var item in automapPreviousSelection)if(item.index>=0&&item.index<MapSelection.Count(Map,item.kind))Selection.Add(item);automapPreviousSelection.Clear();
            selectionBrush=automapPreviousBrush;massSelection=automapPreviousMass;
            ApplyToolbarLayout();Changed(false);Notify("Режим автокарты закрыт. Изменения флагов доступны в Undo.");
        }
        void SetAutomapAction(AutomapLineAction action){CancelAutomapPointer();automapAction=action;RefreshAutomapUI();}
        void LayoutAutomap(){viewportRoot.offsetMin=viewportRoot.offsetMax=Vector2.zero;RefreshAutomapUI();View.SetVerticesDirty();}
        void RefreshAutomapUI()
        {
            automapAllCheck.State=AutomapShowAll?1:0;
            RefreshAutomapAction(automapVisibilityButton,automapVisibilityCheck,AutomapLineAction.ToggleVisibility);
            RefreshAutomapAction(automapTypeButton,automapTypeCheck,AutomapLineAction.ToggleSingleSided);
            RefreshAutomapAction(automapVisibleSingleButton,automapVisibleSingleCheck,AutomapLineAction.VisibleSingleSided);
            RefreshAutomapAction(automapVisibleTwoButton,automapVisibleTwoCheck,AutomapLineAction.VisibleTwoSided);
            automapAllButton.GetComponent<Image>().color=AutomapShowAll?Accent:Panel;
            switch(automapAction)
            {
                case AutomapLineAction.ToggleSingleSided:automapHint.text="Касание: обычная / односторонняя линия";break;
                case AutomapLineAction.VisibleSingleSided:automapHint.text="Касание: видимая односторонняя / скрытая";break;
                case AutomapLineAction.VisibleTwoSided:automapHint.text="Касание: видимая двухсторонняя / скрытая";break;
                default:automapHint.text="Касание: показать / скрыть линию";break;
            }
        }
        void RefreshAutomapAction(Button button,WallFlagStateGraphic check,AutomapLineAction action)
        {bool selected=automapAction==action;check.State=selected?1:0;button.GetComponent<Image>().color=selected?Accent:Panel;}
        void CancelAutomapPointer(){automapPointerActive=false;automapMoved=false;}
        void BeginAutomapPointer(Vector2 point)
        {CancelDrag();if(!View.ScreenRect.Contains(point))return;automapPointerActive=true;automapPointerStart=point;automapPointerContext=CaptureOpenContext();}
        void MoveAutomapPointer(Vector2 previous,Vector2 point)
        {if(!automapPointerActive)return;if(Vector2.Distance(automapPointerStart,point)>8)automapMoved=true;if(automapMoved)View.Pan(previous,point);}
        void EndAutomapPointer(Vector2 point)
        {
            bool tap=automapPointerActive&&!automapMoved&&Vector2.Distance(automapPointerStart,point)<=8;var context=automapPointerContext;CancelAutomapPointer();
            if(!tap||!CurrentOpenContext(context)||!View.ScreenRect.Contains(point))return;
            int line=AutomapLines.Hit(Map,View.World(point),26/View.zoom,AutomapShowAll);if(line<0)return;
            var action=automapAction;
            Edit(map=>map.lines[line].flags=AutomapLines.ApplyAction(map.lines[line].flags,action),action==AutomapLineAction.ToggleVisibility?"Изменена видимость линии на автокарте.":action==AutomapLineAction.ToggleSingleSided?"Изменён тип линии на автокарте.":"Изменены видимость и тип линии на автокарте.");
            View.SetVerticesDirty();
        }
    }
}
