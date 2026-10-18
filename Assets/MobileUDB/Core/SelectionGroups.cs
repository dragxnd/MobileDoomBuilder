using System;
using System.Collections.Generic;

namespace MobileUDB.Core
{
    public static class SelectionGroups
    {
        public const int SlotCount=10;
        static int Bit(int slot){if(slot<0||slot>=SlotCount)throw new ArgumentOutOfRangeException("slot");return 1<<slot;}
        static void Binary(MapDocument map)
        {if(map==null||(map.format!=MapFormat.Doom&&map.format!=MapFormat.Hexen))throw new InvalidOperationException("Поддерживаются бинарные карты Doom / Hexen.");}
        internal static SelectableMapElementData Element(MapDocument map,ElementRef id)
        {
            if(id.index<0||id.index>=MapSelection.Count(map,id.kind))throw new InvalidOperationException("Элемент больше не существует. Выберите его снова.");
            switch(id.kind)
            {case ElementKind.Vertex:return map.vertices[id.index];case ElementKind.Line:return map.lines[id.index];case ElementKind.Sector:return map.sectors[id.index];case ElementKind.Thing:return map.things[id.index];default:throw new ArgumentOutOfRangeException("kind");}
        }
        static IEnumerable<ElementRef> All(MapDocument map,ElementKind? kind=null)
        {
            foreach(ElementKind current in new[]{ElementKind.Vertex,ElementKind.Line,ElementKind.Sector,ElementKind.Thing})
                if(!kind.HasValue||current==kind.Value)for(int i=0;i<MapSelection.Count(map,current);i++)yield return new ElementRef(current,i);
        }
        public static HashSet<ElementRef> Members(MapDocument map,int slot,ElementKind? kind=null)
        {
            Binary(map);int bit=Bit(slot);if(kind.HasValue&&!Enum.IsDefined(typeof(ElementKind),kind.Value))throw new ArgumentOutOfRangeException("kind");
            var result=new HashSet<ElementRef>();foreach(var id in All(map,kind))if((Element(map,id).selectionGroups&bit)!=0)result.Add(id);return result;
        }
        public static int[,] Counts(MapDocument map)
        {
            Binary(map);var counts=new int[SlotCount,4];foreach(var id in All(map))
            {int bits=Element(map,id).selectionGroups;for(int slot=0;slot<SlotCount;slot++)if((bits&(1<<slot))!=0)counts[slot,(int)id.kind]++;}return counts;
        }
        public static SelectionGroupPlan PlanAdd(MapDocument map,IEnumerable<ElementRef> selection,int slot)
        {
            Binary(map);int bit=Bit(slot);if(selection==null)throw new ArgumentNullException("selection");var plan=new SelectionGroupPlan(map);
            foreach(var id in new HashSet<ElementRef>(selection)){var element=Element(map,id);plan.Add(id,element,element.selectionGroups|bit);}return plan;
        }
        public static SelectionGroupPlan PlanClear(MapDocument map,int slot)
        {Binary(map);int bit=Bit(slot);var plan=new SelectionGroupPlan(map);foreach(var id in All(map)){var element=Element(map,id);plan.Add(id,element,element.selectionGroups&~bit);}return plan;}
    }
    public sealed class SelectionGroupPlan
    {
        sealed class Write {public ElementRef id;public SelectableMapElementData element;public int before,after;}
        readonly MapDocument original;readonly List<Write> writes=new List<Write>();bool applied;
        internal SelectionGroupPlan(MapDocument map){original=map;}
        internal void Add(ElementRef id,SelectableMapElementData element,int value)
        {
            if(element.selectionGroups<0||element.selectionGroups>1023||value<0||value>1023)throw new InvalidOperationException("Некорректные группы выделения в проекте.");
            if(element.selectionGroups!=value)writes.Add(new Write{id=id,element=element,before=element.selectionGroups,after=value});
        }
        public int ChangeCount {get{return writes.Count;}}
        public int Apply(MapDocument map)
        {
            if(applied||!ReferenceEquals(map,original))throw new InvalidOperationException("Карта изменилась. Откройте группы снова.");
            foreach(var write in writes)if(!ReferenceEquals(SelectionGroups.Element(map,write.id),write.element)||write.element.selectionGroups!=write.before)throw new InvalidOperationException("Группы изменились. Повторите команду.");
            applied=true;foreach(var write in writes)write.element.selectionGroups=write.after;return writes.Count;
        }
    }
}
