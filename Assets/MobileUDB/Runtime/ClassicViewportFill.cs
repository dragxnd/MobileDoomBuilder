using System;using System.Collections;using System.Collections.Generic;using MobileUDB.Core;using UnityEngine;using UnityEngine.UI;
namespace MobileUDB
{
    public sealed partial class MapViewport
    {
        public ClassicViewMode mode;public bool fullBright;
        const long FillTextureBudget=16L*1024*1024;
        ClassicOverlayGraphic overlay;readonly List<ClassicFillGraphic> fills=new List<ClassicFillGraphic>();
        readonly Dictionary<ResourceTexture,Texture2D> fillTextures=new Dictionary<ResourceTexture,Texture2D>();
        readonly HashSet<ResourceTexture> failedTextures=new HashSet<ResourceTexture>();
        MapDocument fillMap;DoomResources fillResources;long fillRevision=-1,textureBytes;
        ClassicViewMode fillMode;bool fillFullBright,layerDirty=true;MapPoint[] fillPositions;Coroutine fillLoading;Texture2D missingFill;
        public override void SetVerticesDirty(){base.SetVerticesDirty();layerDirty=true;}
        public void RefreshFill(){fillRevision=-1;SetVerticesDirty();}
        protected override void OnPopulateMesh(VertexHelper mesh)
        {mesh.Clear();var r=rectTransform.rect;Quad(mesh,new Vector2(r.xMin,r.yMin),new Vector2(r.xMax,r.yMax),editor!=null&&(editor.AutomapActive||editor.SoundPropagationActive||editor.BlockmapActive)?Color.black:(Color)new Color32(15,23,35,255));}
        T Layer<T>(string name) where T:MaskableGraphic
        {
            var go=new GameObject(name,typeof(RectTransform),typeof(CanvasRenderer));var rect=(RectTransform)go.transform;rect.SetParent(transform,false);rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;
            var graphic=go.AddComponent<T>();graphic.raycastTarget=false;return graphic;
        }
        void LateUpdate()
        {
            if(editor==null||editor.Map==null)return;
            if(overlay==null){overlay=Layer<ClassicOverlayGraphic>("Classic map elements");overlay.owner=this;}
            RefreshSoundLayers();RefreshAutomapLayers();
            if(editor.AutomapActive||editor.SoundPropagationActive||editor.BlockmapActive){if(layerDirty){overlay.SetVerticesDirty();layerDirty=false;}return;}
            UpdateBackground();
            var map=editor.Map;var resources=editor.ResourceCatalog;
            bool resourcesChanged=!ReferenceEquals(fillResources,resources);
            bool rebuild=!ReferenceEquals(fillMap,map)||resourcesChanged||fillRevision!=editor.MapContentRevision||fillMode!=mode||fillFullBright!=fullBright;
            if(!rebuild&&layerDirty&&mode!=ClassicViewMode.Wireframe)
            {if(fillPositions==null||fillPositions.Length!=map.vertices.Count)rebuild=true;else for(int i=0;i<fillPositions.Length;i++){var point=editor.TransformVertexPosition(i);if(fillPositions[i].x!=point.x||fillPositions[i].y!=point.y){rebuild=true;break;}}}
            if(rebuild)RebuildFill(resourcesChanged);
            RefreshThingSprites(rebuild || layerDirty);
            if(layerDirty){foreach(var fill in fills)fill.SetVerticesDirty();overlay.SetVerticesDirty();layerDirty=false;}
        }
        void RebuildFill(bool resourcesChanged)
        {
            if(fillLoading!=null){StopCoroutine(fillLoading);fillLoading=null;}
            foreach(var fill in fills){fill.gameObject.SetActive(false);Destroy(fill.gameObject);}fills.Clear();
            if(resourcesChanged)ClearFillTextures();
            fillMap=editor.Map;fillResources=editor.ResourceCatalog;fillRevision=editor.MapContentRevision;fillMode=mode;fillFullBright=fullBright;layerDirty=true;
            if(mode==ClassicViewMode.Wireframe){fillPositions=null;ClearFillTextures();return;}
            // Preview coordinates are independent of the editable records. Tessellation sees
            // the same transformed vertices as the overlaid walls, without changing the map.
            var preview=new MapDocument{name=fillMap.name,sky=fillMap.sky,lines=fillMap.lines,sides=fillMap.sides,sectors=fillMap.sectors};fillPositions=new MapPoint[fillMap.vertices.Count];
            for(int i=0;i<fillPositions.Length;i++){var point=editor.TransformVertexPosition(i);fillPositions[i]=point;preview.vertices.Add(new MapVertex(point.x,point.y));}
            ClassicFillPlan plan;
            try{plan=ClassicMapFill.Build(preview,fillResources,mode,fullBright);}catch(Exception error){editor.Notify("Заливка карты: "+error.Message);return;}
            var used=new HashSet<ResourceTexture>();
            foreach(var batch in plan.batches)
            {
                if(batch.source!=null)used.Add(batch.source);
                // Keep each UI mesh below Unity's vertex limit; no triangle is discarded.
                for(int first=0;first<batch.triangles.Count;first+=18000)
                {
                    var fill=Layer<ClassicFillGraphic>("Classic fill · "+batch.texture);fill.owner=this;fill.batch=batch;fill.first=first;fill.count=Math.Min(18000,batch.triangles.Count-first);
                    fill.texture=mode==ClassicViewMode.Brightness?Texture2D.whiteTexture:FillTexture(batch.source);fills.Add(fill);
                }
            }
            overlay.transform.SetAsLastSibling();
            var remove=new List<ResourceTexture>();foreach(var entry in fillTextures)if(!used.Contains(entry.Key))remove.Add(entry.Key);
            foreach(var source in remove){var texture=fillTextures[source];textureBytes-=(long)texture.width*texture.height*4;Destroy(texture);fillTextures.Remove(source);}
            if(plan.warnings.Count>0)editor.Notify(plan.warnings[0]);
            if(mode!=ClassicViewMode.Brightness&&fills.Count>0)fillLoading=StartCoroutine(LoadFillTextures());
        }
        Texture FillTexture(ResourceTexture source)
        {
            Texture2D texture;if(source!=null&&fillTextures.TryGetValue(source,out texture))return texture;
            if(missingFill==null){missingFill=new Texture2D(8,8,TextureFormat.RGBA32,false);missingFill.name="UDB unavailable flat";missingFill.filterMode=FilterMode.Point;missingFill.wrapMode=TextureWrapMode.Repeat;var pixels=new Color32[64];for(int y=0;y<8;y++)for(int x=0;x<8;x++)pixels[y*8+x]=((x/4+y/4)&1)==0?new Color32(120,40,120,255):new Color32(28,25,40,255);missingFill.SetPixels32(pixels);missingFill.Apply(false,true);}return missingFill;
        }
        IEnumerator LoadFillTextures()
        {
            bool warned=false;var seen=new HashSet<ResourceTexture>();
            foreach(var fill in fills)
            {
                var source=fill.batch.source;if(source==null||!seen.Add(source)||fillTextures.ContainsKey(source)||failedTextures.Contains(source))continue;
                yield return null;
                Texture2D pending=null;
                try
                {
                    var image=editor.DecodeVisualTexture(source);
                    if(textureBytes+image.rgba.Length>FillTextureBudget){failedTextures.Add(source);if(!warned){warned=true;editor.Notify("Кэш заливки карты заполнен. Часть текстур показана клеткой.");}continue;}
                    var texture=pending=new Texture2D(image.width,image.height,TextureFormat.RGBA32,false);texture.name="UDB classic flat "+source.name;texture.filterMode=FilterMode.Point;texture.wrapMode=TextureWrapMode.Repeat;var pixels=MobileEditor.VisualPixelRows(image);for(int alpha=3;alpha<pixels.Length;alpha+=4)pixels[alpha]=255;texture.LoadRawTextureData(pixels);texture.Apply(false,true);fillTextures.Add(source,texture);textureBytes+=image.rgba.Length;pending=null;
                    foreach(var target in fills)if(ReferenceEquals(target.batch.source,source)){target.texture=texture;target.SetMaterialDirty();target.SetVerticesDirty();}
                }
                catch(Exception error){if(pending!=null)Destroy(pending);failedTextures.Add(source);if(!warned){warned=true;editor.Notify("Заливка карты: "+error.Message);}}
            }
            fillLoading=null;
        }
        void ClearFillTextures(){foreach(var texture in fillTextures.Values)Destroy(texture);fillTextures.Clear();failedTextures.Clear();textureBytes=0;}
        protected override void OnDisable()
        {
            ClearThingSprites();
            ClearBackground();
            if(fillLoading!=null){StopCoroutine(fillLoading);fillLoading=null;}foreach(var fill in fills){fill.gameObject.SetActive(false);Destroy(fill.gameObject);}fills.Clear();ClearFillTextures();fillMap=null;fillRevision=-1;
            if(overlay!=null)overlay.gameObject.SetActive(false);base.OnDisable();
        }
        protected override void OnEnable(){base.OnEnable();if(overlay!=null)overlay.gameObject.SetActive(true);layerDirty=true;}
        protected override void OnDestroy(){ClearBackground();ClearFillTextures();if(missingFill!=null)Destroy(missingFill);base.OnDestroy();}
    }
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ClassicOverlayGraphic:MaskableGraphic
    {
        internal MapViewport owner;
        protected override void OnPopulateMesh(VertexHelper mesh){if(owner==null)mesh.Clear();else owner.PopulateOverlay(mesh);}
    }
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ClassicFillGraphic:MaskableGraphic
    {
        internal MapViewport owner;internal ClassicFillBatch batch;internal int first,count;internal Texture texture;
        public override Texture mainTexture{get{return texture==null?Texture2D.whiteTexture:texture;}}
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();if(owner==null||batch==null)return;var rect=rectTransform.rect;
            for(int i=first;i<first+count;i++)
            {
                var item=batch.triangles[i];var a=owner.Project(item.triangle.a);var b=owner.Project(item.triangle.b);var c=owner.Project(item.triangle.c);
                if(Mathf.Max(a.x,Mathf.Max(b.x,c.x))<rect.xMin||Mathf.Min(a.x,Mathf.Min(b.x,c.x))>rect.xMax||Mathf.Max(a.y,Mathf.Max(b.y,c.y))<rect.yMin||Mathf.Min(a.y,Mathf.Min(b.y,c.y))>rect.yMax)continue;
                int start=mesh.currentVertCount;var color=new Color32(item.brightness,item.brightness,item.brightness,255);
                Add(mesh,item.triangle.a,color);Add(mesh,item.triangle.b,color);Add(mesh,item.triangle.c,color);mesh.AddTriangle(start,start+1,start+2);
            }
        }
        void Add(VertexHelper mesh,MapPoint point,Color32 color){var uv=ClassicMapFill.UV(point,batch.source);mesh.AddVert(owner.Project(point),color,new Vector2(uv.x,uv.y));}
    }
}


