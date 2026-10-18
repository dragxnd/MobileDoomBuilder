// GPL-3.0. Texture search/replace follows UDB FindReplace texture masks.
using System;
using System.Collections.Generic;

namespace MobileUDB.Core
{
    public sealed class MapTextureMatch
    {
        public SurfaceRef surface;
        public bool front;
        public string texture;
        public ElementRef Element { get { return new ElementRef(surface.IsFlat ? ElementKind.Sector : ElementKind.Line,surface.IsFlat ? surface.sector : surface.line); } }
    }
    public static class MapTextureSearch
    {
        static string Pattern(string value)
        {
            if (value == null || (value = value.Trim()).Length == 0 || value.Length > 255) throw new InvalidOperationException("Маска текстуры: 1–255 символов.");
            foreach (char c in value) if (char.IsControl(c)) throw new InvalidOperationException("Недопустимый символ в маске текстуры.");
            return value.Replace('\\','/').ToUpperInvariant();
        }
        static bool Matches(string name,string pattern)
        {
            name = (string.IsNullOrEmpty(name) ? "-" : name).Replace('\\','/').ToUpperInvariant();
            int n = 0,p = 0,star = -1,retry = 0;
            while (n < name.Length)
            {
                if (p < pattern.Length && (pattern[p] == '?' || pattern[p] == name[n])) { p++; n++; }
                else if (p < pattern.Length && pattern[p] == '*') { star = p++; retry = n; }
                else if (star >= 0) { p = star + 1; n = ++retry; }
                else return false;
            }
            while (p < pattern.Length && pattern[p] == '*') p++;
            return p == pattern.Length;
        }
        public static List<MapTextureMatch> Find(MapDocument map,bool flats,string pattern,ISet<ElementRef> selection = null)
        {
            pattern = Pattern(pattern); var found = new List<MapTextureMatch>();
            if (flats)
            {
                for (int i = 0; i < map.sectors.Count; i++)
                {
                    if (selection != null && !selection.Contains(new ElementRef(ElementKind.Sector,i))) continue;
                    foreach (var part in new[] { SurfacePart.Floor,SurfacePart.Ceiling })
                    {
                        string name = part == SurfacePart.Floor ? map.sectors[i].floorTexture : map.sectors[i].ceilingTexture;
                        if (Matches(name,pattern)) found.Add(new MapTextureMatch { surface = new SurfaceRef { sector = i,line = -1,side = -1,part = part },texture = name });
                    }
                }
            }
            else for (int i = 0; i < map.lines.Count; i++)
            {
                var line = map.lines[i];
                foreach (bool front in new[] { true,false })
                {
                    int index = front ? line.front : line.back; if (index < 0) continue; var side = map.sides[index];
                    if (selection != null && !selection.Contains(new ElementRef(ElementKind.Line,i)) && !selection.Contains(new ElementRef(ElementKind.Sector,side.sector))) continue;
                    foreach (var part in new[] { SurfacePart.Upper,SurfacePart.Middle,SurfacePart.Lower })
                    {
                        string name = part == SurfacePart.Upper ? side.upper : part == SurfacePart.Lower ? side.lower : side.middle;
                        if (!Matches(name,pattern)) continue;
                        // Searching exactly '-' means missing required artwork,
                        // not every unused slot or every open two-sided middle.
                        if (pattern == "-" && !DrawTextureOptions.Required(map,line,index,front ? line.back : line.front,part)) continue;
                        found.Add(new MapTextureMatch { surface = new SurfaceRef { line = i,side = index,sector = side.sector,part = part },front = front,texture = name });
                    }
                }
            }
            return found;
        }
        public static int Replace(MapDocument map,bool flats,string pattern,string replacement,ISet<ElementRef> selection = null)
        {
            if (string.IsNullOrWhiteSpace(replacement) || replacement.IndexOfAny(new[] { '*','?' }) >= 0) throw new InvalidOperationException("Для замены нужно имя текстуры без * и ?.");
            replacement = new DrawTextureOptions { middle = replacement }.Validated(map.format).middle;
            var matches = Find(map,flats,pattern,selection); var changes = matches.FindAll(hit => !string.Equals(hit.texture,replacement,StringComparison.Ordinal));
            if (changes.Count == 0) return 0;
            var work = map.Clone(); var uses = new Dictionary<int,int>();
            if (!flats && selection != null) foreach (var line in work.lines) foreach (int side in new[] { line.front,line.back }) if (side >= 0) { int count; uses.TryGetValue(side,out count); uses[side] = count + 1; }
            foreach (var hit in changes)
            {
                var surface = hit.surface;
                if (!flats)
                {
                    var line = work.lines[surface.line]; surface.side = hit.front ? line.front : line.back;
                    if (selection != null && uses[surface.side] > 1)
                    {
                        int old = surface.side; var side = work.sides[old];
                        var copy = new MapSide { sector = side.sector,offsetX = side.offsetX,offsetY = side.offsetY,upper = side.upper,middle = side.middle,lower = side.lower }; side.CopyFieldsTo(copy);
                        surface.side = work.sides.Count; work.sides.Add(copy); uses[old]--; uses[surface.side] = 1;
                        if (hit.front) line.front = surface.side; else line.back = surface.side;
                    }
                }
                VisualGeometry.SetTexture(work,surface,replacement);
            }
            if (work.format != MapFormat.Udmf && work.sides.Count > 65535) throw new InvalidOperationException("Замена превышает предел сторон Doom / Hexen.");
            work.Validate(); map.lines = work.lines; map.sides = work.sides; map.sectors = work.sectors;
            return changes.Count;
        }
    }
}
