// GPL-3.0. CPU patch composition, matching UDB TEXTURESImage's transform order.
using System;
using System.Collections.Generic;
using System.IO;

namespace MobileUDB.Core
{
    public sealed partial class DoomResources
    {
        sealed class TextureDecodeBudget
        {
            public readonly HashSet<ResourceTexture> active = new HashSet<ResourceTexture>();
            long pixels;
            public void Charge(int width,int height)
            { pixels += (long)width * height; if (pixels > 16L * 1024 * 1024) throw new InvalidDataException("Составная текстура превышает предел декодирования."); }
        }
        ResourceImage DecodeComposite(ResourceTexture entry,Func<byte[],ResourceImage> pngDecoder,TextureDecodeBudget budget)
        {
            if (entry.unsupported != null) throw new NotSupportedException(entry.name + ": " + entry.unsupported);
            if (budget.active.Count >= 16 || !budget.active.Add(entry)) throw new InvalidDataException("Циклическая ссылка на текстуру " + entry.name);
            try
            {
                budget.Charge(entry.width,entry.height);
                if (entry.patches == null) return DecodeImage(entry.image,entry.kind,pngDecoder);
                var image = new ResourceImage(entry.width,entry.height,new byte[checked(entry.width * entry.height * 4)]); int loaded = 0;
                foreach (var patch in entry.patches)
                {
                    if (entry.definitionKind != null && patch.name == "TNT1A0") { loaded++; continue; }
                    ResourceImage source = null; byte[] bytes;
                    if (lumps.TryGetValue(patch.name,out bytes))
                    {
                        try { source = DecodeImage(bytes,ResourceKind.Wall,pngDecoder); }
                        catch (InvalidDataException) { if (entry.definitionKind == null) throw; source = DecodeImage(bytes,ResourceKind.Flat,pngDecoder); }
                        budget.Charge(source.width,source.height);
                    }
                    else if (entry.definitionKind != null)
                    {
                        ResourceTexture other = Find(patch.name,ResourceKind.Wall,true) ?? Find(patch.name,ResourceKind.Sprite);
                        if (other == null) graphics.TryGetValue(patch.name,out other);
                        if (other != null) source = DecodeComposite(other,pngDecoder,budget);
                    }
                    if (source == null)
                    {
                        string message = "Не найден патч " + patch.name + " в " + entry.name;
                        if (entry.optional || entry.nullTexture) { ResourceWarning(message); continue; }
                        throw new InvalidDataException(message);
                    }
                    loaded++; CompositePatch(image,source,patch);
                }
                if (loaded == 0 && entry.patches.Count > 0 && !entry.nullTexture) throw new InvalidDataException("Не загружен ни один патч " + entry.name);
                return image;
            }
            finally { budget.active.Remove(entry); }
        }
        static void CompositePatch(ResourceImage destination,ResourceImage source,ResourcePatch patch)
        {
            int styleAlpha = (int)Math.Round(patch.alpha * 255);
            for (int y = 0; y < source.height; y++) for (int x = 0; x < source.width; x++)
            {
                int tx = patch.flipX ? source.width - 1 - x : x,ty = patch.flipY ? source.height - 1 - y : y,rx = tx,ry = ty;
                if (patch.rotation == 90) { rx = source.height - 1 - ty; ry = tx; }
                else if (patch.rotation == 180) { rx = source.width - 1 - tx; ry = source.height - 1 - ty; }
                else if (patch.rotation == 270) { rx = ty; ry = source.width - 1 - tx; }
                long dx = (long)rx + patch.x,dy = (long)ry + patch.y;
                if (dx < 0 || dx >= destination.width || dy < 0 || dy >= destination.height) continue;
                int from = (y * source.width + x) * 4,to = ((int)dy * destination.width + (int)dx) * 4;
                int alpha = source.rgba[from + 3]; if (alpha == 0) continue;
                // UDB's copy and modulate retain the source alpha. Translucent and
                // arithmetic styles multiply it by the patch Alpha property.
                if (patch.style != "copy" && patch.style != "modulate") alpha = alpha * styleAlpha / 255;
                if (alpha == 0) continue;
                int backgroundAlpha = destination.rgba[to + 3],combined = alpha * 255 + backgroundAlpha * (255 - alpha);
                for (int channel = 0; channel < 3; channel++)
                {
                    int color = source.rgba[from + channel],background = destination.rgba[to + channel];
                    int blend = channel == 0 ? patch.red : channel == 1 ? patch.green : patch.blue;
                    if (patch.tint == -1) color = color * blend / 255;
                    else if (patch.tint > 0) { int tint = Math.Min(254,Math.Max(1,(int)(patch.tint * 255))); color = (color * (255 - tint) + blend * tint) / 255; }
                    switch (patch.style)
                    {
                        case "add": color = Math.Min(255,color + background); break;
                        case "subtract": color = Math.Max(0,background - color); break;
                        case "reversesubtract": color = Math.Max(0,color - background); break;
                        case "modulate": color = color * background / 255; break;
                    }
                    destination.rgba[to + channel] = (byte)((color * alpha * 255 + background * backgroundAlpha * (255 - alpha) + combined / 2) / combined);
                }
                destination.rgba[to + 3] = (byte)((combined + 127) / 255);
            }
        }
    }
}
