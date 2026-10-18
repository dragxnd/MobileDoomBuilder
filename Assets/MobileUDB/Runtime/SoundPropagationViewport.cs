// GPL-3.0. Cached, batched sector geometry; only reached sectors receive bright sound colors.
using System;
using System.Collections.Generic;
using MobileUDB.Core;
using UnityEngine;
using UnityEngine.UI;
namespace MobileUDB
{
    public sealed partial class MapViewport
    {
        internal struct SoundTriangle {public MapTriangle triangle;public int sector;}
        readonly List<SoundTriangle> soundTriangles=new List<SoundTriangle>();
        readonly List<SoundFillGraphic> soundFills=new List<SoundFillGraphic>();
        MapDocument soundFillMap;long soundFillRevision=-1;
        void RefreshSoundLayers()
        {
            if(!editor.SoundPropagationActive){foreach(var fill in soundFills)if(fill!=null)fill.gameObject.SetActive(false);return;}
            editor.RefreshSoundData();
            if(!ReferenceEquals(soundFillMap,editor.Map)||soundFillRevision!=editor.MapContentRevision)
            {
                foreach(var fill in soundFills){fill.gameObject.SetActive(false);Destroy(fill.gameObject);}soundFills.Clear();soundTriangles.Clear();
                soundFillMap=editor.Map;soundFillRevision=editor.MapContentRevision;var tessellation=new SectorTessellation(soundFillMap);
                for(int sector=0;sector<soundFillMap.sectors.Count;sector++)
                {
                    try{foreach(var triangle in tessellation.Triangles(sector))soundTriangles.Add(new SoundTriangle{triangle=triangle,sector=sector});}
                    catch(InvalidOperationException){/* An open sector still has its boundary drawn and can be inspected. */}
                }
                for(int first=0;first<soundTriangles.Count;first+=18000)
                {var fill=Layer<SoundFillGraphic>("Sound sector fill");fill.owner=this;fill.first=first;fill.count=Math.Min(18000,soundTriangles.Count-first);soundFills.Add(fill);}
                overlay.transform.SetAsLastSibling();layerDirty=true;
            }
            foreach(var fill in soundFills){fill.gameObject.SetActive(true);if(layerDirty)fill.SetVerticesDirty();}
        }
        internal Color32 SoundSectorColor(int sector)
        {
            var levels=editor.SoundLevels;byte level=levels==null||sector>=levels.Length?SoundPropagation.Unreachable:levels[sector];
            if(sector==editor.SoundSource)return new Color32(65,239,105,255);
            if(level==SoundPropagation.Free)return new Color32(30,177,66,255);
            if(level==SoundPropagation.BehindBlock)return new Color32(209,181,39,255);
            return new Color32(25,33,44,255);
        }
        internal void PopulateSoundFill(VertexHelper mesh,int first,int count)
        {
            mesh.Clear();var rect=rectTransform.rect;
            for(int i=first;i<first+count;i++)
            {
                var item=soundTriangles[i];var t=item.triangle;var a=Project(t.a);var b=Project(t.b);var c=Project(t.c);
                if(Mathf.Max(a.x,Mathf.Max(b.x,c.x))<rect.xMin||Mathf.Min(a.x,Mathf.Min(b.x,c.x))>rect.xMax||Mathf.Max(a.y,Mathf.Max(b.y,c.y))<rect.yMin||Mathf.Min(a.y,Mathf.Min(b.y,c.y))>rect.yMax)continue;
                int start=mesh.currentVertCount;var color=SoundSectorColor(item.sector);mesh.AddVert(a,color,Vector2.zero);mesh.AddVert(b,color,Vector2.zero);mesh.AddVert(c,color,Vector2.zero);mesh.AddTriangle(start,start+1,start+2);
            }
        }
        void PopulateSoundPropagation(VertexHelper mesh)
        {
            var rect=rectTransform.rect;var map=editor.Map;
            for(int i=0;i<map.lines.Count&&mesh.currentVertCount<60000;i++)
            {
                var line=map.lines[i];var a=Project(map.vertices[line.start].Position);var b=Project(map.vertices[line.end].Position);if(!Visible(a,b,rect))continue;
                bool blocked=(line.flags&SoundPropagation.BlockSoundFlag)!=0;
                bool source=editor.SoundSource>=0&&((line.front>=0&&map.sides[line.front].sector==editor.SoundSource)||(line.back>=0&&map.sides[line.back].sector==editor.SoundSource));
                var color=blocked?new Color32(255,68,68,255):source?new Color32(156,255,183,255):new Color32(114,134,149,255);
                Line(mesh,a,b,blocked||source?2.5f:1.5f,color);
            }
            foreach(var screen in fingers)Ring(mesh,Local(screen),18,2,new Color32(105,225,255,255));
        }
    }
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class SoundFillGraphic:MaskableGraphic
    {
        internal MapViewport owner;internal int first,count;
        protected override void OnPopulateMesh(VertexHelper mesh){if(owner==null)mesh.Clear();else owner.PopulateSoundFill(mesh,first,count);}
    }
}
