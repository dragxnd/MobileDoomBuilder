using System;

namespace MobileUDB.Core
{
    public struct ToolbarGroupLayout
    {
        public float x, width, bottom, height, cellWidth;
        public int columns, rows;
    }
    public sealed class MobileToolbarLayout
    {
        public const float ButtonWidth = 48, ButtonHeight = 50, Spacing = 4, CaptionHeight = 14;
        const float Insets = 24, StatusHeight = 40, GroupGap = 2, TopGap = 4;
        public readonly ToolbarGroupLayout[] groups;
        public readonly float footerHeight;
        MobileToolbarLayout(ToolbarGroupLayout[] groups,float footerHeight) { this.groups = groups; this.footerHeight = footerHeight; }
        static float RequiredWidth(int columns,float columnSpacing = Spacing) { return Insets + columns * ButtonWidth + (columns - 1) * columnSpacing; }
        static float Height(int rows) { return CaptionHeight + rows * ButtonHeight + (rows - 1) * Spacing; }
        public static MobileToolbarLayout Create(float availableWidth,bool sideBySide,int[] counts,float columnSpacing = Spacing)
        {
            if (counts == null || counts.Length != 3) throw new ArgumentException("Three toolbar groups are required.");
            if (float.IsNaN(availableWidth) || float.IsInfinity(availableWidth) || availableWidth < RequiredWidth(1)) throw new ArgumentOutOfRangeException(nameof(availableWidth));
            foreach (int count in counts) if (count < 1 || count > 32) throw new ArgumentOutOfRangeException(nameof(counts));
            if (float.IsNaN(columnSpacing) || float.IsInfinity(columnSpacing) || columnSpacing < 0) throw new ArgumentOutOfRangeException(nameof(columnSpacing));
            var groups = new ToolbarGroupLayout[3];
            if (sideBySide)
            {
                int bestMax = int.MaxValue, bestSum = int.MaxValue, bestEmpty = int.MaxValue;
                float bestWidth = float.MaxValue; int[] best = null;
                // Balanced grids only (e.g. 3+3 instead of 5+1). Prefer fewer rows
                // while preserving every button's minimum width and all captions.
                // Taller horizontal strips waste more space than stacked groups.
                for (int a = 1; a <= Math.Min(2,counts[0]); a++) for (int b = 1; b <= Math.Min(2,counts[1]); b++) for (int c = 1; c <= Math.Min(2,counts[2]); c++)
                {
                    int ca = (counts[0] + a - 1) / a, cb = (counts[1] + b - 1) / b, cc = (counts[2] + c - 1) / c;
                    int ra = (counts[0] + ca - 1) / ca, rb = (counts[1] + cb - 1) / cb, rc = (counts[2] + cc - 1) / cc;
                    float required = RequiredWidth(ca,columnSpacing) + RequiredWidth(cb,columnSpacing) + RequiredWidth(cc,columnSpacing);
                    if (required > availableWidth) continue;
                    int max = Math.Max(ra,Math.Max(rb,rc)), sum = ra + rb + rc, empty = ca * ra + cb * rb + cc * rc - counts[0] - counts[1] - counts[2];
                    if (max > bestMax || (max == bestMax && (sum > bestSum || (sum == bestSum && (empty > bestEmpty || (empty == bestEmpty && required >= bestWidth)))))) continue;
                    bestMax = max; bestSum = sum; bestEmpty = empty; bestWidth = required; best = new[] { ca,cb,cc };
                }
                if (best != null)
                {
                    float x = 0, extraPerColumn = (availableWidth - bestWidth) / (best[0] + best[1] + best[2]);
                    for (int i = 0; i < groups.Length; i++)
                    {
                        int columns = best[i]; float width = RequiredWidth(columns,columnSpacing) + columns * extraPerColumn;
                        groups[i] = new ToolbarGroupLayout { x = x,width = width,bottom = StatusHeight,height = Height(bestMax),columns = columns,rows = (counts[i] + columns - 1) / columns,cellWidth = ButtonWidth + extraPerColumn };
                        x += width;
                    }
                    return new MobileToolbarLayout(groups,StatusHeight + Height(bestMax) + TopGap);
                }
            }
            // Portrait / very narrow split-screen: stack the same visible groups.
            int capacity = Math.Max(1,(int)Math.Floor((availableWidth - Insets + columnSpacing) / (ButtonWidth + columnSpacing)));
            float bottom = StatusHeight;
            for (int i = groups.Length - 1; i >= 0; i--)
            {
                int columns = Math.Min(capacity,counts[i]), rows = (counts[i] + columns - 1) / columns;
                float height = Height(rows);
                groups[i] = new ToolbarGroupLayout { width = availableWidth,bottom = bottom,height = height,columns = columns,rows = rows,cellWidth = (availableWidth - Insets - (columns - 1) * columnSpacing) / columns };
                bottom += height + (i == 0 ? 0 : GroupGap);
            }
            return new MobileToolbarLayout(groups,bottom + TopGap);
        }
    }
}
