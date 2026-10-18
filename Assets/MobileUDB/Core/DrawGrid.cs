// GPL-3.0. DrawGridMode.GetShapes / UpdateReferencePoints, adapted from UDB.
using System;
using System.Collections.Generic;

namespace MobileUDB.Core
{
    public enum DrawGridLock { None,Horizontal,Vertical,Both }
    [Serializable] public sealed class DrawGridOptions
    {
        public int columns=3,rows=3;
        public DrawGridLock gridLock;
        public GradientInterpolation horizontal,vertical;
        public bool triangulate,relative=true,continuous,guidelines;
        public DrawGridOptions Copy(){return (DrawGridOptions)MemberwiseClone();}
        public bool LockX {get{return gridLock==DrawGridLock.Horizontal||gridLock==DrawGridLock.Both;}}
        public bool LockY {get{return gridLock==DrawGridLock.Vertical||gridLock==DrawGridLock.Both;}}
        public void Validate()
        {
            if(columns<1||columns>128||rows<1||rows>128||(int)gridLock<0||(int)gridLock>3||(int)horizontal<0||(int)horizontal>3||(int)vertical<0||(int)vertical>3)
                throw new InvalidOperationException("Ячейки X/Y: 1–128. Выберите допустимые параметры сетки.");
        }
    }
    public sealed class DrawGridGeometry
    {
        public readonly IReadOnlyList<IReadOnlyList<CurvePoint>> paths;
        public readonly DrawShapeContour outline;
        public readonly int columns,rows;
        internal readonly CurvePoint start,end;
        internal readonly double rotation;
        internal readonly bool area;
        internal readonly List<CurvePoint[]> cells=new List<CurvePoint[]>();
        internal DrawGridGeometry(List<IReadOnlyList<CurvePoint>> paths,CurvePoint start,CurvePoint end,double rotation,int columns,int rows,bool area)
        {
            this.paths=paths.AsReadOnly();this.start=start;this.end=end;this.rotation=rotation;this.columns=columns;this.rows=rows;this.area=area;
            var points=new List<MapPoint>();if(paths.Count>0)foreach(var p in paths[0])points.Add(new MapPoint((float)p.x,(float)p.y));
            var a=start.GetRotated(rotation);var b=end.GetRotated(rotation);
            outline=new DrawShapeContour(points,new MapPoint((float)a.x,(float)a.y),new MapPoint((float)b.x,(float)b.y),false);
        }
    }
    public static class DrawGrid
    {
        // Bound a single mobile operation, including automatically counted cells.
        public const int MaxCells=256;
        static void Coordinate(CurvePoint p)
        {if(double.IsNaN(p.x)||double.IsNaN(p.y)||double.IsInfinity(p.x)||double.IsInfinity(p.y)||p.x< -32768||p.x>32767||p.y< -32768||p.y>32767)throw new InvalidOperationException("Сетка выходит за границы Doom / Hexen.");}
        struct Block
        {
            public float x,y,right,bottom;
            public Block(double left,double top,double r,double b){x=(float)left;y=(float)top;right=x+((float)r-x);bottom=y+((float)b-y);}
        }
        public static DrawGridGeometry Build(MapPoint first,MapPoint second,DrawGridOptions options,GridFrame grid)
        {return Build(new CurvePoint(first.x,first.y),new CurvePoint(second.x,second.y),options,grid);}
        public static DrawGridGeometry Build(CurvePoint first,CurvePoint second,DrawGridOptions options,GridFrame grid)
        {
            if(options==null||grid==null)throw new ArgumentNullException();options.Validate();grid.Validate();Coordinate(first);Coordinate(second);
            // Upstream rotates around world zero here; grid origin only affects anchor snapping.
            var s=first.GetRotated(-grid.rotation);var e=second.GetRotated(-grid.rotation);
            if(!options.relative){var a=s;s=new CurvePoint(Math.Min(a.x,e.x),Math.Min(a.y,e.y));e=new CurvePoint(Math.Max(a.x,e.x),Math.Max(a.y,e.y));}
            int width=(int)(e.x-s.x),height=(int)(e.y-s.y),step=(int)Math.Max(1,Math.Round(grid.step));
            int nx=options.LockX?(int)Math.Ceiling(Math.Abs(width)/(double)step):options.columns;
            int ny=options.LockY?(int)Math.Ceiling(Math.Abs(height)/(double)step):options.rows;
            if(nx>128||ny>128||(long)Math.Max(1,nx)*Math.Max(1,ny)>MaxCells)throw new InvalidOperationException("За одно построение — до 128 ячеек по оси и 256 всего. Уменьшите число ячеек или увеличьте шаг сетки.");
            var paths=new List<IReadOnlyList<CurvePoint>>();Action<CurvePoint[]> add=points=>
            {var world=new List<CurvePoint>();foreach(var p in points){var q=p.GetRotated(grid.rotation);Coordinate(q);world.Add(q);}paths.Add(world.AsReadOnly());};
            bool area=width!=0&&height!=0;
            if(s.Equals(e))return new DrawGridGeometry(paths,s,e,grid.rotation,nx,ny,area);
            if(!area)
            {
                if(nx>0&&width>0){int d=width/nx;for(int x=0;x<nx;x++)add(new[]{new CurvePoint(s.x+d*x,s.y),new CurvePoint(s.x+d*x+d,s.y)});}
                else if(ny>0&&height>0){int d=height/ny;for(int y=0;y<ny;y++)add(new[]{new CurvePoint(s.x,s.y+d*y),new CurvePoint(s.x,s.y+d*y+d)});}
                else add(new[]{s,e});
                return new DrawGridGeometry(paths,s,e,grid.rotation,nx,ny,false);
            }
            var rect=new List<CurvePoint>{s,new CurvePoint(s.x,e.y),e,new CurvePoint(e.x,s.y),s};
            if(nx==1&&ny==1){var cell=rect.GetRange(0,4).ToArray();if(options.triangulate)rect.AddRange(new[]{s,e});add(rect.ToArray());var single=new DrawGridGeometry(paths,s,e,grid.rotation,nx,ny,true);for(int i=0;i<cell.Length;i++)cell[i]=cell[i].GetRotated(grid.rotation);single.cells.Add(cell);return single;}
            add(rect.ToArray());var blocks=new Block[nx,ny];
            var hx=options.LockX?GradientInterpolation.Linear:options.horizontal;var vy=options.LockY?GradientInterpolation.Linear:options.vertical;
            for(int x=0;x<nx;x++)for(int y=0;y<ny;y++)blocks[x,y]=new Block(
                SectorGradients.Interpolate(s.x,e.x,(double)x/nx,hx),SectorGradients.Interpolate(s.y,e.y,(double)y/ny,vy),
                SectorGradients.Interpolate(s.x,e.x,(x+1.0f)/nx,hx),SectorGradients.Interpolate(s.y,e.y,(y+1.0f)/ny,vy));
            for(int x=1;x<nx;x++){float px=blocks[x,0].x;add(new[]{new CurvePoint(px,s.y),new CurvePoint(px,e.y)});}
            for(int y=1;y<ny;y++){float py=blocks[0,y].y;add(new[]{new CurvePoint(s.x,py),new CurvePoint(e.x,py)});}
            if(options.triangulate)
            {
                bool startflip=(int)Math.Round(((s.x+e.y)/grid.step)%2)==0,flip=startflip;
                for(int x=0;x<nx;x++)
                {for(int y=ny-1;y>=0;y--){var b=blocks[x,y];add(flip?new[]{new CurvePoint(b.x,b.y),new CurvePoint(b.right,b.bottom)}:new[]{new CurvePoint(b.right,b.y),new CurvePoint(b.x,b.bottom)});flip=!flip;}startflip=!startflip;flip=startflip;}
            }
            var result=new DrawGridGeometry(paths,s,e,grid.rotation,nx,ny,true);
            for(int x=0;x<nx;x++)for(int y=0;y<ny;y++){var b=blocks[x,y];var cell=new[]{new CurvePoint(b.x,b.y),new CurvePoint(b.x,b.bottom),new CurvePoint(b.right,b.bottom),new CurvePoint(b.right,b.y)};for(int i=0;i<cell.Length;i++)cell[i]=cell[i].GetRotated(grid.rotation);result.cells.Add(cell);}
            return result;
        }
        public static DrawGridPlan Plan(MapDocument map,DrawGridGeometry geometry,DoomResources resources=null,DrawTextureOptions textures=null)
        {
            ThingClipboard.Binary(map);map.Validate();if(geometry==null||geometry.paths.Count==0)throw new InvalidOperationException("Укажите две разные точки сетки.");
            foreach(var cell in geometry.cells)for(int i=0;i<4;i++)
            {
                var a=MapDocument.Snap(new MapPoint((float)cell[i].x,(float)cell[i].y),1);
                var b=MapDocument.Snap(new MapPoint((float)cell[(i+1)%4].x,(float)cell[(i+1)%4].y),1);
                var c=MapDocument.Snap(new MapPoint((float)cell[(i+2)%4].x,(float)cell[(i+2)%4].y),1);
                if((b.x-a.x)*(double)(c.y-b.y)-(b.y-a.y)*(double)(c.x-b.x)==0)throw new InvalidOperationException("Ячейки слишком малы для бинарной карты. Увеличьте размер или уменьшите число ячеек.");
            }
            var prepared=map.Clone();
            foreach(var path in geometry.paths)
            {
                var points=new List<MapPoint>();CurvePoint? previous=null;
                foreach(var p in path)
                {
                    // The source 1x1 triangulated path intentionally repeats its start.
                    if(previous.HasValue&&previous.Value.Equals(p))continue;
                    var point=new MapPoint((float)p.x,(float)p.y);
                    if(points.Count>0&&(MapDocument.Snap(points[points.Count-1],1)-MapDocument.Snap(point,1)).LengthSquared==0)
                        throw new InvalidOperationException("Ячейки слишком малы для бинарной карты. Увеличьте размер или уменьшите число ячеек.");
                    points.Add(point);previous=p;
                }
                if(points.Count<2)throw new InvalidOperationException("Ячейки слишком малы для бинарной карты.");
                prepared.DrawLines(points,false,resources,textures);
            }
            // Select completed cells by their final boundaries, avoiding stale indices
            // after the shared stitcher compacts sectors between individual cuts.
            var selected=new HashSet<ElementRef>();
            if(geometry.area)for(int sector=0;sector<prepared.sectors.Count;sector++)
            {
                bool found=false,inside=true;
                foreach(var line in prepared.lines)if((line.front>=0&&prepared.sides[line.front].sector==sector)||(line.back>=0&&prepared.sides[line.back].sector==sector))
                {found=true;if(!Inside(prepared.vertices[line.start].Position,geometry)||!Inside(prepared.vertices[line.end].Position,geometry)){inside=false;break;}}
                if(found&&inside)selected.Add(new ElementRef(ElementKind.Sector,sector));
            }
            if(selected.Count==0)for(int i=0;i<prepared.lines.Count;i++)
            {
                var line=prepared.lines[i];var a=prepared.vertices[line.start].Position;var b=prepared.vertices[line.end].Position;
                foreach(var path in geometry.paths)for(int j=1;j<path.Count;j++)if(OnSegment(a,path[j-1],path[j])&&OnSegment(b,path[j-1],path[j])){selected.Add(new ElementRef(ElementKind.Line,i));break;}
            }
            if(selected.Count==0)throw new InvalidOperationException("Сетка не создала доступной геометрии.");
            return new DrawGridPlan(map,prepared,selected);
        }
        static bool Inside(MapPoint point,DrawGridGeometry g)
        {var p=new CurvePoint(point.x,point.y).GetRotated(-g.rotation);return p.x>=Math.Min(g.start.x,g.end.x)-1&&p.x<=Math.Max(g.start.x,g.end.x)+1&&p.y>=Math.Min(g.start.y,g.end.y)-1&&p.y<=Math.Max(g.start.y,g.end.y)+1;}
        static bool OnSegment(MapPoint p,CurvePoint a,CurvePoint b)
        {double dx=b.x-a.x,dy=b.y-a.y,length=dx*dx+dy*dy;if(length<1e-8)return false;double t=((p.x-a.x)*dx+(p.y-a.y)*dy)/length;if(t< -1e-4||t>1.0001)return false;double x=p.x-(a.x+t*dx),y=p.y-(a.y+t*dy);return x*x+y*y<=1;}
    }
    public sealed class DrawGridPlan
    {
        readonly MapDocument original,prepared;readonly byte[] state;readonly HashSet<ElementRef> selection;bool applied;
        internal DrawGridPlan(MapDocument original,MapDocument prepared,HashSet<ElementRef> selection)
        {this.original=original;this.prepared=prepared;this.selection=selection;state=BinaryMapState.Capture(original);}
        public HashSet<ElementRef> Apply(MapDocument map)
        {
            if(applied||!ReferenceEquals(map,original))throw new InvalidOperationException("Предпросмотр сетки относится к другой карте или уже применён.");
            var current=BinaryMapState.Capture(map);if(current.Length!=state.Length)throw new InvalidOperationException("Карта изменилась. Постройте сетку снова.");
            for(int i=0;i<current.Length;i++)if(current[i]!=state[i])throw new InvalidOperationException("Карта изменилась. Постройте сетку снова.");
            map.vertices=prepared.vertices;map.lines=prepared.lines;map.sides=prepared.sides;map.sectors=prepared.sectors;applied=true;return new HashSet<ElementRef>(selection);
        }
    }
}
