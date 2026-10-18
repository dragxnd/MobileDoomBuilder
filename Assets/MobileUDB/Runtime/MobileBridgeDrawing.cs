using System;
using System.Collections.Generic;
using System.Globalization;
using MobileUDB.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        BridgeOptions bridgeOptions=new BridgeOptions();bool bridgeInvertSnap;
        BridgeDrawing bridgeDrawing,bridgeDragOrigin;BridgeGeometry bridgeGeometry;int bridgeDragIndex=-1;
        OpenContext bridgeContext;List<ElementRef> bridgeSelection;
        RectTransform bridgeUI;Text bridgeInfo;Button bridgeApply;
        readonly Text[] bridgeHandleLabels=new Text[4];
        public bool BridgingLines {get{return Tool==MobileTool.Bridge&&!VisualActive;}}
        public BridgeGeometry BridgePreview {get{return BridgingLines&&CurrentBridge()?bridgeGeometry:null;}}
        void LoadBridgeSettings()
        {
            try{var json=PlayerPrefs.GetString("MobileUDB.Bridge","");if(json.Length>0){var next=JsonUtility.FromJson<BridgeOptions>(json);next.Validate();bridgeOptions=next;}}
            catch(Exception){bridgeOptions=new BridgeOptions();}
        }
        bool CanBridgeLines()
        {if(VisualActive||Draft.Count>0||Tool==MobileTool.Paste||ThingAimActive||MakingSector||CurvingLines||BridgingLines||Selection.Count<2)return false;foreach(var item in Selection)if(item.kind!=ElementKind.Line)return false;return true;}
        void BridgeButton(RectTransform body)
        {LineBatchCaption(body,"СОЕДИНЕНИЕ ЦЕПОЧЕК");var row=ShapeOptionRow(body);VisualThingCommand(row,"Мост между стенами…",EditorIcon.Symbol.Bridge,BridgeMenu).interactable=CanBridgeLines()||BridgingLines;}
        bool BridgeSelectionMatches(List<ElementRef> selection)
        {if(selection==null||Selection.Count!=selection.Count)return false;int i=0;foreach(var item in Selection)if(!item.Equals(selection[i++]))return false;return true;}
        bool CurrentBridge()
        {return bridgeDrawing!=null&&bridgeContext!=null&&CurrentOpenContext(bridgeContext)&&BridgeSelectionMatches(bridgeSelection);}
        void ClearBridge()
        {bridgeDrawing=bridgeDragOrigin=null;bridgeGeometry=null;bridgeContext=null;bridgeSelection=null;bridgeDragIndex=-1;}
        void CancelBridge(){CancelDrag();SetTool(MobileTool.Select);Notify("Построение моста отменено.");}
        static string BridgeCoordinate(double value){return value.ToString("0.###",CultureInfo.InvariantCulture);}
        void BridgeMenu()
        {
            bool preview=BridgingLines;if(!preview&&!CanBridgeLines()){Notify("Выберите две отдельные цепочки стен с одинаковым числом звеньев.");return;}
            if(preview&&!CurrentBridge()){Notify("Карта или выделение изменились. Закройте предпросмотр моста.");return;}
            CancelDrag();var context=CaptureOpenContext();var selection=new List<ElementRef>(Selection);var initialTool=Tool;var grid=gridFrame.Copy();var existing=bridgeDrawing;
            BridgeDrawing model;
            try{if(preview)model=bridgeDrawing.Copy();else {var indices=new List<int>();foreach(var item in selection)indices.Add(item.index);model=BridgeDrawing.Setup(Map,indices);}}
            catch(Exception error){Notify(error.Message);return;}
            var next=bridgeOptions.Copy();bool inverse=bridgeInvertSnap;int selectedHandle=0;
            ShowDialog("Мост между стенами",body=>
            {
                LineBatchCaption(body,"ФОРМА");var subdivisions=CurveNumber(body,"Деления · 0–32",next.subdivisions,0,32,1);
                InputField x=null,y=null;string renderedX=null,renderedY=null;
                Action saveHandle=()=>{if(x==null)return;var old=model.Handle(selectedHandle);double nx=x.text==renderedX?old.x:GridNumber(x,-32768,32767),ny=y.text==renderedY?old.y:GridNumber(y,-32768,32767);if(nx!=old.x||ny!=old.y)model.MoveHandle(selectedHandle,new CurvePoint(nx,ny),next.handles);};
                Action refreshHandle=()=>{if(x==null)return;var p=model.Handle(selectedHandle);renderedX=BridgeCoordinate(p.x);renderedY=BridgeCoordinate(p.y);x.text=renderedX;y.text=renderedY;};
                AddButton(body,"Развернуть первую цепочку",()=>{try{saveHandle();model.Flip();refreshHandle();Notify("Первая цепочка развёрнута. Примените параметры для предпросмотра.");}catch(Exception error){Notify(error.Message);}});
                LineBatchCaption(body,"ВЫСОТЫ И СВЕТ");
                var floorNames=new[]{"Линейно","Нижний из двух","Плавно от начала","Плавно к концу","Плавно с двух краёв"};
                var ceilingNames=new[]{"Линейно","Верхний из двух","Плавно от начала","Плавно к концу","Плавно с двух краёв"};
                var lightNames=new[]{"Линейно","Более светлый","Более тёмный"};
                BridgeChoice(body,"Пол",floorNames,(int)next.floor,value=>next.floor=(BridgeHeightMode)value);
                BridgeChoice(body,"Потолок",ceilingNames,(int)next.ceiling,value=>next.ceiling=(BridgeHeightMode)value);
                BridgeChoice(body,"Свет",lightNames,(int)next.light,value=>next.light=(BridgeLightMode)value);
                LineBatchCaption(body,"РУЧКИ КРИВОЙ");
                BridgeChoice(body,"Связь",new[]{"Независимо","Зеркально","Вместе"},(int)next.handles,value=>next.handles=(BridgeHandleMode)value);
                var row=ShapeOptionRow(body);ShapeToggle(row,"Привязка",SnapEnabled^inverse,value=>inverse=SnapEnabled^value);
                var handles=ShapeOptionRow(body);var handleButtons=new List<Button>();Action refreshButtons=()=>{for(int i=0;i<handleButtons.Count;i++)handleButtons[i].GetComponent<Image>().color=i==selectedHandle?Accent:Panel;};
                for(int i=0;i<4;i++){int index=i;var button=Button(handles,(i+1).ToString(),()=>{try{saveHandle();selectedHandle=index;refreshHandle();refreshButtons();}catch(Exception error){Notify(error.Message);}});button.name="Ручка моста "+(i+1);handleButtons.Add(button);}refreshButtons();
                x=Field(body,"Ручка X","",false);y=Field(body,"Ручка Y","",false);
                x.characterLimit=y.characterLimit=32;x.keyboardType=y.keyboardType=TouchScreenKeyboardType.NumbersAndPunctuation;refreshHandle();
                AddLabel(body,"Две цепочки должны иметь одинаковое число звеньев. На карте перетаскивайте ручки 1–4; касание вне ручек перемещает вид. Их координаты можно задать здесь.");
                AddLabel(body,"Зеркально / вместе связывает ручки на одном конце моста. 0 делений — только просмотр; для создания нужен хотя бы один пролёт. «Создать» применяет весь мост с одной отменой.");
                var error=Label(body,"",16);error.color=new Color(1,.72f,.38f);error.alignment=TextAnchor.MiddleLeft;error.gameObject.AddComponent<LayoutElement>().preferredHeight=64;error.gameObject.SetActive(false);
                DialogApply(body,()=>
                {
                    try
                    {
                        if(!CurrentOpenContext(context)||VisualActive||Tool!=initialTool||Draft.Count>0||!BridgeSelectionMatches(selection)||!SameDrawGridFrame(grid,gridFrame)||(preview&&!ReferenceEquals(existing,bridgeDrawing)))throw new InvalidOperationException("Карта, сетка, режим или выделение изменились.");
                        saveHandle();next.subdivisions=Number(subdivisions,0,32);next.Validate();model.CheckCurrent(Map);var geometry=model.Build(next);
                        bridgeOptions=next.Copy();bridgeInvertSnap=inverse;PlayerPrefs.SetString("MobileUDB.Bridge",JsonUtility.ToJson(bridgeOptions));PlayerPrefs.Save();
                        SetTool(MobileTool.Bridge);bridgeDrawing=model;bridgeGeometry=geometry;bridgeContext=context;bridgeSelection=selection;Changed(false);
                        Notify("Мост: перемещайте ручки, проверьте форму и нажмите «Создать».");return true;
                    }
                    catch(Exception e){error.text=e.Message;error.gameObject.SetActive(true);Notify(e.Message);return false;}
                },"Предпросмотр",true);
            });
        }
        void BridgeChoice(RectTransform body,string caption,string[] choices,int value,Action<int> changed)
        {Button button=null;Action refresh=()=>button.GetComponentInChildren<Text>().text=caption+" · "+choices[value];button=AddButton(body,caption,()=>{value=(value+1)%choices.Length;changed(value);refresh();});button.name="Мост · "+caption;refresh();}
        bool BeginBridgePointer(Vector2 screen)
        {
            if(!BridgingLines)return false;if(!CurrentBridge())return true;var p=View.World(screen);double nearest=24/View.zoom;
            for(int i=0;i<4;i++){var h=bridgeDrawing.Handle(i);double dx=p.x-h.x,dy=p.y-h.y,d=Math.Sqrt(dx*dx+dy*dy);if(d<=nearest){nearest=d;bridgeDragIndex=i;}}
            if(bridgeDragIndex>=0)bridgeDragOrigin=bridgeDrawing.Copy();return true;
        }
        bool MoveBridgePointer(Vector2 previous,Vector2 screen)
        {
            if(!BridgingLines)return false;if(!CurrentBridge())return true;if(bridgeDragIndex<0){if(moved)View.Pan(previous,screen);return true;}
            if(!View.ScreenRect.Contains(screen))return true;var old=bridgeDrawing.Copy();
            try{var p=View.World(screen);var q=new CurvePoint(p.x,p.y);if(SnapEnabled^bridgeInvertSnap)q=gridFrame.SnapRaw(q);bridgeDrawing.MoveHandle(bridgeDragIndex,q,bridgeOptions.handles);bridgeGeometry=bridgeDrawing.Build(bridgeOptions);View.SetVerticesDirty();RefreshBridgeUI();}
            catch(Exception error){bridgeDrawing=old;bridgeGeometry=old.Build(bridgeOptions);Notify(error.Message);}return true;
        }
        bool EndBridgePointer(Vector2 screen)
        {if(!BridgingLines)return false;if(bridgeDragIndex>=0&&View.ScreenRect.Contains(screen)){MoveBridgePointer(screen,screen);bridgeDragOrigin=null;bridgeDragIndex=-1;Notify("Ручка изменена. «Создать» применит мост.");}else CancelBridgeGesture();return true;}
        void CancelBridgeGesture()
        {if(bridgeDragOrigin!=null){bridgeDrawing=bridgeDragOrigin;bridgeGeometry=bridgeDrawing.Build(bridgeOptions);if(View!=null)View.SetVerticesDirty();}bridgeDragOrigin=null;bridgeDragIndex=-1;}
        void ApplyBridge()
        {
            CancelDrag();if(!BridgingLines||!CurrentBridge()){Notify("Карта или выделение изменились. Обновите мост.");return;}
            try{var plan=bridgeDrawing.Plan(Map,bridgeGeometry,resources,MakeSectorSky);if(Edit(map=>{plan.Apply(map);Selection.Clear();},"Мост создан. Доступно одно Undo."))SetTool(MobileTool.Select);}
            catch(Exception error){Notify(error.Message);}
        }
        void BuildBridgeUI(RectTransform viewport)
        {
            bridgeUI=Rect("Предпросмотр моста",viewport);bridgeUI.anchorMin=new Vector2(0,1);bridgeUI.anchorMax=Vector2.one;bridgeUI.pivot=new Vector2(.5f,1);bridgeUI.sizeDelta=new Vector2(-24,116);bridgeUI.anchoredPosition=new Vector2(0,-10);bridgeUI.gameObject.AddComponent<Image>().color=Background;
            bridgeInfo=Label(bridgeUI,"",16);var r=bridgeInfo.rectTransform;r.anchorMin=new Vector2(0,.5f);r.anchorMax=Vector2.one;r.offsetMin=new Vector2(8,0);r.offsetMax=new Vector2(-8,0);
            var settings=Button(bridgeUI,"Параметры",BridgeMenu);CurvePinned(settings,0);settings.name="Параметры моста";
            bridgeApply=Button(bridgeUI,"Создать",ApplyBridge);CurvePinned(bridgeApply,1);bridgeApply.name="Создать мост";
            var cancel=Button(bridgeUI,"Отмена",CancelBridge);CurvePinned(cancel,2);cancel.name="Отменить мост";bridgeUI.gameObject.SetActive(false);
            for(int i=0;i<4;i++){var text=Label(viewport,(i+1).ToString(),16);text.name="Номер ручки моста "+(i+1);text.raycastTarget=false;text.color=new Color(.6f,1,.7f);var rt=text.rectTransform;rt.anchorMin=rt.anchorMax=new Vector2(.5f,.5f);rt.sizeDelta=new Vector2(24,24);text.gameObject.SetActive(false);bridgeHandleLabels[i]=text;}
        }
        void RefreshBridgeHandleLabels()
        {
            bool visible=BridgingLines&&CurrentBridge()&&bridgeGeometry!=null;
            for(int i=0;i<4;i++)if(bridgeHandleLabels[i]!=null)
            {var text=bridgeHandleLabels[i];bool show=visible;if(show){var p=bridgeGeometry.handles[i];var screen=View.Project(new MapPoint((float)p.x,(float)p.y));show=View.rectTransform.rect.Contains(screen)&&screen.y<View.rectTransform.rect.yMax-128;text.rectTransform.anchoredPosition=screen+new Vector2(16,14);}text.gameObject.SetActive(show);}
        }
        void RefreshBridgeUI()
        {
            if(!BridgingLines&&bridgeDrawing!=null)ClearBridge();if(bridgeUI==null)return;bridgeUI.gameObject.SetActive(BridgingLines);if(!BridgingLines)return;
            bool valid=CurrentBridge();bridgeApply.interactable=valid&&bridgeGeometry!=null&&bridgeGeometry.cells.Count>0;
            bridgeInfo.text=valid?"Мост · "+bridgeDrawing.Rows+" рядов · "+bridgeOptions.subdivisions+" делений\nРучки 1–4 · вне ручек — перемещение вида":"Карта или выделение изменились\nЗакройте предпросмотр";
        }
    }
}
