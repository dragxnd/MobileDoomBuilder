// GPL-3.0. Drawing overrides follow UDB Tools.ApplyOverridesToSidedef.
using System;
using System.Globalization;

namespace MobileUDB.Core
{
    [Serializable] public sealed class DrawTextureOptions
    {
        // Empty means inherit. "-" is an explicit missing wall texture.
        public string upper, middle, lower, floor, ceiling;
        public bool alignX;
        public bool overrideFloorHeight, overrideCeilingHeight, overrideLight;
        public float floorHeight, ceilingHeight = 128;
        public int light = 192;
        public DrawTextureOptions Validated(MapFormat format)
        {
            if (overrideFloorHeight) ValidateHeight(floorHeight,format);
            if (overrideCeilingHeight) ValidateHeight(ceilingHeight,format);
            if (overrideFloorHeight && overrideCeilingHeight && floorHeight > ceilingHeight) throw new InvalidOperationException("Рисование: пол выше потолка.");
            if (overrideLight && (light < 0 || light > 255)) throw new InvalidOperationException("Освещение рисования: от 0 до 255.");
            return new DrawTextureOptions { upper = Name(upper,format), middle = Name(middle,format), lower = Name(lower,format),
                floor = Name(floor,format), ceiling = Name(ceiling,format), alignX = alignX,
                overrideFloorHeight = overrideFloorHeight,overrideCeilingHeight = overrideCeilingHeight,overrideLight = overrideLight,
                floorHeight = floorHeight,ceilingHeight = ceilingHeight,light = light };
        }
        static void ValidateHeight(float value,MapFormat format)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < -32768 || value > 32767) throw new InvalidOperationException("Высоты рисования: от -32768 до 32767.");
            if (format != MapFormat.Udmf && value != Math.Round(value)) throw new InvalidOperationException("Высоты Doom / Hexen должны быть целыми числами.");
        }
        public DrawTextureOptions FromSector(MapSector sector,MapFormat format)
        {
            if (sector == null) throw new InvalidOperationException("Выберите сектор для образца.");
            var copy = Validated(MapFormat.Udmf);
            copy.floor = sector.floorTexture; copy.ceiling = sector.ceilingTexture;
            copy.floorHeight = sector.floor; copy.ceilingHeight = sector.ceiling; copy.light = sector.light;
            copy.overrideFloorHeight = copy.overrideCeilingHeight = copy.overrideLight = true;
            return copy.Validated(format);
        }
        public DrawTextureOptions FromSide(MapSide side,MapFormat format)
        {
            if (side == null) throw new InvalidOperationException("Выберите сторону стены для образца.");
            var copy = Validated(MapFormat.Udmf);
            // A missing texture is an explicit sample, not inherited artwork.
            copy.upper = string.IsNullOrEmpty(side.upper) ? "-" : side.upper;
            copy.middle = string.IsNullOrEmpty(side.middle) ? "-" : side.middle;
            copy.lower = string.IsNullOrEmpty(side.lower) ? "-" : side.lower;
            return copy.Validated(format);
        }
        static void SetHeight(MapSector sector,bool ceiling,float value)
        {
            float previous = ceiling ? sector.ceiling : sector.floor;
            string prefix = ceiling ? "ceilingplane_" : "floorplane_";
            float c = VisualGeometry.Field(sector,prefix + "c",0);
            if (Math.Abs(c) > .000001f)
            {
                double d = VisualGeometry.Field(sector,prefix + "d",0) - (value - (double)previous) * c;
                if (double.IsNaN(d) || double.IsInfinity(d) || Math.Abs(d) > float.MaxValue) throw new InvalidOperationException("Наклонная плоскость выходит за допустимый диапазон.");
                UdmfText.SetRaw(sector.fields,prefix + "d",((float)d).ToString("R",CultureInfo.InvariantCulture));
            }
            if (ceiling) sector.ceiling = value; else sector.floor = value;
        }
        static string Name(string value,MapFormat format)
        {
            if (string.IsNullOrEmpty(value)) return null;
            value = value.Trim(); int limit = format == MapFormat.Udmf ? 255 : 8;
            if (value.Length == 0 || value.Length > limit) throw new InvalidOperationException("Текстуры рисования: имя должно содержать 1–" + limit + " символов. Измените настройки рисования.");
            foreach (char c in value) if (char.IsControl(c) || (format != MapFormat.Udmf && (c < 33 || c > 126)))
                throw new InvalidOperationException("Текстуры рисования: недопустимое имя для формата карты.");
            return format == MapFormat.Udmf ? value : value.ToUpperInvariant();
        }
        internal void Apply(MapStitchResult result)
        {
            var map = result.map;
            foreach (int index in result.sectors)
            {
                var sector = map.sectors[index];
                if (floor != null) sector.floorTexture = floor;
                if (ceiling != null) sector.ceilingTexture = ceiling;
                // Set both before checking, so raising the whole room is atomic.
                if (overrideFloorHeight) SetHeight(sector,false,floorHeight);
                if (overrideCeilingHeight) SetHeight(sector,true,ceilingHeight);
                if (overrideLight) sector.light = light;
            }
            if (overrideFloorHeight || overrideCeilingHeight)
                foreach (var line in map.lines) foreach (int side in new[] { line.front,line.back })
                {
                    if (side < 0 || !result.sectors.Contains(map.sides[side].sector)) continue;
                    var sector = map.sectors[map.sides[side].sector];
                    foreach (int vertex in new[] { line.start,line.end })
                    {
                        var point = map.vertices[vertex].Position;
                        float low = VisualGeometry.Height(sector,false,point), high = VisualGeometry.Height(sector,true,point);
                        if (float.IsNaN(low) || float.IsInfinity(low) || float.IsNaN(high) || float.IsInfinity(high) || low > high)
                            throw new InvalidOperationException("Новые высоты пересекают пол и потолок. Измените настройки рисования.");
                    }
                }
            if (upper == null && middle == null && lower == null) return;
            // As in UDB, prefer the inner sides of newly created regions. A chain
            // without a new region affects its drawn lines only. Neighbour sides
            // and unrelated walls retain their artwork and UDMF fields.
            for (int i = 0; i < map.lines.Count; i++)
            {
                var line = map.lines[i];
                bool front = line.front >= 0 && (result.sectors.Count > 0 ? result.sectors.Contains(map.sides[line.front].sector) : result.lines.Contains(i));
                bool back = line.back >= 0 && (result.sectors.Count > 0 ? result.sectors.Contains(map.sides[line.back].sector) : result.lines.Contains(i));
                if (!front && !back) continue;
                if (front) ApplySide(map,line,line.front,line.back);
                if (back) ApplySide(map,line,line.back,line.front);
            }
        }
        void ApplySide(MapDocument map,MapLine line,int index,int opposite)
        {
            var side = map.sides[index];
            // One-sided middles are required even in a currently closed door.
            // Do not introduce a fence across a two-sided opening.
            if (middle != null && Required(map,line,index,opposite,SurfacePart.Middle)) side.middle = middle;
            if (upper != null && Required(map,line,index,opposite,SurfacePart.Upper)) side.upper = upper;
            if (lower != null && Required(map,line,index,opposite,SurfacePart.Lower)) side.lower = lower;
        }
        internal static bool Required(MapDocument map,MapLine line,int index,int opposite,SurfacePart part)
        {
            if (part == SurfacePart.Middle) return opposite < 0;
            if (opposite < 0) return false;
            var side = map.sides[index];
            var sector = map.sectors[side.sector]; var other = map.sectors[map.sides[opposite].sector];
            var a = map.vertices[line.start].Position; var b = map.vertices[line.end].Position;
            if (part == SurfacePart.Upper) return !GameSky.Is(map,other.ceilingTexture) && (VisualGeometry.Height(sector,true,a) > VisualGeometry.Height(other,true,a) || VisualGeometry.Height(sector,true,b) > VisualGeometry.Height(other,true,b));
            return !GameSky.Is(map,other.floorTexture) && (VisualGeometry.Height(sector,false,a) < VisualGeometry.Height(other,false,a) || VisualGeometry.Height(sector,false,b) < VisualGeometry.Height(other,false,b));
        }
    }
}
