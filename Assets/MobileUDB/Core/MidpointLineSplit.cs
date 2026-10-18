// GPL-3.0. LinedefsMode.SplitLinedefs midpoint branch / Linedef.Split.
using System;using System.Collections.Generic;
namespace MobileUDB.Core
{
    public static class MidpointLineSplit
    {
        public static LineSplitPlan Prepare(MapDocument map,IEnumerable<int> selected,CurveSplitOffsets offsets=CurveSplitOffsets.Interpolate,DoomResources resources=null)
        {
            ThingClipboard.Binary(map);if(selected==null)throw new ArgumentNullException("selected");
            if(!Enum.IsDefined(typeof(CurveSplitOffsets),offsets))throw new InvalidOperationException("Неизвестный режим смещений текстур.");
            var order=new List<int>();var seen=new HashSet<int>();foreach(int index in selected)
            {if(index<0||index>=map.lines.Count)throw new InvalidOperationException("Выбранная стена больше не существует.");if(seen.Add(index))order.Add(index);}
            if(order.Count==0)throw new InvalidOperationException("Выделите стены для разделения.");map.Validate();
            if((long)map.vertices.Count+order.Count>65535||(long)map.lines.Count+order.Count>65535)throw new InvalidOperationException("Превышен лимит бинарных вершин или стен.");
            var result=map.Clone();var uses=new int[result.sides.Count];foreach(var line in result.lines){if(line.front>=0)uses[line.front]++;if(line.back>=0)uses[line.back]++;}
            var parts=new List<ElementRef>();foreach(int index in order)parts.Add(new ElementRef(ElementKind.Line,index));
            int degenerate=0;var options=new CurveLinedefOptions{offsets=offsets};string sky=GameSky.FlatName(map);
            foreach(int index in order)
            {
                var line=result.lines[index];var a=result.vertices[line.start].Position;var b=result.vertices[line.end].Position;
                var point=new MapPoint((float)Math.Round((double)a.x+((double)b.x-a.x)*.5),(float)Math.Round((double)a.y+((double)b.y-a.y)*.5));
                if(point.x< -32768||point.x>32767||point.y< -32768||point.y>32767)throw new InvalidOperationException("Точка разделения вне бинарных координат.");
                // Native CreateVertex creates a fresh identity even at an existing
                // point. It does not stitch the midpoint with nearby geometry.
                int vertex=result.vertices.Count;result.vertices.Add(new MapVertex(point.x,point.y));
                Isolate(result,ref line.front,uses);Isolate(result,ref line.back,uses);
                CurveLinedefs.Split(result,index,vertex,new CurvePoint(a.x,a.y),new CurvePoint(point.x,point.y),new CurvePoint(b.x,b.y),options,resources,sky);
                var next=result.lines[result.lines.Count-1];parts.Add(new ElementRef(ElementKind.Line,result.lines.Count-1));
                if((point-a).LengthSquared==0)degenerate++;if((point-b).LengthSquared==0)degenerate++;
                // Match native Int16 WAD offset representation on touched sides
                // only; never normalize unrelated unused sidedef records.
                foreach(int side in new[]{line.front,line.back,next.front,next.back})if(side>=0){result.sides[side].offsetX=unchecked((short)result.sides[side].offsetX);result.sides[side].offsetY=unchecked((short)result.sides[side].offsetY);}
            }
            if(result.sides.Count>65535)throw new InvalidOperationException("Превышен лимит бинарных сторон.");
            result.Validate();return new LineSplitPlan(map,result,parts,order.Count,degenerate);
        }
        static void Isolate(MapDocument map,ref int index,int[] uses)
        {
            if(index<0||uses[index]<=1)return;uses[index]--;var old=map.sides[index];var copy=new MapSide{sector=old.sector,offsetX=old.offsetX,offsetY=old.offsetY,upper=old.upper,middle=old.middle,lower=old.lower};old.CopyFieldsTo(copy);index=map.sides.Count;map.sides.Add(copy);
        }
    }
    public sealed class LineSplitPlan
    {
        readonly MapDocument source,prepared;readonly byte[] state;readonly List<ElementRef> parts;bool used;
        public int LineCount {get;private set;}public int DegenerateCount {get;private set;}
        internal LineSplitPlan(MapDocument source,MapDocument prepared,List<ElementRef> parts,int count,int degenerate)
        {this.source=source;this.prepared=prepared;this.parts=parts;state=BinaryMapState.Capture(source);LineCount=count;DegenerateCount=degenerate;}
        public List<ElementRef> Apply(MapDocument map)
        {
            if(used||!ReferenceEquals(map,source))throw new InvalidOperationException("Карта изменилась или разделение уже применено.");var current=BinaryMapState.Capture(map);
            if(current.Length!=state.Length)throw new InvalidOperationException("Карта изменилась. Откройте разделение снова.");for(int i=0;i<state.Length;i++)if(current[i]!=state[i])throw new InvalidOperationException("Карта изменилась. Откройте разделение снова.");
            map.vertices=prepared.vertices;map.lines=prepared.lines;map.sides=prepared.sides;map.sectors=prepared.sectors;used=true;return new List<ElementRef>(parts);
        }
    }
}
