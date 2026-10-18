using System;
using System.Collections.Generic;
using System.IO;

namespace MobileUDB.Core
{
    // Scan-local immutable AABB hierarchy. Unlike a grid, long lines and oversized
    // DEHACKED/argument radii do not occupy an unbounded number of cells.
    internal sealed class ThingCheckIndex
    {
        struct Box
        {
            public double left,bottom,right,top;
            public int index;public uint key;
        }
        struct Node
        {
            public Box box;public int low,high;
        }
        Box[] items;Node[] nodes;int count,work;
        Box extent;
        public ThingCheckIndex(int capacity){items=new Box[capacity];}
        public void Add(int index,double left,double bottom,double right,double top)
        {
            if(double.IsNaN(left)||double.IsNaN(bottom)||double.IsNaN(right)||double.IsNaN(top)||
                double.IsInfinity(left)||double.IsInfinity(bottom)||double.IsInfinity(right)||double.IsInfinity(top))
                throw new InvalidDataException("Некорректный размер объекта при проверке размещения.");
            var box=new Box{index=index,left=left,bottom=bottom,right=right,top=top};
            items[count]=box;extent=count++==0?box:Union(extent,box);
        }
        static Box Union(Box a,Box b)
        {return new Box{left=Math.Min(a.left,b.left),bottom=Math.Min(a.bottom,b.bottom),right=Math.Max(a.right,b.right),top=Math.Max(a.top,b.top)};}
        static uint Coordinate(double center,double origin,double span)
        {return span==0?0:(uint)(Math.Max(0,Math.Min(1,(center-origin)/span))*65535);}
        static uint Spread(uint n)
        {n=(n|(n<<8))&0x00ff00ff;n=(n|(n<<4))&0x0f0f0f0f;n=(n|(n<<2))&0x33333333;return (n|(n<<1))&0x55555555;}
        public IEnumerable<object> Build()
        {
            if(count==0){items=null;nodes=new Node[0];yield break;}
            for(int i=0;i<count;i++)
            {
                var box=items[i];uint x=Coordinate((box.left+box.right)*.5,extent.left,extent.right-extent.left),
                    y=Coordinate((box.bottom+box.top)*.5,extent.bottom,extent.top-extent.bottom);
                box.key=Spread(x)|(Spread(y)<<1);items[i]=box;if((++work&127)==0)yield return null;
            }
            // Cooperative stable merge sort: even index construction is cancellable.
            var scratch=new Box[count];
            for(int width=1;width<count;width*=2)
            {
                for(int start=0;start<count;start+=width*2)
                {
                    int middle=Math.Min(count,start+width),end=Math.Min(count,start+width*2),a=start,b=middle;
                    for(int at=start;at<end;at++)
                    {
                        bool first=b>=end||(a<middle&&(items[a].key<items[b].key||items[a].key==items[b].key&&items[a].index<items[b].index));
                        scratch[at]=items[first?a++:b++];if((++work&127)==0)yield return null;
                    }
                }
                var swap=items;items=scratch;scratch=swap;
            }
            nodes=new Node[checked(count*2-1)];
            foreach(var pause in BuildNode(0,count,0))yield return pause;
            items=null;
        }
        IEnumerable<object> BuildNode(int start,int length,int node)
        {
            if(length==1)nodes[node]=new Node{box=items[start],low=-1,high=-1};
            else
            {
                int half=length/2,low=node+1,high=node+half*2;
                foreach(var pause in BuildNode(start,half,low))yield return pause;
                foreach(var pause in BuildNode(start+half,length-half,high))yield return pause;
                nodes[node]=new Node{box=Union(nodes[low].box,nodes[high].box),low=low,high=high};
            }
            if((++work&127)==0)yield return null;
        }
        static double Distance(Box box,MapPoint p)
        {double x=p.x<box.left?box.left-p.x:p.x>box.right?p.x-box.right:0,y=p.y<box.bottom?box.bottom-p.y:p.y>box.top?p.y-box.top:0;return x*x+y*y;}
        static bool Overlap(Box box,double left,double bottom,double right,double top)
        {return box.right>=left&&box.left<=right&&box.top>=bottom&&box.bottom<=top;}
        // -1 is a cooperative pause, never an element index.
        public IEnumerable<int> Range(double left,double bottom,double right,double top)
        {
            if(nodes.Length==0)yield break;var stack=new int[64];int size=1,visited=0;
            while(size>0)
            {
                var node=nodes[stack[--size]];
                if(Overlap(node.box,left,bottom,right,top))
                {if(node.low<0)yield return node.box.index;else {stack[size++]=node.high;stack[size++]=node.low;}}
                if((++visited&127)==0)yield return -1;
            }
        }
        public IEnumerable<int> Nearest(MapPoint point,Func<double> currentBest)
        {
            if(nodes.Length==0)yield break;var stack=new int[64];int size=1,visited=0;
            while(size>0)
            {
                var node=nodes[stack[--size]];double best=currentBest();
                // Conservative tolerance prevents rounding in a lower bound from
                // pruning an equally near line; the exact distance chooses the winner.
                if(Distance(node.box,point)<=best+Math.Abs(best)*1e-12+1e-12)
                {
                    if(node.low<0)yield return node.box.index;
                    else
                    {
                        bool low=Distance(nodes[node.low].box,point)<=Distance(nodes[node.high].box,point);
                        stack[size++]=low?node.high:node.low;stack[size++]=low?node.low:node.high;
                    }
                }
                if((++visited&127)==0)yield return -1;
            }
        }
        public static IEnumerable<object> SortHits(List<int> hits)
        {
            if(hits.Count<128){hits.Sort();yield break;}
            int count=hits.Count,work=0;var a=hits.ToArray();var b=new int[count];
            for(int width=1;width<count;width*=2)
            {
                for(int start=0;start<count;start+=width*2)
                {
                    int middle=Math.Min(count,start+width),end=Math.Min(count,start+width*2),low=start,high=middle;
                    for(int at=start;at<end;at++)
                    {b[at]=high>=end||(low<middle&&a[low]<a[high])?a[low++]:a[high++];if((++work&127)==0)yield return null;}
                }
                var swap=a;a=b;b=swap;
            }
            for(int i=0;i<count;i++){hits[i]=a[i];if((++work&127)==0)yield return null;}
        }
    }
}
