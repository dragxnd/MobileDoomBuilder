// GPL-3.0. DrawGeometry-style chains: split/join walls and make bounded faces.
using System;
using System.Collections.Generic;

namespace MobileUDB.Core
{
    public sealed partial class MapDocument
    {
        public MapStitchResult DrawLines(IList<MapPoint> points,bool close = false,DoomResources resources = null,DrawTextureOptions textures = null)
        {
            var result = MapStitch.DrawLines(this,points,close,resources,textures);
            ApplyTopology(result.map); return result;
        }
    }
    public static partial class MapStitch
    {
        public static MapStitchResult DrawLines(MapDocument target,IList<MapPoint> points,bool close = false,DoomResources resources = null,DrawTextureOptions textures = null)
        {
            textures = textures == null ? null : textures.Validated(target.format);
            if (points == null || points.Count < 2) throw new InvalidOperationException("Укажите минимум две точки для стены.");
            var addition = new MapDocument { format = target.format,udmfNamespace = target.udmfNamespace };
            // A virtual side supplies default wall artwork; it has no region ownership.
            addition.sectors.Add(new MapSector()); addition.sides.Add(new MapSide { sector = 0,middle = "STARTAN3" });
            foreach (var point in points)
            {
                if (float.IsNaN(point.x) || float.IsNaN(point.y) || float.IsInfinity(point.x) || float.IsInfinity(point.y))
                    throw new InvalidOperationException("Некорректная точка рисования.");
                if (target.format != MapFormat.Udmf && (point.x < -32768 || point.x > 32767 || point.y < -32768 || point.y > 32767))
                    throw new InvalidOperationException("Координаты за пределами бинарного формата карты.");
                var p = target.format == MapFormat.Udmf ? point : MapDocument.Snap(point,1);
                if (addition.vertices.Count > 0 && ((target.format == MapFormat.Udmf ? addition.vertices[addition.vertices.Count - 1].Position : MapDocument.Snap(addition.vertices[addition.vertices.Count - 1].Position,1)) - p).LengthSquared < .000001f)
                    throw new InvalidOperationException("Соседние точки рисования совпадают.");
                // Quantizing a contact before Overlay would disconnect it from
                // an existing diagonal. Intersections use the original point.
                addition.vertices.Add(new MapVertex(point.x,point.y));
                if (addition.vertices.Count > 1) addition.lines.Add(new MapLine { start = addition.vertices.Count - 2,end = addition.vertices.Count - 1,front = 0 });
            }
            if (close)
            {
                if (points.Count < 3) throw new InvalidOperationException("Для замыкания нужно минимум три точки.");
                if ((addition.vertices[0].Position - addition.vertices[addition.vertices.Count - 1].Position).LengthSquared >= .000001f)
                    addition.lines.Add(new MapLine { start = addition.vertices.Count - 1,end = 0,front = 0 });
            }
            var result = Overlay(target,addition,GeometryMergeMode.Merge,resources,new HashSet<int> { 0 });
            CreateDrawnVoidFaces(result);
            if (textures != null) textures.Apply(result);
            if (textures != null && textures.alignX) DrawTextureAlignment.Apply(result,resources);
            result.map.Validate(); foreach (int sector in result.sectors) MapTransforms.ValidateSector(result.map,sector);
            if (target.format != MapFormat.Udmf && (result.map.sides.Count > 65535 || result.map.sectors.Count > 65535))
                throw new InvalidOperationException("Превышен лимит элементов бинарной карты.");
            return result;
        }
        static List<Face> DrawingFaces(MapDocument map)
        {
            var outgoing = new Dictionary<int,List<int>>(); int count = map.lines.Count * 2;
            for (int edge = 0; edge < count; edge++)
            {
                int start = Start(map,edge); List<int> list;
                if (!outgoing.TryGetValue(start,out list)) { list = new List<int>(); outgoing.Add(start,list); }
                list.Add(edge);
            }
            int[] next = new int[count];
            foreach (var pair in outgoing)
            {
                var point = map.vertices[pair.Key].Position; var list = pair.Value;
                list.Sort((one,two) =>
                {
                    var a = map.vertices[End(map,one)].Position; var b = map.vertices[End(map,two)].Position;
                    int order = Math.Atan2(a.y - (double)point.y,a.x - (double)point.x).CompareTo(Math.Atan2(b.y - (double)point.y,b.x - (double)point.x));
                    return order == 0 ? one.CompareTo(two) : order;
                });
                for (int i = 0; i < list.Count; i++) next[list[i] ^ 1] = list[(i + 1) % list.Count];
            }
            var faces = new List<Face>(); bool[] visited = new bool[count];
            for (int first = 0; first < count; first++)
            {
                if (visited[first]) continue; var face = new Face { owner = -1 }; int current = first;
                do
                {
                    if (visited[current]) throw new InvalidOperationException("Не удалось восстановить область рисования.");
                    visited[current] = true; face.edges.Add(current);
                    var a = map.vertices[Start(map,current)].Position; var b = map.vertices[End(map,current)].Position;
                    face.points.Add(a); face.area += a.x * (double)b.y - b.x * (double)a.y;
                    current = next[current];
                } while (current != first);
                faces.Add(face);
            }
            return faces;
        }
        static void CreateDrawnVoidFaces(MapStitchResult result)
        {
            var map = result.map; var faces = DrawingFaces(map); var created = new List<Face>();
            var changed = new HashSet<int>();
            foreach (var face in faces)
            {
                if (face.area >= -Epsilon * Epsilon || !face.edges.Exists(edge => result.lines.Contains(edge / 2))) continue;
                // Existing sector boundaries already own their interior. Only fill
                // bounded void faces touched by the user's drawing.
                if (face.edges.Exists(edge => SideIndex(map,edge) >= 0)) continue;
                MapSector prototype = null;
                foreach (int edge in face.edges)
                {
                    int other = SideIndex(map,edge ^ 1);
                    if (other >= 0) { prototype = map.sectors[map.sides[other].sector]; break; }
                }
                var sector = new MapSector();
                if (prototype != null)
                {
                    sector.floor = prototype.floor; sector.ceiling = prototype.ceiling; sector.light = prototype.light;
                    sector.special = prototype.special; sector.tag = prototype.tag; sector.floorTexture = prototype.floorTexture; sector.ceilingTexture = prototype.ceilingTexture;
                    prototype.CopyFieldsTo(sector);
                }
                face.owner = map.sectors.Count; map.sectors.Add(sector); result.sectors.Add(face.owner); created.Add(face);
            }
            if (created.Count == 0) return;
            foreach (var face in faces)
            {
                int owner = face.owner;
                if (owner < 0)
                {
                    double x,y; Sample(map,face.edges[0],out x,out y); var parent = Containing(created,x,y);
                    if (parent != null) owner = parent.owner;
                }
                if (owner < 0) continue;
                foreach (int edge in face.edges)
                {
                    if (SideIndex(map,edge) >= 0) continue;
                    var line = map.lines[edge / 2]; int other = SideIndex(map,edge ^ 1);
                    var side = new MapSide { middle = "STARTAN3" };
                    if (other >= 0)
                    {
                        var source = map.sides[other]; side.offsetX = source.offsetX; side.offsetY = source.offsetY;
                        side.upper = source.upper; side.middle = source.middle; side.lower = source.lower; source.CopyFieldsTo(side);
                    }
                    side.sector = owner; int index = map.sides.Count; map.sides.Add(side);
                    result.createdSides.Add(index);
                    line.RememberSideOrder();if ((edge & 1) == 0) line.front = index; else line.back = index;
                    changed.Add(edge / 2);
                }
            }
            foreach (int changedLine in changed)
            {
                var line = map.lines[changedLine];
                if (line.front < 0 && line.back >= 0) { int swap = line.start; line.start = line.end; line.end = swap; line.SwapSides(); }
                if (line.front < 0) continue;
                bool both = line.back >= 0;
                line.flags = both ? (line.flags | 4) & ~1 : (line.flags & ~4) | 1;
                if (map.format == MapFormat.Udmf) { UdmfText.SetBool(line.fields,"twosided",both); UdmfText.SetBool(line.fields,"blocking",!both); }
                if (both) { OpenSide(map.sides[line.front]); OpenSide(map.sides[line.back]); }
                else CloseSide(map.sides[line.front]);
            }
        }
    }
}
