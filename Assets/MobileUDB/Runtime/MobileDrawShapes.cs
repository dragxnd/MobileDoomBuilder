using System;
using MobileUDB.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        DrawShapeKind drawingShape;
        DrawShapeOptions rectangleShape=new DrawShapeOptions(),ellipseShape=new DrawShapeOptions{subdivisions=8};
        DrawShapeContour shapeContour;
        MapPoint? shapePointerStart,shapePointerEnd;
        string shapeError;
        Button shapeOptionsButton;
        public bool DrawingShape {get{return Tool==MobileTool.DrawRoom&&drawingShape!=DrawShapeKind.None;}}
        public DrawShapeContour ShapePreview {get{return DrawingShape?shapeContour:null;}}
        public bool ShapeGuidelines {get{return DrawingShape&&CurrentShapeOptions.guidelines;}}
        public bool ShapePlacesThings {get{return DrawingShape&&CurrentShapeOptions.placeThings;}}
        DrawShapeOptions CurrentShapeOptions {get{return DrawingGrid?gridDrawingOptions:DrawingCurve?curveShape:drawingShape==DrawShapeKind.Ellipse?ellipseShape:rectangleShape;}}
        string ShapeName {get{return DrawingGrid?"Сетка секторов":DrawingCurve?"Кривая":drawingShape==DrawShapeKind.Ellipse?"Эллипс":"Прямоугольник";}}
        void LoadShapeSettings()
        {
            rectangleShape=LoadShapeSetting(DrawShapeKind.Rectangle,new DrawShapeOptions());
            ellipseShape=LoadShapeSetting(DrawShapeKind.Ellipse,new DrawShapeOptions{subdivisions=8});
            curveShape=LoadShapeSetting(DrawShapeKind.Curve,new DrawShapeOptions());
            LoadDrawGridSettings();
            LoadBridgeSettings();
        }
        DrawShapeOptions LoadShapeSetting(DrawShapeKind kind,DrawShapeOptions fallback)
        {
            try {var json=PlayerPrefs.GetString("MobileUDB.Shape."+kind,"");if(json.Length==0)return fallback;
                var options=JsonUtility.FromJson<DrawShapeOptions>(json);options.Validate(kind);return options;}
            catch(Exception){return fallback;}
        }
        void ShapeSettingsMenu(DrawShapeKind kind,bool alreadyDrawing=false)
        {
            if(kind==DrawShapeKind.Grid){DrawGridSettingsMenu(alreadyDrawing);return;}
            if(kind==DrawShapeKind.Curve){CurveSettingsMenu(alreadyDrawing);return;}
            if(!alreadyDrawing&&Draft.Count>0){Notify("Завершите или отмените текущее рисование.");return;}
            var context=Map;var next=(kind==DrawShapeKind.Rectangle?rectangleShape:ellipseShape).Copy();
            ShowDialog(kind==DrawShapeKind.Rectangle?"Прямоугольник":"Эллипс",body=>
            {
                LineBatchCaption(body,"ФОРМА");
                var subdivisions=Field(body,kind==DrawShapeKind.Rectangle?"Деления угла 0–16":"Вершины 3–512",next.subdivisions.ToString());
                var bevel=Field(body,kind==DrawShapeKind.Rectangle?"Скругление ±":"Выступы ±",next.bevel.ToString());
                InputField angle=kind==DrawShapeKind.Ellipse?Field(body,"Угол вершин ±360",next.angle.ToString()):null;
                LineBatchCaption(body,"ПОСТРОЕНИЕ");
                var row=ShapeOptionRow(body);
                ShapeToggle(row,"От центра",next.radial,value=>next.radial=value);
                ShapeToggle(row,"Повторять",next.continuous,value=>next.continuous=value);
                row=ShapeOptionRow(body);
                ShapeToggle(row,"Ориентиры",next.guidelines,value=>next.guidelines=value);
                InputField type=null;Button catalog=null;
                ShapeToggle(row,"Объекты",next.placeThings,value=>{next.placeThings=value;if(type!=null)type.transform.parent.gameObject.SetActive(value);if(catalog!=null)catalog.gameObject.SetActive(value);});
                type=Field(body,"Тип объектов",placementType.ToString());type.transform.parent.gameObject.SetActive(next.placeThings);
                catalog=AddButton(body,"Каталог объектов…",()=>ThingPicker(id=>type.text=id.ToString()));catalog.gameObject.SetActive(next.placeThings);
                var help=Label(body,kind==DrawShapeKind.Rectangle?"Скругление: + — округлить угол, − — сделать вогнутым. 0 делений даёт прямой скос. Два касания задают противоположные углы; от центра — центр и край.":"Выступы чередуют радиус вершин; при 3–5 вершинах не применяются. Угол меняет расположение вершин внутри заданных границ. Два касания — границы; от центра — центр и край.",16);
                help.alignment=TextAnchor.MiddleLeft;help.gameObject.AddComponent<LayoutElement>().preferredHeight=112;
                var instructions=Label(body,"Касание уточняет край. «Готово» создаёт фигуру. «Объекты» размещает объекты в вершинах вместо стен.",16);
                instructions.alignment=TextAnchor.MiddleLeft;instructions.gameObject.AddComponent<LayoutElement>().preferredHeight=72;
                DialogApply(body,()=>
                {
                    try
                    {
                        if(!ReferenceEquals(Map,context)||(alreadyDrawing&&(!DrawingShape||drawingShape!=kind)))throw new InvalidOperationException("Режим или карта изменились. Откройте настройки снова.");
                        next.subdivisions=Number(subdivisions,kind==DrawShapeKind.Rectangle?0:3,kind==DrawShapeKind.Rectangle?16:512);
                        next.bevel=Number(bevel,-32768,32767);if(angle!=null)next.angle=Number(angle,-360,360);next.Validate(kind);
                        int id=next.placeThings?Number(type,0,65535):placementType;
                        if(alreadyDrawing&&Draft.Count==2)DrawShapes.Build(Draft[0],Draft[1],kind,next);
                        PlayerPrefs.SetString("MobileUDB.Shape."+kind,JsonUtility.ToJson(next));PlayerPrefs.Save();
                        if(kind==DrawShapeKind.Rectangle)rectangleShape=next;else ellipseShape=next;placementType=id;
                        if(alreadyDrawing){CloseDialog();ClearShapePointer();Changed(false);Notify(ShapeInstructions());}
                        else StartShapeDrawing(kind);
                        return true;
                    }
                    catch(Exception error){Notify(error.Message);return false;}
                },alreadyDrawing?"Применить":"Рисовать",true);
            });
        }
        RectTransform ShapeOptionRow(RectTransform body)
        {
            var row=Rect("Параметры фигуры",body);row.gameObject.AddComponent<LayoutElement>().preferredHeight=52;
            var layout=row.gameObject.AddComponent<HorizontalLayoutGroup>();layout.spacing=8;layout.childControlWidth=layout.childForceExpandWidth=layout.childControlHeight=layout.childForceExpandHeight=true;return row;
        }
        void ShapeToggle(RectTransform row,string title,bool value,Action<bool> changed)
        {
            Button button=null;Action refresh=()=>{button.GetComponentInChildren<Text>().text=(value?"✓ ":"")+title;button.GetComponent<Image>().color=value?Accent:Panel;};
            button=Button(row,title,()=>{value=!value;changed(value);refresh();});button.name=title;refresh();
        }
        void StartShapeDrawing(DrawShapeKind kind)
        {
            if(VisualActive)ExitVisual();CloseDialog();drawingShape=kind;drawOpenChain=false;Draft.Clear();ClearShapePointer();Tool=MobileTool.DrawRoom;Changed(false);Notify(ShapeInstructions());
        }
        string ShapeInstructions()
        {return shapeError??ShapeName+" · "+(DrawingCurve?"Опорных точек: "+Draft.Count+". «Готово» — открытая, «Замкнуть» — замкнутая.":Draft.Count==0?CurrentShapeOptions.radial?"Укажите центр, затем край.":"Укажите два противоположных угла.":Draft.Count==1?"Укажите вторую точку; можно перетащить край.":"Касание уточняет край. «Фигура» — параметры, «Готово» — создать.");}
        void ClearShapePointer(){shapePointerStart=shapePointerEnd=null;RefreshShapePreview();if(View!=null)View.SetVerticesDirty();}
        void RefreshShapePreview()
        {
            shapeContour=null;gridDrawPreview=null;shapeError=null;if(!DrawingShape)return;
            if(DrawingCurve){RefreshCurvePreview();return;}
            MapPoint? first=Draft.Count>0?(MapPoint?)Draft[0]:shapePointerStart;
            MapPoint? second=shapePointerEnd??(Draft.Count>1?(MapPoint?)Draft[1]:null);
            if(!first.HasValue||!second.HasValue)return;
            try {if(DrawingGrid){gridDrawPreview=DrawGrid.Build(first.Value,second.Value,gridShape,gridFrame);shapeContour=gridDrawPreview.outline;}
                else shapeContour=DrawShapes.Build(first.Value,second.Value,drawingShape,CurrentShapeOptions);}
            catch(Exception error){shapeError=error.Message;}
        }
        void ShapePointerMove(Vector2 screen)
        {
            if(!moved)return;shapePointerStart=Draft.Count==0?(MapPoint?)DrawingPoint(dragStart):null;
            shapePointerEnd=DrawingPoint(View.World(screen));RefreshShapePreview();View.SetVerticesDirty();
        }
        void ShapePointerUp(Vector2 screen)
        {
            if(DrawingCurve){CurvePointerUp(screen);return;}
            if(!View.ScreenRect.Contains(screen)){ClearShapePointer();return;}
            if(moved&&Draft.Count==0)Draft.Add(DrawingPoint(dragStart));
            var point=DrawingPoint(View.World(screen));if(Draft.Count<2)Draft.Add(point);else Draft[1]=point;
            shapePointerStart=shapePointerEnd=null;moved=false;Changed(false);Notify(ShapeInstructions());
        }
        void FinishShapeDrawing()
        {
            if(DrawingGrid){FinishGridDrawing();return;}
            if(DrawingCurve){FinishCurveDrawing(false);return;}
            ClearShapePointer();if(Draft.Count!=2||shapeContour==null||shapeContour.points.Count<2){Notify(shapeError??"Укажите две разные точки фигуры.");return;}
            try
            {
                var options=CurrentShapeOptions;var contour=shapeContour;
                var plan=options.placeThings?DrawShapes.PlanThings(Map,contour,placementType):null;
                if(!Edit(m=>
                {
                    Selection.Clear();
                    if(plan!=null){Kind=ElementKind.Thing;foreach(int index in plan.Apply(m))Selection.Add(new ElementRef(Kind,index));}
                    else
                    {
                        var result=m.DrawLines(contour.points,contour.closed,resources,drawTextures);
                        if(result.lines.Count==0&&result.sectors.Count==0)throw new InvalidOperationException("Фигура не добавила геометрию.");
                        Kind=result.sectors.Count>0?ElementKind.Sector:ElementKind.Line;
                        foreach(int index in Kind==ElementKind.Sector?result.sectors:result.lines)Selection.Add(new ElementRef(Kind,index));
                    }
                },ShapeName+" создан. Доступно одно Undo."))return;
                Draft.Clear();if(!options.continuous){drawingShape=DrawShapeKind.None;Tool=MobileTool.Select;}ClearShapePointer();Changed(false);
            }
            catch(Exception error){Notify(error.Message);}
        }
    }
}
