using System;

namespace MobileUDB.Core
{
    // UDB keeps separate CopiedTexture / CopiedFlat buffers for vanilla formats.
    // These values are independent of the geometry clipboard and source map.
    public sealed class SurfaceTextureClipboard
    {
        public string Wall { get; private set; }
        public string Flat { get; private set; }
        public string Texture(SurfaceRef surface) { return surface.IsFlat?Flat:Wall; }
        public bool CanCopy(MapDocument map,SurfaceRef surface)
        {
            if(!SurfaceTextureEdits.Valid(map,surface))return false;
            try {SurfaceTextureEdits.Name(VisualGeometry.Texture(map,surface));return true;}
            catch(InvalidOperationException){return false;}
        }
        public bool CanPaste(MapDocument map,SurfaceRef surface)
        {return SurfaceTextureEdits.Valid(map,surface)&&Texture(surface)!=null;}
        public void Copy(MapDocument map,SurfaceRef surface)
        {
            if(!SurfaceTextureEdits.Valid(map,surface))throw new InvalidOperationException("Выберите существующую поверхность.");
            string value=SurfaceTextureEdits.Name(VisualGeometry.Texture(map,surface));
            if(surface.IsFlat)Flat=value;else Wall=value;
        }
    }
    public static class SurfaceTextureEdits
    {
        public static bool Valid(MapDocument map,SurfaceRef surface)
        {
            if(map==null||(map.format!=MapFormat.Doom&&map.format!=MapFormat.Hexen)||!Enum.IsDefined(typeof(SurfacePart),surface.part))return false;
            if(surface.sector<0||surface.sector>=map.sectors.Count)return false;
            if(surface.IsFlat)return true;
            if(surface.line<0||surface.line>=map.lines.Count||surface.side<0||surface.side>=map.sides.Count)return false;
            var line=map.lines[surface.line];
            // A ref without front/back identity cannot address an ambiguous pair.
            return (line.front==surface.side)!=(line.back==surface.side)&&map.sides[surface.side].sector==surface.sector;
        }
        internal static string Name(string value)
        {
            value=value==null?null:value.Trim();
            if(string.IsNullOrEmpty(value)||value.Length>8)throw new InvalidOperationException("Имя текстуры: 1–8 ASCII-символов.");
            foreach(char c in value)if(c<33||c>126)throw new InvalidOperationException("Имя текстуры должно быть ASCII.");
            return value.ToUpperInvariant();
        }
        // Return the current ref after detaching shared sidedef storage. Only the
        // selected line/part changes; raw SetTexture remains for bulk operations.
        public static SurfaceRef Set(MapDocument map,SurfaceRef surface,string texture)
        {
            if(!Valid(map,surface))throw new InvalidOperationException("Поверхность больше не существует. Выберите её снова.");
            texture=Name(texture);
            if(string.Equals(VisualGeometry.Texture(map,surface),texture,StringComparison.OrdinalIgnoreCase))return surface;
            surface=Exclusive(map,surface);
            VisualGeometry.SetTexture(map,surface,texture);return surface;
        }
        internal static SurfaceRef Exclusive(MapDocument map,SurfaceRef surface)
        {
            if(!Valid(map,surface))throw new InvalidOperationException("Поверхность больше не существует. Выберите её снова.");
            int uses=0;if(!surface.IsFlat)foreach(var line in map.lines){if(line.front==surface.side)uses++;if(line.back==surface.side)uses++;}
            return Exclusive(map,surface,uses);
        }
        internal static SurfaceRef Exclusive(MapDocument map,SurfaceRef surface,int uses)
        {
            if(!Valid(map,surface))throw new InvalidOperationException("Поверхность больше не существует. Выберите её снова.");
            if(!surface.IsFlat)surface.side=Exclusive(map,surface.line,map.lines[surface.line].front==surface.side,uses);
            return surface;
        }
        internal static int Exclusive(MapDocument map,int lineIndex,bool front,int uses)
        {
            var line=map.lines[lineIndex];int index=front?line.front:line.back;if(uses<=1)return index;
            if(map.sides.Count>=65535)throw new InvalidOperationException("Превышен предел сторон Doom / Hexen.");
            var side=map.sides[index];var copy=new MapSide{sector=side.sector,offsetX=side.offsetX,offsetY=side.offsetY,upper=side.upper,middle=side.middle,lower=side.lower};side.CopyFieldsTo(copy);
            int next=map.sides.Count;map.sides.Add(copy);if(front)line.front=next;else line.back=next;return next;
        }
    }
}
