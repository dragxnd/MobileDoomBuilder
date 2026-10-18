// UDMF fields follow UDB's UniversalStreamReader / UniversalStreamWriter.
// GPL-3.0. Preserve extensions even when the mobile UI does not manage them yet.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace MobileUDB.Core
{
    public static class UdmfText
    {
        static readonly string[] LineFlags = { "blocking", "blockmonsters", "twosided", "dontpegtop", "dontpegbottom", "secret", "blocksound", "dontdraw", "mapped" };
#if MOBILEUDB_UDMF // Parser/writer deferred; preserve original implementation.
        public static MapDocument Read(string text, string name)
        {
            var parser = new Parser(text); var map = new MapDocument { name = name, format = MapFormat.Udmf };
            while (!parser.End)
            {
                string key = parser.Identifier();
                if (parser.Accept('=')) { map.udmfFields.Add(new UniversalField(key, parser.Value())); parser.Expect(';'); }
                else
                {
                    var block = parser.Block(key);
                    MapElementData element;
                    if (key == "vertex")
                    {
                        var v = new MapVertex(Real(block.fields, "x", 0, true), Real(block.fields, "y", 0, true));
                        map.vertices.Add(v); element = v;
                    }
                    else if (key == "linedef")
                    {
                        var l = new MapLine { start = Integer(block.fields, "v1", 0, true), end = Integer(block.fields, "v2", 0, true), front = Integer(block.fields, "sidefront", -1), back = Integer(block.fields, "sideback", -1), tag = Integer(block.fields, "id", 0), action = Integer(block.fields, "special", 0), flags = 0 };
                        for (int i = 0; i < 5; i++) l.args[i] = Integer(block.fields, "arg" + i, 0);
                        for (int i = 0; i < LineFlags.Length; i++) if (Boolean(block.fields, LineFlags[i], false)) l.flags |= 1 << i;
                        map.lines.Add(l); element = l;
                    }
                    else if (key == "sidedef")
                    {
                        var s = new MapSide { sector = Integer(block.fields, "sector", 0, true), offsetX = Integer(block.fields, "offsetx", 0), offsetY = Integer(block.fields, "offsety", 0), upper = String(block.fields, "texturetop", "-"), middle = String(block.fields, "texturemiddle", "-"), lower = String(block.fields, "texturebottom", "-") };
                        map.sides.Add(s); element = s;
                    }
                    else if (key == "sector")
                    {
                        var s = new MapSector { floor = Real(block.fields, "heightfloor", 0), ceiling = Real(block.fields, "heightceiling", 0), floorTexture = String(block.fields, "texturefloor", "-", true), ceilingTexture = String(block.fields, "textureceiling", "-", true), light = Integer(block.fields, "lightlevel", 160), special = Integer(block.fields, "special", 0), tag = Integer(block.fields, "id", 0) };
                        map.sectors.Add(s); element = s;
                    }
                    else if (key == "thing")
                    {
                        var t = new MapThing { x = Real(block.fields, "x", 0, true), y = Real(block.fields, "y", 0, true), z = Real(block.fields, "height", 0), angle = Real(block.fields, "angle", 0), type = Integer(block.fields, "type", 0, true), tag = Integer(block.fields, "id", 0), action = Integer(block.fields, "special", 0), flags = 0, udmfInitialized = true };
                        for (int i = 0; i < 5; i++) t.args[i] = Integer(block.fields, "arg" + i, 0);
                        if (Boolean(block.fields, "skill1", false)) t.flags |= 1;
                        if (Boolean(block.fields, "skill3", false)) t.flags |= 2;
                        if (Boolean(block.fields, "skill5", false)) t.flags |= 4;
                        if (Boolean(block.fields, "ambush", false)) t.flags |= 8;
                        map.things.Add(t); element = t;
                    }
                    else { map.udmfBlocks.Add(block.raw); continue; }
                    element.fields = block.fields; element.extraBlocks = block.nested;
                }
            }
            Unique(map.udmfFields);
            map.udmfNamespace = String(map.udmfFields, "namespace", null, true);
            map.Validate(); return map;
        }
        public static string Write(MapDocument map)
        {
            map.Validate(); var output = new StringBuilder(); var root = Copy(map.udmfFields);
            SetString(root, "namespace", map.udmfNamespace);
            for (int i = 0; i < root.Count; i++) if (string.Equals(root[i].name, "namespace", StringComparison.OrdinalIgnoreCase)) { var declaration = root[i]; root.RemoveAt(i); root.Insert(0, declaration); break; }
            WriteFields(output, root, "");
            foreach (string block in map.udmfBlocks) output.AppendLine(block);
            foreach (var v in map.vertices)
            {
                var f = Copy(v.fields); Set(f, "x", v.x); Set(f, "y", v.y); WriteBlock(output, "vertex", f, v.extraBlocks);
            }
            foreach (var l in map.lines)
            {
                var f = Copy(l.fields); Set(f, "v1", l.start); Set(f, "v2", l.end); Set(f, "sidefront", l.front); Set(f, "sideback", l.back); Set(f, "id", l.tag); Set(f, "special", l.action);
                for (int i = 0; i < 5; i++) Set(f, "arg" + i, l.args[i]);
                for (int i = 0; i < LineFlags.Length; i++) SetBool(f, LineFlags[i], (l.flags & (1 << i)) != 0);
                WriteBlock(output, "linedef", f, l.extraBlocks);
            }
            foreach (var s in map.sides)
            {
                var f = Copy(s.fields); Set(f, "sector", s.sector); Set(f, "offsetx", s.offsetX); Set(f, "offsety", s.offsetY); SetString(f, "texturetop", s.upper); SetString(f, "texturemiddle", s.middle); SetString(f, "texturebottom", s.lower); WriteBlock(output, "sidedef", f, s.extraBlocks);
            }
            foreach (var s in map.sectors)
            {
                var f = Copy(s.fields); Set(f, "heightfloor", s.floor); Set(f, "heightceiling", s.ceiling); SetString(f, "texturefloor", s.floorTexture); SetString(f, "textureceiling", s.ceilingTexture); Set(f, "lightlevel", s.light); Set(f, "id", s.tag); Set(f, "special", s.special); WriteBlock(output, "sector", f, s.extraBlocks);
            }
            foreach (var t in map.things)
            {
                var f = Copy(t.fields); Set(f, "x", t.x); Set(f, "y", t.y); Set(f, "height", t.z); Set(f, "angle", t.angle); Set(f, "type", t.type); Set(f, "id", t.tag); Set(f, "special", t.action);
                for (int i = 0; i < 5; i++) Set(f, "arg" + i, t.args[i]);
                if (!t.udmfInitialized)
                {
                    for (int i = 1; i <= 5; i++) DefaultBool(f, "skill" + i, (t.flags & (i <= 2 ? 1 : i == 3 ? 2 : 4)) != 0);
                    DefaultBool(f, "ambush", (t.flags & 8) != 0); DefaultBool(f, "single", true); DefaultBool(f, "coop", true); DefaultBool(f, "dm", true);
                    if (map.udmfNamespace == "zdoom" || map.udmfNamespace == "hexen")
                        for (int i = 1; i <= 3; i++) DefaultBool(f, "class" + i, true);
                }
                WriteBlock(output, "thing", f, t.extraBlocks);
            }
            return output.ToString();
        }
        static void WriteBlock(StringBuilder output, string name, List<UniversalField> fields, List<string> extra)
        {
            output.Append(name).AppendLine("\n{"); WriteFields(output, fields, "    "); foreach (string block in extra) output.Append("    ").AppendLine(block); output.AppendLine("}\n");
        }
        static void WriteFields(StringBuilder output, List<UniversalField> fields, string indent)
        {
            Unique(fields);
            foreach (var f in fields) { IdentifierValid(f.name); ValidateScalar(f.value); output.Append(indent).Append(f.name).Append(" = ").Append(f.value).AppendLine(";"); }
        }
#else
        public static MapDocument Read(string text,string name)
        { throw new NotSupportedException("Первая версия поддерживает только бинарные карты Doom / Hexen."); }
        public static string Write(MapDocument map)
        { throw new NotSupportedException("Первая версия поддерживает только бинарные карты Doom / Hexen."); }
#endif
        public static string Raw(List<UniversalField> fields, string name)
        {
            if (fields != null) foreach (var f in fields) if (string.Equals(f.name, name, StringComparison.OrdinalIgnoreCase)) return f.value;
            return null;
        }
        public static void SetRaw(List<UniversalField> fields, string name, string value)
        {
            IdentifierValid(name); ValidateScalar(value);
            foreach (var f in fields) if (string.Equals(f.name, name, StringComparison.OrdinalIgnoreCase)) { f.value = value; return; }
            fields.Add(new UniversalField(name.ToLowerInvariant(), value));
        }
        public static bool Boolean(List<UniversalField> fields, string name, bool fallback)
        {
            string raw = Raw(fields, name); if (raw == null) return fallback;
            bool result; if (!bool.TryParse(raw, out result)) throw new InvalidDataException("UDMF boolean expected: " + name); return result;
        }
        public static void SetBool(List<UniversalField> fields, string name, bool value) { SetRaw(fields, name, value ? "true" : "false"); }
        static void DefaultBool(List<UniversalField> fields, string name, bool value) { if (Raw(fields, name) == null) SetBool(fields, name, value); }
        public static bool IsManaged(ElementKind kind, string name)
        {
            string key = name.ToLowerInvariant();
            if (kind == ElementKind.Vertex) return key == "x" || key == "y";
            if (kind == ElementKind.Line)
            {
                foreach (string flag in LineFlags) if (key == flag) return true;
                return key == "v1" || key == "v2" || key == "sidefront" || key == "sideback" || key == "id" || key == "special" || IsArgument(key);
            }
            if (kind == ElementKind.Sector) return key == "heightfloor" || key == "heightceiling" || key == "texturefloor" || key == "textureceiling" || key == "lightlevel" || key == "special" || key == "id";
            if (kind == ElementKind.Thing) return key == "x" || key == "y" || key == "height" || key == "angle" || key == "type" || key == "id" || key == "special" || IsArgument(key);
            return false;
        }
        static bool IsArgument(string key) { return key.Length == 4 && key.StartsWith("arg", StringComparison.Ordinal) && key[3] >= '0' && key[3] <= '4'; }
        public static string DecodeString(string raw) { return Decode(raw); }
        public static int Integer(List<UniversalField> fields, string name, int fallback, bool required = false)
        {
            string raw = Required(fields, name, required); if (raw == null) return fallback;
            return ParseInt(raw);
        }
        static int ParseInt(string raw)
        {
            string value = raw.Trim(); bool negative = value.StartsWith("-"); if (value.StartsWith("-") || value.StartsWith("+")) value = value.Substring(1);
            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                uint n; if (!uint.TryParse(value.Substring(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out n)) throw new InvalidDataException("Invalid UDMF integer: " + raw);
                return negative ? checked(-(int)n) : unchecked((int)n);
            }
            int result; if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out result)) throw new InvalidDataException("UDMF integer expected: " + raw); return result;
        }
        static float Real(List<UniversalField> fields, string name, float fallback, bool required = false)
        {
            string raw = Required(fields, name, required); if (raw == null) return fallback;
            float result;
            if (raw.IndexOf("0x", StringComparison.OrdinalIgnoreCase) >= 0) result = ParseInt(raw);
            else if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out result)) throw new InvalidDataException("UDMF number expected: " + name);
            if (float.IsNaN(result) || float.IsInfinity(result)) throw new InvalidDataException("Non-finite UDMF number: " + name); return result;
        }
        static string String(List<UniversalField> fields, string name, string fallback, bool required = false)
        {
            string raw = Required(fields, name, required); return raw == null ? fallback : Decode(raw);
        }
        static string Required(List<UniversalField> fields, string name, bool required)
        {
            string raw = Raw(fields, name); if (raw == null && required) throw new InvalidDataException("Missing UDMF field: " + name); return raw;
        }
        static List<UniversalField> Copy(List<UniversalField> fields)
        {
            var copy = new List<UniversalField>(); foreach (var f in fields) copy.Add(new UniversalField(f.name, f.value)); return copy;
        }
        static void Set(List<UniversalField> fields, string name, int value)
        {
            string raw = Raw(fields, name); if (raw != null && ParseInt(raw) == value) return; SetRaw(fields, name, value.ToString(CultureInfo.InvariantCulture));
        }
        static void Set(List<UniversalField> fields, string name, float value)
        {
            if (Raw(fields, name) != null && Real(fields, name, 0) == value) return;
            SetRaw(fields, name, value.ToString("R", CultureInfo.InvariantCulture));
        }
        static void SetString(List<UniversalField> fields, string name, string value)
        {
            if (Raw(fields, name) != null && String(fields, name, "") == value) return;
            SetRaw(fields, name, Quote(value));
        }
        public static string Quote(string value)
        {
            if (value == null) throw new InvalidDataException("Missing UDMF string.");
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t") + "\"";
        }
        static string Decode(string value)
        {
            if (value.Length < 2 || value[0] != '"' || value[value.Length - 1] != '"') throw new InvalidDataException("UDMF quoted string expected.");
            var result = new StringBuilder();
            for (int i = 1; i < value.Length - 1; i++)
            {
                char c = value[i]; if (c == '"' || c == '\n' || c == '\r') throw new InvalidDataException("Invalid UDMF string.");
                if (c == '\\') { if (++i >= value.Length - 1) throw new InvalidDataException("Truncated UDMF escape."); c = value[i]; c = c == 'n' ? '\n' : c == 'r' ? '\r' : c == 't' ? '\t' : c; }
                result.Append(c);
            }
            return result.ToString();
        }
        static void ValidateScalar(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new InvalidDataException("Empty UDMF value.");
            if (value[0] == '"') { Decode(value); return; }
            bool boolean; if (bool.TryParse(value, out boolean)) return;
            if (value.IndexOf("0x", StringComparison.OrdinalIgnoreCase) >= 0) { ParseInt(value); return; }
            double number; if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number) || double.IsNaN(number) || double.IsInfinity(number)) throw new InvalidDataException("Invalid UDMF scalar: " + value);
        }
        static void Unique(List<UniversalField> fields)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in fields) if (f == null || !names.Add(f.name)) throw new InvalidDataException("Duplicate UDMF field.");
        }
        static void IdentifierValid(string name)
        {
            if (string.IsNullOrEmpty(name) || !(name[0] == '_' || (name[0] >= 'a' && name[0] <= 'z') || (name[0] >= 'A' && name[0] <= 'Z'))) throw new InvalidDataException("Invalid UDMF identifier.");
            foreach (char c in name) if (!(c == '_' || char.IsLetterOrDigit(c)) || c > 127) throw new InvalidDataException("Invalid UDMF identifier: " + name);
        }
