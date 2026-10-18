// GPL-3.0. TEXTURES structure follows UDB TexturesParser/TextureStructure/PatchStructure.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace MobileUDB.Core
{
    public sealed partial class DoomResources
    {
        void ResourceWarning(string message) { if (warnings.Count < 30 && !warnings.Contains(message)) warnings.Add(message); }
        void LoadTextureDefinitions(byte[] bytes,string path,Func<string,byte[]> resolver)
        {
            var definitions = new List<ResourceTexture>(); var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            ReadDefinitions(bytes,path,resolver,active,definitions);
            foreach (var texture in definitions)
            {
                if (texture.unsupported != null) ResourceWarning(texture.name + ": " + texture.unsupported);
                switch (texture.definitionKind)
                {
                    case "texture": walls[texture.name] = texture; flats[texture.name] = texture; break;
                    case "walltexture": walls[texture.name] = texture; break;
                    case "flat": flats[texture.name] = texture; break;
                    case "graphic": graphics[texture.name] = texture; break;
                    case "sprite":
                        sprites[texture.name] = texture; RegisterFrame(texture,texture.name,texture.name[4],texture.name[5],false);
                        if (texture.name.Length == 8) RegisterFrame(texture,texture.name,texture.name[6],texture.name[7],true); break;
                }
            }
        }
        void ReadDefinitions(byte[] bytes,string path,Func<string,byte[]> resolver,HashSet<string> active,List<ResourceTexture> result)
        {
            if (bytes.Length > 8 * 1024 * 1024 || active.Count >= 16 || !active.Add(path)) throw new InvalidDataException("Слишком большой или рекурсивный TEXTURES: " + path);
            try
            {
                var tokens = new TextureTokens(Encoding.UTF8.GetString(bytes),path);
                while (tokens.Peek != null)
                {
                    string kind = tokens.Take().ToLowerInvariant(); if (kind == ";") continue; if (kind == "$gzdb_skip") break;
                    if (kind == "#include")
                    {
                        string included = tokens.Take(); int slash = path.LastIndexOf('/');
                        string resolved = ResourceArchive.Normalize((slash < 0 ? "" : path.Substring(0,slash + 1)) + included);
                        var data = resolver(resolved); if (data == null) throw tokens.Error("Не найден include " + resolved);
                        ReadDefinitions(data,resolved,resolver,active,result); continue;
                    }
                    if (kind != "texture" && kind != "walltexture" && kind != "flat" && kind != "sprite" && kind != "graphic")
                    { throw tokens.Error("Неизвестное определение " + kind); }
                    string name = tokens.Take(); bool optional = string.Equals(name,"optional",StringComparison.OrdinalIgnoreCase); if (optional) name = tokens.Take();
                    if (name.Length == 0 || name.Length > 255 || (kind == "sprite" && name.Length != 6 && name.Length != 8)) throw tokens.Error("Некорректное имя текстуры " + name);
                    tokens.Expect(","); int width = tokens.Integer(); tokens.Expect(","); int height = tokens.Integer(); Dimensions(width,height);
                    var texture = new ResourceTexture { sourceName = textureSourceName, name = name.Replace('\\','/').ToUpperInvariant(),definitionKind = kind,optional = optional,width = width,height = height,
                        kind = kind == "flat" ? ResourceKind.Flat : kind == "sprite" ? ResourceKind.Sprite : ResourceKind.Wall,patches = new List<ResourcePatch>() };
                    tokens.Expect("{");
                    while (tokens.Peek != "}")
                    {
                        string property = tokens.Take().ToLowerInvariant();
                        switch (property)
                        {
                            case "xscale": texture.scaleX = TextureScale(tokens); break;
                            case "yscale": texture.scaleY = TextureScale(tokens); break;
                            case "worldpanning": texture.worldPanning = true; break;
                            case "nulltexture": texture.nullTexture = true; break;
                            case "offset": texture.offsetX = tokens.Integer(); tokens.Expect(","); texture.offsetY = tokens.Integer(); break;
                            case "patch": texture.patches.Add(ReadTexturePatch(tokens,texture)); if (texture.patches.Count > 4096) throw tokens.Error("Слишком много патчей"); break;
                            case ";": break;
                            default: Unsupported(texture,property); tokens.SkipProperty(TextureProperties); break;
                        }
                    }
                    tokens.Expect("}"); if (texture.patches.Count == 0 && !texture.nullTexture) Unsupported(texture,"нет патчей");
                    result.Add(texture); if (result.Count > 100000) throw tokens.Error("Слишком много определений текстур");
                }
            }
            finally { active.Remove(path); }
        }
        static readonly HashSet<string> TextureProperties = new HashSet<string>(new[] { "xscale","yscale","worldpanning","nulltexture","offset","patch" },StringComparer.OrdinalIgnoreCase);
        static readonly HashSet<string> PatchProperties = new HashSet<string>(new[] { "flipx","flipy","rotate","alpha","style","blend" },StringComparer.OrdinalIgnoreCase);
        static void Unsupported(ResourceTexture texture,string property) { texture.unsupported = "Не поддерживается в TEXTURES: " + property; }
        static float TextureScale(TextureTokens tokens)
        { float value = tokens.Real(); if (value < 0 || (value > 0 && value < .000001f)) throw tokens.Error("Некорректный масштаб"); return value == 0 ? 1 : 1 / value; }
        ResourcePatch ReadTexturePatch(TextureTokens tokens,ResourceTexture texture)
        {
            var patch = new ResourcePatch { name = tokens.Take().Replace('\\','/').ToUpperInvariant() }; tokens.Expect(","); patch.x = tokens.Integer(); tokens.Expect(","); patch.y = tokens.Integer();
            if (tokens.Peek != "{") return patch; tokens.Take();
            while (tokens.Peek != "}")
            {
                string property = tokens.Take().ToLowerInvariant();
                switch (property)
                {
                    case "flipx": patch.flipX = true; break;
                    case "flipy": patch.flipY = true; break;
                    case "rotate": patch.rotation = (tokens.Integer() % 360 + 360) % 360; if (patch.rotation % 90 != 0) Unsupported(texture,"Rotate " + patch.rotation); break;
                    case "alpha": patch.alpha = Math.Max(0,Math.Min(1,tokens.Real())); break;
                    case "style": patch.style = tokens.Take().ToLowerInvariant(); if (patch.style != "copy" && patch.style != "translucent" && patch.style != "add" && patch.style != "subtract" && patch.style != "reversesubtract" && patch.style != "modulate") Unsupported(texture,"Style " + patch.style); break;
                    case "blend": ReadTextureBlend(tokens,patch,texture); break;
                    case ";": break;
                    default: Unsupported(texture,property); tokens.SkipProperty(PatchProperties); break;
                }
            }
            tokens.Expect("}"); return patch;
        }
        readonly Dictionary<string,uint> namedColors = new Dictionary<string,uint>(StringComparer.OrdinalIgnoreCase);
        void ReadNamedColors(byte[] bytes)
        {
            if (bytes.Length > 8 * 1024 * 1024) throw new InvalidDataException("Слишком большой X11R6RGB.");
            using (var reader = new StringReader(Encoding.UTF8.GetString(bytes)))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    line = line.Trim(); if (line.Length == 0 || line[0] == '!') continue;
                    string[] parts = line.Split(new[] { ' ', '\t' },StringSplitOptions.RemoveEmptyEntries); byte r,g,b;
                    if (parts.Length < 4 || !byte.TryParse(parts[0],out r) || !byte.TryParse(parts[1],out g) || !byte.TryParse(parts[2],out b))
                        throw new InvalidDataException("Некорректный цвет X11R6RGB: " + line);
                    namedColors[string.Join("",parts,3,parts.Length - 3)] = (uint)(r << 16 | g << 8 | b);
                }
            }
        }
        void ReadTextureBlend(TextureTokens tokens,ResourcePatch patch,ResourceTexture texture)
        {
            string value = tokens.Take(); int red;
            if (int.TryParse(value,NumberStyles.Integer,CultureInfo.InvariantCulture,out red))
            { patch.red = red; tokens.Expect(","); patch.green = tokens.Integer(); tokens.Expect(","); patch.blue = tokens.Integer(); }
            else
            {
                string hex = value.TrimStart('#').Replace(" ",""); uint color;
                if (value.StartsWith("#",StringComparison.Ordinal) && hex.Length == 3) hex = new string(new[] { hex[0],hex[0],hex[1],hex[1],hex[2],hex[2] });
                if (!uint.TryParse(hex,NumberStyles.HexNumber,CultureInfo.InvariantCulture,out color) && !namedColors.TryGetValue(hex,out color))
                    Unsupported(texture,"Blend: неизвестный цвет " + value + " (нужен X11R6RGB)");
                patch.red = (int)((color >> 16) & 255); patch.green = (int)((color >> 8) & 255); patch.blue = (int)(color & 255);
            }
            if (patch.red < 0 || patch.red > 255 || patch.green < 0 || patch.green > 255 || patch.blue < 0 || patch.blue > 255) throw tokens.Error("Blend: компоненты цвета должны быть 0–255");
            patch.tint = -1;
            if (tokens.Peek == ",") { tokens.Take(); patch.tint = Math.Max(0,Math.Min(1,tokens.Real())); }
        }
        sealed class TextureTokens
        {
            readonly string source,path; int position,line = 1,count; string peek; bool loaded;
            public TextureTokens(string source,string path) { this.source = source; this.path = path; }
            public InvalidDataException Error(string message) { return new InvalidDataException(path + ":" + line + ": " + message); }
            public string Peek { get { if (!loaded) { peek = Read(); loaded = true; } return peek; } }
            public string Take() { string value = Peek; if (value == null) throw Error("Неожиданный конец определения"); loaded = false; return value; }
            public void Expect(string value) { if (Take() != value) throw Error("Ожидалось " + value); }
            public int Integer() { int value; if (!int.TryParse(Take(),NumberStyles.Integer,CultureInfo.InvariantCulture,out value)) throw Error("Ожидалось целое число"); return value; }
            public float Real() { float value; if (!float.TryParse(Take(),NumberStyles.Float,CultureInfo.InvariantCulture,out value) || float.IsNaN(value) || float.IsInfinity(value)) throw Error("Ожидалось конечное число"); return value; }
            public void SkipProperty(HashSet<string> known)
            {
                while (Peek != null && Peek != "}" && !known.Contains(Peek))
                {
                    if (Take() != "{") continue; int depth = 1;
                    while (depth > 0) { string value = Take(); if (value == "{") depth++; if (value == "}") depth--; }
                }
            }
            string Read()
            {
                while (position < source.Length)
                {
                    char c = source[position];
                    if (char.IsWhiteSpace(c) || c == '\uFEFF') { if (c == '\n') line++; position++; continue; }
                    if (c == '/' && position + 1 < source.Length && source[position + 1] == '/') { while (position < source.Length && source[position] != '\n') position++; continue; }
                    if (c == '/' && position + 1 < source.Length && source[position + 1] == '*')
                    {
                        position += 2; while (position + 1 < source.Length && (source[position] != '*' || source[position + 1] != '/')) { if (source[position++] == '\n') line++; }
                        if (position + 1 >= source.Length) throw Error("Незакрытый комментарий"); position += 2; continue;
                    }
                    break;
                }
                if (position >= source.Length) return null; if (++count > 1000000) throw Error("Слишком много элементов TEXTURES");
                char first = source[position++]; if (first == '{' || first == '}' || first == ',' || first == ';') return first.ToString();
                if (first == '"')
                {
                    var value = new StringBuilder();
                    while (position < source.Length)
                    {
                        char c = source[position++]; if (c == '"') return value.ToString(); if (c == '\n') line++;
                        if (c == '\\' && position < source.Length && (source[position] == '"' || source[position] == '\\')) c = source[position++]; value.Append(c);
                    }
                    throw Error("Незакрытая строка");
                }
                int start = position - 1;
                while (position < source.Length && !char.IsWhiteSpace(source[position]) && "{},;".IndexOf(source[position]) < 0)
                { if (source[position] == '/' && position + 1 < source.Length && (source[position + 1] == '/' || source[position + 1] == '*')) break; position++; }
                return source.Substring(start,position - start);
            }
        }
    }
}
