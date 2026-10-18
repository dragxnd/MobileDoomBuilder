using System;
using System.Collections.Generic;
using MobileUDB.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        MapClipboard clipboard;
        string clipboardGame, clipboardActions;
        bool includeSectorThings = true;
        GeometryMergeMode geometryMergeMode = GeometryMergeMode.Replace;
        PasteTagMode pasteTagMode = PasteTagMode.Keep;
        static PasteTagMode ValidPasteTagMode(string value)
        {
            if(string.IsNullOrEmpty(value)) return PasteTagMode.Keep;
            foreach(PasteTagMode mode in Enum.GetValues(typeof(PasteTagMode))) if(string.Equals(value,mode.ToString(),StringComparison.OrdinalIgnoreCase)) return mode;
            throw new InvalidOperationException("Неизвестный режим тегов вставки: " + value);
        }
        string PasteTagLabel { get { return pasteTagMode == PasteTagMode.Renumber ? "новые свободные" : pasteTagMode == PasteTagMode.Remove ? "убрать" : "сохранить"; } }
        void PasteTagMenu()
        {
            ShowDialog("Теги при вставке",body =>
            {
                foreach(PasteTagMode item in Enum.GetValues(typeof(PasteTagMode)))
                {
                    var chosen=item;
                    string label=item == PasteTagMode.Renumber ? "Новые свободные теги" : item == PasteTagMode.Remove ? "Убрать теги" : "Сохранить теги";
                    AddButton(body,label+(pasteTagMode==item?" · выбрано":""),()=>{pasteTagMode=chosen;CloseDialog();Changed();Notify("Теги следующей вставки: "+PasteTagLabel+".");});
                }
                AddLabel(body,"Новые: одинаковые теги внутри копии остаются связаны, номера выбираются свободными в текущей карте. Нулевые теги сохраняются. Сохранить: копия может управлять исходными секторами и объектами.");
                AddLabel(body,"Меняются ID, moreids и аргументы, отмеченные как ссылки на теги в текущем профиле действий. Скрипты и неизвестные аргументы не переписываются. Убрать обнуляет эти теги и ссылки, сохраняя коды действий.");
                AddLabel(body,"Настройка сохраняется в проекте. Применяется только при «Готово»; Undo отменяет всю вставку. Для Hexen учитывается предел аргумента 255; если свободных номеров нет, вставка отклоняется.");
            });
        }
        static GeometryMergeMode ValidGeometryMode(string value)
        {
            if (string.IsNullOrEmpty(value) || string.Equals(value,"Replace",StringComparison.OrdinalIgnoreCase)) return GeometryMergeMode.Replace;
            if (string.Equals(value,"Merge",StringComparison.OrdinalIgnoreCase)) return GeometryMergeMode.Merge;
            if (string.Equals(value,"Classic",StringComparison.OrdinalIgnoreCase)) return GeometryMergeMode.Classic;
            throw new InvalidOperationException("Неизвестный режим сшивки: " + value);
        }
        string StitchLabel { get { return geometryMergeMode == GeometryMergeMode.Classic ? "классическая" : geometryMergeMode == GeometryMergeMode.Replace ? "заменить стены" : "сохранить стены"; } }
        List<ElementRef> pasteSelection;
        ElementKind pasteKind;
        MapPoint pastePosition;
        public MapDocument PastePreview { get; private set; }
        Button copyButton, pasteButton, pasteCancelButton;

        void RefreshClipboardButtons()
        {
            if (copyButton == null) return;
            copyButton.interactable = Selection.Count > 0 && Tool != MobileTool.Paste && Draft.Count == 0;
            pasteButton.interactable = clipboard != null && Tool != MobileTool.Paste && Draft.Count == 0;
            pasteCancelButton.gameObject.SetActive(!VisualActive && (Tool == MobileTool.Paste || Tool == MobileTool.DrawRoom || Tool == MobileTool.CutSector || Tool == MobileTool.Transform || Tool == MobileTool.PlaceCamera));
            pasteCancelButton.GetComponentInChildren<Text>().text=Tool==MobileTool.Transform?"Выйти":"Отмена";
            if (Tool == MobileTool.Paste) { propertiesButton.interactable = deleteButton.interactable = redoButton.interactable = false; undoButton.interactable = true; }
        }
        void CopySelection()
        {
            CancelDrag();
            try
            {
                clipboard = MapClipboard.Copy(Map,Selection,Kind,includeSectorThings);
                clipboardGame = effectiveProfile; clipboardActions = Actions.label;
                Notify("Скопировано: " + clipboard.LineCount + " стен, " + clipboard.SectorCount + " секторов, " + clipboard.ThingCount + " объектов. Теги и действия сохраняются.");
            }
            catch (Exception error) { Notify(error.Message); }
        }
        void StartPaste()
        {
            if (clipboard == null) { Notify("Буфер пуст. Сначала скопируйте выделение."); return; }
            if (Draft.Count > 0) { Notify("Завершите или отмените рисование."); return; }
            if (clipboard.Format != Map.format || (MapFormats.IsUniversal(Map.format) && !string.Equals(clipboard.Namespace,Map.udmfNamespace,StringComparison.OrdinalIgnoreCase)) ||
                clipboardGame != effectiveProfile || clipboardActions != Actions.label)
            { Notify("Игра, формат и профиль действий должны совпадать с буфером. Конвертация вставки пока недоступна."); return; }
            CancelDrag(); if (VisualActive) ExitVisual();
            pasteSelection = new List<ElementRef>(Selection); pasteKind = Kind;
            Selection.Clear(); Tool = MobileTool.Paste;
            pastePosition = clipboard.Origin + gridFrame.MoveOffset(clipboard.Origin,new MapPoint(View.center.x,View.center.y) - clipboard.Origin);
            PastePreview = clipboard.Preview(pastePosition);
            Notify("Разместите копию → «Готово». Теги: " + PasteTagLabel + ". Настройки — «Ещё»."); Changed(false);
        }
        void PositionPaste(Vector2 screen)
        {
            if (PastePreview == null || !View.ScreenRect.Contains(screen)) return;
            if(DuplicatingThings){PositionThingDuplicate(screen);return;}
            MapPoint next = clipboard.Origin + gridFrame.MoveOffset(clipboard.Origin,View.World(screen) - clipboard.Origin), delta = next - pastePosition;
            foreach (var vertex in PastePreview.vertices) vertex.Position += delta;
            foreach (var thing in PastePreview.things) thing.Position += delta;
            pastePosition = next; View.SetVerticesDirty();
        }
        void CommitPaste()
        {
            if (PastePreview == null) return;
            if(DuplicatingThings){CommitThingDuplicate();return;}
            bool done = Edit(m => { var selected = clipboard.Paste(m,pastePosition,geometryMergeMode,resources,pasteTagMode,Actions); Selection.Clear(); Selection.UnionWith(selected); Kind = pasteKind; },"Копия вставлена. Теги: " + PasteTagLabel + ". Доступна отмена.");
            if (!done) return;
            CancelPaste(false); Changed(false);
        }
        void CancelPaste(bool restoreSelection = true)
        {
            if (PastePreview == null) return;
            ClearThingDuplicate();
            PastePreview = null; Tool = MobileTool.Select;
            if (restoreSelection && pasteSelection != null) { Selection.Clear(); Selection.UnionWith(pasteSelection); Kind = pasteKind; }
            pasteSelection = null;
            if (View != null) View.SetVerticesDirty();
        }
        void TransformMenu()
        {
            ShowDialog("Преобразование",body =>
            {
                AddButton(body,"Размер и поворот пальцем на карте…",StartTransformHandles).interactable=Selection.Count>0&&Draft.Count==0;
                var errorLabel = Label(body,"",18); errorLabel.alignment = TextAnchor.MiddleLeft; errorLabel.color = new Color32(255,170,140,255);
                errorLabel.gameObject.AddComponent<LayoutElement>().minHeight = 56; errorLabel.gameObject.SetActive(false);
                var angle = Field(body,"Угол, °","0",false); angle.keyboardType = TouchScreenKeyboardType.NumbersAndPunctuation;
                var scaleX = Field(body,"Масштаб X, %","100",false); scaleX.keyboardType = TouchScreenKeyboardType.NumbersAndPunctuation;
                var scaleY = Field(body,"Масштаб Y, %","100",false); scaleY.keyboardType = TouchScreenKeyboardType.NumbersAndPunctuation;
                foreach(var input in new[]{angle,scaleX,scaleY}) input.transform.parent.GetComponent<LayoutElement>().preferredHeight=48;
                AddButton(body,"Применить угол и масштаб",() =>
                {
                    try
                    {
                        float degrees = RealNumber(angle,-36000,36000),x = RealNumber(scaleX,.1f,10000) * .01f,y = RealNumber(scaleY,.1f,10000) * .01f;
                        if (degrees % 360 == 0 && Math.Abs(x - 1) < .000001f && Math.Abs(y - 1) < .000001f) throw new InvalidOperationException("Угол и масштаб не меняют выделение.");
                        if (Edit(m => { var selected = MapTransforms.Transform(m,Selection,degrees,x,y,includeSectorThings,id => !thingCatalog.Find(id).fixedRotation,geometryMergeMode,resources); Selection.Clear(); Selection.UnionWith(selected); },"Угол и масштабы X/Y применены, стены сшиты. Доступна отмена.")) CloseDialog();
                        else { errorLabel.text = message; errorLabel.gameObject.SetActive(true); }
                    }
                    catch (Exception error) { errorLabel.text = error.Message; errorLabel.gameObject.SetActive(true); }
                });
                var row = Rect("ПРЕОБРАЗОВАНИЕ",body); row.gameObject.AddComponent<LayoutElement>().preferredHeight = 68;
                RectTransform group; var buttons = MakeToolbarGroup(row,"ПРЕОБРАЗОВАНИЕ",out group); Stretch(group);
                var left = IconButton(buttons,"Повернуть на 90° влево",EditorIcon.Symbol.Undo,() => TransformSelection(SelectionTransform.RotateLeft)); Badge(left.transform,"90°",false);
                var right = IconButton(buttons,"Повернуть на 90° вправо",EditorIcon.Symbol.Redo,() => TransformSelection(SelectionTransform.RotateRight)); Badge(right.transform,"90°",false);
                IconButton(buttons,"Отразить слева направо",EditorIcon.Symbol.FlipX,() => TransformSelection(SelectionTransform.MirrorX));
                IconButton(buttons,"Отразить сверху вниз",EditorIcon.Symbol.FlipY,() => TransformSelection(SelectionTransform.MirrorY));
                AddLabel(body,"Сначала масштаб по осям карты, затем поворот вокруг центра выделения. Угол + — влево. Одинаковые X/Y — равномерный масштаб; 100% — без изменения оси.");
                AddLabel(body,"В бинарных картах рисунок пола и потолка привязан к координатам карты. Направление объектов меняется от поворота.");
                AddLabel(body,"На карте: квадраты меняют размер по осям, круг поворачивает вокруг центра. Привязка включает шаг 5% и 15°. Два пальца / Alt отменяют текущий жест и управляют видом.");
                AddLabel(body,"Объекты с сектором: " + (includeSectorThings ? "Да" : "Нет") + " · настройка в «Ещё». Сшивка: " + StitchLabel + ". Соседние стены меняют форму. Наклоны сохраняют высоты соответствующих точек; теги не меняются.");
            });
        }
        void StitchMenu()
        {
            ShowDialog("Сшивка геометрии",body =>
            {
                AddButton(body,"Заменить стены" + (geometryMergeMode == GeometryMergeMode.Replace ? " · выбрано" : ""),() => { geometryMergeMode = GeometryMergeMode.Replace; CloseDialog(); Changed(); Notify("Сшивка: заменить стены внутри контура. Их действия удалятся; результат можно отменить."); });
                AddButton(body,"Сохранить стены" + (geometryMergeMode == GeometryMergeMode.Merge ? " · выбрано" : ""),() => { geometryMergeMode = GeometryMergeMode.Merge; CloseDialog(); Changed(); Notify("Сшивка: сохранить внутренние стены и их действия."); });
                AddButton(body,"Классическая" + (geometryMergeMode == GeometryMergeMode.Classic ? " · выбрано" : ""),() => { geometryMergeMode = GeometryMergeMode.Classic; CloseDialog(); Changed(); Notify("Классическая сшивка: вершины и совпадающие стены; пересечения и сектора не перестраиваются."); });
                AddLabel(body,"Заменить / сохранить: разрезать пересечения, объединить границы и перестроить секторы. Различаются удалением внутренних стен. Действие копии имеет приоритет, если задано.");
                AddLabel(body,"Классическая: соединить вершины и совпадающие отрезки без разрезания остальных пересечений и перестройки секторов. Перекрывающиеся сектора сохраняются. При совпадении стен свойства перемещаемой стены имеют приоритет; пустая стена наследует свойства существующей.");
                AddLabel(body,"Режим сохраняется в проекте и применяется к переносу, преобразованию, вставке и комнатам. Цепочки и разрезы всегда строят новые области. Теги вставки настраиваются отдельно в «Ещё». Операции отменяются одним Undo.");
            });
        }
        void TransformSelection(SelectionTransform operation)
        {
            CloseDialog(); Edit(m => { var selected = MapTransforms.Transform(m,Selection,operation,includeSectorThings,id => !thingCatalog.Find(id).fixedRotation,geometryMergeMode,resources); Selection.Clear(); Selection.UnionWith(selected); },"Выделение преобразовано, стены сшиты. Доступна отмена.");
        }
    }
}
