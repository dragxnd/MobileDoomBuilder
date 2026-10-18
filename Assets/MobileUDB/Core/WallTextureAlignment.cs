// GPL-3.0. Connected-side traversal follows UDB BaseVisualMode texture alignment.
using System;
using System.Collections.Generic;
using System.Globalization;

namespace MobileUDB.Core
{
    public static class WallTextureAlignment
    {
        sealed class Node
        {
            public SurfaceRef surface;
            public bool front;
            public int start,end,group;
            public double length;
            public WallTextureMapping mapping;
            public double U(int vertex) { return mapping.StartU + (vertex == end ? length / mapping.width : 0); }
        }
        sealed class Job { public Node node,parent; public int vertex; }
        static string Suffix(SurfacePart part) { return part == SurfacePart.Upper ? "top" : part == SurfacePart.Lower ? "bottom" : "mid"; }
        static void Add(Dictionary<int,List<Node>> index,int key,Node node)
        { List<Node> list; if (!index.TryGetValue(key,out list)) index[key] = list = new List<Node>(); list.Add(node); }
        static MapSide Copy(MapSide side)
        {
            var copy = new MapSide { sector = side.sector,offsetX = side.offsetX,offsetY = side.offsetY,upper = side.upper,middle = side.middle,lower = side.lower };
            side.CopyFieldsTo(copy); return copy;
        }
        static bool SetField(MapSide side,string field,double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || Math.Abs(value) > float.MaxValue) throw new InvalidOperationException("Смещение текстуры выходит за допустимый диапазон.");
            float stored = (float)value;
            if (Math.Abs(VisualGeometry.Field(side,field,field.StartsWith("scale",StringComparison.Ordinal) ? 1 : 0) - stored) < .00001f) return false;
            UdmfText.SetRaw(side.fields,field,stored.ToString("R",CultureInfo.InvariantCulture)); return true;
        }
        static bool Offset(MapDocument map,MapSide side,string suffix,bool x,double value,double period,bool wrap)
        {
            if (wrap) value %= Math.Abs(period);
            if (map.format == MapFormat.Udmf) return SetField(side,(x ? "offsetx_" : "offsety_") + suffix,value - (x ? side.offsetX : side.offsetY));
            value = Math.Round(value,MidpointRounding.AwayFromZero);
            if (double.IsNaN(value) || double.IsInfinity(value) || value < short.MinValue || value > short.MaxValue) throw new InvalidOperationException("Смещение не помещается в Doom / Hexen.");
            int number = (int)value; bool changed = (x ? side.offsetX : side.offsetY) != number;
            if (x) side.offsetX = number; else side.offsetY = number; return changed;
        }
        static double PlaceClippedMiddle(MapDocument map,Node node,WallTextureMapping mapping,double total,double previous)
        {
            var side = map.sides[node.surface.side]; var line = map.lines[node.surface.line];
            var sector = map.sectors[side.sector]; int opposite = node.front ? line.back : line.front;
            var other = map.sectors[map.sides[opposite].sector];
            double low = double.MaxValue,high = double.MinValue;
            foreach (int vertex in new[] { node.start,node.end })
            {
                var point = map.vertices[vertex].Position;
                low = Math.Min(low,Math.Max(VisualGeometry.Height(sector,false,point),VisualGeometry.Height(other,false,point)));
                high = Math.Max(high,Math.Min(VisualGeometry.Height(sector,true,point),VisualGeometry.Height(other,true,point)));
            }
            double period = Math.Abs(mapping.height / mapping.unitY), step = period * mapping.unitY;
            double top = mapping.anchor + total * mapping.unitY;
            double a = (low + .001 - top) / step,b = (high + Math.Abs(mapping.height) - .001 - top) / step;
            double min = Math.Ceiling(Math.Min(a,b)),max = Math.Floor(Math.Max(a,b));
            if (min > max) throw new InvalidOperationException("Нет видимого положения для средней текстуры.");
            double repeats = Math.Max(min,Math.Min(max,Math.Round((previous - total) / period)));
            return total + repeats * period;
        }
        // The selected surface remains fixed. Commit only after every offset is valid.
        public static int Align(MapDocument map,SurfaceRef seed,DoomResources resources,bool x,bool y,IEnumerable<SurfaceRef> selection=null)
        {
            if (seed.IsFlat || (!x && !y)) throw new InvalidOperationException("Выберите текстуру стены и оси выравнивания.");
            var work = map.Clone(); work.Validate();
            var texture = resources == null ? null : resources.Find(VisualGeometry.Texture(work,seed),ResourceKind.Wall,work.format == MapFormat.Udmf);
            if (texture == null) throw new InvalidOperationException("Сначала подключите ресурс с выбранной текстурой.");
            HashSet<int> selectedLines=null;
            if(selection!=null)
            {
                selectedLines=new HashSet<int>();
                foreach(var id in selection){if(!SurfaceTextureEdits.Valid(work,id))throw new InvalidOperationException("Выделенная поверхность больше не существует.");if(!id.IsFlat)selectedLines.Add(id.line);}
                selectedLines.Add(seed.line);
            }
            var groups = new Dictionary<int,List<Node>>(); var candidates = new List<Node>();
            foreach (var surface in VisualGeometry.BuildWalls(work,resources))
            {
                if (selectedLines!=null&&!selectedLines.Contains(surface.id.line)) continue;
                if (!object.ReferenceEquals(texture,resources.Find(surface.texture,ResourceKind.Wall,work.format == MapFormat.Udmf))) continue;
                var line = work.lines[surface.id.line]; bool front = line.front == surface.id.side;
                var node = new Node { surface = surface.id,front = front,start = front ? line.start : line.end,end = front ? line.end : line.start,group = surface.id.line * 2 + (front ? 0 : 1) };
                node.length = Math.Sqrt((work.vertices[node.end].Position - work.vertices[node.start].Position).LengthSquared);
                candidates.Add(node); Add(groups,node.group,node);
            }
            Node root = candidates.Find(n => n.surface.Equals(seed));
            if (root == null) throw new InvalidOperationException("Выбранная часть стены не видна.");
            if (work.format != MapFormat.Udmf)
            {
                // Binary formats share one offset among all three parts of a side.
                candidates.Clear();
                foreach (var group in groups.Values) candidates.Add(group.Find(n => n.surface.Equals(seed)) ?? group.Find(n => n.surface.part == seed.part) ?? group[0]);
            }
            var starts = new Dictionary<int,List<Node>>(); var ends = new Dictionary<int,List<Node>>(); groups.Clear();
            foreach (var node in candidates) { Add(starts,node.start,node); Add(ends,node.end,node); Add(groups,node.group,node); }
            var uses = new Dictionary<int,int>();
            foreach (var line in work.lines) foreach (int side in new[] { line.front,line.back }) if (side >= 0) { int count; uses.TryGetValue(side,out count); uses[side] = count + 1; }
            string seedSuffix = Suffix(seed.part); var seedSide = work.sides[seed.side];
            float sx = VisualGeometry.Field(seedSide,"scalex_" + seedSuffix,1), sy = VisualGeometry.Field(seedSide,"scaley_" + seedSuffix,1);
            var stack = new Stack<Job>(); var visited = new HashSet<Node>(); stack.Push(new Job { node = root }); int changed = 0;
            while (stack.Count > 0)
            {
                var job = stack.Pop(); var node = job.node; if (!visited.Add(node)) continue;
                var line = work.lines[node.surface.line]; node.surface.side = node.front ? line.front : line.back;
                if (line.front >= 0 && line.front == line.back) throw new InvalidOperationException("Обе стороны стены используют один sidedef. Разделите стороны перед выравниванием.");
                if (job.parent != null)
                {
                    int id = node.surface.side; var original = work.sides[id]; var side = Copy(original); work.sides[id] = side;
                    string suffix = Suffix(node.surface.part); bool edited = false;
                    if (work.format == MapFormat.Udmf)
                    {
                        if (x) edited |= SetField(side,"scalex_" + suffix,sx);
                        if (y) edited |= SetField(side,"scaley_" + suffix,sy);
                    }
                    var mapping = VisualGeometry.WallMapping(work,node.surface,texture);
                    if (x)
                    {
                        double current = mapping.StartU + (job.vertex == node.end ? node.length / mapping.width : 0);
                        double total = mapping.offsetX / mapping.unitX + (job.parent.U(job.vertex) - current) * mapping.width / mapping.unitX;
                        edited |= Offset(work,side,suffix,true,total,mapping.width / mapping.unitX,true);
                    }
                    if (y)
                    {
                        double total = mapping.offsetY / mapping.unitY + (mapping.ZeroV - job.parent.mapping.ZeroV) * mapping.height / mapping.unitY;
                        if (node.surface.part == SurfacePart.Middle && !mapping.wrap && (job.parent.surface.part != SurfacePart.Middle || job.parent.mapping.wrap))
                            total = PlaceClippedMiddle(work,node,mapping,total,original.offsetY + VisualGeometry.Field(original,"offsety_" + suffix,0));
                        edited |= Offset(work,side,suffix,false,total,mapping.height / mapping.unitY,node.surface.part != SurfacePart.Middle || mapping.wrap);
                    }
                    if (!edited) work.sides[id] = original;
                    else
                    {
                        changed++;
                        if (uses[id] > 1)
                        {
                            // Compressed WADs may share a sidedef with disconnected walls.
                            work.sides[id] = original; node.surface.side = work.sides.Count; work.sides.Add(side); uses[id]--; uses[node.surface.side] = 1;
                            if (node.front) line.front = node.surface.side; else line.back = node.surface.side;
                        }
                    }
                }
                node.mapping = VisualGeometry.WallMapping(work,node.surface,texture);
                List<Node> neighbors;
                if (ends.TryGetValue(node.start,out neighbors)) foreach (var neighbor in neighbors)
                    if (neighbor.surface.line != node.surface.line && !visited.Contains(neighbor)) stack.Push(new Job { node = neighbor,parent = node,vertex = node.start });
                if (starts.TryGetValue(node.end,out neighbors)) foreach (var neighbor in neighbors)
                    if (neighbor.surface.line != node.surface.line && !visited.Contains(neighbor)) stack.Push(new Job { node = neighbor,parent = node,vertex = node.end });
                foreach (var sibling in groups[node.group]) if (!visited.Contains(sibling)) stack.Push(new Job { node = sibling,parent = node,vertex = node.start });
            }
            if (changed > 0)
            {
                if (work.format != MapFormat.Udmf && work.sides.Count > 65535) throw new InvalidOperationException("Превышен предел сторон Doom / Hexen.");
                work.Validate(); map.lines = work.lines; map.sides = work.sides;
            }
            return changed;
        }
    }
}
