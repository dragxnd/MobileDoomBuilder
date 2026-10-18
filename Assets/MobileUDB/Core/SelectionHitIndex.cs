using System;
using System.Collections.Generic;

namespace MobileUDB.Core
{
    // Immutable per-map-revision picking snapshot for repeated brush samples.
    public sealed class SelectionHitIndex
    {
        sealed class Item
        {
            public int index;public float left,right,bottom,top;public MapPoint a,b;public List<Edge> edges;
            public void Include(MapPoint p){left=Math.Min(left,p.x);right=Math.Max(right,p.x);bottom=Math.Min(bottom,p.y);top=Math.Max(top,p.y);}
        }
        struct Edge {public MapPoint a,b;public Edge(MapPoint a,MapPoint b){this.a=a;this.b=b;}}
        sealed class Node {public float left,right,bottom,top;public Node low,high;public Item item;}
        readonly ElementKind kind;readonly Node root;
        static Item Point(int index,MapPoint p){return new Item{index=index,a=p,b=p,left=p.x,right=p.x,bottom=p.y,top=p.y};}
        public SelectionHitIndex(MapDocument map,ElementKind kind)
        {
            if(map==null)throw new ArgumentNullException("map");this.kind=kind;var items=new List<Item>();
            if(kind==ElementKind.Vertex||kind==ElementKind.Thing)
            {for(int i=0;i<MapSelection.Count(map,kind);i++)items.Add(Point(i,kind==ElementKind.Vertex?map.vertices[i].Position:map.things[i].Position));}
            else if(kind==ElementKind.Line)
            {for(int i=0;i<map.lines.Count;i++){var line=map.lines[i];var item=Point(i,map.vertices[line.start].Position);item.b=map.vertices[line.end].Position;item.Include(item.b);items.Add(item);}}
            else if(kind==ElementKind.Sector)
            {
                var sectors=new Item[map.sectors.Count];
                foreach(var line in map.lines)
                {
                    int front=line.front>=0?map.sides[line.front].sector:-1,back=line.back>=0?map.sides[line.back].sector:-1;if(front==back)continue;
                    var a=map.vertices[line.start].Position;var b=map.vertices[line.end].Position;Include(front);Include(back);
                    void Include(int sector)
                    {if(sector<0)return;var item=sectors[sector];if(item==null){item=Point(sector,a);item.edges=new List<Edge>();sectors[sector]=item;}item.Include(a);item.Include(b);item.edges.Add(new Edge(a,b));}
                }
                foreach(var item in sectors)if(item!=null)items.Add(item);
            }
            else throw new ArgumentOutOfRangeException("kind");root=Build(items,0,items.Count);
        }
        static Node Build(List<Item> items,int start,int count)
        {
            if(count==0)return null;var first=items[start];var node=new Node{left=first.left,right=first.right,bottom=first.bottom,top=first.top};
            for(int i=start+1;i<start+count;i++){var item=items[i];node.left=Math.Min(node.left,item.left);node.right=Math.Max(node.right,item.right);node.bottom=Math.Min(node.bottom,item.bottom);node.top=Math.Max(node.top,item.top);}
            if(count==1){node.item=first;return node;}
            bool x=node.right-node.left>=node.top-node.bottom;
            items.Sort(start,count,Comparer<Item>.Create((a,b)=>(x?a.left+a.right:a.bottom+a.top).CompareTo(x?b.left+b.right:b.bottom+b.top)));
            int half=count/2;node.low=Build(items,start,half);node.high=Build(items,start+half,count-half);return node;
        }
        public ElementRef? Hit(MapPoint p,float radius)
        {
            int best=-1;float nearest=radius*radius;Visit(root);return best<0?(ElementRef?)null:new ElementRef(kind,best);
            void Visit(Node node)
            {
                if(node==null)return;float dx=p.x<node.left?node.left-p.x:p.x>node.right?p.x-node.right:0,dy=p.y<node.bottom?node.bottom-p.y:p.y>node.top?p.y-node.top:0;
                if(kind==ElementKind.Sector?dx!=0||dy!=0:dx*dx+dy*dy>nearest)return;
                var item=node.item;if(item==null){Visit(node.low);Visit(node.high);return;}
                if(kind==ElementKind.Sector)
                {
                    if(item.index<=best)return;bool inside=false;
                    foreach(var edge in item.edges){var a=edge.a;var b=edge.b;if((a.y>p.y)!=(b.y>p.y)&&p.x<(b.x-a.x)*(p.y-a.y)/(b.y-a.y)+a.x)inside=!inside;}
                    if(inside)best=item.index;return;
                }
                float distance;
                if(kind==ElementKind.Line)
                {var ab=item.b-item.a;float t=ab.LengthSquared==0?0:Math.Max(0,Math.Min(1,((p.x-item.a.x)*ab.x+(p.y-item.a.y)*ab.y)/ab.LengthSquared));distance=(p-new MapPoint(item.a.x+t*ab.x,item.a.y+t*ab.y)).LengthSquared;}
                else distance=(p-item.a).LengthSquared;
                if(distance<nearest||distance==nearest&&item.index>best){nearest=distance;best=item.index;}
            }
        }
    }
}
