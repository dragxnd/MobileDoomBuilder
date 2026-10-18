// GPL-3.0. Mobile equivalent of UDB's split/join/correct-sector merge path.
using System;
using System.Collections.Generic;
using System.Globalization;

namespace MobileUDB.Core
{
    public enum GeometryMergeMode { Merge, Replace, Classic }
    public sealed class MapStitchResult
    {
        public MapDocument map;
        public readonly HashSet<int> vertices = new HashSet<int>(), lines = new HashSet<int>(), sectors = new HashSet<int>(), things = new HashSet<int>();
        // Output sidedefs without a pre-existing side in the same direction.
        // Unlike `lines`, this excludes old walls merely split or drawn over.
        public readonly HashSet<int> createdSides = new HashSet<int>();
    }
    public static partial class MapStitch
    {
        const double Epsilon = .001;
        sealed class Edge
        {
            public MapDocument map;
            public int index, sectorOffset;
            public bool added;
            public MapPoint a,b;
            public readonly List<double> cuts = new List<double> { 0,1 };
            public MapLine Line { get { return map.lines[index]; } }
            public double Length { get { return Math.Sqrt((b.x - (double)a.x) * (b.x - a.x) + (b.y - (double)a.y) * (b.y - a.y)); } }
            public MapPoint At(double t) { return new MapPoint((float)(a.x + (b.x - (double)a.x) * t),(float)(a.y + (b.y - (double)a.y) * t)); }
        }
        sealed class Piece
        {
            public Edge edge;
            public int start,end;
            public double from,to;
        }
        struct Pair : IEquatable<Pair>
        {
            public int a,b;
            public Pair(int start,int end) { a = Math.Min(start,end); b = Math.Max(start,end); }
            public bool Equals(Pair other) { return a == other.a && b == other.b; }
            public override bool Equals(object other) { return other is Pair && Equals((Pair)other); }
            public override int GetHashCode() { return a * 397 ^ b; }
        }
        struct Cell : IEquatable<Cell>
        {
            public long x,y;
            public Cell(double a,double b) { x = checked((long)Math.Floor(a / Epsilon)); y = checked((long)Math.Floor(b / Epsilon)); }
            public bool Equals(Cell other) { return x == other.x && y == other.y; }
            public override bool Equals(object other) { return other is Cell && Equals((Cell)other); }
            public override int GetHashCode() { return x.GetHashCode() * 397 ^ y.GetHashCode(); }
        }
        sealed class VertexPool
        {
            public readonly List<MapVertex> vertices = new List<MapVertex>();
            readonly List<int> parents = new List<int>();
            readonly Dictionary<Cell,List<int>> cells = new Dictionary<Cell,List<int>>();
            public int Canonical(int index) { while (parents[index] != index) index = parents[index]; return index; }
            public int Keep(MapVertex prototype) { return Append(prototype.Position,prototype); }
            int Append(MapPoint point,MapVertex prototype)
            {
                var vertex = new MapVertex(point.x,point.y); if (prototype != null) prototype.CopyFieldsTo(vertex);
                int added = vertices.Count; vertices.Add(vertex); parents.Add(added); var cell = new Cell(point.x,point.y);
                List<int> bucket; if (!cells.TryGetValue(cell,out bucket)) { bucket = new List<int>(); cells[cell] = bucket; } bucket.Add(added); return added;
            }
            public int Add(MapPoint point,MapVertex prototype = null)
            {
                var cell = new Cell(point.x,point.y); int found = -1;
                for (long x = cell.x - 1; x <= cell.x + 1; x++) for (long y = cell.y - 1; y <= cell.y + 1; y++)
                {
                    List<int> candidates;
                    if (!cells.TryGetValue(new Cell { x = x,y = y },out candidates)) continue;
                    foreach (int index in candidates) if ((vertices[index].Position - point).LengthSquared <= Epsilon * Epsilon)
                    {
                        int candidate = Canonical(index);
                        if (found < 0) found = candidate;
                        else if (candidate != found) { parents[candidate] = found; MergeFields(vertices[candidate],vertices[found],false); }
                    }
                }
                if (found >= 0) { if (prototype != null) MergeFields(prototype,vertices[found],false); return found; }
                return Append(point,prototype);
            }
        }
        sealed class Region
        {
            public double minX = double.MaxValue,minY = double.MaxValue,maxX = double.MinValue,maxY = double.MinValue;
            public readonly List<Edge> boundary = new List<Edge>();
            public bool Contains(double x,double y)
            {
                if (x < minX || x > maxX || y < minY || y > maxY) return false;
                bool inside = false;
                foreach (var edge in boundary) if ((edge.a.y > y) != (edge.b.y > y) && x < (edge.b.x - (double)edge.a.x) * (y - edge.a.y) / (edge.b.y - (double)edge.a.y) + edge.a.x) inside = !inside;
                return inside;
            }
        }
        sealed class Regions
        {
            readonly Region[] regions;
            readonly int offset;
            public Regions(MapDocument map,int sectorOffset,ISet<int> external = null)
            {
                offset = sectorOffset; regions = new Region[map.sectors.Count]; for (int i = 0; i < regions.Length; i++) regions[i] = new Region();
                for (int i = 0; i < map.lines.Count; i++)
                {
                    var line = map.lines[i]; int front = line.front < 0 ? -1 : map.sides[line.front].sector, back = line.back < 0 ? -1 : map.sides[line.back].sector;
                    if (front == back) continue;
                    var edge = new Edge { map = map,index = i,a = map.vertices[line.start].Position,b = map.vertices[line.end].Position };
                    foreach (int sector in new[] { front,back }) if (sector >= 0 && (external == null || !external.Contains(sector)))
                    {
                        var region = regions[sector]; region.boundary.Add(edge);
                        region.minX = Math.Min(region.minX,Math.Min(edge.a.x,edge.b.x)); region.maxX = Math.Max(region.maxX,Math.Max(edge.a.x,edge.b.x));
                        region.minY = Math.Min(region.minY,Math.Min(edge.a.y,edge.b.y)); region.maxY = Math.Max(region.maxY,Math.Max(edge.a.y,edge.b.y));
                    }
                }
            }
            public int Owner(double x,double y) { for (int i = regions.Length - 1; i >= 0; i--) if (regions[i].Contains(x,y)) return i + offset; return -1; }
            public int PreferredOwner(double x,double y,ISet<int> preferred)
            {
                for (int i = regions.Length - 1; i >= 0; i--) if (preferred.Contains(i) && regions[i].Contains(x,y)) return i + offset;
                return Owner(x,y);
            }
            public int MovingOwner(double x,double y,ISet<int> moving)
            { for (int i = regions.Length - 1; i >= 0; i--) if (moving.Contains(i) && regions[i].Contains(x,y)) return i + offset; return -1; }
        }
        public static MapStitchResult Overlay(MapDocument target,MapDocument addition,GeometryMergeMode mode = GeometryMergeMode.Replace,DoomResources resources = null,ISet<int> externalSectors = null,int partitionOnlySector = -1,bool omitEmptyAddedLines = false)
        {
            target.Validate(); addition.Validate();
            if (!Enum.IsDefined(typeof(GeometryMergeMode),mode)) throw new InvalidOperationException("Неизвестный режим сшивки.");
            if (target.format != addition.format || (target.format == MapFormat.Udmf && !string.Equals(target.udmfNamespace,addition.udmfNamespace,StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("Форматы сшиваемой геометрии должны совпадать.");
            if (mode == GeometryMergeMode.Classic) return ClassicOverlay(target,addition,resources,externalSectors);
            for (int i = 0; i < addition.sectors.Count; i++) if (externalSectors == null || !externalSectors.Contains(i)) MapTransforms.ValidateSector(addition,i);
            if (addition.lines.Count == 0 && addition.vertices.Count == 0 && addition.sectors.Count == 0)
            {
                var thingsOnly = new MapStitchResult { map = target.Clone() }; var copied = addition.Clone();
                foreach (var thing in copied.things) { thingsOnly.things.Add(thingsOnly.map.things.Count); thingsOnly.map.things.Add(thing); }
                return thingsOnly;
            }
            var oldRegions = new Regions(target,0); var newRegions = new Regions(addition,target.sectors.Count,externalSectors);
            var edges = new List<Edge>(); int fixedCount = target.lines.Count;
            AddEdges(edges,target,false,0); AddEdges(edges,addition,true,target.sectors.Count);
            // Existing-to-existing intersections are left alone; unrelated IWAD defects
            // must not be repaired as an accidental side effect of a local paste.
            for (int i = fixedCount; i < edges.Count; i++) for (int j = 0; j < i; j++) Intersect(edges[i],edges[j]);
            var pool = new VertexPool(); var oldVertices = new int[target.vertices.Count]; var newVertices = new int[addition.vertices.Count];
            for (int i = 0; i < oldVertices.Length; i++) oldVertices[i] = pool.Keep(target.vertices[i]);
            for (int i = 0; i < newVertices.Length; i++)
            {
                // Plan contacts in source precision, then give incoming endpoints
                // and wall cuts the same binary vertex. This may bend a diagonal
                // by less than a map unit, as binary WADs cannot store the contact.
                var point = addition.vertices[i].Position;
                if (target.format != MapFormat.Udmf) point = MapDocument.Snap(point,1);
                newVertices[i] = pool.Add(point,addition.vertices[i]);
            }
            // Standalone pasted vertices also split walls at their positions.
            foreach (var vertex in addition.vertices) foreach (var edge in edges) AddCut(edge,vertex.Position);
            foreach (var vertex in target.vertices) for (int i = fixedCount; i < edges.Count; i++) AddCut(edges[i],vertex.Position);
            // A fixed standalone vertex can split an incoming wall without having
            // split its coincident fixed wall. Align their subdivisions before
            // grouping fragments, otherwise the full fixed wall survives twice.
            for (int i = fixedCount; i < edges.Count; i++) foreach (double cut in edges[i].cuts)
            {
                var point = edges[i].At(cut);
                for (int j = 0; j < fixedCount; j++) AddCut(edges[j],point);
            }
            var pieces = new List<Piece>();
            foreach (var edge in edges)
            {
                if (edge.Length < Epsilon) { pieces.Add(new Piece { edge = edge,start = oldVertices[edge.Line.start],end = oldVertices[edge.Line.end],from = 0,to = 1 }); continue; }
                edge.cuts.Sort(); var cuts = new List<double>();
                foreach (double cut in edge.cuts) if (cuts.Count == 0 || (cut - cuts[cuts.Count - 1]) * edge.Length > Epsilon) cuts.Add(cut);
                for (int i = 0; i + 1 < cuts.Count; i++)
                {
                    int a = Vertex(edge,cuts[i],oldVertices,newVertices,pool,target.format),b = Vertex(edge,cuts[i + 1],oldVertices,newVertices,pool,target.format);
                    if (a == b) continue;
                    pieces.Add(new Piece { edge = edge,start = a,end = b,from = cuts[i],to = cuts[i + 1] });
                }
            }
            for (int i = 0; i < oldVertices.Length; i++) oldVertices[i] = pool.Canonical(oldVertices[i]);
            for (int i = 0; i < newVertices.Length; i++) newVertices[i] = pool.Canonical(newVertices[i]);
            var groups = new Dictionary<Pair,List<Piece>>(); var ordered = new List<List<Piece>>();
            foreach (var piece in pieces)
            {
                piece.start = pool.Canonical(piece.start); piece.end = pool.Canonical(piece.end); if (piece.start == piece.end) continue;
                var key = new Pair(piece.start,piece.end); List<Piece> group;
                if (!groups.TryGetValue(key,out group)) { group = new List<Piece>(); groups[key] = group; ordered.Add(group); } group.Add(piece);
            }
            var result = new MapStitchResult { map = target.Clone() }; var output = result.map;
            output.vertices = pool.vertices; output.lines = new List<MapLine>(); output.sides = new List<MapSide>();
            var newCopy = addition.Clone(); output.sectors.AddRange(newCopy.sectors);
            var partitions = new HashSet<int>();
            var affected = new HashSet<int>(); var originalUsed = new HashSet<int>(); foreach (var side in target.sides) originalUsed.Add(side.sector);
            foreach (var group in ordered)
            {
                Piece primary = group[0]; bool added = false;
                foreach (var piece in group) if (piece.edge.added) { added = true; if (piece.edge.Line.action != 0 || primary.edge.added) primary = piece; }
                var line = primary.edge.Line.Clone(); line.start = primary.start; line.end = primary.end;
                if (primary.edge.Length < Epsilon)
                {
                    int owner = newRegions.Owner(primary.edge.a.x,primary.edge.a.y);
                    if (owner >= 0 && mode == GeometryMergeMode.Replace) continue;
                    line.front = Side(output,group,primary,owner >= 0 ? owner : Owner(primary,true),true,resources,false,result.createdSides);
                    line.back = Side(output,group,primary,owner >= 0 ? owner : Owner(primary,false),false,resources,false,result.createdSides);
                    output.lines.Add(line); continue;
                }
                double mx = primary.edge.a.x + (primary.edge.b.x - (double)primary.edge.a.x) * (primary.from + primary.to) * .5;
                double my = primary.edge.a.y + (primary.edge.b.y - (double)primary.edge.a.y) * (primary.from + primary.to) * .5;
                double nx = (primary.edge.b.y - (double)primary.edge.a.y) / primary.edge.Length,ny = -(primary.edge.b.x - (double)primary.edge.a.x) / primary.edge.Length;
                // A sample must stay in this face, even alongside a very thin corridor.
                double step = Math.Min(.01,primary.edge.Length * (primary.to - primary.from) * .1);
                for (int edgeIndex = primary.edge.added ? 0 : fixedCount; edgeIndex < edges.Count; edgeIndex++)
                {
                    var edge = edges[edgeIndex];
                    double distance = Distance(edge.a,edge.b,mx,my);
                    if (distance > Epsilon * .1) step = Math.Min(step,distance * .2);
                }
                int newFront = newRegions.Owner(mx + nx * step,my + ny * step),newBack = newRegions.Owner(mx - nx * step,my - ny * step);
                int front = newFront,back = newBack;
                if (front < 0) front = primary.edge.added ? oldRegions.Owner(mx + nx * step,my + ny * step) : Owner(primary,true);
                if (back < 0) back = primary.edge.added ? oldRegions.Owner(mx - nx * step,my - ny * step) : Owner(primary,false);
                if (added && partitionOnlySector >= 0 && front != partitionOnlySector && back != partitionOnlySector) continue;
                if (added && omitEmptyAddedLines && front < 0 && back < 0) continue;
                if (!added && mode == GeometryMergeMode.Replace && newFront >= target.sectors.Count && newFront == newBack)
                { foreach (var piece in group) { int f = Owner(piece,true),b = Owner(piece,false); if (f >= 0) affected.Add(f); if (b >= 0) affected.Add(b); } continue; }
                foreach (var piece in group)
                {
                    int f = Owner(piece,true),b = Owner(piece,false); if (piece.start != primary.start) { int swap = f; f = b; b = swap; }
                    if (f != front || b != back || piece.edge.cuts.Count > 2 || group.Count > 1)
                    { if (f >= 0) affected.Add(f); if (b >= 0) affected.Add(b); if (front >= 0) affected.Add(front); if (back >= 0) affected.Add(back); }
                    if (piece != primary) MergeFields(piece.edge.Line,line,false);
                }
                bool wasDouble = primary.edge.Line.front >= 0 && primary.edge.Line.back >= 0,nowDouble = front >= 0 && back >= 0;
                line.front = Side(output,group,primary,front,true,resources,nowDouble && !wasDouble,result.createdSides);
                line.back = Side(output,group,primary,back,false,resources,nowDouble && !wasDouble,result.createdSides);
                if (wasDouble != nowDouble || (primary.edge.Line.front < 0 && primary.edge.Line.back < 0 && (front >= 0 || back >= 0)))
                {
                    line.flags = nowDouble ? (line.flags | 4) & ~1 : (line.flags & ~4) | 1;
                    if (output.format == MapFormat.Udmf) { UdmfText.SetBool(line.fields,"twosided",nowDouble); UdmfText.SetBool(line.fields,"blocking",!nowDouble); }
                }
                if (line.front < 0 && line.back >= 0) { int swap = line.start; line.start = line.end; line.end = swap; line.SwapSides(); }
                if (!nowDouble && wasDouble && line.front >= 0) CloseSide(output.sides[line.front]);
                if (added)
                {
                    result.lines.Add(output.lines.Count);
                    if (front >= 0 && front == back && externalSectors != null)
                        foreach (var piece in group) if (piece.edge.added)
                        {
                            int f = piece.edge.Line.front < 0 ? -1 : piece.edge.map.sides[piece.edge.Line.front].sector;
                            int b = piece.edge.Line.back < 0 ? -1 : piece.edge.map.sides[piece.edge.Line.back].sector;
                            if ((f < 0 || externalSectors.Contains(f)) && (b < 0 || externalSectors.Contains(b))) { partitions.Add(output.lines.Count); affected.Add(front); break; }
                        }
                }
                output.lines.Add(line);
            }
            SplitFaces(output,partitions,affected);
            // Remove sectors whose geometry was fully replaced, retaining pre-existing
            // unused control sectors. The ownership map also keeps disconnected parts/holes.
            var used = new HashSet<int>(); foreach (var side in output.sides) used.Add(side.sector);
            int[] sectorMap = new int[output.sectors.Count]; var compact = new List<MapSector>();
            for (int i = 0; i < sectorMap.Length; i++)
            {
                if (!used.Contains(i) && (i >= target.sectors.Count || originalUsed.Contains(i))) { sectorMap[i] = -1; continue; }
                sectorMap[i] = compact.Count; compact.Add(output.sectors[i]); if (i >= target.sectors.Count) result.sectors.Add(sectorMap[i]);
            }
            output.sectors = compact; foreach (var side in output.sides) side.sector = sectorMap[side.sector];
            for (int i = 0; i < addition.things.Count; i++) { result.things.Add(output.things.Count); output.things.Add(newCopy.things[i]); }
            // Keep pre-existing standalone vertices. Discard only newly generated split
            // vertices left with no edge after Replace removes an interior wall.
            var originallyConnected = new HashSet<int>(); foreach (var line in target.lines) { originallyConnected.Add(line.start); originallyConnected.Add(line.end); }
            var keepVertices = new HashSet<int>(); for (int i = 0; i < oldVertices.Length; i++) if (!originallyConnected.Contains(i)) keepVertices.Add(oldVertices[i]); keepVertices.UnionWith(newVertices);
            foreach (var line in output.lines) { keepVertices.Add(line.start); keepVertices.Add(line.end); }
            int[] vertexMap = new int[output.vertices.Count]; var vertexList = new List<MapVertex>();
            for (int i = 0; i < vertexMap.Length; i++) { vertexMap[i] = keepVertices.Contains(i) ? vertexList.Count : -1; if (vertexMap[i] >= 0) vertexList.Add(output.vertices[i]); }
            output.vertices = vertexList; foreach (var line in output.lines) { line.start = vertexMap[line.start]; line.end = vertexMap[line.end]; }
            foreach (int vertex in newVertices) result.vertices.Add(vertexMap[vertex]);
            output.Validate();
            if (output.format != MapFormat.Udmf) foreach (var vertex in output.vertices)
                if (vertex.x < -32768 || vertex.x > 32767 || vertex.y < -32768 || vertex.y > 32767) throw new InvalidOperationException("Координаты за пределами бинарного формата карты.");
            if (output.format != MapFormat.Udmf && (output.vertices.Count > 65535 || output.sides.Count > 65535 || output.lines.Count > 65535 || output.sectors.Count > 65535)) throw new InvalidOperationException("Превышен лимит элементов бинарной карты.");
            foreach (int sector in result.sectors) MapTransforms.ValidateSector(output,sector);
            foreach (int sector in affected) if (sector < target.sectors.Count && sectorMap[sector] >= 0)
            {
                // If a touched original sector was already open, do not claim to repair it.
                bool closed = true; try { MapTransforms.ValidateSector(target,sector); } catch (InvalidOperationException) { closed = false; }
                if (closed) MapTransforms.ValidateSector(output,sectorMap[sector]);
            }
            return result;
        }
        public static MapStitchResult PartitionSector(MapDocument map,int sector,MapPoint start,MapPoint end,DoomResources resources = null)
        {
            if (sector < 0 || sector >= map.sectors.Count) throw new InvalidOperationException("Выберите сектор для разреза.");
            MapTransforms.ValidateSector(map,sector);
            MapPoint boundary;
            if ((start - end).LengthSquared < .01f || !map.TrySectorBoundaryPoint(sector,start,(float)Epsilon,out boundary) || !map.TrySectorBoundaryPoint(sector,end,(float)Epsilon,out boundary))
                throw new InvalidOperationException("Укажите две разные точки на границе сектора.");
            var addition = new MapDocument { format = map.format,udmfNamespace = map.udmfNamespace };
            addition.vertices.Add(new MapVertex(start.x,start.y)); addition.vertices.Add(new MapVertex(end.x,end.y));
            addition.sectors.Add(new MapSector()); addition.sides.Add(new MapSide { sector = 0 });
            addition.lines.Add(new MapLine { start = 0,end = 1,front = 0 });
            var result = Overlay(map,addition,GeometryMergeMode.Merge,resources,new HashSet<int> { 0 },sector,true);
            if (result.sectors.Count == 0) throw new InvalidOperationException("Разрез не разделяет выбранный сектор на отдельные области.");
            return result;
        }
        static void AddEdges(List<Edge> edges,MapDocument map,bool added,int offset)
        { for (int i = 0; i < map.lines.Count; i++) { var line = map.lines[i]; var edge = new Edge { map = map,index = i,added = added,sectorOffset = offset,a = map.vertices[line.start].Position,b = map.vertices[line.end].Position }; if (added && edge.Length < Epsilon) throw new InvalidOperationException("Стена нулевой длины: " + i); edges.Add(edge); } }
        static int Owner(Piece piece,bool front)
        { int side = front ? piece.edge.Line.front : piece.edge.Line.back; return side < 0 ? -1 : piece.edge.map.sides[side].sector + piece.edge.sectorOffset; }
        static int Vertex(Edge edge,double t,int[] oldVertices,int[] newVertices,VertexPool pool,MapFormat format,bool rawBeforeAccuracy=false)
        {
            var mapping = edge.added ? newVertices : oldVertices;
            if (t <= 0) return mapping[edge.Line.start]; if (t >= 1) return mapping[edge.Line.end];
            var point = edge.At(t); if (format != MapFormat.Udmf&&!rawBeforeAccuracy) point = MapDocument.Snap(point,1); return pool.Add(point);
        }
        static double Cross(double ax,double ay,double bx,double by) { return ax * by - ay * bx; }
        static void Intersect(Edge one,Edge two)
        {
            if (one.Length < Epsilon || two.Length < Epsilon) return;
            if (Math.Max(one.a.x,one.b.x) + Epsilon < Math.Min(two.a.x,two.b.x) || Math.Max(two.a.x,two.b.x) + Epsilon < Math.Min(one.a.x,one.b.x) ||
                Math.Max(one.a.y,one.b.y) + Epsilon < Math.Min(two.a.y,two.b.y) || Math.Max(two.a.y,two.b.y) + Epsilon < Math.Min(one.a.y,one.b.y)) return;
            double ax = one.b.x - (double)one.a.x,ay = one.b.y - (double)one.a.y,bx = two.b.x - (double)two.a.x,by = two.b.y - (double)two.a.y;
            double denominator = Cross(ax,ay,bx,by);
            if (Math.Abs(denominator) < 1e-10)
            { AddCut(one,two.a); AddCut(one,two.b); AddCut(two,one.a); AddCut(two,one.b); return; }
            double px = two.a.x - (double)one.a.x,py = two.a.y - (double)one.a.y;
            double t = Cross(px,py,bx,by) / denominator,u = Cross(px,py,ax,ay) / denominator;
            if (t < -1e-9 || t > 1 + 1e-9 || u < -1e-9 || u > 1 + 1e-9) return;
            one.cuts.Add(Math.Max(0,Math.Min(1,t))); two.cuts.Add(Math.Max(0,Math.Min(1,u)));
        }
        static void AddCut(Edge edge,MapPoint p)
        {
            if (edge.Length < Epsilon) return;
            double dx = edge.b.x - (double)edge.a.x,dy = edge.b.y - (double)edge.a.y;
            double t = ((p.x - edge.a.x) * dx + (p.y - edge.a.y) * dy) / (dx * dx + dy * dy);
            if (t > 0 && t < 1 && Distance(edge.a,edge.b,p.x,p.y) <= Epsilon) edge.cuts.Add(t);
        }
        static double Distance(MapPoint a,MapPoint b,double x,double y)
        {
            double dx = b.x - (double)a.x,dy = b.y - (double)a.y;
            if (dx == 0 && dy == 0) return Math.Sqrt((x - a.x) * (x - a.x) + (y - a.y) * (y - a.y));
            double t = Math.Max(0,Math.Min(1,((x - a.x) * dx + (y - a.y) * dy) / (dx * dx + dy * dy)));
            dx = x - (a.x + dx * t); dy = y - (a.y + dy * t); return Math.Sqrt(dx * dx + dy * dy);
        }
        static int Side(MapDocument output,List<Piece> group,Piece primary,int owner,bool front,DoomResources resources,bool open,HashSet<int> createdSides = null)
        {
            if (owner < 0) return -1;
            Piece found = null; bool foundFront = front;
            foreach (var piece in group)
            {
                bool sourceFront = piece.start == primary.start ? front : !front;
                if (Owner(piece,sourceFront) == owner) { found = piece; foundFront = sourceFront; break; }
            }
            if (found == null)
            {
                // A newly created outer side inherits the copied wall artwork/metadata.
                found = primary; foundFront = front;
                if ((foundFront ? found.edge.Line.front : found.edge.Line.back) < 0) foundFront = !foundFront;
            }
            int index = foundFront ? found.edge.Line.front : found.edge.Line.back;
            var side = new MapSide { sector = owner,middle = "-" };
            if (index >= 0)
            {
                var source = found.edge.map.sides[index]; side.offsetX = source.offsetX; side.offsetY = source.offsetY; side.upper = source.upper; side.lower = source.lower; side.middle = source.middle; source.CopyFieldsTo(side);
                double distance = found.edge.Length * (foundFront ? found.from : 1 - found.to); ShiftTexture(side,distance,output.format,resources);
            }
            if (open) OpenSide(side);
            int added = output.sides.Count; output.sides.Add(side);
            if (createdSides == null) return added;
            bool existed = false;
            foreach (var piece in group) if (!piece.edge.added)
            {
                bool same = piece.start == primary.start ? front : !front;
                if ((same ? piece.edge.Line.front : piece.edge.Line.back) >= 0) { existed = true; break; }
            }
            if (!existed) createdSides.Add(added);
            return added;
        }
        sealed class Face
        {
            public readonly List<int> edges = new List<int>();
            public readonly List<MapPoint> points = new List<MapPoint>();
            public double area;
            public int owner;
            public bool cut;
        }
        static int SideIndex(MapDocument map,int edge) { return (edge & 1) == 0 ? map.lines[edge / 2].front : map.lines[edge / 2].back; }
        static int Start(MapDocument map,int edge) { return (edge & 1) == 0 ? map.lines[edge / 2].start : map.lines[edge / 2].end; }
        static int End(MapDocument map,int edge) { return (edge & 1) == 0 ? map.lines[edge / 2].end : map.lines[edge / 2].start; }
        static void SplitFaces(MapDocument map,HashSet<int> partitions,HashSet<int> affected)
        {
            if (partitions.Count == 0) return;
            var owners = new HashSet<int>(); foreach (int i in partitions) owners.Add(map.sides[map.lines[i].front].sector);
            var ownership = new Regions(map,0);
            foreach (int owner in owners)
            {
                var outgoing = new Dictionary<int,List<int>>(); var edges = new List<int>(); var controls = new List<int>();
                for (int i = 0; i < map.lines.Count; i++)
                {
                    var line = map.lines[i]; bool f = line.front >= 0 && map.sides[line.front].sector == owner,b = line.back >= 0 && map.sides[line.back].sector == owner;
                    if (f && b && !partitions.Contains(i)) { controls.Add(i); continue; }
                    for (int side = 0; side < 2; side++) if (side == 0 ? f : b)
                    {
                        int edge = i * 2 + side,vertex = Start(map,edge); List<int> list;
                        if (!outgoing.TryGetValue(vertex,out list)) { list = new List<int>(); outgoing[vertex] = list; }
                        list.Add(edge); edges.Add(edge);
                    }
                }
                var faces = new List<Face>(); var visited = new HashSet<int>();
                foreach (int first in edges)
                {
                    if (visited.Contains(first)) continue;
                    var face = new Face { owner = owner }; int current = first;
                    do
                    {
                        if (!visited.Add(current)) throw new InvalidOperationException("Не удалось восстановить замкнутую область после сшивки.");
                        face.edges.Add(current); face.cut |= partitions.Contains(current / 2);
                        var a = map.vertices[Start(map,current)].Position; var b = map.vertices[End(map,current)].Position;
                        face.points.Add(a); face.area += a.x * (double)b.y - b.x * (double)a.y;
                        List<int> next; if (!outgoing.TryGetValue(End(map,current),out next)) throw new InvalidOperationException("Незамкнутая граница в месте сшивки.");
                        double reverse = Math.Atan2(a.y - (double)b.y,a.x - (double)b.x),turn = double.MaxValue; int chosen = -1;
                        foreach (int candidate in next)
                        {
                            if (candidate == (current ^ 1) && next.Count > 1) continue;
                            var end = map.vertices[End(map,candidate)].Position;
                            double angle = (Math.Atan2(end.y - (double)b.y,end.x - (double)b.x) - reverse + Math.PI * 2) % (Math.PI * 2);
                            if (angle < 1e-10) angle += Math.PI * 2;
                            if (angle < turn) { turn = angle; chosen = candidate; }
                        }
                        if (chosen < 0 || face.edges.Count > edges.Count) throw new InvalidOperationException("Некорректный узел сшивки."); current = chosen;
                    } while (current != first);
                    faces.Add(face);
                }
                var divided = faces.FindAll(face => face.area < -Epsilon * Epsilon && face.cut);
                // A closed chain inside a sector has a new inner face and an
                // untouched outer cycle. Include its containing outer face so
                // the new island receives independent ownership and a hole.
                var containers = new List<Face>();
                foreach (var face in divided)
                {
                    double x,y; Sample(map,face.edges[0],out x,out y);
                    foreach (var candidate in faces) if (candidate.area < -Epsilon * Epsilon && !candidate.cut && Inside(candidate.points,x,y) && !containers.Contains(candidate)) containers.Add(candidate);
                }
                divided.AddRange(containers);
                if (divided.Count < 2) continue; // A dangling wall or a cut from outer boundary to hole does not create two rooms.
                Face largest = divided[0]; foreach (var face in divided) if (face.area < largest.area) largest = face;
                foreach (var face in divided) if (face != largest)
                {
                    var source = map.sectors[owner]; var sector = new MapSector { floor = source.floor,ceiling = source.ceiling,light = source.light,special = source.special,tag = source.tag,floorTexture = source.floorTexture,ceilingTexture = source.ceilingTexture };
                    source.CopyFieldsTo(sector); face.owner = map.sectors.Count; map.sectors.Add(sector); affected.Add(face.owner);
                }
                foreach (var face in divided) Assign(map,face,face.owner);
                foreach (var face in faces) if (face.area > Epsilon * Epsilon)
                {
                    double x,y; Sample(map,face.edges[0],out x,out y); var parent = Containing(divided,x,y);
                    if (parent != null) Assign(map,face,parent.owner);
                }
                foreach (int index in controls)
                {
                    var line = map.lines[index]; var a = map.vertices[line.start].Position; var b = map.vertices[line.end].Position;
                    double x = (a.x + (double)b.x) * .5,y = (a.y + (double)b.y) * .5;
                    if (ownership.Owner(x,y) != owner) continue;
                    var parent = Containing(divided,x,y); if (parent == null) continue;
                    map.sides[line.front].sector = parent.owner; map.sides[line.back].sector = parent.owner;
                }
            }
        }
        static void Assign(MapDocument map,Face face,int owner) { foreach (int edge in face.edges) map.sides[SideIndex(map,edge)].sector = owner; }
        static Face Containing(List<Face> faces,double x,double y)
        { Face found = null; foreach (var face in faces) if (Inside(face.points,x,y) && (found == null || face.area > found.area)) found = face; return found; }
        static bool Inside(List<MapPoint> points,double x,double y)
        {
            bool inside = false;
            for (int i = 0,j = points.Count - 1; i < points.Count; j = i++)
            { var a = points[i]; var b = points[j]; if ((a.y > y) != (b.y > y) && x < (b.x - (double)a.x) * (y - a.y) / (b.y - (double)a.y) + a.x) inside = !inside; }
            return inside;
        }
        static void Sample(MapDocument map,int edge,out double x,out double y)
        {
            var a = map.vertices[Start(map,edge)].Position; var b = map.vertices[End(map,edge)].Position;
            double dx = b.x - (double)a.x,dy = b.y - (double)a.y,length = Math.Sqrt(dx * dx + dy * dy);
            double step = Math.Min(.001,length * .01); x = (a.x + (double)b.x) * .5 + dy / length * step; y = (a.y + (double)b.y) * .5 - dx / length * step;
        }
        public static void ShiftTexture(MapSide side,double distance,MapFormat format,DoomResources resources = null)
        {
            if (Math.Abs(distance) < .000001) return;
            if (format != MapFormat.Udmf) { side.offsetX = unchecked((short)(side.offsetX + (int)distance)); return; }
            foreach (string part in new[] { "top","mid","bottom" })
            {
                string name = part == "top" ? side.upper : part == "bottom" ? side.lower : side.middle;
                var texture = resources == null ? null : resources.Find(name,ResourceKind.Wall,true);
                float scale = Math.Abs(VisualGeometry.Field(side,"scalex_" + part,1)); if (scale < .000001) scale = 1;
                double delta = texture != null && texture.worldPanning ? distance : distance * scale / (texture == null ? 1 : texture.scaleX);
                UdmfText.SetRaw(side.fields,"offsetx_" + part,(VisualGeometry.Field(side,"offsetx_" + part,0) + delta).ToString("R",CultureInfo.InvariantCulture));
            }
        }
        static void OpenSide(MapSide side)
        { string texture = string.IsNullOrEmpty(side.middle) || side.middle == "-" ? "STARTAN3" : side.middle; if (side.upper == "-" || string.IsNullOrEmpty(side.upper)) side.upper = texture; if (side.lower == "-" || string.IsNullOrEmpty(side.lower)) side.lower = texture; side.middle = "-"; }
        static void CloseSide(MapSide side)
        { if (side.middle == "-" || string.IsNullOrEmpty(side.middle)) side.middle = side.upper != "-" && !string.IsNullOrEmpty(side.upper) ? side.upper : side.lower != "-" && !string.IsNullOrEmpty(side.lower) ? side.lower : "STARTAN3"; }
        static void MergeFields(MapElementData source,MapElementData target,bool overwrite)
        {
            foreach (var field in source.fields) if (overwrite || UdmfText.Raw(target.fields,field.name) == null) target.fields.Add(new UniversalField(field.name,field.value));
            foreach (string block in source.extraBlocks) if (!target.extraBlocks.Contains(block)) target.extraBlocks.Add(block);
        }
    }
}
