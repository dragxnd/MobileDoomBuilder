// GPL-3.0. Wall texture anchors follow UDB VisualMiddleSingle/Double, VisualUpper/Lower;
// flat UVs follow VisualFloor/Ceiling. No Unity or desktop UI dependencies.
using System;
using System.Collections.Generic;
using System.Globalization;

namespace MobileUDB.Core
{
    public enum SurfacePart { Floor, Ceiling, Upper, Middle, Lower }
    public struct SurfaceRef : IEquatable<SurfaceRef>
    {
        public int sector, line, side;
        public SurfacePart part;
        public bool IsFlat { get { return part == SurfacePart.Floor || part == SurfacePart.Ceiling; } }
        public bool Equals(SurfaceRef other) { return sector == other.sector && line == other.line && side == other.side && part == other.part; }
        public override bool Equals(object obj) { return obj is SurfaceRef && Equals((SurfaceRef)obj); }
        public override int GetHashCode() { return (((sector * 397) ^ line) * 397 ^ side) * 397 ^ (int)part; }
    }
    public struct SurfaceVertex
    {
        public float x, y, height, u, v;
        public SurfaceVertex(MapPoint point, float height, float u, float v) { x = point.x; y = point.y; this.height = height; this.u = u; this.v = v; }
    }
    public sealed class MapSurface
    {
        public SurfaceRef id;
        public string texture;
        public int light;
        public bool sky;
        public SkyTransfer skyTransfer;
        public ResourceKind kind;
        public readonly List<SurfaceVertex> vertices = new List<SurfaceVertex>();
        public readonly List<int> indices = new List<int>();
    }
    public struct WallTextureMapping
    {
        public float width, height, unitX, unitY, offsetX, offsetY, anchor;
        public bool wrap;
        public double StartU { get { return offsetX / width; } }
        public double ZeroV { get { return 1 - (anchor + offsetY) / height; } }
    }
    public static class VisualGeometry
    {
        public static float Field(MapElementData element, string name, float fallback)
        {
#if MOBILEUDB_UDMF
            string value = UdmfText.Raw(element.fields,name); if (value == null) return fallback;
            if (value.IndexOf("0x",StringComparison.OrdinalIgnoreCase) >= 0) return UdmfText.Integer(element.fields,name,0);
            float number;
            if (!float.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out number) || float.IsNaN(number) || float.IsInfinity(number))
                throw new InvalidOperationException("Некорректное поле " + name + ".");
            return number;
#else
            return fallback;
#endif
        }
        public static float Height(MapSector sector, bool ceiling, MapPoint point)
        {
            string prefix = ceiling ? "ceilingplane_" : "floorplane_";
            float c = Field(sector,prefix + "c",0);
            if (Math.Abs(c) < .000001f) return ceiling ? sector.ceiling : sector.floor;
            return -(Field(sector,prefix + "a",0) * point.x + Field(sector,prefix + "b",0) * point.y + Field(sector,prefix + "d",0)) / c;
        }
        public static void ChangeHeight(MapDocument map, int sectorIndex, bool ceiling, float amount)
        {
            var sector = map.sectors[sectorIndex]; float value = (ceiling ? sector.ceiling : sector.floor) + amount;
            if (value < -32768 || value > 32767) throw new InvalidOperationException("Высота за пределами поддерживаемого диапазона.");
            string prefix = ceiling ? "ceilingplane_" : "floorplane_"; float c = Field(sector,prefix + "c",0);
            if (Math.Abs(c) > .000001f) UdmfText.SetRaw(sector.fields,prefix + "d",(Field(sector,prefix + "d",0) - amount * c).ToString("R",CultureInfo.InvariantCulture));
            if (ceiling) sector.ceiling = value; else sector.floor = value;
            foreach (var line in map.lines)
            {
                if (!(line.front >= 0 && map.sides[line.front].sector == sectorIndex) && !(line.back >= 0 && map.sides[line.back].sector == sectorIndex)) continue;
                foreach (int vertex in new[] { line.start,line.end })
                { var p = map.vertices[vertex].Position; if (Height(sector,false,p) > Height(sector,true,p)) throw new InvalidOperationException("Пол выше потолка."); }
            }
        }
        public static string Texture(MapDocument map, SurfaceRef surface)
        {
            if (surface.IsFlat) return surface.part == SurfacePart.Floor ? map.sectors[surface.sector].floorTexture : map.sectors[surface.sector].ceilingTexture;
            var side = map.sides[surface.side]; return surface.part == SurfacePart.Upper ? side.upper : surface.part == SurfacePart.Lower ? side.lower : side.middle;
        }
        public static void SetTexture(MapDocument map, SurfaceRef surface, string texture)
        {
            if (surface.IsFlat) { if (surface.part == SurfacePart.Floor) map.sectors[surface.sector].floorTexture = texture; else map.sectors[surface.sector].ceilingTexture = texture; }
            else { var side = map.sides[surface.side]; if (surface.part == SurfacePart.Upper) side.upper = texture; else if (surface.part == SurfacePart.Lower) side.lower = texture; else side.middle = texture; }
        }
        static float Scale(float value) { return Math.Abs(value) < .000001 ? 1 : value; }
        public static IEnumerable<MapSurface> Build(MapDocument map, DoomResources resources, List<string> warnings, bool skyTransfers = false)
        {
            var transfers = SkyTransfers.Build(map,skyTransfers);
            var tessellation = new SectorTessellation(map);
            for (int i = 0; i < map.sectors.Count; i++)
            {
                List<MapTriangle> triangles;
                try { triangles = tessellation.Triangles(i); }
                catch (InvalidOperationException e) { if (warnings.Count < 20) warnings.Add(e.Message); continue; }
                if (triangles.Count == 0) continue;
                foreach (bool ceiling in new[] { false,true })
                {
                    var surface = Flat(map,i,ceiling,triangles,resources);
                    if (surface.sky) transfers.TryGetValue(map.sectors[i].tag,out surface.skyTransfer);
                    yield return surface;
                }
            }
            foreach (var wall in BuildWalls(map,resources))
            {
                if(wall.sky)
                {
                    var line=map.lines[wall.id.line];int other=line.front==wall.id.side?line.back:line.front;
                    bool floor=wall.id.part==SurfacePart.Lower;var sector=map.sectors[wall.id.sector];
                    if(other>=0&&!GameSky.Is(map,floor?sector.floorTexture:sector.ceilingTexture))sector=map.sectors[map.sides[other].sector];
                    transfers.TryGetValue(sector.tag,out wall.skyTransfer);
                }
                yield return wall;
            }
        }
        public static IEnumerable<MapSurface> BuildWalls(MapDocument map,DoomResources resources)
        {
            for (int i = 0; i < map.lines.Count; i++)
            {
                var line = map.lines[i];
                if (line.front >= 0) foreach (var wall in Walls(map,i,line.front,line.back,true,resources)) yield return wall;
                if (line.back >= 0) foreach (var wall in Walls(map,i,line.back,line.front,false,resources)) yield return wall;
            }
        }
        public static WallTextureMapping WallMapping(MapDocument map,SurfaceRef surface,ResourceTexture texture)
        {
            var line = map.lines[surface.line]; var side = map.sides[surface.side]; var sector = map.sectors[side.sector];
            int otherSide = line.front == surface.side ? line.back : line.front;
            var other = otherSide < 0 ? null : map.sectors[map.sides[otherSide].sector];
            string suffix = surface.part == SurfacePart.Upper ? "top" : surface.part == SurfacePart.Lower ? "bottom" : "mid";
            float tx = texture == null ? 1 : texture.scaleX, ty = texture == null ? 1 : texture.scaleY;
            float sx = Scale(Field(side,"scalex_" + suffix,1)), sy = Scale(Field(side,"scaley_" + suffix,1));
            var result = new WallTextureMapping { width = (texture == null ? 64 : texture.width) * tx / sx,
                height = (texture == null ? 128 : texture.height) * ty / sy,
                unitX = texture != null && texture.worldPanning ? 1 : tx / Math.Abs(sx),
                unitY = texture != null && texture.worldPanning ? 1 : ty / Math.Abs(sy) };
            result.offsetX = (side.offsetX + Field(side,"offsetx_" + suffix,0)) * result.unitX;
            result.offsetY = (side.offsetY + Field(side,"offsety_" + suffix,0)) * result.unitY;
            bool lowUnpegged = (line.flags & 16) != 0, highUnpegged = (line.flags & 8) != 0;
            if (surface.part == SurfacePart.Upper) result.anchor = highUnpegged ? sector.ceiling : other.ceiling + result.height;
            else if (surface.part == SurfacePart.Lower) result.anchor = lowUnpegged ? (GameSky.Is(map,sector.ceilingTexture) && GameSky.Is(map,other.ceilingTexture) ? other.ceiling : sector.ceiling) : other.floor;
            else if (other == null) result.anchor = lowUnpegged ? sector.floor + result.height : sector.ceiling;
            else result.anchor = lowUnpegged ? Math.Max(sector.floor,other.floor) + Math.Abs(result.height) : Math.Min(sector.ceiling,other.ceiling);
            result.wrap = other == null || UdmfText.Boolean(side.fields,"wrapmidtex",false) || UdmfText.Boolean(line.fields,"wrapmidtex",false) || (map.format == MapFormat.Hexen && line.action == 121 && (line.args[1] & 16) != 0);
            return result;
        }
        static MapSurface Flat(MapDocument map, int sectorIndex, bool ceiling, List<MapTriangle> triangles, DoomResources resources)
        {
            var sector = map.sectors[sectorIndex]; string suffix = ceiling ? "ceiling" : "floor";
            var surface = new MapSurface { id = new SurfaceRef { sector = sectorIndex,line = -1,side = -1,part = ceiling ? SurfacePart.Ceiling : SurfacePart.Floor },
                texture = ceiling ? sector.ceilingTexture : sector.floorTexture,kind = ResourceKind.Flat };
            surface.sky = GameSky.Is(map,surface.texture);
            var texture = resources.Find(surface.texture,ResourceKind.Flat,map.format == MapFormat.Udmf);
            float width = texture == null ? 64 : texture.width * texture.scaleX, height = texture == null ? 64 : texture.height * texture.scaleY;
            float angle = Field(sector,"rotation" + suffix,0) * (float)Math.PI / 180;
            float cos = (float)Math.Cos(angle), sin = (float)Math.Sin(angle);
            float ox = Field(sector,"xpanning" + suffix,0), oy = Field(sector,"ypanning" + suffix,0);
            float sx = Scale(Field(sector,"xscale" + suffix,1)), sy = Scale(Field(sector,"yscale" + suffix,1));
            int extra = (int)Field(sector,"light" + suffix,0);
            surface.light = UdmfText.Boolean(sector.fields,"light" + suffix + "absolute",false) ? extra : sector.light + extra;
            foreach (var triangle in triangles)
            {
                foreach (var point in new[] { triangle.a,ceiling ? triangle.c : triangle.b,ceiling ? triangle.b : triangle.c })
                {
                    float u = (point.x * cos - point.y * sin + ox) * sx / width;
                    float v = 1 - (-(point.x * sin + point.y * cos) + oy) * sy / height;
                    surface.indices.Add(surface.vertices.Count); surface.vertices.Add(new SurfaceVertex(point,Height(sector,ceiling,point),u,v));
                }
            }
            return surface;
        }
        static IEnumerable<MapSurface> Walls(MapDocument map, int lineIndex, int sideIndex, int otherIndex, bool front, DoomResources resources)
        {
            var line = map.lines[lineIndex]; var side = map.sides[sideIndex]; var sector = map.sectors[side.sector];
            var other = otherIndex < 0 ? null : map.sectors[map.sides[otherIndex].sector];
            var start = map.vertices[front ? line.start : line.end].Position; var end = map.vertices[front ? line.end : line.start].Position;
            double length = Math.Sqrt((end - start).LengthSquared); if (length < .001) yield break;
            var positions = new SortedSet<float> { 0,1 };
            var heightsA = new[] { Height(sector,false,start),Height(sector,true,start),other == null ? sector.floor : Height(other,false,start),other == null ? sector.ceiling : Height(other,true,start) };
            var heightsB = new[] { Height(sector,false,end),Height(sector,true,end),other == null ? sector.floor : Height(other,false,end),other == null ? sector.ceiling : Height(other,true,end) };
            // Split where sloped planes cross, so every wall segment has a fixed top/bottom order.
            for (int i = 0; i < 4; i++) for (int j = i + 1; j < 4; j++)
            {
                float a = heightsA[i] - heightsA[j], b = heightsB[i] - heightsB[j];
                if (a * b < 0) positions.Add(a / (a - b));
            }
            var breaks = new List<float>(positions);
            foreach (var part in new[] { SurfacePart.Middle,SurfacePart.Lower,SurfacePart.Upper })
            {
                if (other == null && part != SurfacePart.Middle) continue;
                if (other != null && part == SurfacePart.Middle && (string.IsNullOrEmpty(side.middle) || side.middle == "-")) continue;
                var surface = new MapSurface { id = new SurfaceRef { sector = side.sector,line = lineIndex,side = sideIndex,part = part },kind = ResourceKind.Wall,
                    texture = part == SurfacePart.Upper ? side.upper : part == SurfacePart.Lower ? side.lower : side.middle,light = sector.light };
                // Source VisualUpper / VisualLower render these boundary polygons
                // as sky, including gaps next to a sky plane. They are not holes.
                if(other!=null)surface.sky=part==SurfacePart.Upper?
                    GameSky.Is(map,other.ceilingTexture)&&(GameSky.Is(map,sector.ceilingTexture)||string.IsNullOrEmpty(side.upper)||side.upper=="-"):
                    part==SurfacePart.Lower&&GameSky.Is(map,other.floorTexture)&&(string.IsNullOrEmpty(side.lower)||side.lower=="-");
                string suffix = part == SurfacePart.Upper ? "top" : part == SurfacePart.Lower ? "bottom" : "mid";
                var texture = resources.Find(surface.texture,ResourceKind.Wall,map.format == MapFormat.Udmf);
                var mapping = WallMapping(map,surface.id,texture);
                float width = mapping.width, height = mapping.height, ox = mapping.offsetX, oy = mapping.offsetY, anchor = mapping.anchor;
                bool wrap = mapping.wrap;
                int extra = (int)Field(side,"light_" + suffix,Field(side,"light",0));
                surface.light = UdmfText.Boolean(side.fields,"lightabsolute",false) ? extra : sector.light + extra;
                for (int i = 0; i + 1 < breaks.Count; i++)
                {
                    float t0 = breaks[i], t1 = breaks[i + 1]; var a = Lerp(start,end,t0); var b = Lerp(start,end,t1);
                    float fa = Height(sector,false,a), fb = Height(sector,false,b), ca = Height(sector,true,a), cb = Height(sector,true,b);
                    float ofa = other == null ? fa : Height(other,false,a), ofb = other == null ? fb : Height(other,false,b);
                    float oca = other == null ? ca : Height(other,true,a), ocb = other == null ? cb : Height(other,true,b);
                    float bottomA, bottomB, topA, topB;
                    if (part == SurfacePart.Lower) { bottomA = fa; bottomB = fb; topA = Math.Min(ca,ofa); topB = Math.Min(cb,ofb); }
                    else if (part == SurfacePart.Upper) { bottomA = Math.Max(fa,oca); bottomB = Math.Max(fb,ocb); topA = ca; topB = cb; }
                    else { bottomA = Math.Max(fa,ofa); bottomB = Math.Max(fb,ofb); topA = Math.Min(ca,oca); topB = Math.Min(cb,ocb); }
                    if (part == SurfacePart.Middle && !wrap)
                    { float textureTop = anchor + oy; bottomA = Math.Max(bottomA,textureTop - Math.Abs(height)); bottomB = Math.Max(bottomB,textureTop - Math.Abs(height)); topA = Math.Min(topA,textureTop); topB = Math.Min(topB,textureTop); }
                    // Texture clipping may itself cut a sloped segment. Preserve its positive-height portion.
                    float da = topA - bottomA, db = topB - bottomB;
                    if (da <= .001 && db <= .001) continue;
                    if (da < 0) { float t = -da / (db - da); a = Lerp(a,b,t); t0 += (t1 - t0) * t; bottomA += (bottomB - bottomA) * t; topA = bottomA; }
                    else if (db < 0) { float t = da / (da - db); b = Lerp(a,b,t); t1 = t0 + (t1 - t0) * t; bottomB = bottomA + (bottomB - bottomA) * t; topB = bottomB; }
                    int n = surface.vertices.Count;
                    surface.vertices.Add(new SurfaceVertex(a,bottomA,(float)(length * t0 + ox) / width,1 - (anchor - bottomA + oy) / height));
                    surface.vertices.Add(new SurfaceVertex(a,topA,(float)(length * t0 + ox) / width,1 - (anchor - topA + oy) / height));
                    surface.vertices.Add(new SurfaceVertex(b,topB,(float)(length * t1 + ox) / width,1 - (anchor - topB + oy) / height));
                    surface.vertices.Add(new SurfaceVertex(b,bottomB,(float)(length * t1 + ox) / width,1 - (anchor - bottomB + oy) / height));
                    surface.indices.AddRange(new[] { n,n + 1,n + 2,n,n + 2,n + 3 });
                }
                if (surface.indices.Count > 0) yield return surface;
            }
        }
        static MapPoint Lerp(MapPoint a, MapPoint b, float t) { return new MapPoint(a.x + (b.x - a.x) * t,a.y + (b.y - a.y) * t); }
    }
}
