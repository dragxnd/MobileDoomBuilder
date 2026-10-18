// GPL-3.0. Rules follow BuilderModes LinedefsMode.FlipLinedefs/FlipSidedefs.
using System;
using System.Collections.Generic;

namespace MobileUDB.Core
{
    public static class WallDirection
    {
        public static List<int> Candidates(MapDocument map,IEnumerable<ElementRef> selection,bool sidesOnly)
        {
            if (selection == null) throw new InvalidOperationException("Выберите стены или секторы.");
            var lines = new HashSet<int>(); var sectors = new HashSet<int>();
            foreach (var item in selection)
            {
                if (item.kind == ElementKind.Line)
                {
                    if (item.index < 0 || item.index >= map.lines.Count) throw new InvalidOperationException("Выбранная стена отсутствует.");
                    lines.Add(item.index);
                }
                else if (item.kind == ElementKind.Sector)
                {
                    if (item.index < 0 || item.index >= map.sectors.Count) throw new InvalidOperationException("Выбранный сектор отсутствует.");
                    sectors.Add(item.index);
                }
            }
            if (sectors.Count > 0) for (int i = 0; i < map.lines.Count; i++)
            {
                var line = map.lines[i];
                if ((line.front >= 0 && sectors.Contains(map.sides[line.front].sector)) || (line.back >= 0 && sectors.Contains(map.sides[line.back].sector))) lines.Add(i);
            }
            var result = new List<int>();
            foreach (int index in lines)
            {
                var line = map.lines[index];
                if (sidesOnly ? line.front >= 0 && line.back >= 0 && line.front != line.back : line.back >= 0 || line.front < 0) result.Add(index);
            }
            result.Sort(); return result;
        }
        public static int Flip(MapDocument map,IEnumerable<ElementRef> selection,bool sidesOnly)
        {
            var candidates = Candidates(map,selection,sidesOnly);
            if (candidates.Count == 0) return 0;
            var work = map.Clone();
            foreach (int index in candidates)
            {
                var line = work.lines[index];
                if (!sidesOnly) { int vertex = line.start; line.start = line.end; line.end = vertex; }
                line.SwapSides();
            }
            work.Validate(); map.lines = work.lines;
            return candidates.Count;
        }
    }
}
