using System;
using System.Collections.Generic;

namespace MobileUDB.Core
{
    public enum SelectionOperation { Replace, Add, Remove, Intersect }

    public static class MapSelection
    {
        public static int Count(MapDocument map,ElementKind kind)
        {
            switch(kind)
            {
                case ElementKind.Vertex: return map.vertices.Count;
                case ElementKind.Line: return map.lines.Count;
                case ElementKind.Sector: return map.sectors.Count;
                case ElementKind.Thing: return map.things.Count;
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }
        // Mirrors UDB's marquee modes: endpoints / sector bounds must be fully
        // enclosed, or a wall / sector boundary may intersect the rectangle.
        public static HashSet<ElementRef> Area(MapDocument map,ElementKind kind,MapPoint a,MapPoint b,bool touching)
        {
            var selected=new HashSet<ElementRef>();
            float left=Math.Min(a.x,b.x),right=Math.Max(a.x,b.x),bottom=Math.Min(a.y,b.y),top=Math.Max(a.y,b.y);
            if(left==right||bottom==top)return selected;
            Func<MapPoint,bool> contains=p=>p.x>=left&&p.x<=right&&p.y>=bottom&&p.y<=top;
            var lo=new MapPoint(left,bottom);var hi=new MapPoint(right,top);
            var br=new MapPoint(right,bottom);var tl=new MapPoint(left,top);
            Func<MapPoint,MapPoint,bool> hit=(p,q)=>contains(p)||contains(q)||
                MapDocument.SegmentsIntersect(p,q,lo,br)||MapDocument.SegmentsIntersect(p,q,br,hi)||
                MapDocument.SegmentsIntersect(p,q,hi,tl)||MapDocument.SegmentsIntersect(p,q,tl,lo);
            if(kind==ElementKind.Vertex||kind==ElementKind.Thing)
            {
                for(int i=0;i<Count(map,kind);i++)
                    if(contains(kind==ElementKind.Vertex?map.vertices[i].Position:map.things[i].Position))selected.Add(new ElementRef(kind,i));
                return selected;
            }
            if(kind!=ElementKind.Line&&kind!=ElementKind.Sector)throw new ArgumentOutOfRangeException(nameof(kind));
            if(kind==ElementKind.Line)
            {
                // Index iteration preserves line identity, including coincident lines.
                for(int i=0;i<map.lines.Count;i++)
                {
                    var line=map.lines[i];var p=map.vertices[line.start].Position;var q=map.vertices[line.end].Position;
                    if(contains(p)&&contains(q)||touching&&hit(p,q))selected.Add(new ElementRef(kind,i));
                }
            }
            else
            {
                var bounded=new bool[map.sectors.Count];var seen=new bool[map.sectors.Count];var crossed=new bool[map.sectors.Count];
                foreach(var line in map.lines)
                {
                    var p=map.vertices[line.start].Position;var q=map.vertices[line.end].Position;
                    bool enclosed=contains(p)&&contains(q),intersects=touching&&hit(p,q);
                    Accumulate(line.front);Accumulate(line.back);
                    void Accumulate(int side)
                    {
                        if(side<0)return;int sector=map.sides[side].sector;
                        bounded[sector]=(!seen[sector]||bounded[sector])&&enclosed;
                        seen[sector]=true;crossed[sector]|=intersects;
                    }
                }
                for(int i=0;i<map.sectors.Count;i++)if(seen[i]&&(bounded[i]||crossed[i]))selected.Add(new ElementRef(kind,i));
            }
            return selected;
        }
        public static void Apply(ISet<ElementRef> selection,IEnumerable<ElementRef> matches,SelectionOperation operation)
        {
            switch(operation)
            {
                case SelectionOperation.Replace: selection.Clear();selection.UnionWith(matches);break;
                case SelectionOperation.Add: selection.UnionWith(matches);break;
                case SelectionOperation.Remove: selection.ExceptWith(matches);break;
                case SelectionOperation.Intersect: selection.IntersectWith(matches);break;
                default: throw new ArgumentOutOfRangeException(nameof(operation));
            }
        }
    }
}
