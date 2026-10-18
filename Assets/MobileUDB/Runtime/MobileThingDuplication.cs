using System;
using System.Collections.Generic;
using MobileUDB.Core;
using UnityEngine;

namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        ThingDuplicatePlan duplicateThings;
        OpenContext duplicateContext;
        MapPoint duplicateAnchor,duplicateOffset;
        bool duplicatePlacementValid;
        bool DuplicatingThings {get{return Tool==MobileTool.Paste&&duplicateThings!=null;}}
        bool ThingDuplicateReady {get{return CurrentThingDuplicate()&&duplicatePlacementValid;}}
        public MapPoint? DuplicatePlacementAnchor {get{return ThingDuplicateReady?(MapPoint?)(duplicateAnchor+duplicateOffset):null;}}
        void ThingDuplicateButton(RectTransform body)
        {
            var row=ShapeOptionRow(body);
            VisualThingCommand(row,"Дублировать объекты…",EditorIcon.Symbol.Copy,StartThingDuplicate).interactable=OrientationTargets().Count>0;
        }
        void StartThingDuplicate()
        {
            var targets=OrientationTargets();if(targets.Count==0){Notify("Выберите объекты и завершите рисование или вставку.");return;}
            if(VisualActive){Notify("Для размещения копии откройте 2D-карту.");return;}
            try
            {
                CancelDrag();var plan=ThingDuplication.Prepare(Map,targets,thingCatalog,Actions);
                var anchor=plan.Anchor(new MapPoint(View.center.x,View.center.y));var preview=plan.Preview(new MapPoint());var context=CaptureOpenContext();
                CloseDialog();pasteSelection=new List<ElementRef>(Selection);pasteKind=Kind;
                duplicateThings=plan;duplicateContext=context;duplicateAnchor=anchor;duplicateOffset=new MapPoint();duplicatePlacementValid=true;
                PastePreview=preview;Selection.Clear();Tool=MobileTool.Paste;Changed(false);
                Notify("Копия: коснитесь места для крестика → «Готово». Связей пути: "+plan.PathCount+".");
            }
            catch(Exception error){Notify(error.Message);}
        }
        bool CurrentThingDuplicate()
        {return duplicateThings!=null&&CurrentOpenContext(duplicateContext)&&!VisualActive&&Tool==MobileTool.Paste;}
        void PositionThingDuplicate(Vector2 screen)
        {
            if(!CurrentThingDuplicate()){Notify("Карта изменилась. Отмените копию и повторите команду.");return;}
            try
            {
                var offset=gridFrame.MoveOffset(duplicateAnchor,View.World(screen)-duplicateAnchor);
                var preview=duplicateThings.Preview(offset);duplicateOffset=offset;PastePreview=preview;duplicatePlacementValid=true;Changed(false);
            }
            catch(Exception error){duplicatePlacementValid=false;Notify(error.Message);}
        }
        void CommitThingDuplicate()
        {
            if(!ThingDuplicateReady){Notify("Размещение копии недоступно. Укажите допустимое место или повторите команду.");return;}
            var plan=duplicateThings;var offset=duplicateOffset;
            if(!Edit(map=>{var selected=plan.Apply(map,offset);Selection.Clear();foreach(int index in selected)Selection.Add(new ElementRef(ElementKind.Thing,index));Kind=ElementKind.Thing;},"Дублированы объекты · "+plan.Count+". Одно Undo отменяет копии и связи пути."))return;
            CancelPaste(false);Changed(false);
        }
        void ClearThingDuplicate()
        {duplicateThings=null;duplicateContext=null;}
    }
}
