// GPL-3.0. Port of MapSet's CLASSIC vertex stitching and Linedef.Join rules.
using System;
using System.Collections.Generic;

namespace MobileUDB.Core
{
    public static partial class MapStitch
    {
        const double ClassicDistance = .005;
        sealed class ClassicResult
        {
            public MapDocument map;
            public int[] vertices,sectors;
            public readonly Dictionary<int,List<int>> lines = new Dictionary<int,List<int>>();
        }
        sealed class ClassicPiece
        {
            public MapLine line;
            public bool moving,changed;
            public readonly HashSet<int> sources = new HashSet<int>();
        }
        struct ClassicCut { public double t; public int vertex; }
        static MapSide ClassicCopySide(MapSide source)
        {
            if (source == null) return null;
            var side = new MapSide { sector=source.sector,upper=source.upper,middle=source.middle,lower=source.lower,offsetX=source.offsetX,offsetY=source.offsetY };
            source.CopyFieldsTo(side); return side;
        }
        static int ClassicSide(MapDocument map,int index,MapDocument output,double distance,DoomResources resources)
        {
            if (index < 0) return -1;
            var side = ClassicCopySide(map.sides[index]); ShiftTexture(side,distance,map.format,resources);
            int result = output.sides.Count; output.sides.Add(side); return result;
        }
        static void ClassicTextures(MapSide source,MapSide target,MapFormat format)
        {
            if (source == null || target == null) return;
            int offsets = 0;
            foreach (string part in new[] { "top","mid","bottom" })
            {
                string name = part == "top" ? source.upper : part == "mid" ? source.middle : source.lower;
                if (string.IsNullOrEmpty(name) || name == "-") continue;
                if (part == "top") target.upper = name; else if (part == "mid") target.middle = name; else target.lower = name;
                offsets += part == "mid" ? 2 : 1;
                if (format == MapFormat.Udmf) foreach (string prefix in new[] { "offsetx_","offsety_","scalex_","scaley_" })
                {
                    string key = prefix + part, raw = UdmfText.Raw(source.fields,key);
                    UdmfText.SetRaw(target.fields,key,raw ?? (prefix.StartsWith("scale",StringComparison.Ordinal) ? "1" : "0"));
                }
            }
            if (offsets >= 2) { target.offsetX = source.offsetX; target.offsetY = source.offsetY; }
        }
        static void ClassicJoin(MapDocument map,ClassicPiece kept,ClassicPiece removed)
        {
            var other = kept.line; var source = removed.line;
            MapSide of = other.front < 0 ? null : map.sides[other.front], ob = other.back < 0 ? null : map.sides[other.back];
            MapSide sf = source.front < 0 ? null : map.sides[source.front], sb = source.back < 0 ? null : map.sides[source.back];
            bool otherDouble = of != null && ob != null, sourceDouble = sf != null && sb != null;
            Action<MapSide,MapSide> textures = (a,b) => ClassicTextures(a,b,map.format);
            Action<bool,MapSide> set = (front,side) =>
            {
                int index = -1; if (side != null) { index = map.sides.Count; map.sides.Add(ClassicCopySide(side)); }
                other.RememberSideOrder();if (front) other.front = index; else other.back = index;
            };
            if (sf == null && sb == null) { }
            else if (of == null && ob == null)
            {
                set(source.start == other.start,sf); set(source.start != other.start,sb);
                var properties = source.Clone(); properties.start = other.start; properties.end = other.end; properties.front = other.front; properties.back = other.back;
                kept.line = other = properties;
            }
            else
            {
                if (of != null && sf != null && of.sector == sf.sector) { textures(of,sb); textures(sf,ob); set(true,sb); }
                else if (ob != null && sb != null && ob.sector == sb.sector) { textures(ob,sf); textures(sb,of); set(false,sf); }
                else if (of != null && sb != null && of.sector == sb.sector) { textures(of,sf); textures(sb,ob); set(true,sf); }
                else if (ob != null && sf != null && ob.sector == sf.sector) { textures(ob,sb); textures(sf,of); set(false,sb); }
                else if (ob == null)
                {
                    if (source.start == other.end) { textures(sb,of); set(false,sf); }
                    else { textures(sf,of); set(false,sb); }
                }
                else if (sb == null)
                {
                    if (other.start != source.end && of == null) { textures(of,sf); set(true,sf); }
                }
                else if (source.start == other.end) { textures(ob,sf); textures(sb,of); set(false,sf); }
                else { textures(ob,sb); textures(sf,of); set(false,sb); }
                bool nowDouble = other.front >= 0 && other.back >= 0;
                if (nowDouble != otherDouble)
                {
                    other.flags = nowDouble ? (other.flags | 4) & ~1 : (other.flags & ~4) | 1;
                    if (map.format == MapFormat.Udmf) { UdmfText.SetBool(other.fields,"twosided",nowDouble); UdmfText.SetBool(other.fields,"blocking",!nowDouble); }
                }
                foreach (bool front in new[] { true,false })
                {
                    int side = front ? other.front : other.back, opposite = front ? other.back : other.front;
                    if (side < 0) continue; var s = map.sides[side];
                    if (other.action == 0 && other.tag == 0 && map.sectors[s.sector].tag == 0 && (opposite < 0 || map.sectors[map.sides[opposite].sector].tag == 0))
                    {
                        if (!DrawTextureOptions.Required(map,other,side,opposite,SurfacePart.Upper)) s.upper = "-";
                        if (!DrawTextureOptions.Required(map,other,side,opposite,SurfacePart.Lower)) s.lower = "-";
                    }
                    if (!(otherDouble && sourceDouble) && opposite >= 0) s.middle = "-";
                }
            }
            // Marked geometry retains selection; stationary orientation is kept at a join.
            if (kept.moving && !removed.moving && source.start == other.end)
            { int v=other.start; other.start=other.end; other.end=v; other.SwapSides(); }
            kept.changed = true; kept.sources.UnionWith(removed.sources);
        }
        static ClassicResult ClassicGeometry(MapDocument source,HashSet<int> movingVertices,DoomResources resources,bool snap=false)
        {
            source.Validate(); var output = source.Clone(); var remap = new int[source.vertices.Count];
            for (int i=0;i<remap.Length;i++) remap[i]=i;
            if(snap)SnapVertexGroups(source,output,movingVertices,remap);
            else foreach (int moving in movingVertices)
            {
                if (moving < 0 || moving >= remap.Length) throw new InvalidOperationException("Некорректная вершина сшивки.");
                for (int i=0;i<remap.Length;i++) if (!movingVertices.Contains(i) && remap[i]==i && (output.vertices[i].Position-output.vertices[moving].Position).LengthSquared <= ClassicDistance*ClassicDistance)
                { remap[i]=moving; MergeFields(output.vertices[i],output.vertices[moving],false); }
            }
            output.lines.Clear(); output.sides.Clear(); var pieces = new List<ClassicPiece>();
            for (int index=0;index<source.lines.Count;index++)
            {
                var old=source.lines[index]; int start=remap[old.start],end=remap[old.end];
                bool moving=movingVertices.Contains(old.start)||movingVertices.Contains(old.end);
                bool touched=moving||start!=old.start||end!=old.end;
                if(start==end&&touched)continue;
                var a=output.vertices[start].Position;var b=output.vertices[end].Position;
                double dx=b.x-(double)a.x,dy=b.y-(double)a.y,length=dx*dx+dy*dy;
                var cuts=new List<ClassicCut>{new ClassicCut{t=0,vertex=start},new ClassicCut{t=1,vertex=end}};
                if(!snap&&length>0)for(int v=0;v<remap.Length;v++)
                {
                    if(remap[v]!=v||v==start||v==end||(!moving&&!movingVertices.Contains(v))||(moving&&movingVertices.Contains(v)))continue;
                    var p=output.vertices[v].Position;double t=((p.x-a.x)*dx+(p.y-a.y)*dy)/length;
                    if(t<=0||t>=1)continue;double x=p.x-a.x-t*dx,y=p.y-a.y-t*dy;
                    if(x*x+y*y<=ClassicDistance*ClassicDistance)cuts.Add(new ClassicCut{t=t,vertex=v});
                }
                cuts.Sort((aCut,bCut)=>aCut.t.CompareTo(bCut.t));
                for(int c=0;c+1<cuts.Count;c++)
                {
                    var first=cuts[c];var last=cuts[c+1];if(first.vertex==last.vertex)continue;
                    var line=old.Clone();line.start=first.vertex;line.end=last.vertex;
                    line.front=ClassicSide(source,old.front,output,Math.Sqrt(length)*first.t,resources);
                    line.back=ClassicSide(source,old.back,output,Math.Sqrt(length)*(1-last.t),resources);
                    var piece=new ClassicPiece{line=line,moving=moving,changed=touched||cuts.Count>2};piece.sources.Add(index);pieces.Add(piece);
                }
            }
            var groups=new Dictionary<Pair,List<ClassicPiece>>();foreach(var piece in pieces)
            {var key=new Pair(piece.line.start,piece.line.end);List<ClassicPiece> list;if(!groups.TryGetValue(key,out list)){list=new List<ClassicPiece>();groups.Add(key,list);}list.Add(piece);}
            var removedPieces=new HashSet<ClassicPiece>();
            foreach(var group in groups.Values)
            {
                var kept=group.Find(p=>p.moving)??group.Find(p=>p.changed);if(kept==null||group.Count<2)continue;
                foreach(var piece in group)if(piece!=kept){ClassicJoin(output,kept,piece);removedPieces.Add(piece);}
            }
            var result=new ClassicResult{map=output,vertices=remap,sectors=new int[output.sectors.Count]};
            foreach(var piece in pieces)if(!removedPieces.Contains(piece))
            {
                var line=piece.line;if(piece.changed&&line.front<0&&line.back>=0){int v=line.start;line.start=line.end;line.end=v;line.SwapSides();}
                int id=output.lines.Count;output.lines.Add(line);foreach(int old in piece.sources){List<int> list;if(!result.lines.TryGetValue(old,out list)){list=new List<int>();result.lines.Add(old,list);}list.Add(id);}
            }
            var usedSides=new HashSet<int>();var usedVertices=new HashSet<int>();var originallyUsedVertices=new HashSet<int>();var originallyUsedSectors=new HashSet<int>();
            if(snap)RepairSnappedSectors(source,output,movingVertices,remap);
            foreach(var line in source.lines){originallyUsedVertices.Add(line.start);originallyUsedVertices.Add(line.end);}
            foreach(var side in source.sides)originallyUsedSectors.Add(side.sector);
            foreach(var line in output.lines){usedVertices.Add(line.start);usedVertices.Add(line.end);if(line.front>=0)usedSides.Add(line.front);if(line.back>=0)usedSides.Add(line.back);}
            for(int i=0;i<remap.Length;i++)if(!originallyUsedVertices.Contains(i))usedVertices.Add(remap[i]);
            var sideIds=new int[output.sides.Count];var sides=new List<MapSide>();var usedSectors=new HashSet<int>();
            for(int i=0;i<sideIds.Length;i++){sideIds[i]=-1;if(usedSides.Contains(i)){sideIds[i]=sides.Count;sides.Add(output.sides[i]);usedSectors.Add(output.sides[i].sector);}}
            foreach(var line in output.lines){if(line.front>=0)line.front=sideIds[line.front];if(line.back>=0)line.back=sideIds[line.back];}output.sides=sides;
            var sectors=new List<MapSector>();for(int i=0;i<result.sectors.Length;i++){result.sectors[i]=-1;if(usedSectors.Contains(i)||!originallyUsedSectors.Contains(i)){result.sectors[i]=sectors.Count;sectors.Add(output.sectors[i]);}}
            output.sectors=sectors;foreach(var side in output.sides)side.sector=result.sectors[side.sector];
            var vertexIds=new int[output.vertices.Count];var vertices=new List<MapVertex>();for(int i=0;i<vertexIds.Length;i++){vertexIds[i]=-1;if(usedVertices.Contains(i)){vertexIds[i]=vertices.Count;vertices.Add(output.vertices[i]);}}
            foreach(var line in output.lines){line.start=vertexIds[line.start];line.end=vertexIds[line.end];}output.vertices=vertices;
            for(int i=0;i<remap.Length;i++)remap[i]=vertexIds[remap[i]];
            output.Validate();
            if(output.format!=MapFormat.Udmf)
            {
                if(output.vertices.Count>65535||output.lines.Count>65535||output.sides.Count>65535||output.sectors.Count>65535)throw new InvalidOperationException("Превышен лимит элементов бинарной карты.");
                foreach(var vertex in output.vertices)
                {
                    if(vertex.x < -32768||vertex.x > 32767||vertex.y < -32768||vertex.y > 32767)throw new InvalidOperationException("Координаты за пределами бинарного формата карты.");
                    vertex.Position=MapDocument.Snap(vertex.Position,1);
                }
                foreach(var piece in pieces)if(!removedPieces.Contains(piece)&&piece.changed&&(output.vertices[piece.line.start].Position-output.vertices[piece.line.end].Position).LengthSquared<.000001)
                    throw new InvalidOperationException("Округление сшивки схлопывает стену.");
            }
            return result;
        }
        static MapStitchResult ClassicOverlay(MapDocument target,MapDocument addition,DoomResources resources,ISet<int> external)
        {
            var combined=target.Clone();var copy=addition.Clone();int nv=combined.vertices.Count,nl=combined.lines.Count,ns=combined.sides.Count,nc=combined.sectors.Count,nt=combined.things.Count;
            var moving=new HashSet<int>();foreach(var vertex in copy.vertices){moving.Add(combined.vertices.Count);combined.vertices.Add(vertex);}
            foreach(var line in copy.lines){line.start+=nv;line.end+=nv;if(line.front>=0)line.front+=ns;if(line.back>=0)line.back+=ns;combined.lines.Add(line);}
            foreach(var side in copy.sides){side.sector+=nc;combined.sides.Add(side);}combined.sectors.AddRange(copy.sectors);combined.things.AddRange(copy.things);
            var stitched=ClassicGeometry(combined,moving,resources);var result=new MapStitchResult{map=stitched.map};
            for(int i=nv;i<stitched.vertices.Length;i++)if(stitched.vertices[i]>=0)result.vertices.Add(stitched.vertices[i]);
            for(int i=nl;i<combined.lines.Count;i++){List<int> ids;if(stitched.lines.TryGetValue(i,out ids))result.lines.UnionWith(ids);}
            for(int i=nc;i<stitched.sectors.Length;i++)if(stitched.sectors[i]>=0&&(external==null||!external.Contains(i-nc)))result.sectors.Add(stitched.sectors[i]);
            for(int i=nt;i<combined.things.Count;i++)result.things.Add(i);
            // Drawing alignment tracks only sides supplied by added geometry.
            foreach(int i in result.lines){var line=result.map.lines[i];foreach(int side in new[]{line.front,line.back})if(side>=0&&result.sectors.Contains(result.map.sides[side].sector))result.createdSides.Add(side);}
            return result;
        }
        static HashSet<ElementRef> ClassicMoved(MapDocument moved,HashSet<int> moving,HashSet<ElementRef> selection,DoomResources resources)
        {
            var stitched=ClassicGeometry(moved,moving,resources);var selected=new HashSet<ElementRef>();
            foreach(var item in selection)
            {
                int index=item.index;
                if(item.kind==ElementKind.Line){List<int> ids;if(stitched.lines.TryGetValue(index,out ids))foreach(int id in ids)selected.Add(new ElementRef(item.kind,id));continue;}
                if(item.kind==ElementKind.Vertex)index=stitched.vertices[index];if(item.kind==ElementKind.Sector)index=stitched.sectors[index];if(index>=0)selected.Add(new ElementRef(item.kind,index));
            }
            moved.vertices=stitched.map.vertices;moved.lines=stitched.map.lines;moved.sides=stitched.map.sides;moved.sectors=stitched.map.sectors;
            return selected;
        }
    }
}
