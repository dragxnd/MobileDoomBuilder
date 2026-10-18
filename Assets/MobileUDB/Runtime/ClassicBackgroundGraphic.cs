// GPL-3.0. Native Renderer2D.SetupBackground: tiled, offset from map origin.
using System;using MobileUDB.Core;using UnityEngine;using UnityEngine.UI;
namespace MobileUDB
{
    public sealed partial class MapViewport
    {
        ClassicBackgroundGraphic backgroundGraphic;MapBackground backgroundState;DoomResources backgroundResources;
        Texture2D backgroundTexture;double backgroundWidth,backgroundHeight;
        void UpdateBackground()
        {
            var state=editor.CurrentBackground;var resources=editor.ResourceCatalog;
            if(!ReferenceEquals(state,backgroundState)||!ReferenceEquals(resources,backgroundResources))
            {
                bool sameImage=state!=null&&backgroundState!=null&&state.visible&&backgroundState.visible&&state.source==backgroundState.source&&state.name==backgroundState.name&&state.fileData==backgroundState.fileData&&ReferenceEquals(resources,backgroundResources);
                if(!sameImage)
                {
                    if(backgroundTexture!=null){Destroy(backgroundTexture);backgroundTexture=null;}
                    if(state!=null&&state.visible)
                    {try {var image=editor.DecodeBackground(state,out backgroundWidth,out backgroundHeight);backgroundTexture=MobileEditor.BackgroundTexture(image,state.name);}catch(Exception error){editor.Notify("Фон: "+error.Message);}}
                }
                backgroundState=state;backgroundResources=resources;
                if(backgroundGraphic==null){backgroundGraphic=Layer<ClassicBackgroundGraphic>("Classic background");backgroundGraphic.owner=this;}
                backgroundGraphic.gameObject.SetActive(state!=null&&state.visible&&backgroundTexture!=null);backgroundGraphic.SetMaterialDirty();backgroundGraphic.SetVerticesDirty();backgroundGraphic.transform.SetAsFirstSibling();
            }
            if(layerDirty&&backgroundGraphic!=null)backgroundGraphic.SetVerticesDirty();
        }
        void ClearBackground()
        {if(backgroundTexture!=null){Destroy(backgroundTexture);backgroundTexture=null;}backgroundState=null;backgroundResources=null;if(backgroundGraphic!=null)backgroundGraphic.gameObject.SetActive(false);}
        internal Texture BackgroundTexture => backgroundTexture==null?Texture2D.whiteTexture:backgroundTexture;
        internal void PopulateBackground(VertexHelper mesh)
        {
            mesh.Clear();if(backgroundTexture==null||backgroundState==null||!backgroundState.visible||backgroundState.scaleX==0||backgroundState.scaleY==0)return;
            var rect=rectTransform.rect;var color=new Color32(255,255,255,(byte)((backgroundState.opacity*255+50)/100));
            AddBackgroundVertex(mesh,new Vector2(rect.xMin,rect.yMin),color);AddBackgroundVertex(mesh,new Vector2(rect.xMin,rect.yMax),color);AddBackgroundVertex(mesh,new Vector2(rect.xMax,rect.yMax),color);AddBackgroundVertex(mesh,new Vector2(rect.xMax,rect.yMin),color);
            mesh.AddTriangle(0,1,2);mesh.AddTriangle(0,2,3);
        }
        void AddBackgroundVertex(VertexHelper mesh,Vector2 local,Color32 color)
        {var world=local/zoom+center;var uv=backgroundState.UV(new MapPoint(world.x,world.y),backgroundWidth,backgroundHeight);mesh.AddVert(local,color,new Vector2(uv.x,uv.y));}
    }
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ClassicBackgroundGraphic:MaskableGraphic
    {
        internal MapViewport owner;
        public override Texture mainTexture=>owner==null?Texture2D.whiteTexture:owner.BackgroundTexture;
        protected override void OnPopulateMesh(VertexHelper mesh){if(owner==null)mesh.Clear();else owner.PopulateBackground(mesh);}
    }
}
