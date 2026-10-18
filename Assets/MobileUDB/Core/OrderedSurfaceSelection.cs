using System;
using System.Collections;
using System.Collections.Generic;

namespace MobileUDB.Core
{
    // Visual offsets use the first selected part of a binary sidedef, as UDB
    // does. A removed and re-added mark must move to the end of that sequence.
    public sealed class OrderedSurfaceSelection : IReadOnlyCollection<SurfaceRef>
    {
        readonly LinkedList<SurfaceRef> order=new LinkedList<SurfaceRef>();
        readonly Dictionary<SurfaceRef,LinkedListNode<SurfaceRef>> nodes=new Dictionary<SurfaceRef,LinkedListNode<SurfaceRef>>();
        public int Count {get{return nodes.Count;}}
        public bool Contains(SurfaceRef value){return nodes.ContainsKey(value);}
        public bool Add(SurfaceRef value){if(nodes.ContainsKey(value))return false;nodes.Add(value,order.AddLast(value));return true;}
        public bool Remove(SurfaceRef value){LinkedListNode<SurfaceRef> node;if(!nodes.TryGetValue(value,out node))return false;nodes.Remove(value);order.Remove(node);return true;}
        public void Clear(){nodes.Clear();order.Clear();}
        public int RemoveWhere(Predicate<SurfaceRef> predicate)
        {if(predicate==null)throw new ArgumentNullException("predicate");int count=0;var node=order.First;while(node!=null){var next=node.Next;if(predicate(node.Value)&&Remove(node.Value))count++;node=next;}return count;}
        public LinkedList<SurfaceRef>.Enumerator GetEnumerator(){return order.GetEnumerator();}
        IEnumerator<SurfaceRef> IEnumerable<SurfaceRef>.GetEnumerator(){return order.GetEnumerator();}
        IEnumerator IEnumerable.GetEnumerator(){return order.GetEnumerator();}
    }
    public struct WallTextureSize
    {
        public int width,height;
        public WallTextureSize(int width,int height){this.width=width;this.height=height;}
        public bool Loaded {get{return width>0&&height>0;}}
    }
    public struct WallTextureOffset
    {
        public int x,y;
        public WallTextureOffset(int x,int y){this.x=x;this.y=y;}
    }
}
