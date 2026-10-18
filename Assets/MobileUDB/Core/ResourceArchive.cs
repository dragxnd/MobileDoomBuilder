// GPL-3.0. Resource containers follow UDB PK3StructuredReader namespaces.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace MobileUDB.Core
{
    public sealed class ResourceFile
    {
        public readonly string path;
        public readonly byte[] data;
        public ResourceFile(string path,byte[] data) { this.path = path; this.data = data; }
        public string Stem { get { int slash = path.LastIndexOf('/'),dot = path.LastIndexOf('.'); return path.Substring(slash + 1,(dot > slash ? dot : path.Length) - slash - 1); } }
    }
    public sealed class ResourceArchive
    {
        public readonly bool isPk3, isDehacked;
        public string SourceName { get; private set; }
        static ResourceArchive Named(ResourceArchive archive,string name)
        { archive.SourceName=string.IsNullOrEmpty(name)?null:Path.GetFileName(name.Replace('\\','/'));return archive; }
        public const long MaxDehackedBytes=8L*1024*1024;
        public readonly List<DoomWad> wads = new List<DoomWad>();
        public readonly List<ResourceFile> files = new List<ResourceFile>();
        public long ContentBytes { get; private set; }
        ResourceArchive(bool pk3,bool dehacked=false) { isPk3 = pk3;isDehacked=dehacked; }
        public static ResourceArchive ReadAttachment(byte[] bytes,string name,string kind=null,long maximumBytes=128L*1024*1024)
        {
            bool automatic=string.IsNullOrEmpty(kind)||kind=="auto";
            if(!automatic&&kind!="archive"&&kind!="dehacked")throw new InvalidDataException("Неизвестный тип ресурса проекта.");
            // Older projects may have misleading display extensions. Their actual
            // WAD/ZIP signatures still identify containers before filename hints.
            bool container=bytes!=null&&bytes.Length>=4&&((bytes[0]=='P'&&bytes[1]=='K')||((bytes[0]=='I'||bytes[0]=='P')&&bytes[1]=='W'&&bytes[2]=='A'&&bytes[3]=='D'));
            if(kind=="archive"||(automatic&&(container||!IsDehackedName(name))))return Named(Read(bytes,maximumBytes),name);
            if(bytes==null||bytes.Length==0||bytes.LongLength>Math.Min(MaxDehackedBytes,maximumBytes))throw new InvalidDataException("Патч DEHACKED / BEX: не более 8 МиБ в пределах бюджета ресурсов.");
            foreach(byte c in bytes)if(c<32&&c!=9&&c!=10&&c!=13)throw new InvalidDataException("Патч DEHACKED / BEX должен быть текстовым файлом.");
            var result=new ResourceArchive(false,true);name=string.IsNullOrEmpty(name)?"DEHACKED.deh":name.Replace('\\','/');
            int slash=name.LastIndexOf('/');if(slash>=0)name=name.Substring(slash+1);
            result.files.Add(new ResourceFile(name,bytes));result.ContentBytes=bytes.Length;return result;
        }
        public static bool IsDehackedName(string name)
        {return name!=null&&(name.EndsWith(".deh",StringComparison.OrdinalIgnoreCase)||name.EndsWith(".bex",StringComparison.OrdinalIgnoreCase));}
        public static ResourceArchive FromWad(DoomWad wad)
        {
            if (wad == null) throw new ArgumentNullException("wad"); var result = new ResourceArchive(false); result.wads.Add(wad);
            foreach (var lump in wad.lumps) result.ContentBytes += lump.data.Length; return result;
        }
        public static ResourceArchive Read(byte[] bytes,long maximumBytes = 128L * 1024 * 1024)
        {
            if (bytes == null || bytes.Length < 4 || bytes.LongLength > maximumBytes) throw new InvalidDataException("Архив ресурсов превышает допустимый размер.");
            using(var stream=new MemoryStream(bytes,false))return ReadStream(stream,maximumBytes);
        }
        public static ResourceArchive ReadAttachmentStream(Stream stream,string name,string kind=null,long maximumBytes=128L*1024*1024,Func<bool> cancelled=null)
        {
            bool automatic=string.IsNullOrEmpty(kind)||kind=="auto";
            if(!automatic&&kind!="archive"&&kind!="dehacked")throw new InvalidDataException("Неизвестный тип ресурса проекта.");
            if(stream==null||!stream.CanRead||!stream.CanSeek)throw new InvalidDataException("Требуется читаемый поток ресурсов.");
            if(cancelled!=null&&cancelled())throw new OperationCanceledException();
            stream.Position=0;var header=new byte[4];int read=0;while(read<header.Length){int count=stream.Read(header,read,header.Length-read);if(count==0)break;read+=count;}stream.Position=0;
            bool container=read==4&&((header[0]=='P'&&header[1]=='K')||((header[0]=='I'||header[0]=='P')&&header[1]=='W'&&header[2]=='A'&&header[3]=='D'));
            if(kind=="archive"||(automatic&&(container||!IsDehackedName(name))))return Named(ReadStream(stream,maximumBytes,cancelled),name);
            if(stream.Length==0||stream.Length>Math.Min(MaxDehackedBytes,maximumBytes))throw new InvalidDataException("Патч DEHACKED / BEX: не более 8 МиБ в пределах бюджета ресурсов.");
            var data=new byte[checked((int)stream.Length)];read=0;
            while(read<data.Length){if(cancelled!=null&&cancelled())throw new OperationCanceledException();int count=stream.Read(data,read,Math.Min(65536,data.Length-read));if(count==0)throw new InvalidDataException("Неполный файл ресурсов.");read+=count;}
            return ReadAttachment(data,name,"dehacked",maximumBytes);
        }
        public static ResourceArchive ReadStream(Stream stream,long maximumBytes=128L*1024*1024,Func<bool> cancelled=null)
        {
            if(stream==null||!stream.CanRead||!stream.CanSeek||stream.Length<4||stream.Length>maximumBytes)throw new InvalidDataException("Архив ресурсов превышает допустимый размер.");
            Action check=()=>{if(cancelled!=null&&cancelled())throw new OperationCanceledException();};check();stream.Position=0;
            var header=new byte[4];int headerRead=0;while(headerRead<4){int count=stream.Read(header,headerRead,4-headerRead);if(count==0)throw new InvalidDataException("Неполный архив ресурсов.");headerRead+=count;}stream.Position=0;
            if((header[0]=='I'||header[0]=='P')&&header[1]=='W'&&header[2]=='A'&&header[3]=='D')return FromWad(DoomWad.ReadStream(stream,maximumBytes,cancelled));
            if(header[0]!='P'||header[1]!='K')throw new InvalidDataException("Поддерживаются ресурсы WAD и PK3 / ZIP.");
            var result = new ResourceArchive(true);
            using (var zip = new ZipArchive(stream,ZipArchiveMode.Read,true))
            {
                if (zip.Entries.Count > 65535) throw new InvalidDataException("В PK3 слишком много файлов.");
                long unpacked = 0;
                foreach (var entry in zip.Entries)
                {
                    check();
                    string path = Normalize(entry.FullName);
                    if (path.EndsWith("/",StringComparison.Ordinal)) continue;
                    if (entry.Length < 0 || entry.Length > maximumBytes - unpacked) throw new InvalidDataException("Распакованные ресурсы превышают допустимый размер.");
                    byte[] data = new byte[checked((int)entry.Length)];
                    using (var input = entry.Open())
                    {
                        int read = 0;
                        while (read < data.Length) { check();int count = input.Read(data,read,Math.Min(65536,data.Length - read)); if (count == 0) throw new InvalidDataException("PK3 содержит неполный файл: " + path); read += count; }
                        if (input.ReadByte() != -1) throw new InvalidDataException("Размер файла PK3 не совпадает с каталогом.");
                    }
                    unpacked += data.Length;
                    if (path.IndexOf('/') < 0 && path.EndsWith(".wad",StringComparison.OrdinalIgnoreCase))
                    {
                        var wad = DoomWad.Read(data,maximumBytes - result.ContentBytes); result.wads.Add(wad);
                        long cost = 0; foreach (var lump in wad.lumps) cost += lump.data.Length; result.ContentBytes += Math.Max(data.Length,cost);
                    }
                    else { result.files.Add(new ResourceFile(path,data)); result.ContentBytes += data.Length; }
                    if (result.ContentBytes > maximumBytes) throw new InvalidDataException("Содержимое PK3 превышает допустимый размер.");
                }
            }
            return result;
        }
        internal static string Normalize(string path)
        {
            path = path.Replace('\\','/');
            if (path.Length == 0 || path.Length > 4096 || path[0] == '/' || path.IndexOf(':') >= 0 || path.IndexOf('\0') >= 0)
                throw new InvalidDataException("Недопустимый путь в PK3.");
            foreach (string part in path.Split('/')) if (part == ".." || part == ".") throw new InvalidDataException("Недопустимый путь в PK3.");
            return path;
        }
    }
}
