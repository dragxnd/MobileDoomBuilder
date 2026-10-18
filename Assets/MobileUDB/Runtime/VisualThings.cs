using System;
using System.Collections.Generic;
using MobileUDB.Core;
using UnityEngine;

namespace MobileUDB
{
    public sealed partial class VisualView
    {
        sealed class VisualThing
        {
            public int index, light; public ThingType type; public MapThing thing; public Vector3 origin;
            public SpriteFrame[] frames = new SpriteFrame[8]; public SpriteFrame frame; public string key;
            public readonly Vector3[] points = new Vector3[4]; public Vector2[] uv = new Vector2[4]; public Bounds bounds;
        }
        readonly List<VisualThing> things = new List<VisualThing>();
        readonly Dictionary<string,Batch> thingBatches = new Dictionary<string,Batch>(StringComparer.OrdinalIgnoreCase);
        Vector3 thingsCamera = new Vector3(float.MaxValue,0,0); float thingsYaw;
        public int PickedThing { get; private set; } = -1;
        public int ThingCount { get { return things.Count; } }
        public int ThingBatchCount { get { return thingBatches.Count; } }
        static readonly int[] ThingIndices = { 0,1,2,0,2,3 };
        void ClearThings()
        {
            foreach (var batch in thingBatches.Values) { if (batch.go != null) { batch.go.SetActive(false); Destroy(batch.go); } if (batch.mesh != null) Destroy(batch.mesh); }
            thingBatches.Clear(); things.Clear(); thingsCamera = new Vector3(float.MaxValue,0,0);
        }
        string ThingKey(ThingType type, SpriteFrame frame) { return "Sprite/" + (frame == null ? "?" : frame.image.name) + "/" + type.alpha.ToString("R",System.Globalization.CultureInfo.InvariantCulture); }
        void ThingMaterial(VisualThing thing, SpriteFrame frame)
        {
            string key = ThingKey(thing.type,frame); MaterialEntry entry;
            if (!materials.TryGetValue(key,out entry))
            {
                var mat = new Material(shader); mat.name = "UDB " + key; mat.mainTexture = missing; mat.SetFloat("_Cull",0);
                mat.SetColor("_Tint",new Color(1,1,1,thing.type.alpha));
                if (thing.type.alpha < 1) { mat.SetFloat("_ZWrite",0); mat.renderQueue = 3000; }
                entry = new MaterialEntry { material = mat,source = frame == null ? null : frame.image,sprite = true }; materials.Add(key,entry);
            }
            entry.distance = Math.Min(entry.distance,(thing.origin - renderCamera.transform.position).sqrMagnitude);
        }
        void BuildThings()
        {
            for (int i = 0; i < editor.Map.things.Count; i++)
            {
                var thing = editor.Map.things[i]; var type = editor.ThingTypes.Find(thing.type);
                int sector = ThingPlacement.Sector(editor.Map,thing.Position);
                var visual = new VisualThing { index = i,thing = thing,type = type,light = type.bright ? 255 : sector < 0 ? 192 : Mathf.Clamp(editor.Map.sectors[sector].light,0,255),
                    origin = new Vector3(thing.x,ThingPlacement.BaseHeight(editor.Map,thing,type,sector),thing.y) * Units };
                for (int r = 1; r <= 8; r++) { visual.frames[r - 1] = editor.ResourceCatalog.FindSprite(type.sprite,r); ThingMaterial(visual,visual.frames[r - 1]); }
                things.Add(visual);
            }
            if (PickedThing >= things.Count) PickedThing = -1;
            UpdateThings();
        }
        void UpdateThings()
        {
            if (things.Count == 0 || (thingsCamera == renderCamera.transform.position && thingsYaw == yaw)) return;
            thingsCamera = renderCamera.transform.position; thingsYaw = yaw;
            var cameraPoint = new MapPoint(thingsCamera.x / Units,thingsCamera.z / Units);
            Vector3 cameraRight = Quaternion.Euler(0,yaw,0) * Vector3.right;
            foreach (var batch in thingBatches.Values) { batch.vertices.Clear(); batch.uv.Clear(); batch.colors.Clear(); batch.indices.Clear(); }
            foreach (var visual in things)
            {
                visual.frame = visual.frames[ThingPlacement.Rotation(visual.thing,cameraPoint) - 1]; visual.key = ThingKey(visual.type,visual.frame);
                var source = visual.frame == null ? null : visual.frame.image;
                float sx = visual.type.spriteScale * VisualGeometry.Field(visual.thing,"scalex",1), sy = visual.type.spriteScale * VisualGeometry.Field(visual.thing,"scaley",1);
                if (Mathf.Abs(sx) < .001f) sx = .001f; if (Mathf.Abs(sy) < .001f) sy = .001f;
                float left = source == null ? -ThingPlacement.Radius(visual.thing,visual.type) : -source.offsetX;
                float right = source == null ? ThingPlacement.Radius(visual.thing,visual.type) : source.width - source.offsetX;
                float bottom = source == null ? 0 : source.offsetY - source.height;
                float top = source == null ? ThingPlacement.Height(visual.thing,visual.type) : source.offsetY;
                Vector3 axis = visual.type.fixedRotation ? Quaternion.Euler(0,90 - visual.thing.angle,0) * Vector3.right : cameraRight;
                visual.points[0] = visual.origin + axis * (left * sx * Units) + Vector3.up * (bottom * sy * Units);
                visual.points[1] = visual.origin + axis * (left * sx * Units) + Vector3.up * (top * sy * Units);
                visual.points[2] = visual.origin + axis * (right * sx * Units) + Vector3.up * (top * sy * Units);
                visual.points[3] = visual.origin + axis * (right * sx * Units) + Vector3.up * (bottom * sy * Units);
                bool mirror = visual.frame != null && visual.frame.mirror;
                visual.uv[0] = new Vector2(mirror ? 1 : 0,0); visual.uv[1] = new Vector2(mirror ? 1 : 0,1); visual.uv[2] = new Vector2(mirror ? 0 : 1,1); visual.uv[3] = new Vector2(mirror ? 0 : 1,0);
                visual.bounds = new Bounds(visual.points[0],Vector3.zero); foreach (var point in visual.points) visual.bounds.Encapsulate(point);
                Batch batch;
                if (!thingBatches.TryGetValue(visual.key,out batch))
                {
                    batch = new Batch(); batch.mesh = new Mesh(); batch.mesh.name = "UDB " + visual.key; batch.mesh.MarkDynamic();
                    batch.go = new GameObject("Thing batch " + visual.key,typeof(MeshFilter),typeof(MeshRenderer)); batch.go.layer = 31; batch.go.transform.SetParent(worldRoot.transform,false);
                    batch.go.GetComponent<MeshFilter>().sharedMesh = batch.mesh; var renderer = batch.go.GetComponent<MeshRenderer>(); renderer.sharedMaterial = materials[visual.key].material;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false; thingBatches.Add(visual.key,batch);
                }
                int n = batch.vertices.Count; batch.vertices.AddRange(visual.points); batch.uv.AddRange(visual.uv);
                byte light = ClassicMapFill.RenderBrightness(editor.Map,visual.light,editor.View.fullBright); for (int i = 0; i < 4; i++) batch.colors.Add(new Color32(light,light,light,255));
                foreach (int index in ThingIndices) batch.indices.Add(n + index);
            }
            foreach (var batch in thingBatches.Values)
            {
                batch.go.SetActive(editor.ThingsVisible&&batch.indices.Count > 0); if (batch.indices.Count == 0) continue;
                batch.mesh.Clear(); if (batch.vertices.Count > 65535) batch.mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                batch.mesh.SetVertices(batch.vertices); batch.mesh.SetUVs(0,batch.uv); batch.mesh.SetColors(batch.colors); batch.mesh.SetTriangles(batch.indices,0); batch.mesh.RecalculateBounds();
            }
            if (selectedThings.Count>0) UpdateHighlight();
        }
        internal void RefreshThingsVisibility()
        {
            foreach(var batch in thingBatches.Values)if(batch.go!=null)batch.go.SetActive(editor.ThingsVisible&&batch.indices.Count>0);
            if(highlight!=null)UpdateHighlight();
        }
        int PickThing(Ray ray,float nearestSurface)
        {float nearest;return PickThingDistance(ray,nearestSurface,out nearest);}
        int PickThingDistance(Ray ray, float nearestSurface,out float nearest)
        {
            nearest=nearestSurface;if(!editor.ThingsVisible)return -1;
            UpdateThings(); int picked = -1;
            foreach (var thing in things)
            {
                float near; if (!thing.bounds.IntersectRay(ray,out near) || near > nearest) continue;
                Vector3 horizontal = thing.points[3] - thing.points[0], vertical = thing.points[1] - thing.points[0];
                var normal = Vector3.Cross(horizontal,vertical).normalized; float dot = Vector3.Dot(normal,ray.direction); if (Mathf.Abs(dot) < .000001f) continue;
                float distance = Vector3.Dot(thing.points[0] - ray.origin,normal) / dot; if (distance <= 0 || distance >= nearest) continue;
                var point = ray.GetPoint(distance) - thing.points[0]; float u = Vector3.Dot(point,horizontal) / horizontal.sqrMagnitude, v = Vector3.Dot(point,vertical) / vertical.sqrMagnitude;
                if (u < 0 || u > 1 || v < 0 || v > 1) continue;
                MaterialEntry entry = materials[thing.key];
                if (entry.alpha != null)
                {
                    if (thing.frame != null && thing.frame.mirror) u = 1 - u;
                    int x = Mathf.Clamp((int)(u * entry.width),0,entry.width - 1), y = Mathf.Clamp((int)((1 - v) * entry.height),0,entry.height - 1);
                    if (entry.alpha[y * entry.width + x] < 128) continue;
                }
                nearest = distance; picked = thing.index;
            }
            return picked;
        }
    }
}
