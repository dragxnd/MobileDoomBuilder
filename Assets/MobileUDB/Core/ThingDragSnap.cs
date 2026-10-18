// GPL-3.0. Binary DragThingsMode and MapSet.NearestThingSquareRange semantics.
using System;
using System.Collections.Generic;

namespace MobileUDB.Core
{
    public sealed class ThingDragSnap
    {
        struct Candidate { public MapPoint point;public float size;public bool fixedSize; }
        readonly List<Candidate> candidates=new List<Candidate>();
        readonly GridFrame grid;
        readonly MapPoint pointer,anchor;
        float minX=float.MaxValue,minY=float.MaxValue,maxX=float.MinValue,maxY=float.MinValue;
        public ThingDragSnap(MapDocument map,HashSet<ElementRef> selection,MapPoint pointer,GridFrame frame,ThingCatalog catalog)
        {
            ThingClipboard.Binary(map);if(catalog==null)throw new ArgumentNullException("catalog");
            this.pointer=pointer;grid=frame.DragSettings();var selected=new HashSet<int>();
            foreach(var item in selection)if(item.kind==ElementKind.Thing)selected.Add(item.index);
            if(selected.Count==0)throw new InvalidOperationException("Выберите объекты для перемещения.");
            // Sorted map iteration keeps native first-item ties stable, and all
            // moving identities are excluded from the stationary query.
            double nearest=double.MaxValue;MapPoint closest=pointer;
            for(int i=0;i<map.things.Count;i++)
            {
                var thing=map.things[i];
                if(selected.Contains(i))
                {
                    double x=thing.x-(double)pointer.x,y=thing.y-(double)pointer.y,d=x*x+y*y;
                    if(d<nearest){nearest=d;closest=thing.Position;}
                    minX=Math.Min(minX,thing.x);maxX=Math.Max(maxX,thing.x);minY=Math.Min(minY,thing.y);maxY=Math.Max(maxY,thing.y);
                }
                else {var type=catalog.Find(thing.type);candidates.Add(new Candidate{point=thing.Position,size=ThingPlacement.Radius(thing,type),fixedSize=type.fixedSize});}
            }
            anchor=closest;
        }
        public static double DisplayRadius(double size,bool fixedSize,double scale,bool fixedScale)
        {
            if(double.IsNaN(size)||double.IsInfinity(size)||size<0||double.IsNaN(scale)||double.IsInfinity(scale)||scale<=0)throw new InvalidOperationException("Некорректный размер объекта или масштаб.");
            if(fixedSize&&scale>1)return size/scale;
            if(fixedScale&&size*scale>48)return 48/scale;
            return size;
        }
        public MapPoint Offset(MapPoint delta,double scale)
        {
            if(float.IsNaN(delta.x)||float.IsNaN(delta.y)||float.IsInfinity(delta.x)||float.IsInfinity(delta.y)||double.IsNaN(scale)||double.IsInfinity(scale)||scale<=0)throw new InvalidOperationException("Некорректное перемещение объекта.");
            MapPoint? target=null;
            if(!grid.disableNearbyThings)
            {
                // Native things use the cursor, not the selected anchor, for
                // candidate lookup. The group then moves its anchor to that center.
                double x=pointer.x+(double)delta.x,y=pointer.y+(double)delta.y,range=20/scale;
                float left=(float)(x-range),right=(float)(x+range),bottom=(float)(y-range),top=(float)(y+range);
                double best=double.MaxValue,bestSize=double.MaxValue;
                foreach(var candidate in candidates)
                {
                    double size=DisplayRadius(candidate.size,candidate.fixedSize,scale,grid.fixedThingScale);
                    if(candidate.point.x<left-size||candidate.point.x>right+size||candidate.point.y<bottom-size||candidate.point.y>top+size)continue;
                    double distance=Math.Abs(candidate.point.x-x)+Math.Abs(candidate.point.y-y);
                    if(distance<best||(distance==best&&size<bestSize)){best=distance;bestSize=size;target=candidate.point;}
                }
            }
            // Nearby things use the original cursor and can override cardinal
            // movement, exactly as native MoveThingsRelative does after rotation.
            delta=grid.ConstrainDragDelta(delta);
            var offset=target.HasValue?target.Value-anchor:grid.MoveOffset(anchor,delta);
            offset.x=Math.Max(-32768-minX,Math.Min(32767-maxX,offset.x));offset.y=Math.Max(-32768-minY,Math.Min(32767-maxY,offset.y));return offset;
        }
    }
}
