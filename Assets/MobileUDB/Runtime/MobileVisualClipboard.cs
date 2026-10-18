using System;
using System.Collections.Generic;
using MobileUDB.Core;
using UnityEngine.UI;

namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        readonly SurfaceTextureClipboard surfaceClipboard=new SurfaceTextureClipboard();
        Button visualCopyTextureButton,visualPasteTextureButton;
        void RefreshVisualClipboardButtons()
        {
            if(visualCopyTextureButton==null)return;
            var surface=visualView.Picked;
            visualCopyTextureButton.interactable=surface.HasValue&&surfaceClipboard.CanCopy(Map,surface.Value);
            bool canPaste=false;foreach(var id in visualView.SelectedSurfaces)if(surfaceClipboard.CanPaste(Map,id))canPaste=true;
            visualPasteTextureButton.interactable=canPaste;
            string copied=surface.HasValue?surfaceClipboard.Texture(surface.Value):null;
            visualPasteTextureButton.GetComponent<MobileIconButton>().hint=copied==null?"Вставить скопированную текстуру поверхности":"Вставить текстуру: " + copied;
        }
        void VisualCopyTexture()
        {
            if(!VisualActive||!visualView.Picked.HasValue)return;
            var surface=visualView.Picked.Value;
            try
            {
                surfaceClipboard.Copy(Map,surface);
                Notify((surface.IsFlat?"Пол / потолок: ":"Стена: ") + surfaceClipboard.Texture(surface) + " скопирована. Выберите другую поверхность и нажмите «Вставить».");
            }
            catch(Exception error){Notify(error.Message);}
        }
        void VisualPasteTexture()
        {
            if(!VisualActive||visualView.SelectedSurfaces.Count==0)return;
            ApplyVisualTextures(new List<SurfaceRef>(visualView.SelectedSurfaces),surfaceClipboard.Wall,surfaceClipboard.Flat);
        }
        void ApplyVisualTexture(SurfaceRef surface,string texture)
        {
            ApplyVisualTextures(VisualTextureTargets(surface),surface.IsFlat?null:texture,surface.IsFlat?texture:null);
        }
        void ApplyVisualTextures(List<SurfaceRef> targets,string wall,string flat)
        {
            bool changes=false;
            foreach(var id in targets)
            {
                if(!SurfaceTextureEdits.Valid(Map,id)){Notify("Поверхность больше не существует. Выберите её снова.");return;}
                string name=id.IsFlat?flat:wall;
                if(name!=null&&!string.Equals(VisualGeometry.Texture(Map,id),name,StringComparison.OrdinalIgnoreCase))changes=true;
            }
            if(!changes){Notify(wall==null&&flat==null?"Сначала скопируйте текстуру.":"Подходящие поверхности уже имеют эту текстуру.");return;}
            var marks=visualView.SnapshotSelection();SurfaceBatchResult result=null;
            if(Edit(m=>result=SurfaceBatchEdits.Textures(m,targets,wall,flat),"Текстуры изменены."))visualView.RebindSelection(result,marks);
        }
    }
}
