using System;
using System.IO;
namespace MobileUDB.Core
{
    public static class WadFilePair
    {
        static readonly object gate=new object();
        public static void Write(string path,byte[] wad,byte[] settings,Func<bool> cancelled=null)
        {
            if(!path.EndsWith(".wad",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Карту можно сохранить только в WAD.");
            if(settings==null)throw new ArgumentNullException("settings");
            lock(gate)
            {
                string sidecar=Path.ChangeExtension(path,".dbs");bool existed=File.Exists(sidecar);byte[] old=existed?DocumentFile.Read(sidecar,DbsSelectionGroups.MaximumBytes,null):null;
                if(!AtomicProjectFile.Write(sidecar,settings,cancelled))throw new OperationCanceledException();
                try{if(wad!=null&&!AtomicProjectFile.Write(path,wad,cancelled))throw new OperationCanceledException();}
                catch{if(existed)AtomicProjectFile.Write(sidecar,old);else File.Delete(sidecar);throw;}
            }
        }
    }
}
