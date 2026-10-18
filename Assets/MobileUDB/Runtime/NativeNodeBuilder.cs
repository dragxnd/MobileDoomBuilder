using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using MobileUDB.Core;

namespace MobileUDB
{
    // This backend builds a single validated export in private temporary files.
    // The editor document and resource archives are never passed to native code.
    public static class NativeNodeBuilder
    {
#if UNITY_IOS && !UNITY_EDITOR
        const string Library = "__Internal";
#else
        const string Library = "mobileudb_nodes";
#endif
        [DllImport(Library,CallingConvention = CallingConvention.Cdecl)]
        static extern int mobileudb_nodes_version();
        [DllImport(Library,CallingConvention = CallingConvention.Cdecl)]
        static extern int mobileudb_nodes_build(byte[] input,byte[] output,[Out] byte[] message,int capacity);
        public static bool Available
        {
            get
            {
                try { return mobileudb_nodes_version() == 1; }
                catch (DllNotFoundException) { return false; }
                catch (EntryPointNotFoundException) { return false; }
                catch (BadImageFormatException) { return false; }
            }
        }
        public static byte[] BuildMap(MapDocument document,string temporaryRoot,out string warning)
        {
            warning = null;
            if (!Available) throw new NotSupportedException("Построитель узлов недоступен на этой платформе.");
            var map = document.Clone(); map.Validate();
            if (map.format == MapFormat.Doom && map.auxiliary.Exists(l => l != null && string.Equals(l.name,"BEHAVIOR",StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("BEHAVIOR несовместим с классическим форматом Doom.");
            foreach (var vertex in map.vertices) CheckCoordinate(vertex.x,vertex.y);
            foreach (var thing in map.things) CheckCoordinate(thing.x,thing.y);
            if (map.lines.Count == 0 || map.sectors.Count == 0) throw new InvalidOperationException("Для построения узлов нужна карта со стенами и секторами.");
            foreach (var line in map.lines)
                if (line.front < 0 || (map.vertices[line.end].Position - map.vertices[line.start].Position).LengthSquared < .000001f)
                    throw new InvalidOperationException("Исправьте нулевые стены и стены без передней стороны перед построением узлов.");
#if MOBILEUDB_UDMF // Deferred from the first mobile release.
            // Upstream currently drops unknown/nested UDMF blocks. Reject rather
            // than silently producing an export with lost extension data.
            if (MapFormats.IsUniversal(map.format))
            {
                if (map.udmfBlocks.Count != 0) throw new NotSupportedException("Построение узлов для дополнительных блоков UDMF ещё не поддержано.");
                foreach (var item in map.vertices) CheckBlocks(item);
                foreach (var item in map.lines) CheckBlocks(item);
                foreach (var item in map.sides) CheckBlocks(item);
                foreach (var item in map.sectors) CheckBlocks(item);
                foreach (var item in map.things) CheckBlocks(item);
            }
#endif
            byte[] bytes = new DoomWad().Export(map);
            string root = Path.GetFullPath(temporaryRoot),folder = Path.Combine(root,"nodes-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                string input = Path.Combine(folder,"input.wad"),output = Path.Combine(folder,"output.wad");
                File.WriteAllBytes(input,bytes); var message = new byte[8192];
                int result = mobileudb_nodes_build(Utf8(input),Utf8(output),message,message.Length);
                string detail = Encoding.UTF8.GetString(message).TrimEnd('\0');
                if (result != 1) throw new InvalidOperationException("Не удалось построить узлы: " + detail);
                var size = new FileInfo(output).Length;
                if (size > 128L * 1024 * 1024) throw new InvalidOperationException("Результат построения превышает 128 МБ.");
                byte[] built = File.ReadAllBytes(output); var wad = DoomWad.Read(built);
#if MOBILEUDB_UDMF // Deferred from the first mobile release.
                if (MapFormats.IsUniversal(map.format))
                {
                    if (!wad.lumps.Exists(l => l.name == "ZNODES" && l.data.Length > 12)) throw new InvalidDataException("Построитель не создал ZNODES.");
                }
                else
#endif
                if (!wad.lumps.Exists(l => l.name == "SEGS" && l.data.Length >= 12) ||
                         !wad.lumps.Exists(l => l.name == "SSECTORS" && l.data.Length >= 4) ||
                         !wad.lumps.Exists(l => l.name == "BLOCKMAP" && l.data.Length >= 8))
                    throw new InvalidDataException("Построитель не создал полный набор узлов.");
                warning = detail; return DoomWad.PreserveBuiltAuxiliary(map,wad);
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder,true); }
        }
#if MOBILEUDB_UDMF // Deferred from the first mobile release.
        static void CheckBlocks(MapElementData item)
        { if (item.extraBlocks.Count != 0) throw new NotSupportedException("Построение узлов для вложенных блоков UDMF ещё не поддержано."); }
#endif
        static void CheckCoordinate(float x,float y)
        {
            if (x < -32768 || x >= 32768 || y < -32768 || y >= 32768)
                throw new NotSupportedException("Координаты вне диапазона построителя ZDBSP (−32768 ≤ x, y < 32768).");
        }
        static byte[] Utf8(string text) { return Encoding.UTF8.GetBytes(text + "\0"); }
    }
}
