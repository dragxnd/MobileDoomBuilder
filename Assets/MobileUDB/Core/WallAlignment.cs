// GPL-3.0. Binary adaptation of UDB AlignLinedefs / Tools.FlipSectorLinedefs.
using System;
using System.Collections.Generic;

namespace MobileUDB.Core
{
    public sealed class WallAlignmentPlan
    {
        struct LineState {public MapLine line;public int start,end,front,back,order;}
        readonly MapDocument source;
        readonly MapFormat format;
        readonly LineState[] lines;
        readonly MapSide[] sides;
        readonly int[] owners;
        readonly int sectorCount;
        readonly List<int> changes;
        bool used;
        public int ChangeCount {get{return changes.Count;}}
        public readonly bool SelectedLinesOnly;
        public readonly int TargetCount;
        internal WallAlignmentPlan(MapDocument source,List<int> changes,bool selectedLinesOnly,int targetCount)
        {
            this.source=source;format=source.format;sectorCount=source.sectors.Count;this.changes=changes;
            SelectedLinesOnly=selectedLinesOnly;TargetCount=targetCount;lines=new LineState[source.lines.Count];
            for(int i=0;i<lines.Length;i++){var l=source.lines[i];lines[i]=new LineState{line=l,start=l.start,end=l.end,front=l.front,back=l.back,order=l.firstSideOrder};}
            sides=source.sides.ToArray();owners=new int[sides.Length];for(int i=0;i<sides.Length;i++)owners[i]=sides[i].sector;
        }
        public int Apply(MapDocument map)
        {
            if(used||!ReferenceEquals(map,source)||map.format!=format||map.lines.Count!=lines.Length||map.sides.Count!=sides.Length||map.sectors.Count!=sectorCount)
                throw new InvalidOperationException("Карта изменилась. Откройте направление стен снова.");
            for(int i=0;i<lines.Length;i++)
            {var l=map.lines[i];var before=lines[i];if(!ReferenceEquals(l,before.line)||l.start!=before.start||l.end!=before.end||l.front!=before.front||l.back!=before.back||l.firstSideOrder!=before.order)throw new InvalidOperationException("Направление или стороны стен изменились.");}
            for(int i=0;i<sides.Length;i++)if(!ReferenceEquals(map.sides[i],sides[i])||map.sides[i].sector!=owners[i])throw new InvalidOperationException("Стороны или секторы изменились.");
            map.Validate();used=true;
            foreach(int index in changes){var l=map.lines[index];int swap=l.start;l.start=l.end;l.end=swap;l.SwapSides();}
            return changes.Count;
        }
    }
    public static class WallAlignment
    {
        public static WallAlignmentPlan Plan(MapDocument map,IEnumerable<ElementRef> selection)
        {
            if(map==null||(map.format!=MapFormat.Doom&&map.format!=MapFormat.Hexen))throw new InvalidOperationException("Направление доступно для бинарных Doom / Hexen карт.");
            map.Validate();if(selection==null)throw new InvalidOperationException("Выберите стены или секторы.");
            var targets=new List<int>();var unique=new HashSet<int>();ElementKind? kind=null;
            foreach(var item in selection)
            {
                if(item.kind!=ElementKind.Line&&item.kind!=ElementKind.Sector)throw new InvalidOperationException("Для согласования выберите только стены либо только секторы.");
                if(kind.HasValue&&kind.Value!=item.kind)throw new InvalidOperationException("Не смешивайте стены и секторы при согласовании направления.");
                kind=item.kind;int count=item.kind==ElementKind.Line?map.lines.Count:map.sectors.Count;
                if(item.index<0||item.index>=count)throw new InvalidOperationException("Выбранный элемент отсутствует.");
                if(unique.Add(item.index))targets.Add(item.index);
            }
            if(targets.Count==0)throw new InvalidOperationException("Выберите стены или секторы.");
            bool linesOnly=kind==ElementKind.Line;var sectors=new List<int>();
            if(linesOnly)
            {
                // Source counts both sides and sorts ascending then reverses.
                var counts=new Dictionary<int,int>();var pairs=new List<KeyValuePair<int,int>>();
                foreach(int index in targets)foreach(int side in new[]{map.lines[index].front,map.lines[index].back})
                {if(side<0)continue;int owner=map.sides[side].sector,n;counts.TryGetValue(owner,out n);counts[owner]=n+1;}
                foreach(var pair in counts)pairs.Add(pair);pairs.Sort((a,b)=>a.Value.CompareTo(b.Value));pairs.Reverse();foreach(var pair in pairs)sectors.Add(pair.Key);
            }
            else sectors.AddRange(targets);
            // Upstream binary imports attach sides in line order, front then back.
            var incident=new List<int>[map.sectors.Count];for(int i=0;i<incident.Length;i++)incident[i]=new List<int>();
            for(int i=0;i<map.lines.Count;i++)foreach(bool front in map.lines[i].SideOrder==2?new[]{false,true}:new[]{true,false})
            {int side=front?map.lines[i].front:map.lines[i].back;if(side>=0)incident[map.sides[side].sector].Add(i*2+(front?0:1));}
            var processed=new HashSet<int>();var changes=new List<int>();
            foreach(int sector in sectors)
            {
                var fronts=new List<int>();var backs=new List<int>();int unselectedFront=0,unselectedBack=0;
                foreach(int edge in incident[sector])
                {
                    int index=edge/2;if(processed.Contains(index))continue;bool front=(edge&1)==0;
                    if(linesOnly&&!unique.Contains(index)){if(front)unselectedFront++;else unselectedBack++;continue;}
                    if(front)fronts.Add(index);else backs.Add(index);processed.Add(index);
                }
                // Keep the original tie/all-front behavior, including repeat flips.
                if(fronts.Count==0||(fronts.Count+unselectedFront>backs.Count+unselectedBack&&backs.Count>0))changes.AddRange(backs);
                else foreach(int index in fronts)if(map.lines[index].back>=0)changes.Add(index);
            }
            return new WallAlignmentPlan(map,changes,linesOnly,targets.Count);
        }
    }
}
