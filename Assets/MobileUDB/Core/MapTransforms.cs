// GPL-3.0. Selection transforms, with UDMF plane and flat texture anchors.
using System;
using System.Collections.Generic;
using System.Globalization;

namespace MobileUDB.Core
{
    public enum SelectionTransform { RotateLeft, RotateRight, MirrorX, MirrorY }
    public static class MapTransforms
    {
        public static HashSet<ElementRef> All(MapDocument map)
        {
            var selection = new HashSet<ElementRef>();
            for (int i = 0; i < map.vertices.Count; i++) selection.Add(new ElementRef(ElementKind.Vertex,i));
            for (int i = 0; i < map.sectors.Count; i++) selection.Add(new ElementRef(ElementKind.Sector,i));
            for (int i = 0; i < map.things.Count; i++) selection.Add(new ElementRef(ElementKind.Thing,i));
            return selection;
        }
        public static MapPoint Center(MapDocument map, HashSet<ElementRef> selection)
        {
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (int vertex in map.SelectedVertices(selection)) Bounds(map.vertices[vertex].Position,ref minX,ref minY,ref maxX,ref maxY);
            foreach (var item in selection) if (item.kind == ElementKind.Thing) Bounds(map.things[item.index].Position,ref minX,ref minY,ref maxX,ref maxY);
            if (minX == float.MaxValue) throw new InvalidOperationException("Нет выделения для преобразования.");
            var center = new MapPoint((minX + maxX) * .5f,(minY + maxY) * .5f);
            return map.format == MapFormat.Udmf ? center : MapDocument.Snap(center,1);
        }
        static void Bounds(MapPoint point, ref float minX, ref float minY, ref float maxX, ref float maxY)
        { minX = Math.Min(minX,point.x); minY = Math.Min(minY,point.y); maxX = Math.Max(maxX,point.x); maxY = Math.Max(maxY,point.y); }
        public static void Translate(MapDocument map, MapPoint delta)
        { Apply(map,All(map),1,0,0,1,delta,false,null); }
        public static HashSet<ElementRef> WithSectorThings(MapDocument map,HashSet<ElementRef> selection,bool includeSectorThings)
        {
            var selected = new HashSet<ElementRef>(selection);
            if (includeSectorThings)
                foreach (var item in selection) if (item.kind == ElementKind.Sector)
                    for (int i = 0; i < map.things.Count; i++) if (map.Contains(item.index,map.things[i].Position)) selected.Add(new ElementRef(ElementKind.Thing,i));
            return selected;
        }
        public static HashSet<ElementRef> Move(MapDocument map,HashSet<ElementRef> selection,MapPoint delta,bool includeSectorThings = true,GeometryMergeMode mode = GeometryMergeMode.Replace,DoomResources resources = null)
        {
            if (selection.Count == 0) throw new InvalidOperationException("Нет выделения для перемещения.");
            if (float.IsNaN(delta.x) || float.IsNaN(delta.y) || float.IsInfinity(delta.x) || float.IsInfinity(delta.y)) throw new InvalidOperationException("Некорректное перемещение.");
            if (map.format != MapFormat.Udmf) delta = MapDocument.Snap(delta,1);
            if (delta.x == 0 && delta.y == 0) return new HashSet<ElementRef>(selection);
            return Apply(map,WithSectorThings(map,selection,includeSectorThings),1,0,0,1,delta,true,null,mode,resources);
        }
        public static HashSet<ElementRef> Transform(MapDocument map, HashSet<ElementRef> selection, SelectionTransform transform, bool includeSectorThings = true, Func<int,bool> rotateThing = null,GeometryMergeMode mode = GeometryMergeMode.Replace,DoomResources resources = null)
        {
            if (!Enum.IsDefined(typeof(SelectionTransform),transform)) throw new InvalidOperationException("Неизвестное преобразование.");
            var selected = WithSectorThings(map,selection,includeSectorThings);
            var center = Center(map,selected); float a = 1,b = 0,c = 0,d = 1;
            if (transform == SelectionTransform.RotateLeft) { a = d = 0; b = -1; c = 1; }
            if (transform == SelectionTransform.RotateRight) { a = d = 0; b = 1; c = -1; }
            if (transform == SelectionTransform.MirrorX) a = -1;
            if (transform == SelectionTransform.MirrorY) d = -1;
            return Apply(map,selected,a,b,c,d,new MapPoint(center.x - a * center.x - b * center.y,center.y - c * center.x - d * center.y),true,rotateThing,mode,resources);
        }
        public static HashSet<ElementRef> Transform(MapDocument map,HashSet<ElementRef> selection,float degrees,float scale,bool includeSectorThings = true,Func<int,bool> rotateThing = null,GeometryMergeMode mode = GeometryMergeMode.Replace,DoomResources resources = null)
        {
            if (float.IsNaN(degrees) || float.IsInfinity(degrees) || float.IsNaN(scale) || float.IsInfinity(scale) || scale < .000001f)
                throw new InvalidOperationException("Введите конечный угол и положительный масштаб.");
            var selected = WithSectorThings(map,selection,includeSectorThings); var center = Center(map,selected);
            degrees %= 360; if (degrees == 0 && scale == 1) return new HashSet<ElementRef>(selection);
            double radians = degrees * Math.PI / 180; float a = (float)Math.Cos(radians) * scale,c = (float)Math.Sin(radians) * scale,b = -c,d = a;
            return Apply(map,selected,a,b,c,d,new MapPoint(center.x - a * center.x - b * center.y,center.y - c * center.x - d * center.y),true,rotateThing,mode,resources);
        }
        static MapPoint Point(MapPoint p, float a,float b,float c,float d,MapPoint offset)
        { return new MapPoint(a * p.x + b * p.y + offset.x,c * p.x + d * p.y + offset.y); }
        static void Coordinate(MapPoint p, MapFormat format)
        {
            if (float.IsNaN(p.x) || float.IsInfinity(p.x) || float.IsNaN(p.y) || float.IsInfinity(p.y) ||
                (format != MapFormat.Udmf && (p.x < -32768 || p.x > 32767 || p.y < -32768 || p.y > 32767)))
                throw new InvalidOperationException("Координаты за пределами формата карты.");
        }
        public static HashSet<ElementRef> Transform(MapDocument map,HashSet<ElementRef> selection,float degrees,float scaleX,float scaleY,bool includeSectorThings = true,Func<int,bool> rotateThing = null,GeometryMergeMode mode = GeometryMergeMode.Replace,DoomResources resources = null)
        {
            if (float.IsNaN(degrees) || float.IsInfinity(degrees) || float.IsNaN(scaleX) || float.IsInfinity(scaleX) || float.IsNaN(scaleY) || float.IsInfinity(scaleY) || scaleX < .000001f || scaleY < .000001f)
                throw new InvalidOperationException("Введите конечный угол и положительные масштабы X и Y.");
            if (scaleX == scaleY) return Transform(map,selection,degrees,scaleX,includeSectorThings,rotateThing,mode,resources);
            var selected = WithSectorThings(map,selection,includeSectorThings); var center = Center(map,selected);
            degrees %= 360; double radians = degrees * Math.PI / 180;
            float cos = (float)Math.Cos(radians),sin = (float)Math.Sin(radians);
            float a = cos * scaleX,b = -sin * scaleY,c = sin * scaleX,d = cos * scaleY;
            return Apply(map,selected,a,b,c,d,new MapPoint(center.x - a * center.x - b * center.y,center.y - c * center.x - d * center.y),true,rotateThing,mode,resources,degrees,center);
        }
        public static HashSet<ElementRef> MoveDragged(MapDocument map,HashSet<ElementRef> selection,MapPoint delta,bool stitch,GeometryMergeMode mode=GeometryMergeMode.Replace,DoomResources resources=null)
        {
            ThingClipboard.Binary(map);
            if(delta.x==0&&delta.y==0)return new HashSet<ElementRef>(selection);
            return Apply(map,selection,1,0,0,1,delta,stitch,null,mode,resources,rawUntilStitch:stitch);
        }
        static HashSet<ElementRef> Apply(MapDocument map, HashSet<ElementRef> selection, float a,float b,float c,float d,MapPoint offset,bool stitch,Func<int,bool> rotateThing,GeometryMergeMode mode = GeometryMergeMode.Replace,DoomResources resources = null,float? nonUniformRotation = null,MapPoint textureAnchor = default(MapPoint),bool rawUntilStitch=false)
        {
            var vertices = map.SelectedVertices(selection); var lines = new HashSet<int>(); var sectors = new HashSet<int>();
            bool unstable = false;
            foreach (var line in map.lines)
                if (vertices.Contains(line.start) != vertices.Contains(line.end))
                    unstable = true;
            for (int i = 0; i < map.lines.Count; i++) if (vertices.Contains(map.lines[i].start) && vertices.Contains(map.lines[i].end))
            {
                lines.Add(i); var line = map.lines[i];
                if (line.front >= 0) sectors.Add(map.sides[line.front].sector); if (line.back >= 0) sectors.Add(map.sides[line.back].sector);
            }
            var incomplete = new HashSet<int>();
            foreach (int sector in sectors)
                foreach (var line in map.lines)
                    if ((line.front >= 0 && map.sides[line.front].sector == sector) || (line.back >= 0 && map.sides[line.back].sector == sector))
                        if (!vertices.Contains(line.start) || !vertices.Contains(line.end)) incomplete.Add(sector);
            sectors.ExceptWith(incomplete);
            // All rejection checks happen on a snapshot before touching the caller's map.
            var result = map.Clone();
            foreach (int vertex in vertices) { var p = Point(map.vertices[vertex].Position,a,b,c,d,offset); Coordinate(p,map.format); if (map.format != MapFormat.Udmf&&!rawUntilStitch) p = MapDocument.Snap(p,1); result.vertices[vertex].Position = p; }
            foreach (var item in selection) if (item.kind == ElementKind.Thing)
            {
                var thing = result.things[item.index]; thing.Position = Point(thing.Position,a,b,c,d,offset); Coordinate(thing.Position,map.format);
                if (map.format != MapFormat.Udmf) thing.Position = MapDocument.Snap(thing.Position,1);
                if ((a != 1 || b != 0 || c != 0 || d != 1) && (!nonUniformRotation.HasValue || nonUniformRotation.Value != 0) && (rotateThing == null || rotateThing(thing.type)))
                {
                    double angle = thing.angle * Math.PI / 180;
                    float next = nonUniformRotation.HasValue ? ((thing.angle + nonUniformRotation.Value) % 360 + 360) % 360 : (float)((Math.Atan2(c * Math.Cos(angle) + d * Math.Sin(angle),a * Math.Cos(angle) + b * Math.Sin(angle)) * 180 / Math.PI + 360) % 360);
                    if (map.format != MapFormat.Udmf) next = MapDocument.Snap(new MapPoint(next,0),1).x % 360;
                    if (Math.Abs(next - thing.angle) > .0001f) thing.angle = next;
                }
            }
            bool mirror = a * d - b * c < 0;
            if (mirror) foreach (int i in lines)
            {
                var line = result.lines[i]; int start = line.start; line.start = line.end; line.end = start;
                // UDB EditSelectionMode flips sides as well when selected geometry
                // remains attached to stationary endpoints; stitching fixes owners.
                if (unstable) line.SwapSides();
            }
            float fa = a,fb = b,fc = c,fd = d; var flatOffset = offset;
            if (nonUniformRotation.HasValue)
            {
                // UDMF cannot represent a general sheared UV basis. Like UDB's
                // pinned flat textures, preserve density and rotate around the
                // selection reference point, rather than inventing shear fields.
                double radians = nonUniformRotation.Value * Math.PI / 180;
                fa = fd = (float)Math.Cos(radians); fc = (float)Math.Sin(radians); fb = -fc;
                flatOffset = new MapPoint(textureAnchor.x - fa * textureAnchor.x - fb * textureAnchor.y,textureAnchor.y - fc * textureAnchor.x - fd * textureAnchor.y);
            }
            if (map.format == MapFormat.Udmf) foreach (int sector in sectors)
            {
                TransformPlane(result.sectors[sector],"floorplane_",a,b,c,d,offset);
                TransformPlane(result.sectors[sector],"ceilingplane_",a,b,c,d,offset);
                TransformFlat(result.sectors[sector],"floor",fa,fb,fc,fd,flatOffset);
                TransformFlat(result.sectors[sector],"ceiling",fa,fb,fc,fd,flatOffset);
            }
            // A small binary scale can collapse a complete room when rounding to
            // format accuracy. Reject it before stitching can remove that sector.
            foreach (int sector in sectors)
            {
                bool closed = true; try { ClosedSector(map,sector); } catch (InvalidOperationException) { closed = false; }
                if (closed) ClosedSector(result,sector);
            }
            var transformedSelection = stitch && vertices.Count > 0 ? MapStitch.StitchMoved(map,result,vertices,sectors,selection,mode,resources,rawUntilStitch) : new HashSet<ElementRef>(selection);
            // Native drag stitches fractional line attachments first, then snaps
            // to binary accuracy. Rounding before stitching can miss a diagonal.
            if(rawUntilStitch)foreach(var vertex in result.vertices)vertex.Position=MapDocument.Snap(vertex.Position,1);
            result.Validate(); map.vertices = result.vertices; map.lines = result.lines; map.sides = result.sides; map.sectors = result.sectors; map.things = result.things;
            return transformedSelection;
        }
        static void TransformPlane(MapSector sector,string prefix,float a,float b,float c,float d,MapPoint offset)
        {
            if (Math.Abs(VisualGeometry.Field(sector,prefix + "c",0)) < .000001f) return;
            float x = VisualGeometry.Field(sector,prefix + "a",0), y = VisualGeometry.Field(sector,prefix + "b",0);
            float determinant = a * d - b * c;
            float nx = (d * x - c * y) / determinant, ny = (-b * x + a * y) / determinant;
            Set(sector,prefix + "a",nx); Set(sector,prefix + "b",ny);
            Set(sector,prefix + "d",VisualGeometry.Field(sector,prefix + "d",0) - nx * offset.x - ny * offset.y);
        }
        static void TransformFlat(MapSector sector,string suffix,float a,float b,float c,float d,MapPoint offset)
        {
            double angle = VisualGeometry.Field(sector,"rotation" + suffix,0) * Math.PI / 180;
            double scale = Math.Sqrt(a * (double)a + c * (double)c);
            double x = Math.Cos(angle), y = -Math.Sin(angle), ux = (a * x + b * y) / scale, uy = (c * x + d * y) / scale;
            double vx = -uy, vy = ux; float sign = a * d - b * c < 0 ? -1 : 1;
            Set(sector,"rotation" + suffix,(float)((Math.Atan2(-uy,ux) * 180 / Math.PI + 360) % 360));
            Set(sector,"xpanning" + suffix,(float)(scale * VisualGeometry.Field(sector,"xpanning" + suffix,0) - ux * offset.x - uy * offset.y));
            Set(sector,"ypanning" + suffix,(float)(sign * scale * VisualGeometry.Field(sector,"ypanning" + suffix,0) + vx * offset.x + vy * offset.y));
            if (Math.Abs(scale - 1) > .000001) Set(sector,"xscale" + suffix,(float)(VisualGeometry.Field(sector,"xscale" + suffix,1) / scale));
            if (sign < 0 || Math.Abs(scale - 1) > .000001) Set(sector,"yscale" + suffix,(float)(sign * VisualGeometry.Field(sector,"yscale" + suffix,1) / scale));
        }
        static void Set(MapSector sector,string key,float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) throw new InvalidOperationException("Параметры плоскости выходят за допустимый диапазон.");
            UdmfText.SetRaw(sector.fields,key,value.ToString("R",CultureInfo.InvariantCulture));
        }
        public static void ClosedSectors(MapDocument map) { for (int i = 0; i < map.sectors.Count; i++) ClosedSector(map,i); }
        public static void ValidateSector(MapDocument map,int sector) { ClosedSector(map,sector); }
        static void ClosedSector(MapDocument map,int sector)
        {
            var degree = new Dictionary<int,int>();
            foreach (var line in map.lines)
            {
                bool front = line.front >= 0 && map.sides[line.front].sector == sector, back = line.back >= 0 && map.sides[line.back].sector == sector;
                if (front == back) continue;
                foreach (int v in new[] { line.start,line.end }) { int count; degree.TryGetValue(v,out count); degree[v] = count + 1; }
            }
            if (degree.Count == 0) throw new InvalidOperationException("У сектора нет замкнутой границы.");
            foreach (var pair in degree) if ((pair.Value & 1) != 0) throw new InvalidOperationException("Сектор не замкнут; сначала исправьте его границы.");
            if (new SectorTessellation(map).Triangles(sector).Count == 0) throw new InvalidOperationException("Пустой контур сектора.");
        }
        public static void CheckFreePlacement(MapDocument target,MapDocument copy)
        {
            if (copy.lines.Count == 0 && copy.sectors.Count == 0) return; // Things and standalone vertices may be placed in rooms.
            if (target.vertices.Count == 0) return;
            float ax = float.MaxValue,ay = float.MaxValue,bx = float.MinValue,by = float.MinValue;
            float cx = float.MaxValue,cy = float.MaxValue,dx = float.MinValue,dy = float.MinValue;
            foreach (var vertex in target.vertices) Bounds(vertex.Position,ref ax,ref ay,ref bx,ref by);
            foreach (var vertex in copy.vertices) Bounds(vertex.Position,ref cx,ref cy,ref dx,ref dy);
            if (dx < ax || cx > bx || dy < ay || cy > by) return;
            foreach (var line in copy.lines) foreach (var other in target.lines)
                if (MapDocument.SegmentsIntersect(copy.vertices[line.start].Position,copy.vertices[line.end].Position,target.vertices[other.start].Position,target.vertices[other.end].Position))
                    throw new InvalidOperationException("Контур касается или пересекает стены. Разместите копию в свободном месте; сшивка вставки пока недоступна.");
            foreach (var vertex in copy.vertices) for (int i = 0; i < target.sectors.Count; i++)
                if (target.Contains(i,vertex.Position)) throw new InvalidOperationException("Контур попадает внутрь существующего сектора. Разместите копию снаружи карты.");
            foreach (var vertex in target.vertices) for (int i = 0; i < copy.sectors.Count; i++)
                if (copy.Contains(i,vertex.Position)) throw new InvalidOperationException("Контур охватывает существующую геометрию. Выберите свободное место.");
        }
    }
}
