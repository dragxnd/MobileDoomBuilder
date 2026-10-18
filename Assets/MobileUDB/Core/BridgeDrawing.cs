// GPL-3.0. BridgeMode and CurveTools, adapted from Ultimate Doom Builder.
using System;
using System.Collections.Generic;
using System.Globalization;

namespace MobileUDB.Core
{
    public enum BridgeHeightMode { Linear,Extreme,EaseInSine,EaseOutSine,EaseInOutSine }
    public enum BridgeLightMode { Linear,Highest,Lowest }
    public enum BridgeHandleMode { Independent,Mirror,Copy }
    [Serializable] public sealed class BridgeOptions
    {
        public int subdivisions=5;
        public BridgeHeightMode floor,ceiling;
        public BridgeLightMode light;
        public BridgeHandleMode handles;
        public BridgeOptions Copy(){return (BridgeOptions)MemberwiseClone();}
        public void Validate()
        {if(subdivisions<0||subdivisions>32||(int)floor<0||(int)floor>4||(int)ceiling<0||(int)ceiling>4||(int)light<0||(int)light>2||(int)handles<0||(int)handles>2)throw new InvalidOperationException("Деления моста: 0–32. Выберите допустимые параметры перехода.");}
    }
    public struct BridgeProperties
    {public int floor,ceiling,light;public string lower,upper;}
    public sealed class BridgeCell
    {
        public readonly int row,column;
        public readonly IReadOnlyList<CurvePoint> points;
        public readonly BridgeProperties properties;
        internal BridgeCell(int row,int column,CurvePoint[] points,BridgeProperties properties){this.row=row;this.column=column;this.points=Array.AsReadOnly(points);this.properties=properties;}
    }
    public sealed class BridgeGeometry
    {
        public readonly IReadOnlyList<IReadOnlyList<CurvePoint>> curves;
        public readonly IReadOnlyList<BridgeCell> cells;
        public readonly IReadOnlyList<CurvePoint> handles,anchors;
        public readonly int subdivisions;
        internal readonly BridgeDrawing owner;
        internal BridgeGeometry(BridgeDrawing owner,List<IReadOnlyList<CurvePoint>> curves,List<BridgeCell> cells,CurvePoint[] handles,CurvePoint[] anchors,int subdivisions)
        {this.owner=owner;this.curves=curves.AsReadOnly();this.cells=cells.AsReadOnly();this.handles=Array.AsReadOnly((CurvePoint[])handles.Clone());this.anchors=Array.AsReadOnly((CurvePoint[])anchors.Clone());this.subdivisions=subdivisions;}
    }
    public sealed class BridgeDrawing
    {
        sealed class Line
        {
            public CurvePoint start,end;public BridgeProperties properties;public Line previous,next;
            public Line(MapDocument map,int index)
            {
                var l=map.lines[index];var a=map.vertices[l.start];var b=map.vertices[l.end];start=new CurvePoint((int)a.x,(int)a.y);end=new CurvePoint((int)b.x,(int)b.y);
                properties=new BridgeProperties{floor=0,ceiling=128,light=192,lower="-",upper="-"};
                int side=l.back>=0?l.back:l.front;
                if(side>=0){var sd=map.sides[side];var sector=map.sectors[sd.sector];properties.floor=(int)sector.floor;properties.ceiling=(int)sector.ceiling;properties.light=sector.light;properties.upper=sd.upper!="-"?sd.upper:sd.middle;properties.lower=sd.lower!="-"?sd.lower:sd.middle;}
            }
            public void Invert(){var p=start;start=end;end=p;}
        }
        readonly MapDocument original;readonly byte[] state;
        CurvePoint[] first,second,anchors,handles;
        BridgeProperties[] properties1,properties2;
        double[] lengths1,lengths2;
        BridgeDrawing(MapDocument map){original=map;state=BinaryMapState.Capture(map);}
        public int Rows {get{return first.Length-1;}}
        public CurvePoint Handle(int index){return handles[index];}
        public CurvePoint Anchor(int index){return anchors[index];}
        public BridgeDrawing Copy()
        {var copy=(BridgeDrawing)MemberwiseClone();copy.first=(CurvePoint[])first.Clone();copy.second=(CurvePoint[])second.Clone();copy.properties1=(BridgeProperties[])properties1.Clone();copy.properties2=(BridgeProperties[])properties2.Clone();copy.anchors=(CurvePoint[])anchors.Clone();copy.handles=(CurvePoint[])handles.Clone();copy.lengths1=(double[])lengths1.Clone();copy.lengths2=(double[])lengths2.Clone();return copy;}
        public void CheckCurrent(MapDocument map)
        {
            if(!ReferenceEquals(original,map))throw new InvalidOperationException("Предпросмотр моста относится к другой карте.");var current=BinaryMapState.Capture(map);
            if(current.Length!=state.Length)throw new InvalidOperationException("Карта изменилась. Постройте мост снова.");for(int i=0;i<state.Length;i++)if(state[i]!=current[i])throw new InvalidOperationException("Карта изменилась. Постройте мост снова.");
        }
        public static BridgeDrawing Setup(MapDocument map,IList<int> selection)
        {
            ThingClipboard.Binary(map);map.Validate();if(selection==null||selection.Count<2)throw new InvalidOperationException("Выберите две отдельные цепочки стен с одинаковым числом звеньев.");
            var result=new BridgeDrawing(map);var lines=new List<Line>();var used=new HashSet<int>();var incident=new Dictionary<CurvePoint,List<int>>();
            foreach(int index in selection)
            {
                if(index<0||index>=map.lines.Count||!used.Add(index))throw new InvalidOperationException("Выберите существующие стены без повторов.");
                var line=new Line(map,index);if(line.start==line.end)throw new InvalidOperationException("В цепочке есть совпадающие точки.");int n=lines.Count;lines.Add(line);
                foreach(var p in new[]{line.start,line.end}){List<int> indices;if(!incident.TryGetValue(p,out indices)){indices=new List<int>();incident.Add(p,indices);}indices.Add(n);if(indices.Count>2)throw new InvalidOperationException("Цепочки моста не должны разветвляться.");}
            }
            // Source pairing order, visiting only incident lines instead of all N².
            for(int i=0;i<lines.Count;i++)
            {
                var current=lines[i];var neighbours=new SortedSet<int>(incident[current.start]);neighbours.UnionWith(incident[current.end]);
                foreach(int j in neighbours)if(j!=i)
                {var line=lines[j];if(current.start==line.start){line.Invert();current.previous=line;}else if(current.start==line.end)current.previous=line;else if(current.end==line.end){line.Invert();current.next=line;}else if(current.end==line.start)current.next=line;}
            }
            var groups=new List<List<CurvePoint>>();var sorted=new List<List<Line>>();var visited=new HashSet<Line>();
            foreach(var line in lines)if(line.previous==null)
            {
                var points=new List<CurvePoint>{line.start};var chain=new List<Line>();var next=line;
                while(next!=null){if(!visited.Add(next))throw new InvalidOperationException("Цепочки моста замкнуты или пересекаются по вершинам.");points.Add(next.end);chain.Add(next);next=next.next;}
                groups.Add(points);sorted.Add(chain);
            }
            if(groups.Count!=2||visited.Count!=lines.Count)throw new InvalidOperationException("Нужны ровно две открытые цепочки стен.");
            if(groups[0].Count!=groups[1].Count)throw new InvalidOperationException("В двух цепочках должно быть одинаковое число звеньев.");
            foreach(var a in sorted[0])foreach(var b in sorted[1])if(Intersects(a.start,a.end,b.start,b.end))throw new InvalidOperationException("Цепочки моста пересекаются.");
            result.properties1=new BridgeProperties[sorted[0].Count];result.properties2=new BridgeProperties[sorted[1].Count];
            for(int i=0;i<result.properties1.Length;i++){result.properties1[i]=sorted[0][i].properties;result.properties2[i]=sorted[1][i].properties;}
            if(Intersects(groups[0][0],groups[1][0],groups[0][groups[0].Count-1],groups[1][groups[1].Count-1])){groups[0].Reverse();Array.Reverse(result.properties1);}
            result.first=groups[0].ToArray();result.second=groups[1].ToArray();int last=result.first.Length-1;
            var center1=PointOnLine(result.first[0],result.first[last],.5f);var center2=PointOnLine(result.second[0],result.second[last],.5f);
            var location1=HandleLocation(result.first[0],result.first[last],center2);var location2=HandleLocation(result.second[0],result.second[last],center1);
            result.anchors=new[]{result.first[0],result.second[0],result.first[last],result.second[last]};
            result.handles=new[]{result.anchors[0]+location1,result.anchors[1]+location2,result.anchors[2]+location1,result.anchors[3]+location2};
            result.lengths1=RelativeLengths(result.first);result.lengths2=RelativeLengths(result.second);return result;
        }
        static bool Intersects(CurvePoint a,CurvePoint b,CurvePoint c,CurvePoint d)
        {double zn=(d.y-c.y)*(b.x-a.x)-(d.x-c.x)*(b.y-a.y),ch1=(d.x-c.x)*(a.y-c.y)-(d.y-c.y)*(a.x-c.x),ch2=(b.x-a.x)*(a.y-c.y)-(b.y-a.y)*(a.x-c.x);return zn!=0&&ch1/zn<=1&&ch1/zn>=0&&ch2/zn<=1&&ch2/zn>=0;}
        public static CurvePoint PointOnLine(CurvePoint a,CurvePoint b,double t)
        {return new CurvePoint((int)((1f-t)*a.x+t*b.x),(int)((1f-t)*a.y+t*b.y));}
        static CurvePoint HandleLocation(CurvePoint start,CurvePoint end,CurvePoint direction)
        {
            double angle=-Math.Atan2(start.y-end.y,start.x-end.x),dirAngle=-Math.Atan2(direction.y-start.y,direction.x-start.x);
            double length=Math.Sqrt(Math.Pow(Math.Abs(start.x-end.x),2.0)+Math.Pow(Math.Abs(start.y-end.y),2.0))*.3f;
            double diff=(angle+Math.PI)-(dirAngle+Math.PI);if(diff>Math.PI||(diff<0&&diff> -Math.PI))angle+=Math.PI;return new CurvePoint(Math.Sin(angle)*length,Math.Cos(angle)*length);
        }
        static double[] RelativeLengths(CurvePoint[] points)
        {
            var result=new double[points.Length];double length=CurvePoint.Distance(points[0],points[points.Length-1]);if(length==0)throw new InvalidOperationException("Концы цепочки моста совпадают.");
            double angle=Math.Atan2(points[0].y-points[points.Length-1].y,points[0].x-points[points.Length-1].x);
            for(int i=1;i<points.Length-1;i++){var a=points[i-1];var b=points[i];double curAngle=Math.Atan2(a.y-b.y,a.x-b.x),diff=(angle+Math.PI)-(curAngle+Math.PI),segment=(int)(CurvePoint.Distance(a,b)*Math.Cos(diff));result[i]=result[i-1]+segment/length;}
            result[result.Length-1]=1f;return result;
        }
        static void Coordinate(CurvePoint p)
        {if(double.IsNaN(p.x)||double.IsNaN(p.y)||double.IsInfinity(p.x)||double.IsInfinity(p.y)||p.x< -32768||p.x>32767||p.y< -32768||p.y>32767)throw new InvalidOperationException("Мост выходит за границы Doom / Hexen.");}
        public void MoveHandle(int index,CurvePoint position,BridgeHandleMode mode)
        {
            if(index<0||index>3||(int)mode<0||(int)mode>2)throw new ArgumentOutOfRangeException();Coordinate(position);int paired=(index+2)%4;
            var relative=position-anchors[index];var pair=handles[paired];
            if(mode==BridgeHandleMode.Mirror){double angle=Math.Atan2(-relative.y,-relative.x)+Math.PI*.5,dirAngle=-Math.Atan2(anchors[paired].y-anchors[index].y,anchors[paired].x-anchors[index].x),length=Math.Sqrt(Math.Pow(Math.Abs(relative.x),2.0)+Math.Pow(Math.Abs(relative.y),2.0)),mirrored=angle+dirAngle*2.0;pair=anchors[paired]+new CurvePoint(Math.Sin(mirrored)*length,Math.Cos(mirrored)*length);}
            else if(mode==BridgeHandleMode.Copy)pair=anchors[paired]+relative;
            Coordinate(pair);handles[index]=position;handles[paired]=pair;
        }
        public void Flip()
        {Array.Reverse(first);Array.Reverse(properties1);var p=handles[0];handles[0]=handles[2];handles[2]=p;/* Source keeps controlled points / relative-length samples when flipping. */}
        static IReadOnlyList<CurvePoint> Cubic(CurvePoint a,CurvePoint b,CurvePoint c,CurvePoint d,int steps)
        {
            var result=new List<CurvePoint>();double step=1f/steps,t=0f;
            for(int i=0;i<=steps;i++){double inv=1f-t,m1=inv*inv*inv,m2=3*t*inv*inv,m3=3*t*t*inv,m4=t*t*t;var p=new CurvePoint(m1*a.x+m2*c.x+m3*d.x+m4*b.x,m1*a.y+m2*c.y+m3*d.y+m4*b.y);Coordinate(p);result.Add(p);t+=step;}return result.AsReadOnly();
        }
        public static int Interpolate(int a,int b,float delta,BridgeHeightMode mode,bool ceiling)
        {
            if(mode==BridgeHeightMode.Extreme)return ceiling?Math.Max(a,b):Math.Min(a,b);
            // Source Linear selects the float overload; sine modes select double.
            if(mode==BridgeHeightMode.Linear)return (int)Math.Round(delta*b+(1.0f-delta)*a);
            var m=mode==BridgeHeightMode.EaseInSine?GradientInterpolation.EaseInSine:mode==BridgeHeightMode.EaseOutSine?GradientInterpolation.EaseOutSine:GradientInterpolation.EaseInOutSine;
            return (int)Math.Round(SectorGradients.Interpolate(a,b,delta,m));
        }
        public BridgeGeometry Build(BridgeOptions options)
        {
            options.Validate();var curves=new List<IReadOnlyList<CurvePoint>>();var cells=new List<BridgeCell>();
            for(int i=0;i<first.Length;i++)curves.Add(Cubic(first[i],second[i],PointOnLine(handles[0],handles[2],lengths1[i]),PointOnLine(handles[1],handles[3],lengths2[i]),options.subdivisions));
            for(int row=0;row<Rows;row++)for(int column=0;column<options.subdivisions;column++)
            {
                float delta=column/(float)options.subdivisions;delta+=(1.0f-delta)/options.subdivisions;var a=properties1[row];var b=properties2[row];
                var properties=new BridgeProperties{floor=Interpolate(a.floor,b.floor,delta,options.floor,false),ceiling=Interpolate(a.ceiling,b.ceiling,delta,options.ceiling,true),light=options.light==BridgeLightMode.Highest?Math.Max(a.light,b.light):options.light==BridgeLightMode.Lowest?Math.Min(a.light,b.light):Interpolate(a.light,b.light,delta,BridgeHeightMode.Linear,false),lower=a.lower!="-"?a.lower:b.lower,upper=a.upper!="-"?a.upper:b.upper};
                var p0=curves[row][column];cells.Add(new BridgeCell(row,column,new[]{p0,curves[row][column+1],curves[row+1][column+1],curves[row+1][column],p0},properties));
            }
            return new BridgeGeometry(this,curves,cells,handles,new[]{first[0],second[0],first[first.Length-1],second[second.Length-1]},options.subdivisions);
        }
        public BridgePlan Plan(MapDocument map,BridgeGeometry geometry,DoomResources resources=null,string skyFlat="F_SKY1")
        {
            CheckCurrent(map);if(geometry==null||!ReferenceEquals(geometry.owner,this))throw new InvalidOperationException("Параметры моста изменились. Обновите предпросмотр.");if(geometry.cells.Count==0)throw new InvalidOperationException("Для создания моста нужно хотя бы одно деление.");var prepared=map.Clone();
            string marker="_mobileudb_bridge_"+Guid.NewGuid().ToString("N");
            for(int cellIndex=0;cellIndex<geometry.cells.Count;cellIndex++)
            {
                var cell=geometry.cells[cellIndex];var points=new List<MapPoint>();foreach(var p in cell.points)points.Add(new MapPoint((float)p.x,(float)p.y));
                var result=prepared.DrawLines(points,false,resources,new DrawTextureOptions{alignX=true});var owners=MarkedSectors(prepared,result.lines);
                if(owners.Count==0)throw new InvalidOperationException("Не удалось создать ячейку моста. Уменьшите число делений или поправьте ручки.");
                foreach(int owner in owners)
                {
                    var sector=prepared.sectors[owner];var props=cell.properties;int ceiling=props.ceiling<props.floor?props.floor+8:props.ceiling;
                    if(props.floor< -32768||props.floor>32767||ceiling< -32768||ceiling>32767)throw new InvalidOperationException("Высоты моста выходят за границы Doom / Hexen.");
                    sector.floor=props.floor;sector.ceiling=ceiling;sector.light=props.light;
                    // Clone/split preserve opaque editor fields. The temporary ownership
                    // stamp follows every fragment and is removed before applying to Map.
                    sector.fields.RemoveAll(f=>f.name==marker);sector.fields.Add(new UniversalField(marker,cellIndex.ToString(CultureInfo.InvariantCulture)));
                }
            }
            var ownersFinal=new Dictionary<int,BridgeCell>();
            for(int i=0;i<prepared.sectors.Count;i++)
            {
                var sector=prepared.sectors[i];foreach(var field in sector.fields)if(field.name==marker)ownersFinal[i]=geometry.cells[int.Parse(field.value,CultureInfo.InvariantCulture)];sector.fields.RemoveAll(f=>f.name==marker);
            }
            foreach(var line in prepared.lines)foreach(int index in new[]{line.front,line.back})if(index>=0)
            {
                var side=prepared.sides[index];BridgeCell cell;if(!ownersFinal.TryGetValue(side.sector,out cell))continue;int other=index==line.front?line.back:line.front;
                if(other<0)continue;var opposite=prepared.sides[other];var a=prepared.sectors[side.sector];var b=prepared.sectors[opposite.sector];
                if(b.floor>a.floor&&!string.Equals(b.floorTexture,skyFlat,StringComparison.OrdinalIgnoreCase))side.lower=cell.properties.lower;
                if(b.ceiling<a.ceiling&&!string.Equals(b.ceilingTexture,skyFlat,StringComparison.OrdinalIgnoreCase))side.upper=cell.properties.upper;
                // Visit endpoint columns once; source c += Count-1 loops forever for 1.
                if(cell.column==0||cell.column==geometry.subdivisions-1)
                {
                    if(a.floor>b.floor&&opposite.lower=="-"&&!string.Equals(a.floorTexture,skyFlat,StringComparison.OrdinalIgnoreCase))opposite.lower=cell.properties.lower;
                    if(a.ceiling<b.ceiling&&opposite.upper=="-"&&!string.Equals(a.ceilingTexture,skyFlat,StringComparison.OrdinalIgnoreCase))opposite.upper=cell.properties.upper;
                }
            }
            prepared.Validate();foreach(int sector in ownersFinal.Keys)MapTransforms.ValidateSector(prepared,sector);return new BridgePlan(map,prepared,state,ownersFinal.Count);
        }
        static HashSet<int> MarkedSectors(MapDocument map,HashSet<int> marked)
        {
            var total=new int[map.sectors.Count];var selected=new int[map.sectors.Count];
            for(int i=0;i<map.lines.Count;i++)foreach(int index in new[]{map.lines[i].front,map.lines[i].back})if(index>=0){int owner=map.sides[index].sector;total[owner]++;if(marked.Contains(i))selected[owner]++;}
            var result=new HashSet<int>();for(int i=0;i<total.Length;i++)if(total[i]>0&&total[i]==selected[i])result.Add(i);return result;
        }
    }
    public sealed class BridgePlan
    {
        readonly MapDocument original,prepared;readonly byte[] state;bool applied;public readonly int SectorCount;
        internal BridgePlan(MapDocument original,MapDocument prepared,byte[] state,int count){this.original=original;this.prepared=prepared;this.state=state;SectorCount=count;}
        public void Apply(MapDocument map)
        {
            if(applied||!ReferenceEquals(original,map))throw new InvalidOperationException("Предпросмотр моста уже применён или относится к другой карте.");var current=BinaryMapState.Capture(map);
            if(current.Length!=state.Length)throw new InvalidOperationException("Карта изменилась. Постройте мост снова.");for(int i=0;i<state.Length;i++)if(state[i]!=current[i])throw new InvalidOperationException("Карта изменилась. Постройте мост снова.");
            map.vertices=prepared.vertices;map.lines=prepared.lines;map.sides=prepared.sides;map.sectors=prepared.sectors;applied=true;
        }
    }
}
