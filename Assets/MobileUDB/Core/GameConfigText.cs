// GPL-3.0. Reads the declarative subset used by bundled UDB configurations.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace MobileUDB.Core
{
    public sealed class ConfigNode
    {
        public readonly HashSet<string> quotedValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string,string> values = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string,ConfigNode> children = new Dictionary<string,ConfigNode>(StringComparer.OrdinalIgnoreCase);
        public string Text(string key, string fallback = "") { string v; return values.TryGetValue(key,out v) && v != null ? v : fallback; }
        public int Integer(string key, int fallback = 0) { int n; return int.TryParse(Text(key),NumberStyles.Integer,CultureInfo.InvariantCulture,out n) ? n : fallback; }
        public bool Boolean(string key, bool fallback = false) { bool b; string v = Text(key); return bool.TryParse(v,out b) ? b : v == "1" ? true : v == "0" ? false : fallback; }
        public ConfigNode Section(string path)
        {
            var node = this; foreach (string part in path.Split('.')) if (!node.children.TryGetValue(part,out node)) return new ConfigNode(); return node;
        }
        public void Merge(ConfigNode source)
        {
            foreach (var p in source.values)
            {
                children.Remove(p.Key);
                if(source.quotedValues.Contains(p.Key))quotedValues.Add(p.Key);else quotedValues.Remove(p.Key);
                if (p.Value == null) values.Remove(p.Key); else values[p.Key] = p.Value;
            }
            foreach (var p in source.children)
            {
                values.Remove(p.Key); ConfigNode child;
                if (!children.TryGetValue(p.Key,out child)) { child = new ConfigNode(); children[p.Key] = child; }
                child.Merge(p.Value);
            }
        }
    }
    public static class GameConfigText
    {
        public static ConfigNode Read(string text, Func<string,string> includes = null,bool dbsEscapes=false) { return new Parser(text,new Context(includes,dbsEscapes)).Read(); }
        sealed class Context
        {
            readonly Func<string,string> includes;
            readonly HashSet<string> active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            readonly Dictionary<string,ConfigNode> cache = new Dictionary<string,ConfigNode>(StringComparer.OrdinalIgnoreCase);
            public readonly bool dbsEscapes;
            public Context(Func<string,string> includes,bool dbsEscapes) { this.includes = includes;this.dbsEscapes=dbsEscapes; }
            public ConfigNode Include(string name)
            {
                if (includes == null) throw new InvalidDataException("Include не разрешён в этом профиле.");
                ConfigNode node; if (cache.TryGetValue(name,out node)) return node;
                if (active.Count >= 16 || !active.Add(name)) throw new InvalidDataException("Циклический include профиля: " + name);
                try { string text = includes(name); if (text == null) throw new InvalidDataException("Нет включаемого профиля: " + name); node = new Parser(text,this).Read(); cache[name] = node; return node; }
                finally { active.Remove(name); }
            }
        }
        sealed class Parser
        {
            readonly string text; readonly Context context; int at, depth; bool quoted;
            public Parser(string text, Context context) { this.text = text ?? ""; this.context = context; }
            string Next()
            {
                quoted = false;
                while (at < text.Length)
                {
                    if (char.IsWhiteSpace(text[at])) { at++; continue; }
                    if (at + 1 < text.Length && text[at] == '/' && text[at + 1] == '/') { while (at < text.Length && text[at] != '\n') at++; continue; }
                    if (at + 1 < text.Length && text[at] == '/' && text[at + 1] == '*') { int end = text.IndexOf("*/",at + 2,StringComparison.Ordinal); if (end < 0) throw new InvalidDataException("Незакрытый комментарий профиля."); at = end + 2; continue; }
                    break;
                }
                if (at == text.Length) return null;
                char c = text[at++]; if ("{}=;(),".IndexOf(c) >= 0) return c.ToString();
                if (c == '"')
                {
                    quoted = true; var value = new StringBuilder();
                    while (at < text.Length)
                    {
                        c = text[at++]; if (c == '"') return value.ToString();
                        if(c=='\\'&&at<text.Length)
                        {
                            if(context.dbsEscapes){c=text[at++];if(c=='n')c='\n';else if(c=='r')c='\r';else if(c=='t')c='\t';}
                            else if(text[at]=='"'||text[at]=='\\')c=text[at++];
                        }
                        value.Append(c);
                    }
                    throw new InvalidDataException("Незакрытая строка профиля.");
                }
                int start = at - 1; while (at < text.Length && !char.IsWhiteSpace(text[at]) && "{}=;(),".IndexOf(text[at]) < 0) at++;
                return text.Substring(start,at - start);
            }
            public ConfigNode Read()
            {
                if (++depth > 32) throw new InvalidDataException("Слишком глубокий профиль."); var node = new ConfigNode(); string key;
                while ((key = Next()) != null && key != "}")
                {
                    if (key == ";") continue; string op = Next();
                    if (key == "include" && op == "(")
                    {
                        string name = Next(), section = null, close = Next();
                        if (close == ",") { section = Next(); close = Next(); }
                        if (close != ")" || Next() != ";") throw new InvalidDataException("Некорректный include.");
                        var included = context.Include(name); node.Merge(section == null ? included : included.Section(section));
                    }
                    else if (op == "{") { var child = Read(); ConfigNode old; node.values.Remove(key); if (node.children.TryGetValue(key,out old)) old.Merge(child); else node.children[key] = child; }
                    else if (op == "=") { string value = Next(); bool wasQuoted=quoted; bool isNull = !quoted && string.Equals(value,"NULL",StringComparison.OrdinalIgnoreCase); if (value == null || Next() != ";") throw new InvalidDataException("Некорректное свойство профиля: " + key); node.children.Remove(key); node.values[key] = isNull ? null : value; if(wasQuoted)node.quotedValues.Add(key);else node.quotedValues.Remove(key); }
                    else if (op == ";") node.values[key] = "";
                    else throw new InvalidDataException("Некорректный узел профиля: " + key);
                }
                if ((depth > 1 && key == null) || (depth == 1 && key == "}")) throw new InvalidDataException("Незакрытый или лишний узел профиля."); depth--; return node;
            }
        }
    }
}
