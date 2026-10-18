// Binary layout follows Source/Core/IO/DoomMapSetIO.cs in Ultimate Doom Builder.
// GPL-3.0; original UDB authors: Pascal vd Heiden and contributors.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MobileUDB.Core
{
    public sealed class WadLump
    {
        public string name;
        public byte[] data;
        public WadLump(string name, byte[] data) { this.name = name; this.data = data; }
    }
    public sealed class DoomWad
    {
        public readonly List<WadLump> lumps = new List<WadLump>();
        const int MaxFile = 256 * 1024 * 1024;
        static readonly HashSet<string> MapLumps = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "THINGS", "LINEDEFS", "SIDEDEFS", "VERTEXES", "SEGS", "SSECTORS", "NODES", "SECTORS", "REJECT", "BLOCKMAP", "BEHAVIOR", "SCRIPTS", "ZNODES", "DIALOGUE", "LEAFS", "LIGHTS", "MACROS" };
        static readonly HashSet<string> GeometryLumps = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "THINGS", "LINEDEFS", "SIDEDEFS", "VERTEXES", "SECTORS", "TEXTMAP", "ENDMAP", "SEGS", "SSECTORS", "NODES", "REJECT", "BLOCKMAP", "ZNODES", "GL_VERT", "GL_SEGS", "GL_SSECT", "GL_NODES", "GL_PVS" };
        static readonly HashSet<string> GlLumps = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "GL_VERT", "GL_SEGS", "GL_SSECT", "GL_NODES", "GL_PVS" };
        public static DoomWad Read(byte[] bytes,long maximumContentBytes = MaxFile)
        {
            if (bytes == null || bytes.Length < 12 || bytes.Length > MaxFile) throw new InvalidDataException("Invalid WAD size (maximum 256 MB).");
            using (var stream = new MemoryStream(bytes))
            using (var reader = new BinaryReader(stream))
            {
                string signature = Encoding.ASCII.GetString(reader.ReadBytes(4));
                if (signature != "PWAD" && signature != "IWAD") throw new InvalidDataException("Not a WAD file.");
                int count = reader.ReadInt32(), offset = reader.ReadInt32();
                if (count < 0 || count > 1000000 || offset < 12 || (long)offset + count * 16L > bytes.Length) throw new InvalidDataException("Invalid WAD directory.");
                var wad = new DoomWad(); long totalSize = 0;
                stream.Position = offset;
                for (int i = 0; i < count; i++)
                {
                    int position = reader.ReadInt32(), size = reader.ReadInt32(); string name = ReadName(reader);
                    if (position < 0 || size < 0 || (long)position + size > bytes.Length) throw new InvalidDataException("Invalid lump: " + name);
                    // Avoid overlap amplification from malicious directory entries.
                    totalSize += size;
                    if (totalSize > MaxFile || totalSize > maximumContentBytes) throw new InvalidDataException("WAD contents exceed memory limit.");
                    var data = new byte[size]; Buffer.BlockCopy(bytes, position, data, 0, size);
                    wad.lumps.Add(new WadLump(name, data));
                }
                return wad;
            }
        }
        struct StreamLump { public int position,size; public string name; }
        public static DoomWad ReadStream(Stream stream,long maximumContentBytes=MaxFile,Func<bool> cancelled=null)
        {
            if(stream==null||!stream.CanRead||!stream.CanSeek||stream.Length<12||stream.Length>MaxFile)throw new InvalidDataException("Invalid WAD size (maximum 256 MB).");
            Action check=()=>{if(cancelled!=null&&cancelled())throw new OperationCanceledException();};check();stream.Position=0;
            using(var reader=new BinaryReader(stream,Encoding.ASCII,true))
            {
                string signature=Encoding.ASCII.GetString(reader.ReadBytes(4));if(signature!="PWAD"&&signature!="IWAD")throw new InvalidDataException("Not a WAD file.");
                int count=reader.ReadInt32(),offset=reader.ReadInt32();long length=stream.Length;
                if(count<0||count>1000000||offset<12||(long)offset+count*16L>length)throw new InvalidDataException("Invalid WAD directory.");
                // Read the directory once; avoid seeking back to it for each lump.
                var entries=new List<StreamLump>(count);long total=0;stream.Position=offset;
                for(int i=0;i<count;i++)
                {
                    check();int position=reader.ReadInt32(),size=reader.ReadInt32();string name=ReadName(reader);
                    if(position<0||size<0||(long)position+size>length)throw new InvalidDataException("Invalid lump: "+name);
                    total+=size;if(total>MaxFile||total>maximumContentBytes)throw new InvalidDataException("WAD contents exceed memory limit.");
                    entries.Add(new StreamLump{position=position,size=size,name=name});
                }
                var wad=new DoomWad();foreach(var entry in entries)
                {
                    check();var data=new byte[entry.size];stream.Position=entry.position;int read=0;
                    while(read<data.Length){check();int got=stream.Read(data,read,Math.Min(65536,data.Length-read));if(got==0)throw new InvalidDataException("Incomplete lump: "+entry.name);read+=got;}
                    wad.lumps.Add(new WadLump(entry.name,data));
                }
                return wad;
            }
        }
        bool IsMarker(int index)
        {
            if (index + 1 >= lumps.Count) return false;
            return lumps[index].data.Length == 0 && (lumps[index + 1].name == "THINGS" || lumps[index + 1].name == "TEXTMAP");
        }
        public List<string> MapNames()
        {
            var names = new List<string>();
            for (int i = 0; i < lumps.Count; i++) if (IsMarker(i)) names.Add(lumps[i].name);
            return names;
        }
        public List<string> EditableMapNames()
        {
            var names = new List<string>();
            for(int i=0;i<lumps.Count;i++)
                if(IsMarker(i) && (lumps[i+1].name != "TEXTMAP" || MapFormats.IsUniversal(MapFormat.Udmf)))
                    names.Add(lumps[i].name);
            return names;
        }
        int Marker(string name)
        {
            for (int i = 0; i < lumps.Count; i++) if (IsMarker(i) && string.Equals(lumps[i].name, name, StringComparison.OrdinalIgnoreCase)) return i;
            throw new InvalidDataException("Map not found: " + name);
        }
        int MapEnd(int marker)
        {
            int end = marker + 1;
            if (end < lumps.Count && lumps[end].name == "TEXTMAP")
            {
                while (end < lumps.Count && lumps[end].name != "ENDMAP")
                {
                    if (end != marker + 1 && IsMarker(end)) throw new InvalidDataException("UDMF map has no ENDMAP: " + lumps[marker].name);
                    end++;
                }
                if (end >= lumps.Count) throw new InvalidDataException("UDMF map has no ENDMAP: " + lumps[marker].name);
                return end + 1;
            }
            while (end < lumps.Count && MapLumps.Contains(lumps[end].name)) end++;
            return end;
        }
        Dictionary<string, byte[]> MapData(string name)
        {
            int marker = Marker(name), end = MapEnd(marker); var data = new Dictionary<string, byte[]>();
            for (int i = marker + 1; i < end; i++) data[lumps[i].name] = lumps[i].data;
            if (data.ContainsKey("MACROS") || data.ContainsKey("LEAFS")) throw new NotSupportedException("Doom 64 / console map format is not supported yet.");
            return data;
        }
        static BinaryReader Records(Dictionary<string, byte[]> data, string name, int size)
        {
            byte[] bytes;
            if (!data.TryGetValue(name, out bytes) || bytes.Length % size != 0) throw new InvalidDataException("Invalid or missing " + name);
            return new BinaryReader(new MemoryStream(bytes));
        }
        public MapDocument Import(string name)
        {
            var data = MapData(name);
#if MOBILEUDB_UDMF
            var map = data.ContainsKey("TEXTMAP") ? UdmfText.Read(new UTF8Encoding(false, true).GetString(data["TEXTMAP"]), name) : ImportBinary(data, name);
#else
            if(data.ContainsKey("TEXTMAP"))MapFormats.RequireSupported(MapFormat.Udmf);
            var map = ImportBinary(data,name);
#endif
            int marker = Marker(name), end = MapEnd(marker);
            for (int i = marker + 1; i < end; i++) if (!GeometryLumps.Contains(lumps[i].name)) map.auxiliary.Add(new MapAuxiliaryLump(lumps[i].name, lumps[i].data));
            return map;
        }
        static MapDocument ImportBinary(Dictionary<string, byte[]> data, string name)
        {
            bool hexen = data.ContainsKey("BEHAVIOR"); var map = new MapDocument { name = name, format = hexen ? MapFormat.Hexen : MapFormat.Doom };
            using (var r = Records(data, "VERTEXES", 4)) while (r.BaseStream.Position < r.BaseStream.Length) map.vertices.Add(new MapVertex(r.ReadInt16(), r.ReadInt16()));
            using (var r = Records(data, "SECTORS", 26)) while (r.BaseStream.Position < r.BaseStream.Length)
                map.sectors.Add(new MapSector { floor = r.ReadInt16(), ceiling = r.ReadInt16(), floorTexture = ReadName(r), ceilingTexture = ReadName(r), light = r.ReadInt16(), special = r.ReadUInt16(), tag = r.ReadUInt16() });
            using (var r = Records(data, "SIDEDEFS", 30)) while (r.BaseStream.Position < r.BaseStream.Length)
                map.sides.Add(new MapSide { offsetX = r.ReadInt16(), offsetY = r.ReadInt16(), upper = ReadName(r), lower = ReadName(r), middle = ReadName(r), sector = r.ReadUInt16() });
            using (var r = Records(data, "LINEDEFS", hexen ? 16 : 14)) while (r.BaseStream.Position < r.BaseStream.Length)
            {
                var l = new MapLine { start = r.ReadUInt16(), end = r.ReadUInt16(), flags = r.ReadUInt16() };
                if (hexen) { l.action = r.ReadByte(); for (int i = 0; i < 5; i++) l.args[i] = r.ReadByte(); }
                else { l.action = r.ReadUInt16(); l.tag = r.ReadUInt16(); }
                l.front = SideIndex(r.ReadUInt16()); l.back = SideIndex(r.ReadUInt16()); map.lines.Add(l);
            }
            using (var r = Records(data, "THINGS", hexen ? 20 : 10)) while (r.BaseStream.Position < r.BaseStream.Length)
            {
                var t = new MapThing(); if (hexen) t.tag = r.ReadUInt16();
                t.x = r.ReadInt16(); t.y = r.ReadInt16(); if (hexen) t.z = r.ReadInt16();
                t.angle = r.ReadUInt16(); t.type = r.ReadUInt16(); t.flags = r.ReadUInt16();
                if (hexen) { t.action = r.ReadByte(); for (int i = 0; i < 5; i++) t.args[i] = r.ReadByte(); }
                map.things.Add(t);
            }
            // BSP builders append split points to VERTEXES for SEGS. They are
            // not editable vertices unless a linedef uses them. Match native
            // DoomMapSetIO / HexenMapSetIO.RemoveUnusedVertices on WAD import.
            // Validate first so corrupt endpoint references cannot be repaired
            // into a different (apparently valid) line by compaction.
            map.Validate(); RemoveUnusedBinaryVertices(map); return map;
        }
        static void RemoveUnusedBinaryVertices(MapDocument map)
        {
            var used = new bool[map.vertices.Count]; int count = 0;
            foreach (var line in map.lines)
            {
                if (!used[line.start]) { used[line.start] = true; count++; }
                if (!used[line.end]) { used[line.end] = true; count++; }
            }
            if (count == map.vertices.Count) return;
            var indices = new int[map.vertices.Count]; var vertices = new List<MapVertex>(count);
            for (int i = 0; i < map.vertices.Count; i++)
            {
                indices[i] = used[i] ? vertices.Count : -1;
                if (used[i]) vertices.Add(map.vertices[i]);
            }
            foreach (var line in map.lines) { line.start = indices[line.start]; line.end = indices[line.end]; }
            map.vertices = vertices;
        }
        static int SideIndex(ushort i) { return i == ushort.MaxValue ? -1 : i; }
        static List<WadLump> BuildMap(MapDocument map)
        {
            map.Validate(); ValidateName(map.name);
            if (map.format != MapFormat.Udmf && (map.vertices.Count > 65535 || map.sides.Count > 65535 || map.lines.Count > 65535 || map.sectors.Count > 65535))
                throw new InvalidOperationException("Doom binary map limit exceeded.");
            var replacement = new List<WadLump> { new WadLump(map.name.ToUpperInvariant(), new byte[0]) };
            
#if MOBILEUDB_UDMF // Deferred from the first release.
if (map.format == MapFormat.Udmf)
            {
                replacement.Add(new WadLump("TEXTMAP", new UTF8Encoding(false).GetBytes(UdmfText.Write(map))));
                AppendAuxiliary(replacement, map);
                replacement.Add(new WadLump("ENDMAP", new byte[0]));
            }
            else
#endif

            {
            bool hexen = map.format == MapFormat.Hexen;
            replacement.Add(Write("THINGS", w => { foreach (var t in map.things) {
                if (hexen) U16(w, t.tag); Coordinate(w, t.x); Coordinate(w, t.y); if (hexen) Coordinate(w, t.z);
                U16(w, checked((int)Math.Round(t.angle))); U16(w, t.type); U16(w, t.flags);
                if (hexen) { U8(w, t.action); for (int i = 0; i < 5; i++) U8(w, t.args[i]); }
            } }));
            replacement.Add(Write("LINEDEFS", w => { foreach (var l in map.lines) {
                U16(w, l.start); U16(w, l.end); U16(w, l.flags);
                if (hexen) { U8(w, l.action); for (int i = 0; i < 5; i++) U8(w, l.args[i]); }
                else { U16(w, l.action); U16(w, l.tag); }
                U16(w, l.front < 0 ? 65535 : l.front); U16(w, l.back < 0 ? 65535 : l.back);
            } }));
            replacement.Add(Write("SIDEDEFS", w => { foreach (var s in map.sides) { I16(w, s.offsetX); I16(w, s.offsetY); WriteName(w, s.upper); WriteName(w, s.lower); WriteName(w, s.middle); U16(w, s.sector); } }));
            replacement.Add(Write("VERTEXES", w => { foreach (var v in map.vertices) { Coordinate(w, v.x); Coordinate(w, v.y); } }));
            // Old nodes are invalid after editing. Export is for editors / engines that build nodes.
            replacement.Add(new WadLump("SEGS", new byte[0])); replacement.Add(new WadLump("SSECTORS", new byte[0])); replacement.Add(new WadLump("NODES", new byte[0]));
            replacement.Add(Write("SECTORS", w => { foreach (var s in map.sectors) { Coordinate(w, s.floor); Coordinate(w, s.ceiling); WriteName(w, s.floorTexture); WriteName(w, s.ceilingTexture); I16(w, s.light); U16(w, s.special); U16(w, s.tag); } }));
            replacement.Add(new WadLump("REJECT", new byte[0])); replacement.Add(new WadLump("BLOCKMAP", new byte[0]));
            // Hexen identification requires BEHAVIOR directly after BLOCKMAP.
            // SCRIPTS may already exist even when a new map has no compiled ACS.
            if (hexen)
            {
                var behavior = map.auxiliary.Find(l => l != null && string.Equals(l.name,"BEHAVIOR",StringComparison.OrdinalIgnoreCase));
                replacement.Add(new WadLump("BEHAVIOR",behavior == null ? new byte[] { 65,67,83,0,8,0,0,0,0,0,0,0 } : behavior.data));
            }
            AppendAuxiliary(replacement, map,hexen);
            }
            return replacement;
        }
        public byte[] Export(MapDocument map) { return Export(new[] { map }); }
        public byte[] Export(IEnumerable<MapDocument> maps)
        {
            var replacements = new Dictionary<string, List<WadLump>>(StringComparer.OrdinalIgnoreCase);
            foreach (var map in maps)
            {
                string source=MapNaming.Key(map);
                if (replacements.ContainsKey(source)) throw new InvalidDataException("Duplicate source map in WAD export: " + source);
                replacements.Add(source, BuildMap(map));
            }
            return ExportReplacements(replacements);
        }
        // Each replacement is a complete, already-built single-map WAD. Do not
        // import/export it through MapDocument: that would invalidate its nodes.
        public byte[] ExportBuiltMaps(IEnumerable<DoomWad> maps,IDictionary<string,string> sourceNames=null)
        {
            var replacements = new Dictionary<string,List<WadLump>>(StringComparer.OrdinalIgnoreCase);
            foreach (var map in maps)
            {
                var names = map.MapNames();
                if (names.Count != 1 || !map.IsMarker(0)) throw new InvalidDataException("Expected one built map per replacement.");
                string name = names[0]; ValidateName(name);
                string source=MapNaming.Source(name,sourceNames);
                if (replacements.ContainsKey(source)) throw new InvalidDataException("Duplicate built source map: " + source);
                replacements.Add(source,new List<WadLump>(map.lumps));
            }
            return ExportReplacements(replacements);
        }
        // Restore the input's auxiliary blocks verbatim around generated geometry.
        // This also preserves binary DIALOGUE/custom blocks omitted by a nodebuilder.
        public static byte[] PreserveBuiltAuxiliary(MapDocument map,DoomWad built)
        {
            if (built.MapNames().Count != 1 || !built.IsMarker(0) || !string.Equals(built.lumps[0].name,map.name,StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Nodebuilder returned the wrong map.");
            var canonical = BuildMap(map); var result = new List<WadLump>();
            var generated = new Dictionary<string,WadLump>(StringComparer.OrdinalIgnoreCase);
            foreach (var lump in built.lumps) if (GeometryLumps.Contains(lump.name) && lump.name != "ENDMAP") generated[lump.name] = lump;
            result.Add(canonical[0]);
            foreach (var lump in canonical.GetRange(1,canonical.Count - 1))
            {
                if (lump.name == "ENDMAP") continue;
                WadLump replacement;
                result.Add(GeometryLumps.Contains(lump.name) && generated.TryGetValue(lump.name,out replacement) ? replacement : lump);
            }
            // UDMF has no node placeholders in the canonical editor export.
            if (map.format == MapFormat.Udmf)
            {
                WadLump nodes; if (!generated.TryGetValue("ZNODES",out nodes)) throw new InvalidDataException("Missing ZNODES.");
                result.Add(nodes); result.Add(new WadLump("ENDMAP",new byte[0]));
            }
            return Serialize(result);
        }
        byte[] ExportReplacements(Dictionary<string,List<WadLump>> replacements)
        {
            var names=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);foreach(var pair in replacements)names.Add(pair.Key,pair.Value[0].name);ValidateMapNames(names);
            var output = new List<WadLump>(); var replaced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < lumps.Count; i++)
            {
                List<WadLump> replacement;
                if (IsMarker(i) && replacements.TryGetValue(lumps[i].name, out replacement))
                {
                    if (!replaced.Add(lumps[i].name)) throw new InvalidDataException("Duplicate map marker: " + lumps[i].name);
                    output.AddRange(replacement); i = MapEnd(i) - 1;
                }
                else if (lumps[i].name.StartsWith("GL_", StringComparison.OrdinalIgnoreCase) && replacements.ContainsKey(lumps[i].name.Substring(3)))
                {
                    // External GL node groups become invalid after geometry changes.
                    while (i + 1 < lumps.Count && GlLumps.Contains(lumps[i + 1].name)) i++;
                }
                else output.Add(lumps[i]);
            }
            foreach (var replacement in replacements) if (!replaced.Contains(replacement.Key)) output.AddRange(replacement.Value);
            return Serialize(output);
        }
        // Lightweight namespace validation, shared by rename UI and exporters.
        // source -> current names. Reject ambiguous output instead of overwriting
        // unrelated maps/data; the desktop editor offers an unsafe collision warning.
        public void ValidateMapNames(IDictionary<string,string> names)
        {
            var targets=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var renamed=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var pair in names){MapNaming.RequireName(pair.Key);MapNaming.RequireName(pair.Value);if(!targets.Add(pair.Value))throw new InvalidDataException("Имя карты уже используется: "+pair.Value);if(!string.Equals(pair.Key,pair.Value,StringComparison.OrdinalIgnoreCase))renamed.Add(pair.Value);}
            var output=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for(int i=0;i<lumps.Count;i++)
            {
                if(IsMarker(i))
                {string name;string source=lumps[i].name;seen.Add(source);if(!names.TryGetValue(source,out name))name=source;if(!output.Add(name))throw new InvalidDataException("Имя карты уже используется: "+name);}
                else if(renamed.Contains(lumps[i].name))throw new InvalidDataException("Имя занято данными WAD: "+lumps[i].name);
            }
            foreach(var pair in names)if(!seen.Contains(pair.Key)&&!output.Add(pair.Value))throw new InvalidDataException("Имя карты уже используется: "+pair.Value);
        }
        static void AppendAuxiliary(List<WadLump> replacement, MapDocument map,bool skipBehavior = false)
        {
            foreach (var l in map.auxiliary)
            {
                if (l == null || l.data == null) throw new InvalidDataException("Invalid auxiliary map lump.");
                if (!GeometryLumps.Contains(l.name) && !(skipBehavior && string.Equals(l.name,"BEHAVIOR",StringComparison.OrdinalIgnoreCase))) replacement.Add(new WadLump(l.name, l.data));
            }
        }
        static WadLump Write(string name, Action<BinaryWriter> action)
        {
            using (var stream = new MemoryStream()) { using (var w = new BinaryWriter(stream, Encoding.ASCII, true)) action(w); return new WadLump(name, stream.ToArray()); }
        }
        static byte[] Serialize(List<WadLump> lumps)
        {
            long size = 12L + lumps.Count * 16L;
            foreach (var lump in lumps) { size += lump.data.Length; if (size > MaxFile) throw new InvalidDataException("WAD export exceeds 256 MB."); }
            using (var stream = new MemoryStream())
            using (var w = new BinaryWriter(stream))
            {
                w.Write(Encoding.ASCII.GetBytes("PWAD")); w.Write(lumps.Count); w.Write(0);
                var offsets = new List<int>();
                foreach (var l in lumps) { offsets.Add(checked((int)stream.Position)); w.Write(l.data); }
                int directory = checked((int)stream.Position);
                for (int i = 0; i < lumps.Count; i++) { w.Write(offsets[i]); w.Write(lumps[i].data.Length); WriteName(w, lumps[i].name); }
                stream.Position = 8; w.Write(directory); return stream.ToArray();
            }
        }
        static string ReadName(BinaryReader r)
        {
            byte[] bytes = r.ReadBytes(8); int length = 0;
            while (length < bytes.Length && bytes[length] != 0) length++;
            return Encoding.ASCII.GetString(bytes, 0, length).Trim().ToUpperInvariant();
        }
        static void ValidateName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 8) throw new InvalidOperationException("WAD names must contain 1 to 8 ASCII characters.");
            foreach (char c in name) if (c < 33 || c > 126) throw new InvalidOperationException("WAD names must be ASCII.");
        }
        static void WriteName(BinaryWriter w, string name)
        {
            if (name == "") { w.Write(new byte[8]); return; }
            ValidateName(name); byte[] bytes = new byte[8]; Encoding.ASCII.GetBytes(name.ToUpperInvariant()).CopyTo(bytes, 0); w.Write(bytes);
        }
        static void Coordinate(BinaryWriter w, float n)
        {
            if (float.IsNaN(n) || float.IsInfinity(n) || n < short.MinValue || n > short.MaxValue) throw new InvalidOperationException("Doom coordinate out of range.");
            I16(w, checked((int)Math.Round(n, MidpointRounding.AwayFromZero)));
        }
        static void I16(BinaryWriter w, int n) { w.Write(checked((short)n)); }
        static void U16(BinaryWriter w, int n) { w.Write(checked((ushort)n)); }
        static void U8(BinaryWriter w, int n) { w.Write(checked((byte)n)); }
    }
}
