// GPL-3.0. Binary sky names/default-map tables from UDB game configurations.
using System;
using System.Collections.Generic;

namespace MobileUDB.Core
{
    // Immutable editor context; binary WADs do not store the selected game profile.
    public sealed class GameSky
    {
        public readonly string profile,flatName;
        readonly Dictionary<string,string> textures=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        readonly string firstTexture;
        readonly string overrideMap,overrideTexture;
        readonly int? lightMode;
        public readonly string source;
        GameSky(string profile,ConfigNode node)
        {
            this.profile=profile;flatName=node.Text("skyflatname","F_SKY1");
            firstTexture="";
            foreach(var pair in node.Section("defaultskytextures").values)
            {
                if(firstTexture.Length==0)firstTexture=pair.Key;
                foreach(string name in pair.Value.Split(new[]{','},StringSplitOptions.RemoveEmptyEntries))textures[name]=pair.Key;
            }
        }
        GameSky(GameSky basis,string mapName,string texture,string source,string flatName,int? lightMode=null,bool setLight=false)
        {
            profile=basis.profile;this.flatName=flatName??basis.flatName;textures=basis.textures;firstTexture=basis.firstTexture;
            overrideMap=mapName;overrideTexture=texture;this.source=source;
            this.lightMode=setLight?lightMode:basis.LightMode(mapName);
        }
        internal GameSky Override(string mapName,string texture,string source,string flatName=null){return new GameSky(this,mapName,texture,source,flatName);}
        internal GameSky WithLightMode(string mapName,int mode){return new GameSky(this,mapName,Texture(mapName),source,flatName,mode,true);}
        public int? LightMode(string mapName){return overrideMap!=null&&string.Equals(mapName,overrideMap,StringComparison.OrdinalIgnoreCase)?lightMode:null;}
        // Native Renderer.CalculateBrightness only overrides configuration for Standard=0
        // and Doom=2. Other modes retain Common.cfg's DoomLightLevels=true default.
        public static bool DoomLightLevels(MapDocument map){return map==null||map.sky==null||map.sky.LightMode(map.name)!=0;}
        public static GameSky Load(string definitions,string profile)
        {
            ConfigNode node;
            if(!GameConfigText.Read(definitions).children.TryGetValue(profile,out node))throw new InvalidOperationException("Нет правил неба: "+profile);
            return new GameSky(profile,node);
        }
        public string Texture(string mapName)
        {if(overrideMap!=null&&string.Equals(mapName,overrideMap,StringComparison.OrdinalIgnoreCase))return overrideTexture;
            string texture;return mapName!=null&&textures.TryGetValue(mapName,out texture)?texture:firstTexture;}
        public static string FlatName(MapDocument map){return map.sky==null?"F_SKY1":map.sky.flatName;}
        public static bool Is(MapDocument map,string name){return string.Equals(name,FlatName(map),StringComparison.OrdinalIgnoreCase);}
    }
}
