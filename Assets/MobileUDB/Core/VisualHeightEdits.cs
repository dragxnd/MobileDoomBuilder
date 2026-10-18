using System;
using System.Collections.Generic;

namespace MobileUDB.Core
{
    public enum HeightWallMode {Ignore,Floor,Ceiling,Both}
    public sealed class VisualHeightPlan
    {
        internal MapDocument map;internal readonly List<Action<MapDocument>> writes=new List<Action<MapDocument>>();
        internal readonly HashSet<SurfaceRef> retained=new HashSet<SurfaceRef>();bool applied;
        internal byte[] originalState;
        public int ChangeCount {get{return writes.Count;}}
        // Prepared and applied immediately, without an intervening map edit.
        public SurfaceBatchResult Apply(MapDocument target)
        {
            if(applied||!ReferenceEquals(map,target))throw new InvalidOperationException("Карта изменилась. Откройте высоты снова.");
            if(originalState!=null){var current=BinaryMapState.Capture(target);if(current.Length!=originalState.Length)throw new InvalidOperationException("Карта изменилась. Откройте высоты снова.");for(int i=0;i<current.Length;i++)if(current[i]!=originalState[i])throw new InvalidOperationException("Карта изменилась. Откройте высоты снова.");}
            applied=true;
            foreach(var write in writes)write(target);var result=new SurfaceBatchResult{changed=ChangeCount};foreach(var id in retained)result.rebound[id]=id;return result;
        }
    }
    public static class VisualHeightEdits
    {
        static void Coordinate(float value)
        {if(float.IsNaN(value)||float.IsInfinity(value)||value<-32768||value>32767||value!=(float)Math.Truncate(value))throw new InvalidOperationException("Высота должна быть целым числом от −32768 до 32767.");}
        static VisualHeightPlan Targets(MapDocument map,IEnumerable<SurfaceRef> surfaces,HeightWallMode walls,out Dictionary<int,int> planes)
        {
            if(map==null||(map.format!=MapFormat.Doom&&map.format!=MapFormat.Hexen))throw new InvalidOperationException("Поддерживаются бинарные карты Doom / Hexen.");
            if(!Enum.IsDefined(typeof(HeightWallMode),walls))throw new ArgumentOutOfRangeException("walls");
            if(surfaces==null)throw new ArgumentNullException("surfaces");var plan=new VisualHeightPlan{map=map};planes=new Dictionary<int,int>();
            foreach(var id in surfaces)
            {
                if(!SurfaceTextureEdits.Valid(map,id))throw new InvalidOperationException("Поверхность больше не существует. Выберите её снова.");plan.retained.Add(id);
                int mask=id.IsFlat?(id.part==SurfacePart.Floor?1:2):walls==HeightWallMode.Floor?1:walls==HeightWallMode.Ceiling?2:walls==HeightWallMode.Both?3:0;
                if(mask!=0){int old;planes.TryGetValue(id.sector,out old);planes[id.sector]=old|mask;}
            }
            return plan;
        }
        static void Sector(VisualHeightPlan plan,int index,float floor,float ceiling)
        {
            Coordinate(floor);Coordinate(ceiling);if(floor>ceiling)throw new InvalidOperationException("Изменение поднимает пол выше потолка.");
            var old=plan.map.sectors[index];bool changeFloor=old.floor!=floor,changeCeiling=old.ceiling!=ceiling;
            if(changeFloor||changeCeiling)plan.writes.Add(m=>{if(changeFloor)m.sectors[index].floor=floor;if(changeCeiling)m.sectors[index].ceiling=ceiling;});
        }
        public static VisualHeightPlan Shift(MapDocument map,IEnumerable<SurfaceRef> surfaces,IEnumerable<int> things,int amount,HeightWallMode walls=HeightWallMode.Ignore,Func<int,bool> hangsByType=null)
        {
            Dictionary<int,int> planes;var plan=Targets(map,surfaces,walls,out planes);
            foreach(var pair in planes){var sector=map.sectors[pair.Key];Sector(plan,pair.Key,sector.floor+((pair.Value&1)!=0?amount:0),sector.ceiling+((pair.Value&2)!=0?amount:0));}
            var seen=new HashSet<int>();if(things!=null)foreach(int index in things)
            {
                if(index<0||index>=map.things.Count||map.things[index]==null)throw new InvalidOperationException("Объект больше не существует. Выберите его снова.");
                if(!seen.Add(index)||map.format==MapFormat.Doom)continue;var thing=map.things[index];float z=thing.z+(hangsByType!=null&&hangsByType(thing.type)?-(float)amount:amount);Coordinate(z);
                if(z!=thing.z)plan.writes.Add(m=>m.things[index].z=z);
            }
            return plan;
        }
        // Upstream binary neighbors include sectors touching selected boundary vertices.
        static HashSet<int> Around(MapDocument map,HashSet<int> selected)
        {
            var vertices=new HashSet<int>();foreach(var line in map.lines)
                if((line.front>=0&&selected.Contains(map.sides[line.front].sector))||(line.back>=0&&selected.Contains(map.sides[line.back].sector))){vertices.Add(line.start);vertices.Add(line.end);}
            var result=new HashSet<int>();foreach(var line in map.lines)if(vertices.Contains(line.start)||vertices.Contains(line.end))
            {if(line.front>=0&&!selected.Contains(map.sides[line.front].sector))result.Add(map.sides[line.front].sector);if(line.back>=0&&!selected.Contains(map.sides[line.back].sector))result.Add(map.sides[line.back].sector);}
            return result;
        }
        static float Level(MapDocument map,HashSet<int> selected,bool ceiling,bool up,bool within)
        {
            if(within&&selected.Count<2)throw new InvalidOperationException("Внутри выделения нужны как минимум два пола или два потолка.");
            float edge=up?float.MinValue:float.MaxValue,limit=ceiling?float.MinValue:float.MaxValue;
            foreach(int index in selected)
            {var s=map.sectors[index];float height=ceiling?s.ceiling:s.floor;edge=up?Math.Max(edge,height):Math.Min(edge,height);limit=ceiling?Math.Max(limit,s.floor):Math.Min(limit,s.ceiling);}
            if(within)return edge;float target=up?float.MaxValue:float.MinValue;
            foreach(int index in Around(map,selected))
            {
                var s=map.sectors[index];foreach(float height in new[]{s.floor,s.ceiling})
                {
                    if((up?height<=edge:height>=edge)||(!ceiling&&up&&height>limit)||(ceiling&&!up&&height<limit))continue;
                    target=up?Math.Min(target,height):Math.Max(target,height);
                }
            }
            if(target==(up?float.MaxValue:float.MinValue))
            {
                if(!ceiling&&up&&limit>edge)return limit;if(ceiling&&!up&&limit<edge)return limit;
                throw new InvalidOperationException("Нет подходящего соседнего уровня "+(up?"выше.":"ниже."));
            }
            return target;
        }
        public static VisualHeightPlan Nearest(MapDocument map,IEnumerable<SurfaceRef> surfaces,bool up,bool withinSelection=false)
        {
            Dictionary<int,int> planes;var plan=Targets(map,surfaces,HeightWallMode.Ignore,out planes);var floors=new HashSet<int>();var ceilings=new HashSet<int>();
            foreach(var pair in planes){if((pair.Value&1)!=0)floors.Add(pair.Key);if((pair.Value&2)!=0)ceilings.Add(pair.Key);}
            if(floors.Count+ceilings.Count==0)throw new InvalidOperationException("Отметьте пол или потолок.");
            float floor=floors.Count>0?Level(map,floors,false,up,withinSelection):0,ceiling=ceilings.Count>0?Level(map,ceilings,true,up,withinSelection):0;
            foreach(var pair in planes){var sector=map.sectors[pair.Key];Sector(plan,pair.Key,(pair.Value&1)!=0?floor:sector.floor,(pair.Value&2)!=0?ceiling:sector.ceiling);}
            return plan;
        }
        public static VisualHeightPlan NearestTargets(MapDocument map,IEnumerable<SurfaceRef> surfaces,IEnumerable<int> things,bool up,bool withinSelection,Func<int,ThingType> findType)
        {
            if(things==null||findType==null)throw new ArgumentNullException(things==null?"things":"findType");
            Dictionary<int,int> planes;var plan=Targets(map,surfaces,HeightWallMode.Ignore,out planes);
            var targets=new List<int>(things);
            if(planes.Count==0&&(targets.Count==0||map.format==MapFormat.Doom))throw new InvalidOperationException("Отметьте пол, потолок или объекты с высотой Hexen.");
            var working=map.Clone();
            // UDB moves planes first, then processes things in selection order.
            if(planes.Count>0)Nearest(working,plan.retained,up,withinSelection).Apply(working);
            ThingHeightAlignment.ApplyOrdered(working,targets,up,findType);
            foreach(var pair in planes){var s=working.sectors[pair.Key];Sector(plan,pair.Key,s.floor,s.ceiling);}
            foreach(int index in new HashSet<int>(targets))if(working.things[index].z!=map.things[index].z)
            {float z=working.things[index].z;plan.writes.Add(m=>m.things[index].z=z);}
            plan.originalState=BinaryMapState.Capture(map);return plan;
        }
    }
}
