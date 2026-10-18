// GPL-3.0. Persistent per-document settings; exported folders contain only WAD.
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
namespace MobileUDB.Core
{
    public static class WadSettingsStore
    {
        static readonly object gate=new object();
        public static string SettingsPath(string privateRoot,string wadPath)
        {
            string identity=Path.GetFullPath(wadPath);
            if(Path.DirectorySeparatorChar=='\\')identity=identity.ToUpperInvariant();
            string digest;using(var hash=SHA256.Create())digest=BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes("path:"+identity))).Replace("-","").ToLowerInvariant();
            return Path.Combine(privateRoot,"WadSettings",digest+".dbs");
        }
        public static byte[] Read(string privateRoot,string wadPath,Func<bool> cancelled=null)
        {string path=SettingsPath(privateRoot,wadPath);return File.Exists(path)?DocumentFile.Read(path,DbsSelectionGroups.MaximumBytes,cancelled):null;}
        public static void WriteWad(string privateRoot,string wadPath,byte[] wad,byte[] settings,Func<bool> cancelled=null)
        {
            wadPath=Path.GetFullPath(wadPath);
            if(!wadPath.EndsWith(".wad",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Карту можно сохранить только в WAD.");
            if(settings==null)throw new ArgumentNullException("settings");
            if(settings.Length>DbsSelectionGroups.MaximumBytes)throw new InvalidDataException("Настройки WAD слишком большие.");
            lock(gate)
            {
                string path=SettingsPath(privateRoot,wadPath);byte[] old=Read(privateRoot,wadPath);bool stored=false;
                try
                {
                    if(!AtomicProjectFile.Write(path,settings,cancelled))throw new OperationCanceledException();stored=true;
                    if(cancelled!=null&&cancelled())throw new OperationCanceledException();
                    if(wad!=null)WriteOnlyWad(wadPath,wad,cancelled);
                }
                catch
                {
                    if(stored){if(old!=null)AtomicProjectFile.Write(path,old);else File.Delete(path);}throw;
                }
            }
        }
        static void WriteOnlyWad(string path,byte[] bytes,Func<bool> cancelled)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));string temporary=path+".tmp-"+Guid.NewGuid().ToString("N");
            try
            {
                using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(bytes,0,bytes.Length);stream.Flush(true);}
                if(cancelled!=null&&cancelled())throw new OperationCanceledException();
                if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);
            }
            finally{if(File.Exists(temporary))File.Delete(temporary);}
        }
    }
}
