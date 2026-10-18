using System;
using System.Collections.Generic;
using MobileUDB.Core;
using UnityEngine;

namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        static readonly Vector2[] HandleDirections = { new Vector2(-1,-1),new Vector2(0,-1),new Vector2(1,-1),new Vector2(1,0),new Vector2(1,1),new Vector2(0,1),new Vector2(-1,1),new Vector2(-1,0) };
        readonly Vector2[] transformHandles = new Vector2[9];
        HashSet<ElementRef> transformSelection;
        HashSet<int> transformVertices = new HashSet<int>(),transformThings = new HashSet<int>();
        MapPoint transformCenter;
        Vector2 transformExtent,transformStartLocal,transformHandleVector;
        bool transformFrame,transformDragging,transformRotateAllowed;
        int transformHandle;
        float transformAngle,transformScaleX=1,transformScaleY=1,transformPreviousAngle,transformRawAngle;
        void StartTransformHandles()
        {
            if (Draft.Count > 0 || Selection.Count == 0) { Notify("Завершите рисование и выберите элементы для преобразования."); return; }
            CancelDrag(); CancelPaste(); if (VisualActive) ExitVisual(); CloseDialog(); Tool=MobileTool.Transform;
            RefreshTransformFrame();
            if (!transformFrame) { Tool=MobileTool.Select; Notify("Нет элементов для преобразования."); return; }
            View.zoom=Mathf.Clamp(Mathf.Min(Mathf.Max(32,View.rectTransform.rect.width-144)/Mathf.Max(16,transformExtent.x*2),Mathf.Max(32,View.rectTransform.rect.height-180)/Mathf.Max(16,transformExtent.y*2)),.025f,20);
            View.center=new Vector2(transformCenter.x,transformCenter.y+20/View.zoom);
            Changed(false); Notify("Квадраты — размер · круг — поворот · два пальца / Alt — обзор.");
        }
        void RefreshTransformFrame()
        {
            if (transformDragging) return;
            transformFrame=false;
            if (Tool!=MobileTool.Transform || Selection.Count==0) return;
            transformSelection=MapTransforms.WithSectorThings(Map,Selection,includeSectorThings);
            transformVertices=Map.SelectedVertices(transformSelection); transformThings.Clear();
            var min=new Vector2(float.MaxValue,float.MaxValue);var max=new Vector2(float.MinValue,float.MinValue);
            Action<MapPoint> bounds=p=>{var v=new Vector2(p.x,p.y);min=Vector2.Min(min,v);max=Vector2.Max(max,v);};
            foreach(int i in transformVertices) bounds(Map.vertices[i].Position);
            bool canRotate=false;
            foreach(var item in transformSelection)if(item.kind==ElementKind.Thing){transformThings.Add(item.index);bounds(Map.things[item.index].Position);canRotate|=!thingCatalog.Find(Map.things[item.index].type).fixedRotation;}
            if(min.x==float.MaxValue)return;
            transformCenter=MapTransforms.Center(Map,transformSelection);
            var center=new Vector2(transformCenter.x,transformCenter.y);transformExtent=Vector2.Max(max-center,center-min);
            transformRotateAllowed=canRotate||transformExtent.sqrMagnitude>.000001f;transformFrame=true;
        }
        internal bool TransformHandleEnabled(int index)
        {
            if(index==8)return transformRotateAllowed;
            var d=HandleDirections[index];return (d.x!=0&&transformExtent.x>.000001f)||(d.y!=0&&transformExtent.y>.000001f);
        }
        internal Vector2[] TransformHandlePositions()
        {
            if(!transformFrame||Tool!=MobileTool.Transform)return null;
            var center=View.Project(transformCenter);
            var extent=new Vector2(Mathf.Max(36,transformExtent.x*View.zoom)+20,Mathf.Max(36,transformExtent.y*View.zoom)+20);
            for(int i=0;i<9;i++)
            {
                var p=i==8?new Vector2(0,extent.y+44):Vector2.Scale(HandleDirections[i],extent);
                if(transformDragging)p=TransformLocalVector(p);
                transformHandles[i]=center+p;
            }
            return transformHandles;
        }
        Vector2 TransformLocalVector(Vector2 p)
        {
            float radians=transformAngle*Mathf.Deg2Rad,c=Mathf.Cos(radians),s=Mathf.Sin(radians);
            p=Vector2.Scale(p,new Vector2(transformScaleX,transformScaleY));return new Vector2(c*p.x-s*p.y,s*p.x+c*p.y);
        }
        void BeginTransformPointer(Vector2 screen)
        {
            RefreshTransformFrame();var handles=TransformHandlePositions();if(handles==null)return;
            var local=View.Local(screen);int hit=-1;float distance=22*22;
            for(int i=0;i<handles.Length;i++)if(TransformHandleEnabled(i)){float d=(handles[i]-local).sqrMagnitude;if(d<distance){hit=i;distance=d;}}
            if(hit<0){Notify("Потяните квадрат размера или круг поворота. Для обзора используйте два пальца.");return;}
            transformHandle=hit;transformStartLocal=local;transformHandleVector=handles[hit]-View.Project(transformCenter);
            transformAngle=transformRawAngle=0;transformScaleX=transformScaleY=1;
            var delta=local-View.Project(transformCenter);transformPreviousAngle=Mathf.Atan2(delta.y,delta.x)*Mathf.Rad2Deg;
            transformDragging=true;View.SetVerticesDirty();
        }
        void MoveTransformPointer(Vector2 screen)
        {
            if(!transformDragging)return;
            var local=View.Local(screen);
            if((local-transformStartLocal).sqrMagnitude<9&&!moved)return;
            if(transformHandle==8)
            {
                var delta=local-View.Project(transformCenter);if(delta.sqrMagnitude<16)return;
                float angle=Mathf.Atan2(delta.y,delta.x)*Mathf.Rad2Deg;transformRawAngle+=Mathf.DeltaAngle(transformPreviousAngle,angle);transformPreviousAngle=angle;
                transformAngle=SnapEnabled?Mathf.Round(transformRawAngle/15)*15:transformRawAngle;
            }
            else
            {
                var d=HandleDirections[transformHandle];var delta=local-transformStartLocal;
                if(d.x!=0&&transformExtent.x>.000001f)transformScaleX=Mathf.Clamp(1+delta.x/transformHandleVector.x,.01f,100);
                if(d.y!=0&&transformExtent.y>.000001f)transformScaleY=Mathf.Clamp(1+delta.y/transformHandleVector.y,.01f,100);
                if(SnapEnabled){transformScaleX=Mathf.Max(.05f,Mathf.Round(transformScaleX*20)/20);transformScaleY=Mathf.Max(.05f,Mathf.Round(transformScaleY*20)/20);}
            }
            View.SetVerticesDirty();Notify("Угол "+transformAngle.ToString("0.#")+"° · X "+(transformScaleX*100).ToString("0.#")+"% · Y "+(transformScaleY*100).ToString("0.#")+"%. Отпустите для применения.");
        }
        void EndTransformPointer(Vector2 screen)
        {
            if(!transformDragging)return;
            if(!View.ScreenRect.Contains(screen)){CancelTransformGesture();Notify("Преобразование отменено: палец вне карты.");return;}
            MoveTransformPointer(screen);
            var selected=new HashSet<ElementRef>(transformSelection);float angle=transformAngle,x=transformScaleX,y=transformScaleY;
            bool changed=(View.Local(screen)-transformStartLocal).sqrMagnitude>=9&&(Mathf.Abs(angle%360)>.0001f||Mathf.Abs(x-1)>.0001f||Mathf.Abs(y-1)>.0001f);
            CancelTransformGesture();
            if(changed)Edit(m=>{var next=MapTransforms.Transform(m,selected,angle,x,y,false,id=>!thingCatalog.Find(id).fixedRotation,geometryMergeMode,resources);Selection.Clear();Selection.UnionWith(next);},"Преобразование применено. Один Undo отменяет жест.");
        }
        void CancelTransformGesture()
        {
            if(!transformDragging)return;transformDragging=false;transformAngle=0;transformScaleX=transformScaleY=1;
            RefreshTransformFrame();if(View!=null)View.SetVerticesDirty();
        }
        MapPoint PreviewTransformPoint(MapPoint p)
        {
            // Match the committed transform's snapped binary pivot and float matrix exactly.
            double radians=(transformAngle%360)*Math.PI/180;
            float cos=(float)Math.Cos(radians),sin=(float)Math.Sin(radians);
            float a=cos*transformScaleX,b=-sin*transformScaleY,c=sin*transformScaleX,d=cos*transformScaleY;
            var offset=new MapPoint(transformCenter.x-a*transformCenter.x-b*transformCenter.y,transformCenter.y-c*transformCenter.x-d*transformCenter.y);
            var result=new MapPoint(a*p.x+b*p.y+offset.x,c*p.x+d*p.y+offset.y);
            return MapFormats.IsUniversal(Map.format)?result:MapDocument.Snap(result,1);
        }
        internal MapPoint TransformVertexPosition(int index)
        {var point=Map.vertices[index].Position;return transformDragging&&transformVertices.Contains(index)?PreviewTransformPoint(point):point;}
        internal MapPoint TransformThingPosition(int index)
        {var point=Map.things[index].Position;return transformDragging&&transformThings.Contains(index)?PreviewTransformPoint(point):point;}
        internal float TransformThingAngle(int index)
        {
            float angle=Map.things[index].angle;if(!transformDragging||!transformThings.Contains(index)||thingCatalog.Find(Map.things[index].type).fixedRotation)return angle;
            angle=Mathf.Repeat(angle+transformAngle,360);return MapFormats.IsUniversal(Map.format)?angle:MapDocument.Snap(new MapPoint(angle,0),1).x%360;
        }
    }
}
