// GPL-3.0. Drawing-time horizontal strips; only genuinely new sidedefs change.
using System;
using System.Collections.Generic;
using System.Globalization;

namespace MobileUDB.Core
{
    internal static class DrawTextureAlignment
    {
        sealed class Edge
        {
            public int line,side,start,end,sector;
            public double length;
            public readonly List<SurfacePart> parts = new List<SurfacePart>();
            public readonly List<Edge> next = new List<Edge>(), previous = new List<Edge>();
        }
        struct VertexKey : IEquatable<VertexKey>
        {
            public int vertex,sector;
            public bool Equals(VertexKey other) { return vertex == other.vertex && sector == other.sector; }
            public override bool Equals(object other) { return other is VertexKey && Equals((VertexKey)other); }
            public override int GetHashCode() { return vertex * 397 ^ sector; }
        }
        sealed class Phase { public ResourceTexture texture; public SurfacePart part; public double end; }
        public static void Apply(MapStitchResult result,DoomResources resources)
        {
            if (resources == null || result.createdSides.Count == 0) return;
            var map = result.map; var edges = new List<Edge>(); var starts = new Dictionary<VertexKey,List<Edge>>();
            for (int i = 0; i < map.lines.Count; i++)
            {
                var line = map.lines[i];
                foreach (bool front in new[] { true,false })
                {
                    int side = front ? line.front : line.back;
                    if (side < 0 || !result.createdSides.Contains(side)) continue;
                    var edge = new Edge { line = i,side = side,start = front ? line.start : line.end,end = front ? line.end : line.start,sector = map.sides[side].sector };
                    int opposite = front ? line.back : line.front;
                    foreach (var part in new[] { SurfacePart.Middle,SurfacePart.Upper,SurfacePart.Lower })
                        if (DrawTextureOptions.Required(map,line,side,opposite,part)) edge.parts.Add(part);
                    if (edge.parts.Count == 0) continue;
                    var a = map.vertices[edge.start]; var b = map.vertices[edge.end];
                    edge.length = Math.Sqrt((a.x - (double)b.x) * (a.x - b.x) + (a.y - (double)b.y) * (a.y - b.y));
                    if (edge.length < .001) continue;
                    edges.Add(edge); var key = new VertexKey { vertex = edge.start,sector = edge.sector }; List<Edge> list;
                    if (!starts.TryGetValue(key,out list)) starts[key] = list = new List<Edge>(); list.Add(edge);
                }
            }
            foreach (var edge in edges)
            {
                List<Edge> neighbors;
                if (starts.TryGetValue(new VertexKey { vertex = edge.end,sector = edge.sector },out neighbors))
                    foreach (var next in neighbors) if (next.line != edge.line) { edge.next.Add(next); next.previous.Add(edge); }
            }
            var visited = new HashSet<Edge>();
            // Open strips begin at an endpoint or junction. Remaining strips are
            // cycles with deterministic line-order origins. Never cross a branch
            // or enter the opposite sector just because it shares a vertex.
            foreach (var edge in edges) if (edge.previous.Count != 1 || edge.previous[0].next.Count != 1) Walk(map,edge,resources,visited);
            foreach (var edge in edges) Walk(map,edge,resources,visited);
        }
        static void Walk(MapDocument map,Edge edge,DoomResources resources,HashSet<Edge> visited)
        {
            if (visited.Contains(edge)) return;
            // A solitary surface has no new neighbour to align with.
            if (edge.next.Count != 1 || edge.next[0].previous.Count != 1 || visited.Contains(edge.next[0])) { visited.Add(edge); return; }
            double distance = 0;
            var previous = new List<Phase>();
            while (visited.Add(edge))
            {
                var current = new List<Phase>();
                foreach (var part in edge.parts)
                {
                    var surface = new SurfaceRef { line = edge.line,side = edge.side,sector = edge.sector,part = part };
                    var texture = resources.Find(VisualGeometry.Texture(map,surface),ResourceKind.Wall,map.format == MapFormat.Udmf);
                    if (texture == null) continue;
                    var mapping = VisualGeometry.WallMapping(map,surface,texture);
                    double period = Math.Abs(mapping.width / mapping.unitX), offset = distance / mapping.unitX;
                    if (map.format == MapFormat.Udmf)
                    {
                        var parent = previous.Find(p => object.ReferenceEquals(p.texture,texture) && p.part == part) ?? previous.Find(p => object.ReferenceEquals(p.texture,texture));
                        if (parent != null) offset = parent.end * mapping.width / mapping.unitX;
                    }
                    offset %= period;
                    if (double.IsNaN(offset) || double.IsInfinity(offset) || Math.Abs(offset) > float.MaxValue)
                        throw new InvalidOperationException("Некорректный масштаб текстуры при выравнивании новых стен.");
                    var side = map.sides[edge.side];
                    if (map.format == MapFormat.Udmf)
                    {
                        string suffix = part == SurfacePart.Middle ? "mid" : part == SurfacePart.Upper ? "top" : "bottom";
                        UdmfText.SetRaw(side.fields,"offsetx_" + suffix,((float)(offset - side.offsetX)).ToString("R",CultureInfo.InvariantCulture));
                        mapping = VisualGeometry.WallMapping(map,surface,texture);
                        current.Add(new Phase { texture = texture,part = part,end = mapping.StartU + edge.length / mapping.width });
                    }
                    else
                    {
                        offset = Math.Round(offset,MidpointRounding.AwayFromZero);
                        if (offset < short.MinValue || offset > short.MaxValue) throw new InvalidOperationException("Смещение новых стен не помещается в Doom / Hexen.");
                        side.offsetX = (int)offset;
                        break; // Binary sides share their offset; prefer middle, then upper, then lower.
                    }
                }
                distance += edge.length;
                previous = current;
                if (edge.next.Count != 1 || edge.next[0].previous.Count != 1) break;
                edge = edge.next[0];
            }
        }
    }
}
