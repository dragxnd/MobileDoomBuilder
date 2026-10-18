using System;using MobileUDB.Core;using UnityEngine;using UnityEngine.UI;
namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        Button ToolsCommand(RectTransform row,string title,string shortTitle,EditorIcon.Symbol symbol,Action action,bool enabled=true)
        {
            var button=IconButton(row,title,symbol,action);button.name=title;button.interactable=enabled;
            var icon=button.GetComponentInChildren<EditorIcon>().rectTransform;icon.anchorMin=icon.anchorMax=new Vector2(0,.5f);icon.sizeDelta=new Vector2(24,24);icon.anchoredPosition=new Vector2(20,0);
            var label=Label(button.transform,shortTitle,16);Stretch(label.rectTransform);label.rectTransform.offsetMin=new Vector2(38,0);label.rectTransform.offsetMax=new Vector2(-4,0);
            label.resizeTextForBestFit=true;label.resizeTextMinSize=14;label.resizeTextMaxSize=16;return button;
        }
        void ToolsCaption(RectTransform body,string title){CurveCaption(body,title);}
        void BuildToolsMenu(RectTransform body)
        {
            body.GetComponent<VerticalLayoutGroup>().spacing=5;
            ToolsCommand(ShapeOptionRow(body),"Просмотреть блокмапу","Блокмапа",EditorIcon.Symbol.Blockmap,EnterBlockmap,
                !VisualActive&&Draft.Count==0&&Tool!=MobileTool.Paste&&!ThingAimActive&&!MakingSector&&!CurvingLines&&!BridgingLines);
            ToolsCommand(ShapeOptionRow(body),"Просмотреть распространение звука","Распространение звука",EditorIcon.Symbol.SoundPropagation,EnterSoundPropagation,
                !VisualActive&&Draft.Count==0&&Tool!=MobileTool.Paste&&!ThingAimActive&&!MakingSector&&!CurvingLines&&!BridgingLines);
            ToolsCommand(ShapeOptionRow(body),"Редактировать автокарту","Автокарта",EditorIcon.Symbol.Automap,EnterAutomap,
                !VisualActive&&Draft.Count==0&&Tool!=MobileTool.Paste&&!ThingAimActive&&!MakingSector&&!CurvingLines&&!BridgingLines);
            // Put the current editing family's commands within the first screen.
            if(Kind==ElementKind.Line)ToolsWalls(body);
            else if(Kind==ElementKind.Sector)ToolsSectors(body);
            else if(Kind==ElementKind.Thing)ToolsThings(body);
            else ToolsDrawing(body);
            ToolsSelection(body);
            if(Kind!=ElementKind.Line)ToolsWalls(body);
            if(Kind!=ElementKind.Sector)ToolsSectors(body);
            if(Kind!=ElementKind.Thing)ToolsThings(body);
            if(Kind!=ElementKind.Vertex)ToolsDrawing(body);
            ToolsSettings(body);ToolsMap(body);ToolsView(body);
            
        }
        void ToolsSelection(RectTransform body)
        {
            ToolsCaption(body,"ВЫДЕЛЕНИЕ И СВОЙСТВА");var row=ShapeOptionRow(body);
            ToolsCommand(row,"Выделение: рамка, всё, инвертировать…","Выделение…",EditorIcon.Symbol.Select,SelectionMenu);
            ToolsCommand(row,"Поворот / размер / отражение…","Поворот / размер…",EditorIcon.Symbol.Move,TransformMenu);
            ElementRef source;SurfaceRef? wall;bool copy=PropertySource(out source,out wall);bool paste=PropertySelection().fields!=PropertyFields.None;
            row=ShapeOptionRow(body);ToolsCommand(row,"Копировать свойства","Копировать свойства",EditorIcon.Symbol.Copy,()=>{CloseDialog();CopyProperties();},copy);
            ToolsCommand(row,"Вставить свойства","Вставить свойства",EditorIcon.Symbol.Paste,()=>{CloseDialog();PasteProperties(PropertyFields.All,null);},paste);
            row=ShapeOptionRow(body);ToolsCommand(row,"Выбрать свойства для вставки…","Поля для вставки…",EditorIcon.Symbol.Properties,PropertyPasteOptions,paste);
        }
        void ToolsWalls(RectTransform body)
        {
            ToolsCaption(body,"ГЕОМЕТРИЯ СТЕН");var row=ShapeOptionRow(body);
            ToolsCommand(row,"Разделить…","Разделить…",EditorIcon.Symbol.Vertex,LineSplitMenu,LineDissolveReady);
            ToolsCommand(row,"Растворить стены…","Растворить стены…",EditorIcon.Symbol.Merge,LineDissolveMenu,LineDissolveReady);
            row=ShapeOptionRow(body);ToolsCommand(row,"Построить дугу из стен…","Дуга из стен…",EditorIcon.Symbol.CurveLines,CurveLinedefMenu,CanCurveLinedefs());
            ToolsCommand(row,"Мост между стенами…","Мост…",EditorIcon.Symbol.Bridge,BridgeMenu,CanBridgeLines()||BridgingLines);
            row=ShapeOptionRow(body);ToolsCommand(row,"Направление и стороны стен…","Направление / стороны…",EditorIcon.Symbol.WallFlip,WallDirectionMenu);
            ToolsCommand(row,"Выровнять текстуры стен…","Выровнять текстуры…",EditorIcon.Symbol.Texture,TextureAlignmentMenu);
        }
        void ToolsSectors(RectTransform body)
        {
            ToolsCaption(body,"ГЕОМЕТРИЯ СЕКТОРОВ");var row=ShapeOptionRow(body);
            ToolsCommand(row,"Объединить секторы…","Объединить…",EditorIcon.Symbol.Join,JoinSectorsMenu,SectorJoinReady);
            bool cut=!VisualActive&&Selection.Count==1&&Kind==ElementKind.Sector&&Draft.Count==0&&Tool!=MobileTool.Paste;
            ToolsCommand(row,"Разделить выбранный сектор стеной","Разрезать стеной",EditorIcon.Symbol.Cut,()=>{CloseDialog();SetTool(MobileTool.CutSector);},cut);
            row=ShapeOptionRow(body);ToolsCommand(row,"Создать двери…","Двери…",EditorIcon.Symbol.Door,DoorMenu,DoorsReady);
            ToolsCommand(row,"Градиенты…","Градиенты…",EditorIcon.Symbol.BrightnessGradient,SectorGradientMenu,GradientsReady);
            row=ShapeOptionRow(body);ToolsCommand(row,"Создать сектор из контура…","Из контура…",EditorIcon.Symbol.MakeSector,MakeSectorMenu,!VisualActive&&Draft.Count==0&&Tool!=MobileTool.Paste);
            ToolsCommand(row,"Объекты вместе с сектором: "+(includeSectorThings?"Да":"Нет"),"С объектами: "+(includeSectorThings?"Да":"Нет"),EditorIcon.Symbol.Thing,()=>{includeSectorThings=!includeSectorThings;MoreMenu();});
        }
        void ToolsThings(RectTransform body)
        {
            ToolsCaption(body,"ОБЪЕКТЫ");var row=ShapeOptionRow(body);bool selected=OrientationTargets().Count>0;
            ToolsCommand(row,"Направление объектов…","Направление…",EditorIcon.Symbol.ThingPoint,ThingOrientationMenu,selected);
            ToolsCommand(row,"Дублировать объекты…","Дублировать…",EditorIcon.Symbol.Copy,StartThingDuplicate,selected&&!VisualActive);
            row=ShapeOptionRow(body);ToolsCommand(row,"По выделению…","Создать по выделению…",EditorIcon.Symbol.PlaceThings,ThingPlacementMenu,ThingPlacementReady);
        }
        void ToolsDrawing(RectTransform body)
        {
            ToolsCaption(body,"РИСОВАНИЕ");var row=ShapeOptionRow(body);
            ToolsCommand(row,"Рисование: комнаты / стены / фигуры…","Комнаты / фигуры…",EditorIcon.Symbol.Room,DrawingMenu);
            ToolsCommand(row,"Отменить рисование","Отменить рисование",EditorIcon.Symbol.Delete,()=>{CloseDialog();CancelDraft();});
            row=ShapeOptionRow(body);ToolsCommand(row,"Убрать первую точку рисования","Убрать первую точку",EditorIcon.Symbol.Vertex,()=>{CloseDialog();RemoveFirstDraftPoint();},Tool==MobileTool.DrawRoom&&Draft.Count>0);
        }
        void ToolsSettings(RectTransform body)
        {
            ToolsCaption(body,"СЕТКА И ВСТАВКА");var row=ShapeOptionRow(body);
            ToolsCommand(row,"Сетка · шаг "+GridStepText+"…","Сетка · "+GridStepText+"…",EditorIcon.Symbol.Grid,GridSettingsMenu);
            ToolsCommand(row,"Сшивка: "+StitchLabel+"…","Сшивка…",EditorIcon.Symbol.Merge,StitchMenu);
            row=ShapeOptionRow(body);ToolsCommand(row,"Теги при вставке: "+PasteTagLabel+"…","Теги вставки…",EditorIcon.Symbol.Paste,PasteTagMenu);
            ToolsCommand(row,"Подписи тегов…","Подписи тегов…",EditorIcon.Symbol.Properties,TagLabelsMenu,DbsGroupsReady);
        }
        void ToolsMap(RectTransform body)
        {
            ToolsCaption(body,"КАРТА И ПОИСК");var row=ShapeOptionRow(body);
            ToolsCommand(row,"Проверить карту…","Проверить карту…",EditorIcon.Symbol.Check,CheckMap);
            ToolsCommand(row,"Найти объекты / действия…","Объекты / действия…",EditorIcon.Symbol.FindMode,ElementSearchMenu);
            row=ShapeOptionRow(body);ToolsCommand(row,"Найти / заменить текстуры…","Найти текстуры…",EditorIcon.Symbol.FindMode,TextureSearchMenu);
            ToolsCommand(row,"Открыть 3D-режим","3D-режим",EditorIcon.Symbol.Visual,()=>{CloseDialog();SetTool(MobileTool.Visual);});
            row=ShapeOptionRow(body);ToolsCommand(row,"Найти / заменить теги и TID…","Теги / TID…",EditorIcon.Symbol.FindMode,TagSearchMenu);
        }
        void ToolsView(RectTransform body)
        {
            ToolsCaption(body,"НАСТРОЙКИ");var settingsRow=ShapeOptionRow(body);ToolsCommand(settingsRow,"Настройки приложения…","Настройки…",EditorIcon.Symbol.Configuration,SettingsMenu);
            ToolsCaption(body,"ВИД И СПРАВКА");var row=ShapeOptionRow(body);
            ToolsCommand(row,"Масштаб +","Масштаб +",EditorIcon.Symbol.Zoom,()=>{CloseDialog();View.ZoomAt(View.ScreenRect.center,1.4f);});
            ToolsCommand(row,"Масштаб −","Масштаб −",EditorIcon.Symbol.Zoom,()=>{CloseDialog();View.ZoomAt(View.ScreenRect.center,1/1.4f);});
            row=ShapeOptionRow(body);ToolsCommand(row,"Перейти к координатам…","Координаты…",EditorIcon.Symbol.Fit,GoToCoordinatesMenu);
            ToolsCommand(row,"Разместить стартовую камеру UDB касанием на 2D-карте","Поставить камеру…",EditorIcon.Symbol.Visual,BeginCameraPlacement,!VisualActive&&Draft.Count==0&&Tool!=MobileTool.Paste);
            row=ShapeOptionRow(body);ToolsCommand(row,"Фон карты…","Фон…",EditorIcon.Symbol.Grid,BackgroundMenu);
            row=ShapeOptionRow(body);ToolsCommand(row,"Вид карты: линии / свет / пол / потолок…","Вид карты…",EditorIcon.Symbol.ViewNormal,ClassicViewMenu);
            row=ShapeOptionRow(body);ToolsCommand(row,"Справка по значкам — удерживайте кнопку для подсказки","Справка по значкам…",EditorIcon.Symbol.Help,()=>{CloseDialog();ToolbarHelp();});
            var hint=Label(body,"Удерживайте кнопку, чтобы прочитать полное название. Один палец — инструмент; два — масштаб и обзор. На ПК удерживайте Alt: место нажатия — первый палец, курсор — второй.",16);hint.alignment=TextAnchor.UpperLeft;hint.gameObject.AddComponent<LayoutElement>().minHeight=60;
        }
    }
}

