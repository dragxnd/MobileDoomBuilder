using System;
using System.Collections;
using System.Collections.Generic;

namespace MobileUDB.Core
{
    // UDB keeps selected sectors in a linked list. Membership remains a HashSet
    // for existing geometry APIs; direct and ISet mutations also retain chronology.
    public sealed class OrderedSelection : HashSet<ElementRef>, ISet<ElementRef>
    {
        readonly LinkedList<ElementRef> order=new LinkedList<ElementRef>();
        readonly Dictionary<ElementRef,LinkedListNode<ElementRef>> nodes=new Dictionary<ElementRef,LinkedListNode<ElementRef>>();
        public new bool Add(ElementRef item)
        {if(!base.Add(item))return false;nodes.Add(item,order.AddLast(item));return true;}
        void ICollection<ElementRef>.Add(ElementRef item){Add(item);}
        public new bool Remove(ElementRef item)
        {if(!base.Remove(item))return false;order.Remove(nodes[item]);nodes.Remove(item);return true;}
        public new void Clear(){base.Clear();order.Clear();nodes.Clear();}
        public new void UnionWith(IEnumerable<ElementRef> other)
        {if(other==null)throw new ArgumentNullException("other");if(ReferenceEquals(this,other))return;foreach(var item in other)Add(item);}
        public new void ExceptWith(IEnumerable<ElementRef> other)
        {if(other==null)throw new ArgumentNullException("other");foreach(var item in new List<ElementRef>(other))Remove(item);}
        public new void IntersectWith(IEnumerable<ElementRef> other)
        {if(other==null)throw new ArgumentNullException("other");var keep=new HashSet<ElementRef>(other);RemoveWhere(item=>!keep.Contains(item));}
        public new void SymmetricExceptWith(IEnumerable<ElementRef> other)
        {
            if(other==null)throw new ArgumentNullException("other");var unique=new List<ElementRef>();var seen=new HashSet<ElementRef>();
            foreach(var item in other)if(seen.Add(item))unique.Add(item);foreach(var item in unique)if(!Remove(item))Add(item);
        }
        public new int RemoveWhere(Predicate<ElementRef> predicate)
        {if(predicate==null)throw new ArgumentNullException("predicate");int count=0;foreach(var item in new List<ElementRef>(order))if(predicate(item)&&Remove(item))count++;return count;}
        public new LinkedList<ElementRef>.Enumerator GetEnumerator(){return order.GetEnumerator();}
        IEnumerator<ElementRef> IEnumerable<ElementRef>.GetEnumerator(){return order.GetEnumerator();}
        IEnumerator IEnumerable.GetEnumerator(){return order.GetEnumerator();}
        public new void CopyTo(ElementRef[] array,int arrayIndex)
        {if(array==null)throw new ArgumentNullException("array");if(arrayIndex<0||arrayIndex>array.Length||Count>array.Length-arrayIndex)throw new ArgumentOutOfRangeException("arrayIndex");foreach(var item in order)array[arrayIndex++]=item;}
        void ICollection<ElementRef>.CopyTo(ElementRef[] array,int arrayIndex){CopyTo(array,arrayIndex);}
    }
}
