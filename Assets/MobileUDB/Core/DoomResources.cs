// GPL-3.0. Format layout follows UDB WADReader, PatchNames,
// DoomPictureReader and DoomFlatReader (Pascal vd Heiden and contributors).
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MobileUDB.Core
{
    public enum ResourceKind { Wall, Flat, Sprite }
    public sealed class ResourceImage
    {
        // RGBA rows are top to bottom, as in the Doom lump formats.
        public readonly int width, height;
        public readonly byte[] rgba;
        public ResourceImage(int width, int height, byte[] rgba) { this.width = width; this.height = height; this.rgba = rgba; }
    }
    public sealed class ResourceTexture
    {
        public string name, shortName, sourceName;
        public string MapName(bool longNames) { return longNames || shortName == null ? name : shortName; }
        public ResourceKind kind;
        public int width, height;
        public int offsetX, offsetY;
        public float scaleX = 1, scaleY = 1;
        public bool worldPanning;
        internal byte[] image;
        internal List<ResourcePatch> patches;
        internal string definitionKind, unsupported;
        internal bool optional, nullTexture;
    }
    sealed class ResourcePatch
    {
        public string name, style = "copy";
        public int x, y, rotation;
        public bool flipX, flipY;
        public float alpha = 1, tint = -2;
        public int red = 255, green = 255, blue = 255;
    }
    public sealed partial class DoomResources
    {
        const int MaxPixels = 4 * 1024 * 1024, MaxDimension = 4096;
        readonly Dictionary<string, ResourceTexture> walls = new Dictionary<string, ResourceTexture>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, ResourceTexture> flats = new Dictionary<string, ResourceTexture>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, ResourceTexture> sprites = new Dictionary<string, ResourceTexture>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, SpriteFrame> spriteFrames = new Dictionary<string, SpriteFrame>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, byte[]> lumps = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, ResourceTexture> graphics = new Dictionary<string, ResourceTexture>(StringComparer.OrdinalIgnoreCase);
        public readonly List<string> warnings = new List<string>();
        // Keep all patches in container order, rather than only the last lump.
        public readonly List<ResourceFile> dehackedPatches = new List<ResourceFile>();
        byte[] palette;
        string textureSourceName;
        public int WallCount { get { return new HashSet<ResourceTexture>(walls.Values).Count; } }
        public int FlatCount { get { return new HashSet<ResourceTexture>(flats.Values).Count; } }
        public int SpriteCount { get { return new HashSet<ResourceTexture>(sprites.Values).Count; } }
        public bool HasPalette { get { return palette != null; } }
        public static DoomResources Build(IEnumerable<DoomWad> sources)
        {
            var result = new DoomResources(); string[] patchNames = new string[0];
            var ordered = new List<DoomWad>(sources);
            foreach (var wad in ordered) if (wad != null) foreach (var lump in wad.lumps) if (lump.name == "X11R6RGB") result.ReadNamedColors(lump.data);
            foreach (var wad in ordered) { result.CollectMapSky(wad);result.LoadWad(wad,ref patchNames); }
            return result;
        }
        public static DoomResources BuildArchives(IEnumerable<ResourceArchive> sources)
        {
            var result = new DoomResources(); string[] patchNames = new string[0];
            var ordered = new List<ResourceArchive>(sources);
            foreach (var source in ordered) if (source != null)
            {
                foreach (var file in source.files) if (file.path.IndexOf('/') < 0 && string.Equals(file.Stem,"X11R6RGB",StringComparison.OrdinalIgnoreCase)) result.ReadNamedColors(file.data);
                foreach (var wad in source.wads) foreach (var lump in wad.lumps) if (lump.name == "X11R6RGB") result.ReadNamedColors(lump.data);
            }
            foreach (var source in ordered)
            {
                if (source == null) continue;
                result.textureSourceName=source.SourceName ?? "WAD карты";
                result.CollectMapSky(source);
                if(source.isDehacked)result.dehackedPatches.AddRange(source.files);
                else if (source.isPk3) result.LoadPk3(source,ref patchNames);
                else foreach (var wad in source.wads) result.LoadWad(wad,ref patchNames);
            }
            return result;
        }
        void LoadWad(DoomWad wad,ref string[] patchNames)
        {
                if (wad == null) return; var result = this;
                byte[] names = null;
                foreach (var lump in wad.lumps)
                {
                    result.lumps[lump.name] = lump.data;
                    if(lump.name=="DEHACKED")result.dehackedPatches.Add(new ResourceFile("DEHACKED #"+result.dehackedPatches.Count,lump.data));
                    if (lump.name == "PLAYPAL")
                    {
                        if (lump.data.Length < 768) throw new InvalidDataException("PLAYPAL содержит неполную палитру.");
                        result.palette = lump.data;
                    }
                    if (lump.name == "PNAMES") names = lump.data;
                }
                // UDB inherits PNAMES when the next container has none of its own.
                if (names != null) patchNames = ReadPatchNames(names);
                byte[] texture1 = null, texture2 = null; int flatDepth = 0, textureDepth = 0, spriteDepth = 0;
                foreach (var lump in wad.lumps)
                {
                    string name = lump.name;
                    if (name == "TEXTURE1") texture1 = lump.data;
                    if (name == "TEXTURE2") texture2 = lump.data;
                    if (Marker(name, 'F', true)) { flatDepth++; continue; }
                    if (Marker(name, 'F', false)) { flatDepth = Math.Max(0, flatDepth - 1); continue; }
                    if (Marker(name, 'S', true)) { spriteDepth++; continue; }
                    if (Marker(name, 'S', false)) { spriteDepth = Math.Max(0, spriteDepth - 1); continue; }
                    if (name == "TX_START") { textureDepth++; continue; }
                    if (name == "TX_END") { textureDepth = Math.Max(0, textureDepth - 1); continue; }
                    if (lump.data.Length == 0) continue;
                    try
                    {
                        if (flatDepth > 0) result.AddImage(name, lump.data, ResourceKind.Flat);
                        if (textureDepth > 0) result.AddImage(name, lump.data, ResourceKind.Wall);
                        if (spriteDepth > 0) result.AddImage(name, lump.data, ResourceKind.Sprite);
                    }
                    catch (InvalidDataException e) { if (result.warnings.Count < 30) result.warnings.Add(name + ": " + e.Message); }
                }
                // TX namespace overrides composite textures from the same archive.
                var loose = new Dictionary<string, ResourceTexture>(result.walls, StringComparer.OrdinalIgnoreCase);
                if (texture1 != null) result.ReadTextureSet(texture1, patchNames, true);
                if (texture2 != null) result.ReadTextureSet(texture2, patchNames, false);
                if (textureDepth != 0 || flatDepth != 0 || spriteDepth != 0) result.warnings.Add("В архиве не закрыта область изображений.");
                bool inTx = false;
                foreach (var lump in wad.lumps)
                {
                    if (lump.name == "TX_START") inTx = true;
                    else if (lump.name == "TX_END") inTx = false;
                    else if (inTx && loose.ContainsKey(lump.name)) result.walls[lump.name] = loose[lump.name];
                }
                foreach (var lump in wad.lumps) if (lump.name == "TEXTURES") LoadTextureDefinitions(lump.data,"TEXTURES",path =>
                { for (int i = wad.lumps.Count - 1; i >= 0; i--) if (string.Equals(wad.lumps[i].name,path,StringComparison.OrdinalIgnoreCase)) return wad.lumps[i].data; return null; });
        }
        static bool Marker(string name, char prefix, bool start)
        {
            string suffix = start ? "_START" : "_END";
            if (!name.EndsWith(suffix, StringComparison.Ordinal) || name[0] != prefix) return false;
            string stem = name.Substring(0, name.Length - suffix.Length);
            if (stem.Length < 1 || stem.Length > 2) return false;
            return stem.Length == 1 || stem[1] == prefix || char.IsDigit(stem[1]);
        }
        static void Dimensions(int width, int height)
        {
            if (width < 1 || height < 1 || width > MaxDimension || height > MaxDimension || (long)width * height > MaxPixels)
                throw new InvalidDataException("Размер текстуры превышает допустимый предел.");
        }
        static void Need(byte[] data, int at, long count)
        {
            if (at < 0 || count < 0 || (long)at + count > data.Length) throw new InvalidDataException("Повреждённые данные текстуры.");
        }
        static int I32(byte[] data, int at) { Need(data, at, 4); return BitConverter.ToInt32(data, at); }
        static int I16(byte[] data, int at) { Need(data, at, 2); return BitConverter.ToInt16(data, at); }
        static string Name(byte[] data, int at)
        {
            Need(data, at, 8); int count = 0; while (count < 8 && data[at + count] != 0) count++;
            return Encoding.ASCII.GetString(data, at, count).Trim().ToUpperInvariant();
        }
        static string[] ReadPatchNames(byte[] data)
        {
            int count = I32(data, 0); Need(data, 4, count * 8L);
            if (count > 100000) throw new InvalidDataException("Слишком много имён патчей.");
            var names = new string[count]; for (int i = 0; i < count; i++) names[i] = Name(data, 4 + i * 8); return names;
        }
        void ReadTextureSet(byte[] data, string[] names, bool skipFirst)
        {
            int count = I32(data, 0); Need(data, 4, count * 4L);
            if (count > 100000) throw new InvalidDataException("Слишком много текстур.");
            for (int i = 0; i < count; i++)
            {
                int at = I32(data, 4 + i * 4); Need(data, at, 18);
                string name = Name(data, at); int width = I16(data, at + 12), height = I16(data, at + 14);
                int patchCount = I16(data, at + 16), header = 18, stride = 6;
                // Doom has a zero column-directory field; Strife omits it.
                if (patchCount == 0) { patchCount = I16(data, at + 20); header = 22; stride = 10; }
                Need(data, at + header, patchCount * (long)stride);
                if (skipFirst && i == 0) continue;
                Dimensions(width, height);
                var texture = new ResourceTexture { sourceName = textureSourceName, name = name, kind = ResourceKind.Wall, width = width, height = height,
                    scaleX = data[at + 10] == 0 ? 1 : 8f / data[at + 10], scaleY = data[at + 11] == 0 ? 1 : 8f / data[at + 11],
                    worldPanning = (BitConverter.ToUInt16(data, at + 8) & 0x8000) != 0, patches = new List<ResourcePatch>() };
                for (int p = 0; p < patchCount; p++)
                {
                    int record = at + header + p * stride; Need(data, record, 6); int index = BitConverter.ToUInt16(data, record + 4);
                    if (index >= names.Length || string.IsNullOrEmpty(names[index])) throw new InvalidDataException("Неизвестный патч в текстуре " + name + ".");
                    texture.patches.Add(new ResourcePatch { name = names[index], x = I16(data, record), y = I16(data, record + 2) });
                }
                // The first TEXTURE1 entry is Doom's reserved null texture, as in UDB.
                if (!string.IsNullOrEmpty(name)) walls[name] = texture;
            }
        }
        void AddImage(string name, byte[] data, ResourceKind kind,string shortName = null)
        {
            int width, height, offsetX = 0, offsetY = 0;
            bool png = PngDimensions(data, out width, out height);
            if (!png)
            {
                if (kind == ResourceKind.Flat) FlatDimensions(data, out width, out height);
                else { width = I16(data, 0); height = I16(data, 2); offsetX = I16(data,4); offsetY = I16(data,6); }
            }
            Dimensions(width, height);
            if (png && kind == ResourceKind.Sprite) PngOffsets(data,out offsetX,out offsetY);
            var entry = new ResourceTexture { sourceName = textureSourceName, name = name, shortName = shortName, kind = kind, width = width, height = height, offsetX = offsetX, offsetY = offsetY, image = data };
            Dictionary(kind)[name] = entry;
            if (shortName != null) Dictionary(kind)[shortName] = entry;
            string frameName = shortName ?? name;
            if (kind == ResourceKind.Sprite && (frameName.Length == 6 || frameName.Length == 8))
            {
                // Assign in archive/lump order, including the mirrored second frame block.
                RegisterFrame(entry,frameName,frameName[4],frameName[5],false);
                if (frameName.Length == 8) RegisterFrame(entry,frameName,frameName[6],frameName[7],true);
            }
        }
        Dictionary<string,ResourceTexture> Dictionary(ResourceKind kind) { return kind == ResourceKind.Wall ? walls : kind == ResourceKind.Flat ? flats : sprites; }
        void RegisterFrame(ResourceTexture entry,string frameName, char frame, char rotation, bool mirror)
        {
            if (rotation < '0' || rotation > '8') return;
            string key = frameName.Substring(0,4) + frame;
            if (rotation == '0') for (int i = 1; i <= 8; i++) spriteFrames[key + i] = new SpriteFrame(entry,mirror);
            else spriteFrames[key + rotation] = new SpriteFrame(entry,mirror);
        }
        public SpriteFrame FindSprite(string configured, int rotation = 2)
        {
            if (string.IsNullOrEmpty(configured) || configured.Length < 5) return null;
            SpriteFrame frame; string stem = configured.Substring(0,5);
            if (spriteFrames.TryGetValue(stem + Math.Max(1,Math.Min(8,rotation)),out frame)) return frame;
            // Partial mod replacements can still show a known frame rather than vanish.
            ResourceTexture exact; if (sprites.TryGetValue(configured,out exact)) return new SpriteFrame(exact,false);
            for (int i = 1; i <= 8; i++) if (spriteFrames.TryGetValue(stem + i,out frame)) return frame;
            return null;
        }
        public List<ResourceTexture> Textures(ResourceKind kind, bool mixed = false,bool longNames = true)
        {
            var choices = new Dictionary<string, ResourceTexture>(Dictionary(kind), StringComparer.OrdinalIgnoreCase);
            if (mixed && kind != ResourceKind.Sprite) foreach (var entry in kind == ResourceKind.Wall ? flats : walls) if (!choices.ContainsKey(entry.Key)) choices[entry.Key] = entry.Value;
            var unique = new HashSet<ResourceTexture>(); foreach (var choice in choices) if (longNames || (choice.Key.Length <= 8 && choice.Key.IndexOf('/') < 0)) unique.Add(choice.Value);
            var result = new List<ResourceTexture>(unique); result.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.MapName(longNames), b.MapName(longNames))); return result;
        }
        public ResourceTexture Find(string name, ResourceKind kind, bool mixed = false)
        {
            if (string.IsNullOrEmpty(name)) return null; name = name.Replace('\\','/');
            ResourceTexture entry; var first = Dictionary(kind);
            if (first.TryGetValue(name, out entry)) return entry;
            if (mixed && kind != ResourceKind.Sprite && (kind == ResourceKind.Wall ? flats : walls).TryGetValue(name, out entry)) return entry;
            return null;
        }
        public ResourceImage Decode(ResourceTexture entry, Func<byte[], ResourceImage> pngDecoder = null, int maximumSize = 0)
        {
            if (entry == null) throw new InvalidDataException("Текстура отсутствует в ресурсах.");
            ResourceImage image = DecodeComposite(entry,pngDecoder,new TextureDecodeBudget());
            if (maximumSize <= 0 || Math.Max(image.width, image.height) <= maximumSize) return image;
            float factor = maximumSize / (float)Math.Max(image.width, image.height);
            int w = Math.Max(1, (int)(image.width * factor)), h = Math.Max(1, (int)(image.height * factor)); var output = new byte[w * h * 4];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                Buffer.BlockCopy(image.rgba, ((y * image.height / h) * image.width + x * image.width / w) * 4, output, (y * w + x) * 4, 4);
            return new ResourceImage(w, h, output);
        }
        ResourceImage DecodeImage(byte[] bytes, ResourceKind kind, Func<byte[], ResourceImage> pngDecoder)
        {
            int width, height;
            if (PngDimensions(bytes, out width, out height))
            {
                Dimensions(width, height);
                if (pngDecoder == null) throw new NotSupportedException("Для PNG требуется декодер изображений.");
                var decoded = pngDecoder(bytes);
                if (decoded == null || decoded.width != width || decoded.height != height || decoded.rgba.Length != width * height * 4)
                    throw new InvalidDataException("Не удалось декодировать PNG.");
                return decoded;
            }
            if (palette == null) throw new InvalidDataException("Не загружена палитра PLAYPAL. Подключите IWAD.");
            if (kind == ResourceKind.Flat)
            {
                FlatDimensions(bytes, out width, out height); Dimensions(width, height); var rgba = new byte[width * height * 4];
                for (int i = 0; i < width * height; i++) Pixel(rgba, i * 4, bytes[i]); return new ResourceImage(width, height, rgba);
            }
            width = I16(bytes, 0); height = I16(bytes, 2); Dimensions(width, height); Need(bytes, 8, width * 4L);
            var pixels = new byte[width * height * 4];
            for (int x = 0; x < width; x++)
            {
                int at = I32(bytes, 8 + x * 4); if (at < 8 + width * 4) throw new InvalidDataException("Повреждённый адрес столбца патча.");
                int top = -1;
                while (true)
                {
                    Need(bytes, at, 1); int next = bytes[at++]; if (next == 255) break;
                    top = top < 0 || (next > top || (next == top && height <= 256)) ? next : checked(top + next);
                    Need(bytes, at, 2); int length = bytes[at]; at += 2; Need(bytes, at, length + 1L);
                    if ((long)top + length > height) throw new InvalidDataException("Пост патча выходит за границу изображения.");
                    for (int y = 0; y < length; y++) Pixel(pixels, ((top + y) * width + x) * 4, bytes[at + y]);
                    at += length + 1;
                }
            }
            return new ResourceImage(width, height, pixels);
        }
        void Pixel(byte[] rgba, int at, int index)
        {
            rgba[at] = palette[index * 3]; rgba[at + 1] = palette[index * 3 + 1]; rgba[at + 2] = palette[index * 3 + 2]; rgba[at + 3] = 255;
        }
        static void FlatDimensions(byte[] data, out int width, out int height)
        {
            int side = (int)Math.Sqrt(data.Length);
            if ((long)side * side == data.Length && side > 0) { width = height = side; }
            else if (data.Length > 4096) width = height = 64;
            else throw new InvalidDataException("Неизвестный формат плоской текстуры.");
        }
        static bool PngDimensions(byte[] data, out int width, out int height)
        {
            width = height = 0;
            if (data.Length < 8 || data[0] != 137 || data[1] != 80 || data[2] != 78 || data[3] != 71 || data[4] != 13 || data[5] != 10 || data[6] != 26 || data[7] != 10) return false;
            Need(data, 8, 16);
            if (Encoding.ASCII.GetString(data, 12, 4) != "IHDR") throw new InvalidDataException("В PNG нет IHDR.");
            width = checked((int)((uint)data[16] << 24 | (uint)data[17] << 16 | (uint)data[18] << 8 | data[19]));
            height = checked((int)((uint)data[20] << 24 | (uint)data[21] << 16 | (uint)data[22] << 8 | data[23])); return true;
        }
        static void PngOffsets(byte[] data, out int x, out int y)
        {
            x = y = 0; int at = 8;
            while (at < data.Length)
            {
                Need(data,at,12); uint count = Big32(data,at); Need(data,at + 8,(long)count + 4);
                if (Encoding.ASCII.GetString(data,at + 4,4) == "grAb")
                { if (count != 8) throw new InvalidDataException("Повреждённый PNG grAb."); x = unchecked((int)Big32(data,at + 8)); y = unchecked((int)Big32(data,at + 12)); return; }
                at = checked(at + 12 + (int)count);
            }
        }
        static uint Big32(byte[] data, int at) { return (uint)data[at] << 24 | (uint)data[at + 1] << 16 | (uint)data[at + 2] << 8 | data[at + 3]; }
    }
    public sealed class SpriteFrame
    {
        public readonly ResourceTexture image; public readonly bool mirror;
        public SpriteFrame(ResourceTexture image, bool mirror) { this.image = image; this.mirror = mirror; }
    }
}
