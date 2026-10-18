using System;
using System.Collections.Generic;
using MobileUDB.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        sealed class WallDirectionScope
        {
            public OpenContext context;
            public bool visual;
            public List<ElementRef> selection;
            public HashSet<SurfaceRef> surfaces;
            public HashSet<int> things;
        }
        bool CurrentWallScope(WallDirectionScope scope)
        {
            if(!CurrentOpenContext(scope.context)||VisualActive!=scope.visual||Draft.Count>0||Tool==MobileTool.Paste||ThingAimActive||MakingSector)return false;
            if(scope.visual)return scope.surfaces.SetEquals(visualView.SelectedSurfaces)&&scope.things.SetEquals(visualView.SelectedThings);
            if(Selection.Count!=scope.selection.Count)return false;int i=0;foreach(var item in Selection)if(!item.Equals(scope.selection[i++]))return false;return true;
        }
        void WallDirectionMenu()
        {
            if(Draft.Count>0||Tool==MobileTool.Paste||ThingAimActive||MakingSector){Notify("Завершите рисование, вставку или предпросмотр перед изменением стен.");return;}
            var scope=new WallDirectionScope{context=CaptureOpenContext(),visual=VisualActive,selection=new List<ElementRef>(Selection)};
            if(VisualActive)
            {
                scope.surfaces=new HashSet<SurfaceRef>(visualView.SelectedSurfaces);scope.things=new HashSet<int>(visualView.SelectedThings);scope.selection.Clear();var lines=new HashSet<int>();
                foreach(var surface in visualView.SelectedSurfaces)if(!surface.IsFlat&&lines.Add(surface.line))scope.selection.Add(new ElementRef(ElementKind.Line,surface.line));
            }
            int flip=WallDirection.Candidates(Map,scope.selection,false).Count,swap=WallDirection.Candidates(Map,scope.selection,true).Count;
            WallAlignmentPlan alignment=null;string alignmentError=null;
            try{alignment=WallAlignment.Plan(Map,scope.selection);}catch(InvalidOperationException error){alignmentError=error.Message;}
            ShowDialog("Направление и стороны",body=>
            {
                var feedback=Label(body,"",16);feedback.name="Направление стен: сообщение";feedback.alignment=TextAnchor.MiddleLeft;feedback.color=new Color32(255,170,140,255);feedback.gameObject.AddComponent<LayoutElement>().preferredHeight=64;feedback.gameObject.SetActive(false);
                Action<string> error=text=>{feedback.text=text;feedback.gameObject.SetActive(true);};
                LineBatchCaption(body,"НАПРАВЛЕНИЕ");var row=ShapeOptionRow(body);
                VisualThingCommand(row,"Развернуть · "+flip,EditorIcon.Symbol.WallFlip,()=>{if(!ApplyWallDirection(scope,false))error(message);}).interactable=flip>0;
                LineBatchCaption(body,"СОГЛАСОВАНИЕ С СЕКТОРОМ");row=ShapeOptionRow(body);
                VisualThingCommand(row,"Согласовать · "+(alignment==null?0:alignment.ChangeCount),EditorIcon.Symbol.WallFlip,()=>{if(!ApplyWallAlignment(scope,alignment))error(message);}).interactable=alignment!=null&&alignment.ChangeCount>0;
                LineBatchCaption(body,"СТОРОНЫ");row=ShapeOptionRow(body);
                VisualThingCommand(row,"Поменять стороны · "+swap,EditorIcon.Symbol.WallFlip,()=>{if(!ApplyWallDirection(scope,true))error(message);}).interactable=swap>0;
                AddLabel(body,alignmentError??(alignment.SelectedLinesOnly?"Меняются только выбранные стены. UDB учитывает направления соседних невыбранных стен и сначала обрабатывает сектор с большим числом выбранных сторон.":"Обрабатываются стены выбранных секторов в порядке выделения. Общая граница меняется один раз.")+" Направление выбирается по правилам UDB; при равенстве — в обратную сторону. Повторная команда может снова развернуть двусторонние стены.");
                AddLabel(body,"Разворот меняет направление срабатывания действия, сохраняя геометрию и отделку. Обычные односторонние стены пропускаются; стены только с обратной стороной исправляются.");
                AddLabel(body,"Обмен сторон сохраняет направление линии, но меняет сектора и отделку местами. Это исправление перепутанных сторон двусторонней стены. Число на кнопке — сколько стен изменится; 0 означает отсутствие изменений. Один Undo отменяет всю команду.");
            });
        }
        bool ApplyWallDirection(WallDirectionScope scope,bool sidesOnly)
        {
            if(!CurrentWallScope(scope)){Notify("Карта или выделение изменились. Откройте направление стен снова.");return false;}
            if(WallDirection.Candidates(Map,scope.selection,sidesOnly).Count==0){Notify("Нет подходящих стен.");return false;}
            int changed=0;
            if(!Edit(map=>changed=WallDirection.Flip(map,scope.selection,sidesOnly),"Стены изменены. Доступна отмена."))return false;
            CloseDialog();if(VisualActive)visualView.ForgetSelection();Notify((sidesOnly?"Поменяны стороны стен: ":"Развёрнуто стен: ")+changed+".");return true;
        }
        bool ApplyWallAlignment(WallDirectionScope scope,WallAlignmentPlan plan)
        {
            if(!CurrentWallScope(scope)){Notify("Карта или выделение изменились. Откройте направление стен снова.");return false;}
            if(plan==null||plan.ChangeCount==0){Notify("Нет стен для согласования.");return false;}
            if(!Edit(map=>{plan.Apply(map);if(!scope.visual&&plan.SelectedLinesOnly&&plan.TargetCount==1)Selection.Clear();},"Направление согласовано · стен: "+plan.ChangeCount+". Один Undo отменяет изменения."))return false;
            CloseDialog();return true;
        }
    }
}
