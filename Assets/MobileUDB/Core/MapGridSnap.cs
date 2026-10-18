using System;
using System.Collections.Generic;

namespace MobileUDB.Core
{
    public sealed class GridSnapPlan
    {
        internal MapDocument original,prepared;bool applied;
        public int VertexCount {get;internal set;}public int ThingCount {get;internal set;}
        public int ChangeCount {get{return VertexCount+ThingCount;}}
        public void Apply(MapDocument map)
        {
            if(applied||!ReferenceEquals(map,original))throw new InvalidOperationException("Карта изменилась. Выберите элементы снова.");applied=true;
            if(ChangeCount==0)return;
            map.vertices=prepared.vertices;map.lines=prepared.lines;map.sides=prepared.sides;map.sectors=prepared.sectors;map.things=prepared.things;
        }
    }
    public static class MapGridSnap
    {
        public static GridSnapPlan Plan(MapDocument map,IEnumerable<ElementRef> selection,int grid,bool includeSectorThings=false)
        {return Plan(map,selection,new GridFrame{step=grid},includeSectorThings);}
        public static GridSnapPlan Plan(MapDocument map,IEnumerable<ElementRef> selection,GridFrame grid,bool includeSectorThings=false)
        {
            if(map==null||(map.format!=MapFormat.Doom&&map.format!=MapFormat.Hexen))throw new InvalidOperationException("Поддерживаются бинарные карты Doom / Hexen.");
            if(grid==null)throw new ArgumentNullException("grid");grid=grid.Validated();if(selection==null)throw new ArgumentNullException("selection");
            var selected=new HashSet<ElementRef>();foreach(var id in selection)
            {
                if(!Enum.IsDefined(typeof(ElementKind),id.kind)||id.index<0||id.index>=MapSelection.Count(map,id.kind))throw new InvalidOperationException("Элемент больше не существует. Выберите его снова.");selected.Add(id);
            }
            selected=MapTransforms.WithSectorThings(map,selected,includeSectorThings);var vertices=map.SelectedVertices(selected);var moved=new HashSet<int>();var nextVertices=new Dictionary<int,MapPoint>();var nextThings=new Dictionary<int,MapPoint>();
            foreach(int index in vertices){var before=map.vertices[index].Position;var next=grid.Snap(before);if(next.x!=before.x||next.y!=before.y){moved.Add(index);nextVertices[index]=next;}}
            foreach(var id in selected)if(id.kind==ElementKind.Thing){var before=map.things[id.index].Position;var next=grid.Snap(before);if(next.x!=before.x||next.y!=before.y)nextThings[id.index]=next;}
            var plan=new GridSnapPlan{original=map,VertexCount=nextVertices.Count,ThingCount=nextThings.Count};if(plan.ChangeCount==0)return plan;
            var prepared=map.Clone();foreach(var pair in nextVertices)prepared.vertices[pair.Key].Position=pair.Value;foreach(var pair in nextThings)prepared.things[pair.Key].Position=pair.Value;
            if(moved.Count>0)MapStitch.SnapMovedVertices(prepared,moved);prepared.Validate();plan.prepared=prepared;return plan;
        }
    }
    public static partial class MapStitch
    {
        struct SnapKey : IEquatable<SnapKey>
        {
            public float x,y;public SnapKey(MapPoint p){x=p.x;y=p.y;}
            public bool Equals(SnapKey other){return x==other.x&&y==other.y;}public override bool Equals(object value){return value is SnapKey&&Equals((SnapKey)value);}
            public override int GetHashCode(){return x.GetHashCode()*397^y.GetHashCode();}
        }
        static void SnapVertexGroups(MapDocument source,MapDocument output,HashSet<int> moving,int[] remap)
        {
            var groups=new Dictionary<SnapKey,List<int>>();foreach(int index in moving)groups[new SnapKey(source.vertices[index].Position)]=new List<int>();
            for(int i=0;i<source.vertices.Count;i++){List<int> group;if(groups.TryGetValue(new SnapKey(source.vertices[i].Position),out group))group.Add(i);}
            foreach(var group in groups.Values)if(group.Count>1)
            {int keep=group[0];foreach(int index in group)if(index!=keep){remap[index]=keep;MergeFields(output.vertices[index],output.vertices[keep],false);}}
        }
        static void RepairSnappedSectors(MapDocument source,MapDocument output,HashSet<int> moving,int[] remap)
        {
            var roots=new HashSet<int>();foreach(int index in moving)roots.Add(remap[index]);var affected=new HashSet<int>();
            foreach(var line in source.lines)if(roots.Contains(remap[line.start])||roots.Contains(remap[line.end]))
            {if(line.front>=0)affected.Add(source.sides[line.front].sector);if(line.back>=0)affected.Add(source.sides[line.back].sector);}
            var counts=new int[output.sectors.Count];var minX=new float[counts.Length];var minY=new float[counts.Length];var maxX=new float[counts.Length];var maxY=new float[counts.Length];
            for(int i=0;i<counts.Length;i++){minX[i]=minY[i]=float.MaxValue;maxX[i]=maxY[i]=float.MinValue;}
            foreach(var line in output.lines)foreach(int side in new[]{line.front,line.back})if(side>=0)
            {
                int sector=output.sides[side].sector;counts[sector]++;foreach(int vertex in new[]{line.start,line.end}){var p=output.vertices[vertex];minX[sector]=Math.Min(minX[sector],p.x);maxX[sector]=Math.Max(maxX[sector],p.x);minY[sector]=Math.Min(minY[sector],p.y);maxY[sector]=Math.Max(maxY[sector],p.y);}
            }
            var bad=new HashSet<int>();foreach(int sector in affected)if(counts[sector]<3||minX[sector]>=maxX[sector]||minY[sector]>=maxY[sector])bad.Add(sector);
            foreach(int sector in bad)
            {
                int neighbor=-1;
                if(minX[sector]<maxX[sector]&&minY[sector]<maxY[sector])foreach(var line in output.lines)if(line.front>=0&&line.back>=0)
                {int f=output.sides[line.front].sector,b=output.sides[line.back].sector;if(f==sector&&!bad.Contains(b)){neighbor=b;break;}if(b==sector&&!bad.Contains(f)){neighbor=f;break;}}
                if(neighbor>=0){foreach(var side in output.sides)if(side.sector==sector)side.sector=neighbor;continue;}
                foreach(var line in output.lines)
                {
                    if(line.front>=0&&output.sides[line.front].sector==sector)line.front=-1;if(line.back>=0&&output.sides[line.back].sector==sector)line.back=-1;
                    if(line.front<0&&line.back>=0){int swap=line.start;line.start=line.end;line.end=swap;line.SwapSides();}
                    if(line.front>=0&&line.back<0)line.flags=(line.flags&~4)|1;
                }
            }
        }
        internal static void SnapMovedVertices(MapDocument map,HashSet<int> moving)
        {
            var result=ClassicGeometry(map,moving,null,true);map.vertices=result.map.vertices;map.lines=result.map.lines;map.sides=result.map.sides;map.sectors=result.map.sectors;
        }
    }
}
