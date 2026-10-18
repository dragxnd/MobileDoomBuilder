using System;
using System.Collections.Generic;
using MobileUDB.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MobileUDB
{
    // One batched UI mesh, independent of the project's render pipeline.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed partial class MapViewport : MaskableGraphic
    {
        public MobileEditor editor;
        public Vector2 center;
        public float zoom = 0.8f;
        public readonly List<Vector2> fingers = new List<Vector2>();
        public Rect ScreenRect
        {
            get
            {
                Vector3[] corners = new Vector3[4]; rectTransform.GetWorldCorners(corners);
                return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
            }
        }
        public Vector2 Local(Vector2 screen)
        {
            Vector2 local; RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, screen, null, out local); return local;
        }
        public MapPoint World(Vector2 screen)
        {
            Vector2 p = Local(screen) / zoom + center;
            return new MapPoint(p.x, p.y);
        }
        public Vector2 Project(MapPoint p) { return (new Vector2(p.x, p.y) - center) * zoom; }
        public void Pan(Vector2 previous, Vector2 current) { center -= (Local(current) - Local(previous)) / zoom; SetVerticesDirty(); }
        public void ZoomAt(Vector2 position, float factor)
        {
            MapPoint anchor = World(position); zoom = Mathf.Clamp(zoom * factor, 0.025f, 20);
            Vector2 p = Local(position); center = new Vector2(anchor.x, anchor.y) - p / zoom; SetVerticesDirty();
            editor.MatchDynamicGrid();
        }
        public void Gesture(Vector2 oldA, Vector2 oldB, Vector2 a, Vector2 b)
        {
            float distance = Vector2.Distance(oldA, oldB);
            Vector2 oldMid = (oldA + oldB) * 0.5f, mid = (a + b) * 0.5f;
            Pan(oldMid, mid);
            if (distance > 8) ZoomAt(mid, Vector2.Distance(a, b) / distance);
        }
        public void Fit()
        {
            if (editor == null || editor.Map.vertices.Count == 0) { center = Vector2.zero; zoom = 0.8f;if(editor!=null)editor.MatchDynamicGrid();return; }
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
            foreach (var v in editor.Map.vertices) { min = Vector2.Min(min, new Vector2(v.x, v.y)); max = Vector2.Max(max, new Vector2(v.x, v.y)); }
            center = (min + max) * 0.5f;
            zoom = Mathf.Clamp(Mathf.Min(rectTransform.rect.width / Mathf.Max(128, max.x - min.x + 128), rectTransform.rect.height / Mathf.Max(128, max.y - min.y + 128)), 0.025f, 20);
            editor.MatchDynamicGrid();
            SetVerticesDirty();
        }
        internal void PopulateOverlay(VertexHelper vh)
        {
            vh.Clear(); if (editor == null || editor.Map == null) return;
            Rect r = rectTransform.rect;
            if(editor.BlockmapActive){PopulateBlockmap(vh);return;}
            if(editor.SoundPropagationActive){PopulateSoundPropagation(vh);return;}
            if(editor.AutomapActive){PopulateAutomap(vh);return;}
            var associations=editor.Associations;
            foreach(var triangle in associations.sectorFill)
            {if(vh.currentVertCount>55000)break;int first=vh.currentVertCount;Color tint=new Color32(88,199,247,42);vh.AddVert(Project(triangle.a),tint,Vector2.zero);vh.AddVert(Project(triangle.b),tint,Vector2.zero);vh.AddVert(Project(triangle.c),tint,Vector2.zero);vh.AddTriangle(first,first+1,first+2);}

            RenderGrid(vh,r);
            var map = editor.Map;
            for (int i = 0; i < map.lines.Count; i++)
            {
                var l = map.lines[i]; Vector2 a = Project(editor.TransformVertexPosition(l.start)), b = Project(editor.TransformVertexPosition(l.end));
                if (!Visible(a, b, r)) continue;
                bool selected = editor.Selection.Contains(new ElementRef(ElementKind.Line, i)) ||
                    (l.front >= 0 && editor.Selection.Contains(new ElementRef(ElementKind.Sector, map.sides[l.front].sector))) ||
                    (l.back >= 0 && editor.Selection.Contains(new ElementRef(ElementKind.Sector, map.sides[l.back].sector)));
                // Binary Doom/Hexen flag 1 blocks players. UDB draws these walls
                // at full brightness; a little extra width makes them clear on phones.
                bool blocked = l.front >= 0 && l.back >= 0 && (l.flags & 1) != 0;
                Color c = selected ? new Color32(255, 190, 70, 255) : l.back >= 0 && !blocked ? new Color32(112, 153, 175, 255) : new Color32(192, 218, 232, 255);
                if(l.action>0&&!selected)c=l.back>=0&&!blocked?new Color32(80,157,98,255):new Color32(116,220,130,255);
                bool related=associations.related.Contains(new ElementRef(ElementKind.Line,i))||(l.front>=0&&associations.related.Contains(new ElementRef(ElementKind.Sector,map.sides[l.front].sector)))||(l.back>=0&&associations.related.Contains(new ElementRef(ElementKind.Sector,map.sides[l.back].sector)));
                if(related&&!selected)c=new Color32(88,199,247,255);
                bool region=editor.MakeSectorPreviewLine(i);if(region)c=new Color32(104,217,247,255);
                Line(vh, a, b, selected||region||related ? 3 : blocked ? 2.6f : 1.8f, c);
                Vector2 mid = (a + b) / 2, d = b - a;
                if (d.magnitude > 18) Line(vh, mid, mid + new Vector2(d.y, -d.x).normalized * 8, 1, c);
            }
            if (editor.Kind == ElementKind.Vertex || zoom > 1.4f)
                for (int i = 0; i < map.vertices.Count && vh.currentVertCount < 58000; i++)
                {
                    Vector2 p = Project(editor.TransformVertexPosition(i)); if (!r.Contains(p)) continue;
                    bool selected = editor.Selection.Contains(new ElementRef(ElementKind.Vertex, i));
                    Quad(vh, p - Vector2.one * (selected ? 5 : 3), p + Vector2.one * (selected ? 5 : 3), selected ? new Color32(255, 190, 70, 255) : new Color32(86, 211, 192, 255));
                }
            else foreach(var id in editor.Selection)
                if(id.kind==ElementKind.Vertex&&id.index>=0&&id.index<map.vertices.Count&&vh.currentVertCount<58000)
                {Vector2 p=Project(editor.TransformVertexPosition(id.index));if(r.Contains(p))Quad(vh,p-Vector2.one*5,p+Vector2.one*5,new Color32(255,190,70,255));}
            int associationArrows=0;
            foreach(var edge in associations.edges)
            {
                if(!editor.ThingsVisible&&(edge.source.kind==ElementKind.Thing||edge.target.kind==ElementKind.Thing))continue;
                if(vh.currentVertCount>61000||associationArrows++>=256)break;
                var a=Project(associations.positions[edge.source]);var b=Project(associations.positions[edge.target]);var delta=b-a;float length=delta.magnitude;if(length<6||!Visible(a,b,r))continue;
                var direction=delta/length;var normal=new Vector2(-direction.y,direction.x);Color color=editor.Selection.Contains(edge.source)?new Color32(88,199,247,255):new Color32(185,140,255,255);
                Line(vh,a,b,1.6f,color);float head=Mathf.Min(9,length*.25f);var tip=Vector2.Lerp(a,b,.72f);Line(vh,tip,tip-direction*head+normal*head*.55f,2,color);Line(vh,tip,tip-direction*head-normal*head*.55f,2,color);
            }
            // Thing icons and their selection backplates are batched in separate layers.
            var aim=editor.ThingAimPoint;
            var curved=editor.LinedefCurvePreview;
            if(curved!=null)foreach(var curve in curved)
            {
                var previous=Project(new MapPoint((float)curve.start.x,(float)curve.start.y));Color color=new Color32(104,217,247,255);
                for(int i=0;i<=curve.points.Count&&vh.currentVertCount<62000;i++)
                {
                    var point=i==curve.points.Count?curve.end:curve.points[i];var next=Project(new MapPoint((float)point.x,(float)point.y));
                    if(Visible(previous,next,r))Line(vh,previous,next,2,color);if(i<curve.points.Count&&r.Contains(next))Quad(vh,next-Vector2.one*3,next+Vector2.one*3,color);previous=next;
                }
            }
            if(aim.HasValue){var target=Project(aim.Value);Color mark=new Color32(104,217,247,255);Ring(vh,target,10,2,mark);Line(vh,target-new Vector2(14,0),target+new Vector2(14,0),2,mark);Line(vh,target-new Vector2(0,14),target+new Vector2(0,14),2,mark);}
            var shape=editor.ShapePreview;
            var bridge=editor.BridgePreview;
            if(bridge!=null)
            {
                Color color=new Color32(104,217,247,255),bridgeHandleColor=new Color32(151,244,169,255);
                foreach(var curve in bridge.curves)for(int i=1;i<curve.Count&&vh.currentVertCount<60000;i++)
                {var a=Project(new MapPoint((float)curve[i-1].x,(float)curve[i-1].y));var b=Project(new MapPoint((float)curve[i].x,(float)curve[i].y));if(Visible(a,b,r))Line(vh,a,b,2,color);if(r.Contains(b))Quad(vh,b-Vector2.one*2,b+Vector2.one*2,color);}
                for(int row=1;row<bridge.curves.Count;row++)for(int column=1;column<bridge.subdivisions&&vh.currentVertCount<60000;column++)
                {var p=bridge.curves[row-1][column];var q=bridge.curves[row][column];var a=Project(new MapPoint((float)p.x,(float)p.y));var b=Project(new MapPoint((float)q.x,(float)q.y));if(Visible(a,b,r))Line(vh,a,b,2,color);}
                for(int i=0;i<4&&vh.currentVertCount<62000;i++)
                {var p=bridge.handles[i];var q=bridge.anchors[i];var a=Project(new MapPoint((float)p.x,(float)p.y));var b=Project(new MapPoint((float)q.x,(float)q.y));if(Visible(a,b,r))Line(vh,a,b,1,bridgeHandleColor);if(r.Contains(a)){Quad(vh,a-Vector2.one*8,a+Vector2.one*8,new Color32(22,33,48,255));Ring(vh,a,11,2,bridgeHandleColor);}}
            }
            var drawGrid=editor.GridDrawPreview;
            if(drawGrid!=null)
            {
                Color color=new Color32(104,217,247,255);
                foreach(var path in drawGrid.paths)for(int i=0;i<path.Count&&vh.currentVertCount<62000;i++)
                {
                    var p=Project(new MapPoint((float)path[i].x,(float)path[i].y));if(r.Contains(p))Quad(vh,p-Vector2.one*2,p+Vector2.one*2,color);
                    if(i>0){var a=Project(new MapPoint((float)path[i-1].x,(float)path[i-1].y));if(Visible(a,p,r))Line(vh,a,p,2,color);}
                }
                if(editor.ShapeGuidelines&&shape!=null&&shape.points.Count>=5&&vh.currentVertCount<63000)
                {
                    Color guide=new Color32(67,118,141,255);var a=Project(shape.points[0]);var b=Project(shape.points[1]);var c=Project(shape.points[2]);var d=Project(shape.points[3]);
                    Line(vh,(a+b)*.5f,(c+d)*.5f,1,guide);Line(vh,(a+d)*.5f,(b+c)*.5f,1,guide);
                }
            }
            else if(shape!=null)
            {
                Color color=new Color32(104,217,247,255);
                for(int i=0;i<shape.points.Count&&vh.currentVertCount<63000;i++)
                {
                    var p=Project(shape.points[i]);if(r.Contains(p))Quad(vh,p-Vector2.one*3,p+Vector2.one*3,color);
                    if(!editor.ShapePlacesThings&&(i>0||shape.closed))
                    {var a=Project(shape.points[i==0?shape.points.Count-1:i-1]);if(Visible(a,p,r))Line(vh,a,p,2,color);}
                }
                if(editor.ShapeGuidelines&&vh.currentVertCount<63000)
                {
                    var a=Project(shape.start);var b=Project(shape.end);var c=(a+b)*.5f;Color guide=new Color32(67,118,141,255);
                    Line(vh,a,new Vector2(a.x,b.y),1,guide);Line(vh,new Vector2(a.x,b.y),b,1,guide);Line(vh,b,new Vector2(b.x,a.y),1,guide);Line(vh,new Vector2(b.x,a.y),a,1,guide);
                    Line(vh,new Vector2(a.x,c.y),new Vector2(b.x,c.y),1,guide);Line(vh,new Vector2(c.x,a.y),new Vector2(c.x,b.y),1,guide);
                }
            }
            for (int i = 0; i < editor.Draft.Count; i++)
            {
                Vector2 p = Project(editor.Draft[i]); Ring(vh, p, 5, 2, new Color32(104, 217, 247, 255));
                if (i > 0&&!editor.DrawingShape) Line(vh, Project(editor.Draft[i - 1]), p, 2, new Color32(104, 217, 247, 255));
            }
            var drawingCursor=editor.DrawingCursor;
            if(drawingCursor.HasValue&&editor.Draft.Count>0)Line(vh,Project(editor.Draft[editor.Draft.Count-1]),Project(drawingCursor.Value),2,new Color32(104,217,247,255));
            if (editor.Tool == MobileTool.DrawRoom && !editor.DrawingChain && editor.Draft.Count > 2) Line(vh, Project(editor.Draft[editor.Draft.Count - 1]), Project(editor.Draft[0]), 1, new Color32(67, 118, 141, 255));
            var preview = editor.PastePreview;
            if (preview != null)
            {
                Color ghost = new Color32(104,217,247,255);
                foreach (var line in preview.lines)
                {
                    Vector2 a = Project(preview.vertices[line.start].Position), b = Project(preview.vertices[line.end].Position);
                    if (Visible(a,b,r)) Line(vh,a,b,3,ghost);
                }
                foreach (var vertex in preview.vertices) { Vector2 p = Project(vertex.Position); if (r.Contains(p)) Quad(vh,p - Vector2.one * 3,p + Vector2.one * 3,ghost); }
                if(editor.ThingsVisible)foreach (var thing in preview.things)
                {
                    Vector2 p = Project(thing.Position); if (!r.Contains(p)) continue;
                    Ring(vh,p,Mathf.Clamp(16 * zoom,6,22),2,ghost);
                    float angle = thing.angle * Mathf.Deg2Rad; Line(vh,p,p + new Vector2(Mathf.Cos(angle),Mathf.Sin(angle)) * 14,2,ghost);
                }
            }
            var copyAnchor=editor.DuplicatePlacementAnchor;
            if(copyAnchor.HasValue)
            {
                Vector2 p=Project(copyAnchor.Value);if(r.Contains(p))
                {Line(vh,p-new Vector2(7,0),p+new Vector2(7,0),2,Color.white);Line(vh,p-new Vector2(0,7),p+new Vector2(0,7),2,Color.white);}
            }
            var handles=editor.TransformHandlePositions();
            if(handles!=null)
            {
                Color color=new Color32(104,217,247,255);
                for(int i=0;i<8;i+=2)if(Visible(handles[i],handles[(i+2)%8],r))Line(vh,handles[i],handles[(i+2)%8],1.5f,color);
                Line(vh,handles[5],handles[8],1.5f,color);
                for(int i=0;i<9;i++)if(r.Contains(handles[i])&&editor.TransformHandleEnabled(i))
                {
                    if(i==8){Ring(vh,handles[i],12,3,color);Line(vh,handles[i]+new Vector2(8,8),handles[i]+new Vector2(15,8),2,color);}
                    else {Quad(vh,handles[i]-Vector2.one*10,handles[i]+Vector2.one*10,color);Quad(vh,handles[i]-Vector2.one*6,handles[i]+Vector2.one*6,new Color32(22,33,48,255));}
                }
            }
            if(editor.AreaSelectionVisible)
            {
                Vector2 a=Project(editor.AreaSelectionStart),b=Project(editor.AreaSelectionEnd);
                var lo=Vector2.Min(a,b);var hi=Vector2.Max(a,b);
                Quad(vh,lo,hi,new Color32(104,217,247,30));
                var color=new Color32(104,217,247,255);var br=new Vector2(hi.x,lo.y);var tl=new Vector2(lo.x,hi.y);
                Line(vh,lo,br,2,color);Line(vh,br,hi,2,color);Line(vh,hi,tl,2,color);Line(vh,tl,lo,2,color);
            }
            foreach (Vector2 screen in fingers) { Vector2 p = Local(screen); Ring(vh, p, 18, 2, new Color32(105, 225, 255, 255)); Line(vh, p - Vector2.right * 24, p + Vector2.right * 24, 1, Color.cyan); }
        }
        void RenderGrid(VertexHelper vh,Rect rect)
        {
            var grid=editor.GridSettings;double step=grid.DisplayStep(zoom);
            double minX=double.MaxValue,minY=double.MaxValue,maxX=double.MinValue,maxY=double.MinValue;
            foreach(var corner in new[]{new Vector2(rect.xMin,rect.yMin),new Vector2(rect.xMax,rect.yMin),new Vector2(rect.xMax,rect.yMax),new Vector2(rect.xMin,rect.yMax)})
            {var point=grid.Local(new CurvePoint(center.x+corner.x/zoom,center.y+corner.y/zoom));minX=Math.Min(minX,point.x);maxX=Math.Max(maxX,point.x);minY=Math.Min(minY,point.y);maxY=Math.Max(maxY,point.y);}
            for(double x=Math.Ceiling(minX/step)*step;x<=maxX;x+=step)GridLine(vh,grid,new CurvePoint(x,minY),new CurvePoint(x,maxY),x==0);
            for(double y=Math.Ceiling(minY/step)*step;y<=maxY;y+=step)GridLine(vh,grid,new CurvePoint(minX,y),new CurvePoint(maxX,y),y==0);
        }
        void GridLine(VertexHelper vh,GridFrame grid,CurvePoint a,CurvePoint b,bool axis)
        {
            // Native grid pixels are one pixel wide. Subpixel UI lines can vanish
            // completely at integer zoom/positions on a non-MSAA canvas.
            float width=Mathf.Max(1,canvas==null?1:1/Mathf.Max(.01f,canvas.scaleFactor));
            a=grid.World(a);b=grid.World(b);Line(vh,Project(new MapPoint((float)a.x,(float)a.y)),Project(new MapPoint((float)b.x,(float)b.y)),width,axis?new Color32(53,81,106,255):new Color32(30,43,59,255));
        }
        static bool Visible(Vector2 a, Vector2 b, Rect r) { return Mathf.Max(a.x, b.x) >= r.xMin && Mathf.Min(a.x, b.x) <= r.xMax && Mathf.Max(a.y, b.y) >= r.yMin && Mathf.Min(a.y, b.y) <= r.yMax; }
        static void Quad(VertexHelper vh, Vector2 a, Vector2 b, Color c)
        {
            int n = vh.currentVertCount; if (n > 60000) return;
            vh.AddVert(new Vector3(a.x, a.y), c, Vector2.zero); vh.AddVert(new Vector3(a.x, b.y), c, Vector2.zero);
            vh.AddVert(new Vector3(b.x, b.y), c, Vector2.zero); vh.AddVert(new Vector3(b.x, a.y), c, Vector2.zero);
            vh.AddTriangle(n, n + 1, n + 2); vh.AddTriangle(n, n + 2, n + 3);
        }
        static void Line(VertexHelper vh, Vector2 a, Vector2 b, float width, Color c)
        {
            int n = vh.currentVertCount; if (n > 60000) return;
            Vector2 normal = new Vector2(-(b - a).y, (b - a).x).normalized * width * 0.5f;
            vh.AddVert(a - normal, c, Vector2.zero); vh.AddVert(a + normal, c, Vector2.zero); vh.AddVert(b + normal, c, Vector2.zero); vh.AddVert(b - normal, c, Vector2.zero);
            vh.AddTriangle(n, n + 1, n + 2); vh.AddTriangle(n, n + 2, n + 3);
        }
        static void Ring(VertexHelper vh, Vector2 p, float radius, float width, Color c)
        {
            for (int i = 0; i < 16; i++) { float a = i * Mathf.PI / 8, b = (i + 1) * Mathf.PI / 8; Line(vh, p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius, p + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * radius, width, c); }
        }
    }
}



