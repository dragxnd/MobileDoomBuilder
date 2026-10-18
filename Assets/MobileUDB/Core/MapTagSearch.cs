// GPL-3.0. Native FindLinedefTags / FindSectorTags / FindThingTag binary fields.
using System;using System.Collections.Generic;
namespace MobileUDB.Core
{
    public static class MapTagSearch
    {
        public static bool Supported(MapFormat format,ElementKind kind)=>kind==ElementKind.Sector&&(format==MapFormat.Doom||format==MapFormat.Hexen)||kind==ElementKind.Line&&format==MapFormat.Doom||kind==ElementKind.Thing&&format==MapFormat.Hexen;
        // Native FindThingTag replacement is Int16-capped, although its search and
        // Hexen's actual TID field cover UInt16. Keep the original search behavior.
        public static int MaximumReplacement(ElementKind kind)=>kind==ElementKind.Thing?short.MaxValue:ushort.MaxValue;
        static void Validate(MapDocument map,ElementKind kind,int tag,bool replacement=false)
        {
            ThingClipboard.Binary(map);if(!Supported(map.format,kind))throw new InvalidOperationException("Этот вид тегов недоступен в текущем формате.");
            int maximum=replacement?MaximumReplacement(kind):ushort.MaxValue;
            if(tag<0||tag>maximum)throw new InvalidOperationException("Номер должен быть от 0 до "+maximum+".");
        }
        public static int Tag(MapDocument map,ElementRef hit)=>hit.kind==ElementKind.Line?map.lines[hit.index].tag:hit.kind==ElementKind.Sector?map.sectors[hit.index].tag:map.things[hit.index].tag;
        public static List<ElementRef> Find(MapDocument map,ElementKind kind,int tag,HashSet<ElementRef> scope=null)
        {
            Validate(map,kind,tag);var result=new List<ElementRef>();for(int i=0;i<MapSelection.Count(map,kind);i++){var hit=new ElementRef(kind,i);if((scope==null||scope.Contains(hit))&&Tag(map,hit)==tag)result.Add(hit);}return result;
        }
        public static int Replace(MapDocument map,ElementKind kind,int tag,int replacement,HashSet<ElementRef> reviewed)
        {
            Validate(map,kind,tag);Validate(map,kind,replacement,true);if(tag==replacement)return 0;
            var found=Find(map,kind,tag,reviewed);foreach(var hit in found)switch(kind){case ElementKind.Line:map.lines[hit.index].tag=replacement;break;case ElementKind.Sector:map.sectors[hit.index].tag=replacement;break;case ElementKind.Thing:map.things[hit.index].tag=replacement;break;}return found.Count;
        }
    }
}