#if MOBILEUDB_UDMF
        sealed class BlockData
        {
            public string raw;
            public List<UniversalField> fields = new List<UniversalField>();
            public List<string> nested = new List<string>();
        }
        sealed class Parser
        {
            readonly string text;
            int position;
            public Parser(string text) { this.text = text ?? throw new ArgumentNullException(nameof(text)); }
            public bool End { get { Trivia(); return position == text.Length; } }
            void Trivia()
            {
                while (position < text.Length)
                {
                    if (char.IsWhiteSpace(text[position]) || text[position] == '\uFEFF') { position++; continue; }
                    if (position + 1 < text.Length && text[position] == '/' && text[position + 1] == '/') { position += 2; while (position < text.Length && text[position] != '\n') position++; continue; }
                    if (position + 1 < text.Length && text[position] == '/' && text[position + 1] == '*') { int end = text.IndexOf("*/", position + 2, StringComparison.Ordinal); if (end < 0) throw Error("Unclosed comment"); position = end + 2; continue; }
                    break;
                }
            }
            public string Identifier()
            {
                Trivia(); int start = position; while (position < text.Length && (char.IsLetterOrDigit(text[position]) || text[position] == '_')) position++;
                string key = text.Substring(start, position - start); IdentifierValid(key); return key.ToLowerInvariant();
            }
            public bool Accept(char token) { Trivia(); if (position >= text.Length || text[position] != token) return false; position++; return true; }
            public void Expect(char token) { if (!Accept(token)) throw Error("Expected '" + token + "'"); }
            public string Value()
            {
                Trivia(); int start = position;
                if (position < text.Length && text[position] == '"')
                {
                    position++; bool closed = false;
                    while (position < text.Length)
                    {
                        char c = text[position++]; if (c == '\\') { if (position >= text.Length) throw Error("Truncated string"); position++; }
                        else if (c == '"') { closed = true; break; }
                    }
                    if (!closed) throw Error("Unclosed string");
                }
                else while (position < text.Length && !char.IsWhiteSpace(text[position]) && text[position] != ';' && text[position] != '}' && text[position] != '/' && text[position] != '{') position++;
                string value = text.Substring(start, position - start); ValidateScalar(value); return value;
            }
            public BlockData Block(string name, int depth = 0)
            {
                if (depth > 64) throw Error("UDMF block nesting limit exceeded");
                Expect('{'); int start = position - 1; var block = new BlockData();
                while (!Accept('}'))
                {
                    if (End) throw Error("Unclosed block"); string key = Identifier();
                    if (Accept('=')) { block.fields.Add(new UniversalField(key, Value())); Expect(';'); }
                    else block.nested.Add(Block(key, depth + 1).raw);
                }
                Unique(block.fields); block.raw = name + " " + text.Substring(start, position - start); return block;
            }
            InvalidDataException Error(string message) { return new InvalidDataException(message + " at character " + position + "."); }
        }
#endif
    }
}
