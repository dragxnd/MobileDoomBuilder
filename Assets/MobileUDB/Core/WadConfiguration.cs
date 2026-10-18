using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
namespace MobileUDB.Core
{
    // UDB-compatible sidecar, preserving sections not edited by the mobile UI.
    public sealed class WadConfiguration
    {
        public readonly ConfigNode root;
        public readonly DbsMapSettings maps;
        public WadConfiguration(byte[] bytes)
        {root=DbsSelectionGroups.ReadConfiguration(bytes);maps=DbsMapSettings.Read(bytes,true);}
        public string Profile
        {
            get {string mobile=root.Section("mobileudb").Text("profile");if(mobile.Length>0)return mobile;
                string name=root.Text("gameconfig").ToLowerInvariant();foreach(string game in new[]{"Hexen","Heretic","Strife","Doom2","Doom"})if(name.Contains(game.ToLowerInvariant()))return game;return "auto";}
        }
        public string Engine
        {get {string mobile=root.Section("mobileudb").Text("engine");if(mobile.Length>0)return mobile;string name=root.Text("gameconfig").ToLowerInvariant();foreach(string engine in new[]{"MBF21","MBF","Boom"})if(name.Contains(engine.ToLowerInvariant()))return engine;return "vanilla";}}
        public List<string> Resources(string map)
        {var result=new List<string>();foreach(var entry in root.Section("maps."+map+".resources").children){string location=entry.Value.Text("location");if(location.Length>0)result.Add(location);}if(result.Count>32)throw new InvalidDataException("В DBS более 32 ресурсов.");return result;}
        static void String(ConfigNode node,string key,string value){node.values[key]=value;node.quotedValues.Add(key);}
        public static byte[] Export(WadConfiguration previous,byte[] mapSettings,string profile,string engine,IReadOnlyList<string> resources,IDictionary<string,string> renamed=null,IDictionary<string,List<string>> perMapResources=null)
        {
            var root=new ConfigNode();if(previous!=null)root.Merge(previous.root);
            var fresh=DbsSelectionGroups.ReadConfiguration(mapSettings);ConfigNode mapRoot;
            if(!root.children.TryGetValue("maps",out mapRoot))root.children["maps"]=mapRoot=new ConfigNode();
            if(renamed!=null)
            {
                var originals=new Dictionary<string,ConfigNode>(StringComparer.OrdinalIgnoreCase);
                foreach(var pair in renamed){ConfigNode old;if(mapRoot.children.TryGetValue(pair.Key,out old))originals[pair.Value]=old;}
                foreach(var pair in renamed)mapRoot.children.Remove(pair.Key);
                foreach(var pair in originals)mapRoot.children[pair.Key]=pair.Value;
            }
            foreach(var entry in fresh.children["maps"].children)
            {
                ConfigNode map;if(!mapRoot.children.TryGetValue(entry.Key,out map))mapRoot.children[entry.Key]=map=new ConfigNode();
                foreach(string replace in new[]{"selectiongroups","taglabels"})map.children.Remove(replace);
                map.Merge(entry.Value);var list=new ConfigNode();map.children["resources"]=list;
                var locations=perMapResources!=null&&perMapResources.ContainsKey(entry.Key)?(IReadOnlyList<string>)perMapResources[entry.Key]:resources;
                for(int i=0;i<locations.Count;i++){var resource=new ConfigNode();list.children["resource"+i]=resource;String(resource,"location",locations[i]);resource.values["type"]=locations[i].EndsWith(".wad",StringComparison.OrdinalIgnoreCase)?"0":"2";resource.values["option1"]="0";resource.values["option2"]="0";resource.values["notfortesting"]="0";}
            }
            String(root,"type","Doom Builder Map Settings Configuration");
            if(previous==null||previous.Profile!=profile||previous.Engine!=engine)
            {string baseGame=profile+ (profile=="Hexen"?"Hexen":"Doom");string prefix=engine=="vanilla"||engine=="auto"?(profile=="Doom"||profile=="Doom2"?"Doom":profile) : engine=="MBF"?"MBF21":engine;String(root,"gameconfig",(prefix.Length>0?prefix+"_":"")+baseGame+".cfg");}
            var mobile=new ConfigNode();String(mobile,"profile",profile);String(mobile,"engine",engine);root.children["mobileudb"]=mobile;
            var text=new StringBuilder();Write(root,text,0);var bytes=new UTF8Encoding(false,true).GetBytes(text.ToString());if(bytes.Length>DbsSelectionGroups.MaximumBytes)throw new InvalidDataException("DBS превышает 8 МиБ.");return bytes;
        }
        static void Write(ConfigNode node,StringBuilder text,int depth)
        {
            string indent=new string(' ',depth*2);
            foreach(var entry in node.values){text.Append(indent).Append(entry.Key).Append(" = ");if(entry.Value==null)text.Append("NULL");else if(node.quotedValues.Contains(entry.Key))text.Append('"').Append(entry.Value.Replace("\\","\\\\").Replace("\"","\\\"").Replace("\n","\\n").Replace("\r","\\r").Replace("\t","\\t")).Append('"');else text.Append(entry.Value);text.Append(";\n");}
            foreach(var entry in node.children){text.Append(indent).Append(entry.Key).Append("\n").Append(indent).Append("{\n");Write(entry.Value,text,depth+1);text.Append(indent).Append("}\n");}
        }
    }
}
