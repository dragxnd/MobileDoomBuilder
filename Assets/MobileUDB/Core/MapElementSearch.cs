using System;
using System.Collections.Generic;

namespace MobileUDB.Core
{
    public enum ElementSearchKind { ThingType, LineAction, SectorEffect, ThingAction }
    public static class MapElementSearch
    {
        public static int Maximum(MapFormat format,ElementSearchKind search)
        {return format==MapFormat.Udmf?int.MaxValue:format==MapFormat.Hexen&&(search==ElementSearchKind.LineAction||search==ElementSearchKind.ThingAction)?255:65535;}
        // Code-only replacement matches UDB's replacement with no explicit argument values.
        public static int Replace(MapDocument map,ElementSearchKind search,int code,int replacement,HashSet<ElementRef> scope=null)
        {
            if(!Enum.IsDefined(typeof(ElementSearchKind),search))throw new ArgumentOutOfRangeException("search");
            int minimum=map.format==MapFormat.Udmf?int.MinValue:0,maximum=Maximum(map.format,search);
            if(replacement<minimum||replacement>maximum)throw new InvalidOperationException("Код замены должен быть от "+minimum+" до "+maximum+".");
            if(search==ElementSearchKind.ThingAction&&map.format==MapFormat.Doom)throw new InvalidOperationException("В формате Doom у объектов нет действий.");
            if(code==replacement)return 0;
            var found=Find(map,search,code,scope);
            foreach(var hit in found)
            {
                switch(search)
                {
                    case ElementSearchKind.ThingType:map.things[hit.index].type=replacement;break;
                    case ElementSearchKind.LineAction:map.lines[hit.index].action=replacement;break;
                    case ElementSearchKind.SectorEffect:map.sectors[hit.index].special=replacement;break;
                    case ElementSearchKind.ThingAction:map.things[hit.index].action=replacement;break;
                }
            }
            return found.Count;
        }
        // Exact codes include zero and unknown extensions. Scope contains element identities,
        // not geometry-derived neighbours, so searching a selection never widens it silently.
        public static List<ElementRef> Find(MapDocument map,ElementSearchKind search,int code,HashSet<ElementRef> scope=null)
        {
            if(!Enum.IsDefined(typeof(ElementSearchKind),search))throw new ArgumentOutOfRangeException("search");
            var found=new List<ElementRef>();
            if(search==ElementSearchKind.ThingAction&&map.format==MapFormat.Doom)return found;
            ElementKind kind=search==ElementSearchKind.LineAction?ElementKind.Line:search==ElementSearchKind.SectorEffect?ElementKind.Sector:ElementKind.Thing;
            int count=kind==ElementKind.Line?map.lines.Count:kind==ElementKind.Sector?map.sectors.Count:map.things.Count;
            for(int i=0;i<count;i++)
            {
                var element=new ElementRef(kind,i);if(scope!=null&&!scope.Contains(element))continue;
                int value=search==ElementSearchKind.LineAction?map.lines[i].action:search==ElementSearchKind.SectorEffect?map.sectors[i].special:search==ElementSearchKind.ThingAction?map.things[i].action:map.things[i].type;
                if(value==code)found.Add(element);
            }
            return found;
        }
    }
}
