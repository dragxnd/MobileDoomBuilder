// Original Ultimate Doom Builder toolbar artwork. GPL-3.0; see LICENSE.txt.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MobileUDB
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class EditorIcon : MaskableGraphic
    {
        public enum Symbol { Vertex, Wall, Sector, Thing, Select, Pan, Move, Room, AddThing, Cut, Undo, Redo, Properties, Delete, More, File, Fit, Visual, Floor, Ceiling, Texture, Up, Down, Copy, Paste, FlipX, FlipY, Rectangle, Ellipse, Curve, Similar, Group, GroupAdd, GroupRemove, Door, BrightnessGradient, FloorsGradient, CeilsGradient, ThingPoint, MakeSector, WallFlip, CurveLines, Grid, GridIncrease, GridDecrease, DrawGrid, Bridge, FilterThings, PlaceThings, Merge, Join, FindMode, Check, Help, Zoom, SaveMap, OpenMap, NewMap, Configuration, ViewNormal, ViewBrightness, ViewTextureFloor, ViewTextureCeiling, GridDynamic, UpperUnpegged, LowerUnpegged, Overflow, Automap, SoundPropagation, Statistics, Blockmap, DisplayLighting }
        public Symbol symbol;
        static readonly Dictionary<Symbol, Texture2D> textures = new Dictionary<Symbol, Texture2D>();
        public override Texture mainTexture
        {
            get
            {
                if (symbol == Symbol.Pan || symbol == Symbol.UpperUnpegged || symbol == Symbol.LowerUnpegged || symbol == Symbol.Overflow || symbol == Symbol.Statistics) return Texture2D.whiteTexture;
                Texture2D texture;
                if (!textures.TryGetValue(symbol, out texture))
                {
                    texture = Resources.Load<Texture2D>("MobileUDB/Icons/" + symbol);
                    if (texture != null) { texture.filterMode = FilterMode.Point; texture.wrapMode = TextureWrapMode.Clamp; }
                    textures[symbol] = texture;
                }
                return texture == null ? Texture2D.whiteTexture : texture;
            }
        }
        void PanArrows(VertexHelper mesh, Rect r)
        {
            var c = r.center; float radius = Mathf.Min(r.width,r.height)*.44f;
            for(int i=0;i<4;i++) { var d = new Vector2(Mathf.Cos(i*Mathf.PI/2),Mathf.Sin(i*Mathf.PI/2)); var side = new Vector2(-d.y,d.x);
                Stroke(mesh,c,c+d*radius,2.2f); Stroke(mesh,c+d*radius,c+d*(radius-6)+side*5,2.2f); Stroke(mesh,c+d*radius,c+d*(radius-6)-side*5,2.2f); }
        }
        void Stroke(VertexHelper mesh,Vector2 a,Vector2 b,float width)
        {
            var offset = new Vector2(a.y-b.y,b.x-a.x).normalized*width*.5f; int n=mesh.currentVertCount;
            mesh.AddVert(a-offset,color,Vector2.zero);mesh.AddVert(a+offset,color,Vector2.zero);mesh.AddVert(b+offset,color,Vector2.zero);mesh.AddVert(b-offset,color,Vector2.zero);mesh.AddTriangle(n,n+1,n+2);mesh.AddTriangle(n,n+2,n+3);
        }
        void UnpeggedArrow(VertexHelper mesh,Rect r)
        {
            float direction=symbol==Symbol.UpperUnpegged?1:-1;var c=r.center;float radius=r.height*.4f;
            var edge=c+new Vector2(0,direction*radius);Stroke(mesh,edge+new Vector2(-r.width*.32f,0),edge+new Vector2(r.width*.32f,0),2.2f);
            var tip=edge-new Vector2(0,direction*4);Stroke(mesh,c-new Vector2(0,direction*radius),tip,2.2f);
            Stroke(mesh,tip,tip+new Vector2(-5,-direction*6),2.2f);Stroke(mesh,tip,tip+new Vector2(5,-direction*6),2.2f);
        }
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear(); var r = rectTransform.rect;
            if (symbol == Symbol.Statistics)
            {
                float bottom=r.yMin+r.height*.22f;
                for(int i=0;i<3;i++)
                {
                    float x=r.xMin+r.width*(.25f+i*.25f);
                    Stroke(mesh,new Vector2(x,bottom),new Vector2(x,bottom+r.height*(.3f+i*.16f)),r.width*.16f);
                }
                Stroke(mesh,new Vector2(r.xMin+r.width*.1f,r.yMin+r.height*.13f),new Vector2(r.xMax-r.width*.1f,r.yMin+r.height*.13f),r.height*.06f);return;
            }
            if (symbol == Symbol.Overflow)
            {
                for(int dot=-1;dot<=1;dot++)
                {
                    var center=r.center+new Vector2(dot*r.width*.3f,0);int first=mesh.currentVertCount;
                    mesh.AddVert(center,color,Vector2.zero);
                    for(int i=0;i<=16;i++){float angle=i*Mathf.PI/8;mesh.AddVert(center+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*Mathf.Min(r.width,r.height)*.085f,color,Vector2.zero);if(i>0)mesh.AddTriangle(first,first+i,first+i+1);}
                }return;
            }
            if (symbol == Symbol.Pan) { PanArrows(mesh,r); return; }
            if (symbol == Symbol.UpperUnpegged || symbol == Symbol.LowerUnpegged) { UnpeggedArrow(mesh,r); return; }
            int n = mesh.currentVertCount;
            mesh.AddVert(new Vector3(r.xMin,r.yMin),color,new Vector2(0,0));
            mesh.AddVert(new Vector3(r.xMin,r.yMax),color,new Vector2(0,1));
            mesh.AddVert(new Vector3(r.xMax,r.yMax),color,new Vector2(1,1));
            mesh.AddVert(new Vector3(r.xMax,r.yMin),color,new Vector2(1,0));
            mesh.AddTriangle(n,n+1,n+2); mesh.AddTriangle(n,n+2,n+3);
        }
    }
}




