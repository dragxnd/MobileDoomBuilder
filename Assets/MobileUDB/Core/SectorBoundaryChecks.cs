// GPL-3.0. Derived from UDB CheckClosedSectors and CheckMapSize.
using System;using System.Collections.Generic;
namespace MobileUDB.Core
{
    public static class SectorBoundaryChecks
    {
        sealed class Boundary
        {
            public readonly Dictionary<int,int> directions=new Dictionary<int,int>();
            public readonly HashSet<int> lines=new HashSet<int>();public int sideCount;
            public double minX=double.MaxValue,minY=double.MaxValue,maxX=double.MinValue,maxY=double.MinValue;
            public void Point(MapDocument map,int index,int direction)
            {int value;directions.TryGetValue(index,out value);directions[index]=value|direction;var point=map.vertices[index];minX=Math.Min(minX,point.x);maxX=Math.Max(maxX,point.x);minY=Math.Min(minY,point.y);maxY=Math.Max(maxY,point.y);}
        }
        // MapChecks first validates records/references/finite vertices. Null yields
        // cooperate with mobile time slices; the scan never repairs or mutates data.
        public static IEnumerable<MapIssue> Scan(MapDocument map,int safeBoundary=32767)
        {
            var sectors=new Boundary[map.sectors.Count];for(int i=0;i<sectors.Length;i++){sectors[i]=new Boundary();if((i&127)==0)yield return null;}
            var incident=new List<int>[map.vertices.Count];double minX=int.MaxValue,minY=int.MaxValue,maxX=int.MinValue,maxY=int.MinValue;int minXi=-1,minYi=-1,maxXi=-1,maxYi=-1;
            for(int i=0;i<map.vertices.Count;i++)
            {
                incident[i]=new List<int>();var vertex=map.vertices[i];
                if(vertex.x<minX){minX=vertex.x;minXi=i;}if(vertex.x>maxX){maxX=vertex.x;maxXi=i;}
                if(vertex.y<minY){minY=vertex.y;minYi=i;}if(vertex.y>maxY){maxY=vertex.y;maxYi=i;}
                if((i&127)==0)yield return null;
            }
            if(safeBoundary>0&&(maxX-minX>safeBoundary||maxY-minY>safeBoundary))
            {
                var references=new List<ElementRef>();var unique=new HashSet<int>();foreach(int index in new[]{minXi,minYi,maxXi,maxYi})if(index>=0&&unique.Add(index))references.Add(new ElementRef(ElementKind.Vertex,index));
                yield return new MapIssue("map-too-big","Размах карты превышает безопасные "+safeBoundary+" единиц: X "+(maxX-minX).ToString("0.###",System.Globalization.CultureInfo.InvariantCulture)+" · Y "+(maxY-minY).ToString("0.###",System.Globalization.CultureInfo.InvariantCulture),false,new MapPoint((float)((minX+maxX)/2),(float)((minY+maxY)/2)),references.ToArray());
            }
            for(int i=0;i<map.lines.Count;i++)
            {
                var line=map.lines[i];incident[line.start].Add(i);if(line.start!=line.end)incident[line.end].Add(i);
                for(int role=0;role<2;role++)
                {
                    bool front=role==0;int index=front?line.front:line.back;if(index<0)continue;var boundary=sectors[map.sides[index].sector];boundary.sideCount++;boundary.lines.Add(i);
                    boundary.Point(map,line.start,front?1:2);boundary.Point(map,line.end,front?2:1);
                }
                if((i&127)==0)yield return null;
            }
            for(int sector=0;sector<sectors.Length;sector++)
            {
                var boundary=sectors[sector];var holes=new List<int>();var seen=new HashSet<int>();int steps=0;
                foreach(var vertex in boundary.directions)
                {
                    if(vertex.Value!=3)
                    {
                        if(seen.Add(vertex.Key))holes.Add(vertex.Key);
                        foreach(int index in incident[vertex.Key])
                        {
                            var line=map.lines[index];if(!References(map,line,sector))
                            {
                                int value;if(boundary.directions.TryGetValue(line.start,out value)&&value==3&&seen.Add(line.start))holes.Add(line.start);
                                if(boundary.directions.TryGetValue(line.end,out value)&&value==3&&seen.Add(line.end))holes.Add(line.end);
                            }
                            if((++steps&127)==0)yield return null;
                        }
                    }
                    if((++steps&127)==0)yield return null;
                }
                MapPoint position=boundary.directions.Count==0?new MapPoint(0,0):new MapPoint((float)((boundary.minX+boundary.maxX)/2),(float)((boundary.minY+boundary.maxY)/2));
                if(holes.Count>0)
                {
                    position=map.vertices[holes[0]].Position;var references=new List<ElementRef>{new ElementRef(ElementKind.Sector,sector)};foreach(int index in holes)references.Add(new ElementRef(ElementKind.Vertex,index));
                    yield return new MapIssue("open-sector","Сектор #"+sector+": незамкнутая граница · вершин разрыва: "+holes.Count,true,position,references.ToArray());
                }
                else if(boundary.sideCount<3||(float)(boundary.maxX-boundary.minX)<=0||(float)(boundary.maxY-boundary.minY)<=0||boundary.lines.Count<3)
                    yield return new MapIssue("empty-sector","Сектор #"+sector+": недостаточно сторон / стен либо пустая область",true,position,new ElementRef(ElementKind.Sector,sector));
                yield return null;
            }
        }
        static bool References(MapDocument map,MapLine line,int sector)
        {return (line.front>=0&&map.sides[line.front].sector==sector)||(line.back>=0&&map.sides[line.back].sector==sector);}
    }
}

