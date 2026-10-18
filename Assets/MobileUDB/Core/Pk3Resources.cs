// GPL-3.0. Root WADs, loose namespace images, full paths and classic aliases.
using System;
using System.IO;

namespace MobileUDB.Core
{
    public sealed partial class DoomResources
    {
        void LoadPk3(ResourceArchive source,ref string[] patchNames)
        {
            var root = new DoomWad(); byte[] embeddedNames = null;
            foreach (var wad in source.wads) foreach (var lump in wad.lumps) if (lump.name == "PNAMES") embeddedNames = lump.data;
            foreach (var file in source.files)
            {
                string path = file.path.ToUpperInvariant(); lumps[path] = file.data;
                if (path.IndexOf('/') < 0)
                {
                    string name = file.Stem.ToUpperInvariant();
                    if(name.StartsWith("DEHACKED",StringComparison.Ordinal))dehackedPatches.Add(file);
                    if (name == "PLAYPAL" || name == "PNAMES" || name == "TEXTURE1" || name == "TEXTURE2") root.lumps.Add(new WadLump(name,file.data));
                    continue;
                }
            }
            // UDB prefers PNAMES from the last root WAD over a loose root file.
            if (embeddedNames != null) root.lumps.Add(new WadLump("PNAMES",embeddedNames));
            foreach (var file in source.files)
            {
                string path = file.path.ToUpperInvariant(); int slash = path.IndexOf('/'); if (slash < 0) continue;
                string folder = path.Substring(0,slash),stem = file.Stem.ToUpperInvariant(); if (stem.Length == 0) continue;
                string shortName = stem.Length > 8 ? stem.Substring(0,8) : stem;
                ResourceKind kind;
                if (folder == "TEXTURES") kind = ResourceKind.Wall;
                else if (folder == "FLATS") kind = ResourceKind.Flat;
                else if (folder == "SPRITES") kind = ResourceKind.Sprite;
                else continue;
                try { AddImage(path,file.data,kind,shortName); }
                catch (InvalidDataException error) { if (warnings.Count < 30) warnings.Add(file.path + ": " + error.Message); }
            }
            // ZDoom's lookup fallback ends in graphics; patches have priority.
            foreach (string folder in new[] { "GRAPHICS","SPRITES","FLATS","TEXTURES","PATCHES" }) foreach (var file in source.files)
            {
                if (!file.path.StartsWith(folder + "/",StringComparison.OrdinalIgnoreCase)) continue;
                string name = file.Stem.ToUpperInvariant(); if (name.Length > 8) name = name.Substring(0,8);
                if (name.Length > 0) lumps[name] = file.data;
            }
            // Loose TEXTURE1/2 definitions take priority over loose images.
            LoadWad(root,ref patchNames);
            foreach (var file in source.files) if (file.path.IndexOf('/') < 0 && file.Stem.StartsWith("TEXTURES",StringComparison.OrdinalIgnoreCase))
                LoadTextureDefinitions(file.data,file.path,path =>
                { for (int i = source.files.Count - 1; i >= 0; i--) if (string.Equals(source.files[i].path,path,StringComparison.OrdinalIgnoreCase)) return source.files[i].data; return null; });
            // Root WAD images/patches override loose short names in UDB. Full PK3
            // image paths remain addressable even when their short alias is replaced.
            foreach (var wad in source.wads) LoadWad(wad,ref patchNames);
        }
    }
}
