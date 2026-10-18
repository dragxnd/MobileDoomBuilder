// Mobile port of Ultimate Doom Builder. GPL-3.0; see LICENSE.txt.
using System;
using System.Collections.Generic;

namespace MobileUDB.Core
{
    public sealed partial class MapDocument
    {
        bool IsBoundary(MapLine line, int sector)
        {
            return (line.front >= 0 && sides[line.front].sector == sector) !=
                (line.back >= 0 && sides[line.back].sector == sector);
        }
        public bool TrySectorBoundaryPoint(int sector, MapPoint point, float radius, out MapPoint result)
        {
            result = point; if (sector < 0 || sector >= sectors.Count) return false;
            double distance = (double)radius * radius; bool found = false;
            foreach (var line in lines)
            {
                if (!IsBoundary(line, sector)) continue;
                var a = vertices[line.start].Position; var b = vertices[line.end].Position;
                double t = Math.Max(0, Math.Min(1, EdgeParameter(a, b, point)));
                var projected = new MapPoint((float)(a.x + t * ((double)b.x - a.x)), (float)(a.y + t * ((double)b.y - a.y)));
                if ((projected - a).LengthSquared < 0.01f) projected = a;
                else if ((projected - b).LengthSquared < 0.01f) projected = b;
                double d = (projected - point).LengthSquared;
                if (d <= distance) { distance = d; result = projected; found = true; }
            }
            return found;
        }
        void ApplyTopology(MapDocument result)
        {
            result.Validate(); vertices = result.vertices; lines = result.lines;
            sides = result.sides; sectors = result.sectors;
        }
        public int JoinSectors(IEnumerable<int> selected, bool removeSharedLines)
        {
            ThingClipboard.Binary(this);if(selected==null)throw new ArgumentNullException("selected");
            var indices = new HashSet<int>();var order=new List<int>();
            foreach (int i in selected)
            {
                if (i < 0 || i >= sectors.Count) throw new InvalidOperationException("Некорректный выбранный сектор.");
                if(indices.Add(i))order.Add(i);
            }
            if (indices.Count < 2) throw new InvalidOperationException("Выберите минимум два сектора.");
            Validate();var result = Clone();var records=new List<MapSector>();foreach(int index in order)records.Add(result.sectors[index]);
            var properties=records[0];var removedLines = new HashSet<ElementRef>();var beforeFaces=new int[sectors.Count];var removedFaces=new int[sectors.Count];
            for (int i = 0; i < result.lines.Count; i++)
            {
                var l = result.lines[i];
                if(l.front>=0)beforeFaces[result.sides[l.front].sector]++;if(l.back>=0)beforeFaces[result.sides[l.back].sector]++;
                if (removeSharedLines && l.front >= 0 && l.back >= 0 &&
                    result.sides[l.front].sector != result.sides[l.back].sector &&
                    indices.Contains(result.sides[l.front].sector) && indices.Contains(result.sides[l.back].sector))
                {removedLines.Add(new ElementRef(ElementKind.Line,i));removedFaces[result.sides[l.front].sector]++;removedFaces[result.sides[l.back].sector]++;}
            }
            // Dispose the selected common lines first, as SectorsMode does. This
            // removes newly orphaned vertices/sides without global normalization.
            if(removedLines.Count>0)result.Delete(removedLines);
            int first=-1;for(int i=0;i<order.Count;i++)
            {int index=order[i];int alive=result.sectors.IndexOf(records[i]);if(alive>=0&&(removedFaces[index]==0||removedFaces[index]<beforeFaces[index])){first=alive;break;}}
            if(first<0)throw new InvalidOperationException("После удаления общих стен не осталось сектора. Карта не изменена.");
            var remaining=new HashSet<int>();foreach(var record in records){int index=result.sectors.IndexOf(record);if(index>=0)remaining.Add(index);}
            foreach(var side in result.sides)if(remaining.Contains(side.sector))side.sector=first;
            remaining.Remove(first);var mapping=Compact(result.sectors,remaining);
            foreach (var side in result.sides) side.sector = mapping[side.sector];
            // Native binary SectorProperties.Apply uses the first selected
            // sector's values even if removing its enclosing walls disposed it.
            // Keep surviving target editor metadata; binary copy skips UDMF fields.
            var target=result.sectors[mapping[first]];target.floor=properties.floor;target.ceiling=properties.ceiling;
            target.floorTexture=properties.floorTexture;target.ceilingTexture=properties.ceilingTexture;
            target.light=properties.light;target.special=properties.special;target.tag=properties.tag;
            ApplyTopology(result); return mapping[first];
        }
        public int SplitSector(int sector, MapPoint start, MapPoint end,DoomResources resources = null)
        {
            if (sector < 0 || sector >= sectors.Count) throw new InvalidOperationException("Выберите сектор для разделения.");
            var stitched = MapStitch.PartitionSector(this,sector,start,end,resources);
            ApplyTopology(stitched.map); foreach (int added in stitched.sectors) return added;
            throw new InvalidOperationException("Разрез не создал сектор.");
        }

    }
}
