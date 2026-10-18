// GPL-3.0. Stable previous/current-name association follows UDB MapOptions.
using System;
using System.Collections.Generic;
using System.IO;

namespace MobileUDB.Core
{
    public static class MapNaming
    {
        public static string Key(MapDocument map)=>string.IsNullOrEmpty(map.sourceName)?map.name:map.sourceName;
        public static void RequireName(string name)
        {
            if(string.IsNullOrEmpty(name)||name.Length>8)throw new InvalidDataException("Имя карты: 1–8 символов ASCII.");
            foreach(char c in name)if(c<33||c>126)throw new InvalidDataException("Имя карты должно быть ASCII без пробелов.");
        }
        public static string Normalize(string name,bool pk3=false)
        {
            name=(name??"").Trim().ToUpperInvariant();RequireName(name);
            if(pk3&&(name.IndexOf('/')>=0||name.IndexOf('\\')>=0))throw new InvalidDataException("Имя карты PK3 не должно содержать разделители пути.");
            return name;
        }
        public static bool Rename(MapDocument map,string name,bool pk3=false)
        {
            name=Normalize(name,pk3);if(string.Equals(map.name,name,StringComparison.OrdinalIgnoreCase))return false;
            if(string.IsNullOrEmpty(map.sourceName))map.sourceName=map.name;
            map.name=name;return true;
        }
        public static Dictionary<string,string> Sources(IEnumerable<MapDocument> maps)
        {
            var targets=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);var sources=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var map in maps)
            {
                RequireName(map.name);string source=Key(map);RequireName(source);
                if(targets.ContainsKey(map.name)||!sources.Add(source))throw new InvalidDataException("Повторяющееся имя / исходная карта в проекте.");
                targets.Add(map.name,source);
            }
            return targets;
        }
        public static string Source(string target,IDictionary<string,string> sources)
        {string source;if(sources!=null&&sources.TryGetValue(target,out source)){RequireName(source);return source;}return target;}
    }
}
