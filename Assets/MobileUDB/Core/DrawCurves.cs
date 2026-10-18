using System;
using System.Collections.Generic;

namespace MobileUDB.Core
{
    // Minimal double-precision vector for the unchanged upstream curve mathematics.
    public struct CurvePoint : IEquatable<CurvePoint>
    {
        public double x,y;
        public CurvePoint(double x,double y){this.x=x;this.y=y;}
        public static CurvePoint operator +(CurvePoint a,CurvePoint b){return new CurvePoint(a.x+b.x,a.y+b.y);}
        public static CurvePoint operator -(CurvePoint a,CurvePoint b){return new CurvePoint(a.x-b.x,a.y-b.y);}
        public static CurvePoint operator *(CurvePoint a,double value){return new CurvePoint(a.x*value,a.y*value);}
        public static bool operator ==(CurvePoint a,CurvePoint b){return a.Equals(b);}
        public static bool operator !=(CurvePoint a,CurvePoint b){return !a.Equals(b);}
        public bool Equals(CurvePoint other){return x==other.x&&y==other.y;}
        public override bool Equals(object other){return other is CurvePoint&&Equals((CurvePoint)other);}
        public override int GetHashCode(){return x.GetHashCode()*397^y.GetHashCode();}
        public double GetLength(){return Math.Sqrt(x*x+y*y);}
        public CurvePoint GetNormal(){double square=x*x+y*y;if(square>0.0000000001f){double mul=1/Math.Sqrt(square);return new CurvePoint(x*mul,y*mul);}return new CurvePoint();}
        public CurvePoint GetRotated(double angle){return new CurvePoint(x*Math.Cos(angle)-y*Math.Sin(angle),x*Math.Sin(angle)+y*Math.Cos(angle));}
        public static double Distance(CurvePoint a,CurvePoint b){return (a-b).GetLength();}
    }
    public static class DrawCurves
    {
        static void Coordinate(float value)
        {if(float.IsNaN(value)||float.IsInfinity(value)||value<-32768||value>32767)throw new InvalidOperationException("Кривая выходит за границы Doom / Hexen.");}
        static bool Same(MapPoint a,MapPoint b){return a.x==b.x&&a.y==b.y;}
        public static DrawShapeContour Build(IList<MapPoint> anchors,DrawShapeOptions options,bool close=false)
        {
            if(anchors==null||anchors.Count<2)throw new InvalidOperationException("Укажите минимум две точки кривой.");
            options.Validate(DrawShapeKind.Curve);var input=new List<CurvePoint>();
            foreach(var point in anchors){Coordinate(point.x);Coordinate(point.y);if(input.Count==0||input[input.Count-1]!=new CurvePoint(point.x,point.y))input.Add(new CurvePoint(point.x,point.y));}
            if(input.Count<2)throw new InvalidOperationException("Укажите две разные точки кривой.");
            bool closed=close||input.Count>2&&input[0]==input[input.Count-1];
            if(closed){if(input[0]!=input[input.Count-1])input.Add(input[0]);if(input.Count<4)throw new InvalidOperationException("Для замкнутой кривой нужно три точки.");}
            long estimated=1;for(int i=1;i<input.Count;i++)estimated+=Math.Max(1,(int)Math.Round(CurvePoint.Distance(input[i-1],input[i])/options.segmentLength));
            if(estimated>65535)throw new InvalidOperationException("Кривая слишком подробная. Увеличьте длину сегмента.");
            var curve=CurveTools.CurveThroughPoints(input,.5f,.75f,options.segmentLength);var points=new List<MapPoint>();
            foreach(var segment in curve.Segments)
            {
                for(int i=0;i<segment.Points.Length;i++)
                {
                    // Preserve exact anchor contacts despite floating parameter accumulation.
                    var p=i==0?segment.Start:i==segment.Points.Length-1?segment.End:segment.Points[i];
                    var point=new MapPoint((float)p.x,(float)p.y);Coordinate(point.x);Coordinate(point.y);
                    if(points.Count==0||!Same(MapDocument.Snap(points[points.Count-1],1),MapDocument.Snap(point,1)))points.Add(point);
                }
            }
            if(closed&&points.Count>1&&Same(MapDocument.Snap(points[0],1),MapDocument.Snap(points[points.Count-1],1)))points.RemoveAt(points.Count-1);
            if(points.Count<(closed?3:2))throw new InvalidOperationException("Кривая слишком мала для бинарной сетки.");
            var lo=points[0];var hi=lo;foreach(var p in points){lo.x=Math.Min(lo.x,p.x);lo.y=Math.Min(lo.y,p.y);hi.x=Math.Max(hi.x,p.x);hi.y=Math.Max(hi.y,p.y);}
            return new DrawShapeContour(points,lo,hi,closed);
        }
        static bool OnGeometry(MapDocument map,MapPoint point)
        {
            foreach(var line in map.lines)
            {
                var a=map.vertices[line.start].Position;var b=map.vertices[line.end].Position;double dx=b.x-(double)a.x,dy=b.y-(double)a.y,length=dx*dx+dy*dy;
                if(length==0)continue;double t=((point.x-a.x)*dx+(point.y-a.y)*dy)/length;if(t<0||t>1)continue;
                double x=point.x-a.x-dx*t,y=point.y-a.y-dy*t;if(x*x+y*y<.000001)return true;
            }
            return false;
        }
        public static CurveDrawingPlan Prepare(MapDocument map,DrawShapeContour contour,DoomResources resources=null,DrawTextureOptions textures=null)
        {
            ThingClipboard.Binary(map);if(contour==null)throw new ArgumentNullException("contour");
            var result=MapStitch.DrawLines(map,contour.points,contour.closed,resources,textures);result.map.Validate();
            bool autoClose=!contour.closed&&OnGeometry(map,contour.points[0])&&OnGeometry(map,contour.points[contour.points.Count-1])&&result.map.sectors.Count>map.sectors.Count;
            return new CurveDrawingPlan(map,result,autoClose);
        }
        public static CurveDrawingPlan PrepareAutoClose(MapDocument map,DrawShapeContour contour,DoomResources resources=null,DrawTextureOptions textures=null)
        {
            if(contour==null||contour.closed||!OnGeometry(map,contour.points[0])||!OnGeometry(map,contour.points[contour.points.Count-1]))return null;
            var plan=Prepare(map,contour,resources,textures);return plan.CanAutoClose?plan:null;
        }
    }
    public sealed class CurveDrawingPlan
    {
        readonly MapDocument original;readonly MapStitchResult result;bool applied;
        public readonly bool CanAutoClose;
        internal CurveDrawingPlan(MapDocument original,MapStitchResult result,bool canAutoClose){this.original=original;this.result=result;CanAutoClose=canAutoClose;}
        public MapStitchResult Apply(MapDocument map)
        {
            if(applied||!ReferenceEquals(map,original))throw new InvalidOperationException("Карта изменилась. Повторите рисование кривой.");
            applied=true;map.vertices=result.map.vertices;map.lines=result.map.lines;map.sides=result.map.sides;map.sectors=result.map.sectors;return result;
        }
    }
}
