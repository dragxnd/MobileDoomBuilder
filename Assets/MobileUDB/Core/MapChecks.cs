// GPL-3.0. Mobile diagnostics derived from UDB BuilderModes/ErrorChecks.
using System;
using System.Collections.Generic;

namespace MobileUDB.Core
{
    public sealed class MapIssue
    {
        public readonly string code, message;
        public readonly bool error;
        public readonly ElementRef[] elements;
        public readonly MapPoint? position;
        public readonly UnusedTextureTarget unusedTexture;
        public MapIssue(string code,string message,bool error,MapPoint? position,params ElementRef[] elements)
        { this.code = code; this.message = message; this.error = error; this.position = position; this.elements = elements; }
        public MapIssue(string code,string message,MapPoint position,UnusedTextureTarget target,ElementRef element)
            :this(code,message,false,position,element){unusedTexture=target;}
    }
    public static class MapChecks
    {
        static ElementRef Ref(ElementKind kind,int index) { return new ElementRef(kind,index); }
        static bool Empty(string name) { return string.IsNullOrEmpty(name) || name == "-"; }
        static bool Valid(int index,int count) { return index >= 0 && index < count; }
        static bool Finite(float number) { return !float.IsNaN(number) && !float.IsInfinity(number); }
        static double Cross(MapPoint a,MapPoint b) { return (double)a.x * b.y - (double)a.y * b.x; }
        static double Dot(MapPoint a,MapPoint b) { return (double)a.x * b.x + (double)a.y * b.y; }
        static MapPoint At(MapPoint a,MapPoint delta,double amount) { return new MapPoint((float)(a.x + delta.x * amount),(float)(a.y + delta.y * amount)); }
        static bool Heights(MapSector sector,MapPoint point,out float floor,out float ceiling)
        {
            floor = ceiling = 0;
            try { floor = VisualGeometry.Height(sector,false,point); ceiling = VisualGeometry.Height(sector,true,point); return Finite(floor) && Finite(ceiling); }
            catch (InvalidOperationException) { return false; }
        }
        // Null yields are cooperative scheduling points. The caller may stop/dispose
        // at any time. This check never normalizes, repairs or otherwise mutates the map.
        public static IEnumerable<MapIssue> Scan(MapDocument map,DoomResources resources = null,int maximumPairChecks = 2000000,ActionCatalog actions=null,ThingCatalog things=null)
        {
            bool structural = false;
            for (int i = 0; i < map.vertices.Count; i++)
            {
                var v = map.vertices[i];
                if (v == null || !Finite(v.x) || !Finite(v.y)) { structural = true; yield return new MapIssue("invalid-vertex","Некорректные координаты вершины #" + i,true,null,Ref(ElementKind.Vertex,i)); }
                if ((i & 127) == 0) yield return null;
            }
            for (int i = 0; i < map.sides.Count; i++)
            {
                if (map.sides[i] == null || !Valid(map.sides[i].sector,map.sectors.Count)) { structural = true; yield return new MapIssue("invalid-side","Сторона #" + i + ": неверная ссылка на сектор",true,null); }
                if ((i & 127) == 0) yield return null;
            }
            for (int i = 0; i < map.lines.Count; i++)
            {
                var l = map.lines[i];
                if (l == null || !Valid(l.start,map.vertices.Count) || !Valid(l.end,map.vertices.Count) || l.front < -1 || l.front >= map.sides.Count || l.back < -1 || l.back >= map.sides.Count)
                { structural = true; yield return new MapIssue("invalid-line","Стена #" + i + ": неверные ссылки",true,null,Ref(ElementKind.Line,i)); }
                if ((i & 127) == 0) yield return null;
            }
            for (int i = 0; i < map.sectors.Count; i++) if (map.sectors[i] == null) structural = true;
            if (structural) { yield return new MapIssue("incomplete","Геометрия не проверена: сначала исправьте повреждённые ссылки / координаты.",false,null); yield break; }
            foreach(var issue in SectorBoundaryChecks.Scan(map))yield return issue;
            if(actions!=null||things!=null)foreach(var issue in MapMetadataChecks.Scan(map,actions,things))yield return issue;
            foreach(var issue in PolyobjectChecks.Scan(map,actions,things))yield return issue;
            foreach(var issue in BinaryTextureChecks.Scan(map,actions))yield return issue;
            if(things!=null)foreach(var issue in ThingMapChecks.Scan(map,things,actions==null?null:actions.thingFlags,maximumPairChecks,false))yield return issue;
            bool wallResources = resources != null && resources.WallCount > 0,flatResources = resources != null && resources.FlatCount > 0;
            bool mixed = MapFormats.IsUniversal(map.format);
            var usedVertices = new bool[map.vertices.Count];
            var boundary = new Dictionary<int,int>[map.sectors.Count];
            for (int s = 0; s < boundary.Length; s++) boundary[s] = new Dictionary<int,int>();
            var sorted = new List<Segment>();
            for (int i = 0; i < map.lines.Count; i++)
            {
                var l = map.lines[i]; var a = map.vertices[l.start].Position; var b = map.vertices[l.end].Position; var middle = At(a,b - a,.5);
                var action=actions==null?null:actions.Find(l.action);
                usedVertices[l.start] = usedVertices[l.end] = true; var element = Ref(ElementKind.Line,i);
                if ((b - a).LengthSquared < .000001f) yield return new MapIssue("zero-line","Стена #" + i + ": нулевая длина",true,middle,element);
                else sorted.Add(new Segment(i,a,b));
                if (l.front < 0) yield return new MapIssue("missing-front","Стена #" + i + (l.back < 0 ? ": нет сторон" : ": нет передней стороны"),true,middle,element);
                else if (((l.flags & 4) != 0) != (l.back >= 0)) yield return new MapIssue("side-flag","Стена #" + i + ": флаг двусторонности не совпадает со сторонами",true,middle,element);
                foreach (int sideIndex in new[] { l.front,l.back })
                {
                    if (sideIndex < 0) continue; var side = map.sides[sideIndex]; int sector = side.sector;
                    int opposite = sideIndex == l.front ? l.back : l.front;
                    // Self-referencing lines do not bound a sector.
                    if (opposite < 0 || map.sides[opposite].sector != sector)
                    {
                        int start = sideIndex == l.front ? l.start : l.end,end = sideIndex == l.front ? l.end : l.start;
                        AddBoundary(boundary[sector],start,1); AddBoundary(boundary[sector],end,-1);
                    }
                    string prefix = "Стена #" + i + (sideIndex == l.front ? " · передняя: " : " · задняя: ");
                    if (wallResources || (mixed && flatResources))
                    {
                        var names=new[]{side.upper,side.middle,side.lower};var ignored=new[]{action!=null&&action.ignoreUpperTexture,action!=null&&action.ignoreMiddleTexture,action!=null&&action.ignoreLowerTexture};
                        for(int part=0;part<names.Length;part++)
                        {string name=names[part];if (!ignored[part]&&!Empty(name) && resources.Find(name,ResourceKind.Wall,mixed) == null)
                                yield return new MapIssue("unknown-wall",prefix + "текстура «" + name + "» не найдена",false,middle,element);
                        }
                    }
                }
                if ((i & 63) == 0) yield return null;
            }
            var coordinates = new Dictionary<MapPoint,int>(); var unused = new List<ElementRef>();
            for (int i = 0; i < map.vertices.Count; i++)
            {
                var p = map.vertices[i].Position; int previous;
                if (!usedVertices[i]) unused.Add(Ref(ElementKind.Vertex,i));
                if (coordinates.TryGetValue(p,out previous)) yield return new MapIssue("duplicate-vertex","Вершины #" + previous + " / #" + i + ": совпадают",true,p,Ref(ElementKind.Vertex,previous),Ref(ElementKind.Vertex,i));
                else coordinates.Add(p,i);
                if ((i & 127) == 0) yield return null;
            }
            if (unused.Count > 0) yield return new MapIssue("stray-vertex","Неиспользуемые вершины: " + unused.Count + " (первая #" + unused[0].index + "). Возможны остатки BSP.",false,map.vertices[unused[0].index].Position,unused.ToArray());
            for (int i = 0; i < map.sectors.Count; i++)
            {
                var s = map.sectors[i]; var element = Ref(ElementKind.Sector,i); MapPoint? location = null; bool invalidHeight = false,inverted = false; int visited = 0;
                foreach (var edge in boundary[i])
                {
                    var point = map.vertices[edge.Key].Position; if (!location.HasValue) location = point;
                    float floor,ceiling; if (!Heights(s,point,out floor,out ceiling)) invalidHeight = true; else if (floor > ceiling + .001f) inverted = true;
                    if ((++visited & 127) == 0) yield return null;
                }
                if (!location.HasValue) location = new MapPoint(0,0);
                if (!Finite(s.floor) || !Finite(s.ceiling) || invalidHeight) yield return new MapIssue("invalid-height","Сектор #" + i + ": некорректные высоты / плоскости",true,location,element);
                else if (inverted || (boundary[i].Count == 0 && s.floor > s.ceiling)) yield return new MapIssue("inverted-sector","Сектор #" + i + ": пол выше потолка",true,location,element);
                foreach (bool ceiling in new[] { false,true })
                {
                    string name = ceiling ? s.ceilingTexture : s.floorTexture,part = ceiling ? "потолок" : "пол";
                    if (Empty(name)) yield return new MapIssue("missing-flat","Сектор #" + i + " · " + part + ": нет текстуры",false,location,element);
                    else if ((flatResources || (mixed && wallResources)) && !GameSky.Is(map,name) && resources.Find(name,ResourceKind.Flat,mixed) == null)
                        yield return new MapIssue("unknown-flat","Сектор #" + i + " · " + part + ": «" + name + "» не найдена",false,location,element);
                }
                yield return null;
            }
            sorted.Sort((a,b) => a.minX.CompareTo(b.minX)); int comparisons = 0;
            for (int i = 0; i < sorted.Count; i++)
            {
                var a = sorted[i];
                for (int j = i + 1; j < sorted.Count && sorted[j].minX <= a.maxX + .0001; j++)
                {
                    if (++comparisons > maximumPairChecks) { yield return new MapIssue("incomplete","Проверка пересечений остановлена на пределе сложности. Остальные проверки завершены.",false,null); yield break; }
                    var b = sorted[j];
                    if (a.minY <= b.maxY + .0001 && b.minY <= a.maxY + .0001)
                    {
                        MapPoint point; string code = Intersection(a,b,map,out point);
                        if (code != null) yield return new MapIssue(code,"Стены #" + a.index + " / #" + b.index + (code == "overlap" ? ": накладываются" : ": пересекаются без общей вершины"),true,point,Ref(ElementKind.Line,a.index),Ref(ElementKind.Line,b.index));
                    }
                    if ((comparisons & 511) == 0) yield return null;
                }
                if ((i & 127) == 0) yield return null;
            }
        }
        static void AddBoundary(Dictionary<int,int> boundary,int vertex,int delta)
        { int value; boundary.TryGetValue(vertex,out value); boundary[vertex] = value + delta; }
        sealed class Segment
        {
            public readonly int index; public readonly MapPoint a,b; public readonly float minX,maxX,minY,maxY;
            public Segment(int index,MapPoint a,MapPoint b) { this.index = index; this.a = a; this.b = b; minX = Math.Min(a.x,b.x); maxX = Math.Max(a.x,b.x); minY = Math.Min(a.y,b.y); maxY = Math.Max(a.y,b.y); }
        }
        static string Intersection(Segment a,Segment b,MapDocument map,out MapPoint position)
        {
            position = a.a; var r = a.b - a.a; var s = b.b - b.a; var delta = b.a - a.a; double denominator = Cross(r,s);
            if (Math.Abs(denominator) > 1e-9)
            {
                double t = Cross(delta,s) / denominator,u = Cross(delta,r) / denominator;
                if (t < -1e-8 || t > 1 + 1e-8 || u < -1e-8 || u > 1 + 1e-8) return null;
                var first = map.lines[a.index]; var second = map.lines[b.index];
                if (first.start == second.start || first.start == second.end || first.end == second.start || first.end == second.end) return null;
                position = At(a.a,r,t); return "crossing";
            }
            if (Math.Abs(Cross(delta,r)) > 1e-7 * Math.Sqrt(Dot(r,r))) return null;
            double length = Dot(r,r),t0 = Dot(delta,r) / length,t1 = t0 + Dot(s,r) / length;
            double start = Math.Max(0,Math.Min(t0,t1)),end = Math.Min(1,Math.Max(t0,t1));
            if ((end - start) * Math.Sqrt(length) <= .0001) return null;
            position = At(a.a,r,(start + end) * .5); return "overlap";
        }
    }
}
