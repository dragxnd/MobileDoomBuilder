// GPL-3.0. UDB block grid, selected cell and its linedefs/things; no labels or editing handles.
using MobileUDB.Core;
using UnityEngine;
using UnityEngine.UI;
namespace MobileUDB
{
    public sealed partial class MapViewport
    {
        void PopulateBlockmap(VertexHelper mesh)
        {
            editor.RefreshBlockmapData();var data=editor.BlockmapData;var map=editor.Map;var r=rectTransform.rect;
            var highlighted=data.GetCell(editor.BlockmapX,editor.BlockmapY);
            var cyan=new Color32(80,220,255,255);var orange=new Color32(255,170,48,210);
            if(editor.BlockmapX>=0)
            {
                var a=Project(new MapPoint(data.OriginX+editor.BlockmapX*128,data.OriginY+editor.BlockmapY*128));var b=a+Vector2.one*(128*zoom);
                Quad(mesh,a,b,new Color32(35,88,110,105));
            }
            // Cull grid rows/columns before emitting the mesh, including at minimum zoom.
            int left=Mathf.Clamp(Mathf.CeilToInt((center.x+r.xMin/zoom-data.OriginX)/128),0,data.Width);
            int right=Mathf.Clamp(Mathf.FloorToInt((center.x+r.xMax/zoom-data.OriginX)/128),0,data.Width);
            int bottom=Mathf.Clamp(Mathf.CeilToInt((center.y+r.yMin/zoom-data.OriginY)/128),0,data.Height);
            int top=Mathf.Clamp(Mathf.FloorToInt((center.y+r.yMax/zoom-data.OriginY)/128),0,data.Height);
            for(int x=left;x<=right&&mesh.currentVertCount<55000;x++)Line(mesh,Project(new MapPoint(data.OriginX+x*128,data.OriginY)),Project(new MapPoint(data.OriginX+x*128,data.OriginY+data.Height*128)),1.5f,orange);
            for(int y=bottom;y<=top&&mesh.currentVertCount<55000;y++)Line(mesh,Project(new MapPoint(data.OriginX,data.OriginY+y*128)),Project(new MapPoint(data.OriginX+data.Width*128,data.OriginY+y*128)),1.5f,orange);
            for(int i=0;i<map.lines.Count&&mesh.currentVertCount<59000;i++)
            {
                var l=map.lines[i];var a=Project(map.vertices[l.start].Position);var b=Project(map.vertices[l.end].Position);if(!Visible(a,b,r))continue;
                bool selected=highlighted!=null&&highlighted.Lines.Contains(i);
                Line(mesh,a,b,selected?3:1.5f,selected?cyan:new Color32(150,174,190,255));
            }
            foreach(var v in map.vertices)
            {var p=Project(v.Position);if(r.Contains(p)&&mesh.currentVertCount<59000)Quad(mesh,p-Vector2.one*1.5f,p+Vector2.one*1.5f,new Color32(190,210,220,255));}
            for(int i=0;editor.ThingsVisible&&i<map.things.Count&&mesh.currentVertCount<59000;i++)
            {
                var t=map.things[i];var p=Project(t.Position);if(!r.Contains(p))continue;
                bool selected=highlighted!=null&&highlighted.Things.Contains(i);var color=selected?cyan:new Color32(120,155,125,255);
                float radius=Mathf.Clamp(12*zoom,2,20);Ring(mesh,p,radius,selected?2.5f:1.5f,color);
                var direction=new Vector2(Mathf.Cos(t.angle*Mathf.Deg2Rad),Mathf.Sin(t.angle*Mathf.Deg2Rad));Line(mesh,p,p+direction*radius,selected?2:1.5f,color);
            }
            if(editor.BlockmapX>=0&&mesh.currentVertCount<59000)
            {
                var a=Project(new MapPoint(data.OriginX+editor.BlockmapX*128,data.OriginY+editor.BlockmapY*128));var b=a+Vector2.one*(128*zoom);
                Line(mesh,a,new Vector2(b.x,a.y),2.5f,cyan);Line(mesh,new Vector2(b.x,a.y),b,2.5f,cyan);Line(mesh,b,new Vector2(a.x,b.y),2.5f,cyan);Line(mesh,new Vector2(a.x,b.y),a,2.5f,cyan);
            }
        }
    }
}

