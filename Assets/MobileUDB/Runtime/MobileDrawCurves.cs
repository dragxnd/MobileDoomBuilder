using System;
using System.Collections.Generic;
using MobileUDB.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        DrawShapeOptions curveShape=new DrawShapeOptions();
        public bool DrawingCurve {get{return DrawingShape&&drawingShape==DrawShapeKind.Curve;}}
        void CurveSettingsMenu(bool alreadyDrawing=false)
        {
            if(!alreadyDrawing&&Draft.Count>0){Notify("Завершите или отмените текущее рисование.");return;}
            var context=Map;var next=curveShape.Copy();
            ShowDialog("Кривая",body=>
            {
                LineBatchCaption(body,"ПОДРОБНОСТЬ");
                var length=Field(body,"Сегмент 16–4096",next.segmentLength.ToString());
                var row=ShapeOptionRow(body);
                Button finer=null,coarser=null;
                finer=Button(row,"Детальнее",()=>{length.text=Math.Max(16,Number(length,16,4096)-Math.Max(16,Number(length,16,4096)/32*16)).ToString();});
                coarser=Button(row,"Грубее",()=>{length.text=Math.Min(4096,Number(length,16,4096)+Math.Max(16,Number(length,16,4096)/32*16)).ToString();});
                Action refresh=()=>{int value;bool valid=int.TryParse(length.text,out value)&&value>=16&&value<=4096;finer.interactable=valid&&value>16;coarser.interactable=valid&&value<4096;};length.onValueChanged.AddListener(_=>refresh());refresh();
                LineBatchCaption(body,"ПОСТРОЕНИЕ");row=ShapeOptionRow(body);
                ShapeToggle(row,"Повторять",next.continuous,value=>next.continuous=value);
                ShapeToggle(row,"Автозамыкание",next.autoClose,value=>next.autoClose=value);
                InputField type=null;Button catalog=null;row=ShapeOptionRow(body);
                ShapeToggle(row,"Объекты",next.placeThings,value=>{next.placeThings=value;if(type!=null)type.transform.parent.gameObject.SetActive(value);if(catalog!=null)catalog.gameObject.SetActive(value);});
                type=Field(body,"Тип объектов",placementType.ToString());type.transform.parent.gameObject.SetActive(next.placeThings);
                catalog=AddButton(body,"Каталог объектов…",()=>ThingPicker(id=>type.text=id.ToString()));catalog.gameObject.SetActive(next.placeThings);
                var help=Label(body,"Касания ставят опорные точки. Кривая проходит через них. Меньший сегмент даёт больше стен; две точки дают прямую. «Готово» завершает открытую кривую, «Замкнуть» соединяет её плавно.",16);help.alignment=TextAnchor.MiddleLeft;help.gameObject.AddComponent<LayoutElement>().preferredHeight=112;
                var auto=Label(body,"Автозамыкание завершает кривую, когда оба конца касаются существующих стен и получается новый сектор. В режиме объектов используйте «Готово» или «Замкнуть».",16);auto.alignment=TextAnchor.MiddleLeft;auto.gameObject.AddComponent<LayoutElement>().preferredHeight=96;
                if(alreadyDrawing&&Draft.Count>0)AddButton(body,"Убрать первую опорную точку",()=>{CloseDialog();RemoveFirstDraftPoint();});
                DialogApply(body,()=>
                {
                    try
                    {
                        if(!ReferenceEquals(Map,context)||(alreadyDrawing&&!DrawingCurve))throw new InvalidOperationException("Режим или карта изменились. Откройте настройки снова.");
                        next.segmentLength=Number(length,16,4096);next.Validate(DrawShapeKind.Curve);int id=next.placeThings?Number(type,0,65535):placementType;
                        if(alreadyDrawing&&Draft.Count>=2)DrawCurves.Build(Draft,next);
                        PlayerPrefs.SetString("MobileUDB.Shape.Curve",JsonUtility.ToJson(next));PlayerPrefs.Save();curveShape=next;placementType=id;
                        if(alreadyDrawing){CloseDialog();ClearShapePointer();Changed(false);Notify(ShapeInstructions());}else StartShapeDrawing(DrawShapeKind.Curve);
                        return true;
                    }
                    catch(Exception error){Notify(error.Message);return false;}
                },alreadyDrawing?"Применить":"Рисовать",true);
            });
        }
        void RefreshCurvePreview()
        {
            var anchors=new List<MapPoint>(Draft);if(anchors.Count==0&&shapePointerStart.HasValue)anchors.Add(shapePointerStart.Value);
            if(shapePointerEnd.HasValue&&(anchors.Count==0||(anchors[anchors.Count-1]-shapePointerEnd.Value).LengthSquared>0))anchors.Add(shapePointerEnd.Value);
            if(anchors.Count<2)return;
            try {shapeContour=DrawCurves.Build(anchors,curveShape);}
            catch(Exception error){shapeError=error.Message;}
        }
        void CurvePointerUp(Vector2 screen)
        {
            if(!View.ScreenRect.Contains(screen)){ClearShapePointer();return;}
            if(moved&&Draft.Count==0)Draft.Add(DrawingPoint(dragStart));
            var point=DrawingPoint(View.World(screen));shapePointerStart=shapePointerEnd=null;moved=false;
            if(Draft.Count>=3&&(point-Draft[0]).LengthSquared<Math.Pow(20/View.zoom,2)){FinishCurveDrawing(true);return;}
            if(Draft.Count==0||(point-Draft[Draft.Count-1]).LengthSquared>.000001f)Draft.Add(point);
            Changed(false);Notify(ShapeInstructions());
            if(curveShape.autoClose&&!curveShape.placeThings&&shapeContour!=null&&shapeError==null)
            {
                try {var plan=DrawCurves.PrepareAutoClose(Map,shapeContour,resources,drawTextures);if(plan!=null)FinishCurveDrawing(false,plan);}
                catch(Exception error){Notify(error.Message);}
            }
        }
        void FinishCurveDrawing(bool close,CurveDrawingPlan prepared=null)
        {
            try
            {
                ClearShapePointer();var contour=DrawCurves.Build(Draft,curveShape,close);
                var objects=curveShape.placeThings?DrawShapes.PlanThings(Map,contour,placementType):null;
                var plan=objects==null?prepared??DrawCurves.Prepare(Map,contour,resources,drawTextures):null;
                if(!Edit(m=>
                {
                    Selection.Clear();
                    if(objects!=null){Kind=ElementKind.Thing;foreach(int index in objects.Apply(m))Selection.Add(new ElementRef(Kind,index));}
                    else
                    {
                        var result=plan.Apply(m);if(result.lines.Count==0&&result.sectors.Count==0)throw new InvalidOperationException("Кривая не добавила геометрию.");
                        Kind=result.sectors.Count>0?ElementKind.Sector:ElementKind.Line;
                        foreach(int index in Kind==ElementKind.Sector?result.sectors:result.lines)Selection.Add(new ElementRef(Kind,index));
                    }
                },"Кривая создана. Доступно одно Undo."))return;
                Draft.Clear();if(!curveShape.continuous){drawingShape=DrawShapeKind.None;Tool=MobileTool.Select;}ClearShapePointer();Changed(false);
            }
            catch(Exception error){Notify(error.Message);}
        }
    }
}
