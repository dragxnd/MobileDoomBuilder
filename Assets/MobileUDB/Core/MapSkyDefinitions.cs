// GPL-3.0. Sky subset of UDB MapinfoParser/WADReader/PK3StructuredReader;
// UMAPINFO rev 2.2, entry replacement follows DSDA-Doom ParseUMapInfo.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace MobileUDB.Core
{
    public sealed partial class DoomResources
    {
        sealed class SkyScript
        {
            public string path,source;
            public byte[] data;
            public bool universal;
            public Func<string,byte[]> include;
        }
        readonly List<SkyScript> skyScripts=new List<SkyScript>();
        int skyContainerIndex;
        readonly Dictionary<GameSky,Dictionary<string,GameSky>> resolvedSkies=new Dictionary<GameSky,Dictionary<string,GameSky>>();

        static byte[] LastLump(DoomWad wad,string name)
        {
            for(int i=wad.lumps.Count-1;i>=0;i--)if(string.Equals(wad.lumps[i].name,name,StringComparison.OrdinalIgnoreCase))return wad.lumps[i].data;
            return null;
        }
        void CollectMapSky(DoomWad wad)
        {
            if(wad==null)return;
            string path=LastLump(wad,"ZMAPINFO")!=null?"ZMAPINFO":"MAPINFO";
            byte[] data=LastLump(wad,path);
            string source="WAD #"+(++skyContainerIndex)+"/";
            if(data!=null)skyScripts.Add(new SkyScript{path=path,source=source+path,data=data,include=name=>LastLump(wad,name)});
            foreach(var lump in wad.lumps)if(string.Equals(lump.name,"UMAPINFO",StringComparison.OrdinalIgnoreCase))
                skyScripts.Add(new SkyScript{path="UMAPINFO",source=source+"UMAPINFO",data=lump.data,universal=true});
        }
        void CollectMapSky(ResourceArchive archive)
        {
            if(archive.isDehacked)return;
            if(archive.isPk3)
            {
                string source="PK3 #"+(++skyContainerIndex)+"/";
                bool hasZ=archive.files.Exists(f=>f.path.IndexOf('/')<0&&string.Equals(f.Stem,"ZMAPINFO",StringComparison.OrdinalIgnoreCase));
                foreach(var file in archive.files)
                {
                    if(file.path.IndexOf('/')>=0)continue;
                    bool universal=string.Equals(file.Stem,"UMAPINFO",StringComparison.OrdinalIgnoreCase);
                    if(!universal&&!string.Equals(file.Stem,hasZ?"ZMAPINFO":"MAPINFO",StringComparison.OrdinalIgnoreCase))continue;
                    skyScripts.Add(new SkyScript{path=file.path,source=source+file.path,data=file.data,universal=universal,include=name=>
                    {for(int i=archive.files.Count-1;i>=0;i--)if(string.Equals(archive.files[i].path,name,StringComparison.OrdinalIgnoreCase))return archive.files[i].data;return null;}});
                }
            }
            foreach(var wad in archive.wads)CollectMapSky(wad);
        }
        // Immutable context and stable identity until resources/profile/map change.
        // UMAPINFO takes priority for Doom/Heretic; ZDoom's binary profile prefers MAPINFO.
        public GameSky ResolveMapSky(GameSky basis,string mapName,bool preferUniversal=true)
        {
            if(basis==null)throw new ArgumentNullException("basis");
            if(string.IsNullOrEmpty(mapName)||skyScripts.Count==0)return basis;
            bool supportsUniversal=basis.profile=="Doom"||basis.profile=="Doom2"||basis.profile=="Heretic";
            preferUniversal&=supportsUniversal;
            string key=(preferUniversal?"U/":"M/")+mapName;
            Dictionary<string,GameSky> cache;GameSky resolved;
            if(!resolvedSkies.TryGetValue(basis,out cache))resolvedSkies[basis]=cache=new Dictionary<string,GameSky>(StringComparer.OrdinalIgnoreCase);
            if(cache.TryGetValue(key,out resolved))return resolved;
            var traditional=new MapSkyReader(mapName,message=>ResourceWarning(message));
            var universal=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            bool mapError=false;
            foreach(var script in skyScripts)
            {
                if(script.universal)
                {
                    if(!supportsUniversal)continue;
                    try {MapSkyReader.Universal(script.data,script.source,basis.profile,universal);}
                    catch(InvalidDataException error){ResourceWarning(error.Message);}
                }
                else if(!mapError)
                {
                    try {traditional.Read(script.data,script.path,script.source,script.include);}
                    catch(InvalidDataException error){ResourceWarning(error.Message);mapError=true;}
                }
            }
            string sky;bool hasUniversal=universal.TryGetValue(mapName,out sky);
            bool hasTraditional=!mapError&&(traditional.defined||traditional.texture!=null);
            if(hasUniversal&&(preferUniversal||!hasTraditional))
                resolved=string.IsNullOrEmpty(sky)?basis:basis.Override(mapName,sky,"UMAPINFO");
            else if(hasTraditional)resolved=basis.Override(mapName,traditional.texture??"","MAPINFO");
            else resolved=basis;
            if(!mapError&&traditional.flatName!=null)resolved=resolved.Override(mapName,resolved.Texture(mapName),resolved.source??"MAPINFO",traditional.flatName);
            if(!mapError&&traditional.lightMode.HasValue)resolved=resolved.WithLightMode(mapName,traditional.lightMode.Value);
            return cache[key]=resolved;
        }
    }

    // No map data is rewritten. Unused gameplay properties are skipped structurally.
    sealed class MapSkyReader
    {
        readonly string mapName;
        readonly Action<string> warning;
        readonly HashSet<string> parsed=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> active=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int bytes,tokens;
        public string texture,flatName;
        public int? lightMode;
        public bool defined;
        public MapSkyReader(string mapName,Action<string> warning){this.mapName=mapName;this.warning=warning;}
        static bool Word(SkyToken token,string word){return token!=null&&!token.quoted&&string.Equals(token.value,word,StringComparison.OrdinalIgnoreCase);}
        static bool Header(SkyToken token){return Word(token,"map")||Word(token,"defaultmap")||Word(token,"adddefaultmap");}
        public void Read(byte[] data,string path,string source,Func<string,byte[]> include)
        {
            string identity=source+"/"+path;
            if(active.Contains(identity))throw new InvalidDataException(source+": циклический include MAPINFO.");
            if(!parsed.Add(identity))return;
            if(active.Count>=16||data==null||data.Length>8*1024*1024||data.Length>16*1024*1024-bytes)
                throw new InvalidDataException(source+": слишком большой или глубокий MAPINFO.");
            bytes+=data.Length;active.Add(identity);
            try
            {
                var reader=new SkyTokens(data,source,()=>{if(++tokens>1000000)throw new InvalidDataException(source+": слишком много элементов MAPINFO.");});
                while(reader.Peek!=null)
                {
                    var token=reader.Take();
                    if(Word(token,"$gzdb_skip"))break;
                    if(Word(token,"include"))
                    {
                        string name=reader.Take().value;
                        if(name.IndexOf('\\')>=0)throw reader.Error("Обратная косая черта запрещена в include MAPINFO.");
                        try {name=ResourceArchive.Normalize(name);}catch(InvalidDataException){throw reader.Error("Недопустимый путь include: "+name);}
                        byte[] child=include==null?null:include(name);
                        if(child==null)throw reader.Error("Не найден include: "+name);
                        // Includes are relative to the container root, never the current file.
                        Read(child,name,source.Substring(0,Math.Max(0,source.Length-path.Length))+name,include);
                    }
                    else if(Word(token,"gameinfo"))
                    {
                        reader.Expect("{");
                        while(!Word(reader.Peek,"}"))
                        {
                            var property=reader.Take();
                            if(Word(property,"skyflatname"))
                            {reader.Expect("=");flatName=reader.Take().value.ToUpperInvariant();if(flatName.Length==0)throw reader.Error("Пустое имя небесной плоскости.");}
                            else if(Word(property,"{"))SkipBlock(reader);
                            else if(Word(reader.Peek,"="))SkipAssignment(reader);
                        }
                        reader.Take();
                    }
                    else if(Header(token))
                    {
                        bool matches=true;
                        if(Word(token,"defaultmap")){texture=null;lightMode=null;defined=false;}
                        if(Word(token,"map"))
                        {
                            string name=reader.Take().value;int number;
                            if(int.TryParse(name,NumberStyles.Integer,CultureInfo.InvariantCulture,out number))name="MAP"+number.ToString(number>0&&number<10?"00":"0",CultureInfo.InvariantCulture);
                            matches=string.Equals(name,mapName,StringComparison.OrdinalIgnoreCase);
                            if(Word(reader.Peek,"lookup")){reader.Take();reader.Take();}
                            else if(reader.Peek!=null&&!Word(reader.Peek,"{"))reader.Take(); // map title
                        }
                        Block(reader,matches);
                        if(matches&&Word(token,"map"))defined=true;
                    }
                    else if(Word(token,"{"))SkipBlock(reader);
                }
            }
            finally{active.Remove(identity);}
        }
        void Block(SkyTokens reader,bool matches)
        {
            bool braced=Word(reader.Peek,"{");if(braced)reader.Take();
            while(reader.Peek!=null)
            {
                if(!braced&&Header(reader.Peek))return;
                var token=reader.Take();
                if(Word(token,"}")){if(!braced)throw reader.Error("Лишняя закрывающая скобка.");return;}
                if(Word(token,"{")){SkipBlock(reader);continue;}
                if(!matches)continue;
                if(Word(token,"sky1")||Word(token,"sky2"))
                {
                    bool assignment=Word(reader.Peek,"=");if(assignment)reader.Take();
                    var name=reader.Take();if(!name.quoted&&(name.value=="}"||Header(name)||name.value==","||name.value=="="))throw reader.Error("Ожидалось имя неба.");
                    if(name.value.Length==0)throw reader.Error("Пустое имя неба.");
                    defined=true;
                    if(Word(token,"sky1"))texture=name.value.ToUpperInvariant();
                    else warning(reader.Source+": sky2 пока не отображается в предпросмотре.");
                    bool comma=Word(reader.Peek,",");if(comma)reader.Take();
                    float speed;
                    if(reader.Peek!=null&&!reader.Peek.quoted&&float.TryParse(reader.Peek.value,NumberStyles.Float,CultureInfo.InvariantCulture,out speed))
                    {reader.Take();if(float.IsNaN(speed)||float.IsInfinity(speed))throw reader.Error("Некорректная скорость неба.");if(speed!=0)warning(reader.Source+": прокрутка неба пока не отображается в предпросмотре.");}
                    else if(comma)throw reader.Error("Ожидалась скорость неба после запятой.");
                }
                else if(Word(token,"lightmode"))
                {
                    reader.Expect("=");var value=reader.Take();int mode;
                    if(value.quoted||!int.TryParse(value.value,NumberStyles.Integer,CultureInfo.InvariantCulture,out mode))throw reader.Error("lightmode требует целый номер.");
                    lightMode=mode;
                }
                else if(Word(token,"doublesky")||Word(token,"skybox"))
                {defined=true;warning(reader.Source+": "+token.value+" пока не отображается в предпросмотре.");if(Word(token,"skybox")){reader.Expect("=");reader.Take();}}
                else if(Word(reader.Peek,"="))SkipAssignment(reader);
            }
            if(braced)throw reader.Error("Незакрытый блок карты.");
        }
        static void SkipBlock(SkyTokens reader)
        {
            int depth=1;while(depth>0){var token=reader.Take();if(Word(token,"{")){if(++depth>32)throw reader.Error("Слишком глубокий блок.");}else if(Word(token,"}"))depth--;}
        }
        static void Value(SkyTokens reader)
        {var token=reader.Take();if(Word(token,"{")){SkipBlock(reader);return;}if(!token.quoted&&(token.value=="}"||token.value==","||token.value=="="))throw reader.Error("Ожидалось значение свойства.");}
        static void SkipAssignment(SkyTokens reader)
        {reader.Expect("=");Value(reader);while(Word(reader.Peek,",")){reader.Take();Value(reader);}}

        public static void Universal(byte[] data,string source,string profile,Dictionary<string,string> destination)
        {
            if(data.Length>8*1024*1024)throw new InvalidDataException(source+": слишком большой UMAPINFO.");
            var entries=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            var reader=new SkyTokens(data,source);while(reader.Peek!=null)
            {
                reader.Expect("map",true);var name=reader.Take();
                if(name.quoted||!ValidUniversalMap(name.value,profile))throw reader.Error("Недопустимое имя карты UMAPINFO: "+name.value);
                reader.Expect("{");string sky=null;
                while(!Word(reader.Peek,"}"))
                {
                    var key=reader.Take();if(key.quoted)throw reader.Error("Ожидалось имя свойства.");
                    if(Word(key,"skytexture"))
                    {reader.Expect("=");var value=reader.Take();if(!value.quoted||value.value.Length>8)throw reader.Error("skytexture требует строку длиной до 8 символов.");sky=value.value.ToUpperInvariant();}
                    else SkipAssignment(reader);
                    if(Word(reader.Peek,";"))reader.Take();
                }
                reader.Take();entries[name.value]=sky; // last whole entry replaces earlier values
            }
            // A malformed file cannot partially replace a previous valid entry.
            foreach(var pair in entries)destination[pair.Key]=pair.Value;
        }
        static bool ValidUniversalMap(string name,string profile)
        {
            int at=0;
            if(profile=="Doom2"){if(!name.StartsWith("MAP",StringComparison.OrdinalIgnoreCase))return false;at=3;return Digits(name,ref at)&&at==name.Length;}
            if(name.Length==0||char.ToUpperInvariant(name[at++])!='E'||!Digits(name,ref at)||at>=name.Length||char.ToUpperInvariant(name[at++])!='M')return false;
            return Digits(name,ref at)&&at==name.Length;
        }
        static bool Digits(string name,ref int at){int start=at;while(at<name.Length&&name[at]>='0'&&name[at]<='9')at++;return at>start;}
    }
    sealed class SkyToken
    {
        public string value;public bool quoted;
        public SkyToken(string value,bool quoted=false){this.value=value;this.quoted=quoted;}
    }
    sealed class SkyTokens
    {
        readonly string text;readonly Action count;int at,line=1,tokens;SkyToken peek;bool loaded;
        public readonly string Source;
        public SkyTokens(byte[] data,string source,Action count=null){text=Encoding.UTF8.GetString(data);Source=source;this.count=count;}
        public InvalidDataException Error(string message){return new InvalidDataException(Source+":"+line+": "+message);}
        public SkyToken Peek{get{if(!loaded){peek=Read();loaded=true;}return peek;}}
        public SkyToken Take(){var token=Peek;if(token==null)throw Error("Неожиданный конец MAPINFO.");loaded=false;if(count!=null)count();return token;}
        public void Expect(string value,bool ignoreCase=false)
        {var token=Take();if(token.quoted||!string.Equals(token.value,value,ignoreCase?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal))throw Error("Ожидалось "+value);}
        SkyToken Read()
        {
            while(at<text.Length)
            {
                char c=text[at];if(char.IsWhiteSpace(c)||c=='\uFEFF'){if(c=='\n')line++;at++;continue;}
                if(c=='/'&&at+1<text.Length&&text[at+1]=='/'){while(at<text.Length&&text[at]!='\n')at++;continue;}
                if(c=='/'&&at+1<text.Length&&text[at+1]=='*')
                {at+=2;while(at+1<text.Length&&(text[at]!='*'||text[at+1]!='/')){if(text[at++]=='\n')line++;}if(at+1>=text.Length)throw Error("Незакрытый комментарий.");at+=2;continue;}
                break;
            }
            if(at>=text.Length)return null;if(++tokens>1000000)throw Error("Слишком много элементов MAPINFO.");
            char first=text[at++];if("{}=,;".IndexOf(first)>=0)return new SkyToken(first.ToString());
            if(first=='"')
            {
                var value=new StringBuilder();while(at<text.Length)
                {char c=text[at++];if(c=='"')return new SkyToken(value.ToString(),true);if(c=='\n')line++;if(c=='\\'&&at<text.Length&&(text[at]=='"'||text[at]=='\\'))c=text[at++];value.Append(c);}
                throw Error("Незакрытая строка.");
            }
            int start=at-1;while(at<text.Length&&!char.IsWhiteSpace(text[at])&&"{}=,;".IndexOf(text[at])<0)
            {if(text[at]=='/'&&at+1<text.Length&&(text[at+1]=='/'||text[at+1]=='*'))break;at++;}
            return new SkyToken(text.Substring(start,at-start));
        }
    }
}
