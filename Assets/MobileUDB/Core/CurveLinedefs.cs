// GPL-3.0. Circular-segment formulas from UDB CurveLinedefsMode (Pascal vd Heiden,
// Anders Astrand, mxd); binary splitting follows Linedef.Split / AdjustSplitCoordinates.
using System;
using System.Collections.Generic;
using System.IO;

namespace MobileUDB.Core
{
    public enum CurveSplitOffsets { Interpolate, CopyXY, ResetXCopyY, ResetXY }
    [Serializable] public sealed class CurveLinedefOptions
    {
        public int vertices=8,distance=128,angle=180;
        public bool fixedCurve,outwards=true;
        public CurveSplitOffsets offsets;
        public CurveLinedefOptions Copy(){return (CurveLinedefOptions)MemberwiseClone();}
        public void Validate()
        {
            if(vertices<1||vertices>200||distance< -10000||distance>10000||angle<0||angle>350||!Enum.IsDefined(typeof(CurveSplitOffsets),offsets))
                throw new InvalidOperationException("Вершины: 1–200, высота: −10000…10000, угол: 0–350°.");
        }
    }
    public sealed class CurvedLinedef
    {
        public readonly int line;
        public readonly CurvePoint start,end;
        public readonly IReadOnlyList<CurvePoint> points;
        internal CurvedLinedef(int line,CurvePoint start,CurvePoint end,List<CurvePoint> points)
        {this.line=line;this.start=start;this.end=end;this.points=points.AsReadOnly();}
    }
    public static class CurveLinedefs
    {
        // Same arithmetic and float segDelta as upstream GenerateCurve. Return raw
        // double points; binary accuracy is applied after splitting, not before.
        public static List<CurvePoint> Generate(CurvePoint start,CurvePoint end,CurveLinedefOptions options)
        {
            if(options==null)throw new ArgumentNullException("options");options.Validate();
            double dx=end.x-start.x,dy=end.y-start.y,length=Math.Sqrt(dx*dx+dy*dy);
            if(!Finite(length)||length==0)throw new InvalidOperationException("Стена имеет нулевую или некорректную длину.");
            bool fixedcurve=options.fixedCurve,fixedcurveoutwards=options.outwards;
            int vertices=Math.Min(options.vertices,(int)Math.Ceiling(length/4));int distance=options.distance;
            int angle=(!fixedcurve&&distance==0?Math.Max(5,options.angle):options.angle);
            double theta=angle/57.295779513082320876798154814105;
            if((!fixedcurve&&distance<0)||(fixedcurve&&fixedcurveoutwards))theta=-theta;
            var points=new List<CurvePoint>(vertices);float segDelta=1.0f/(vertices+1);
            var center=new CurvePoint(start.x+(end.x-start.x)*.5,start.y+(end.y-start.y)*.5);
            double lineAngle=-Math.Atan2(-dy,dx)+Math.PI*.5;if(lineAngle<0)lineAngle+=Math.PI*2;
            double rotation=lineAngle+Math.PI*.5,cos=Math.Cos(rotation),sin=Math.Sin(rotation);
            if(angle==0)
            {
                for(int v=1;v<=vertices;v++)
                {double x=(length*segDelta)*(vertices-v+1)-length*.5f;points.Add(new CurvePoint(cos*x+center.x,sin*x+center.y));}
            }
            else
            {
                double d=(length/Math.Tan(theta/2))/2,R=d/Math.Cos(theta/2),h=R-d;
                double yDeform=fixedcurve?1:distance/h,xDelta=Math.Min(1,yDeform);
                for(int v=1;v<=vertices;v++)
                {
                    double a=(Math.PI-theta)/2+v*(theta/(vertices+1));
                    double xr=Math.Cos(a)*R,xl=(length*segDelta)*(vertices-v+1)-length*.5f;
                    double x=xDelta*xr+(1.0f-xDelta)*xl,y=(Math.Sin(a)*R-d)*yDeform;
                    points.Add(new CurvePoint(cos*x-sin*y+center.x,sin*x+cos*y+center.y));
                }
            }
            foreach(var p in points)if(!Finite(p.x)||!Finite(p.y))throw new InvalidOperationException("Дуга имеет некорректные координаты.");
            return points;
        }
        static bool Finite(double n){return !double.IsNaN(n)&&!double.IsInfinity(n);}
        static CurvePoint Point(MapVertex vertex){return new CurvePoint(vertex.x,vertex.y);}
        static MapPoint BinaryPoint(CurvePoint point)
        {
            double x=Math.Round(point.x),y=Math.Round(point.y);
            if(x< -32768||x>32767||y< -32768||y>32767)throw new InvalidOperationException("Дуга выходит за пределы бинарных координат.");
            return new MapPoint((float)x,(float)y);
        }
        static MapSide CopySide(MapSide side)
        {
            var copy=new MapSide{sector=side.sector,offsetX=side.offsetX,offsetY=side.offsetY,upper=side.upper,lower=side.lower,middle=side.middle};
            side.CopyFieldsTo(copy);return copy;
        }
        static bool Texture(string name){return !string.IsNullOrEmpty(name)&&name!="-";}
        static bool Required(MapDocument map,MapSide side,MapSide other,bool upper,string sky)
        {
            if(other==null)return false;var opposite=map.sectors[other.sector];var own=map.sectors[side.sector];
            if(string.Equals(upper?opposite.ceilingTexture:opposite.floorTexture,sky,StringComparison.OrdinalIgnoreCase))return false;
            return upper?opposite.ceiling<own.ceiling:opposite.floor>own.floor;
        }
        static bool Adjusts(MapDocument map,MapSide side,MapSide other,bool front,string sky)
        {
            return (other==null&&(front||Texture(side.middle)))||(front&&Texture(side.middle))||Required(map,side,other,true,sky)||Required(map,side,other,false,sky);
        }
        static void Wrap(MapDocument map,MapSide side,MapSide other,DoomResources resources,string sky)
        {
            if(resources==null)return;
            // Native texture selection falls through when the higher-priority
            // required texture is absent from the resource catalog.
            var names=new[]{other==null&&Texture(side.middle)?side.middle:null,
                Required(map,side,other,true,sky)&&Texture(side.upper)?side.upper:null,
                Required(map,side,other,false,sky)&&Texture(side.lower)?side.lower:null};
            foreach(var name in names)if(name!=null){var texture=resources.Find(name,ResourceKind.Wall,true);if(texture!=null&&texture.width>0){side.offsetX%=texture.width;return;}}
        }
        internal static void Split(MapDocument map,int index,int vertex,CurvePoint a,CurvePoint middle,CurvePoint end,CurveLinedefOptions options,DoomResources resources,string sky)
        {
            var old=map.lines[index];var next=old.Clone();next.start=vertex;next.firstSideOrder=0;
            // Existing sides stay on the original piece. New sides attach front then back.
            if(old.front>=0){next.front=map.sides.Count;map.sides.Add(CopySide(map.sides[old.front]));}
            if(old.back>=0){next.back=map.sides.Count;map.sides.Add(CopySide(map.sides[old.back]));}
            old.end=vertex;map.lines.Add(next);
            var of=old.front<0?null:map.sides[old.front];var ob=old.back<0?null:map.sides[old.back];
            var nf=next.front<0?null:map.sides[next.front];var nb=next.back<0?null:map.sides[next.back];
            if(options.offsets==CurveSplitOffsets.Interpolate)
            {
                if(ob!=null&&Adjusts(map,ob,of,false,sky))ob.offsetX=checked(ob.offsetX+(int)CurvePoint.Distance(middle,end));
                if(nf!=null&&Adjusts(map,nf,nb,true,sky))nf.offsetX=checked(nf.offsetX+(int)CurvePoint.Distance(a,middle));
                if(nf!=null)Wrap(map,nf,nb,resources,sky);if(nb!=null)Wrap(map,nb,nf,resources,sky);
            }
            else if(options.offsets!=CurveSplitOffsets.CopyXY)
            {foreach(var side in new[]{nf,nb})if(side!=null){side.offsetX=0;if(options.offsets==CurveSplitOffsets.ResetXY)side.offsetY=0;}}
        }
        public static CurveLinedefPlan Prepare(MapDocument map,IEnumerable<ElementRef> selection,CurveLinedefOptions options,GeometryMergeMode mode,DoomResources resources=null,string sky="F_SKY1")
        {
            ThingClipboard.Binary(map);map.Validate();if(selection==null)throw new ArgumentNullException("selection");
            if(options==null)throw new ArgumentNullException("options");options=options.Copy();options.Validate();
            if(!Enum.IsDefined(typeof(GeometryMergeMode),mode))throw new InvalidOperationException("Неизвестный режим сшивки.");
            var ids=new List<int>();var seen=new HashSet<int>();foreach(var id in selection)
            {if(id.kind!=ElementKind.Line||id.index<0||id.index>=map.lines.Count)throw new InvalidOperationException("Выберите только существующие стены.");if(seen.Add(id.index))ids.Add(id.index);}
            if(ids.Count==0)throw new InvalidOperationException("Выберите стены для построения дуги.");
            var curves=new List<CurvedLinedef>();long count=0;foreach(int id in ids)
            {var line=map.lines[id];var a=Point(map.vertices[line.start]);var b=Point(map.vertices[line.end]);var points=Generate(a,b,options);foreach(var p in points)BinaryPoint(p);count+=points.Count;curves.Add(new CurvedLinedef(id,a,b,points));}
            if(count+map.vertices.Count>65535||count+map.lines.Count>65535)throw new InvalidOperationException("Слишком много вершин дуги для бинарной карты.");
            var prepared=map.Clone();var moving=new HashSet<int>();var selected=new HashSet<ElementRef>();
            // Shared raw SIDEDEFS are legal in binary maps; isolate each touched line
            // before changing its offsets, without modifying unselected references.
            foreach(var curve in curves)
            {
                var line=prepared.lines[curve.line];if(line.front>=0){var copy=CopySide(prepared.sides[line.front]);line.front=prepared.sides.Count;prepared.sides.Add(copy);}
                if(line.back>=0){var copy=CopySide(prepared.sides[line.back]);line.back=prepared.sides.Count;prepared.sides.Add(copy);}
                moving.Add(line.start);moving.Add(line.end);selected.Add(new ElementRef(ElementKind.Line,curve.line));
                int split=curve.line;var a=curve.start;foreach(var point in curve.points)
                {
                    int vertex=prepared.vertices.Count;prepared.vertices.Add(new MapVertex((float)point.x,(float)point.y));moving.Add(vertex);
                    Split(prepared,split,vertex,a,point,curve.end,options,resources,sky);split=prepared.lines.Count-1;selected.Add(new ElementRef(ElementKind.Line,split));a=point;
                }
            }
            // UDB marks original endpoints and every split vertex before StitchGeometry.
            var result=MapStitch.StitchMoved(map,prepared,moving,new HashSet<int>(),selected,mode,resources);
            // Upstream stores integer offsets and writes Int16 SIDEDEFS. Keep the
            // binary result exportable after large split-offset increments.
            foreach(var side in prepared.sides){side.offsetX=unchecked((short)side.offsetX);side.offsetY=unchecked((short)side.offsetY);}
            prepared.Validate();if(prepared.sides.Count>65535||prepared.sectors.Count>65535)throw new InvalidOperationException("Превышен лимит бинарной карты.");
            return new CurveLinedefPlan(map,prepared,curves,result,(int)count);
        }
    }
    public sealed class CurveLinedefPlan
    {
        readonly MapDocument original,prepared;readonly byte[] state;readonly HashSet<ElementRef> selection;bool applied;
        public readonly IReadOnlyList<CurvedLinedef> curves;public readonly int AddedVertices;
        internal CurveLinedefPlan(MapDocument original,MapDocument prepared,List<CurvedLinedef> curves,HashSet<ElementRef> selection,int added)
        {this.original=original;this.prepared=prepared;this.curves=curves.AsReadOnly();this.selection=selection;AddedVertices=added;state=BinaryMapState.Capture(original);}
        public HashSet<ElementRef> Apply(MapDocument map)
        {
            if(applied||!ReferenceEquals(map,original))throw new InvalidOperationException("Предпросмотр относится к другой карте или уже применён.");
            byte[] current=BinaryMapState.Capture(map);if(current.Length!=state.Length)throw new InvalidOperationException("Карта изменилась. Постройте дугу снова.");
            for(int i=0;i<current.Length;i++)if(current[i]!=state[i])throw new InvalidOperationException("Карта изменилась. Постройте дугу снова.");
            map.vertices=prepared.vertices;map.lines=prepared.lines;map.sides=prepared.sides;map.sectors=prepared.sectors;applied=true;
            return new HashSet<ElementRef>(selection);
        }
    }
}

