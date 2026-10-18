// GPL-3.0. Mobile sector fill uses the same even/odd boundary rule as MapDocument.Contains.
using System;
using System.Collections.Generic;

namespace MobileUDB.Core
{
    public struct MapTriangle
    {
        public MapPoint a, b, c;
        public MapTriangle(MapPoint a, MapPoint b, MapPoint c) { this.a = a; this.b = b; this.c = c; }
        public double Area { get { return Math.Abs((b.x - (double)a.x) * (c.y - a.y) - (b.y - (double)a.y) * (c.x - a.x)) * .5; } }
    }
    public sealed class SectorTessellation
    {
        struct Edge
        {
            public MapPoint a, b;
            public double Y(double x) { return a.y + (b.y - (double)a.y) * (x - a.x) / (b.x - (double)a.x); }
        }
        readonly List<Edge>[] boundaries;
        public SectorTessellation(MapDocument map)
        {
            boundaries = new List<Edge>[map.sectors.Count]; for (int i = 0; i < boundaries.Length; i++) boundaries[i] = new List<Edge>();
            foreach (var line in map.lines)
            {
                int front = line.front < 0 ? -1 : map.sides[line.front].sector, back = line.back < 0 ? -1 : map.sides[line.back].sector;
                if (front == back) continue; // Self-referencing control lines do not bound the sector.
                var edge = new Edge { a = map.vertices[line.start].Position, b = map.vertices[line.end].Position };
                if (front >= 0) boundaries[front].Add(edge); if (back >= 0) boundaries[back].Add(edge);
            }
        }
        public List<MapTriangle> Triangles(int sector)
        {
            var edges = boundaries[sector]; var xs = new SortedSet<float>();
            foreach (var edge in edges) { xs.Add(edge.a.x); xs.Add(edge.b.x); }
            var events = new List<float>(xs); var result = new List<MapTriangle>(); var crossing = new List<Edge>();
            // Every interval has a constant boundary order for a valid planar map. Pair its
            // intersections to form trapezoids: holes and disconnected islands need no bridges.
            for (int i = 0; i + 1 < events.Count; i++)
            {
                double left = events[i], right = events[i + 1], mid = (left + right) * .5;
                crossing.Clear();
                foreach (var edge in edges) if (mid > Math.Min(edge.a.x, edge.b.x) && mid < Math.Max(edge.a.x, edge.b.x)) crossing.Add(edge);
                crossing.Sort((a,b) => a.Y(mid).CompareTo(b.Y(mid)));
                if ((crossing.Count & 1) != 0) throw new InvalidOperationException("Незамкнутая граница сектора " + sector + ".");
                for (int j = 0; j < crossing.Count; j += 2)
                {
                    var low = crossing[j]; var high = crossing[j + 1];
                    double yl0 = low.Y(left), yl1 = low.Y(right), yh0 = high.Y(left), yh1 = high.Y(right);
                    if (yh0 + .001 < yl0 || yh1 + .001 < yl1) throw new InvalidOperationException("Пересекающиеся границы сектора " + sector + ".");
                    var a = new MapPoint((float)left,(float)yl0); var b = new MapPoint((float)left,(float)yh0);
                    var c = new MapPoint((float)right,(float)yh1); var d = new MapPoint((float)right,(float)yl1);
                    Add(result,new MapTriangle(a,b,c)); Add(result,new MapTriangle(a,c,d));
                }
            }
            return result;
        }
        static void Add(List<MapTriangle> result, MapTriangle triangle) { if (triangle.Area > .000001) result.Add(triangle); }
    }
}
