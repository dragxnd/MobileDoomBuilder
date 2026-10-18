using System;
using System.Collections.Generic;
using System.Globalization;
using MobileUDB.Core;
using UnityEngine;
using UnityEngine.UI;
namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        bool massSelection;
        MapDocument vertexInsertionBefore,vertexInsertionPreview;
        List<ElementRef> vertexInsertionSelection;
        ElementKind vertexInsertionKind;
        int vertexInsertionLine;
        MapPoint vertexInsertionPoint;
        OpenContext vertexPointerContext;
        Button massSelectionButton,flipLinesButton;
        MapPoint? drawingCursor;
        readonly HashSet<ElementRef> massStroke=new HashSet<ElementRef>();
        readonly List<int> vertexMeasuredLines=new List<int>();
        readonly List<Text> vertexLineLabels=new List<Text>();
        RectTransform vertexLabelRoot;
        void BeginVertexTool()
        {SetTool(MobileTool.EditVertices);Notify("Касание стены — добавить вершину; двигайте палец без отпускания, чтобы сразу переместить её. Два пальца — обзор.");}
        bool BeginVertexPointer(Vector2 screen)
        {
            if(Tool!=MobileTool.EditVertices)return false;
            if(!View.ScreenRect.Contains(screen))return true;
            vertexPointerContext=CaptureOpenContext();var hit=Map.Hit(View.World(screen),ElementKind.Vertex,18/View.zoom);
            if(!hit.HasValue)
            {
                var point=View.World(screen);var wall=Map.Hit(point,ElementKind.Line,26/View.zoom);if(!wall.HasValue)return true;
                try
                {
                    // Prepare on a copy. Insertion and drag remain one uncommitted
                    // gesture, so cancel/two fingers restore the original topology.
                    var preview=Map.Clone();int vertex=preview.InsertVertexOnLine(wall.Value.index,point,resources);preview.Validate();
                    var selected=new HashSet<ElementRef>{new ElementRef(ElementKind.Vertex,vertex)};
                    var origin=preview.Clone();var snap=new GeometryDragSnap(origin,selected,dragStart,gridFrame);
                    vertexInsertionBefore=Map;vertexInsertionSelection=new List<ElementRef>(Selection);vertexInsertionKind=Kind;
                    vertexInsertionLine=wall.Value.index;vertexInsertionPoint=point;vertexInsertionPreview=preview;
                    Map=preview;Selection.Clear();Selection.UnionWith(selected);Kind=ElementKind.Vertex;
                    dragOrigin=origin;dragSelection=selected;dragSnap=snap;MeasureSelectedVertices(selected);Changed(false);
                    Notify("Вершина под пальцем. Перемещайте и отпустите для добавления. Два пальца отменяют добавление и включают обзор.");
                }
                catch(Exception error){Notify(error.Message);}
                return true;
            }
            Selection.Clear();Selection.Add(hit.Value);Kind=ElementKind.Vertex;
            dragOrigin=Map.Clone();dragSelection=new HashSet<ElementRef>(Selection);dragSnap=new GeometryDragSnap(dragOrigin,dragSelection,dragStart,gridFrame);
            MeasureSelectedVertices(dragSelection);Changed(false);return true;
        }
        void EndVertexInsertion(Vector2 screen)
        {
            if(vertexInsertionBefore==null){if(!moved&&View.ScreenRect.Contains(screen))Notify("Коснитесь стены, чтобы добавить вершину.");return;}
            var context=vertexPointerContext;int line=vertexInsertionLine;var point=vertexInsertionPoint;
            bool owned=ReferenceEquals(Map,vertexInsertionPreview),stitch=dragSnap!=null&&dragSnap.Merge;
            MapPoint delta=new MapPoint(0,0);
            try{if(moved||Vector2.Distance(pointerStart,screen)>8)delta=SnappedMoveDelta(View.World(screen)-dragStart);}
            catch(Exception error){CancelDrag();Notify(error.Message);return;}
            CancelDrag();
            if(!owned||!CurrentOpenContext(context)){Notify("Карта изменилась. Коснитесь стены снова.");return;}
            if(Edit(map=>
            {
                int vertex=map.InsertVertexOnLine(line,point,resources);Kind=ElementKind.Vertex;Selection.Clear();Selection.Add(new ElementRef(ElementKind.Vertex,vertex));
                if(delta.x!=0||delta.y!=0){var remapped=MapTransforms.MoveDragged(map,new HashSet<ElementRef>(Selection),delta,stitch,geometryMergeMode,resources);Selection.Clear();Selection.UnionWith(remapped);}
            },"Вершина добавлена"+(delta.x!=0||delta.y!=0?" и перемещена":"")+". Доступна отмена."))MeasureSelectedVertices(Selection);
        }
        void CancelVertexInsertion()
        {
            if(vertexInsertionBefore==null)return;bool owned=ReferenceEquals(Map,vertexInsertionPreview);
            if(owned){Map=vertexInsertionBefore;Kind=vertexInsertionKind;Selection.Clear();Selection.UnionWith(vertexInsertionSelection);}
            vertexInsertionBefore=null;vertexInsertionPreview=null;vertexInsertionSelection=null;
            dragOrigin=null;dragSelection=null;dragSnap=null;dragThingSnap=null;
            if(owned)Changed(false);
        }
        static bool MassSelectionKind(ElementKind kind)
        {return kind==ElementKind.Vertex||kind==ElementKind.Line||kind==ElementKind.Sector;}
        void ToggleMassSelection()
        {
            if(massSelection&&PaintingSelection){DisablePaintSelection();massSelection=false;RefreshMassSelectionButton();return;}
            if(!MassSelectionKind(Kind))return;
            StartPaintSelection(SelectionBrush.Toggle);massSelection=PaintingSelection;RefreshMassSelectionButton();
            if(massSelection)Notify("Массовый выбор: касайтесь вершин / стен / секторов или проводите по ним. Повторное касание снимает отметку. Кнопка или Back выключает режим.");
        }
        void RefreshMassSelectionButton()
        {
            if(flipLinesButton!=null){bool selected=false;foreach(var item in Selection)if(item.kind==ElementKind.Line){selected=true;break;}flipLinesButton.interactable=!VisualActive&&Draft.Count==0&&Tool!=MobileTool.Paste&&selected;}
            if(massSelection&&!PaintingSelection)massSelection=false;
            if(massSelectionButton==null)return;
            massSelectionButton.interactable=!VisualActive&&MassSelectionKind(Kind);
            massSelectionButton.GetComponent<Image>().color=massSelection&&PaintingSelection?Accent:Panel;
        }
        void MeasureSelectedVertices(IEnumerable<ElementRef> selection)
        {
            vertexMeasuredLines.Clear();var vertices=new HashSet<int>();
            foreach(var item in selection)
            {
                if(item.kind==ElementKind.Vertex&&item.index>=0&&item.index<Map.vertices.Count)vertices.Add(item.index);
                else if(item.kind==ElementKind.Line&&item.index>=0&&item.index<Map.lines.Count)
                {
                    // Moving a linedef moves both endpoints and stretches their attached neighbors.
                    var line=Map.lines[item.index];vertices.Add(line.start);vertices.Add(line.end);
                }
            }
            if(vertices.Count>0)for(int i=0;i<Map.lines.Count;i++)if(vertices.Contains(Map.lines[i].start)||vertices.Contains(Map.lines[i].end))vertexMeasuredLines.Add(i);
            RefreshVertexLineLabels();
        }
        void ClearVertexMeasures()
        {vertexMeasuredLines.Clear();foreach(var label in vertexLineLabels)label.transform.parent.gameObject.SetActive(false);}
        internal static string VertexLineMeasure(MapPoint a,MapPoint b)
        {
            double dx=b.x-(double)a.x,dy=b.y-(double)a.y,angle=Math.Atan2(dy,dx)*180/Math.PI+90;if(angle<0)angle+=360;
            return "L:"+Math.Sqrt(dx*dx+dy*dy).ToString("0",CultureInfo.InvariantCulture)+"; A:"+Math.Round(angle).ToString(CultureInfo.InvariantCulture);
        }
        void FlipSelectedLines()
        {
            if(VisualActive||Draft.Count>0||Tool==MobileTool.Paste)return;
            var lines=new List<ElementRef>();foreach(var item in Selection)if(item.kind==ElementKind.Line)lines.Add(item);
            var scope=new WallDirectionScope{context=CaptureOpenContext(),visual=false,selection=new List<ElementRef>(Selection)};
            if(WallDirection.Candidates(Map,lines,false).Count==0){Notify("Нет стен для разворота: обычные односторонние стены UDB пропускает.");return;}
            ApplyWallDirection(scope,false);
        }
        public void PreviewDrawingPointer(Vector2 screen)
        {
            if(VisualActive||Tool!=MobileTool.DrawRoom||DrawingShape||Draft.Count==0||!View.ScreenRect.Contains(screen)){ClearDrawingCursor();return;}
            var world=View.World(screen);
            if(drawingCursor.HasValue&&(world-drawingCursor.Value).LengthSquared<.0001f)return;
            drawingCursor=DrawingPoint(world);View.SetVerticesDirty();
        }
        void ClearDrawingCursor(){if(drawingCursor.HasValue){drawingCursor=null;if(View!=null)View.SetVerticesDirty();}}
        public MapPoint? DrawingCursor {get{return !VisualActive&&Tool==MobileTool.DrawRoom&&!DrawingShape&&Draft.Count>0?drawingCursor:null;}}
        void ShowLineMeasure(MapPoint a,MapPoint b,ref int visible)
        {
            if((a-b).LengthSquared<.0001f)return;
            // Scale the entire label, including its backing and wall gap. No
            // minimum size: distant geometry must not keep full-size UI labels.
            float scale=Mathf.Min(1.5f,View.zoom/.8f);
            var position=(View.Project(a)+View.Project(b))*.5f+Vector2.up*(13*scale);
            if(!View.rectTransform.rect.Contains(position)||visible>=128)return;
            if(vertexLabelRoot==null){vertexLabelRoot=Rect("Line measures",View.transform);Stretch(vertexLabelRoot);}
            if(visible==vertexLineLabels.Count)
            {
                var frame=Rect("Line measure",vertexLabelRoot);frame.anchorMin=frame.anchorMax=frame.pivot=new Vector2(.5f,.5f);frame.sizeDelta=new Vector2(124,22);
                var background=frame.gameObject.AddComponent<Image>();background.color=new Color32(18,28,41,225);background.raycastTarget=false;
                var label=Label(frame,"",14);Stretch(label.rectTransform);label.color=new Color32(255,218,127,255);vertexLineLabels.Add(label);
            }
            var current=vertexLineLabels[visible++];current.text=VertexLineMeasure(a,b);var rect=(RectTransform)current.transform.parent;rect.anchoredPosition=position;rect.localScale=new Vector3(scale,scale,1);rect.gameObject.SetActive(true);
        }
        void RefreshVertexLineLabels()
        {
            int visible=0;
            if(!VisualActive&&(Tool==MobileTool.EditVertices||Tool==MobileTool.Move&&dragOrigin!=null))foreach(int index in vertexMeasuredLines)
            {
                if(index<0||index>=Map.lines.Count)continue;var line=Map.lines[index];
                ShowLineMeasure(Map.vertices[line.start].Position,Map.vertices[line.end].Position,ref visible);
            }
            if(!VisualActive&&Tool==MobileTool.DrawRoom&&!DrawingShape)
            {
                for(int i=1;i<Draft.Count&&visible<128;i++)ShowLineMeasure(Draft[i-1],Draft[i],ref visible);
                if(DrawingCursor.HasValue)ShowLineMeasure(Draft[Draft.Count-1],DrawingCursor.Value,ref visible);
            }
            for(int i=visible;i<vertexLineLabels.Count;i++)vertexLineLabels[i].transform.parent.gameObject.SetActive(false);
            if(vertexLabelRoot!=null)vertexLabelRoot.SetAsLastSibling();
        }
    }
}
