// Mobile port of Ultimate Doom Builder. GPL-3.0; see LICENSE.txt.
using System;
using System.Collections.Generic;

namespace MobileUDB.Core
{
    public sealed partial class MapDocument
    {
        const double TopologyEpsilon = 0.001;
        static double EdgeParameter(MapPoint a, MapPoint b, MapPoint p)
        {
            double dx = (double)b.x - a.x, dy = (double)b.y - a.y;
            double length = dx * dx + dy * dy;
            return length == 0 ? 0 : (((double)p.x - a.x) * dx + ((double)p.y - a.y) * dy) / length;
        }
        static bool OnEdge(MapPoint a, MapPoint b, MapPoint p, bool interior = false)
        {
            double t = EdgeParameter(a, b, p);
            if (t < 0 || t > 1 || (interior && (t <= 0 || t >= 1))) return false;
            double dx = p.x - (a.x + t * ((double)b.x - a.x));
            double dy = p.y - (a.y + t * ((double)b.y - a.y));
            return dx * dx + dy * dy <= TopologyEpsilon * TopologyEpsilon;
        }
        static bool OnBoundary(List<MapPoint> polygon, MapPoint p)
        {
            for (int i = 0; i < polygon.Count; i++) if (OnEdge(polygon[i], polygon[(i + 1) % polygon.Count], p)) return true;
            return false;
        }
        bool InSectorInterior(int sector, MapPoint p)
        {
            if (!Contains(sector, p)) return false;
            foreach (var l in lines)
            {
                bool front = l.front >= 0 && sides[l.front].sector == sector;
                bool back = l.back >= 0 && sides[l.back].sector == sector;
                if (front != back && OnEdge(vertices[l.start].Position, vertices[l.end].Position, p)) return false;
            }
            return true;
        }
        void AddAdjacentRoom(List<MapPoint> points)
        {
            if (points == null || points.Count < 3) throw new InvalidOperationException("Нужно минимум три точки.");
            var polygon = new List<MapPoint>(points);
            double area = 0;
            for (int i = 0; i < polygon.Count; i++)
            {
                var a = polygon[i]; var b = polygon[(i + 1) % polygon.Count];
                if (!Finite(a.x) || !Finite(a.y)) throw new InvalidOperationException("Некорректная точка контура.");
                var next = polygon[(i + 2) % polygon.Count];
                if (OnEdge(a, b, next, true) || OnEdge(b, next, a, true))
                    throw new InvalidOperationException("Соседние отрезки контура накладываются друг на друга.");
                area += a.x * (double)b.y - b.x * (double)a.y;
                for (int j = i + 1; j < polygon.Count; j++)
                    if ((a - polygon[j]).LengthSquared < 0.01f) throw new InvalidOperationException("Повторяющиеся точки контура.");
                for (int j = i + 1; j < polygon.Count; j++)
                {
                    if (j == i + 1 || (i == 0 && j == polygon.Count - 1)) continue;
                    if (SegmentsIntersect(a, b, polygon[j], polygon[(j + 1) % polygon.Count]))
                        throw new InvalidOperationException("Контур пересекает сам себя.");
                }
            }
            if (Math.Abs(area) < 0.1) throw new InvalidOperationException("Контур не имеет площади.");
            if (area > 0) polygon.Reverse(); // Front sidedef is on the right.
            var usedVertices = new HashSet<int>();
            foreach (var l in lines) { usedVertices.Add(l.start); usedVertices.Add(l.end); }

            // Include existing wall endpoints in a longer new boundary, so matching
            // segments can share one linedef rather than creating overlapping walls.
            var expanded = new List<MapPoint>();
            for (int i = 0; i < polygon.Count; i++)
            {
                var a = polygon[i]; var b = polygon[(i + 1) % polygon.Count];
                expanded.Add(a);
                var inserts = new List<MapPoint>();
                foreach (int vertex in usedVertices)
                {
                    var v = vertices[vertex];
                    if (OnEdge(a, b, v.Position, true) && (v.Position - a).LengthSquared >= 0.01f && (v.Position - b).LengthSquared >= 0.01f &&
                        !inserts.Exists(p => (p - v.Position).LengthSquared < 0.01f)) inserts.Add(v.Position);
                }
                inserts.Sort((p, q) => EdgeParameter(a, b, p).CompareTo(EdgeParameter(a, b, q)));
                expanded.AddRange(inserts);
            }
            polygon = expanded;
            for (int i = 0; i < polygon.Count; i++)
            {
                var a = polygon[i]; var b = polygon[(i + 1) % polygon.Count];
                foreach (var l in lines)
                {
                    var c = vertices[l.start].Position; var d = vertices[l.end].Position;
                    if (!SegmentsIntersect(a, b, c, d)) continue;
                    // Contact and collinear overlap are intentional. Cutting across
                    // another wall needs sector reconstruction, which is a later step.
                    if (!OnEdge(a, b, c) && !OnEdge(a, b, d) && !OnEdge(c, d, a) && !OnEdge(c, d, b))
                        throw new InvalidOperationException("Контур пересекает стену. Примыкание к стене поддерживается; пересечение ещё нет.");
                }
                var middle = new MapPoint((a.x + b.x) / 2, (a.y + b.y) / 2);
                for (int sector = 0; sector < sectors.Count; sector++)
                    if (InSectorInterior(sector, a) || InSectorInterior(sector, middle))
                        throw new InvalidOperationException("Контур занимает существующий сектор.");
            }
            foreach (int vertex in usedVertices)
            {
                var v = vertices[vertex];
                if (!OnBoundary(polygon, v.Position) && PointInPolygon(polygon, v.Position))
                    throw new InvalidOperationException("Комната охватывает существующую геометрию.");
            }

            var boundary = new List<int>();
            foreach (var p in polygon) boundary.Add(FindOrAddVertex(p));
            foreach (int vertex in boundary)
                for (int i = 0; i < lines.Count; i++)
                    if (vertex != lines[i].start && vertex != lines[i].end &&
                        OnEdge(vertices[lines[i].start].Position, vertices[lines[i].end].Position, vertices[vertex].Position, true)) SplitLineAt(i, vertex);

            int newSector = sectors.Count; sectors.Add(new MapSector());
            for (int i = 0; i < boundary.Count; i++)
            {
                int a = boundary[i], b = boundary[(i + 1) % boundary.Count];
                if (a == b) throw new InvalidOperationException("Слишком близкие точки контура.");
                MapLine shared = null; bool forward = false;
                foreach (var l in lines)
                    if ((l.start == a && l.end == b) || (l.start == b && l.end == a))
                    {
                        if (shared != null) throw new InvalidOperationException("В месте примыкания есть дублирующиеся стены.");
                        shared = l; forward = l.start == a;
                    }
                if (shared == null)
                {
                    int side = sides.Count; sides.Add(new MapSide { sector = newSector });
                    lines.Add(new MapLine { start = a, end = b, front = side });
                    continue;
                }
                if ((forward ? shared.front : shared.back) >= 0)
                    throw new InvalidOperationException("Эта сторона стены уже принадлежит сектору.");
                int other = forward ? shared.back : shared.front;
                int added = sides.Count;
                if (other < 0) sides.Add(new MapSide { sector = newSector });
                else
                {
                    // Preserve the former solid-wall texture for height differences.
                    // Both new sides have no opaque middle texture across the opening.
                    var previous = CopySide(sides[other]);
                    string texture = string.IsNullOrEmpty(previous.middle) || previous.middle == "-" ? "STARTAN3" : previous.middle;
                    if (string.IsNullOrEmpty(previous.upper) || previous.upper == "-") previous.upper = texture;
                    if (string.IsNullOrEmpty(previous.lower) || previous.lower == "-") previous.lower = texture;
                    previous.middle = "-";
                    int replaced = sides.Count; sides.Add(previous);
                    if (forward) shared.back = replaced; else shared.front = replaced;
                    added = sides.Count;
                    sides.Add(new MapSide { sector = newSector, middle = "-", upper = texture, lower = texture });
                }
                shared.RememberSideOrder();if (forward) shared.front = added; else shared.back = added;
                if (shared.front >= 0 && shared.back >= 0) shared.flags = (shared.flags | 4) & ~1;
                else if (shared.front < 0)
                {
                    int endpoint = shared.start; shared.start = shared.end; shared.end = endpoint;
                    shared.SwapSides(); shared.flags = (shared.flags & ~4) | 1;
                }
            }
            RemoveUnusedSides();
        }
        static MapSide CopySide(MapSide source)
        {
            var copy = new MapSide { sector = source.sector, offsetX = source.offsetX, offsetY = source.offsetY,
                upper = source.upper, lower = source.lower, middle = source.middle };
            source.CopyFieldsTo(copy); return copy;
        }
        void SplitLineAt(int index, int middle,DoomResources resources = null)
        {
            var line = lines[index];
            if (middle == line.start || middle == line.end) return;
            var a = vertices[line.start].Position; var b = vertices[line.end].Position; var p = vertices[middle].Position;
            double dx = p.x - (double)a.x,dy = p.y - (double)a.y;
            double firstLength = Math.Sqrt(dx * dx + dy * dy); dx = b.x - (double)p.x; dy = b.y - (double)p.y;
            double secondLength = Math.Sqrt(dx * dx + dy * dy);
            var part = line.Clone(); part.start = middle;
            // As in UDB Linedef.Split, sides must be independent. Offset continuity
            // follows each side's direction and local UDMF scales/resource panning.
            var oldFront = line.front < 0 ? null : CopySide(sides[line.front]);
            var newFront = oldFront == null ? null : CopySide(oldFront);
            var oldBack = line.back < 0 ? null : CopySide(sides[line.back]);
            var newBack = oldBack == null ? null : CopySide(oldBack);
            if (newFront != null) MapStitch.ShiftTexture(newFront,firstLength,format,resources);
            if (oldBack != null) MapStitch.ShiftTexture(oldBack,secondLength,format,resources);
            line.front = AppendSide(oldFront); part.front = AppendSide(newFront);
            line.back = AppendSide(oldBack); part.back = AppendSide(newBack);
            line.end = middle; lines.Add(part);
        }
        int AppendSide(MapSide side) { if (side == null) return -1; int index = sides.Count; sides.Add(side); return index; }
        void RemoveUnusedSides()
        {
            var used = new HashSet<int>(); var removed = new HashSet<int>();
            foreach (var l in lines) { if (l.front >= 0) used.Add(l.front); if (l.back >= 0) used.Add(l.back); }
            for (int i = 0; i < sides.Count; i++) if (!used.Contains(i)) removed.Add(i);
            var indices = Compact(sides, removed);
            foreach (var l in lines) { if (l.front >= 0) l.front = indices[l.front]; if (l.back >= 0) l.back = indices[l.back]; }
        }
    }
}
