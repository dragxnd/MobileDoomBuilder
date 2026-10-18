// GPL-3.0. Publish complete project bytes; never expose a partially written save.
using System;
using System.IO;

namespace MobileUDB.Core
{
    public static class AtomicProjectFile
    {
        static readonly object commitGate = new object();
        public static bool Write(string path,byte[] bytes,Func<bool> cancelled=null)
        {
            if (bytes == null) throw new ArgumentNullException("bytes");
            path = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                if (cancelled != null && cancelled()) return false;
                using (var stream = new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                { stream.Write(bytes,0,bytes.Length); stream.Flush(true); }
                lock (commitGate)
                {
                    if (cancelled != null && cancelled()) return false;
                    if (File.Exists(path)) File.Replace(temp,path,path + ".bak"); else File.Move(temp,path);
                }
                return true;
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
    // Unity's thread owns this state; completion of an older snapshot keeps newer edits dirty.
    public sealed class SaveRevision
    {
        public long Current { get; private set; }
        public long Saved { get; private set; }
        public bool Dirty { get { return Current != Saved; } }
        public void Changed() { Current++; }
        public void MarkSaved(long revision)
        { if (revision > Saved && revision <= Current) Saved = revision; }
    }
}
