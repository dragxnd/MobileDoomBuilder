// GPL-3.0. LinedefsMode.DissolveItem / Tools.MergeInvalidSectors.
using System;
using System.Collections.Generic;

namespace MobileUDB.Core
{
    public sealed partial class MapDocument
    {
        struct DissolveBox
        {
            public bool found;public double minX,minY,maxX,maxY;
            public void Add(MapPoint p){if(!found){minX=maxX=p.x;minY=maxY=p.y;found=true;}else{minX=Math.Min(minX,p.x);minY=Math.Min(minY,p.y);maxX=Math.Max(maxX,p.x);maxY=Math.Max(maxY,p.y);}}
            public float Area {get{return found?(float)(maxX-minX)*(float)(maxY-minY):0;}}
            public MapPoint Center {get{return found?new MapPoint((float)minX+(float)(maxX-minX)/2,(float)minY+(float)(maxY-minY)/2):new MapPoint();}}
        }
        sealed class DissolveRepair {public MapSector record;public MapPoint center;}
        public LineDissolvePlan PrepareLineDissolve(IEnumerable<int> selected,DrawTextureOptions defaults=null,bool autoClearTextures=true)
        {
            ThingClipboard.Binary(this);if(selected==null)throw new ArgumentNullException("selected");
            var ordered=new List<int>();var seen=new HashSet<int>();foreach(int index in selected)
            {if(index<0||index>=lines.Count)throw new InvalidOperationException("Выбранная стена больше не существует.");if(seen.Add(index))ordered.Add(index);}
            if(ordered.Count==0)throw new InvalidOperationException("Сначала выделите стены.");Validate();
            var result=Clone();var boxes=new DissolveBox[sectors.Count];var counts=new int[sectors.Count];
            foreach(var line in result.lines)foreach(int side in new[]{line.front,line.back})if(side>=0)
            {int sector=result.sides[side].sector;counts[sector]++;boxes[sector].Add(result.vertices[line.start].Position);boxes[sector].Add(result.vertices[line.end].Position);}
            var repairs=new List<DissolveRepair>();var captured=new HashSet<int>();
            foreach(int index in ordered)foreach(int side in new[]{result.lines[index].front,result.lines[index].back})if(side>=0)
            {int sector=result.sides[side].sector;if(counts[sector]<4&&captured.Add(sector))repairs.Add(new DissolveRepair{record=result.sectors[sector],center=boxes[sector].Center});}
            var joined=new HashSet<int>();int joinCount=0,repairCount=0;var parents=new int[sectors.Count];for(int i=0;i<parents.Length;i++)parents[i]=i;
            foreach(int index in ordered)
            {
                var line=result.lines[index];if(line.front<0||line.back<0)continue;
                int front=DissolveRoot(parents,result.sides[line.front].sector),back=DissolveRoot(parents,result.sides[line.back].sector);if(front==back)continue;
                // Native BBox is cached until Map.Update after the whole action.
                // Do not recompute an expanded joined sector's area mid-selection.
                int target=boxes[front].Area>boxes[back].Area?front:back,source=target==front?back:front;
                parents[source]=target;
                joined.Add(source);joinCount++;
            }
            var mapping=Compact(result.sectors,joined);foreach(var side in result.sides)side.sector=mapping[DissolveRoot(parents,side.sector)];
            var removed=new HashSet<ElementRef>();foreach(int index in ordered)removed.Add(new ElementRef(ElementKind.Line,index));result.Delete(removed,autoClearTextures);
            foreach(var repair in repairs)
            {
                if(repair.record==null)continue;int sector=result.sectors.IndexOf(repair.record);if(sector<0)continue;int attached=0;
                foreach(var line in result.lines)foreach(int side in new[]{line.front,line.back})if(side>=0&&result.sides[side].sector==sector)attached++;
                if(attached==0||attached>=3)continue;
                // Sector.Dispose removes its sides without disposing their walls.
                // MakeSector may find a surrounding area at the old bbox center.
                result.DisposeDissolveSector(sector);
                if(!SectorCreation.HasPotentialSector(result,repair.center))continue;
                var current=new int[repairs.Count];for(int i=0;i<repairs.Count;i++)current[i]=repairs[i].record==null?-1:result.sectors.IndexOf(repairs[i].record);
                var built=SectorCreation.Build(result,repair.center,defaults,GameSky.FlatName(result),autoClearTextures);
                for(int i=0;i<repairs.Count;i++)repairs[i].record=current[i]<0||built.sectorRemap[current[i]]<0?null:built.map.sectors[built.sectorRemap[current[i]]];
                result=built.map;repairCount++;
            }
            result.Validate();return new LineDissolvePlan(this,result,ordered.Count,joinCount,repairCount);
        }
        static int DissolveRoot(int[] parents,int index)
        {int root=index;while(parents[root]!=root)root=parents[root];while(index!=root){int next=parents[index];parents[index]=root;index=next;}return root;}
        void DisposeDissolveSector(int sector)
        {
            var removedSides=new HashSet<int>();for(int i=0;i<sides.Count;i++)if(sides[i].sector==sector)removedSides.Add(i);
            foreach(var line in lines){if(removedSides.Contains(line.front))line.front=-1;if(removedSides.Contains(line.back))line.back=-1;}
            var sideMap=Compact(sides,removedSides);var sectorMap=Compact(sectors,new HashSet<int>{sector});
            foreach(var side in sides)side.sector=sectorMap[side.sector];foreach(var line in lines){if(line.front>=0)line.front=sideMap[line.front];if(line.back>=0)line.back=sideMap[line.back];}
        }
    }
    public sealed class LineDissolvePlan
    {
        readonly MapDocument original,prepared;readonly byte[] state;bool used;
        public int LineCount {get;private set;}public int JoinCount {get;private set;}public int RepairCount {get;private set;}
        internal LineDissolvePlan(MapDocument original,MapDocument prepared,int lines,int joins,int repairs)
        {this.original=original;this.prepared=prepared;LineCount=lines;JoinCount=joins;RepairCount=repairs;state=BinaryMapState.Capture(original);}
        public void Apply(MapDocument map)
        {
            if(used||!ReferenceEquals(map,original))throw new InvalidOperationException("Карта изменилась или операция уже применена.");
            var current=BinaryMapState.Capture(map);if(current.Length!=state.Length)throw new InvalidOperationException("Карта изменилась. Откройте растворение снова.");
            for(int i=0;i<state.Length;i++)if(current[i]!=state[i])throw new InvalidOperationException("Карта изменилась. Откройте растворение снова.");
            map.vertices=prepared.vertices;map.lines=prepared.lines;map.sides=prepared.sides;map.sectors=prepared.sectors;used=true;
        }
    }
}
