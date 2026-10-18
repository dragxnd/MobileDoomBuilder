using System;
using System.Collections.Generic;

namespace MobileUDB.Core
{
    [Serializable] public sealed class MapTagLabel
    {public int tag;public string label;public MapTagLabel(int tag,string label){this.tag=tag;this.label=label;}}
    public static class MapTagLabels
    {
        public static void Validate(IReadOnlyList<MapTagLabel> labels)
        {
            if(labels==null)return;var tags=new HashSet<int>();
            foreach(var entry in labels)if(entry==null||entry.tag==0||string.IsNullOrEmpty(entry.label)||!tags.Add(entry.tag))throw new InvalidOperationException("Некорректные или повторные подписи тегов карты.");
        }
        public static List<MapTagLabel> Copy(IReadOnlyList<MapTagLabel> labels)
        {if(labels==null)return null;var copy=new List<MapTagLabel>();foreach(var entry in labels)copy.Add(new MapTagLabel(entry.tag,entry.label));return copy;}
        public static string Find(MapDocument map,int tag)
        {if(map.tagLabels!=null)foreach(var entry in map.tagLabels)if(entry.tag==tag)return entry.label;return null;}
        public static TagLabelPlan Set(MapDocument map,int tag,string label)
        {
            if(tag==0)throw new InvalidOperationException("У нулевого тега нет подписи.");
            label=label==null?"":label.Trim();var next=Copy(map.tagLabels)??new List<MapTagLabel>();int index=next.FindIndex(e=>e.tag==tag);
            if(label.Length==0){if(index>=0)next.RemoveAt(index);}
            else if(index>=0)next[index].label=label;else next.Add(new MapTagLabel(tag,label));
            return new TagLabelPlan(map,next);
        }
        public static TagLabelPlan Import(MapDocument map,IReadOnlyList<MapTagLabel> labels,bool replace)
        {
            Validate(labels);var next=replace?Copy(labels)??new List<MapTagLabel>():Copy(map.tagLabels)??new List<MapTagLabel>();
            if(!replace&&labels!=null)foreach(var entry in labels){int index=next.FindIndex(e=>e.tag==entry.tag);if(index>=0)next[index].label=entry.label;else next.Add(new MapTagLabel(entry.tag,entry.label));}
            return new TagLabelPlan(map,next);
        }
    }
    public sealed class TagLabelPlan
    {
        readonly MapDocument original;readonly byte[] state;readonly List<MapTagLabel> next;bool applied;
        public int ChangeCount {get;private set;}
        internal TagLabelPlan(MapDocument map,List<MapTagLabel> next)
        {
            ThingClipboard.Binary(map);map.Validate();MapTagLabels.Validate(next);original=map;this.next=MapTagLabels.Copy(next);state=BinaryMapState.Capture(map);
            var before=new Dictionary<int,string>();if(map.tagLabels!=null)foreach(var e in map.tagLabels)before.Add(e.tag,e.label);
            foreach(var e in next){string old;if(!before.TryGetValue(e.tag,out old)||old!=e.label)ChangeCount++;before.Remove(e.tag);}ChangeCount+=before.Count;
        }
        public int Apply(MapDocument map)
        {
            if(applied||!ReferenceEquals(map,original))throw new InvalidOperationException("Карта изменилась. Откройте подписи снова.");
            var current=BinaryMapState.Capture(map);if(current.Length!=state.Length)throw new InvalidOperationException("Карта изменилась. Откройте подписи снова.");
            for(int i=0;i<current.Length;i++)if(current[i]!=state[i])throw new InvalidOperationException("Карта изменилась. Откройте подписи снова.");
            applied=true;if(ChangeCount>0)map.tagLabels=MapTagLabels.Copy(next);return ChangeCount;
        }
    }
}
