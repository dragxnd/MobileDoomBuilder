// GPL-3.0. Classic UDB BlockMap: current geometry, 128-unit cells and native DDA traversal.
using System;
using System.Collections.Generic;
namespace MobileUDB.Core
{
    public sealed class BlockmapPreview
    {
        public const int CellSize=128;
        public sealed class Cell
        {
            public readonly HashSet<int> Lines=new HashSet<int>();
            public readonly HashSet<int> Things=new HashSet<int>();
        }
        public readonly float OriginX,OriginY;
        public readonly int Width,Height;
        readonly Dictionary<long,Cell> cells=new Dictionary<long,Cell>();
        public BlockmapPreview(MapDocument map)
        {
            float minX=float.MaxValue,minY=float.MaxValue,maxX=float.MinValue,maxY=float.MinValue;
            foreach(var v in map.vertices)Bounds(v.x,v.y,ref minX,ref minY,ref maxX,ref maxY);
            foreach(var t in map.things)Bounds(t.x,t.y,ref minX,ref minY,ref maxX,ref maxY);
            if(minX==float.MaxValue)minX=minY=maxX=maxY=0;
            OriginX=minX;OriginY=minY;
            Width=Math.Max(1,(Outer(maxX)>>7)-(Outer(minX)>>7)+1);
            Height=Math.Max(1,(Outer(maxY)>>7)-(Outer(minY)>>7)+1);
            for(int i=0;i<map.lines.Count;i++)
            {
                var l=map.lines[i];if(l.start<0||l.end<0||l.start>=map.vertices.Count||l.end>=map.vertices.Count)continue;
                AddLine(map.vertices[l.start].Position,map.vertices[l.end].Position,i);
            }
            for(int i=0;i<map.things.Count;i++)
            {int x,y;if(TryCell(map.things[i].Position,out x,out y))Ensure(x,y).Things.Add(i);}
        }
        static void Bounds(float x,float y,ref float minX,ref float minY,ref float maxX,ref float maxY)
        {minX=Math.Min(minX,x);minY=Math.Min(minY,y);maxX=Math.Max(maxX,x);maxY=Math.Max(maxY,y);}
        static int Outer(float v){return (int)(v<0?Math.Floor(v):Math.Ceiling(v));}
        static long Key(int x,int y){return ((long)x<<32)|(uint)y;}
        bool Inside(int x,int y){return x>=0&&y>=0&&x<Width&&y<Height;}
        Cell Ensure(int x,int y){Cell c;long key=Key(x,y);if(!cells.TryGetValue(key,out c)){c=new Cell();cells.Add(key,c);}return c;}
        public Cell GetCell(int x,int y){Cell c;return Inside(x,y)&&cells.TryGetValue(Key(x,y),out c)?c:null;}
        public bool TryCell(MapPoint point,out int x,out int y)
        {x=(int)Math.Floor((point.x-OriginX)/CellSize);y=(int)Math.Floor((point.y-OriginY)/CellSize);return Inside(x,y);}
        void AddLine(MapPoint a,MapPoint b,int index)
        {
            int x,y,endX,endY;TryCell(a,out x,out y);TryCell(b,out endX,out endY);
            double dx=b.x-a.x,dy=b.y-a.y;int stepX=Math.Sign(dx),stepY=Math.Sign(dy);
            double deltaX=stepX==0?double.MaxValue:CellSize/Math.Abs(dx),deltaY=stepY==0?double.MaxValue:CellSize/Math.Abs(dy);
            double nextX=stepX==0?double.MaxValue:(OriginX+(x+(stepX>0?1:0))*CellSize-a.x)/dx;
            double nextY=stepY==0?double.MaxValue:(OriginY+(y+(stepY>0?1:0))*CellSize-a.y)/dy;
            int remaining=Math.Abs(endX-x)+Math.Abs(endY-y)+1;
            while(remaining-->0)
            {
                if(Inside(x,y))Ensure(x,y).Lines.Add(index);
                if(x==endX&&y==endY)break;
                // UDB visits the Y neighbour first when a line crosses a grid corner.
                if(nextX<nextY){nextX+=deltaX;if(x!=endX)x+=stepX;}
                else{nextY+=deltaY;if(y!=endY)y+=stepY;}
            }
        }
    }
}
