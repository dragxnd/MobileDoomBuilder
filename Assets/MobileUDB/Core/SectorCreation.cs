// GPL-3.0. Adapted from UDB Tools.FindClosestPath / FindPotentialSectorAt / MakeSector.
using System;
using System.Collections.Generic;

namespace MobileUDB.Core
{
    public sealed class SectorCreationResult
    {
        public MapDocument map;
        public int sector;
        internal int[] sectorRemap; // Source sector identities after rebuilding.
        public readonly List<int> directedEdges = new List<int>(); // line*2, front=even
    }
    public static class SectorCreation
    {
        struct Point
        {public double x,y;public Point(double x,double y){this.x=x;this.y=y;}public static implicit operator Point(MapPoint p){return new Point(p.x,p.y);}}
        sealed class Trace
        {
            readonly MapDocument map;
            readonly List<int>[] attached;
            public Trace(MapDocument map)
            {
                this.map=map;attached=new List<int>[map.vertices.Count];
                for(int i=0;i<attached.Length;i++)attached[i]=new List<int>();
                for(int i=0;i<map.lines.Count;i++)
                {var line=map.lines[i];attached[line.start].Add(i);attached[line.end].Add(i);}
            }
            MapPoint Start(int edge){var l=map.lines[edge/2];return map.vertices[(edge&1)==0?l.start:l.end].Position;}
            MapPoint End(int edge){return Start(edge^1);}
            double Angle(int line,int vertex)
            {var l=map.lines[line];var a=map.vertices[l.start].Position;var b=map.vertices[l.end].Position;return -Math.Atan2(-((double)b.y-a.y),(double)b.x-a.x)+Math.PI*.5+(l.end==vertex?Math.PI:0);}
            static double Difference(double a,double b)
            {double d=Math.Abs(a-b)%(Math.PI*2);return d>Math.PI?Math.PI*2-d:d;}
            double Relative(int baseline,int candidate,int vertex,bool front)
            {
                double n=Difference(Angle(baseline,vertex),Angle(candidate,vertex));
                var one=map.lines[baseline];var two=map.lines[candidate];
                var a=map.vertices[one.start==vertex?one.end:one.start].Position;
                var b=map.vertices[two.start==vertex?two.end:two.start].Position;
                double s=Side(a,b,map.vertices[vertex].Position);
                if((s<0&&front)||(s>0&&!front))n=Math.PI*2-n;return n;
            }
            // Source angle sorter and trace-count fallback, including turns at dead ends.
            public List<int> Path(int first)
            {
                var result=new List<int>();var counts=new Dictionary<int,int>();int edge=first;
                do
                {
                    result.Add(edge);int previous=edge/2;var line=map.lines[previous];
                    bool front=(edge&1)==0;int vertex=front?line.end:line.start;
                    var lines=new List<int>(attached[vertex]);bool sortFront=line.end==vertex?!front:front;
                    lines.Sort((a,b)=>Math.Sign(Relative(previous,b,vertex,sortFront)-Relative(previous,a,vertex,sortFront)));
                    int next=previous;
                    if(lines.Count==1)front=!front;
                    else
                    {
                        next=lines[0]==previous?lines[1]:lines[0];int count;counts.TryGetValue(next,out count);
                        if(count>0&&next!=first/2)
                            foreach(int alternative in lines)
                            {int other;counts.TryGetValue(alternative,out other);if(alternative!=next&&alternative!=previous&&other<count){next=alternative;break;}}
                        var nl=map.lines[next];if(line.start==nl.start||line.end==nl.end)front=!front;
                    }
                    int used;counts.TryGetValue(next,out used);if(used>=3)return null;counts[next]=used+1;
                    edge=next*2+(front?0:1);
                }while(edge!=first);
                return result;
            }
            public Point SidePoint(int edge)
            {
                var a=Start(edge);var b=End(edge);double dx=(double)b.x-a.x,dy=(double)b.y-a.y,len=Math.Sqrt(dx*dx+dy*dy);
                return new Point(a.x+dx*.5+dy/len*.01,a.y+dy*.5-dx/len*.01);
            }
            public bool Contains(List<int> polygon,Point p)
            {
                bool inside=false;
                foreach(int edge in polygon){var a=Start(edge);var b=End(edge);if((a.y>p.y)!=(b.y>p.y)&&p.x<((double)b.x-a.x)*(p.y-a.y)/((double)b.y-a.y)+a.x)inside=!inside;}
                return inside;
            }
            double RelativeAtPoint(int line,MapPoint p)
            {
                var l=map.lines[line];var a=map.vertices[l.start].Position;var b=map.vertices[l.end].Position;
                if(a.y==p.y)return VertexAngle(p,a,b);
                if(b.y==p.y)return VertexAngle(p,b,a);
                return double.NaN;
            }
            // Original Angle2D.GetAngle(p1,p2,p3), including oriented angle.
            static double VertexAngle(MapPoint p,MapPoint origin,MapPoint end)
            {
                double ax=(double)origin.x-p.x,ay=(double)origin.y-p.y,bx=(double)origin.x-end.x,by=(double)origin.y-end.y;
                double dot=ax*bx+ay*by,cos2=2*dot*dot/((ax*ax+ay*ay)*(bx*bx+by*by))-1;
                double r=(cos2<=-1?Math.PI:cos2>=1?0:Math.Acos(cos2))*.5;if(dot<0)r=Math.PI-r;if(ax*by-ay*bx<0)r=Math.PI*2-r;return r;
            }
            public List<int> Region(int first)
            {
                var point=SidePoint(first);var scanned=new HashSet<int>();List<int> outer=null;int scan=first;
                while(scanned.Add(scan))
                {
                    var path=Path(scan);if(path==null)return null;
                    if(Contains(path,point)){outer=path;break;}
                    MapPoint right=Start(path[0]);foreach(int edge in path){var a=Start(edge);var b=End(edge);if(a.x>right.x)right=a;if(b.x>right.x)right=b;}
                    int next=-1;double found=double.MaxValue,distance=double.MaxValue;
                    for(int i=0;i<map.lines.Count;i++)
                    {
                        var l=map.lines[i];var a=map.vertices[l.start].Position;var b=map.vertices[l.end].Position;
                        if(a.x<=right.x&&b.x<=right.x)continue;if((a.y>right.y&&b.y>right.y)||(a.y<right.y&&b.y<right.y)||a.y==b.y)continue;
                        double x=a.x+((double)b.x-a.x)*(right.y-a.y)/((double)b.y-a.y);
                        double u=(x-right.x)/(32767d-right.x);if(double.IsNaN(u)||u<=.00001)continue;
                        double d=DistanceSquared(a,b,right);
                        if(u<found){next=i;found=u;distance=d;}
                        else if(next>=0&&Math.Round(u,4)==Math.Round(found,4))
                        {double n=RelativeAtPoint(i,right),o=RelativeAtPoint(next,right);if(!double.IsNaN(n)&&!double.IsNaN(o)&&n<o&&d<distance){next=i;distance=d;}}
                    }
                    if(next<0)return null;var nl=map.lines[next];scan=next*2+(Side(map.vertices[nl.start].Position,map.vertices[nl.end].Position,right)<0?0:1);
                }
                if(outer==null)return null;
                var all=new List<int>(outer);var holes=new List<List<int>>();var usedVertices=new HashSet<int>();
                Action<List<int>> track=path=>{foreach(int edge in path){var l=map.lines[edge/2];usedVertices.Add(l.start);usedVertices.Add(l.end);}};
                track(outer);
                // Source searches rightmost untraced vertices inside the outer polygon,
                // then traces their exterior. Child parity excludes enclosed islands.
                for(int pass=0;pass<map.vertices.Count;pass++)
                {
                    int vertex=-1;
                    for(int i=0;i<map.vertices.Count;i++)
                    {
                        var p=map.vertices[i].Position;if(usedVertices.Contains(i)||attached[i].Count==0||!Contains(outer,p))continue;
                        bool inside=true;foreach(var hole in holes)if(Contains(hole,p))inside=!inside;if(!inside)continue;
                        if(vertex<0||p.x>=map.vertices[vertex].x)vertex=i;
                    }
                    if(vertex<0)break;int line=-1;double best=double.MaxValue;
                    foreach(int candidate in attached[vertex]){double d=Difference(Math.PI*.5,Angle(candidate,vertex));if(d<best){best=d;line=candidate;}}
                    var pos=map.vertices[vertex].Position;var l=map.lines[line];var test=new MapPoint(pos.x+100,pos.y);
                    int edge=line*2+(Side(map.vertices[l.start].Position,map.vertices[l.end].Position,test)<0?0:1);
                    var inner=Path(edge);if(inner==null||Contains(inner,SidePoint(edge)))break;
                    all.AddRange(inner);holes.Add(inner);track(inner);
                }
                return all;
            }
        }
        static double Side(MapPoint a,MapPoint b,Point p){return ((double)b.x-a.x)*(p.y-a.y)-((double)b.y-a.y)*(p.x-a.x);}
        static double DistanceSquared(MapPoint a,MapPoint b,Point p,bool safe=false)
        {
            double dx=(double)b.x-a.x,dy=(double)b.y-a.y,t=((p.x-a.x)*dx+(p.y-a.y)*dy)/(dx*dx+dy*dy);
            // Source nearest-line selection excludes one map unit at each end to
            // resolve ambiguity at vertices; outer ray tie checks use ordinary distance.
            double inverse=1/Math.Sqrt(dx*dx+dy*dy);
            t=safe&&inverse<=1?Math.Max(inverse,Math.Min(1-inverse,t)):Math.Max(0,Math.Min(1,t));
            double x=a.x+t*dx-p.x,y=a.y+t*dy-p.y;return x*x+y*y;
        }
        static bool Missing(string texture){return string.IsNullOrEmpty(texture)||texture=="-";}
        static int SideIndex(MapDocument map,int edge){var l=map.lines[edge/2];return (edge&1)==0?l.front:l.back;}
        static MapSector CopySector(MapSector source)
        {var copy=new MapSector{floor=source.floor,ceiling=source.ceiling,light=source.light,special=source.special,tag=source.tag,floorTexture=source.floorTexture,ceilingTexture=source.ceilingTexture};source.CopyFieldsTo(copy);return copy;}
        public static SectorCreationResult Build(MapDocument source,MapPoint point,DrawTextureOptions defaults=null,string skyFlat="F_SKY1",bool autoClearTextures=true)
        {
            if(source==null||(source.format!=MapFormat.Doom&&source.format!=MapFormat.Hexen))throw new InvalidOperationException("Создание сектора доступно для Doom / Hexen.");
            source.Validate();if(float.IsNaN(point.x)||float.IsNaN(point.y)||float.IsInfinity(point.x)||float.IsInfinity(point.y))throw new InvalidOperationException("Некорректная точка области.");
            int nearest=-1;double distance=double.MaxValue;
            for(int i=0;i<source.lines.Count;i++)
            {
                var line=source.lines[i];var a=source.vertices[line.start].Position;var b=source.vertices[line.end].Position;
                if(a.x==b.x&&a.y==b.y)continue;double d=DistanceSquared(a,b,point,true);if(d<distance){distance=d;nearest=i;}
            }
            if(nearest<0)throw new InvalidOperationException("Сначала нарисуйте замкнутый контур стен.");
            var selected=source.lines[nearest];int first=nearest*2+(Side(source.vertices[selected.start].Position,source.vertices[selected.end].Position,point)<=0?0:1);
            var trace=new Trace(source);var edges=trace.Region(first);
            if(edges==null)throw new InvalidOperationException("Здесь нет замкнутой области. Проверьте соединения стен.");
            // Build in a detached map. The real map is untouched if any allocation fails.
            defaults=(defaults??new DrawTextureOptions()).Validated(source.format);var map=source.Clone();
            if(map.sectors.Count>=65535)throw new InvalidOperationException("Достигнут предел секторов бинарной карты.");
            MapSector sector=null,basic=null;string upper=null,middle=null,lower=null;
            Action<MapSide> take=side=>{if(upper==null&&!Missing(side.upper))upper=side.upper;if(middle==null&&!Missing(side.middle))middle=side.middle;if(lower==null&&!Missing(side.lower))lower=side.lower;};
            bool found=false;
            foreach(int edge in edges){int side=SideIndex(map,edge);if(side<0)continue;sector=map.sectors[map.sides[side].sector];take(map.sides[side]);found=true;break;}
            foreach(int edge in edges){int side=SideIndex(map,edge^1);if(side<0)continue;if(sector==null)sector=map.sectors[map.sides[side].sector];take(map.sides[side]);found=true;break;}
            if(!found||sector==null)
            {
                var excluded=new HashSet<int>();foreach(int edge in edges)excluded.Add(edge/2);var p=trace.SidePoint(edges[0]);int line=-1;double best=double.MaxValue;
                for(int i=0;i<map.lines.Count;i++){if(excluded.Contains(i))continue;var l=map.lines[i];var a=map.vertices[l.start].Position;var b=map.vertices[l.end].Position;if(a.x==b.x&&a.y==b.y)continue;double d=DistanceSquared(a,b,p,true);if(d<best){best=d;line=i;}}
                if(line>=0){var l=map.lines[line];bool front=Side(map.vertices[l.start].Position,map.vertices[l.end].Position,p)<0;int side=front?l.front:l.back;
                    if(side>=0){if(sector==null)sector=map.sectors[map.sides[side].sector];take(map.sides[side]);}
                    else {side=front?l.back:l.front;if(side>=0){basic=map.sectors[map.sides[side].sector];take(map.sides[side]);}}}
            }
            upper=upper??defaults.upper??"STARTAN3";middle=middle??defaults.middle??"STARTAN3";lower=lower??defaults.lower??"STARTAN3";
            var created=sector!=null?CopySector(sector):basic!=null?new MapSector{floor=basic.floor,ceiling=basic.ceiling,light=basic.light,floorTexture=basic.floorTexture,ceilingTexture=basic.ceilingTexture}:new MapSector{floor=defaults.overrideFloorHeight?defaults.floorHeight:0,ceiling=defaults.overrideCeilingHeight?defaults.ceilingHeight:128,light=defaults.overrideLight?defaults.light:192,floorTexture=defaults.floor??"FLOOR0_1",ceilingTexture=defaults.ceiling??"CEIL1_1"};
            if(created.ceiling<created.floor)created.ceiling=created.floor;
            int sectorIndex=map.sectors.Count;map.sectors.Add(created);var changedSectors=new HashSet<int>();var changedLines=new HashSet<int>();
            foreach(int edge in edges)changedLines.Add(edge/2);
            var uses=new Dictionary<int,int>();foreach(var l in map.lines)foreach(int s in new[]{l.front,l.back})if(s>=0){int n;uses.TryGetValue(s,out n);uses[s]=n+1;}
            foreach(int index in changedLines)foreach(bool front in new[]{true,false})
            {var l=map.lines[index];int side=front?l.front:l.back;if(side<0)continue;int unique=SurfaceTextureEdits.Exclusive(map,index,front,uses[side]);if(unique!=side){uses[side]--;uses[unique]=1;}}
            foreach(int edge in edges)
            {
                var line=map.lines[edge/2];bool wasSingle=line.front<0||line.back<0;int index=SideIndex(map,edge);
                if(index<0){if(map.sides.Count>=65535)throw new InvalidOperationException("Достигнут предел сторон бинарной карты.");line.RememberSideOrder();index=map.sides.Count;map.sides.Add(new MapSide{middle="-",sector=sectorIndex});if((edge&1)==0)line.front=index;else line.back=index;}
                else changedSectors.Add(map.sides[index].sector);
                var side=map.sides[index];int opposite=SideIndex(map,edge^1);
                if(side.sector!=sectorIndex)line.firstSideOrder=opposite>=0&&map.sides[opposite].sector==sectorIndex?((edge&1)==0?2:1):((edge&1)==0?1:2);
                side.sector=sectorIndex;
                FillRequired(map,side,opposite,upper,middle,lower,skyFlat);
                foreach(int s in new[]{line.front,line.back})if(s>=0)Clean(map,line,s,line.front==s?line.back:line.front,wasSingle,autoClearTextures,skyFlat);
                if(wasSingle&&line.front>=0&&line.back>=0)line.flags=(line.flags|4)&~1;
            }
            foreach(int index in changedLines){var l=map.lines[index];if(l.front<0&&l.back>=0){int swap=l.start;l.start=l.end;l.end=swap;l.SwapSides();}}
            // Upstream SetSector disposes a sector when its last attached side leaves.
            var usedSectors=new HashSet<int>();var usedSides=new HashSet<int>();foreach(var l in map.lines)foreach(int s in new[]{l.front,l.back})if(s>=0)usedSides.Add(s);
            foreach(int s in usedSides)usedSectors.Add(map.sides[s].sector);
            var remap=new int[map.sectors.Count];var sectors=new List<MapSector>();for(int i=0;i<remap.Length;i++){remap[i]=-1;if(!changedSectors.Contains(i)||usedSectors.Contains(i)){remap[i]=sectors.Count;sectors.Add(map.sectors[i]);}}
            // Preserve unrelated unused sides; their dead sector is retained if referenced.
            for(int i=0;i<map.sides.Count;i++)if(remap[map.sides[i].sector]<0&&!usedSides.Contains(i)){int old=map.sides[i].sector;remap[old]=sectors.Count;sectors.Add(map.sectors[old]);}
            foreach(var s in map.sides)s.sector=remap[s.sector];map.sectors=sectors;sectorIndex=remap[sectorIndex];
            map.Validate();MapTransforms.ValidateSector(map,sectorIndex);
            var result=new SectorCreationResult{map=map,sector=sectorIndex,sectorRemap=remap};result.directedEdges.AddRange(edges);return result;
        }
        internal static bool HasPotentialSector(MapDocument source,MapPoint point)
        {
            int nearest=-1;double distance=double.MaxValue;
            for(int i=0;i<source.lines.Count;i++)
            {
                var line=source.lines[i];var a=source.vertices[line.start].Position;var b=source.vertices[line.end].Position;
                if(a.x==b.x&&a.y==b.y)continue;double d=DistanceSquared(a,b,point,true);if(d<distance){distance=d;nearest=i;}
            }
            if(nearest<0)return false;var selected=source.lines[nearest];int first=nearest*2+(Side(source.vertices[selected.start].Position,source.vertices[selected.end].Position,point)<=0?0:1);
            return new Trace(source).Region(first)!=null;
        }
        static bool Upper(MapDocument map,MapSide side,int other,string sky){return other>=0&&!string.Equals(map.sectors[map.sides[other].sector].ceilingTexture,sky,StringComparison.OrdinalIgnoreCase)&&map.sectors[side.sector].ceiling>map.sectors[map.sides[other].sector].ceiling;}
        static bool Lower(MapDocument map,MapSide side,int other,string sky){return other>=0&&!string.Equals(map.sectors[map.sides[other].sector].floorTexture,sky,StringComparison.OrdinalIgnoreCase)&&map.sectors[side.sector].floor<map.sectors[map.sides[other].sector].floor;}
        static void FillRequired(MapDocument map,MapSide side,int other,string upper,string middle,string lower,string sky)
        {if(other<0&&Missing(side.middle))side.middle=middle;if(Upper(map,side,other,sky)&&Missing(side.upper))side.upper=upper;if(Lower(map,side,other,sky)&&Missing(side.lower))side.lower=lower;}
        static void Clean(MapDocument map,MapLine line,int index,int other,bool wasSingle,bool auto,string sky)
        {
            var side=map.sides[index];bool high=Upper(map,side,other,sky),low=Lower(map,side,other,sky);
            if(line.tag==0&&line.action==0&&map.sectors[side.sector].tag==0&&(other<0||map.sectors[map.sides[other].sector].tag==0))
            {if(auto&&!high)side.upper="-";else if(wasSingle&&high&&Missing(side.upper))side.upper=side.middle;if(auto&&!low)side.lower="-";else if(wasSingle&&low&&Missing(side.lower))side.lower=side.middle;}
            if(wasSingle&&other>=0)side.middle="-";
        }
    }
    public sealed partial class MapDocument
    {
        // Prepare every selected region off-map, then commit all topology once.
        // Keep created indices valid as each reconstructed sector disposes/remaps old ones.
        public List<int> MakeSelectedSectors(IEnumerable<ElementRef> selection,DrawTextureOptions defaults=null,string skyFlat="F_SKY1",bool autoClearTextures=false)
        {
            if(selection==null)throw new ArgumentNullException("selection");var sectors=new List<ElementRef>(selection);
            foreach(var item in sectors)if(item.kind!=ElementKind.Sector)throw new InvalidOperationException("Выделите секторы.");
            var points=SelectionThingPlacement.Positions(this,sectors);var working=this;var created=new List<int>();
            foreach(var point in points)
            {
                var result=SectorCreation.Build(working,point,defaults,skyFlat,autoClearTextures);
                for(int i=0;i<created.Count;i++){int next=result.sectorRemap[created[i]];if(next<0)throw new InvalidOperationException("Выбранные области перекрываются.");created[i]=next;}
                created.Add(result.sector);working=result.map;
            }
            ApplyTopology(working);return created;
        }
        public int MakeSector(MapPoint point,DrawTextureOptions defaults=null,string skyFlat="F_SKY1",bool autoClearTextures=true)
        {var result=SectorCreation.Build(this,point,defaults,skyFlat,autoClearTextures);ApplyTopology(result.map);return result.sector;}
    }
}
