using System;
using System.Collections;
using System.Collections.Generic;

namespace MobileUDB.Core
{
    // Selection order matters when the next target sees an earlier moved thing.
    public sealed class OrderedThingSelection : IReadOnlyCollection<int>
    {
        readonly LinkedList<int> order = new LinkedList<int>();
        readonly Dictionary<int,LinkedListNode<int>> nodes = new Dictionary<int,LinkedListNode<int>>();
        public int Count { get { return nodes.Count; } }
        public bool Contains(int value) { return nodes.ContainsKey(value); }
        public bool Add(int value) { if(nodes.ContainsKey(value))return false;nodes.Add(value,order.AddLast(value));return true; }
        public bool Remove(int value) { LinkedListNode<int> node;if(!nodes.TryGetValue(value,out node))return false;nodes.Remove(value);order.Remove(node);return true; }
        public void Clear() { nodes.Clear();order.Clear(); }
        public int RemoveWhere(Predicate<int> predicate)
        {if(predicate==null)throw new ArgumentNullException("predicate");int count=0;var node=order.First;while(node!=null){var next=node.Next;if(predicate(node.Value)&&Remove(node.Value))count++;node=next;}return count;}
        public LinkedList<int>.Enumerator GetEnumerator() { return order.GetEnumerator(); }
        IEnumerator<int> IEnumerable<int>.GetEnumerator() { return GetEnumerator(); }
        IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }
    }
    public static class ThingHeightAlignment
    {
        static bool Intersects(MapThing a,float ar,MapThing b,float br)
        {
            // RectangleF arithmetic and strict edge intersection from UDB.
            float ax=(float)((double)a.x-ar),ay=(float)((double)a.y-ar),aw=(float)(ar*2.0);
            float bx=(float)((double)b.x-br),by=(float)((double)b.y-br),bw=(float)(br*2.0);
            return bx<ax+aw && ax<bx+bw && by<ay+aw && ay<by+bw;
        }
        static double Aligned(MapDocument map,MapThing thing,ThingType type,int sector,double targetHeight,double rawZ)
        {
            if(type.absoluteZ && type.hangs)return rawZ;
            var s=map.sectors[sector];double height=ThingPlacement.Height(thing,type);
            if(type.absoluteZ)return rawZ-s.floor+height;
            if(type.hangs)return (double)s.ceiling-s.floor-rawZ-height-targetHeight;
            return rawZ+height;
        }
        static double Next(MapDocument map,int index,bool up,int[] sectors,ThingType[] types,double[] rawZ)
        {
            var thing=map.things[index];var info=types[index];
            if(info.absoluteZ && info.hangs)return rawZ[index];
            var sector=map.sectors[sectors[index]];
            double height=ThingPlacement.Height(thing,info),floor=info.absoluteZ?0:sector.floor,ceiling=sector.ceiling;
            double position=info.hangs?(ceiling-floor)-rawZ[index]-height:rawZ[index];
            double next=up?double.MaxValue:double.MinValue;
            float radius=ThingPlacement.Radius(thing,info);
            for(int i=0;i<map.things.Count;i++)
            {
                if(i==index || sectors[i]<0 || !Intersects(thing,radius,map.things[i],ThingPlacement.Radius(map.things[i],types[i])))continue;
                double z=Aligned(map,map.things[i],types[i],sectors[i],height,rawZ[i]);
                if(up ? z>position && z<next : z<position && z>next)next=z;
            }
            if(info.hangs)
            {
                // Binary maps have the ordinary floor/ceiling pair, no extra levels.
                // UDB compares double sentinels against float constants in these
                // branches. Use consistent double sentinels to avoid wrong/no-hit Z.
                if(up)return next!=double.MaxValue?Math.Max(ceiling-floor-next-height,0):0;
                return next!=double.MinValue?ceiling-floor-Math.Min(next,ceiling-floor)-height:ceiling-floor-height;
            }
            if(up)
            {
                double z=ceiling-sector.floor-height;
                if(next!=double.MaxValue)z=Math.Min(z,next);
                return info.absoluteZ?z+sector.floor:z;
            }
            double lower=next!=double.MinValue?Math.Max(next,sector.floor):0;
            return info.absoluteZ?lower+sector.floor:lower;
        }
        internal static void ApplyOrdered(MapDocument working,IEnumerable<int> indices,bool up,Func<int,ThingType> findType)
        {
            var targets=new List<int>();var seen=new HashSet<int>();
            foreach(int index in indices)
            {
                if(index<0||index>=working.things.Count)throw new InvalidOperationException("Объект больше не существует. Выберите его снова.");
                if(seen.Add(index))targets.Add(index);
            }
            if(working.format==MapFormat.Doom)return;
            var sectors=new int[working.things.Count];var types=new ThingType[working.things.Count];var rawZ=new double[working.things.Count];
            for(int i=0;i<working.things.Count;i++)
            {
                var thing=working.things[i];rawZ[i]=thing.z;sectors[i]=ThingPlacement.Sector(working,thing.Position);
                types[i]=findType(thing.type)??new ThingType{id=thing.type};
                float height=ThingPlacement.Height(thing,types[i]),radius=ThingPlacement.Radius(thing,types[i]);
                if(float.IsNaN(height)||float.IsInfinity(height)||height<0||float.IsNaN(radius)||float.IsInfinity(radius)||radius<0)throw new InvalidOperationException("Некорректные размеры типа объекта.");
            }
            foreach(int index in targets)
            {
                if(sectors[index]<0)continue;double z=Next(working,index,up,sectors,types,rawZ);
                // Source HexenMapSetIO writes (Int16)Position.z: canonicalize that
                // representation here because the port's general writer rounds.
                double encoded=Math.Truncate(z);
                if(double.IsNaN(encoded)||double.IsInfinity(encoded)||encoded<short.MinValue||encoded>short.MaxValue)throw new InvalidOperationException("Высота объекта выходит за границы бинарного формата.");
                rawZ[index]=z;
            }
            // Later targets see the unrounded source-style result; canonicalize
            // only after the ordered calculation is complete.
            foreach(int index in targets)if(sectors[index]>=0)working.things[index].z=(float)Math.Truncate(rawZ[index]);
        }
    }
}
