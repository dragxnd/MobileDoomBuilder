// GPL-3.0. Shape construction follows UDB DrawRectangleMode / DrawEllipseMode.
using System;
using System.Collections.Generic;

namespace MobileUDB.Core
{
    public enum DrawShapeKind { None,Rectangle,Ellipse,Curve,Grid }
    [Serializable] public sealed class DrawShapeOptions
    {
        public int subdivisions,bevel,angle,segmentLength=32;
        public bool autoClose;
        public bool radial,continuous,guidelines,placeThings;
        public DrawShapeOptions Copy(){return (DrawShapeOptions)MemberwiseClone();}
        public void Validate(DrawShapeKind kind)
        {
            if(kind==DrawShapeKind.Curve){if(segmentLength<16||segmentLength>4096)throw new InvalidOperationException("Длина сегмента должна быть от 16 до 4096.");return;}
            if(kind!=DrawShapeKind.Rectangle&&kind!=DrawShapeKind.Ellipse)throw new InvalidOperationException("Выберите прямоугольник или эллипс.");
            int min=kind==DrawShapeKind.Rectangle?0:3,max=kind==DrawShapeKind.Rectangle?16:512;
            if(subdivisions<min||subdivisions>max)throw new InvalidOperationException("Число делений должно быть от "+min+" до "+max+".");
            if(bevel<-32768||bevel>32767||angle<-360||angle>360)throw new InvalidOperationException("Некорректные параметры фигуры.");
        }
    }
    public sealed class DrawShapeContour
    {
        public readonly List<MapPoint> points;
        public readonly MapPoint start,end;
        public readonly bool closed;
        internal DrawShapeContour(List<MapPoint> points,MapPoint start,MapPoint end,bool closed)
        {this.points=points;this.start=start;this.end=end;this.closed=closed;}
    }
    public static class DrawShapes
    {
        struct Point {public double x,y;public Point(double x,double y){this.x=x;this.y=y;}}
        static void Coordinate(double value)
        {if(double.IsNaN(value)||double.IsInfinity(value)||value<-32768||value>32767)throw new InvalidOperationException("Фигура выходит за границы Doom / Hexen.");}
        public static DrawShapeContour Build(MapPoint first,MapPoint second,DrawShapeKind kind,DrawShapeOptions options)
        {
            if(kind==DrawShapeKind.Curve)throw new InvalidOperationException("Кривая строится по списку опорных точек.");
            if(options==null)throw new ArgumentNullException("options");options.Validate(kind);
            Coordinate(first.x);Coordinate(first.y);Coordinate(second.x);Coordinate(second.y);
            double ax=first.x,ay=first.y;
            if(options.radial){ax=2*ax-second.x;ay=2*ay-second.y;Coordinate(ax);Coordinate(ay);}
            var start=new Point(Math.Min(ax,second.x),Math.Min(ay,second.y));var end=new Point(Math.Max(ax,second.x),Math.Max(ay,second.y));
            var raw=new List<Point>();bool closed=start.x!=end.x&&start.y!=end.y;
            int width=(int)Math.Round(end.x-start.x),height=(int)Math.Round(end.y-start.y);
            if(start.x==end.x&&start.y==end.y){}
            else if(!closed){raw.Add(start);raw.Add(end);}
            else if(kind==DrawShapeKind.Rectangle)
            {
                if(options.bevel==0){raw.Add(start);raw.Add(new Point(start.x,end.y));raw.Add(end);raw.Add(new Point(end.x,start.y));}
                else
                {
                    int bevel=Math.Min(Math.Abs(options.bevel),Math.Min(width,height)/2);if(options.bevel<0)bevel=-bevel;
                    bool reverse=options.bevel<0;
                    Corner(raw,start,bevel,bevel,!reverse,options);
                    Corner(raw,new Point(end.x,start.y),-bevel,bevel,reverse,options);
                    Corner(raw,end,-bevel,-bevel,!reverse,options);
                    Corner(raw,new Point(start.x,end.y),bevel,-bevel,reverse,options);
                }
            }
            else Ellipse(raw,start,end,width,height,options);
            var points=new List<MapPoint>();foreach(var p in raw)
            {
                var point=new MapPoint((float)p.x,(float)p.y);Coordinate(point.x);Coordinate(point.y);
                // Remove binary zero-length edges while retaining exact contacts until stitching.
                if(points.Count==0||!SameBinary(points[points.Count-1],point))points.Add(point);
            }
            if(closed&&points.Count>1&&SameBinary(points[0],points[points.Count-1]))points.RemoveAt(points.Count-1);
            if(closed&&points.Count<3)throw new InvalidOperationException("Фигура слишком мала для бинарной сетки. Увеличьте размер или уменьшите число вершин.");
            if(closed)
            {
                double area=0;for(int i=0;i<points.Count;i++){var a=MapDocument.Snap(points[i],1);var b=MapDocument.Snap(points[(i+1)%points.Count],1);area+=a.x*(double)b.y-b.x*(double)a.y;}
                if(Math.Abs(area)<1)throw new InvalidOperationException("Фигура слишком мала для бинарной сетки.");
            }
            return new DrawShapeContour(points,new MapPoint((float)start.x,(float)start.y),new MapPoint((float)end.x,(float)end.y),closed);
        }
        public static ThingInsertPlan PlanThings(MapDocument map,DrawShapeContour contour,int type)
        {
            ThingClipboard.Binary(map);if(contour==null||contour.points.Count<2)throw new InvalidOperationException("Укажите две точки фигуры.");
            if(type<0||type>65535)throw new InvalidOperationException("Тип объекта за пределами Doom / Hexen.");
            var things=new List<MapThing>();var used=new HashSet<Tuple<float,float>>();
            foreach(var point in contour.points)
            {var p=MapDocument.Snap(point,1);Coordinate(p.x);Coordinate(p.y);if(used.Add(Tuple.Create(p.x,p.y)))things.Add(new MapThing{x=p.x,y=p.y,type=type,flags=map.format==MapFormat.Hexen?2023:7});}
            return new ThingInsertPlan(map,things);
        }
        static bool SameBinary(MapPoint a,MapPoint b)
        {a=MapDocument.Snap(a,1);b=MapDocument.Snap(b,1);return a.x==b.x&&a.y==b.y;}
        static void Corner(List<Point> result,Point start,int bx,int by,bool reverse,DrawShapeOptions options)
        {
            var center=options.bevel>0?new Point(start.x+bx,start.y+by):start;var points=new List<Point>();
            double angle=Math.PI,step=Math.PI*.5/(options.subdivisions+1);
            for(int i=0;i<options.subdivisions+2;i++){points.Add(new Point(center.x+Math.Sin(angle)*bx,center.y+Math.Cos(angle)*by));angle+=step;}
            if(reverse)points.Reverse();result.AddRange(points);
        }
        static void Ellipse(List<Point> points,Point start,Point end,int width,int height,DrawShapeOptions options)
        {
            double bevel=options.subdivisions<6?0:options.bevel<0?-Math.Min(Math.Abs(options.bevel),Math.Min(width,height)/2)+1:options.bevel;
            double hw=width*.5,hh=height*.5,cx=start.x+hw,cy=start.y+hh;
            double angle=options.angle*Math.PI/180,step=-Math.PI*2/options.subdivisions;
            for(int i=0;i<options.subdivisions;i++){bool odd=(i&1)!=0;points.Add(new Point(cx-Math.Sin(angle)*(hw+(odd?bevel:0)),cy-Math.Cos(angle)*(hh+(odd?bevel:0))));angle+=step;}
            double minX=double.MaxValue,minY=double.MaxValue,maxX=double.MinValue,maxY=double.MinValue;
            foreach(var p in points){minX=Math.Min(minX,p.x);maxX=Math.Max(maxX,p.x);minY=Math.Min(minY,p.y);maxY=Math.Max(maxY,p.y);}
            if(maxX==minX||maxY==minY)throw new InvalidOperationException("Фигура слишком мала.");
            double sx=minX!=start.x||maxX!=end.x?(end.x-start.x)/(maxX-minX):1;
            // Also fit the upper extent: upstream only checks minY, which leaves
            // e.g. its three-vertex, zero-phase shape shorter than the requested box.
            double sy=minY!=start.y||maxY!=end.y?(end.y-start.y)/(maxY-minY):1;
            if(sx!=1||sy!=1)for(int i=0;i<points.Count;i++){var p=points[i];points[i]=new Point((cx-p.x)*sx+cx,(cy-p.y)*sy+cy);}
            minX=double.MaxValue;minY=double.MaxValue;foreach(var p in points){minX=Math.Min(minX,p.x);minY=Math.Min(minY,p.y);}
            double ox=start.x-minX,oy=start.y-minY;
            for(int i=0;i<points.Count;i++){var p=points[i];points[i]=new Point(p.x+ox,p.y+oy);}
        }
    }
}
