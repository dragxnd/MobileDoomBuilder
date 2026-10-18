using System;
using System.Collections.Generic;
using MobileUDB.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        internal bool VisualMultipleSelection {get;private set;}
        Button visualSelectionButton;
        void ToggleVisualSelection()
        {
            CancelDrag(); VisualBrush=VisualSelectionBrush.Off;
            VisualMultipleSelection=!VisualMultipleSelection;
            if(!VisualMultipleSelection)visualView.CollapseSurfaceSelection();
            Notify(VisualMultipleSelection?"Групповой выбор: касайтесь поверхностей и объектов; повторное касание снимает отметку.":"Выбор одного элемента.");
        }
        List<SurfaceRef> VisualTextureTargets(SurfaceRef primary)
        {
            var result=new List<SurfaceRef>(visualView.SelectedSurfaces);
            if(!result.Contains(primary)){result.Clear();result.Add(primary);}return result;
        }
        HashSet<int> VisualSectorTargets()
        {
            var result=new HashSet<int>();foreach(var id in visualView.SelectedSurfaces)if(SurfaceTextureEdits.Valid(Map,id))result.Add(id.sector);
            return result;
        }
        bool VisualPropertiesAvailable()
        {
            bool walls=false,flats=false;foreach(var id in visualView.SelectedSurfaces){if(id.IsFlat)flats=true;else walls=true;}
            return (walls?1:0)+(flats?1:0)+(visualView.SelectedThings.Count>0?1:0)==1;
        }
        void VisualProperties()
        {
            SelectVisualSurface(visualView.Picked);
            if(VisualPropertiesAvailable())Properties();
        }
        void VisualFilteredProperties(ElementKind kind)
        {
            Selection.Clear();Kind=kind;
            if(kind==ElementKind.Thing)foreach(int index in visualView.SelectedThings)Selection.Add(new ElementRef(kind,index));
            else foreach(var id in visualView.SelectedSurfaces)
                if((kind==ElementKind.Sector)==id.IsFlat)Selection.Add(new ElementRef(kind,id.IsFlat?id.sector:id.line));
            if(Selection.Count>0)Properties();
        }
        void VisualLight(int amount)
        {
            var sectors=VisualSectorTargets();bool changes=false;
            foreach(int index in sectors)if(Mathf.Clamp(Map.sectors[index].light+amount,0,255)!=Map.sectors[index].light)changes=true;
            if(!changes){Notify("Свет уже на предельном значении.");return;}
            Edit(m=>{foreach(int index in sectors)m.sectors[index].light=Mathf.Clamp(m.sectors[index].light+amount,0,255);},"Свет изменён · секторов: "+sectors.Count+".");
        }
        void VisualOffsets(int x,int y)
        {
            var targets=new List<SurfaceRef>(visualView.SelectedSurfaces);var marks=visualView.SnapshotSelection();
            bool walls=false;foreach(var id in targets)if(!id.IsFlat)walls=true;
            if(!walls)return;
            SurfaceBatchResult result=null;
            if(Edit(m=>result=SurfaceBatchEdits.OffsetWalls(m,targets,x,y),"Смещения текстур изменены."))visualView.RebindSelection(result,marks);
        }
        bool VisualMoveWallTextures(int horizontal,int vertical,bool record=true)
        {
            var targets=new List<SurfaceRef>(visualView.SelectedSurfaces);var marks=visualView.SnapshotSelection();
            bool changes=false;var roles=new HashSet<int>();foreach(var id in targets)if(!id.IsFlat)
            {
                if(!SurfaceTextureEdits.Valid(Map,id)){changes=true;break;}
                int role=id.line*2+(Map.lines[id.line].front==id.side?0:1);if(!roles.Add(role))continue;
                var side=Map.sides[id.side];var next=SurfaceBatchEdits.DirectionalOffset(side.offsetX,side.offsetY,horizontal,vertical,id.part,visualView.LoadedWallTextureSize(id));
                if(next.x!=side.offsetX||next.y!=side.offsetY)changes=true;
            }
            if(!changes){Notify("Смещения уже совпадают.");return false;}
            SurfaceBatchResult result=null;
            if(!EditWallTextureStep(m=>{result=SurfaceBatchEdits.MoveWallTextures(m,targets,horizontal,vertical,visualView.LoadedWallTextureSize);if(result.changed==0)throw new InvalidOperationException("Смещения уже совпадают.");},record))return false;
            visualView.RebindSelection(result,marks);return true;
        }
        // The repeat button guards map revision and selection between steps. Only its
        // first successful step records history; each continuation still rolls back on error.
        bool EditWallTextureStep(Action<MapDocument> action,bool record)
        {
            if(record)return Edit(action,"Текстуры перемещены.");
            var before=Map.Clone();try{action(Map);Map.Validate();Notify("Текстуры перемещены.");Changed();return true;}
            catch(Exception e){Map=before;Notify(e.Message);Changed(false);return false;}
        }
        bool SameVisualTextureSelection(VisualView.SelectionSnapshot expected)
        {
            if(!VisualActive)return false;var current=visualView.SnapshotSelection();
            if(current.surfaces.Count!=expected.surfaces.Count||current.things.Count!=expected.things.Count||current.primaryThing!=expected.primaryThing||!Nullable.Equals(current.primary,expected.primary))return false;
            for(int i=0;i<expected.surfaces.Count;i++)if(!current.surfaces[i].Equals(expected.surfaces[i]))return false;
            for(int i=0;i<expected.things.Count;i++)if(current.things[i]!=expected.things[i])return false;return true;
        }
    }
}
