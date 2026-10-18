using System;
using System.Collections.Generic;
using System.IO;

namespace MobileUDB.Core
{
    public static partial class Pk3Maps
    {
        // Keep each embedded WAD's raw lumps, including nodes and auxiliary data.
        // In maps/<name>.wad the filename identifies the map, not its inner marker.
        public static DoomWad Read(ResourceArchive archive,long maximumBytes)
        {
            if (archive == null || !archive.isPk3) throw new InvalidDataException("Ожидается PK3 / ZIP.");
            var result = new DoomWad(); var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long size = 12;
            foreach (var file in archive.files)
            {
                if (!file.path.StartsWith("maps/",StringComparison.OrdinalIgnoreCase) ||
                    file.path.IndexOf('/',5) >= 0 || !file.path.EndsWith(".wad",StringComparison.OrdinalIgnoreCase)) continue;
                string name = file.Stem.ToUpperInvariant();
                if (name.Length == 0 || name.Length > 8) throw new InvalidDataException("Имя карты для экспорта WAD должно содержать 1–8 ASCII-символов: " + file.path);
                foreach (char c in name) if (c < 33 || c > 126) throw new InvalidDataException("Имя карты должно быть ASCII: " + file.path);
                if (!names.Add(name)) throw new InvalidDataException("Повторяющееся имя карты в PK3: " + name);
                var wad = DoomWad.Read(file.data,maximumBytes - size); var inside = wad.MapNames();
                if (inside.Count != 1) throw new InvalidDataException("В maps/*.wad ожидается ровно одна карта: " + file.path);
                int marker = 0;
                while (marker + 1 < wad.lumps.Count && !(wad.lumps[marker].name == inside[0] && wad.lumps[marker].data.Length == 0 &&
                    (wad.lumps[marker + 1].name == "THINGS" || wad.lumps[marker + 1].name == "TEXTMAP"))) marker++;
                for (int i = 0; i < wad.lumps.Count; i++)
                {
                    var lump = wad.lumps[i]; string lumpName = i == marker ? name : lump.name;
                    // Conventional external GL groups must follow the renamed map too.
                    if (lumpName == "GL_" + inside[0]) lumpName = "GL_" + name;
                    if (lumpName.Length > 8) throw new InvalidDataException("GL-маркер не помещается в WAD: " + file.path);
                    size += 16L + lump.data.Length;
                    if (size > maximumBytes || result.lumps.Count >= 1000000) throw new InvalidDataException("Карты PK3 превышают предел памяти проекта.");
                    result.lumps.Add(new WadLump(lumpName,lump.data));
                }
            }
            if (names.Count == 0) throw new InvalidDataException("В PK3 нет карт maps/*.wad. Ресурсники подключайте через «Файл → Ресурсы».");
            return result;
        }
    }
}
