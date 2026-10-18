using System;
using MobileUDB.Core;
using UnityEngine;

namespace MobileUDB
{
    public sealed partial class VisualView
    {
        struct PaintTarget : IEquatable<PaintTarget>
        {
            public SurfaceRef? surface;
            public int thing;
            public int Family { get { return thing >= 0 ? 3 : surface.HasValue ? surface.Value.IsFlat ? 2 : 1 : 0; } }
            public bool Equals(PaintTarget other) { return thing == other.thing && Nullable.Equals(surface,other.surface); }
        }
        bool paintTracking;
        int paintFamily;
        PaintTarget paintLast;
        static PaintTarget EmptyPaintTarget { get { return new PaintTarget { thing = -1 }; } }

        PaintTarget PaintTargetAt(Vector2 screen)
        {
            if(!PlacementReady || !ScreenRect.Contains(screen)) return EmptyPaintTarget;
            Rect rect = ScreenRect;
            var location = new Vector2((screen.x-rect.xMin)/rect.width,(screen.y-rect.yMin)/rect.height);
            float distance; int thing; var face = FindPick(renderCamera.ViewportPointToRay(location),out thing,out distance);
            return new PaintTarget { thing = thing, surface = thing >= 0 || face == null ? (SurfaceRef?)null : face.surface.id };
        }

        internal void BeginPaintSelection(Vector2 screen)
        {
            CancelLook(); if(!PlacementReady || !ScreenRect.Contains(screen)) return;
            paintTracking = true; var hit = PaintTargetAt(screen); paintFamily = hit.Family; paintLast = EmptyPaintTarget;
            if(PaintTargetSelection(hit)) PublishPaintSelection();
        }

        bool PaintTargetSelection(PaintTarget hit)
        {
            if(hit.Equals(paintLast)) return false;
            // UDB remembers all highlight transitions, including empty space and
            // ineligible types. Returning to a target can therefore toggle it again.
            paintLast = hit; if(paintFamily == 0 || hit.Family != paintFamily) return false;
            bool changed;
            if(hit.thing >= 0)
            {
                bool selected = selectedThings.Contains(hit.thing);
                if(editor.VisualBrush == VisualSelectionBrush.Remove || editor.VisualBrush == VisualSelectionBrush.Toggle && selected)
                    changed = selectedThings.Remove(hit.thing);
                else changed = selectedThings.Add(hit.thing);
                if(changed){Picked = null; PickedThing = selectedThings.Contains(hit.thing) ? hit.thing : -1;}
            }
            else
            {
                var id = hit.surface.Value; bool selected = selectedSurfaces.Contains(id);
                if(editor.VisualBrush == VisualSelectionBrush.Remove || editor.VisualBrush == VisualSelectionBrush.Toggle && selected)
                    changed = selectedSurfaces.Remove(id);
                else changed = selectedSurfaces.Add(id);
                if(changed){Picked = selectedSurfaces.Contains(id) ? hit.surface : null; PickedThing = -1;}
            }
            return changed;
        }

        void PublishPaintSelection() { EnsurePrimary(); editor.SelectVisualSurface(Picked); UpdateHighlight(); }
        internal void MovePaintSelection(Vector2 previous,Vector2 screen)
        {
            if(!paintTracking || !editor.PaintingVisualSelection) return;
            // Bounded screen-space sampling; publish highlight/labels once per input.
            int steps = Math.Min(64,Math.Max(1,Mathf.CeilToInt(Vector2.Distance(previous,screen)/8)));
            bool changed = false;
            for(int i=1;i<=steps;i++) changed |= PaintTargetSelection(PaintTargetAt(Vector2.Lerp(previous,screen,i/(float)steps)));
            if(changed) PublishPaintSelection();
        }
        internal void EndPaintSelection(Vector2 screen)
        {
            if(paintTracking && editor.PaintingVisualSelection && PaintTargetSelection(PaintTargetAt(screen))) PublishPaintSelection();
            CancelPaintSelection();
        }
        void CancelPaintSelection() { paintTracking = false; paintFamily = 0; paintLast = EmptyPaintTarget; }
    }
}
