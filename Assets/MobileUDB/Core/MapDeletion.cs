using System;
using System.Collections.Generic;

namespace MobileUDB.Core
{
    public sealed partial class MapDocument
    {
        // Binary deletion follows SectorsMode.DeleteItem and the native
        // Linedef/Vertex/Sector disposal cascades. Repair only touched boundaries.
        public void Delete(HashSet<ElementRef> selection,bool autoClearTextures=true)
        {
            if(selection==null)throw new ArgumentNullException("selection");
            var removedVertices=new HashSet<int>();var removedLines=new HashSet<int>();var removedSectors=new HashSet<int>();var removedThings=new HashSet<int>();var vertexOrder=new List<int>();
            foreach(var id in selection)
            {
                int count;HashSet<int> removed;
                switch(id.kind)
                {
                    case ElementKind.Vertex:count=vertices.Count;removed=removedVertices;break;
                    case ElementKind.Line:count=lines.Count;removed=removedLines;break;
                    case ElementKind.Sector:count=sectors.Count;removed=removedSectors;break;
                    case ElementKind.Thing:count=things.Count;removed=removedThings;break;
                    default:throw new InvalidOperationException("Неизвестный тип элемента.");
                }
                if(id.index<0||id.index>=count)throw new InvalidOperationException("Элемент больше не существует. Выберите его снова.");if(removed.Add(id.index)&&id.kind==ElementKind.Vertex)vertexOrder.Add(id.index);
            }
            // Object deletion has no reason to normalize or compact geometry.
            if(removedVertices.Count==0&&removedLines.Count==0&&removedSectors.Count==0){Compact(things,removedThings);return;}
            // VerticesMode.DeleteItem merges a degree-two vertex before disposal.
            // Mixed-kind removal has no native mode counterpart; retain its
            // existing explicit incident-line deletion semantics.
            if(removedVertices.Count>0&&removedLines.Count==0&&removedSectors.Count==0&&removedThings.Count==0)DeleteVerticesBeforeCompact(vertexOrder,removedVertices,removedLines);
            var affected=new HashSet<int>();var candidateSides=new HashSet<int>();var candidateVertices=new HashSet<int>();var candidateSectors=new HashSet<int>();
            for(int i=0;i<sides.Count;i++)if(removedSectors.Contains(sides[i].sector))candidateSides.Add(i);
            for(int i=0;i<lines.Count;i++)
            {
                var line=lines[i];if(removedVertices.Contains(line.start)||removedVertices.Contains(line.end))removedLines.Add(i);
                if(removedLines.Contains(i))continue;
                bool front=line.front>=0&&removedSectors.Contains(sides[line.front].sector),back=line.back>=0&&removedSectors.Contains(sides[line.back].sector);
                if(!front&&!back)continue;affected.Add(i);
                if(front)line.front=-1;if(back)line.back=-1;
                if(line.front<0&&line.back<0)removedLines.Add(i);
            }
            foreach(int index in removedLines)
            {
                var line=lines[index];candidateVertices.Add(line.start);candidateVertices.Add(line.end);
                if(line.front>=0)candidateSides.Add(line.front);if(line.back>=0)candidateSides.Add(line.back);
            }
            var uses=new int[sides.Count];var usedVertices=new HashSet<int>();
            for(int i=0;i<lines.Count;i++)if(!removedLines.Contains(i))
            {var line=lines[i];usedVertices.Add(line.start);usedVertices.Add(line.end);if(line.front>=0)uses[line.front]++;if(line.back>=0)uses[line.back]++;}
            foreach(int index in affected)
            {
                if(removedLines.Contains(index))continue;var line=lines[index];
                if(line.front<0&&line.back>=0){int swap=line.start;line.start=line.end;line.end=swap;line.SwapSides();}
                var side=sides[line.front];string middle=side.middle,upper=side.upper,lower=side.lower;
                // Native single-sided MiddleRequired: prefer existing high, then
                // low. If both are absent, leave the missing texture for repair.
                if(DeleteEmptyTexture(middle)){if(!DeleteEmptyTexture(upper))middle=upper;else if(!DeleteEmptyTexture(lower))middle=lower;}
                // Native default AutoClearSidedefTextures=true, with action/tag
                // exemptions. Do not discard potential moving-sector textures.
                if(autoClearTextures&&line.tag==0&&line.action==0&&sectors[side.sector].tag==0){upper="-";lower="-";}
                if(middle!=side.middle||upper!=side.upper||lower!=side.lower)
                {
                    // Shared WAD sidedef records are supported by this port.
                    // Isolate a changed boundary from any surviving other user.
                    if(uses[line.front]>1)
                    {
                        uses[line.front]--;var copy=new MapSide{sector=side.sector,offsetX=side.offsetX,offsetY=side.offsetY,upper=side.upper,middle=side.middle,lower=side.lower};
                        side.CopyFieldsTo(copy);sides.Add(copy);line.front=sides.Count-1;side=copy;
                    }
                    side.middle=middle;side.upper=upper;side.lower=lower;
                }
                line.flags=(line.flags&~4)|1;
            }
            foreach(int vertex in candidateVertices)if(!usedVertices.Contains(vertex))removedVertices.Add(vertex);
            var usedSides=new HashSet<int>();for(int i=0;i<lines.Count;i++)if(!removedLines.Contains(i))
            {var line=lines[i];if(line.front>=0)usedSides.Add(line.front);if(line.back>=0)usedSides.Add(line.back);}
            var removedSides=new HashSet<int>();foreach(int side in candidateSides)if(!usedSides.Contains(side)){removedSides.Add(side);candidateSectors.Add(sides[side].sector);}
            var remainingSectorSides=new HashSet<int>();for(int i=0;i<sides.Count;i++)if(!removedSides.Contains(i))remainingSectorSides.Add(sides[i].sector);
            // Dispose empty sectors touched by removed sidedefs, as native
            // Sector.DetachSidedefP does. Preserve unrelated pre-existing orphans.
            foreach(int sector in candidateSectors)if(!remainingSectorSides.Contains(sector))removedSectors.Add(sector);
            var vertexMap=Compact(vertices,removedVertices);Compact(lines,removedLines);Compact(things,removedThings);
            var sideMap=Compact(sides,removedSides);var sectorMap=Compact(sectors,removedSectors);
            foreach(var side in sides)side.sector=sectorMap[side.sector];
            foreach(var line in lines){line.start=vertexMap[line.start];line.end=vertexMap[line.end];if(line.front>=0)line.front=sideMap[line.front];if(line.back>=0)line.back=sideMap[line.back];}
        }
        static bool DeleteEmptyTexture(string name){return string.IsNullOrEmpty(name)||name=="-";}
    }
}
