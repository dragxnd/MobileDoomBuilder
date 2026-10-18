using System;
using System.Collections.Generic;
using System.Globalization;

namespace MobileUDB.Core
{
    public sealed class MapTagTarget
    {
        public int tag,count,firstIndex;
        public ElementKind kind;
    }
    public static class MapTagTargets
    {
        // UniversalType: SectorTag=13, ThingTag=14, LinedefTag=15.
        public static List<MapTagTarget> Find(MapDocument map,int type,Func<int,bool> acceptedThingType=null,ActionCatalog actions=null)
        {
            if(type<13||type>15)throw new ArgumentOutOfRangeException("type");
            var found=new Dictionary<int,MapTagTarget>();
            Action<int,ElementKind,int> add=(tag,kind,index)=>
            {
                if(tag==0)return;MapTagTarget entry;
                if(!found.TryGetValue(tag,out entry)){entry=new MapTagTarget{tag=tag,kind=kind,firstIndex=index};found.Add(tag,entry);}
                entry.count++;
            };
            if(type==13)for(int i=0;i<map.sectors.Count;i++)foreach(int tag in Tags(map.sectors[i],map.sectors[i].tag,map.format))add(tag,ElementKind.Sector,i);
            if(type==14&&map.format!=MapFormat.Doom)for(int i=0;i<map.things.Count;i++)
                if(acceptedThingType==null||acceptedThingType(map.things[i].type))add(map.things[i].tag,ElementKind.Thing,i);
            if(type==15&&map.format!=MapFormat.Hexen)for(int i=0;i<map.lines.Count;i++)foreach(int tag in Tags(map.lines[i],map.lines[i].tag,map.format))add(tag,ElementKind.Line,i);
            if(type==15&&map.format==MapFormat.Hexen)for(int i=0;i<map.lines.Count;i++)
            {
                var encoding=HexenLineTags.Encoding(map.lines[i],actions);
                if(encoding!=null&&encoding.type==15)add(encoding.Read(map.lines[i]),ElementKind.Line,i);
            }
            var result=new List<MapTagTarget>(found.Values);result.Sort((a,b)=>a.tag.CompareTo(b.tag));return result;
        }
        static HashSet<int> Tags(MapElementData element,int primary,MapFormat format)
        {
            var tags=new HashSet<int>{primary};if(format!=MapFormat.Udmf)return tags;
            string raw=UdmfText.Raw(element.fields,"moreids");if(raw==null)return tags;
            foreach(string token in UdmfText.DecodeString(raw).Split((char[])null,StringSplitOptions.RemoveEmptyEntries))
            {
                int tag;if(!int.TryParse(token,NumberStyles.Integer,CultureInfo.InvariantCulture,out tag))throw new InvalidOperationException("Некорректный moreids: "+token);
                tags.Add(tag);
            }
            return tags;
        }
    }
}
