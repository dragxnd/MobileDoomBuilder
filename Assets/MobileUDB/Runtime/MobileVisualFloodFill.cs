using System;
using System.Collections.Generic;
using MobileUDB.Core;

namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        void VisualFloodFill()
        {
            if(!VisualActive)return;
            try
            {
                var visible=visualView.FloodFillSurfaces();
                if(!visualView.Picked.HasValue)return;var primary=visualView.Picked.Value;
                string texture=surfaceClipboard.Texture(primary);
                if(texture==null){Notify("Сначала скопируйте текстуру стены или пола / потолка.");return;}
                if(string.Equals(VisualGeometry.Texture(Map,primary),texture,StringComparison.OrdinalIgnoreCase))
                {Notify("Поверхность уже имеет скопированную текстуру.");return;}
                var marks=visualView.SnapshotSelection();
                var targets=TextureFloodFill.Targets(Map,primary,visible,marks.surfaces,visualView.SelectionCount>1);
                if(targets.Count==0){Notify("Нет связанных поверхностей с этой текстурой.");return;}
                SurfaceBatchResult result=null;
                if(Edit(m=>result=SurfaceBatchEdits.Textures(m,targets,primary.IsFlat?null:texture,primary.IsFlat?texture:null,marks.surfaces),"Заливка текстурой: "+texture+" · поверхностей: "+targets.Count+"."))
                    visualView.RebindSelection(result,marks);
            }
            catch(Exception error){Notify(error.Message);}
        }
    }
    public sealed partial class VisualView
    {
        internal List<SurfaceRef> FloodFillSurfaces()
        {
            if(rebuild)Rebuild();var result=new List<SurfaceRef>(faces.Count);
            foreach(var face in faces)result.Add(face.surface.id);return result;
        }
    }
}
