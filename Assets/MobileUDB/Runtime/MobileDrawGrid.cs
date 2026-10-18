using System;
using System.Collections.Generic;
using MobileUDB.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        DrawGridOptions gridShape=new DrawGridOptions();
        readonly DrawShapeOptions gridDrawingOptions=new DrawShapeOptions();
        DrawGridGeometry gridDrawPreview;
        public bool DrawingGrid {get{return DrawingShape&&drawingShape==DrawShapeKind.Grid;}}
        public DrawGridGeometry GridDrawPreview {get{return DrawingGrid?gridDrawPreview:null;}}
        void SyncGridDrawingOptions(){gridDrawingOptions.continuous=gridShape.continuous;gridDrawingOptions.guidelines=gridShape.guidelines;}
        void LoadDrawGridSettings()
        {
            try{var json=PlayerPrefs.GetString("MobileUDB.Shape.Grid","");if(json.Length>0){var next=JsonUtility.FromJson<DrawGridOptions>(json);next.Validate();gridShape=next;}}
            catch(Exception){gridShape=new DrawGridOptions();}SyncGridDrawingOptions();
        }
        static bool SameDrawGridFrame(GridFrame a,GridFrame b)
        {return a.step==b.step&&a.rotation==b.rotation&&a.originX==b.originX&&a.originY==b.originY&&a.snap==b.snap&&a.increment==b.increment;}
        void DrawGridSettingsMenu(bool alreadyDrawing=false)
        {
            if(!alreadyDrawing&&Draft.Count>0){Notify("Завершите или отмените текущее рисование.");return;}
            var context=CaptureOpenContext();var tool=Tool;var anchors=new List<MapPoint>(Draft);var grid=gridFrame.Copy();var next=gridShape.Copy();
            ShowDialog("Сетка секторов",body=>
            {
                LineBatchCaption(body,"ЯЧЕЙКИ");
                var columns=Field(body,"Ячеек по X · 1–128",next.columns.ToString());
                var rows=Field(body,"Ячеек по Y · 1–128",next.rows.ToString());
                var locks=ShapeOptionRow(body);Button lockButton=null;
                var lockNames=new[]{"Вручную","X по шагу","Y по шагу","X/Y по шагу"};
                Action refresh=()=>{lockButton.GetComponentInChildren<Text>().text="Количество: "+lockNames[(int)next.gridLock];columns.interactable=!next.LockX;rows.interactable=!next.LockY;};
                lockButton=Button(locks,"Количество",()=>{next.gridLock=(DrawGridLock)(((int)next.gridLock+1)%4);refresh();});lockButton.name="Количество ячеек";
                LineBatchCaption(body,"РАСПРЕДЕЛЕНИЕ ЛИНИЙ");
                var names=new[]{"Равномерно","Плавно с двух краёв","Гуще у начала","Гуще у конца"};
                Button horizontal=null,vertical=null;Action refreshModes=()=>
                {horizontal.GetComponentInChildren<Text>().text="X · "+names[next.LockX?0:(int)next.horizontal];horizontal.interactable=!next.LockX;
                 vertical.GetComponentInChildren<Text>().text="Y · "+names[next.LockY?0:(int)next.vertical];vertical.interactable=!next.LockY;};
                horizontal=AddButton(body,"X",()=>{next.horizontal=(GradientInterpolation)(((int)next.horizontal+1)%4);refreshModes();});horizontal.name="Распределение X";
                vertical=AddButton(body,"Y",()=>{next.vertical=(GradientInterpolation)(((int)next.vertical+1)%4);refreshModes();});vertical.name="Распределение Y";
                lockButton.onClick.AddListener(()=>refreshModes());refresh();refreshModes();
                LineBatchCaption(body,"ПОСТРОЕНИЕ");
                var row=ShapeOptionRow(body);ShapeToggle(row,"Диагонали",next.triangulate,value=>next.triangulate=value);
                ShapeToggle(row,"От первой точки",next.relative,value=>next.relative=value);
                row=ShapeOptionRow(body);ShapeToggle(row,"Повторять",next.continuous,value=>next.continuous=value);
                ShapeToggle(row,"Ориентиры",next.guidelines,value=>next.guidelines=value);
                AddLabel(body,"Два касания или перетаскивание задают углы. «Готово» строит сетку с одной отменой. «Фигура» меняет параметры предпросмотра.");
                AddLabel(body,"По шагу: число ячеек берётся из текущей сетки карты, распределение равномерное. За один раз — до 256 ячеек; для маленьких ячеек увеличьте размер фигуры.");
                DialogApply(body,()=>
                {
                    try
                    {
                        if(!CurrentOpenContext(context)||Tool!=tool||VisualActive||!SameDrawGridFrame(grid,gridFrame)||Draft.Count!=anchors.Count||(alreadyDrawing&&!DrawingGrid))throw new InvalidOperationException("Карта, сетка или режим изменились. Откройте параметры снова.");
                        for(int i=0;i<anchors.Count;i++)if(anchors[i].x!=Draft[i].x||anchors[i].y!=Draft[i].y)throw new InvalidOperationException("Опорные точки изменились. Откройте параметры снова.");
                        if(!next.LockX)next.columns=Number(columns,1,128);if(!next.LockY)next.rows=Number(rows,1,128);next.Validate();
                        if(alreadyDrawing&&Draft.Count==2)DrawGrid.Build(Draft[0],Draft[1],next,gridFrame);
                        PlayerPrefs.SetString("MobileUDB.Shape.Grid",JsonUtility.ToJson(next));PlayerPrefs.Save();gridShape=next;SyncGridDrawingOptions();
                        if(alreadyDrawing){CloseDialog();ClearShapePointer();Changed(false);Notify(ShapeInstructions());}else StartShapeDrawing(DrawShapeKind.Grid);
                        return true;
                    }
                    catch(Exception error){Notify(error.Message);return false;}
                },alreadyDrawing?"Применить":"Рисовать",true);
            });
        }
        void FinishGridDrawing()
        {
            ClearShapePointer();if(Draft.Count!=2||gridDrawPreview==null){Notify(shapeError??"Укажите два угла сетки.");return;}
            try
            {
                var plan=DrawGrid.Plan(Map,gridDrawPreview,resources,drawTextures);
                if(!Edit(m=>{var selected=plan.Apply(m);Selection.Clear();foreach(var item in selected)Selection.Add(item);foreach(var item in selected){Kind=item.kind;break;}},"Сетка создана. Доступно одно Undo."))return;
                Draft.Clear();if(!gridShape.continuous){drawingShape=DrawShapeKind.None;Tool=MobileTool.Select;}ClearShapePointer();Changed(false);
            }
            catch(Exception error){Notify(error.Message);}
        }
    }
}
