using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace MobileUDB.Core
{
    public static partial class Pk3Maps
    {
        // Unchanged entries retain their decompressed bytes, path, order, timestamp
        // and attributes. ZIP compression and container metadata may be regenerated.
        public static byte[] Export(byte[] original,IEnumerable<DoomWad> maps,
            long maximumBytes = 128L * 1024 * 1024,Func<bool> cancelled = null,IDictionary<string,string> sourceNames=null)
        {
            if (original == null || original.LongLength > maximumBytes) throw new InvalidDataException("PK3 превышает предел памяти.");
            var replacements = new Dictionary<string,DoomWad>(StringComparer.OrdinalIgnoreCase);
            foreach (var map in maps)
            {
                var names = map.MapNames();
                if (names.Count != 1 || map.lumps.Count < 2 || map.lumps[0].name != names[0]) throw new InvalidDataException("Ожидается одна подготовленная карта.");
                MapNaming.Normalize(names[0],true);string key=MapNaming.Source(names[0],sourceNames);
                if (replacements.ContainsKey(key)) throw new InvalidDataException("Повторяющаяся исходная карта для экспорта: " + key);
                replacements.Add(key,map);
            }
            using (var input = new MemoryStream(original,false))
            using (var source = new ZipArchive(input,ZipArchiveMode.Read,false))
            using (var output = new BoundedZipStream(maximumBytes))
            {
                if (source.Entries.Count > 65535) throw new InvalidDataException("В PK3 слишком много файлов.");
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var replaced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                long inputSize = 0,outputSize = 0; byte[] buffer = new byte[65536];
                using (var target = new ZipArchive(output,ZipArchiveMode.Create,true))
                {
                    foreach (var entry in source.Entries)
                    {
                        CancelIfRequested(cancelled);
                        string path = ResourceArchive.Normalize(entry.FullName);
                        inputSize += entry.Length;
                        if (entry.Length < 0 || inputSize > maximumBytes) throw new InvalidDataException("Распакованный PK3 превышает предел памяти.");
                        byte[] updated = null; DoomWad replacement;string outputPath=entry.FullName;
                        bool isMap = path.StartsWith("maps/",StringComparison.OrdinalIgnoreCase) && path.IndexOf('/',5) < 0 && path.EndsWith(".wad",StringComparison.OrdinalIgnoreCase);
                        if (isMap)
                        {
                            string name = new ResourceFile(path,null).Stem;
                            if (!seen.Add(name)) throw new InvalidDataException("Повторяющаяся карта в PK3: " + name);
                            if (replacements.TryGetValue(name,out replacement))
                            {
                                var data = new byte[checked((int)entry.Length)];
                                using (var read = entry.Open()) using (var memory = new MemoryStream(data,true)) CopyEntry(read,memory,entry.Length,buffer,cancelled);
                                var embedded = DoomWad.Read(data,maximumBytes); var names = embedded.MapNames();
                                if (names.Count != 1) throw new InvalidDataException("Ожидается одна карта: " + path);
                                // Keep the original inner marker, which may differ from the PK3 filename.
                                var renamed = new DoomWad();
                                string current=replacement.lumps[0].name;
                                for (int i = 0; i < replacement.lumps.Count; i++)
                                {
                                    var lump = replacement.lumps[i];string lumpName=i==0?names[0]:lump.name;
                                    if(string.Equals(lumpName,"GL_"+current,StringComparison.OrdinalIgnoreCase))lumpName="GL_"+names[0];
                                    renamed.lumps.Add(new WadLump(lumpName,lump.data));
                                }
                                updated = embedded.ExportBuiltMaps(new[] { renamed }); replaced.Add(name);
                                if(!string.Equals(current,name,StringComparison.OrdinalIgnoreCase))
                                {int slash=Math.Max(entry.FullName.LastIndexOf('/'),entry.FullName.LastIndexOf('\\'));outputPath=entry.FullName.Substring(0,slash+1)+current+entry.FullName.Substring(entry.FullName.Length-4);}
                            }
                        }
                        outputSize += updated == null ? entry.Length : updated.LongLength;
                        if (outputSize > maximumBytes) throw new InvalidDataException("Результат PK3 превышает предел памяти.");
                        if(!paths.Add(ResourceArchive.Normalize(outputPath)))throw new InvalidDataException("Повторяющийся путь в результате PK3: "+outputPath);
                        var written = target.CreateEntry(outputPath,CompressionLevel.Optimal);
                        written.LastWriteTime = entry.LastWriteTime; written.ExternalAttributes = entry.ExternalAttributes;
                        using (var write = written.Open())
                        {
                            if (updated != null) using (var read = new MemoryStream(updated,false)) CopyEntry(read,write,updated.Length,buffer,cancelled);
                            else using (var read = entry.Open()) CopyEntry(read,write,entry.Length,buffer,cancelled);
                        }
                    }
                    foreach (string name in replacements.Keys) if (!replaced.Contains(name)) throw new InvalidDataException("В исходном PK3 нет карты: " + name);
                }
                CancelIfRequested(cancelled); return output.ToArray();
            }
        }
        static void CancelIfRequested(Func<bool> cancelled)
        { if (cancelled != null && cancelled()) throw new OperationCanceledException(); }
        static void CopyEntry(Stream read,Stream write,long expected,byte[] buffer,Func<bool> cancelled)
        {
            long copied = 0;
            while (true)
            {
                CancelIfRequested(cancelled); int count = read.Read(buffer,0,buffer.Length); if (count == 0) break;
                copied += count; if (copied > expected) throw new InvalidDataException("Размер файла PK3 не совпадает с каталогом.");
                write.Write(buffer,0,count);
            }
            if (copied != expected) throw new InvalidDataException("Неполный файл PK3.");
        }
        sealed class BoundedZipStream : Stream
        {
            readonly long limit;
            readonly MemoryStream stream = new MemoryStream();
            public BoundedZipStream(long limit) { this.limit = limit; }
            public byte[] ToArray() { return stream.ToArray(); }
            public override bool CanRead { get { return true; } }
            public override bool CanSeek { get { return true; } }
            public override bool CanWrite { get { return true; } }
            public override long Length { get { return stream.Length; } }
            public override long Position { get { return stream.Position; } set { stream.Position = value; } }
            public override void Flush() { stream.Flush(); }
            public override int Read(byte[] bytes,int offset,int count) { return stream.Read(bytes,offset,count); }
            public override long Seek(long offset,SeekOrigin origin) { return stream.Seek(offset,origin); }
            void Check(long end) { if (end > limit) throw new InvalidDataException("Сжатый PK3 превышает предел памяти."); }
            public override void Write(byte[] bytes,int offset,int count) { Check(Position + count); stream.Write(bytes,offset,count); }
            public override void WriteByte(byte value) { Check(Position + 1); stream.WriteByte(value); }
            public override void SetLength(long value) { Check(value); stream.SetLength(value); }
            protected override void Dispose(bool disposing) { if (disposing) stream.Dispose(); base.Dispose(disposing); }
        }
    }
}
