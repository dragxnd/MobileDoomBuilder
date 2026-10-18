using System;
using MobileUDB.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        bool drawOpenChain;
        Button closeChainButton;
        public bool DrawingChain { get { return drawOpenChain; } }
        void DrawingMenu()
        {
            if (Draft.Count > 0) { Notify("Завершите или отмените текущее рисование."); return; }
            ShowDialog("Рисование",body =>
            {
                AddButton(body,"Замкнутая комната",() => StartDrawing(false));
                AddButton(body,"Цепочка стен",() => StartDrawing(true));
                var curves=ShapeOptionRow(body);
                VisualThingCommand(curves,"Кривая",EditorIcon.Symbol.Curve,()=>CurveSettingsMenu());
                LineBatchCaption(body,"ФИГУРЫ · ДВЕ ТОЧКИ");
                var shapes=ShapeOptionRow(body);
                VisualThingCommand(shapes,"Прямоугольник",EditorIcon.Symbol.Rectangle,()=>ShapeSettingsMenu(DrawShapeKind.Rectangle));
                VisualThingCommand(shapes,"Эллипс",EditorIcon.Symbol.Ellipse,()=>ShapeSettingsMenu(DrawShapeKind.Ellipse));
                var grids=ShapeOptionRow(body);
                VisualThingCommand(grids,"Сетка секторов",EditorIcon.Symbol.DrawGrid,()=>DrawGridSettingsMenu());
                AddButton(body,"Настройки рисования…",DrawSettingsMenu);
                AddButton(body,"Выровнять новые стены по X: " + (drawTextures.alignX ? "Да" : "Нет"),() => { drawTextures.alignX = !drawTextures.alignX; Changed(); DrawingMenu(); });
                AddLabel(body,"Касания ставят точки. Отмена сверху убирает последнюю точку, «Отмена» на карте отменяет всё рисование.");
                AddLabel(body,"Выравнивание использует ширину подключённых текстур. Старые стены и смещения по Y сохраняются.");
                AddLabel(body,"Цепочка может оставаться открытой. «Готово» создаёт только указанные стены; «Замкнуть» добавляет стену к первой точке. Пересечения сшиваются, замкнувшиеся области становятся секторами.");
            });
        }
        void StartDrawing(bool chain)
        {
            if (VisualActive) ExitVisual();
            CloseDialog();drawingShape=DrawShapeKind.None;ClearShapePointer();drawOpenChain = chain; Tool = MobileTool.DrawRoom; Changed(false);
            Notify(chain ? "Касания — точки цепочки. «Готово» — открытые стены, «Замкнуть» — соединить с первой точкой." : "Касания — точки комнаты. «Готово» замыкает контур.");
        }
        MapPoint DrawingPoint(MapPoint point)
        {
            // Vertices have priority over nearby walls. Geometry snapping remains
            // useful when an existing map's vertices are off the current grid.
            float radius = 16 / View.zoom; var hit = Map.Hit(point,ElementKind.Vertex,radius);
            if (hit.HasValue) return Map.vertices[hit.Value.index].Position;
            var grid = Snapped(point); MapPoint nearest = grid; double distance = radius * (double)radius;
            foreach (var line in Map.lines)
            {
                var a = Map.vertices[line.start].Position; var b = Map.vertices[line.end].Position;
                double dx = b.x - (double)a.x,dy = b.y - (double)a.y,length = dx * dx + dy * dy;
                if (length < .000001) continue;
                double along = Math.Max(0,Math.Min(1,((grid.x - a.x) * dx + (grid.y - a.y) * dy) / length));
                var candidate = new MapPoint((float)(a.x + dx * along),(float)(a.y + dy * along));
                // Keep the exact wall contact through intersection planning.
                // Binary coordinates are rounded once the shared vertex is known.
                double d = (candidate - point).LengthSquared;
                if (d < distance) { distance = d; nearest = candidate; }
            }
            return nearest;
        }
        void FinishLines(bool close)
        {
            Edit(m =>
            {
                var result = m.DrawLines(Draft,close,resources,drawTextures); Draft.Clear(); Selection.Clear();
                if (result.sectors.Count > 0) { Kind = ElementKind.Sector; foreach (int sector in result.sectors) Selection.Add(new ElementRef(Kind,sector)); }
                else { Kind = ElementKind.Line; foreach (int line in result.lines) Selection.Add(new ElementRef(Kind,line)); }
                Tool = MobileTool.Select;
            },"Стены созданы, пересечения сшиты. Доступна отмена.");
        }
        void CancelDraft()
        {
            Draft.Clear();drawingShape=DrawShapeKind.None;ClearShapePointer();Tool = MobileTool.Select; cutSector = -1; Changed(false); Notify("Рисование отменено.");
        }
        void RemoveFirstDraftPoint()
        {CancelDrag();if(Draft.Count==0)return;Draft.RemoveAt(0);Changed(false);Notify("Первая опорная точка убрана.");}
    }
}
