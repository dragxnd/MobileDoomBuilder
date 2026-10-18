using System;
using System.Collections.Generic;
using MobileUDB.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        CurveLinedefOptions linedefCurve=new CurveLinedefOptions();
        CurveLinedefPlan linedefCurvePlan;
        OpenContext linedefCurveContext;
        List<ElementRef> linedefCurveSelection;
        RectTransform linedefCurveUI;Text linedefCurveInfo;Button linedefCurveApply;
        public bool CurvingLines {get{return Tool==MobileTool.CurveLines&&!VisualActive;}}
        public IReadOnlyList<CurvedLinedef> LinedefCurvePreview {get{return CurvingLines&&CurrentLinedefCurve()?linedefCurvePlan?.curves:null;}}
        bool CanCurveLinedefs()
        {
            if(VisualActive||Draft.Count>0||Tool==MobileTool.Paste||ThingAimActive||MakingSector||BridgingLines||Selection.Count==0)return false;
            foreach(var item in Selection)if(item.kind!=ElementKind.Line)return false;return true;
        }
        void CurveLinedefButton(RectTransform body)
        {
            LineBatchCaption(body,"СТЕНЫ");var row=ShapeOptionRow(body);
            VisualThingCommand(row,"Построить дугу из стен…",EditorIcon.Symbol.CurveLines,CurveLinedefMenu).interactable=CanCurveLinedefs();
        }
        bool CurrentLinedefCurve()
        {
            if(linedefCurvePlan==null||linedefCurveContext==null||!CurrentOpenContext(linedefCurveContext)||VisualActive||!CurvingLines||Selection.Count!=linedefCurveSelection.Count)return false;
            int i=0;foreach(var item in Selection)if(!item.Equals(linedefCurveSelection[i++]))return false;return true;
        }
        void ClearLinedefCurve(){linedefCurvePlan=null;linedefCurveContext=null;linedefCurveSelection=null;}
        void CancelLinedefCurve(){SetTool(MobileTool.Select);Notify("Построение дуги отменено.");}
        void CurveLinedefMenu()
        {
            bool preview=CurvingLines;if(!preview&&!CanCurveLinedefs()){Notify("Выберите стены на 2D-карте и завершите текущее действие.");return;}
            if(preview&&!CurrentLinedefCurve()){Notify("Карта или выделение изменились. Закройте предпросмотр и откройте дугу снова.");return;}
            if(!preview){try{linedefCurve=JsonUtility.FromJson<CurveLinedefOptions>(PlayerPrefs.GetString("MobileUDB.CurveLines",JsonUtility.ToJson(new CurveLinedefOptions())))??new CurveLinedefOptions();linedefCurve.Validate();}catch{linedefCurve=new CurveLinedefOptions();}}
            var context=CaptureOpenContext();var selection=new List<ElementRef>(Selection);var next=linedefCurve.Copy();var initialTool=Tool;var merge=geometryMergeMode;
            ShowDialog("Дуга из стен",body=>
            {
                body.GetComponent<VerticalLayoutGroup>().spacing=5;
                CurveCaption(body,"ФОРМА");var row=ShapeOptionRow(body);Button deformed=null,circle=null;
                var distance=default(InputField);Action refresh=null;
                deformed=Button(row,"Высота",()=>{next.fixedCurve=false;refresh();});circle=Button(row,"Окружность",()=>{next.fixedCurve=true;refresh();});
                var verts=CurveNumber(body,"ВЕРШИНЫ · 1–200",next.vertices,1,200,1);
                distance=CurveNumber(body,"ВЫСОТА · −10000…10000",next.distance,-10000,10000,8);
                var angle=CurveNumber(body,"УГОЛ · 0–350°",next.angle,0,350,5);
                var heightRow=distance.transform.parent.parent;var heightCaption=heightRow.parent.GetChild(heightRow.GetSiblingIndex()-1);
                refresh=()=>{deformed.GetComponent<Image>().color=!next.fixedCurve?Accent:Panel;circle.GetComponent<Image>().color=next.fixedCurve?Accent:Panel;heightRow.gameObject.SetActive(!next.fixedCurve);heightCaption.gameObject.SetActive(!next.fixedCurve);};refresh();
                row=ShapeOptionRow(body);VisualThingCommand(row,"Перевернуть",EditorIcon.Symbol.WallFlip,()=>{if(next.fixedCurve)next.outwards=!next.outwards;else{int n;if(int.TryParse(distance.text,out n)&&n>= -10000&&n<=10000)distance.text=(-n).ToString();}Notify("Направление дуги изменено. Нажмите «Предпросмотр»." );});
                Button(row,"Сброс",()=>{var offsets=next.offsets;next=new CurveLinedefOptions{offsets=offsets};verts.text="8";distance.text="128";angle.text="180";refresh();});
                CurveCaption(body,"СМЕЩЕНИЯ ТЕКСТУР ПРИ РАЗБИЕНИИ");row=ShapeOptionRow(body);
                Button offset=null;offset=Button(row,CurveOffsetLabel(next.offsets),()=>{next.offsets=(CurveSplitOffsets)(((int)next.offsets+1)%4);offset.GetComponentInChildren<Text>().text=CurveOffsetLabel(next.offsets);});
                var error=Label(body,"",16);error.name="Дуга: сообщение";error.color=new Color(1,.72f,.38f);error.alignment=TextAnchor.MiddleLeft;error.gameObject.AddComponent<LayoutElement>().preferredHeight=64;error.gameObject.SetActive(false);
                DialogApply(body,()=>
                {
                    try
                    {
                        if(!CurrentOpenContext(context)||VisualActive||Draft.Count>0||Tool!=initialTool||geometryMergeMode!=merge||Selection.Count!=selection.Count)throw new InvalidOperationException("Карта, режим или выделение изменились.");
                        int i=0;foreach(var item in Selection)if(!item.Equals(selection[i++]))throw new InvalidOperationException("Порядок выделения изменился.");
                        next.vertices=Number(verts,1,200);next.distance=Number(distance,-10000,10000);next.angle=Number(angle,0,350);next.Validate();
                        var plan=CurveLinedefs.Prepare(Map,selection,next,merge,resources,MakeSectorSky);
                        linedefCurve=next.Copy();PlayerPrefs.SetString("MobileUDB.CurveLines",JsonUtility.ToJson(linedefCurve));PlayerPrefs.Save();
                        SetTool(MobileTool.CurveLines);linedefCurvePlan=plan;linedefCurveContext=context;linedefCurveSelection=selection;Changed(false);
                        Notify("Проверьте дугу. «Создать» изменит выбранные стены одним Undo.");return true;
                    }
                    catch(Exception e){error.text=e.Message;error.gameObject.SetActive(true);Notify(e.Message);return false;}
                },"Предпросмотр",true);
            });
        }
        InputField CurveNumber(RectTransform body,string caption,int value,int minimum,int maximum,int increment)
        {
            CurveCaption(body,caption);var row=ShapeOptionRow(body);row.GetComponent<LayoutElement>().preferredHeight=48;
            var less=Button(row,"−"+increment,()=>{});var field=Field(row,"",value.ToString());field.name="Дуга число · "+caption;
            var fieldRow=field.transform.parent;fieldRow.GetChild(0).gameObject.SetActive(false);
            var fr=field.GetComponent<RectTransform>();fr.anchorMin=Vector2.zero;fr.anchorMax=Vector2.one;fr.offsetMin=Vector2.zero;fr.offsetMax=Vector2.zero;
            var layout=fieldRow.GetComponent<LayoutElement>();layout.preferredWidth=120;layout.preferredHeight=48;layout.flexibleWidth=1;
            var more=Button(row,"+"+increment,()=>{});
            less.onClick.AddListener(()=>{int n;if(int.TryParse(field.text,out n)&&n>=minimum&&n<=maximum)field.text=Math.Max(minimum,n-increment).ToString();});
            more.onClick.AddListener(()=>{int n;if(int.TryParse(field.text,out n)&&n>=minimum&&n<=maximum)field.text=Math.Min(maximum,n+increment).ToString();});
            Action refresh=()=>{int n;bool valid=int.TryParse(field.text,out n)&&n>=minimum&&n<=maximum;less.interactable=valid&&n>minimum;more.interactable=valid&&n<maximum;};field.onValueChanged.AddListener(_=>refresh());refresh();return field;
        }
        void CurveCaption(RectTransform body,string caption)
        {var text=Label(body,caption,14);text.alignment=TextAnchor.MiddleLeft;text.color=new Color(.7f,.76f,.82f);text.gameObject.AddComponent<LayoutElement>().preferredHeight=20;}
        static string CurveOffsetLabel(CurveSplitOffsets offsets)
        {return offsets==CurveSplitOffsets.Interpolate?"Продолжать текстуру":offsets==CurveSplitOffsets.CopyXY?"Копировать X / Y":offsets==CurveSplitOffsets.ResetXCopyY?"X = 0, копировать Y":"X / Y = 0";}
        void ApplyLinedefCurve()
        {
            if(!CurrentLinedefCurve()){Notify("Карта или выделение изменились. Закройте предпросмотр.");return;}
            var plan=linedefCurvePlan;
            if(Edit(map=>{var selected=plan.Apply(map);Selection.Clear();Selection.UnionWith(selected);Kind=ElementKind.Line;},"Дуги построены, геометрия сшита. Доступно одно Undo."))SetTool(MobileTool.Select);
        }
        void BuildLinedefCurveUI(RectTransform viewport)
        {
            linedefCurveUI=Rect("Предпросмотр дуги из стен",viewport);linedefCurveUI.anchorMin=new Vector2(0,1);linedefCurveUI.anchorMax=Vector2.one;linedefCurveUI.pivot=new Vector2(.5f,1);linedefCurveUI.sizeDelta=new Vector2(-24,116);linedefCurveUI.anchoredPosition=new Vector2(0,-10);linedefCurveUI.gameObject.AddComponent<Image>().color=Background;
            linedefCurveInfo=Label(linedefCurveUI,"",16);var info=linedefCurveInfo.rectTransform;info.anchorMin=new Vector2(0,.5f);info.anchorMax=Vector2.one;info.offsetMin=new Vector2(8,0);info.offsetMax=new Vector2(-8,0);
            var settings=Button(linedefCurveUI,"Параметры",CurveLinedefMenu);CurvePinned(settings,0);linedefCurveApply=Button(linedefCurveUI,"Создать",ApplyLinedefCurve);linedefCurveApply.name="Применить дугу из стен";CurvePinned(linedefCurveApply,1);
            var cancel=Button(linedefCurveUI,"Отмена",CancelLinedefCurve);cancel.name="Отменить дугу из стен";CurvePinned(cancel,2);linedefCurveUI.gameObject.SetActive(false);
        }
        static void CurvePinned(Button button,int column)
        {var r=button.GetComponent<RectTransform>();r.anchorMin=new Vector2(column/3f,0);r.anchorMax=new Vector2((column+1)/3f,0);r.pivot=new Vector2(.5f,0);r.sizeDelta=new Vector2(-8,52);r.anchoredPosition=new Vector2(0,4);}
        void RefreshLinedefCurveUI()
        {
            if(!CurvingLines&&linedefCurvePlan!=null)ClearLinedefCurve();if(linedefCurveUI==null)return;linedefCurveUI.gameObject.SetActive(CurvingLines);if(!CurvingLines)return;
            bool valid=CurrentLinedefCurve();linedefCurveApply.interactable=valid;linedefCurveInfo.text=valid?"Дуги · "+linedefCurvePlan.curves.Count+" стен · "+linedefCurvePlan.AddedVertices+" вершин\nПроверьте форму · перетаскивание перемещает вид":"Карта или выделение изменились\nЗакройте предпросмотр";
        }
    }
}
