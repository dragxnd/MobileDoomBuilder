// GPL-3.0. Geometry closure follows UDB MapSet.MarkAllSelectedGeometry/CloneMarked.
using System;
using System.Collections.Generic;

namespace MobileUDB.Core
{
    // An independent, in-memory snapshot; source edits and repeated pastes never alias it.
    public sealed class MapClipboard
    {
        readonly MapDocument fragment;
        readonly ElementKind kind;
        readonly HashSet<int> externalSectors;
        public MapPoint Origin { get; private set; }
        public int SectorCount { get { return fragment.sectors.Count - externalSectors.Count; } }
        public int ThingCount { get { return fragment.things.Count; } }
        public int LineCount { get { return fragment.lines.Count; } }
        public MapFormat Format { get { return fragment.format; } }
        public string Namespace { get { return fragment.udmfNamespace; } }
        MapClipboard(MapDocument data, ElementKind selectionKind,HashSet<int> external)
        { fragment = data; kind = selectionKind; externalSectors = external; Origin = MapTransforms.Center(data, MapTransforms.All(data)); }

        public static MapClipboard Copy(MapDocument map, HashSet<ElementRef> selection, ElementKind kind, bool includeSectorThings = true)
        {
            var vertices = new HashSet<int>(); var lines = new HashSet<int>(); var sectors = new HashSet<int>(); var things = new HashSet<int>();
            foreach (var item in selection)
            {
                if (item.kind == ElementKind.Vertex) vertices.Add(item.index);
                if (item.kind == ElementKind.Line) lines.Add(item.index);
                if (item.kind == ElementKind.Sector) sectors.Add(item.index);
                if (item.kind == ElementKind.Thing) things.Add(item.index);
            }
            for (int i = 0; i < map.lines.Count; i++)
            {
                var line = map.lines[i];
                if (sectors.Contains(Sector(map, line.front)) || sectors.Contains(Sector(map, line.back))) lines.Add(i);
            }
            // A complete selection of walls also selects its enclosed sector properties.
            var incomplete = new HashSet<int>(); var referenced = new HashSet<int>();
            for (int i = 0; i < map.lines.Count; i++)
                foreach (int sector in new[] { Sector(map, map.lines[i].front), Sector(map, map.lines[i].back) })
                    if (sector >= 0) { if (lines.Contains(i)) referenced.Add(sector); else incomplete.Add(sector); }
            foreach (int sector in referenced) if (!incomplete.Contains(sector)) sectors.Add(sector);
            foreach (int i in lines)
            {
                var line = map.lines[i]; vertices.Add(line.start); vertices.Add(line.end);
            }
            if (includeSectorThings && sectors.Count > 0)
                for (int i = 0; i < map.things.Count; i++)
                    foreach (int sector in sectors) if (map.Contains(sector, map.things[i].Position)) { things.Add(i); break; }
            if (vertices.Count == 0 && things.Count == 0) throw new InvalidOperationException("Сначала выберите элементы для копирования.");

            var result = new MapDocument { format = map.format, udmfNamespace = map.udmfNamespace };
            var vertexIds = new Dictionary<int,int>(); var sectorIds = new Dictionary<int,int>();
            var external = new HashSet<int>();
            var source = map.Clone(); // Includes extension fields and independent five-argument arrays.
            for (int i = 0; i < map.vertices.Count; i++) if (vertices.Contains(i)) { vertexIds[i] = result.vertices.Count; result.vertices.Add(source.vertices[i]); }
            for (int i = 0; i < map.sectors.Count; i++) if (sectors.Contains(i) || referenced.Contains(i))
            { sectorIds[i] = result.sectors.Count; if (!sectors.Contains(i)) external.Add(result.sectors.Count); result.sectors.Add(source.sectors[i]); }
            for (int i = 0; i < map.lines.Count; i++) if (lines.Contains(i))
            {
                var line = source.lines[i]; line.start = vertexIds[line.start]; line.end = vertexIds[line.end];
                line.front = CopySide(map, result, line.front, sectorIds); line.back = CopySide(map, result, line.back, sectorIds);
                result.lines.Add(line);
            }
            for (int i = 0; i < map.things.Count; i++) if (things.Contains(i)) result.things.Add(source.things[i]);
            result.Validate(); for (int i = 0; i < result.sectors.Count; i++) if (!external.Contains(i)) MapTransforms.ValidateSector(result,i);
            return new MapClipboard(result, kind,external);
        }
        static int Sector(MapDocument map, int side) { return side < 0 ? -1 : map.sides[side].sector; }
        static int CopySide(MapDocument source, MapDocument target, int index, Dictionary<int,int> sectors)
        {
            if (index < 0 || !sectors.ContainsKey(source.sides[index].sector)) return -1;
            var side = source.sides[index];
            var copy = new MapSide { sector = sectors[side.sector], offsetX = side.offsetX, offsetY = side.offsetY, upper = side.upper, lower = side.lower, middle = side.middle };
            side.CopyFieldsTo(copy); target.sides.Add(copy); return target.sides.Count - 1;
        }
        public MapDocument Preview(MapPoint position)
        {
            var copy = fragment.Clone(); MapTransforms.Translate(copy,position - Origin); return copy;
        }
        public HashSet<ElementRef> Paste(MapDocument target, MapPoint position,GeometryMergeMode mode = GeometryMergeMode.Replace,DoomResources resources = null,PasteTagMode tags = PasteTagMode.Keep,ActionCatalog actions = null)
        {
            if (target.format != Format || (Format == MapFormat.Udmf && !string.Equals(target.udmfNamespace,Namespace,StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Формат и пространство имён карты должны совпадать с буфером. Конвертация вставки пока недоступна.");
            var copy = Preview(position); copy.Validate();
            PasteTags.Apply(copy,target,tags,actions);
            var stitched = MapStitch.Overlay(target,copy,mode,resources,externalSectors); var output = stitched.map;
            target.vertices = output.vertices; target.lines = output.lines; target.sides = output.sides; target.sectors = output.sectors; target.things = output.things;
            var selected = new HashSet<ElementRef>();
            // Prefer the source selection mode, but retain all pasted things in the selection.
            if (kind == ElementKind.Vertex) foreach (int i in stitched.vertices) selected.Add(new ElementRef(kind,i));
            else if (kind == ElementKind.Line) foreach (int i in stitched.lines) selected.Add(new ElementRef(kind,i));
            else foreach (int i in stitched.sectors) selected.Add(new ElementRef(ElementKind.Sector,i));
            foreach (int i in stitched.things) selected.Add(new ElementRef(ElementKind.Thing,i));
            return selected;
        }
    }
}
