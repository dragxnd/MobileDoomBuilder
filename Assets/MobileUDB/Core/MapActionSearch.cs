// GPL-3.0. FindLinedefTypes, FindThingAction, FindSectorEffect search semantics.
using System;using System.Collections.Generic;
namespace MobileUDB.Core
{
    public sealed class ElementSearchQuery
    {
        public ElementSearchKind kind;public int code;public bool nativeGeneralized;
        // Five Hexen byte arguments; -1 means any (search) / leave unchanged (replace).
        public int[] arguments;
        public ElementSearchQuery Validated(MapDocument map,bool replacement=false)
        {
            ThingClipboard.Binary(map);if(!Enum.IsDefined(typeof(ElementSearchKind),kind))throw new ArgumentOutOfRangeException("kind");
            int min=replacement||kind==ElementSearchKind.ThingType?0:-1;
            if(code<min||code>MapElementSearch.Maximum(map.format,kind))throw new InvalidOperationException("Код должен быть от "+min+" до "+MapElementSearch.Maximum(map.format,kind)+".");
            if(kind==ElementSearchKind.ThingAction&&map.format==MapFormat.Doom)throw new InvalidOperationException("В формате Doom у объектов нет действий.");
            if(arguments!=null)
            {
                if(map.format!=MapFormat.Hexen||kind!=ElementSearchKind.LineAction&&kind!=ElementSearchKind.ThingAction||arguments.Length!=5)throw new InvalidOperationException("Пять аргументов доступны для действий Hexen.");
                foreach(int value in arguments)if(value< -1||value>255)throw new InvalidOperationException("Аргумент: пусто или 0–255.");
            }
            return new ElementSearchQuery{kind=kind,code=code,nativeGeneralized=nativeGeneralized,arguments=arguments==null?null:(int[])arguments.Clone()};
        }
        public string Caption()
        {
            string text=code==-1?"Любой ненулевой код":("Код "+code);
            if(nativeGeneralized&&(kind==ElementSearchKind.LineAction||kind==ElementSearchKind.SectorEffect))text+=" · как UDB";
            if(arguments!=null){var values=new string[5];for(int i=0;i<5;i++)values[i]=arguments[i]<0?"*":arguments[i].ToString();text+=" · арг.: "+string.Join(" / ",values);}return text;
        }
    }
    public static class MapActionSearch
    {
        // Native linedef search deliberately accepts ANY common enabled option value,
        // including matching values in different generalized categories. This is not
        // a bit-mask subset test; preserve UDB's actual GetGeneralizedBits/BitsMatch.
        static HashSet<int> LineBits(ActionCatalog catalog,int code)
        {
            var bits=new HashSet<int>();if(catalog==null)return bits;
            foreach(var category in catalog.generalizedLines)if(category.Contains(code))
            {int value=code-category.offset;foreach(var option in category.options)foreach(var bit in option.choices)if(bit.value>0&&(value&bit.value)==bit.value)bits.Add(bit.value);}
            return bits;
        }
        sealed class SectorData {internal int basic;internal HashSet<int> bits=new HashSet<int>();}
        static SectorData SectorBits(ActionCatalog catalog,int code)
        {
            var data=new SectorData();if(code<=0)return data;int current=code;
            if(catalog!=null)for(int i=catalog.generalizedSectors.Count-1;i>=0;i--)
                for(int j=catalog.generalizedSectors[i].choices.Count-1;j>=0;j--)
                {int bit=catalog.generalizedSectors[i].choices[j].value;if(bit>0&&(current&bit)==bit){current-=bit;data.bits.Add(bit);}}
            if(current>0)data.basic=current;return data;
        }
        public static bool CodeMatches(ElementSearchQuery query,int candidate,ActionCatalog catalog)
        {
            if(query.code==-1)return candidate>0;if(query.code==candidate)return true;if(!query.nativeGeneralized||catalog==null)return false;
            if(query.kind==ElementSearchKind.LineAction)
            {var expected=LineBits(catalog,query.code);return expected.Count>0&&expected.Overlaps(LineBits(catalog,candidate));}
            if(query.kind==ElementSearchKind.SectorEffect&&query.code>0&&candidate>0&&catalog.generalizedSectors.Count>0)
            {var expected=SectorBits(catalog,query.code);var actual=SectorBits(catalog,candidate);if(expected.basic!=0&&expected.bits.Count>0)return expected.basic==actual.basic&&expected.bits.IsSubsetOf(actual.bits);if(expected.bits.Count>0)return expected.bits.IsSubsetOf(actual.bits);return expected.basic==actual.basic;}
            return false;
        }
        static ElementKind Kind(ElementSearchKind search)=>search==ElementSearchKind.LineAction?ElementKind.Line:search==ElementSearchKind.SectorEffect?ElementKind.Sector:ElementKind.Thing;
        static int Code(MapDocument map,ElementRef hit,ElementSearchKind search)=>search==ElementSearchKind.LineAction?map.lines[hit.index].action:search==ElementSearchKind.SectorEffect?map.sectors[hit.index].special:search==ElementSearchKind.ThingAction?map.things[hit.index].action:map.things[hit.index].type;
        static int[] Args(MapDocument map,ElementRef hit)=>hit.kind==ElementKind.Line?map.lines[hit.index].args:map.things[hit.index].args;
        public static List<ElementRef> Find(MapDocument map,ElementSearchQuery query,ActionCatalog catalog,HashSet<ElementRef> scope=null)
        {
            query=query.Validated(map);var result=new List<ElementRef>();var matches=new Dictionary<int,bool>();var kind=Kind(query.kind);
            for(int i=0;i<MapSelection.Count(map,kind);i++)
            {
                var hit=new ElementRef(kind,i);if(scope!=null&&!scope.Contains(hit))continue;int code=Code(map,hit,query.kind);bool match;
                if(!matches.TryGetValue(code,out match)){match=CodeMatches(query,code,catalog);matches[code]=match;}if(!match)continue;
                if(query.arguments!=null){var args=Args(map,hit);for(int a=0;a<5;a++)if(query.arguments[a]>=0&&query.arguments[a]!=args[a]){match=false;break;}}
                if(match)result.Add(hit);
            }
            return result;
        }
        public static int Replace(MapDocument map,ElementSearchQuery query,ElementSearchQuery replacement,ActionCatalog catalog,HashSet<ElementRef> scope)
        {
            query=query.Validated(map);replacement=replacement.Validated(map,true);if(query.kind!=replacement.kind)throw new InvalidOperationException("Вид замены не совпадает с поиском.");
            int changed=0;foreach(var hit in Find(map,query,catalog,scope))
            {
                bool different=Code(map,hit,query.kind)!=replacement.code;
                if(replacement.arguments!=null){var args=Args(map,hit);for(int a=0;a<5;a++)if(replacement.arguments[a]>=0&&args[a]!=replacement.arguments[a]){different=true;break;}}
                if(!different)continue;
                switch(query.kind){case ElementSearchKind.LineAction:map.lines[hit.index].action=replacement.code;break;case ElementSearchKind.SectorEffect:map.sectors[hit.index].special=replacement.code;break;case ElementSearchKind.ThingAction:map.things[hit.index].action=replacement.code;break;default:map.things[hit.index].type=replacement.code;break;}
                if(replacement.arguments!=null){var args=Args(map,hit);for(int a=0;a<5;a++)if(replacement.arguments[a]>=0)args[a]=replacement.arguments[a];}changed++;
            }
            return changed;
        }
    }
}
