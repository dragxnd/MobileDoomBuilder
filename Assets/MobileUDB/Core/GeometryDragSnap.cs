// GPL-3.0. Adapted from UDB DragGeometryMode, MapSet nearest queries and
// Linedef.GetGridIntersections (Pascal vd Heiden and contributors).
using System;
using System.Collections.Generic;

namespace MobileUDB.Core
{
    public sealed class GeometryDragSnap
    {
        readonly MapDocument origin;
        readonly GridFrame grid;
        readonly HashSet<int> moving;
        readonly List<int> fixedLines = new List<int>();
        readonly MapPoint anchor;
        readonly CurvePoint incrementOffset;
        float minX=float.MaxValue,minY=float.MaxValue,maxX=float.MinValue,maxY=float.MinValue;
        int cachedLine=-1;
        List<CurvePoint> intersections;
        public bool HasGeometry { get { return moving.Count > 0; } }
        public bool Merge { get { return HasGeometry && !grid.disableNearbySnap; } }
        public GeometryDragSnap(MapDocument map,HashSet<ElementRef> selection,MapPoint pointer,GridFrame frame)
        {
            ThingClipboard.Binary(map);
            origin=map;grid=frame.DragSettings();moving=map.SelectedVertices(selection);
            anchor=grid.NearestAnchor(map,selection,pointer);
            incrementOffset=grid.SnapRaw(new CurvePoint(anchor.x,anchor.y))-new CurvePoint(anchor.x,anchor.y);
            foreach(int index in moving)Include(map.vertices[index].Position);
            foreach(var item in selection)if(item.kind==ElementKind.Thing)Include(map.things[item.index].Position);
            for(int i=0;i<map.lines.Count;i++)if(!moving.Contains(map.lines[i].start)&&!moving.Contains(map.lines[i].end))fixedLines.Add(i);
        }
        void Include(MapPoint p) { minX=Math.Min(minX,p.x);maxX=Math.Max(maxX,p.x);minY=Math.Min(minY,p.y);maxY=Math.Max(maxY,p.y); }
        public MapPoint Offset(MapPoint delta,double range)
        {
            if(float.IsNaN(delta.x)||float.IsNaN(delta.y)||float.IsInfinity(delta.x)||float.IsInfinity(delta.y)||double.IsNaN(range)||double.IsInfinity(range)||range<0)
                throw new InvalidOperationException("Некорректное перемещение или радиус привязки.");
            delta=grid.ConstrainDragDelta(delta);
            var point=new CurvePoint(anchor.x+(double)delta.x,anchor.y+(double)delta.y);
            CurvePoint? target=null;
            if(Merge)
            {
                // The original square query ranks by Manhattan distance, with
                // first-in-map ties. Vertices take precedence over any wall.
                double best=double.MaxValue;
                float left=(float)(point.x-range),right=(float)(point.x+range),bottom=(float)(point.y-range),top=(float)(point.y+range);
                for(int i=0;i<origin.vertices.Count;i++)if(!moving.Contains(i))
                {
                    var v=origin.vertices[i];if(v.x<left||v.x>right||v.y<bottom||v.y>top)continue;
                    double distance=Math.Abs(v.x-point.x)+Math.Abs(v.y-point.y);
                    if(distance<best){best=distance;target=new CurvePoint(v.x,v.y);}
                }
                if(!target.HasValue)
                {
                    int line=-1;best=double.MaxValue;
                    foreach(int index in fixedLines)
                    {
                        var edge=origin.lines[index];var a=origin.vertices[edge.start].Position;var b=origin.vertices[edge.end].Position;
                        double distance=SafeDistance(a,b,point);
                        if(distance<best&&distance<=range*range){best=distance;line=index;}
                    }
                    if(line>=0)
                    {
                        var edge=origin.lines[line];var a=origin.vertices[edge.start].Position;var b=origin.vertices[edge.end].Position;
                        if(grid.snap)
                        {
                            if(cachedLine!=line){intersections=GridIntersections(a,b,grid,grid.increment?incrementOffset:new CurvePoint());cachedLine=line;}
                            best=double.MaxValue;
                            foreach(var candidate in intersections){double x=candidate.x-point.x,y=candidate.y-point.y,d=x*x+y*y;if(d<best){best=d;target=candidate;}}
                            // No grid crossing: the original falls back to the
                            // ordinary anchor/grid snap, rather than projection.
                        }
                        else target=Projection(a,b,point);
                    }
                }
            }
            var result=target.HasValue?new MapPoint((float)(target.Value.x-anchor.x),(float)(target.Value.y-anchor.y)):grid.MoveOffset(anchor,delta);
            if(minX!=float.MaxValue){result.x=Math.Max(-32768-minX,Math.Min(32767-maxX,result.x));result.y=Math.Max(-32768-minY,Math.Min(32767-maxY,result.y));}
            return result;
        }
        static CurvePoint Projection(MapPoint a,MapPoint b,CurvePoint point)
        {
            double x=b.x-(double)a.x,y=b.y-(double)a.y,length=x*x+y*y;
            double u=length==0?0:Math.Max(0,Math.Min(1,((point.x-a.x)*x+(point.y-a.y)*y)/length));
            return new CurvePoint(a.x+x*u,a.y+y*u);
        }
        static double SafeDistance(MapPoint a,MapPoint b,CurvePoint point)
        {
            double x=b.x-(double)a.x,y=b.y-(double)a.y,length=x*x+y*y;if(length==0)return double.NaN;
            double inverse=1/Math.Sqrt(length),u=((point.x-a.x)*x+(point.y-a.y)*y)/length;
            u=inverse>1?Math.Max(0,Math.Min(1,u)):Math.Max(inverse,Math.Min(1-inverse,u));
            double dx=point.x-(a.x+x*u),dy=point.y-(a.y+y*u);return dx*dx+dy*dy;
        }
        // Same enumeration/order as native GetGridIntersections. Cache only the
        // last candidate wall, avoiding a new large list on each pointer frame.
        public static List<CurvePoint> GridIntersections(MapPoint start,MapPoint end,GridFrame frame,CurvePoint offset)
        {
            frame.Validate();var a=new CurvePoint(start.x,start.y);var b=new CurvePoint(end.x,end.y);
            bool transformed=Math.Abs(frame.rotation)>1e-4||Math.Abs(frame.originX)>1e-4||Math.Abs(frame.originY)>1e-4;
            if(transformed){a=(a-new CurvePoint(frame.originX,frame.originY)).GetRotated(-frame.rotation);b=(b-new CurvePoint(frame.originX,frame.originY)).GetRotated(-frame.rotation);}
            var result=new List<CurvePoint>();double minX=Math.Min(a.x,b.x),maxX=Math.Max(a.x,b.x),minY=Math.Min(a.y,b.y),maxY=Math.Max(a.y,b.y),inverse=1f/(double)frame.step;
            for(double gx=Math.Round((minX+frame.step*.5)*inverse)*frame.step+offset.x;gx<maxX;gx+=frame.step)
            {double u=(gx-minX)/(maxX-minX);if(a.x>b.x)u=1-u;result.Add(new CurvePoint(gx,a.y+(b.y-a.y)*u));}
            for(double gy=Math.Round((minY+frame.step*.5)*inverse)*frame.step+offset.y;gy<maxY;gy+=frame.step)
            {double u=(gy-minY)/(maxY-minY);if(a.y>b.y)u=1-u;result.Add(new CurvePoint(a.x+(b.x-a.x)*u,gy));}
            if(transformed)for(int i=0;i<result.Count;i++)result[i]=result[i].GetRotated(frame.rotation)+new CurvePoint(frame.originX,frame.originY);
            return result;
        }
    }
}
