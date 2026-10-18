// GPL-3.0. GridSetup.SnappedToGrid and MainForm grid transform actions, adapted
// from Ultimate Doom Builder (Pascal vd Heiden and contributors).
using System;
using System.Collections.Generic;

namespace MobileUDB.Core
{
    [Serializable] public sealed class MapGridFrame
    {public string name;public GridFrame grid;}
    [Serializable] public sealed class GridFrame
    {
        public double step=32;
        public double rotation,originX,originY;
        public bool snap=true,increment;
        // Missing legacy JSON fields default to false, retaining enabled native
        // geometry snapping. This mobile preference is not written into DBS.
        public bool disableNearbySnap;
        // Mobile default: drag things on the grid without pulling into neighbours.
        // The marker distinguishes a new explicit choice from legacy auto-enabled settings.
        public bool disableNearbyThings=true;
        public bool nearbyThingsPreferenceSet;
        public bool fixedThingScale;
        public bool cardinalDrag;
        // Preserve the earlier mobile port's manual grid in legacy projects.
        public bool dynamicGrid;
        public GridFrame Copy(){return (GridFrame)MemberwiseClone();}
        public GridFrame Validated(){var copy=Copy();copy.Validate();return copy;}
        // Native Alt+Shift forces grid and relative increments for this drag only.
        public GridFrame DragSettings()
        {var copy=Validated();if(copy.cardinalDrag){copy.snap=true;copy.increment=true;}return copy;}
        public MapPoint ConstrainDragDelta(MapPoint delta)
        {
            if(!Finite(delta.x)||!Finite(delta.y))throw new InvalidOperationException("Некорректное перемещение.");
            if(!cardinalDrag)return delta;
            // Keep native +44/truncate angle thresholds and radial length; this
            // is not a dominant-coordinate projection. Nearby snap runs later.
            double angle=-Math.Atan2(-delta.y,delta.x)+Math.PI*.5;
            if(angle<0)angle+=Math.PI*2;
            int degrees=((int)(angle*(180.0/Math.PI))+44)%360;
            double snapped=(degrees/90*90)/(180.0/Math.PI);
            double length=Math.Sqrt(delta.x*(double)delta.x+delta.y*(double)delta.y);
            return new MapPoint((float)(length*Math.Sin(snapped)),(float)(-length*Math.Cos(snapped)));
        }
        public void Validate()
        {
            if(!Finite(step)||step<1||step>65536||!Finite(rotation)||!Finite(originX)||!Finite(originY)||originX< -32768||originX>32767||originY< -32768||originY>32767)
                throw new InvalidOperationException("Шаг сетки: 1–65536; начало: −32768…32767; угол должен быть конечным.");
            if(!nearbyThingsPreferenceSet){disableNearbyThings=true;nearbyThingsPreferenceSet=true;}
        }
        static bool Finite(double n){return !double.IsNaN(n)&&!double.IsInfinity(n);}
        public static double DynamicStep(double width,double height,double scale)
        {
            if(!Finite(width)||!Finite(height)||!Finite(scale)||width<=0||height<=0||scale<=0)throw new InvalidOperationException("Некорректный размер обзора или масштаб сетки.");
            // ClassicMode.MatchGridSizeToDisplayScale: aim for32 lines on
            // the shorter viewport side, round up to power-of-two, binary min1.
            double target=Math.Ceiling(Math.Min(width,height)/scale/4);
            if(target>=524288)return 65536; // Existing mobile grid-size limit.
            int size=(int)target;size--;size|=size>>1;size|=size>>2;size|=size>>4;size|=size>>8;size|=size>>16;size++;
            return Math.Max(1,size/8f);
        }
        public double DisplayStep(double scale)
        {
            Validate();if(!Finite(scale)||scale<=0)throw new InvalidOperationException("Некорректный масштаб сетки.");
            double size=step;if(!dynamicGrid)while(size*scale<=6)size*=2;
            return size;
        }
        public bool Transformed {get{return Math.Abs(rotation)>1e-4||originX!=0||originY!=0;}}
        public CurvePoint Local(CurvePoint point)
        {return Transformed?(point-new CurvePoint(originX,originY)).GetRotated(-rotation):point;}
        public CurvePoint World(CurvePoint point)
        {return Transformed?point.GetRotated(rotation)+new CurvePoint(originX,originY):point;}
        public CurvePoint SnapRaw(CurvePoint point)
        {
            Validate();if(!Finite(point.x)||!Finite(point.y))throw new InvalidOperationException("Некорректная точка привязки.");
            var local=Local(point);double inverse=1f/(double)step;
            var result=World(new CurvePoint(Math.Round(local.x*inverse)*step,Math.Round(local.y*inverse)*step));
            return new CurvePoint(Math.Max(-32768,Math.Min(32767,result.x)),Math.Max(-32768,Math.Min(32767,result.y)));
        }
        public MapPoint Snap(MapPoint point)
        {
            var result=SnapRaw(new CurvePoint(point.x,point.y));return new MapPoint((float)Math.Round(result.x),(float)Math.Round(result.y));
        }
        public MapPoint MoveOffset(MapPoint anchor,MapPoint delta)
        {
            if(!snap)return MapDocument.Snap(delta,1);
            var a=new CurvePoint(anchor.x,anchor.y);var target=a+new CurvePoint(delta.x,delta.y);
            var result=SnapRaw(target);if(increment)result-=SnapRaw(a)-a;
            return new MapPoint((float)(Math.Round(result.x)-anchor.x),(float)(Math.Round(result.y)-anchor.y));
        }
        public void Align(MapDocument map,int lineIndex)
        {
            ThingClipboard.Binary(map);map.Validate();if(lineIndex<0||lineIndex>=map.lines.Count)throw new InvalidOperationException("Выберите одну существующую стену.");
            var line=map.lines[lineIndex];var a=map.vertices[line.start];var b=map.vertices[line.end];double dx=b.x-(double)a.x,dy=b.y-(double)a.y;
            if(dx==0&&dy==0)throw new InvalidOperationException("Стена имеет нулевую длину.");
            double angle=-Math.Atan2(-dy,dx)+Math.PI*.5;if(angle<0)angle+=Math.PI*2;
            rotation=angle;originX=a.x;originY=a.y;Validate();
        }
        public MapPoint NearestAnchor(MapDocument map,IEnumerable<ElementRef> selection,MapPoint point)
        {
            var selected=new HashSet<ElementRef>(selection);var vertices=map.SelectedVertices(selected);double nearest=double.MaxValue;MapPoint anchor=point;bool found=false;
            // Source geometry drag anchors at the nearest selected vertex; thing-only
            // drag anchors at the nearest selected thing, not the pointer position.
            int nearestIndex=int.MaxValue;
            foreach(int index in vertices){var p=map.vertices[index].Position;double dx=p.x-(double)point.x,dy=p.y-(double)point.y,distance=dx*dx+dy*dy;if(distance<nearest||(distance==nearest&&index<nearestIndex)){nearest=distance;nearestIndex=index;anchor=p;found=true;}}
            if(!found)foreach(var item in selected)if(item.kind==ElementKind.Thing)
            {var p=map.things[item.index].Position;double dx=p.x-(double)point.x,dy=p.y-(double)point.y,distance=dx*dx+dy*dy;if(distance<nearest||(distance==nearest&&item.index<nearestIndex)){nearest=distance;nearestIndex=item.index;anchor=p;}}
            return anchor;
        }
    }
}
