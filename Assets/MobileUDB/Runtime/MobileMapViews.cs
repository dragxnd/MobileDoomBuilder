using System;using System.Collections.Generic;using MobileUDB.Core;using UnityEngine;
namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        readonly Dictionary<string,MapView> mapViews=new Dictionary<string,MapView>(StringComparer.OrdinalIgnoreCase);
        readonly SaveRevision viewRecoveryRevision=new SaveRevision();
        bool classicViewRestorePending,classicViewTracked;
        Vector2 trackedViewCenter;float trackedViewScale;
        MapView CurrentClassicView()
        {MapView saved;mapViews.TryGetValue(MapNaming.Key(Map),out saved);return new MapView{x=View.center.x,y=View.center.y,scale=View.zoom,camera=saved==null?null:saved.camera}.Validated();}
        MapView CaptureClassicView(){EnsureClassicView();return CurrentClassicView();}
        void QueueClassicViewRestore(float delay)
        {CancelInvoke(nameof(Fit));CancelInvoke(nameof(RestoreClassicView));classicViewRestorePending=true;classicViewTracked=false;Invoke(nameof(RestoreClassicView),delay);}
        void EnsureClassicView(){if(classicViewRestorePending)RestoreClassicView();MatchDynamicGrid();}
        void RestoreClassicView()
        {
            CancelInvoke(nameof(RestoreClassicView));
            MapView saved;if(mapViews.TryGetValue(MapNaming.Key(Map),out saved))ApplyClassicView(saved);else View.Fit();
            classicViewRestorePending=false;classicViewTracked=true;trackedViewCenter=View.center;trackedViewScale=View.zoom;
            mapViews[MapNaming.Key(Map)]=CurrentClassicView();
            if(VisualActive){visualView.ResetCamera();RestoreVisualCamera();}
        }
        void ApplyClassicView(MapView saved)
        {saved=saved.Validated();View.center=new Vector2(saved.x,saved.y);View.zoom=Mathf.Clamp(saved.scale,.025f,20);View.SetVerticesDirty();}
        void ObserveClassicView()
        {
            if(View==null||Map==null||classicViewRestorePending)return;
            MatchDynamicGrid();
            if(classicViewTracked&&trackedViewCenter.x==View.center.x&&trackedViewCenter.y==View.center.y&&trackedViewScale==View.zoom)return;
            var view=CurrentClassicView();
            if(classicViewTracked){viewRecoveryRevision.Changed();saveAt=Time.unscaledTime+2;}
            classicViewTracked=true;trackedViewCenter=View.center;trackedViewScale=View.zoom;mapViews[MapNaming.Key(Map)]=view;
        }
        void RememberClassicView(){EnsureClassicView();ObserveClassicView();ObserveVisualCamera();mapViews[MapNaming.Key(Map)]=CaptureClassicView();}
        internal void RestoreVisualCamera()
        {
            if(!VisualActive||classicViewRestorePending)return;
            MapView saved;if(mapViews.TryGetValue(MapNaming.Key(Map),out saved)&&saved.camera!=null)visualView.ApplyCamera(saved.camera);
            ObserveVisualCamera();
        }
        void ObserveVisualCamera()
        {
            if(!VisualActive||Map==null||classicViewRestorePending)return;
            var camera=visualView.CaptureCamera();var view=CurrentClassicView();
            if(MapCamera.Same(view.camera,camera))return;
            view.camera=camera;mapViews[MapNaming.Key(Map)]=view;
            viewRecoveryRevision.Changed();saveAt=Time.unscaledTime+2;
        }
        string SynchronizeCameraMarker(MapDocument map,out bool changed)
        {
            changed=false;if(!VisualActive||classicViewRestorePending)return null;
            try{changed=MapCameraMarker.Synchronize(map,visualView.CaptureCamera());return null;}
            catch(InvalidOperationException e){return "Камера WAD не обновлена: "+e.Message;}
        }
        MapDocument SnapshotMapWithCamera()
        {string warning;return SnapshotMapWithCamera(out warning);}
        MapDocument SnapshotMapWithCamera(out string warning)
        {var copy=Map.Clone();bool changed;warning=SynchronizeCameraMarker(copy,out changed);return copy;}
    }
}
