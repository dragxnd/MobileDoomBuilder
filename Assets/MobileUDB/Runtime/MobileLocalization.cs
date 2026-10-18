// GPL-3.0. Offline UI localization; map/project data is never localized.
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
namespace MobileUDB
{
    public static class UiLanguage
    {
        public const string PreferenceKey="MobileUDB.Language";
        public static readonly string[] Codes={"en","ru","fr","de","zh-Hans","zh-Hant","ko","ja","pt-BR","pt-PT","es"};
        public static readonly string[] NativeNames={"English","Русский","Français","Deutsch","简体中文","繁體中文","한국어","日本語","Português (Brasil)","Português (Portugal)","Español"};
        [Serializable] sealed class Entry {public string source;public string text;}
        [Serializable] sealed class Catalog {public Entry[] entries;}
        sealed class Node {public readonly Dictionary<char,Node> children=new Dictionary<char,Node>();public string value;}
        static Dictionary<string,string> messages=new Dictionary<string,string>(StringComparer.Ordinal);
        static readonly Dictionary<string,string> cache=new Dictionary<string,string>(StringComparer.Ordinal);
        static Node root=new Node();static string code;static Font interfaceFont;
        static readonly Regex dataPaths=new Regex(@"(?:[A-Za-z]:[\\/]|(?:content|file|https?)://)[^\r\n""<>|]*|[^\s\r\n""<>«»]*\.(?:wad|pk3|pk7|zip|dbs|png|jpg|jpeg|otf|ttf)\b",RegexOptions.IgnoreCase);
        public static event Action Changed;
        public static string Code {get{Initialize();return code;}}
        public static string NativeName {get{return NativeNames[Array.IndexOf(Codes,Code)];}}
        public static Font InterfaceFont {get{if(interfaceFont==null)interfaceFont=Resources.Load<Font>("MobileUDB/Localization/NotoSansCJKsc-Regular");return interfaceFont!=null?interfaceFont:Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");}}
        public static void Initialize()
        {if(code==null)Load(PlayerPrefs.GetString(PreferenceKey,"en"));}
        static void Load(string requested)
        {
            code=Array.IndexOf(Codes,requested)>=0?requested:"en";messages.Clear();cache.Clear();root=new Node();
            if(code=="ru")return;
            var resource=Resources.Load<TextAsset>("MobileUDB/Localization/"+code);
            if(resource==null)throw new InvalidOperationException("Missing UI language catalog: "+code);
            var catalog=JsonUtility.FromJson<Catalog>(resource.text);
            foreach(var entry in catalog.entries)
            {
                if(string.IsNullOrEmpty(entry.source)||string.IsNullOrEmpty(entry.text))continue;
                messages[entry.source]=entry.text;var node=root;
                foreach(char ch in entry.source){Node next;if(!node.children.TryGetValue(ch,out next)){next=new Node();node.children.Add(ch,next);}node=next;}node.value=entry.text;
            }
        }
        public static void Set(string language)
        {
            if(Array.IndexOf(Codes,language)<0)throw new ArgumentOutOfRangeException("language");
            Initialize();if(code!=language){Load(language);if(Changed!=null)Changed();}
            PlayerPrefs.SetString(PreferenceKey,language);PlayerPrefs.Save();
        }
        public static string Translate(string source)
        {
            if(string.IsNullOrEmpty(source))return source;Initialize();if(code=="ru")return source;
            string result;if(messages.TryGetValue(source,out result)||cache.TryGetValue(source,out result))return result;
            // Dynamic labels are composed from the same source literals as the
            // catalog. Match longest source fragments once, never translated output.
            var text=new StringBuilder(source.Length);var paths=dataPaths.Matches(source);int path=0;
            for(int i=0;i<source.Length;)
            {
                while(path<paths.Count&&paths[path].Index<i)path++;
                if(path<paths.Count&&paths[path].Index==i){text.Append(paths[path].Value);i+=paths[path].Length;path++;continue;}
                var node=root;int end=i;string replacement=null;
                for(int j=i;j<source.Length;j++){Node next;if(!node.children.TryGetValue(source[j],out next))break;node=next;if(node.value!=null&&(i==0||!char.IsLetter(source[i-1])||!char.IsLetter(source[i]))&&(j+1==source.Length||!char.IsLetter(source[j+1])||!char.IsLetter(source[j]))){replacement=node.value;end=j+1;}}
                if(replacement==null){text.Append(source[i]);i++;}else{text.Append(replacement);i=end;}
            }
            result=text.ToString();if(cache.Count>=2048)cache.Clear();cache[source]=result;return result;
        }
    }
}
