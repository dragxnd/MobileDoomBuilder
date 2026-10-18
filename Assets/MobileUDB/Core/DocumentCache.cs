// GPL-3.0. Owned temporary export files; no Unity or native UI dependency.
using System;
using System.IO;

namespace MobileUDB.Core
{
    public static class DocumentCache
    {
        const string ExportPrefix = "MobileUDB-export-";
        const string ImportPrefix = "MobileUDB-import-";
        static StringComparison PathComparison { get { return Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal; } }
        public static string ExportName(string filename)
        {
            if (string.IsNullOrEmpty(filename)) throw new InvalidDataException("Укажите имя экспортируемого файла.");
            string name = Path.GetFileName(filename.Replace('\\','/'));
            if (string.IsNullOrEmpty(name) || name == "." || name == ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new InvalidDataException("Недопустимое имя экспортируемого файла.");
            foreach (char c in name) if (char.IsControl(c)) throw new InvalidDataException("Недопустимое имя экспортируемого файла.");
            return name;
        }
        public static string WriteExportCopy(string cacheDirectory,string filename,byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException("bytes");
            string name = ExportName(filename), root = Path.GetFullPath(cacheDirectory);
            string folder = Path.Combine(root,ExportPrefix + Guid.NewGuid().ToString("N")), path = Path.Combine(folder,name);
            try
            {
                Directory.CreateDirectory(folder);
                using (var stream = new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None)) stream.Write(bytes,0,bytes.Length);
                return path;
            }
            catch { DeleteExportCopy(root,path); throw; }
        }
        public static void DeleteExportCopy(string cacheDirectory,string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            string root = Path.GetFullPath(cacheDirectory).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
            string full = Path.GetFullPath(path), folder = Path.GetDirectoryName(full);
            if (string.IsNullOrEmpty(folder)) return;
            if (!string.Equals(Path.GetDirectoryName(folder),root,PathComparison)) return;
            string name = Path.GetFileName(folder);
            if (!name.StartsWith(ExportPrefix,StringComparison.Ordinal) || name.Length != ExportPrefix.Length + 32) return;
            Guid id; if (!Guid.TryParseExact(name.Substring(ExportPrefix.Length),"N",out id)) return;
            if (Directory.Exists(folder) && (File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) return;
            File.Delete(full);
            // Never recursively delete a directory containing someone else's files.
            if (Directory.Exists(folder) && Directory.GetFileSystemEntries(folder).Length == 0) Directory.Delete(folder,false);
        }
        public static bool IsImportCopy(string cacheDirectory,string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string root = Path.GetFullPath(cacheDirectory).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
            string full = Path.GetFullPath(path), name = Path.GetFileName(full);
            if (!string.Equals(Path.GetDirectoryName(full),root,PathComparison)) return false;
            if (!name.StartsWith(ImportPrefix,StringComparison.Ordinal) || !name.EndsWith(".tmp",StringComparison.Ordinal)) return false;
            int length = name.Length - ImportPrefix.Length - 4; if (length < 1) return false;
            for (int i = ImportPrefix.Length; i < name.Length - 4; i++)
                if (!(name[i] >= '0' && name[i] <= '9') && !(name[i] >= 'a' && name[i] <= 'z') && !(name[i] >= 'A' && name[i] <= 'Z') && name[i] != '-') return false;
            if (Directory.Exists(root) && (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) return false;
            return !File.Exists(full) || (File.GetAttributes(full) & FileAttributes.ReparsePoint) == 0;
        }
        public static void DeleteImportCopy(string cacheDirectory,string path)
        { if (IsImportCopy(cacheDirectory,path)) File.Delete(Path.GetFullPath(path)); }
    }
}
