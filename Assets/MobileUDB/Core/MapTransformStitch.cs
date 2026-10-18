// GPL-3.0. Rebuild moved geometry while retaining UDB's unstable neighbour lines.
using System;
using System.Collections.Generic;

namespace MobileUDB.Core
{
    public static partial class MapStitch
    {
        // Unlike clipboard overlay, these walls already belong to the destination.
        // Shared endpoints move together; no copy/delete detachment is performed.
        public static HashSet<ElementRef> StitchMoved(MapDocument before,MapDocument moved,HashSet<int> movingVertices,HashSet<int> movingSectors,HashSet<ElementRef> selection,GeometryMergeMode mode,DoomResources resources = null,bool rawBeforeAccuracy=false)
        {
            if (!Enum.IsDefined(typeof(GeometryMergeMode),mode)) throw new InvalidOperationException("Неизвестный режим сшивки.");
            if (mode == GeometryMergeMode.Classic) return ClassicMoved(moved,movingVertices,selection,resources);
            var edges = new List<Edge>(); var affected = new HashSet<int>();
            for (int pass = 0; pass < 2; pass++) for (int i = 0; i < moved.lines.Count; i++)
            {
                var line = moved.lines[i]; bool moving = movingVertices.Contains(line.start) || movingVertices.Contains(line.end);
                if (moving != (pass == 1)) continue;
                var edge = new Edge { map = moved,index = i,added = moving,a = moved.vertices[line.start].Position,b = moved.vertices[line.end].Position };
                edges.Add(edge);
                if (moving) { if (line.front >= 0) affected.Add(moved.sides[line.front].sector); if (line.back >= 0) affected.Add(moved.sides[line.back].sector); }
            }
            int fixedCount = edges.FindIndex(edge => edge.added); if (fixedCount < 0) fixedCount = edges.Count;
            for (int i = fixedCount; i < edges.Count; i++) for (int j = 0; j < i; j++) Intersect(edges[i],edges[j]);
            var pool = new VertexPool(); var vertexIds = new int[moved.vertices.Count];
            for (int i = 0; i < vertexIds.Length; i++) vertexIds[i] = pool.Keep(moved.vertices[i]);
            foreach (int vertex in movingVertices) vertexIds[vertex] = pool.Add(moved.vertices[vertex].Position,moved.vertices[vertex]);
            foreach (int vertex in movingVertices) foreach (var edge in edges) AddCut(edge,moved.vertices[vertex].Position);
            for (int v = 0; v < moved.vertices.Count; v++) if (!movingVertices.Contains(v))
                for (int i = fixedCount; i < edges.Count; i++) AddCut(edges[i],moved.vertices[v].Position);
            for (int i = fixedCount; i < edges.Count; i++) foreach (double cut in edges[i].cuts)
                for (int j = 0; j < fixedCount; j++) AddCut(edges[j],edges[i].At(cut));
            var groups = new Dictionary<Pair,List<Piece>>(); var ordered = new List<List<Piece>>(); var pieces = new List<Piece>();
            var zero = new List<Edge>();
            foreach (var edge in edges)
            {
                if (edge.Length < Epsilon) { if (!edge.added) zero.Add(edge); continue; }
                edge.cuts.Sort(); var cuts = new List<double>();
                foreach (double cut in edge.cuts) if (cuts.Count == 0 || (cut - cuts[cuts.Count - 1]) * edge.Length > Epsilon) cuts.Add(cut);
                for (int i = 0; i + 1 < cuts.Count; i++)
                {
                    int a = Vertex(edge,cuts[i],vertexIds,vertexIds,pool,moved.format,rawBeforeAccuracy),b = Vertex(edge,cuts[i + 1],vertexIds,vertexIds,pool,moved.format,rawBeforeAccuracy);
                    if (a == b) continue;
                    var piece = new Piece { edge = edge,start = a,end = b,from = cuts[i],to = cuts[i + 1] };
                    // Canonical grouping is deferred until all cuts have joined vertices.
                    pieces.Add(piece);
                }
            }
            foreach (var piece in pieces)
            {
                piece.start = pool.Canonical(piece.start); piece.end = pool.Canonical(piece.end);
                if (piece.start == piece.end) continue;
                List<Piece> group; var pair = new Pair(piece.start,piece.end);
                if (!groups.TryGetValue(pair,out group)) { group = new List<Piece>(); groups.Add(pair,group); ordered.Add(group); } group.Add(piece);
            }
            var localGroups = new List<List<Piece>>();
            foreach (var group in ordered)
            {
                bool touched = group.Exists(piece => piece.edge.added || piece.edge.cuts.Count > 2);
                if (!touched && group.Count > 1) foreach (var piece in group) localGroups.Add(new List<Piece> { piece });
                else localGroups.Add(group);
            }
            ordered = localGroups;
            var output = moved.Clone(); output.vertices = pool.vertices; output.lines = new List<MapLine>(); output.sides = new List<MapSide>();
            var ownership = new Regions(moved,0); var primaries = new List<Piece>(); var retained = new List<List<Piece>>(); var changed = new HashSet<int>();
            foreach (var group in ordered)
            {
                var primary = group[0]; bool moving = false;
                foreach (var piece in group) if (piece.edge.added) { moving = true; if (piece.edge.Line.action != 0 || primary.edge.added) primary = piece; }
                double x,y; MovedSample(primary,edges,out x,out y,true);
                int f = ownership.MovingOwner(x,y,movingSectors);
                MovedSample(primary,edges,out x,out y,false); int b = ownership.MovingOwner(x,y,movingSectors);
                if (!moving && mode == GeometryMergeMode.Replace && f >= 0 && f == b)
                { foreach (var piece in group) { int one = Owner(piece,true),two = Owner(piece,false); if (one >= 0) affected.Add(one); if (two >= 0) affected.Add(two); } continue; }
                var line = primary.edge.Line.Clone(); line.start = primary.start; line.end = primary.end;
                foreach (var piece in group) if (piece != primary) MergeFields(piece.edge.Line,line,false);
                if (moving || group.Count > 1 || primary.edge.cuts.Count > 2) changed.Add(output.lines.Count);
                output.lines.Add(line); primaries.Add(primary); retained.Add(group);
            }
            // Trace the planar graph, including void sides. Each cycle is one face
            // boundary; positive cycles also represent holes/exterior boundaries.
            var owners = TraceMovedFaces(output,moved,ownership,movingSectors,affected,changed,primaries,edges);
            for (int i = 0; i < output.lines.Count; i++)
            {
                var line = output.lines[i]; var primary = primaries[i]; var group = retained[i]; int front = owners[i * 2],back = owners[i * 2 + 1];
                bool wasDouble = primary.edge.Line.front >= 0 && primary.edge.Line.back >= 0,nowDouble = front >= 0 && back >= 0;
                line.front = Side(output,group,primary,front,true,resources,nowDouble && !wasDouble);
                line.back = Side(output,group,primary,back,false,resources,nowDouble && !wasDouble);
                if (wasDouble != nowDouble || (primary.edge.Line.front < 0 && primary.edge.Line.back < 0 && (front >= 0 || back >= 0)))
                {
                    line.flags = nowDouble ? (line.flags | 4) & ~1 : (line.flags & ~4) | 1;
                    if (output.format == MapFormat.Udmf) { UdmfText.SetBool(line.fields,"twosided",nowDouble); UdmfText.SetBool(line.fields,"blocking",!nowDouble); }
                }
                if (line.front < 0 && line.back >= 0) { int swap = line.start; line.start = line.end; line.end = swap; line.SwapSides(); }
                if (!nowDouble && wasDouble && line.front >= 0) CloseSide(output.sides[line.front]);
            }
            foreach (var edge in zero)
            {
                var piece = new Piece { edge = edge,start = pool.Canonical(vertexIds[edge.Line.start]),end = pool.Canonical(vertexIds[edge.Line.end]),from = 0,to = 1 };
                var group = new List<Piece> { piece }; var line = edge.Line.Clone(); line.start = piece.start; line.end = piece.end;
                line.front = Side(output,group,piece,Owner(piece,true),true,resources,false); line.back = Side(output,group,piece,Owner(piece,false),false,resources,false); output.lines.Add(line);
            }
            var usedSectors = new HashSet<int>(); foreach (var side in output.sides) usedSectors.Add(side.sector);
            var oldUsed = new HashSet<int>(); foreach (var side in before.sides) oldUsed.Add(side.sector);
            int[] sectorIds = new int[output.sectors.Count]; var sectors = new List<MapSector>();
            for (int i = 0; i < sectorIds.Length; i++) { sectorIds[i] = !usedSectors.Contains(i) && oldUsed.Contains(i) && affected.Contains(i) ? -1 : sectors.Count; if (sectorIds[i] >= 0) sectors.Add(output.sectors[i]); }
            output.sectors = sectors; foreach (var side in output.sides) side.sector = sectorIds[side.sector];
            var originallyConnected = new HashSet<int>(); foreach (var line in before.lines) { originallyConnected.Add(line.start); originallyConnected.Add(line.end); }
            var keep = new HashSet<int>(); for (int i = 0; i < vertexIds.Length; i++) if (!originallyConnected.Contains(i)) keep.Add(pool.Canonical(vertexIds[i]));
            foreach (var line in output.lines) { keep.Add(line.start); keep.Add(line.end); }
            int[] compactIds = new int[output.vertices.Count]; var vertices = new List<MapVertex>();
            for (int i = 0; i < compactIds.Length; i++) { compactIds[i] = keep.Contains(i) ? vertices.Count : -1; if (compactIds[i] >= 0) vertices.Add(output.vertices[i]); }
            output.vertices = vertices; foreach (var line in output.lines) { line.start = compactIds[line.start]; line.end = compactIds[line.end]; }
            output.Validate();
            if (output.format != MapFormat.Udmf && (output.vertices.Count > 65535 || output.lines.Count > 65535 || output.sides.Count > 65535 || output.sectors.Count > 65535)) throw new InvalidOperationException("Превышен лимит элементов бинарной карты.");
            foreach (int sector in affected) if (sectorIds[sector] >= 0)
            {
                bool closed = true; try { MapTransforms.ValidateSector(before,sector); } catch (InvalidOperationException) { closed = false; }
                if (closed) MapTransforms.ValidateSector(output,sectorIds[sector]);
            }
            var selected = new HashSet<ElementRef>();
            foreach (var item in selection)
            {
                int index = item.index;
                if (item.kind == ElementKind.Vertex) index = compactIds[pool.Canonical(vertexIds[index])];
                if (item.kind == ElementKind.Sector) index = sectorIds[index];
                if (item.kind == ElementKind.Line)
                { for (int i = 0; i < retained.Count; i++) foreach (var piece in retained[i]) if (piece.edge.index == item.index) { selected.Add(new ElementRef(ElementKind.Line,i)); break; } continue; }
                if (index >= 0) selected.Add(new ElementRef(item.kind,index));
            }
            moved.vertices = output.vertices; moved.lines = output.lines; moved.sides = output.sides; moved.sectors = output.sectors;
            return selected;
        }
        static void MovedSample(Piece piece,List<Edge> edges,out double x,out double y,bool front)
        {
            double t = (piece.from + piece.to) * .5; var mid = piece.edge.At(t);
            double step = Math.Min(.01,piece.edge.Length * (piece.to - piece.from) * .1);
            foreach (var edge in edges) { double distance = Distance(edge.a,edge.b,mid.x,mid.y); if (distance > Epsilon * .1) step = Math.Min(step,distance * .2); }
            if (!front) step = -step;
            x = mid.x + (piece.edge.b.y - (double)piece.edge.a.y) / piece.edge.Length * step;
            y = mid.y - (piece.edge.b.x - (double)piece.edge.a.x) / piece.edge.Length * step;
        }
        static int[] TraceMovedFaces(MapDocument map,MapDocument source,Regions ownership,HashSet<int> preferred,HashSet<int> affected,HashSet<int> changed,List<Piece> primaries,List<Edge> sourceEdges)
        {
            var outgoing = new Dictionary<int,List<int>>(); int[] owners = new int[map.lines.Count * 2];
            for (int i = 0; i < owners.Length; i++)
            {
                int start = Start(map,i); List<int> list; if (!outgoing.TryGetValue(start,out list)) { list = new List<int>(); outgoing.Add(start,list); } list.Add(i);
            }
            // A rotation system makes the next-edge map a permutation, including
            // pre-existing parallel duplicate edges elsewhere in a legacy map.
            int[] next = new int[owners.Length];
            foreach (var pair in outgoing)
            {
                var start = map.vertices[pair.Key].Position; var list = pair.Value;
                list.Sort((one,two) =>
                {
                    var a = map.vertices[End(map,one)].Position; var b = map.vertices[End(map,two)].Position;
                    int order = Math.Atan2(a.y - (double)start.y,a.x - (double)start.x).CompareTo(Math.Atan2(b.y - (double)start.y,b.x - (double)start.x));
                    return order == 0 ? one.CompareTo(two) : order;
                });
                for (int i = 0; i < list.Count; i++) next[list[i] ^ 1] = list[(i + 1) % list.Count];
            }
            bool[] visited = new bool[owners.Length];
            for (int first = 0; first < owners.Length; first++)
            {
                if (visited[first]) continue; var face = new List<int>(); int current = first; bool touched = false;
                do
                {
                    if (visited[current]) throw new InvalidOperationException("Не удалось восстановить область преобразования.");
                    visited[current] = true; face.Add(current); touched |= changed.Contains(current / 2);
                    if (face.Count > owners.Length) throw new InvalidOperationException("Некорректный узел преобразования."); current = next[current];
                } while (current != first);
                // A face may border both moved and stationary walls. Its interior
                // receives a single owner, with fully selected sectors taking priority.
                // Use the unsnapped source fragment so rounding binary intersections
                // cannot move the sample across the original boundary.
                double x,y; MovedSample(primaries[first / 2],sourceEdges,out x,out y,(first & 1) == 0); int owner = ownership.PreferredOwner(x,y,preferred);
                foreach (int edge in face)
                {
                    var line = map.lines[edge / 2]; int oldSide = (edge & 1) == 0 ? line.front : line.back;
                    int oldOwner = oldSide < 0 ? -1 : source.sides[oldSide].sector;
                    owners[edge] = touched || affected.Contains(oldOwner) ? owner : oldOwner;
                    if (owners[edge] != oldOwner) { if (oldOwner >= 0) affected.Add(oldOwner); if (owners[edge] >= 0) affected.Add(owners[edge]); }
                }
            }
            return owners;
        }
    }
}
