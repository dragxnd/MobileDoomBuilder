using System.Collections.Generic;
using MobileUDB.Core;
using UnityEngine;

namespace MobileUDB
{
    public sealed partial class VisualView
    {
        readonly OrderedSurfaceSelection selectedSurfaces=new OrderedSurfaceSelection();
        readonly OrderedThingSelection selectedThings=new OrderedThingSelection();
        public IReadOnlyCollection<SurfaceRef> SelectedSurfaces {get{return selectedSurfaces;}}
        public IReadOnlyCollection<int> SelectedThings {get{return selectedThings;}}
        public int SelectionCount {get{return selectedSurfaces.Count+selectedThings.Count;}}
        internal sealed class SelectionSnapshot
        {
            public List<SurfaceRef> surfaces;public List<int> things;public SurfaceRef? primary;public int primaryThing;
        }
        internal SelectionSnapshot SnapshotSelection()
        {return new SelectionSnapshot{surfaces=new List<SurfaceRef>(selectedSurfaces),things=new List<int>(selectedThings),primary=Picked,primaryThing=PickedThing};}
        void SelectSurface(SurfaceRef? surface)
        {
            if(!editor.VisualMultipleSelection){selectedSurfaces.Clear();selectedThings.Clear();Picked=null;PickedThing=-1;}
            if(surface.HasValue)
            {
                var id=surface.Value;
                if(editor.VisualMultipleSelection&&!selectedSurfaces.Add(id))selectedSurfaces.Remove(id);
                else selectedSurfaces.Add(id);
                Picked=selectedSurfaces.Contains(id)?surface:null;PickedThing=-1;
            }
            EnsurePrimary();
            editor.SelectVisualSurface(Picked);UpdateHighlight();
        }
        void SelectThing(int index)
        {
            if(!editor.VisualMultipleSelection){selectedSurfaces.Clear();selectedThings.Clear();}
            if(editor.VisualMultipleSelection&&!selectedThings.Add(index))selectedThings.Remove(index);else selectedThings.Add(index);
            Picked=null;PickedThing=selectedThings.Contains(index)?index:-1;EnsurePrimary();
            editor.SelectVisualSurface(Picked);UpdateHighlight();
        }
        SurfaceRef? FirstSelected() {foreach(var id in selectedSurfaces)return id;return null;}
        void EnsurePrimary()
        {
            if(PickedThing>=0&&selectedThings.Contains(PickedThing)){Picked=null;return;}
            PickedThing=-1;if(Picked.HasValue&&selectedSurfaces.Contains(Picked.Value))return;
            Picked=FirstSelected();if(!Picked.HasValue)foreach(int index in selectedThings){PickedThing=index;break;}
        }
        internal void CollapseSurfaceSelection()
        {
            var current=Picked;int thing=PickedThing;selectedSurfaces.Clear();selectedThings.Clear();
            if(current.HasValue)selectedSurfaces.Add(current.Value);else if(thing>=0)selectedThings.Add(thing);
            EnsurePrimary();editor.SelectVisualSurface(Picked);UpdateHighlight();
        }
        internal void RebindSelection(SurfaceBatchResult result,SelectionSnapshot marks)
        {
            selectedSurfaces.Clear();foreach(var id in marks.surfaces)
            {SurfaceRef rebound;selectedSurfaces.Add(result.rebound.TryGetValue(id,out rebound)?rebound:id);}
            selectedThings.Clear();foreach(int index in marks.things)if(index>=0&&index<editor.Map.things.Count)selectedThings.Add(index);
            Picked=null;PickedThing=marks.primaryThing;
            if(marks.primary.HasValue){SurfaceRef rebound;Picked=result.rebound.TryGetValue(marks.primary.Value,out rebound)?rebound:marks.primary;PickedThing=-1;}
            EnsurePrimary();
            RequestRebuild();editor.SelectVisualSurface(Picked);
        }
        internal void ForgetThingSelection()
        {selectedThings.Clear();PickedThing=-1;EnsurePrimary();RequestRebuild();editor.SelectVisualSurface(Picked);}
        void UpdateSurfaceHighlight()
        {
            var visible=new HashSet<SurfaceRef>();foreach(var face in faces)visible.Add(face.surface.id);
            bool pruned=selectedSurfaces.RemoveWhere(id=>!visible.Contains(id))>0;
            pruned|=selectedThings.RemoveWhere(index=>index<0||index>=things.Count)>0;EnsurePrimary();
            if(pruned)editor.SelectVisualSurface(Picked);
            var points=new List<Vector3>();var indices=new List<int>();
            foreach(var face in faces)if(selectedSurfaces.Contains(face.surface.id))
            {int first=points.Count;points.AddRange(face.points);foreach(int index in face.surface.indices)indices.Add(first+index);}
            foreach(var thing in things)if(editor.ThingsVisible&&selectedThings.Contains(thing.index))
            {int first=points.Count;points.AddRange(thing.points);foreach(int index in ThingIndices)indices.Add(first+index);}
            highlight.SetActive(points.Count>0);
            if(highlightMesh!=null)highlightMesh.Clear();if(points.Count==0)return;
            if(highlightMesh==null){highlightMesh=new Mesh();highlightMesh.name="UDB selected elements";}
            highlightMesh.indexFormat=points.Count>65535?UnityEngine.Rendering.IndexFormat.UInt32:UnityEngine.Rendering.IndexFormat.UInt16;
            highlightMesh.SetVertices(points);highlightMesh.SetTriangles(indices,0);
            highlightMesh.RecalculateBounds();
            var colors=new Color32[points.Count];for(int i=0;i<colors.Length;i++)colors[i]=Color.white;highlightMesh.colors32=colors;
            highlight.GetComponent<MeshFilter>().sharedMesh=highlightMesh;
        }
    }
}
