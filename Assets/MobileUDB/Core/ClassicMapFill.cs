// GPL-3.0. Binary 2D modes follow UDB Renderer2D/SurfaceManager and BuilderPlug.
using System;using System.Collections.Generic;
namespace MobileUDB.Core
{
    public enum ClassicViewMode { Wireframe, Brightness, Floors, Ceilings }
    public struct ClassicFillTriangle
    {
        public MapTriangle triangle;public byte brightness;public int sector;
        public ClassicFillTriangle(MapTriangle triangle,byte brightness,int sector){this.triangle=triangle;this.brightness=brightness;this.sector=sector;}
    }
    public sealed class ClassicFillBatch
    {
        public string texture;public ResourceTexture source;public readonly List<ClassicFillTriangle> triangles=new List<ClassicFillTriangle>();
    }
    public sealed class ClassicFillPlan
    {
        public readonly List<ClassicFillBatch> batches=new List<ClassicFillBatch>();
        public readonly List<string> warnings=new List<string>();
    }
    public static class ClassicMapFill
    {
        public static byte Brightness(int level,bool doomLightLevels=true)
        {if(level<192&&doomLightLevels)level=(int)(192.0f-(192.0f-level)*1.5f);return (byte)Math.Max(0,Math.Min(255,level));}
        public static byte RenderBrightness(MapDocument map,int level,bool fullBright=false)
        {return fullBright?(byte)255:Brightness(level,GameSky.DoomLightLevels(map));}
        public static MapPoint UV(MapPoint point,ResourceTexture source)
        {
            float width=source==null?64:source.width*source.scaleX,height=source==null?64:source.height*source.scaleY;
            if(float.IsNaN(width)||float.IsInfinity(width)||width<=0)width=64;if(float.IsNaN(height)||float.IsInfinity(height)||height<=0)height=64;
            // UDB uses u=x/ScaledWidth,v=-y/ScaledHeight. Unity texture rows are flipped.
            return new MapPoint(point.x/width,1+point.y/height);
        }
        public static ClassicFillPlan Build(MapDocument map,DoomResources resources,ClassicViewMode mode,bool fullBright=false)
        {
            var plan=new ClassicFillPlan();if(map==null||mode==ClassicViewMode.Wireframe)return plan;
            var tessellation=new SectorTessellation(map);var batches=new Dictionary<string,ClassicFillBatch>(StringComparer.OrdinalIgnoreCase);
            for(int i=0;i<map.sectors.Count;i++)
            {
                List<MapTriangle> triangles;try{triangles=tessellation.Triangles(i);}catch(InvalidOperationException error){if(plan.warnings.Count<8)plan.warnings.Add(error.Message);continue;}
                if(triangles.Count==0)continue;var sector=map.sectors[i];string name=mode==ClassicViewMode.Brightness?"":mode==ClassicViewMode.Ceilings?sector.ceilingTexture:sector.floorTexture;
                name=name??"-";ClassicFillBatch batch;if(!batches.TryGetValue(name,out batch)){batch=new ClassicFillBatch{texture=name,source=mode==ClassicViewMode.Brightness||resources==null?null:resources.Find(name,ResourceKind.Flat,false)};batches.Add(name,batch);plan.batches.Add(batch);}
                byte light=RenderBrightness(map,sector.light,fullBright&&mode!=ClassicViewMode.Brightness);
                foreach(var triangle in triangles)batch.triangles.Add(new ClassicFillTriangle(triangle,light,i));
            }
            return plan;
        }
    }
}
