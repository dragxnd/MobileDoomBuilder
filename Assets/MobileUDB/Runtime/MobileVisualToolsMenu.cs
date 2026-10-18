using MobileUDB.Core;using UnityEngine;using UnityEngine.UI;
namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        void BuildVisualToolsMenu(RectTransform body)
        {
            body.GetComponent<VerticalLayoutGroup>().spacing=5;
            var context=CaptureOpenContext();var marks=visualView.SnapshotSelection();
            bool walls=false,flats=false;foreach(var id in visualView.SelectedSurfaces){if(id.IsFlat)flats=true;else walls=true;}
            if(visualView.PickedThing>=0)VisualToolsThings(body);
            else if(visualView.Picked.HasValue)VisualToolsSurface(body,walls,context,marks);
            if(walls||DrawSampleLine()>=0)
            {
                ToolsCaption(body,"СТЕНЫ И РИСОВАНИЕ");var row=ShapeOptionRow(body);
                if(walls)ToolsCommand(row,"Смещения и привязка текстур стен…","Текстуры стен…",EditorIcon.Symbol.Texture,VisualWallToolsMenu);
                if(DrawSampleLine()>=0)
                {
                    ToolsCommand(row,"Взять текстуры стены для рисования…","Взять текстуры…",EditorIcon.Symbol.Copy,SampleDrawWallMenu);
                    row=ShapeOptionRow(body);ToolsCommand(row,"Направление и стороны стены…","Направление / стороны…",EditorIcon.Symbol.WallFlip,WallDirectionMenu);
                }
            }
            if(visualView.SelectionCount>0)
            {
                ToolsCaption(body,"СВОЙСТВА И ВЫСОТЫ");var row=ShapeOptionRow(body);int count=0;
                if(walls){ToolsCommand(row,"Свойства отмеченных стен…","Свойства стен…",EditorIcon.Symbol.Wall,()=>VisualFilteredProperties(ElementKind.Line));count++;}
                if(flats){ToolsCommand(row,"Свойства отмеченных секторов…","Свойства секторов…",EditorIcon.Symbol.Sector,()=>VisualFilteredProperties(ElementKind.Sector));count++;}
                if(count==2){row=ShapeOptionRow(body);count=0;}
                if(visualView.SelectedThings.Count>0){ToolsCommand(row,"Свойства отмеченных объектов…","Свойства объектов…",EditorIcon.Symbol.Thing,()=>VisualFilteredProperties(ElementKind.Thing));count++;}
                if(count==2)row=ShapeOptionRow(body);
                ToolsCommand(row,"Высоты выделения…","Высоты выделения…",EditorIcon.Symbol.Floor,VisualHeightSelectionMenu);
            }
            ToolsCaption(body,"ОСВЕЩЕНИЕ ВИДА");var lighting=ShapeOptionRow(body);ToolsCommand(lighting,"Полная яркость: "+(View.fullBright?"Да":"Нет"),"Без затемнения: "+(View.fullBright?"Да":"Нет"),EditorIcon.Symbol.ViewBrightness,()=>{ToggleDisplayBrightness();VisualMoreMenu();});
            ToolsCaption(body,"ВЫДЕЛЕНИЕ И БУФЕРЫ");var selection=ShapeOptionRow(body);
            ToolsCommand(selection,"Кисть выделения…","Кисть выделения…",EditorIcon.Symbol.Select,VisualPaintSelectionMenu);
            ToolsCommand(selection,"Снять выделение","Снять выделение",EditorIcon.Symbol.Select,()=>{CancelDrag();visualView.ForgetSelection();SelectVisualSurface(null);CloseDialog();},visualView.SelectionCount>0);
            selection=ShapeOptionRow(body);ToolsCommand(selection,"Объекты: создать / копировать / вставить…","Объекты / буфер…",EditorIcon.Symbol.AddThing,VisualThingClipboardMenu);
            ElementRef source;SurfaceRef? wall;bool copy=PropertySource(out source,out wall),paste=PropertySelection().fields!=PropertyFields.None;
            ToolsCommand(selection,"Копировать свойства","Копировать свойства",EditorIcon.Symbol.Copy,()=>{CloseDialog();CopyProperties();},copy);
            selection=ShapeOptionRow(body);ToolsCommand(selection,"Вставить свойства","Вставить свойства",EditorIcon.Symbol.Paste,()=>{CloseDialog();PasteProperties(PropertyFields.All,null);},paste);
            ToolsCommand(selection,"Выбрать свойства для вставки…","Поля вставки…",EditorIcon.Symbol.Properties,PropertyPasteOptions,paste);
            ToolsCaption(body,"КАМЕРА И ШАГ");var view=ShapeOptionRow(body);
            ToolsCommand(view,"Шаг высоты: "+visualStep+" → следующий","Шаг: "+visualStep+" →",EditorIcon.Symbol.Grid,()=>{visualStep=visualStep>=32?1:visualStep*2;CloseDialog();Changed(false);});
            ToolsCommand(view,"Вернуться к старту игрока","К старту игрока",EditorIcon.Symbol.Visual,()=>{CloseDialog();visualView.ResetPlayerCamera();ObserveVisualCamera();});
            view=ShapeOptionRow(body);
            ToolsCommand(view,"Приблизить камеру к точке касания / прицелу","К точке",EditorIcon.Symbol.Zoom,()=>
            {
                if(!CurrentOpenContext(context)){Notify("Карта изменилась. Откройте 3D-инструменты снова.");return;}
                CloseDialog();bool moved=visualView.ApproachCameraTarget();ObserveVisualCamera();
                Notify(moved?"Камера приблизилась к точке; осталось ¼ расстояния.":visualView.PlacementReady?"В этой точке нет поверхности или объекта.":"Обновление вида… Повторите команду после загрузки.");
            });
            view=ShapeOptionRow(body);
            ToolsCommand(view,"Записать текущую камеру в служебный объект WAD","Записать в WAD",EditorIcon.Symbol.SaveMap,()=>
            {
                if(!CurrentOpenContext(context)){Notify("Карта изменилась. Откройте 3D-инструменты снова.");return;}
                var camera=visualView.CaptureCamera();
                try
                {
                    if(!MapCameraMarker.NeedsSave(Map,camera)){CloseDialog();Notify("Эта позиция камеры уже записана в WAD.");return;}
                    if(Edit(m=>MapCameraMarker.Save(m,camera),"Камера записана в объект 32000. Undo отменяет запись."))CloseDialog();
                }
                catch(System.Exception e){Notify(e.Message);}
            });
            ToolsCommand(view,"Вернуться к служебному объекту камеры WAD","К камере WAD",EditorIcon.Symbol.Visual,()=>
            {
                if(!CurrentOpenContext(context)){Notify("Карта изменилась. Откройте 3D-инструменты снова.");return;}
                CloseDialog();if(visualView.RestoreMarkerCamera()){ObserveVisualCamera();Notify("Камера восстановлена из WAD.");}
            },MapCameraMarker.Find(Map)>=0);
            var markerHint=Label(body,"Камера WAD — объект UDB (32000). Существующая камера обновляется при выходе из 3D и в снимке экспорта. Doom хранит XY / направление, Hexen — также высоту; наклон — в проекте. Ручная запись отменяется через Undo.",14);
            markerHint.alignment=TextAnchor.UpperLeft;markerHint.gameObject.AddComponent<LayoutElement>().minHeight=80;
            var cameraHint=Label(body,"«К точке» — к последнему касанию поверхности или объекта. После движения камеры цель — прицел. Угол сохраняется; карта и Undo не меняются.",14);
            cameraHint.alignment=TextAnchor.UpperLeft;cameraHint.gameObject.AddComponent<LayoutElement>().minHeight=64;
            var help=Label(body,"Удерживайте стрелки для движения, кнопку — для полного названия. Один палец поворачивает камеру, касание выбирает элемент. Два пальца перемещают и приближают. На ПК: Alt фиксирует первый палец, курсор — второй; WASD и Q/E также двигают камеру.",16);
            help.alignment=TextAnchor.UpperLeft;help.gameObject.AddComponent<LayoutElement>().minHeight=96;
        }
        void VisualToolsThings(RectTransform body)
        {
            ToolsCaption(body,"ОБЪЕКТЫ");var row=ShapeOptionRow(body);
            ToolsCommand(row,"Повернуть объекты -45°","Поворот −45°",EditorIcon.Symbol.ThingPoint,()=>{CloseDialog();RotateVisualThings(-45);});
            ToolsCommand(row,"Повернуть объекты +45°","Поворот +45°",EditorIcon.Symbol.ThingPoint,()=>{CloseDialog();RotateVisualThings(45);});
            if(Map.format!=MapFormat.Doom)
            {
                row=ShapeOptionRow(body);ToolsCommand(row,"Объекты выше","Объекты выше",EditorIcon.Symbol.Up,()=>{CloseDialog();MoveVisualThings(0,0,1);});
                ToolsCommand(row,"Объекты ниже","Объекты ниже",EditorIcon.Symbol.Down,()=>{CloseDialog();MoveVisualThings(0,0,-1);});
            }
            row=ShapeOptionRow(body);ToolsCommand(row,"Удалить отмеченные объекты…","Удалить объекты…",EditorIcon.Symbol.Delete,DeleteVisualThings);
            if(Map.format==MapFormat.Doom)
            {
                var note=Label(body,"Doom размещает объекты на полу / потолке; отдельная высота Z доступна в Hexen.",14);note.alignment=TextAnchor.UpperLeft;note.gameObject.AddComponent<LayoutElement>().minHeight=40;
            }
        }
        void VisualToolsSurface(RectTransform body,bool walls,OpenContext context,VisualView.SelectionSnapshot marks)
        {
            var surface=visualView.Picked.Value;ToolsCaption(body,"ПОВЕРХНОСТИ");var row=ShapeOptionRow(body);string copied=surfaceClipboard.Texture(surface);
            ToolsCommand(row,"Залить скопированной текстурой","Залить текстурой",EditorIcon.Symbol.Paste,()=>{CloseDialog();VisualFloodFill();},copied!=null);
            if(!surface.IsFlat)ToolsCommand(row,"Выровнять текстуру стены…","Выровнять текстуру…",EditorIcon.Symbol.Texture,()=>TextureAlignmentAxes(surface));
            row=ShapeOptionRow(body);ToolsCommand(row,"Свет +16","Свет +16",EditorIcon.Symbol.BrightnessGradient,()=>{CloseDialog();VisualLight(16);});
            ToolsCommand(row,"Свет −16","Свет −16",EditorIcon.Symbol.BrightnessGradient,()=>{CloseDialog();VisualLight(-16);});
            if(walls||MapFormats.IsUniversal(Map.format))
            {
                row=ShapeOptionRow(body);VisualToolsShift(row,"Текстура влево · 8","Текстура ← 8",new Vector2(-8,0),90,context,marks,surface);
                VisualToolsShift(row,"Текстура вправо · 8","Текстура → 8",new Vector2(8,0),-90,context,marks,surface);
                row=ShapeOptionRow(body);VisualToolsShift(row,"Текстура вверх · 8","Текстура ↑ 8",new Vector2(0,-8),0,context,marks,surface);
                VisualToolsShift(row,"Текстура вниз · 8","Текстура ↓ 8",new Vector2(0,8),180,context,marks,surface);
            }
            if(copied!=null){var note=Label(body,"Заливка: "+copied+". Связанные поверхности с той же текстурой; несколько отметок ограничивают область выделением. Undo отменяет всю заливку.",14);note.alignment=TextAnchor.UpperLeft;note.gameObject.AddComponent<LayoutElement>().minHeight=56;}
        }
        void VisualToolsShift(RectTransform row,string title,string label,Vector2 delta,float angle,OpenContext context,VisualView.SelectionSnapshot marks,SurfaceRef surface)
        {
            var button=ToolsCommand(row,title,label,EditorIcon.Symbol.Up,()=>
            {
                if(!CurrentOpenContext(context)||!SameVisualTextureSelection(marks)){Notify("Карта или выделение изменились. Откройте 3D-инструменты снова.");return;}
                CloseDialog();if(!MapFormats.IsUniversal(Map.format))VisualMoveWallTextures((int)delta.x,(int)delta.y);
#if MOBILEUDB_UDMF // Deferred from the first release.
                else Edit(m=>
                {
                    MapElementData element=surface.IsFlat?(MapElementData)m.sectors[surface.sector]:m.sides[surface.side];
                    string suffix=surface.part==SurfacePart.Floor?"floor":surface.part==SurfacePart.Ceiling?"ceiling":surface.part==SurfacePart.Upper?"_top":surface.part==SurfacePart.Lower?"_bottom":"_mid";
                    string x=(surface.IsFlat?"xpanning":"offsetx")+suffix,y=(surface.IsFlat?"ypanning":"offsety")+suffix;
                    UdmfText.SetRaw(element.fields,x,(VisualGeometry.Field(element,x,0)+delta.x).ToString("R",System.Globalization.CultureInfo.InvariantCulture));
                    UdmfText.SetRaw(element.fields,y,(VisualGeometry.Field(element,y,0)+delta.y).ToString("R",System.Globalization.CultureInfo.InvariantCulture));
                },"Текстура сдвинута.");
#endif
            });button.GetComponentInChildren<EditorIcon>().transform.localRotation=Quaternion.Euler(0,0,angle);
        }
    }
}

