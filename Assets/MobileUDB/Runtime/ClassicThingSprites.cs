using System;
using System.Collections;
using System.Collections.Generic;
using MobileUDB.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MobileUDB
{
    public sealed partial class MapViewport
    {
        readonly Dictionary<ResourceTexture,Texture2D> classicSprites=new Dictionary<ResourceTexture,Texture2D>();
        readonly HashSet<ResourceTexture> failedClassicSprites=new HashSet<ResourceTexture>();
        readonly List<ClassicThingSpriteGraphic> spriteLayers=new List<ClassicThingSpriteGraphic>();
        ClassicThingBackdropGraphic thingBackdrop;
        DoomResources spriteResources;ThingCatalog spriteCatalog;Coroutine spriteLoading;
        void RefreshThingSprites(bool dirty)
        {
            if(!ReferenceEquals(spriteResources,editor.ResourceCatalog)||!ReferenceEquals(spriteCatalog,editor.ThingTypes))
            {ClearThingSprites();spriteResources=editor.ResourceCatalog;spriteCatalog=editor.ThingTypes;dirty=true;}
            if(!editor.ThingsVisible)
            {if(thingBackdrop!=null)thingBackdrop.gameObject.SetActive(false);foreach(var layer in spriteLayers)layer.gameObject.SetActive(false);return;}
            if(!dirty)return;
            var previous=new List<ClassicThingSpriteGraphic>(spriteLayers);foreach(var layer in previous)layer.quads.Clear();spriteLayers.Clear();
            if(spriteResources==null||spriteCatalog==null)return;
            if(thingBackdrop==null)thingBackdrop=Layer<ClassicThingBackdropGraphic>("2D thing backplates");thingBackdrop.tiles.Clear();
            var batches=new Dictionary<ResourceTexture,ClassicThingSpriteGraphic>();var fallbackBatches=new Dictionary<Texture2D,ClassicThingSpriteGraphic>();
            var map=editor.Map;var r=rectTransform.rect;var related=editor.Associations.related;
            // Use the same north-facing eight-frame convention as Renderer2D.RenderThingsBatch.
            for(int i=0;i<map.things.Count;i++)
            {
                var thing=map.things[i];var p=Project(editor.TransformThingPosition(i));if(!r.Contains(p))continue;
                var type=spriteCatalog.Find(thing.type);float angle=editor.TransformThingAngle(i),preview;if(editor.TryThingAimAngle(i,out preview))angle=preview;
                int rotation=Mathf.FloorToInt(((270-angle)%360+360)%360/45)+1;
                var frame=spriteResources.FindSprite(type.sprite,rotation);Texture2D fallback=null;
                if(frame==null&&type.id==888&&type.sprite=="DOGSA2")fallback=Resources.Load<Texture2D>("MobileUDB/Icons/MBFDog");
                if(frame==null&&fallback==null)fallback=Resources.Load<Texture2D>("MobileUDB/Icons/"+(type.id==32000?"Visual":"Thing"));
                bool selected=editor.Selection.Contains(new ElementRef(ElementKind.Thing,i));
                // Icons and backplates scale with map units, without screen-size clamps.
                float radius=ThingPlacement.Radius(thing,type)*zoom;
                thingBackdrop.tiles.Add(new ClassicThingBackdropGraphic.Tile{center=p,radius=radius,color=selected?new Color32(239,189,40,250):related.Contains(new ElementRef(ElementKind.Thing,i))?new Color32(88,199,247,245):new Color32(174,132,15,235)});
                ClassicThingSpriteGraphic batch;
                if(frame!=null)
                {
                    if(!batches.TryGetValue(frame.image,out batch)){batch=previous.Find(layer=>ReferenceEquals(layer.source,frame.image));if(batch==null)batch=Layer<ClassicThingSpriteGraphic>("2D sprites · "+frame.image.name);batch.source=frame.image;Texture2D texture;if(classicSprites.TryGetValue(frame.image,out texture))batch.texture=texture;batches.Add(frame.image,batch);spriteLayers.Add(batch);}
                }
                else
                {
                    if(fallback==null)continue;
                    if(!fallbackBatches.TryGetValue(fallback,out batch)){batch=previous.Find(layer=>layer.source==null&&layer.texture==fallback);if(batch==null)batch=Layer<ClassicThingSpriteGraphic>("2D fallback icon");batch.texture=fallback;fallbackBatches.Add(fallback,batch);spriteLayers.Add(batch);}
                }
                batch.quads.Add(new ClassicThingSpriteGraphic.SpriteQuad{center=p,radius=radius*.85f,mirror=frame!=null&&frame.mirror,color=new Color32(255,255,255,255)});
            }
            foreach(var old in previous)if(!spriteLayers.Contains(old)){old.gameObject.SetActive(false);Destroy(old.gameObject);}
            foreach(var layer in spriteLayers){layer.gameObject.SetActive(true);layer.SetVerticesDirty();}
            thingBackdrop.gameObject.SetActive(true);
            // Grid, walls, then filled backplates and untinted sprites; no thing rings/arrows.
            overlay.transform.SetAsLastSibling();thingBackdrop.transform.SetAsLastSibling();thingBackdrop.SetVerticesDirty();
            foreach(var layer in spriteLayers)layer.transform.SetAsLastSibling();
            var unused=new List<ResourceTexture>();foreach(var source in classicSprites.Keys)if(!batches.ContainsKey(source))unused.Add(source);
            foreach(var source in unused){Destroy(classicSprites[source]);classicSprites.Remove(source);}
            if(batches.Count>0&&spriteLoading==null)spriteLoading=StartCoroutine(LoadClassicSprites());
        }
        IEnumerator LoadClassicSprites()
        {
            // One new visible image per frame; navigation never restarts the queue.
            while(true)
            {
                yield return null;
                var layer=spriteLayers.Find(item=>item.source!=null&&item.texture==null&&!failedClassicSprites.Contains(item.source));
                if(layer==null||classicSprites.Count>=256)break;
                Texture2D pending=null;
                try
                {
                    var image=editor.DecodeClassicSprite(layer.source);var texture=pending=new Texture2D(image.width,image.height,TextureFormat.RGBA32,false);
                    texture.name="UDB 2D sprite "+layer.source.name;texture.filterMode=FilterMode.Point;texture.wrapMode=TextureWrapMode.Clamp;texture.LoadRawTextureData(MobileEditor.VisualPixelRows(image));texture.Apply(false,true);
                    classicSprites.Add(layer.source,texture);layer.texture=texture;layer.SetMaterialDirty();layer.SetVerticesDirty();pending=null;
                }
                catch(Exception){if(pending!=null)Destroy(pending);failedClassicSprites.Add(layer.source);}
            }
            spriteLoading=null;
        }
        void ClearThingSprites()
        {
            if(spriteLoading!=null){StopCoroutine(spriteLoading);spriteLoading=null;}
            if(thingBackdrop!=null){thingBackdrop.gameObject.SetActive(false);Destroy(thingBackdrop.gameObject);thingBackdrop=null;}
            foreach(var layer in spriteLayers){if(layer!=null){layer.gameObject.SetActive(false);Destroy(layer.gameObject);}}spriteLayers.Clear();
            foreach(var texture in classicSprites.Values)if(texture!=null)Destroy(texture);classicSprites.Clear();failedClassicSprites.Clear();spriteResources=null;spriteCatalog=null;
        }
    }
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ClassicThingBackdropGraphic : MaskableGraphic
    {
        public struct Tile {public Vector2 center;public float radius;public Color32 color;}
        public readonly List<Tile> tiles=new List<Tile>();
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            foreach(var tile in tiles)
            {
                if(mesh.currentVertCount>=58000)break;float r=tile.radius,c=Mathf.Min(2,r*.1f);int n=mesh.currentVertCount;
                mesh.AddVert(tile.center,tile.color,Vector2.zero);
                mesh.AddVert(tile.center+new Vector2(-r+c,-r),tile.color,Vector2.zero);
                mesh.AddVert(tile.center+new Vector2(r-c,-r),tile.color,Vector2.zero);
                mesh.AddVert(tile.center+new Vector2(r,-r+c),tile.color,Vector2.zero);
                mesh.AddVert(tile.center+new Vector2(r,r-c),tile.color,Vector2.zero);
                mesh.AddVert(tile.center+new Vector2(r-c,r),tile.color,Vector2.zero);
                mesh.AddVert(tile.center+new Vector2(-r+c,r),tile.color,Vector2.zero);
                mesh.AddVert(tile.center+new Vector2(-r,r-c),tile.color,Vector2.zero);
                mesh.AddVert(tile.center+new Vector2(-r,-r+c),tile.color,Vector2.zero);
                for(int i=0;i<8;i++)mesh.AddTriangle(n,n+1+i,n+1+(i+1)%8);
            }
        }
    }
    public sealed class ClassicThingSpriteGraphic : MaskableGraphic
    {
        public struct SpriteQuad {public Vector2 center;public float radius;public bool mirror;public Color32 color;}
        public ResourceTexture source;public Texture2D texture;public readonly List<SpriteQuad> quads=new List<SpriteQuad>();
        public override Texture mainTexture {get{return texture==null?Texture2D.whiteTexture:texture;}}
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();if(texture==null)return;
            float aspect=texture.width/(float)texture.height;
            foreach(var quad in quads)
            {
                if(mesh.currentVertCount>=60000)break;var half=aspect>=1?new Vector2(quad.radius,quad.radius/aspect):new Vector2(quad.radius*aspect,quad.radius);
                var min=quad.center-half;var max=quad.center+half;float left=quad.mirror?1:0,right=quad.mirror?0:1;int n=mesh.currentVertCount;
                mesh.AddVert(new Vector3(min.x,min.y),quad.color,new Vector2(left,0));mesh.AddVert(new Vector3(min.x,max.y),quad.color,new Vector2(left,1));mesh.AddVert(new Vector3(max.x,max.y),quad.color,new Vector2(right,1));mesh.AddVert(new Vector3(max.x,min.y),quad.color,new Vector2(right,0));mesh.AddTriangle(n,n+1,n+2);mesh.AddTriangle(n,n+2,n+3);
            }
        }
    }
    public sealed partial class MobileEditor
    {
        internal ResourceImage DecodeClassicSprite(ResourceTexture source) {return resources.Decode(source,DecodePng,128);}
    }
}
