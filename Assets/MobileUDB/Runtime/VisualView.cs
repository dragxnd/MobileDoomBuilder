using System;
using System.Collections;
using System.Collections.Generic;
using MobileUDB.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace MobileUDB
{
    public sealed partial class VisualView : MonoBehaviour
    {
        const float Units = 1f / 64;
        const long TextureBudget = 64L * 1024 * 1024;
        public MobileEditor editor;
        public Camera renderCamera { get; private set; }
        public SurfaceRef? Picked { get; private set; }
        public int SurfaceCount { get { return faces.Count; } }
        public int TriangleCount { get; private set; }
        public int BatchCount { get { return batches.Count; } }
        sealed class Face { public MapSurface surface; public Vector3[] points; public Bounds bounds; public string key; }
        sealed class Batch
        {
            public readonly List<Vector3> vertices = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<Color32> colors = new List<Color32>();
            public readonly List<int> indices = new List<int>();
            public GameObject go; public Mesh mesh;
        }
        sealed class MaterialEntry
        {
            public Material material; public Texture2D texture; public ResourceTexture source;
            public byte[] alpha; public int width, height; public bool sky, sprite; public float distance = float.MaxValue;
        }
        readonly List<Face> faces = new List<Face>();
        readonly Dictionary<string,Batch> batches = new Dictionary<string,Batch>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string,MaterialEntry> materials = new Dictionary<string,MaterialEntry>(StringComparer.OrdinalIgnoreCase);
        GameObject worldRoot, cameraObject, highlight;
        Mesh highlightMesh;
        Material highlightMaterial;
        Texture2D missing;
        Shader shader;
        RawImage display;
        RectTransform viewport;
        RenderTexture target;
        Coroutine loading;
        long textureBytes;
        bool rebuild = true, dragging, dragged;
        Vector2 pointerStart;
        float yaw, pitch;
        Vector2? placementViewport;
        Text[] fingerMarkers;
        public Rect ScreenRect
        {
            get { var corners = new Vector3[4]; viewport.GetWorldCorners(corners); return Rect.MinMaxRect(corners[0].x,corners[0].y,corners[2].x,corners[2].y); }
        }
        public void Initialize(MobileEditor owner)
        {
            editor = owner; viewport = GetComponent<RectTransform>(); display = GetComponent<RawImage>();
            shader = Resources.Load<Shader>("MobileUDB/VisualSurface");
            if (shader == null) throw new InvalidOperationException("Шейдер 3D-режима не найден.");
            worldRoot = new GameObject("Mobile UDB 3D geometry"); worldRoot.transform.SetParent(editor.transform,false);
            cameraObject = new GameObject("Mobile UDB visual camera"); cameraObject.transform.SetParent(editor.transform,false);
            renderCamera = cameraObject.AddComponent<Camera>(); renderCamera.enabled = false; renderCamera.cullingMask = 1 << 31;
            renderCamera.clearFlags = CameraClearFlags.SolidColor; renderCamera.backgroundColor = new Color32(35,46,65,255);
            renderCamera.nearClipPlane = .025f; renderCamera.farClipPlane = 2048; renderCamera.fieldOfView = 75;
            renderCamera.allowHDR = false; renderCamera.allowMSAA = false;
            missing = new Texture2D(8,8,TextureFormat.RGBA32,false); missing.name = "UDB missing texture"; missing.filterMode = FilterMode.Point; missing.wrapMode = TextureWrapMode.Repeat;
            var pixels = new Color32[64]; for (int i = 0; i < 64; i++) pixels[i] = ((i / 8 / 4 + i % 8 / 4) & 1) == 0 ? new Color32(125,65,125,255) : new Color32(45,37,56,255);
            missing.SetPixels32(pixels); missing.Apply(false,true);
            highlight = new GameObject("Selected visual surface",typeof(MeshFilter),typeof(MeshRenderer)); highlight.transform.SetParent(worldRoot.transform,false); highlight.layer = 31;
            highlightMaterial = new Material(shader); highlightMaterial.name = "UDB surface selection"; highlightMaterial.renderQueue = 3000;
            highlightMaterial.SetColor("_Tint",new Color(1,.72f,.12f,.28f)); highlightMaterial.SetFloat("_AlphaClip",0); highlightMaterial.SetFloat("_ZWrite",0); highlightMaterial.SetFloat("_Cull",0);
            highlight.GetComponent<MeshRenderer>().sharedMaterial = highlightMaterial; highlight.SetActive(false);
            fingerMarkers = new Text[2];
            for (int i = 0; i < 2; i++)
            {
                var go = new GameObject("Visual finger " + i,typeof(RectTransform),typeof(Text)); go.transform.SetParent(transform,false);
                var text = go.GetComponent<Text>(); text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); text.text = "○"; text.fontSize = 36; text.color = Color.cyan; text.alignment = TextAnchor.MiddleCenter; text.raycastTarget = false;
                var rect = text.rectTransform; rect.anchorMin = rect.anchorMax = new Vector2(.5f,.5f); rect.sizeDelta = new Vector2(48,48); go.SetActive(false); fingerMarkers[i] = text;
            }
        }
        void OnEnable() { if (editor != null) { worldRoot.SetActive(true); cameraObject.SetActive(true); RequestRebuild(); ResetCamera(); } }
        void OnDisable()
        {
            CancelLook(); ForgetSelection(); if (loading != null) { StopCoroutine(loading); loading = null; }
            if (renderCamera != null) { renderCamera.enabled = false; renderCamera.targetTexture = null; }
            if (worldRoot != null) worldRoot.SetActive(false); if (cameraObject != null) cameraObject.SetActive(false);
            ClearGeometry(); ClearMaterials(); ReleaseTarget();
        }
        void OnDestroy()
        {
            ClearGeometry(); ClearMaterials(); ReleaseTarget();
            if (highlightMesh != null) Destroy(highlightMesh); if (highlightMaterial != null) Destroy(highlightMaterial);
            if (missing != null) Destroy(missing); if (worldRoot != null) Destroy(worldRoot); if (cameraObject != null) Destroy(cameraObject);
        }
        void ReleaseTarget() { if (target != null) { target.Release(); Destroy(target); target = null; } if (display != null) display.texture = null; }
        public void RequestRebuild() { CancelPaintSelection(); rebuild = true; }
        internal WallTextureSize LoadedWallTextureSize(SurfaceRef surface)
        {
            if(!SurfaceTextureEdits.Valid(editor.Map,surface)||surface.IsFlat)return new WallTextureSize();
            string name=VisualGeometry.Texture(editor.Map,surface);var source=editor.ResourceCatalog.Find(name,ResourceKind.Wall,false);
            // Upstream's internal MissingTexture3D and UnknownTexture3D are both
            // loaded 64x64 images. Keep their offset period, not our 8x8 checker.
            if(source==null)return new WallTextureSize(64,64);
            MaterialEntry entry;if(materials.TryGetValue(ResourceKind.Wall+"/"+name,out entry)&&entry.texture!=null&&ReferenceEquals(entry.source,source))
                return new WallTextureSize(source.width,source.height);
            return new WallTextureSize();
        }
        internal void RebindSelection(SurfaceRef surface)
        {selectedSurfaces.Clear();selectedThings.Clear();selectedSurfaces.Add(surface);Picked=surface;PickedThing=-1;RequestRebuild();editor.SelectVisualSurface(surface);}
        public void ForgetSelection() { CancelPaintSelection();placementViewport=null;selectedSurfaces.Clear();selectedThings.Clear();Picked = null; PickedThing = -1; if (highlight != null) highlight.SetActive(false); }
        public void ResourcesChanged() { if (loading != null) StopCoroutine(loading); loading = null; ClearMaterials(); RequestRebuild(); }
        void ClearGeometry()
        {
            ClearThings();
            foreach (var batch in batches.Values) { if (batch.go != null) { batch.go.SetActive(false); Destroy(batch.go); } if (batch.mesh != null) Destroy(batch.mesh); }
            batches.Clear(); faces.Clear(); TriangleCount = 0;
        }
        void ClearMaterials()
        {
            foreach (var entry in materials.Values) { if (entry.texture != null) Destroy(entry.texture); if (entry.material != null) Destroy(entry.material); }
            materials.Clear(); textureBytes = 0;
        }
        void LateUpdate()
        {
            if (editor == null) return;
            Rect rect = ScreenRect; float scale = Math.Min(1,1280 / Math.Max(1,Math.Max(rect.width,rect.height)));
            int width = Math.Max(16,(int)(rect.width * scale)), height = Math.Max(16,(int)(rect.height * scale));
            if (target == null || target.width != width || target.height != height)
            {
                renderCamera.targetTexture = null; ReleaseTarget(); target = new RenderTexture(width,height,24,RenderTextureFormat.ARGB32); target.name = "UDB visual viewport";
                target.Create(); renderCamera.targetTexture = target; renderCamera.aspect = width / (float)height; display.texture = target; renderCamera.enabled = true;
            }
            if (rebuild) Rebuild();
            UpdateThings();
            for (int i = 0; i < fingerMarkers.Length; i++)
            {
                bool visible = i < editor.View.fingers.Count; fingerMarkers[i].gameObject.SetActive(visible);
                if (visible) { Vector2 local; RectTransformUtility.ScreenPointToLocalPointInRectangle(viewport,editor.View.fingers[i],null,out local); fingerMarkers[i].rectTransform.anchoredPosition = local; }
            }
        }
        void Update()
        {
            if (editor == null || editor.ModalOpen) return;
            var keys = Keyboard.current; if (keys == null) return;
            Vector3 move = Vector3.zero;
            if (keys.wKey.isPressed || keys.upArrowKey.isPressed) move.z++;
            if (keys.sKey.isPressed || keys.downArrowKey.isPressed) move.z--;
            if (keys.aKey.isPressed || keys.leftArrowKey.isPressed) move.x--;
            if (keys.dKey.isPressed || keys.rightArrowKey.isPressed) move.x++;
            if (keys.eKey.isPressed) move.y++; if (keys.qKey.isPressed) move.y--;
            if (move != Vector3.zero) Move(move.normalized,Time.unscaledDeltaTime * (keys.shiftKey.isPressed ? 384 : 192));
        }
        MaterialEntry Material(string key, MapSurface surface, Bounds bounds)
        {
            MaterialEntry entry;
            var transfer = surface.skyTransfer;
            var catalog = editor.ResourceCatalog;
            var source = surface.sky ? catalog.Find(transfer == null ? SkyTransfers.DefaultTexture(editor.Map) : transfer.texture,ResourceKind.Wall,true) : catalog.Find(surface.texture,surface.kind,MapFormats.IsUniversal(editor.Map.format));
            if (!materials.TryGetValue(key,out entry))
            {
                var material = new Material(shader); material.name = "UDB " + key; material.mainTexture = missing; material.SetFloat("_Sky",surface.sky ? 1 : 0);
                entry = new MaterialEntry { material = material,source = source,sky = surface.sky }; materials.Add(key,entry);
            }
            else if (!ReferenceEquals(entry.source,source))
            {
                if (entry.texture != null) { textureBytes -= (long)entry.width * entry.height * 4; Destroy(entry.texture); }
                entry.texture = null; entry.alpha = null; entry.source = source; entry.material.mainTexture = missing;
            }
            // Use original resource dimensions, even when the mobile decoder downsamples to 512.
            int width = source == null ? 256 : Math.Max(1,source.width), height = source == null ? 128 : Math.Max(1,source.height);
            entry.material.SetVector("_SkyTransfer",transfer == null ? Vector4.zero : new Vector4(1,(float)transfer.AngleTurns,transfer.ReverseColumns ? -1 : 1,transfer.HorizonRow));
            entry.material.SetVector("_SkySize",new Vector4(width,height,0,0));
            entry.distance = Math.Min(entry.distance,(bounds.center - renderCamera.transform.position).sqrMagnitude); return entry;
        }
        public void Rebuild()
        {
            rebuild = false; if (loading != null) { StopCoroutine(loading); loading = null; } ClearGeometry();
            foreach (var entry in materials.Values) entry.distance = float.MaxValue;
            var warnings = new List<string>();
            try
            {
                foreach (var surface in VisualGeometry.Build(editor.Map,editor.ResourceCatalog,warnings,editor.Actions.mbf || editor.Actions.extended))
                {
                    string key = surface.sky ? surface.skyTransfer == null ? "sky/default/" + SkyTransfers.DefaultTexture(editor.Map) : "sky/transfer/" + surface.skyTransfer.line : surface.kind + "/" + surface.texture;
                    var points = new Vector3[surface.vertices.Count];
                    for (int i = 0; i < points.Length; i++) { var v = surface.vertices[i]; points[i] = new Vector3(v.x,v.height,v.y) * Units; }
                    var bounds = new Bounds(points[0],Vector3.zero); foreach (var point in points) bounds.Encapsulate(point);
                    faces.Add(new Face { surface = surface,points = points,bounds = bounds,key = key });
                    Batch batch; if (!batches.TryGetValue(key,out batch)) { batch = new Batch(); batches.Add(key,batch); }
                    int n = batch.vertices.Count; batch.vertices.AddRange(points);
                    byte light = surface.sky?(byte)255:ClassicMapFill.RenderBrightness(editor.Map,surface.light,editor.View.fullBright);
                    foreach (var v in surface.vertices) { batch.uv.Add(new Vector2(v.u,v.v)); batch.colors.Add(new Color32(light,light,light,255)); }
                    foreach (int index in surface.indices) batch.indices.Add(n + index); TriangleCount += surface.indices.Count / 3;
                    Material(key,surface,bounds);
                }
                foreach (var pair in batches)
                {
                    var batch = pair.Value; batch.mesh = new Mesh(); batch.mesh.name = "UDB " + pair.Key;
                    if (batch.vertices.Count > 65535) batch.mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                    batch.mesh.SetVertices(batch.vertices); batch.mesh.SetUVs(0,batch.uv); batch.mesh.SetColors(batch.colors); batch.mesh.SetTriangles(batch.indices,0); batch.mesh.RecalculateBounds();
                    batch.go = new GameObject("Visual batch " + pair.Key,typeof(MeshFilter),typeof(MeshRenderer)); batch.go.layer = 31; batch.go.transform.SetParent(worldRoot.transform,false);
                    batch.go.GetComponent<MeshFilter>().sharedMesh = batch.mesh; var renderer = batch.go.GetComponent<MeshRenderer>(); renderer.sharedMaterial = materials[pair.Key].material;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false;
                }
                BuildThings(); UpdateHighlight();
                var unused = new List<string>(); foreach (var pair in materials) if (pair.Value.distance == float.MaxValue) unused.Add(pair.Key);
                foreach (string key in unused)
                {
                    var entry = materials[key]; if (entry.texture != null) { textureBytes -= (long)entry.width * entry.height * 4; Destroy(entry.texture); }
                    Destroy(entry.material); materials.Remove(key);
                }
                var pending = new List<MaterialEntry>(); foreach (var entry in materials.Values) if (entry.texture == null && entry.distance < float.MaxValue) pending.Add(entry);
                pending.Sort((a,b) => a.distance.CompareTo(b.distance)); loading = StartCoroutine(LoadTextures(pending));
                if (warnings.Count > 0) editor.Notify(warnings[0]);
            }
            catch (Exception e) { editor.Notify("Ошибка 3D: " + e.Message); Debug.LogException(e); }
        }
        IEnumerator LoadTextures(List<MaterialEntry> entries)
        {
            bool warned = false;
            foreach (var entry in entries)
            {
                yield return null;
                if (entry.source == null) continue;
                try
                {
                    var decoded = editor.DecodeVisualTexture(entry.source);
                    if (textureBytes + decoded.rgba.Length > TextureBudget) { if (!warned) { editor.Notify("Кэш 3D-текстур заполнен. Дальние поверхности показываются заглушками."); warned = true; } continue; }
                    var texture = new Texture2D(decoded.width,decoded.height,TextureFormat.RGBA32,false); texture.name = "UDB visual texture " + entry.source.name;
                    texture.filterMode = FilterMode.Point; texture.wrapModeU = entry.sprite ? TextureWrapMode.Clamp : TextureWrapMode.Repeat; texture.wrapModeV = entry.sprite || (entry.sky && entry.material.GetVector("_SkyTransfer").x < .5f) ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
                    texture.LoadRawTextureData(MobileEditor.VisualPixelRows(decoded)); texture.Apply(false,true); entry.texture = texture; entry.material.mainTexture = texture;
                    entry.width = decoded.width; entry.height = decoded.height;
                    bool alpha = false; for (int p = 3; p < decoded.rgba.Length; p += 4) if (decoded.rgba[p] < 128) { alpha = true; break; }
                    if (alpha) { entry.alpha = new byte[entry.width * entry.height]; for (int p = 0; p < entry.alpha.Length; p++) entry.alpha[p] = decoded.rgba[p * 4 + 3]; }
                    textureBytes += decoded.rgba.Length;
                }
                catch (Exception e) { if (!warned) { editor.Notify("3D: " + e.Message); warned = true; } }
            }
            loading = null;
        }
        public void ResetCamera()
        {if(!RestoreMarkerCamera())ResetPlayerCamera();}
        internal bool RestoreMarkerCamera()
        {
            if(editor==null||renderCamera==null)return false;
            MapCamera saved;if(!MapCameraMarker.TryRead(editor.Map,out saved))return false;
            ApplyCamera(saved);return true;
        }
        public void ResetPlayerCamera()
        {
            if (editor == null || renderCamera == null) return;
            CancelLook();
            placementViewport=null;
            var map = editor.Map; MapPoint point = new MapPoint(editor.View.center.x,editor.View.center.y); float angle = 0, z = 0; bool found = false;
            foreach (var thing in map.things) if (thing.type == 1) { point = thing.Position; angle = thing.angle; z = thing.z; found = true; break; }
            int sector = -1; for (int i = 0; i < map.sectors.Count; i++) if (map.Contains(i,point)) { sector = i; break; }
            if (sector < 0)
            {
                var tess = new SectorTessellation(map); double area = 0;
                for (int i = 0; i < map.sectors.Count; i++)
                {
                    try { foreach (var triangle in tess.Triangles(i)) if (triangle.Area > area) { area = triangle.Area; sector = i; point = new MapPoint((triangle.a.x + triangle.b.x + triangle.c.x) / 3,(triangle.a.y + triangle.b.y + triangle.c.y) / 3); } } catch (InvalidOperationException) { }
                }
            }
            float floor = sector < 0 ? 0 : VisualGeometry.Height(map.sectors[sector],false,point), ceiling = sector < 0 ? 128 : VisualGeometry.Height(map.sectors[sector],true,point);
            float height = floor + (found ? z : 0) + Math.Min(41,Math.Max(1,(ceiling - floor) * .5f));
            renderCamera.transform.position = new Vector3(point.x,height,point.y) * Units; yaw = 90 - angle; pitch = 0; Rotate();
        }
        internal MapCamera CaptureCamera()
        {var position=renderCamera.transform.position/Units;return new MapCamera{x=position.x,y=position.z,z=position.y,yaw=yaw,pitch=pitch}.Validated();}
        internal void ApplyCamera(MapCamera saved)
        {saved=saved.Validated();CancelLook();placementViewport=null;renderCamera.transform.position=new Vector3(saved.x,saved.z,saved.y)*Units;yaw=saved.yaw;pitch=saved.pitch;Rotate();}
        void Rotate() { renderCamera.transform.rotation = Quaternion.Euler(pitch,yaw,0); }
        public void Move(Vector3 direction, float amount)
        {
            if (editor.ModalOpen || !gameObject.activeInHierarchy) return;
            if(direction!=Vector3.zero&&amount!=0){CancelPaintSelection();placementViewport=null;}
            var rotation = Quaternion.Euler(0,yaw,0); renderCamera.transform.position += rotation * direction * (amount * Units);
        }
        public void BeginPointer(Vector2 screen) { pointerStart = screen; dragging = true; dragged = false; }
        public void Look(Vector2 previous, Vector2 current)
        {
            if (!dragging) return; if (Vector2.Distance(current,pointerStart) > 8) dragged = true;
            float sensitivity = 160f / Mathf.Max(200,Math.Min(Screen.width,Screen.height)); var delta = current - previous;
            if(delta!=Vector2.zero)placementViewport=null;
            yaw = (yaw + delta.x * sensitivity) % 360; pitch = Mathf.Clamp(pitch - delta.y * sensitivity,-85,85); Rotate();
        }
        public void EndPointer(Vector2 screen) { if (dragging && !dragged) PickAt(screen); CancelLook(); }
        public void CancelLook() { dragging = dragged = false; CancelPaintSelection(); }
        public void Gesture(Vector2 oldA, Vector2 oldB, Vector2 a, Vector2 b)
        {
            CancelLook(); var delta = ((a + b) - (oldA + oldB)) * .5f; float factor = 384 / Mathf.Max(200,ScreenRect.height);
            Move(new Vector3(-delta.x * factor,delta.y * factor,(Vector2.Distance(a,b) - Vector2.Distance(oldA,oldB)) * factor),1);
        }
        public SurfaceRef? PickAt(Vector2 screen)
        {
            if (!ScreenRect.Contains(screen)) return Picked;
            Rect rect=ScreenRect;placementViewport=new Vector2((screen.x-rect.xMin)/rect.width,(screen.y-rect.yMin)/rect.height);
            var ray=renderCamera.ViewportPointToRay(placementViewport.Value);float distance;int thing;
            var bestFace=FindPick(ray,out thing,out distance);
            if(thing>=0)SelectThing(thing);
            else SelectSurface(bestFace==null?(SurfaceRef?)null:bestFace.surface.id);
            return Picked;
        }
        // Recast the last tap in current geometry; after navigation use the crosshair.
        // No stored world coordinate can survive Undo / opening another map.
        internal bool PlacementReady {get{return !rebuild&&renderCamera!=null&&gameObject.activeInHierarchy;}}
        internal bool TryCameraHit(out Vector3 point)
        {
            point=Vector3.zero;if(!PlacementReady)return false;
            var ray=renderCamera.ViewportPointToRay(placementViewport??new Vector2(.5f,.5f));
            int thing;float distance;var face=FindPick(ray,out thing,out distance);
            if(face==null&&thing<0)return false;
            point=ray.GetPoint(distance)/Units;return true;
        }
        internal bool ApproachCameraTarget()
        {
            Vector3 point;if(!TryCameraHit(out point))return false;
            ApplyCamera(CaptureCamera().Approach(point.x,point.z,point.y));return true;
        }
        internal bool TryPlacementPoint(out MapPoint point)
        {
            point=new MapPoint();if(!PlacementReady)return false;
            var location=placementViewport??new Vector2(.5f,.5f);var ray=renderCamera.ViewportPointToRay(location);
            int thing;float distance;var face=FindPick(ray,out thing,out distance);
            if(face==null&&thing<0)return false;var hit=ray.GetPoint(distance)/Units;
            point=new MapPoint(hit.x,hit.z);return true;
        }
        internal void RebindInsertedThings(IEnumerable<int> indices)
        {
            ForgetSelection();foreach(int index in indices)if(index>=0&&index<editor.Map.things.Count)selectedThings.Add(index);
            EnsurePrimary();RequestRebuild();editor.SelectVisualSurface(Picked);
        }
        internal void CameraAtThing(int index)
        {
            if(index<0||index>=editor.Map.things.Count)throw new InvalidOperationException("Объект больше не существует.");
            var thing=editor.Map.things[index];var type=editor.ThingTypes.Find(thing.type);int sector=ThingPlacement.Sector(editor.Map,thing.Position);
            float height=ThingPlacement.BaseHeight(editor.Map,thing,type,sector)+ThingPlacement.Height(thing,type)*.5f;
            placementViewport=null;CancelLook();renderCamera.transform.position=new Vector3(thing.x,height,thing.y)*Units;
            yaw=90-thing.angle;pitch=0;Rotate();
        }
        Face FindPick(Ray ray,out int thing,out float best)
        {
            Face bestFace = null; best = float.MaxValue;
            foreach (var face in faces)
            {
                float near; if (!face.bounds.IntersectRay(ray,out near) || near > best) continue;
                var indices = face.surface.indices;
                for (int i = 0; i < indices.Count; i += 3)
                {
                    int ia = indices[i], ib = indices[i + 1], ic = indices[i + 2];
                    var a = face.points[ia]; var e1 = face.points[ib] - a; var e2 = face.points[ic] - a;
                    var p = Vector3.Cross(ray.direction,e2); float det = Vector3.Dot(e1,p); if (det <= .0000001f) continue;
                    var t = ray.origin - a; float u = Vector3.Dot(t,p) / det; if (u < 0 || u > 1) continue;
                    var q = Vector3.Cross(t,e1); float v = Vector3.Dot(ray.direction,q) / det; if (v < 0 || u + v > 1) continue;
                    float distance = Vector3.Dot(e2,q) / det; if (distance <= 0 || distance >= best) continue;
                    MaterialEntry material; if (materials.TryGetValue(face.key,out material) && material.alpha != null && !face.surface.sky)
                    {
                        var va = face.surface.vertices[ia]; var vb = face.surface.vertices[ib]; var vc = face.surface.vertices[ic];
                        float tu = va.u * (1 - u - v) + vb.u * u + vc.u * v, tv = va.v * (1 - u - v) + vb.v * u + vc.v * v;
                        int x = Math.Min(material.width - 1,(int)(Mathf.Repeat(tu,1) * material.width));
                        int y = Math.Min(material.height - 1,(int)(Mathf.Repeat(1 - tv,1) * material.height));
                        if (material.alpha[y * material.width + x] < 128) continue;
                    }
                    best = distance; bestFace = face;
                }
            }
            thing = PickThingDistance(ray,best,out best);
            return bestFace;
        }
        void UpdateHighlight()
        {
            if (highlight == null) return;
            UpdateSurfaceHighlight();
        }
    }
}
