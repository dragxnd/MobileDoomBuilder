// GPL-3.0. ThingsMode Shift clone-drag and binary path-point insertion.
using System;
using System.Collections.Generic;

namespace MobileUDB.Core
{
    public static class ThingDuplication
    {
        public static ThingDuplicatePlan Prepare(MapDocument map,IEnumerable<int> indices,ThingCatalog catalog,ActionCatalog actions=null)
        {
            ThingClipboard.Binary(map);map.Validate();if(indices==null)throw new ArgumentNullException("indices");
            var targets=new List<int>();var seen=new HashSet<int>();foreach(int index in indices)
            {if(index<0||index>=map.things.Count)throw new InvalidOperationException("Выбранный объект отсутствует.");if(seen.Add(index))targets.Add(index);}
            if(targets.Count==0)throw new InvalidOperationException("Выберите объекты для дублирования.");
            var prepared=map.Clone();var copies=new List<MapThing>();var links=new List<int>();
            foreach(int index in targets)
            {
                var source=prepared.things[index];ThingClipboard.Coordinate(source.x);ThingClipboard.Coordinate(source.y);if(map.format==MapFormat.Hexen)ThingClipboard.Coordinate(source.z);ThingClipboard.Properties(source,map.format);
                var copy=ThingClipboard.Copy(source);prepared.things.Add(copy);
                // Class names, rather than fixed DoomEdNums, also support MAPINFO remaps.
                string name=catalog==null?"":catalog.Find(source.type).className??"";int argument=-1;
                if(map.format==MapFormat.Hexen)
                {if(string.Equals(name,"InterpolationPoint",StringComparison.OrdinalIgnoreCase))argument=3;else if(string.Equals(name,"PatrolPoint",StringComparison.OrdinalIgnoreCase))argument=0;}
                if(argument>=0)
                {
                    if(source.tag==0)source.tag=PasteTags.NextFree(prepared,actions,65535);
                    int tag=PasteTags.NextFree(prepared,actions,65535);
                    // Hexen stores this next-point argument in one byte. Refuse a
                    // truncated path link rather than silently exporting a different ID.
                    if(tag>255)throw new InvalidOperationException("Для связи точки пути нет свободного тега 1–255.");
                    source.args[argument]=copy.tag=tag;links.Add(index);
                }
                copies.Add(copy);
            }
            return new ThingDuplicatePlan(map,prepared,targets,copies,links);
        }
    }
    public sealed class ThingDuplicatePlan
    {
        readonly MapDocument original,prepared;readonly List<int> targets,links;readonly List<MapThing> copies;readonly byte[] state;bool used;
        public int Count {get{return copies.Count;}}
        public int PathCount {get{return links.Count;}}
        public IReadOnlyList<int> Targets {get{return targets.AsReadOnly();}}
        internal ThingDuplicatePlan(MapDocument original,MapDocument prepared,List<int> targets,List<MapThing> copies,List<int> links)
        {this.original=original;this.prepared=prepared;this.targets=targets;this.copies=copies;this.links=links;state=BinaryMapState.Capture(original);}
        public MapPoint Anchor(MapPoint near)
        {
            if(!ThingOrientation.Finite(near.x)||!ThingOrientation.Finite(near.y))throw new InvalidOperationException("Некорректная опора копии.");
            MapPoint anchor=copies[0].Position;double distance=double.MaxValue;
            // The mobile command has no held mouse point. Use the nearest selected
            // thing to the view center, then keep that anchor through placement.
            foreach(var thing in copies){double x=(double)thing.x-near.x,y=(double)thing.y-near.y,d=x*x+y*y;if(d<distance){distance=d;anchor=thing.Position;}}
            return anchor;
        }
        List<MapThing> Placed(MapPoint offset)
        {
            if(!ThingOrientation.Finite(offset.x)||!ThingOrientation.Finite(offset.y))throw new InvalidOperationException("Некорректное смещение копии.");
            var result=new List<MapThing>();foreach(var source in copies)
            {var thing=ThingClipboard.Copy(source);thing.x=(float)Math.Round((double)source.x+offset.x);thing.y=(float)Math.Round((double)source.y+offset.y);ThingClipboard.Coordinate(thing.x);ThingClipboard.Coordinate(thing.y);result.Add(thing);}return result;
        }
        public MapDocument Preview(MapPoint offset)
        {var preview=new MapDocument{format=original.format,name=original.name};preview.things.AddRange(Placed(offset));return preview;}
        public List<int> Apply(MapDocument map,MapPoint offset)
        {
            if(used||!ReferenceEquals(map,original))throw new InvalidOperationException("Карта изменилась или копия уже применена.");
            byte[] current=BinaryMapState.Capture(map);if(current.Length!=state.Length)throw new InvalidOperationException("Карта изменилась. Дублируйте объекты снова.");
            for(int i=0;i<state.Length;i++)if(state[i]!=current[i])throw new InvalidOperationException("Карта изменилась. Дублируйте объекты снова.");
            var placed=Placed(offset);var validation=prepared.Clone();validation.things.RemoveRange(original.things.Count,copies.Count);validation.things.AddRange(placed);validation.Validate();
            // Finish all validation before modifying path links or adding objects.
            foreach(int index in links){map.things[index].tag=prepared.things[index].tag;map.things[index].args=(int[])prepared.things[index].args.Clone();}
            var selected=new List<int>();foreach(var thing in placed){selected.Add(map.things.Count);map.things.Add(thing);}used=true;return selected;
        }
    }
}
