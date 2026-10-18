// GPL-3.0. BuilderModes Vertices/Linedefs/SectorsMode.placethings.
using System;
using System.Collections.Generic;

namespace MobileUDB.Core
{
    public static class SelectionThingPlacement
    {
        public static List<MapPoint> Positions(MapDocument map,IEnumerable<ElementRef> selection)
        {
            ThingClipboard.Binary(map);map.Validate();if(selection==null)throw new ArgumentNullException("selection");
            var targets=new SortedSet<int>();ElementKind? kind=null;
            foreach(var item in selection)
            {
                if(item.kind==ElementKind.Thing||kind.HasValue&&kind.Value!=item.kind)throw new InvalidOperationException("Выберите вершины, стены или секторы одного режима.");
                int count=item.kind==ElementKind.Vertex?map.vertices.Count:item.kind==ElementKind.Line?map.lines.Count:item.kind==ElementKind.Sector?map.sectors.Count:0;
                if(item.index<0||item.index>=count)throw new InvalidOperationException("Выбранный элемент больше не существует.");kind=item.kind;targets.Add(item.index);
            }
            if(targets.Count==0)throw new InvalidOperationException("Сначала выделите вершины, стены или секторы.");
            var points=new List<MapPoint>();var seen=new HashSet<MapPoint>();SectorTessellation fill=kind==ElementKind.Sector?new SectorTessellation(map):null;var work=new long[1];
            Action<MapPoint> add=p=>{ThingClipboard.Coordinate(p.x);ThingClipboard.Coordinate(p.y);if(seen.Add(p))points.Add(p);};
            foreach(int index in targets)
            {
                if(kind==ElementKind.Vertex)add(map.vertices[index].Position);
                else if(kind==ElementKind.Line){var line=map.lines[index];add(map.vertices[line.start].Position);add(map.vertices[line.end].Position);}
                else add(SectorPosition(map,index,fill,work));
            }
            return points;
        }
        static MapPoint SectorPosition(MapDocument map,int sector,SectorTessellation fill,long[] work)
        {
            // Native labels maximize clearance at interior triangulation diagonals.
            // Use the port's existing tessellation, including holes/islands, rather
            // than a bounding-box center that can lie outside a concave sector.
            var edges=new List<MapLine>();var balance=new Dictionary<int,int>();foreach(var line in map.lines)
            {
                PlacementStep(work);
                int front=line.front<0?-1:map.sides[line.front].sector,back=line.back<0?-1:map.sides[line.back].sector;
                if(front!=back&&(front==sector||back==sector))
                {
                    edges.Add(line);int start=front==sector?line.start:line.end,end=front==sector?line.end:line.start,value;
                    balance.TryGetValue(start,out value);balance[start]=value+1;balance.TryGetValue(end,out value);balance[end]=value-1;
                }
            }
            if(edges.Count<3)throw new InvalidOperationException("В секторе #"+sector+" недостаточно граничных стен.");
            foreach(int value in balance.Values)if(value!=0)throw new InvalidOperationException("Незамкнутая граница сектора #"+sector+". Сначала исправьте стены.");
            var triangles=fill.Triangles(sector);var seen=new HashSet<MapPoint>();double best=-1;MapPoint chosen=new MapPoint();
            Action<MapPoint> candidate=point=>
            {
                point=new MapPoint((float)Math.Round(point.x),(float)Math.Round(point.y));if(!seen.Add(point))return;
                double distance=double.MaxValue;
                foreach(var edge in edges)
                {
                    PlacementStep(work);
                    var a=map.vertices[edge.start].Position;var b=map.vertices[edge.end].Position;double dx=b.x-(double)a.x,dy=b.y-(double)a.y,length=dx*dx+dy*dy;
                    double t=length==0?0:Math.Max(0,Math.Min(1,((point.x-a.x)*dx+(point.y-a.y)*dy)/length));
                    double x=point.x-a.x-t*dx,y=point.y-a.y-t*dy;distance=Math.Min(distance,x*x+y*y);
                }
                if(distance>best&&distance>0&&map.Contains(sector,point)){best=distance;chosen=point;}
            };
            foreach(var triangle in triangles)
            {
                candidate(Midpoint(triangle.a,triangle.b));candidate(Midpoint(triangle.b,triangle.c));candidate(Midpoint(triangle.c,triangle.a));
            }
            // A triangular or very thin component may have no usable diagonal.
            if(best<0)foreach(var triangle in triangles)candidate(new MapPoint((triangle.a.x+triangle.b.x+triangle.c.x)/3,(triangle.a.y+triangle.b.y+triangle.c.y)/3));
            if(best<0)throw new InvalidOperationException("В секторе #"+sector+" нет подходящей целочисленной точки внутри. Проверьте границу.");return chosen;
        }
        static void PlacementStep(long[] work){if(++work[0]>2000000)throw new InvalidOperationException("Выделение слишком сложно для размещения по секторам. Используйте выбранные вершины или стены.");}
        static MapPoint Midpoint(MapPoint a,MapPoint b){return new MapPoint((a.x+b.x)*.5f,(a.y+b.y)*.5f);}
        public static ThingPlacementPlan Prepare(MapDocument map,IEnumerable<ElementRef> selection,MapThing template)
        {
            if(template==null)throw new ArgumentNullException("template");var points=Positions(map,selection);ThingClipboard.Properties(template,map.format);
            if(Math.Round(template.angle)!=template.angle||Math.Round(template.z)!=template.z)throw new InvalidOperationException("Угол и высота должны быть целыми.");
            if(map.format==MapFormat.Hexen)ThingClipboard.Coordinate(template.z);
            var copies=new List<MapThing>();foreach(var point in points)
            {var copy=ThingClipboard.Copy(template);copy.x=(float)Math.Round(point.x);copy.y=(float)Math.Round(point.y);ThingClipboard.Coordinate(copy.x);ThingClipboard.Coordinate(copy.y);copies.Add(copy);}
            var validation=map.Clone();validation.things.AddRange(copies);validation.Validate();return new ThingPlacementPlan(map,copies);
        }
    }
    public sealed class ThingPlacementPlan
    {
        readonly MapDocument original;readonly List<MapThing> copies;readonly byte[] state;bool used;
        public int Count {get{return copies.Count;}}
        internal ThingPlacementPlan(MapDocument map,List<MapThing> copies){original=map;this.copies=copies;state=BinaryMapState.Capture(map);}
        public List<int> Apply(MapDocument map)
        {
            if(used||!ReferenceEquals(map,original))throw new InvalidOperationException("Карта изменилась или размещение уже применено.");
            var current=BinaryMapState.Capture(map);if(current.Length!=state.Length)throw new InvalidOperationException("Карта изменилась. Откройте размещение снова.");
            for(int i=0;i<state.Length;i++)if(current[i]!=state[i])throw new InvalidOperationException("Карта изменилась. Откройте размещение снова.");
            var result=new List<int>();foreach(var copy in copies){result.Add(map.things.Count);map.things.Add(ThingClipboard.Copy(copy));}used=true;return result;
        }
    }
}
