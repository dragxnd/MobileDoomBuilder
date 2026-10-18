// Mobile port of Ultimate Doom Builder. GPL-3.0; see repository LICENSE.txt.
using System;
using System.Collections.Generic;

namespace MobileUDB.Core
{
    public enum MapFormat { Doom, Hexen, Udmf }
    [Serializable] public sealed class UniversalField
    {
        public string name, value;
        public UniversalField(string name, string value) { this.name = name; this.value = value; }
    }
    [Serializable] public class MapElementData
    {
        // Raw UDMF scalar values retain strings, booleans, precision and extension fields.
        public List<UniversalField> fields = new List<UniversalField>();
        public List<string> extraBlocks = new List<string>();
        public void CopyFieldsTo(MapElementData target)
        {
            var selectable=this as SelectableMapElementData;var selectedTarget=target as SelectableMapElementData;
            if(selectable!=null&&selectedTarget!=null)selectedTarget.selectionGroups=selectable.selectionGroups;
            if (fields != null) foreach (var f in fields) target.fields.Add(new UniversalField(f.name, f.value));
            if (extraBlocks != null) target.extraBlocks.AddRange(extraBlocks);
        }
    }
    [Serializable] public sealed class MapAuxiliaryLump
    {
        public string name;
        public byte[] data;
        public MapAuxiliaryLump(string name, byte[] data) { this.name = name; this.data = data; }
    }
    [Serializable] public struct MapPoint
    {
        public float x, y;
        public MapPoint(float x, float y) { this.x = x; this.y = y; }
        public static MapPoint operator +(MapPoint a, MapPoint b) { return new MapPoint(a.x + b.x, a.y + b.y); }
        public static MapPoint operator -(MapPoint a, MapPoint b) { return new MapPoint(a.x - b.x, a.y - b.y); }
        public float LengthSquared { get { return x * x + y * y; } }
    }
    [Serializable] public class SelectableMapElementData : MapElementData
    {
        // Editor metadata only: ten UDB selection-group bits, saved in mobile projects.
        public int selectionGroups;
    }
    [Serializable] public sealed class MapVertex : SelectableMapElementData
    {
        public float x, y;
        public MapPoint Position { get { return new MapPoint(x, y); } set { x = value.x; y = value.y; } }
        public MapVertex(float x, float y) { this.x = x; this.y = y; }
    }
    [Serializable] public sealed class MapLine : SelectableMapElementData
    {
        public int start, end, flags = 1, action, tag, front = -1, back = -1;
        // Project-only incidence order: 0 = binary-import order, 1 = front first,
        // 2 = back first. WAD records do not store this editor history.
        public int firstSideOrder;
        public int[] args = new int[5];
        public int SideOrder {get{return front<0?(back<0?0:2):back<0?1:firstSideOrder==2?2:1;}}
        public void RememberSideOrder(){firstSideOrder=SideOrder;}
        public void SwapSides()
        {
            int first=SideOrder;
            int swap=front;front=back;back=swap;firstSideOrder=first==0?0:3-first;
        }
        public MapLine Clone()
        {
            var copy = new MapLine { start = start, end = end, flags = flags, action = action, tag = tag, front = front, back = back, firstSideOrder=firstSideOrder, args = (int[])args.Clone() };
            CopyFieldsTo(copy); return copy;
        }
    }
    [Serializable] public sealed class MapSide : MapElementData
    {
        public int sector, offsetX, offsetY;
        public string upper = "-", lower = "-", middle = "STARTAN3";
    }
    [Serializable] public sealed class MapSector : SelectableMapElementData
    {
        public float floor, ceiling = 128;
        public int light = 192, special, tag;
        public string floorTexture = "FLOOR0_1", ceilingTexture = "CEIL1_1";
    }
    [Serializable] public sealed class MapThing : SelectableMapElementData
    {
        public float x, y, z, angle;
        public int type = 1, flags = 7, tag, action;
        public int[] args = new int[5];
        public bool udmfInitialized;
        public MapPoint Position { get { return new MapPoint(x, y); } set { x = value.x; y = value.y; } }
    }
    public enum ElementKind { Vertex, Line, Sector, Thing }
    public struct ElementRef : IEquatable<ElementRef>
    {
        public ElementKind kind;
        public int index;
        public ElementRef(ElementKind kind, int index) { this.kind = kind; this.index = index; }
        public bool Equals(ElementRef other) { return kind == other.kind && index == other.index; }
        public override bool Equals(object obj) { return obj is ElementRef && Equals((ElementRef)obj); }
        public override int GetHashCode() { return ((int)kind * 397) ^ index; }
    }
    [Serializable] public sealed partial class MapDocument
    {
        public int version = 1;
        public string name = "MAP01";
        // Project-only association with the original archive marker / PK3 filename.
        // Remains stable through repeated renames; never written into binary records.
        public string sourceName;
        [NonSerialized] public GameSky sky;
        public MapFormat format;
        public string udmfNamespace = "zdoom";
        public List<UniversalField> udmfFields = new List<UniversalField>();
        public List<string> udmfBlocks = new List<string>();
        public List<MapAuxiliaryLump> auxiliary = new List<MapAuxiliaryLump>();
        public List<MapVertex> vertices = new List<MapVertex>();
        public List<MapLine> lines = new List<MapLine>();
        public List<MapSide> sides = new List<MapSide>();
        public List<MapSector> sectors = new List<MapSector>();
        public List<MapThing> things = new List<MapThing>();
        // Map-local editor names; binary WAD records retain their numeric tags.
        public List<MapTagLabel> tagLabels = new List<MapTagLabel>();

        public static MapDocument Demo()
        {
            var map = new MapDocument();
            map.AddRoom(new List<MapPoint> { new MapPoint(-256, -256), new MapPoint(-256, 256), new MapPoint(256, 256), new MapPoint(256, -256) });
            map.things.Add(new MapThing());
            return map;
        }
        public void Validate()
        {
            MapFormats.RequireSupported(format);
            if (version != 1) throw new InvalidOperationException("Unsupported mobile project version.");
            if (!Enum.IsDefined(typeof(MapFormat), format)) throw new InvalidOperationException("Unsupported map format.");
            MapNaming.RequireName(name);if(!string.IsNullOrEmpty(sourceName))MapNaming.RequireName(sourceName);
            MapTagLabels.Validate(tagLabels);
            if (udmfFields == null) udmfFields = new List<UniversalField>();
            if (udmfBlocks == null) udmfBlocks = new List<string>();
            if (auxiliary == null) auxiliary = new List<MapAuxiliaryLump>();
            if (vertices == null || lines == null || sides == null || sectors == null || things == null)
                throw new InvalidOperationException("Incomplete map document.");
            foreach (var v in vertices) if (v == null || !Finite(v.x) || !Finite(v.y)) throw new InvalidOperationException("Invalid vertex.");
            foreach (var t in things) if (t == null || !Finite(t.x) || !Finite(t.y) || !Finite(t.z) || !Finite(t.angle)) throw new InvalidOperationException("Invalid thing.");
            foreach (var l in lines)
                // Zero-length geometry remains editable diagnostic data, including
                // the same endpoint index after a native degree-two deletion.
                if (l == null || l.start < 0 || l.start >= vertices.Count || l.end < 0 || l.end >= vertices.Count ||
                    l.front < -1 || l.front >= sides.Count || l.back < -1 || l.back >= sides.Count || l.firstSideOrder<0 || l.firstSideOrder>2)
                    throw new InvalidOperationException("Invalid linedef references.");
            foreach (var s in sides) if (s == null || s.sector < 0 || s.sector >= sectors.Count) throw new InvalidOperationException("Invalid sidedef sector.");
            foreach (var s in sectors) if (s == null || !Finite(s.floor) || !Finite(s.ceiling)) throw new InvalidOperationException("Invalid sector.");
            foreach (var v in vertices) Normalize(v);
            foreach (var s in sides) Normalize(s);
            foreach (var s in sectors) Normalize(s);
            foreach (var l in lines) { Normalize(l); if (l.args == null) l.args = new int[5]; if (l.args.Length != 5) throw new InvalidOperationException("Linedef requires five arguments."); }
            foreach (var t in things) { Normalize(t); if (t.args == null) t.args = new int[5]; if (t.args.Length != 5) throw new InvalidOperationException("Thing requires five arguments."); }
        }
        static void Normalize(MapElementData element)
        {
            var selectable=element as SelectableMapElementData;
            if(selectable!=null&&(selectable.selectionGroups<0||selectable.selectionGroups>1023))throw new InvalidOperationException("Некорректные группы выделения в проекте.");
            if (element.fields == null) element.fields = new List<UniversalField>();
            if (element.extraBlocks == null) element.extraBlocks = new List<string>();
        }
        static bool Finite(float n) { return !float.IsNaN(n) && !float.IsInfinity(n); }
        public MapDocument Clone()
        {
            var copy = new MapDocument { version = version, name = name,sourceName=sourceName, format = format, udmfNamespace = udmfNamespace,sky=sky };
            copy.tagLabels=MapTagLabels.Copy(tagLabels);
            foreach (var f in udmfFields) copy.udmfFields.Add(new UniversalField(f.name, f.value));
            copy.udmfBlocks.AddRange(udmfBlocks);
            // Auxiliary bytes are immutable, shared across history snapshots to avoid script copies.
            foreach (var lump in auxiliary) copy.auxiliary.Add(new MapAuxiliaryLump(lump.name, lump.data));
            foreach (var v in vertices) { var c = new MapVertex(v.x, v.y); v.CopyFieldsTo(c); copy.vertices.Add(c); }
            foreach (var l in lines) copy.lines.Add(l.Clone());
            foreach (var s in sides) { var c = new MapSide { sector = s.sector, offsetX = s.offsetX, offsetY = s.offsetY, upper = s.upper, lower = s.lower, middle = s.middle }; s.CopyFieldsTo(c); copy.sides.Add(c); }
            foreach (var s in sectors) { var c = new MapSector { floor = s.floor, ceiling = s.ceiling, light = s.light, special = s.special, tag = s.tag, floorTexture = s.floorTexture, ceilingTexture = s.ceilingTexture }; s.CopyFieldsTo(c); copy.sectors.Add(c); }
            foreach (var t in things) { var c = new MapThing { x = t.x, y = t.y, z = t.z, angle = t.angle, type = t.type, flags = t.flags, tag = t.tag, action = t.action, args = (int[])t.args.Clone(), udmfInitialized = t.udmfInitialized }; t.CopyFieldsTo(c); copy.things.Add(c); }
            return copy;
        }
        public static MapPoint Snap(MapPoint p, int grid)
        {
            return new MapPoint((float)Math.Round(p.x / grid, MidpointRounding.AwayFromZero) * grid,
                (float)Math.Round(p.y / grid, MidpointRounding.AwayFromZero) * grid);
        }
        public int FindOrAddVertex(MapPoint p)
        {
            for (int i = 0; i < vertices.Count; i++) if ((vertices[i].Position - p).LengthSquared < 0.01f) return i;
            vertices.Add(new MapVertex(p.x, p.y));
            return vertices.Count - 1;
        }
        // Plan on a snapshot so rejected contours never leave half-split walls.
        public int AddRoom(List<MapPoint> points,GeometryMergeMode mode = GeometryMergeMode.Replace,DoomResources resources = null,DrawTextureOptions textures = null)
        {
            textures = textures == null ? null : textures.Validated(format);
            var addition = new MapDocument { format = format,udmfNamespace = udmfNamespace };
            addition.AddAdjacentRoom(points);
            // A contour drawn inside a room inherits its sector properties, including
            // explicit world-space slope planes. New artwork can be changed afterwards.
            if (points != null && points.Count > 0)
                for (int i = sectors.Count - 1; i >= 0; i--) if (Contains(i,points[0]))
                {
                    var source = sectors[i]; var sector = new MapSector { floor = source.floor,ceiling = source.ceiling,light = source.light,special = source.special,tag = source.tag,floorTexture = source.floorTexture,ceilingTexture = source.ceilingTexture };
                    source.CopyFieldsTo(sector); addition.sectors[0] = sector; break;
                }
            var stitched = MapStitch.Overlay(this,addition,mode,resources); var result = stitched.map;
            if (textures != null) textures.Apply(stitched);
            if (textures != null && textures.alignX) DrawTextureAlignment.Apply(stitched,resources);
            vertices = result.vertices; lines = result.lines; sides = result.sides; sectors = result.sectors;
            foreach (int sector in stitched.sectors) return sector;
            throw new InvalidOperationException("Контур не создал сектор.");
        }
        static float Cross(MapPoint a, MapPoint b, MapPoint c) { return (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x); }
        public static bool SegmentsIntersect(MapPoint a, MapPoint b, MapPoint c, MapPoint d)
        {
            if (Math.Max(a.x, b.x) < Math.Min(c.x, d.x) || Math.Max(c.x, d.x) < Math.Min(a.x, b.x) ||
                Math.Max(a.y, b.y) < Math.Min(c.y, d.y) || Math.Max(c.y, d.y) < Math.Min(a.y, b.y)) return false;
            return Cross(a, b, c) * Cross(a, b, d) <= 0 && Cross(c, d, a) * Cross(c, d, b) <= 0;
        }
        static bool PointInPolygon(List<MapPoint> points, MapPoint p)
        {
            bool inside = false;
            for (int i = 0, j = points.Count - 1; i < points.Count; j = i++)
            {
                var a = points[i]; var b = points[j];
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }
            return inside;
        }
        public bool Contains(int sector, MapPoint p)
        {
            bool inside = false;
            foreach (var l in lines)
            {
                bool f = l.front >= 0 && sides[l.front].sector == sector;
                bool b = l.back >= 0 && sides[l.back].sector == sector;
                if (f == b) continue;
                var a = vertices[l.start]; var z = vertices[l.end];
                if ((a.y > p.y) != (z.y > p.y) && p.x < (z.x - a.x) * (p.y - a.y) / (z.y - a.y) + a.x) inside = !inside;
            }
            return inside;
        }
        public ElementRef? Hit(MapPoint p, ElementKind kind, float radius)
        {
            if (kind == ElementKind.Sector)
            {
                for (int i = sectors.Count - 1; i >= 0; i--) if (Contains(i, p)) return new ElementRef(kind, i);
                return null;
            }
            int best = -1; float nearest = radius * radius;
            int count = kind == ElementKind.Vertex ? vertices.Count : kind == ElementKind.Thing ? things.Count : lines.Count;
            for (int i = 0; i < count; i++)
            {
                float distance;
                if (kind == ElementKind.Line)
                {
                    var l = lines[i]; var a = vertices[l.start].Position; var b = vertices[l.end].Position; var ab = b - a;
                    float t = ab.LengthSquared == 0 ? 0 : Math.Max(0, Math.Min(1, ((p.x - a.x) * ab.x + (p.y - a.y) * ab.y) / ab.LengthSquared));
                    distance = (p - new MapPoint(a.x + t * ab.x, a.y + t * ab.y)).LengthSquared;
                }
                else distance = (p - (kind == ElementKind.Vertex ? vertices[i].Position : things[i].Position)).LengthSquared;
                if (distance <= nearest) { nearest = distance; best = i; }
            }
            return best < 0 ? (ElementRef?)null : new ElementRef(kind, best);
        }
        public HashSet<int> SelectedVertices(HashSet<ElementRef> selection)
        {
            var result = new HashSet<int>();
            foreach (var e in selection)
            {
                if (e.kind == ElementKind.Vertex) result.Add(e.index);
                if (e.kind == ElementKind.Line) { result.Add(lines[e.index].start); result.Add(lines[e.index].end); }
                if (e.kind == ElementKind.Sector)
                    foreach (var l in lines)
                        if ((l.front >= 0 && sides[l.front].sector == e.index) || (l.back >= 0 && sides[l.back].sector == e.index))
                        { result.Add(l.start); result.Add(l.end); }
            }
            return result;
        }
        public void MoveFrom(MapDocument origin, HashSet<ElementRef> selection, MapPoint delta)
        {
            foreach (int i in origin.SelectedVertices(selection)) vertices[i].Position = origin.vertices[i].Position + delta;
            foreach (var e in selection) if (e.kind == ElementKind.Thing) things[e.index].Position = origin.things[e.index].Position + delta;
        }
        public void SplitLine(int index,DoomResources resources = null)
        {
            MidpointLineSplit.Prepare(this,new[]{index},CurveSplitOffsets.Interpolate,resources).Apply(this);
        }
        static int[] Compact<T>(List<T> items, HashSet<int> removed)
        {
            int[] map = new int[items.Count]; int target = 0;
            for (int i = 0; i < items.Count; i++) { map[i] = removed.Contains(i) ? -1 : target; if (map[i] >= 0) items[target++] = items[i]; }
            items.RemoveRange(target, items.Count - target); return map;
        }
    }
    public sealed class MapHistory
    {
        readonly List<MapDocument> undo = new List<MapDocument>(), redo = new List<MapDocument>();
        public bool CanUndo { get { return undo.Count > 0; } }
        public bool CanRedo { get { return redo.Count > 0; } }
        public void Record(MapDocument before) { undo.Add(before.Clone()); if (undo.Count > 32) undo.RemoveAt(0); redo.Clear(); }
        public MapDocument Undo(MapDocument current)
        {
            if (!CanUndo) return current;
            redo.Add(current.Clone()); var result = undo[undo.Count - 1]; undo.RemoveAt(undo.Count - 1); return result;
        }
        public MapDocument Redo(MapDocument current)
        {
            if (!CanRedo) return current;
            undo.Add(current.Clone()); var result = redo[redo.Count - 1]; redo.RemoveAt(redo.Count - 1); return result;
        }
        public void Clear() { undo.Clear(); redo.Clear(); }
    }
}

