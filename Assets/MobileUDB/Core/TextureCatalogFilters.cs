// GPL-3.0. Uses UDB texture-set filters and the resolved resource identities.
using System;using System.Collections.Generic;using System.Text.RegularExpressions;
namespace MobileUDB.Core
{
    public sealed class TextureCategory
    {
        public readonly string name;
        public readonly HashSet<ResourceTexture> textures;
        public TextureCategory(string name,HashSet<ResourceTexture> textures){this.name=name;this.textures=textures;}
    }
    public static class TextureCatalogFilters
    {
        public static HashSet<ResourceTexture> Used(MapDocument map,DoomResources resources,ResourceKind kind,bool mixed=false)
        {
            var used=new HashSet<ResourceTexture>();
            Action<string,ResourceKind> add=(name,type)=>{if(string.IsNullOrEmpty(name)||name=="-")return;var entry=resources.Find(name,type,mixed);if(entry!=null)used.Add(entry);};
            if(kind==ResourceKind.Wall||mixed)
            {
                var sides=new HashSet<int>();foreach(var line in map.lines){if(line.front>=0)sides.Add(line.front);if(line.back>=0)sides.Add(line.back);}
                foreach(int index in sides)if(index<map.sides.Count){var side=map.sides[index];add(side.upper,ResourceKind.Wall);add(side.middle,ResourceKind.Wall);add(side.lower,ResourceKind.Wall);}
            }
            if(kind==ResourceKind.Flat||mixed)foreach(var sector in map.sectors){add(sector.floorTexture,ResourceKind.Flat);add(sector.ceilingTexture,ResourceKind.Flat);}
            return used;
        }
        public static List<TextureCategory> Categories(List<ResourceTexture> choices,ConfigNode definitions)
        {
            var result=new List<TextureCategory>();
            foreach(var pair in definitions.children)
            {
                var matches=new HashSet<ResourceTexture>();var filters=new List<Regex>();
                foreach(var value in pair.Value.values)if(value.Key.StartsWith("filter",StringComparison.OrdinalIgnoreCase)&&!string.IsNullOrEmpty(value.Value))
                    filters.Add(new Regex("\\A"+Regex.Escape(value.Value).Replace("\\*",".*").Replace("\\?",".")+"\\z",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant));
                foreach(var entry in choices)foreach(var filter in filters)if(filter.IsMatch(entry.shortName??entry.name)){matches.Add(entry);break;}
                if(matches.Count>0)result.Add(new TextureCategory(pair.Value.Text("name",pair.Key),matches));
            }
            result.Sort((a,b)=>StringComparer.OrdinalIgnoreCase.Compare(a.name,b.name));
            var groups=new SortedDictionary<string,HashSet<ResourceTexture>>(StringComparer.OrdinalIgnoreCase);
            foreach(var entry in choices)
            {
                string source=entry.sourceName;
                if(!string.IsNullOrEmpty(source))AddGroup(groups,"Архив · "+source,entry);
                int first=entry.name.IndexOf('/'),last=entry.name.LastIndexOf('/');
                if(first>=0&&last>first)AddGroup(groups,"Папка · "+entry.name.Substring(0,last),entry);
            }
            foreach(var group in groups)result.Add(new TextureCategory(group.Key,group.Value));
            return result;
        }
        static void AddGroup(IDictionary<string,HashSet<ResourceTexture>> groups,string name,ResourceTexture entry)
        {HashSet<ResourceTexture> group;if(!groups.TryGetValue(name,out group))groups[name]=group=new HashSet<ResourceTexture>();group.Add(entry);}
    }
}
