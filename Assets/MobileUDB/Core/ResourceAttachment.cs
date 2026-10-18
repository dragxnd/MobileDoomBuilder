// GPL-3.0. Prepare an attachment independently from mutable editor/UI state.
using System;
using System.Collections.Generic;
using System.IO;

namespace MobileUDB.Core
{
    public sealed class PreparedResourceAttachment
    {
        public ResourceArchive source;
        public DoomResources catalog;
        public string name,kind,data,error;
    }
    public static class ResourceAttachment
    {
        public static PreparedResourceAttachment Prepare(byte[] bytes,string filename,IEnumerable<ResourceArchive> existing,DoomWad map,long availableBytes,Func<bool> cancelled=null,Action<string> progress=null)
        {
            Action check = () => { if (cancelled != null && cancelled()) throw new OperationCanceledException(); };
            check(); if (progress != null) progress("Чтение архива…");
            var candidate = ResourceArchive.ReadAttachment(bytes,filename,null,availableBytes);
            if (Math.Max(bytes.LongLength,candidate.ContentBytes) > availableBytes) throw new InvalidDataException("Ресурсы превышают предел памяти мобильного проекта.");
            return PrepareCandidate(candidate,filename,existing,map,()=>Convert.ToBase64String(bytes),check,progress);
        }
        public static PreparedResourceAttachment PrepareFile(string path,string filename,IEnumerable<ResourceArchive> existing,DoomWad map,long availableBytes,Func<bool> cancelled=null,Action<string> progress=null)
        {
            Action check=()=>{if(cancelled!=null&&cancelled())throw new OperationCanceledException();};check();
            // Hold the same read-only file handle through catalog and Base64
            // preparation, so descriptors cannot diverge from parsed resources.
            using(var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,65536))
            {
                if(progress!=null)progress("Чтение архива…");
                var candidate=ResourceArchive.ReadAttachmentStream(input,filename,null,availableBytes,cancelled);
                if(Math.Max(input.Length,candidate.ContentBytes)>availableBytes)throw new InvalidDataException("Ресурсы превышают предел памяти мобильного проекта.");
                return PrepareCandidate(candidate,filename,existing,map,()=>StreamBase64.Read(input,cancelled),check,progress);
            }
        }
        static PreparedResourceAttachment PrepareCandidate(ResourceArchive candidate,string filename,IEnumerable<ResourceArchive> existing,DoomWad map,Func<string> portableData,Action check,Action<string> progress)
        {
            check(); var sources = new List<ResourceArchive>(existing); sources.Add(candidate);
            if (progress != null) progress("Каталог текстур и спрайтов…");
            // Explicit attachments must be valid independently of a map's tables.
            var external = DoomResources.BuildArchives(sources); check();
            DoomResources catalog = external; string error = null;
            if (map != null)
            {
                sources.Add(ResourceArchive.FromWad(map));
                try { catalog = DoomResources.BuildArchives(sources); }
                catch (Exception failure) { error = "Ресурсы карты не загружены: " + failure.Message; catalog = external; }
            }
            check(); if (progress != null) progress("Подготовка ресурса для проекта…");
            string data = portableData(); check();
            string name = string.IsNullOrEmpty(filename) ? "Ресурсы" : Path.GetFileName(filename.Replace('\\','/'));
            if (name == "Ресурсы" || name == "MobileUDB-import") name = "Ресурсы " + (candidate.isDehacked ? "DEHACKED" : candidate.isPk3 ? "PK3" : "WAD");
            return new PreparedResourceAttachment { source = candidate,catalog = catalog,data = data,name = name,
                kind = candidate.isDehacked ? "dehacked" : "archive",error = error };
        }
    }
}
