using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Text;
using MobileUDB.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace MobileUDB
{
    public enum MobileTool { Select, Pan, Move, DrawRoom, AddThing, CutSector, Visual, Paste, Transform, PointThings, MakeSector, CurveLines, Bridge, PlaceCamera, EditVertices }
    [Serializable] public sealed class MobileProject
    {
        public string format = "MobileUDB";
        public MapDocument map;
        public string sourceWad;
        public string sourcePk3ResourceId;
        public string gameProfile;
        public string actionEngine;
        public string geometryMergeMode;
        public GridFrame grid;
        public List<MapGridFrame> grids=new List<MapGridFrame>();
        public List<MapViewFrame> views=new List<MapViewFrame>();
        public List<MapBackgroundFrame> backgrounds=new List<MapBackgroundFrame>();
        public string pasteTagMode;
        public DrawTextureOptions drawTextures;
        public List<MapDocument> editedMaps = new List<MapDocument>();
        public List<MobileResourceArchive> resources = new List<MobileResourceArchive>();
    }
    public sealed partial class MobileEditor : MonoBehaviour
    {
        public MapDocument Map { get; private set; }
        public readonly OrderedSelection Selection = new OrderedSelection();
        public readonly List<MapPoint> Draft = new List<MapPoint>();
        public ElementKind Kind { get; private set; } = ElementKind.Line;
        public MobileTool Tool { get; private set; } = MobileTool.Select;
        GridFrame gridFrame=new GridFrame().Validated();
        readonly Dictionary<string,GridFrame> mapGrids=new Dictionary<string,GridFrame>(StringComparer.OrdinalIgnoreCase);
        public int Grid { get { return (int)Math.Max(1,Math.Round(gridFrame.step)); } private set { gridFrame.step=value; } }
        public bool SnapEnabled {get{return gridFrame.snap;}set{gridFrame.snap=value;}}
        public GridFrame GridSettings {get{return gridFrame.Validated();}}
        public bool MultiSelect;
        public bool ModalOpen { get { return modal != null; } }
        public MapViewport View { get; private set; }
        readonly MapHistory history = new MapHistory();
        readonly Dictionary<ElementKind, Button> kindButtons = new Dictionary<ElementKind, Button>();
        readonly Dictionary<MobileTool, Button> toolButtons = new Dictionary<MobileTool, Button>();
        RectTransform safeRoot, modal;
        RectTransform footerRoot, viewportRoot;
        CanvasScaler uiScaler;
        enum ToolbarCategory { Mode, Tool, Action }
        readonly Dictionary<ToolbarCategory, RectTransform> categoryGroups = new Dictionary<ToolbarCategory, RectTransform>();
        Vector2 lastScreenSize;
        Text title, status;
        Button undoButton, redoButton, propertiesButton, deleteButton, finishButton, mapStatisticsButton;
        MapDocument dragOrigin;
        GeometryDragSnap dragSnap;
        ThingDragSnap dragThingSnap;
        HashSet<ElementRef> dragSelection;
        MapPoint dragStart;
        Vector2 pointerStart;
        bool moved;
        int cutSector = -1;
        float saveAt;
        string message = "Два пальца — масштаб и панорама. Alt — два пальца на ПК.";
        byte[] sourceWad;
        DoomWad archive;
        readonly Dictionary<string, MapDocument> editedMaps = new Dictionary<string, MapDocument>(StringComparer.OrdinalIgnoreCase);
        Rect lastSafe;
        Font font;
        static readonly Color Background = new Color32(22, 33, 48, 255), Panel = new Color32(29, 43, 61, 255), Accent = new Color32(39, 121, 145, 255);
        string SavePath { get { return Path.Combine(Application.persistentDataPath, "map.mobileudb"); } }
        string RecoveryPath { get { return Path.Combine(Application.persistentDataPath, "recovery.mobileudb"); } }

        void Awake()
        {
            UiLanguage.Initialize();LoadInterfaceScale();Map = MapDocument.Demo(); font = UiLanguage.InterfaceFont;
            Application.targetFrameRate = 60;
            LoadShapeSettings();
            BuildUI();
            var navigation = gameObject.AddComponent<TouchNavigation>(); navigation.editor = this; navigation.view = View;
            gameObject.AddComponent<MobileFileBridge>().editor = this;
            RebuildResources();
            Changed(false); QueueClassicViewRestore(.1f);
            // Recovery is offered, never substituted for the user's current map silently.
            if (File.Exists(RecoveryPath)) Notify("Найдена автокопия. Автокопия доступна в «Файл → Восстановить последнюю карту». ");
        }
        void Update()
        {
            if (lastScreenSize != new Vector2(Screen.width, Screen.height) || lastSafe != Screen.safeArea) ApplyToolbarLayout();
            LayoutInterfaceScaleMenu();ObserveClassicView();ObserveVisualCamera();UpdateRecovery();
            if(VisualActive)LayoutVisualTextureControls();
            RefreshVertexLineLabels();RefreshFloorTextureToggle();RefreshDisplayLightingToggles();UpdateToolbarOverflow();
        }
        public void Notify(string text) { message = text; RefreshLabels(); }
        void Changed(bool edited = true)
        {
            RefreshShapePreview();
            RefreshTransformFrame();
            if (edited) { recoveryMapActive=true; wadDirty=true; ForgetPaintSelectionIndex(); recoveryRevision.Changed(); saveAt = Time.unscaledTime + 2; if (visualView != null) visualView.RequestRebuild(); }
            View.SetVerticesDirty(); RefreshLabels();
        }
        void RefreshLabels()
        {
            if (title == null) return;
            if(BlockmapActive){RefreshBlockmapData();return;}
            if(SoundPropagationActive){RefreshSoundPropagationUI();return;}
            if(AutomapActive){RefreshAutomapUI();return;}
            title.text = Map.name + " · " + Map.format + (Screen.width < Screen.height ? "\n" : " · ") + Map.lines.Count + " стен";
            mapStatisticsButton.interactable=!VisualActive;
            string[] kinds = { "Вершины", "Стены", "Секторы", "Объекты" }, tools = { PaintingSelection?(massSelection?"Массовый выбор":PaintLabel):"Выбор", "Обзор", "Двигать", DrawingShape?ShapeName:drawOpenChain ? "Цепочка" : "Комната", "+ Объект", "Разрез", "3D", "Вставка", "Размер / поворот", "Направление", "Создать сектор", "Дуга из стен", "Мост", "Камера · укажите точку", "Вершина · добавить / двигать" };
            RefreshPaintSelectionUI();RefreshFloorTextureToggle();RefreshDisplayLightingToggles();
            RefreshThingAimUI();
            RefreshMakeSectorUI();
            RefreshLinedefCurveUI();
            RefreshBridgeUI();
            status.text = kinds[(int)Kind] + " · " + (DuplicatingThings?"Копия объектов":tools[(int)Tool]) + " · сетка " + GridStepText + (Tool==MobileTool.Move&&gridFrame.cardinalDrag?" · 90°":SnapEnabled ? " · привязка" : " · свободно") + " · " + Selection.Count + " выбрано\n" + message;
            foreach (var p in kindButtons) p.Value.GetComponent<Image>().color = p.Key == Kind ? Accent : Panel;
            if(headerGridSmaller!=null){headerGridSmaller.interactable=!VisualActive&&gridFrame.step>1;headerGridLarger.interactable=!VisualActive&&gridFrame.step<65536;}
            Button sectorCreationButton;if(toolButtons.TryGetValue(MobileTool.MakeSector,out sectorCreationButton))sectorCreationButton.interactable=SelectedSectorCreationReady;
            foreach (var p in toolButtons) p.Value.GetComponent<Image>().color = p.Key == Tool ? Accent : Panel;
            undoButton.interactable = history.CanUndo || Draft.Count > 0 || ThingAimActive || MakingSector || CurvingLines || BridgingLines; redoButton.interactable = history.CanRedo && Draft.Count == 0 && !ThingAimActive && !MakingSector && !CurvingLines && !BridgingLines;
            propertiesButton.interactable = !ThingAimActive && !MakingSector && !CurvingLines && !BridgingLines && (Selection.Count == 1 || MultipleSectorsSelected() || MultipleSelected(ElementKind.Thing) || MultipleSelected(ElementKind.Line) || MultipleSelected(ElementKind.Vertex)); deleteButton.interactable = Selection.Count > 0 && !ThingAimActive && !MakingSector && !CurvingLines && !BridgingLines;
            finishButton.gameObject.SetActive(!VisualActive && (Tool == MobileTool.DrawRoom || Tool == MobileTool.CutSector || Tool == MobileTool.Paste));
            finishButton.interactable = Tool == MobileTool.Paste ? (DuplicatingThings?ThingDuplicateReady:PastePreview != null) : Tool == MobileTool.CutSector ? Draft.Count == 2 : DrawingShape?(DrawingCurve?Draft.Count>=2:Draft.Count==2)&&shapeContour!=null&&shapeContour.points.Count>=2:Draft.Count >= (drawOpenChain ? 2 : 3);
            shapeOptionsButton.gameObject.SetActive(!VisualActive&&DrawingShape);
            shapeOptionsButton.GetComponentInChildren<Text>().text=DrawingCurve?"Кривая":"Фигура";
            closeChainButton.gameObject.SetActive(!VisualActive && Tool == MobileTool.DrawRoom && (drawOpenChain||DrawingCurve));
            var closingRect=closeChainButton.GetComponent<RectTransform>();closingRect.anchorMin=closingRect.anchorMax=DrawingCurve?new Vector2(1,1):new Vector2(.5f,1);closingRect.pivot=DrawingCurve?Vector2.one:new Vector2(.5f,1);closingRect.sizeDelta=new Vector2(DrawingCurve?136:110,56);closingRect.anchoredPosition=DrawingCurve?new Vector2(-12,-76):new Vector2(-8,-12);
            closeChainButton.interactable = Draft.Count >= 3;
            RefreshClipboardButtons();
            RefreshVisualLabels();
            RefreshSectorToolbarButtons();
            if(SyncToolbarOverflowGroups()&&!toolbarLayoutInProgress)ApplyToolbarLayoutCore(false);
        }
        void MapStatistics()
        {
            if(VisualActive||AutomapActive||SoundPropagationActive||BlockmapActive)return;
            ShowDialog("Статистика карты",body=>
            {
                MapStatisticsRow(body,"Вершины (vertices)",Map.vertices.Count);
                MapStatisticsRow(body,"Линии (linedefs)",Map.lines.Count);
                MapStatisticsRow(body,"Стороны (sidedefs)",Map.sides.Count);
                MapStatisticsRow(body,"Секторы (sectors)",Map.sectors.Count);
                MapStatisticsRow(body,"Объекты (things)",Map.things.Count);
            });
        }
        void MapStatisticsRow(RectTransform body,string name,int count)
        {
            var label=Label(body,name+": "+count,20);label.alignment=TextAnchor.MiddleLeft;
            var layout=label.gameObject.AddComponent<LayoutElement>();layout.minHeight=36;layout.preferredHeight=40;
        }
        public void Fit() { EnsureClassicView();if (VisualActive) visualView.ResetCamera(); else View.Fit(); }
        MapPoint Snapped(MapPoint p) { return SnapEnabled ? gridFrame.Snap(p) : MapFormats.IsUniversal(Map.format) ? p : MapDocument.Snap(p,1); }
        public void BeginPointer(Vector2 screen)
        {
            if(BlockmapActive){BeginBlockmapPointer(screen);return;}
            if(SoundPropagationActive){BeginSoundPointer(screen);return;}
            if(AutomapActive){BeginAutomapPointer(screen);return;}
            if (VisualActive) { CancelDrag(); if(PaintingVisualSelection)visualView.BeginPaintSelection(screen);else visualView.BeginPointer(screen); return; }
            CancelDrag();
            if(HiddenThingInteraction)return;
            pointerStart = screen; dragStart = View.World(screen); moved = false;
            PreviewDrawingPointer(screen);
            if(Tool==MobileTool.PlaceCamera){cameraPlacementTracking=true;cameraPlacementContext=CaptureOpenContext();return;}
            if(BeginVertexPointer(screen))return;
            if(BeginThingAimPointer())return;
            if(BeginMakeSectorPointer())return;
            if(BeginBridgePointer(screen))return;
            if(CurvingLines)return;
            if(BeginPaintSelection(screen))return;
            BeginAreaSelection(screen);
            if (Tool == MobileTool.Transform) { BeginTransformPointer(screen); return; }
            if (Tool == MobileTool.Paste) { PositionPaste(screen); return; }
            if (Tool == MobileTool.Move)
            {
                var hit = Map.Hit(dragStart, Kind, 26 / View.zoom);
                if (hit.HasValue && !Selection.Contains(hit.Value)) { Selection.Clear(); Selection.Add(hit.Value); }
                if (Selection.Count > 0) { dragOrigin = Map.Clone(); dragSelection = MapTransforms.WithSectorThings(dragOrigin,Selection,includeSectorThings); dragSnap=new GeometryDragSnap(dragOrigin,dragSelection,dragStart,gridFrame);if(!dragSnap.HasGeometry)foreach(var item in dragSelection)if(item.kind==ElementKind.Thing){dragThingSnap=new ThingDragSnap(dragOrigin,dragSelection,dragStart,gridFrame,thingCatalog);break;} }
                if((Kind==ElementKind.Vertex||Kind==ElementKind.Line)&&dragSelection!=null)MeasureSelectedVertices(dragSelection);
                Changed(false);
            }
        }
        public void MovePointer(Vector2 previous, Vector2 screen)
        {
            if(BlockmapActive){MoveBlockmapPointer(previous,screen);return;}
            if(SoundPropagationActive){MoveSoundPointer(previous,screen);return;}
            if(AutomapActive){MoveAutomapPointer(previous,screen);return;}
            if (VisualActive) { if(PaintingVisualSelection)visualView.MovePaintSelection(previous,screen);else visualView.Look(previous, screen); return; }
            PreviewDrawingPointer(screen);
            if(MovePaintSelection(previous,screen))return;
            if (Vector2.Distance(pointerStart, screen) > 8) moved = true;
            if(Tool==MobileTool.PlaceCamera){if(moved)View.Pan(previous,screen);return;}
            if(MoveThingAimPointer(previous,screen))return;
            if(MoveMakeSectorPointer(previous,screen))return;
            if(MoveBridgePointer(previous,screen))return;
            if(CurvingLines){if(moved)View.Pan(previous,screen);return;}
            if (Tool == MobileTool.Select) MoveAreaSelection(screen);
            if (Tool == MobileTool.Transform) { MoveTransformPointer(screen); return; }
            if (Tool == MobileTool.Paste) { PositionPaste(screen); return; }
            if (Tool == MobileTool.Pan) View.Pan(previous, screen);
            else if(DrawingShape)ShapePointerMove(screen);
            else if ((Tool == MobileTool.Move||Tool==MobileTool.EditVertices) && dragOrigin != null && moved)
            {
                MapPoint delta = View.World(screen) - dragStart;
                Map.MoveFrom(dragOrigin, dragSelection, SnappedMoveDelta(delta)); Changed(false);
            }
        }
        public void EndPointer(Vector2 screen)
        {
            if(BlockmapActive){EndBlockmapPointer(screen);return;}
            if(SoundPropagationActive){EndSoundPointer(screen);return;}
            if(AutomapActive){EndAutomapPointer(screen);return;}
            ClearDrawingCursor();
            if (VisualActive) { if(PaintingVisualSelection)visualView.EndPaintSelection(screen);else visualView.EndPointer(screen); return; }
            if(HiddenThingInteraction)return;
            if(Tool==MobileTool.PlaceCamera){EndCameraPlacement(screen);return;}
            if(EndThingAimPointer(screen))return;
            if(EndMakeSectorPointer(screen))return;
            if(EndBridgePointer(screen))return;
            if(CurvingLines)return;
            if(EndPaintSelection(screen))return;
            if (Tool == MobileTool.Transform) { EndTransformPointer(screen); return; }
            if (Tool == MobileTool.Paste) { PositionPaste(screen); return; }
            if(Tool==MobileTool.EditVertices&&(vertexInsertionBefore!=null||dragOrigin==null)){EndVertexInsertion(screen);return;}
            if (Tool == MobileTool.Move||Tool==MobileTool.EditVertices)
            {
                if (dragOrigin != null)
                {
                    var selected = dragSelection; var delta = SnappedMoveDelta(View.World(screen) - dragStart); bool commit = (moved || Vector2.Distance(pointerStart,screen) > 8) && (delta.x != 0 || delta.y != 0);bool stitch=dragSnap!=null&&dragSnap.Merge;
                    Map = dragOrigin; dragOrigin = null; dragSelection = null;dragSnap=null;dragThingSnap=null;
                    if (commit) Edit(m => { var remapped = MapTransforms.MoveDragged(m,selected,delta,stitch,geometryMergeMode,resources); Selection.Clear(); Selection.UnionWith(remapped); },stitch?"Перемещено, стены сшиты. Доступна отмена.":"Перемещено без сшивки. Доступна отмена.");
                    else Changed(false);
                }
                if(Tool==MobileTool.EditVertices)MeasureSelectedVertices(Selection);else ClearVertexMeasures();
                moved = false; RefreshLabels(); return;
            }
            if (Tool == MobileTool.Select && EndAreaSelection(screen)) return;
            if(DrawingShape){ShapePointerUp(screen);return;}
            if (moved || !View.ScreenRect.Contains(screen)) return;
            MapPoint p = View.World(screen);
            if (Tool == MobileTool.Select)
            {
                var hit = Map.Hit(p, Kind, 26 / View.zoom);
                if (!MultiSelect) Selection.Clear();
                if (hit.HasValue) { if (!Selection.Add(hit.Value)) Selection.Remove(hit.Value); }
                Notify(hit.HasValue ? "Выбрано. «Свойства» или инструмент «Двигать»." : "Нет элемента рядом."); Changed(false);
            }
            if (Tool == MobileTool.DrawRoom)
            {
                p = DrawingPoint(p);
                if (Draft.Count > 2 && (p - Draft[0]).LengthSquared < Math.Pow(20 / View.zoom, 2)) { if (drawOpenChain) FinishLines(true); else FinishRoom(); }
                else { if (Draft.Count == 0 || (p - Draft[Draft.Count - 1]).LengthSquared > 0.01f) Draft.Add(p); Notify("Точек: " + Draft.Count + (drawOpenChain ? ". «Готово» — стены, «Замкнуть» — контур." : ". Замкните контур кнопкой «Готово».")); Changed(false); }
            }
            if (Tool == MobileTool.CutSector)
            {
                MapPoint boundary;
                if (!Map.TrySectorBoundaryPoint(cutSector, Snapped(p), 26 / View.zoom, out boundary) &&
                    !Map.TrySectorBoundaryPoint(cutSector, p, 26 / View.zoom, out boundary))
                { Notify("Коснитесь границы выбранного сектора."); return; }
                if (Draft.Count < 2) Draft.Add(boundary); else Draft[1] = boundary;
                Notify(Draft.Count == 1 ? "Укажите вторую точку на границе сектора." : "Стена готова. Нажмите «Готово» или уточните вторую точку."); Changed(false);
            }
            if (Tool == MobileTool.AddThing)
                Edit(m => { p = Snapped(p); m.things.Add(new MapThing { x = p.x, y = p.y, type = placementType, angle = placementAngle, flags = m.format == MapFormat.Hexen ? 2023 : 7 }); Selection.Clear(); Selection.Add(new ElementRef(ElementKind.Thing, m.things.Count - 1)); Kind = ElementKind.Thing; }, "Добавлен: " + thingCatalog.Find(placementType).title);
        }
        public void CancelDrag()
        {
            CancelAutomapPointer();CancelSoundPointer();CancelBlockmapPointer();
            ClearVertexMeasures();ClearDrawingCursor();CancelVertexInsertion();
            cameraPlacementTracking=false;
            CancelAreaSelection();
            CancelPaintSelectionGesture();
            thingAimTracking=false;
            makeSectorTracking=false;
            CancelBridgeGesture();
            CancelTransformGesture();
            if (visualView != null) visualView.CancelLook();
            ClearShapePointer();
            if (dragOrigin != null) { Map = dragOrigin; dragOrigin = null; Changed(false); }
            dragSnap=null;
            dragThingSnap=null;
            dragSelection = null;
            moved = false;
        }
        bool Edit(Action<MapDocument> action, string text)
        {
            CancelDrag(); var before = Map.Clone(); var selectedBefore = new List<ElementRef>(Selection); var oldKind = Kind;
            try { action(Map); Map.Validate(); history.Record(before); Notify(text); Changed(); return true; }
            catch (Exception e) { Map = before; Selection.Clear(); Selection.UnionWith(selectedBefore); Kind = oldKind; Notify(e.Message); Changed(false); return false; }
        }
        bool cameraPlacementTracking;
        OpenContext cameraPlacementContext;
        void BeginCameraPlacement()
        {
            if(VisualActive||Draft.Count>0||Tool==MobileTool.Paste){Notify("Завершите рисование или вставку и откройте 2D-карту.");return;}
            CloseDialog();EnsureClassicView();SetTool(MobileTool.PlaceCamera);
            Notify("Коснитесь точки для камеры UDB, без привязки к сетке. Перетаскивание — обзор; «Отмена» — выход.");
        }
        void EndCameraPlacement(Vector2 screen)
        {
            bool accepted=cameraPlacementTracking&&!moved&&Vector2.Distance(pointerStart,screen)<=8&&View.ScreenRect.Contains(screen);
            cameraPlacementTracking=false;if(!accepted)return;
            if(!CurrentOpenContext(cameraPlacementContext)){Notify("Карта изменилась. Укажите точку снова.");return;}
            var point=View.World(screen);
            try
            {
                bool changed=MapCameraMarker.NeedsPlacement(Map,point);int marker=MapCameraMarker.Find(Map);
                if(changed&&!Edit(m=>marker=MapCameraMarker.Place(m,point),"Камера UDB размещена. Undo отменяет перенос и удаление дубликатов."))return;
                Selection.Clear();Selection.Add(new ElementRef(ElementKind.Thing,marker));Kind=ElementKind.Thing;
                MapCamera camera;MapCameraMarker.TryRead(Map,out camera);var view=CurrentClassicView();view.camera=camera;mapViews[MapNaming.Key(Map)]=view;
                viewRecoveryRevision.Changed();saveAt=Time.unscaledTime+2;SetTool(MobileTool.Select);
                if(!changed)Notify("Камера уже находится в этой точке.");
            }
            catch(Exception e){Notify(e.Message);}
        }
        public void SetTool(MobileTool tool)
        {
            if(BlockmapActive)ExitBlockmap();
            if(SoundPropagationActive)ExitSoundPropagation();
            if(AutomapActive)ExitAutomap();
            CancelDrag();
            if (Draft.Count > 0 && tool != Tool) { Notify("Завершите или отмените текущее рисование."); return; }
            if(tool!=MobileTool.PointThings&&thingAim!=null)ClearThingAimState();
            if(tool!=MobileTool.MakeSector)ClearMakeSectorPreview();
            if(tool!=MobileTool.CurveLines)ClearLinedefCurve();
            if(tool!=MobileTool.Bridge)ClearBridge();
            if(tool!=MobileTool.Select)selectionBrush=SelectionBrush.Off;
            if (tool != MobileTool.Paste) CancelPaste();
            if (tool == MobileTool.Visual) { if (VisualActive) ExitVisual(); else EnterVisual(); return; }
            if (VisualActive) ExitVisual();
            if (tool == MobileTool.CutSector)
            {
                if (Selection.Count != 1) { Notify("Для разделения выберите один сектор."); return; }
                foreach (var e in Selection)
                { if (e.kind != ElementKind.Sector) { Notify("Переключитесь на секторы и выберите один сектор."); return; } cutSector = e.index; }
                Notify("Укажите две точки на границе выбранного сектора.");
            }
            if(tool==MobileTool.EditVertices){Kind=ElementKind.Vertex;Selection.RemoveWhere(item=>item.kind!=ElementKind.Vertex);}
            Tool = tool; Changed(false); if (tool == MobileTool.AddThing) PlacementMenu();
            if (tool == MobileTool.DrawRoom) DrawingMenu();
        }
        void SetKind(ElementKind kind)
        {
            if(BlockmapActive)ExitBlockmap();
            if(SoundPropagationActive)ExitSoundPropagation();
            if(AutomapActive)ExitAutomap();
            CancelDrag(); if (Draft.Count > 0) { Notify("Завершите или отмените текущий контур."); return; }
            CancelPaste();
            if (VisualActive) ExitVisual();
            if(massSelection&&!MassSelectionKind(kind)){massSelection=false;selectionBrush=SelectionBrush.Off;}
            Kind = kind; Selection.Clear(); Tool = MobileTool.Select; Changed(false);
        }
        void FinishRoom()
        {
            Edit(m => { int sector = m.AddRoom(Draft,geometryMergeMode,resources,drawTextures); Draft.Clear(); Kind = ElementKind.Sector; Selection.Clear(); Selection.Add(new ElementRef(ElementKind.Sector,sector)); Tool = MobileTool.Select; }, "Контур создан, стены сшиты.");
        }
        void FinishDraft()
        {
            if(DrawingShape){FinishShapeDrawing();return;}
            if (Tool == MobileTool.Paste) { CommitPaste(); return; }
            if (Tool == MobileTool.DrawRoom && drawOpenChain) { FinishLines(false); return; }
            if (Tool != MobileTool.CutSector) { FinishRoom(); return; }
            if (Draft.Count != 2) { Notify("Укажите две точки на границе сектора."); return; }
            Edit(m => { int added = m.SplitSector(cutSector, Draft[0], Draft[1],resources); Draft.Clear(); Selection.Clear();
                Selection.Add(new ElementRef(ElementKind.Sector, added)); Kind = ElementKind.Sector; Tool = MobileTool.Select; cutSector = -1; }, "Сектор разделён. Высоты и текстуры можно изменить в свойствах.");
        }
        void Undo()
        {
            if(BridgingLines){CancelBridge();return;}
            if(CurvingLines){CancelLinedefCurve();return;}
            if(ThingAimActive){CancelThingAim();return;}
            if(MakingSector&&makeSectorPreview!=null){ClearMakeSectorPreview();Changed(false);Notify("Предпросмотр сектора отменён.");return;}
            CancelDrag();
            if (Tool == MobileTool.Paste) { CancelPaste(); Changed(false); Notify("Вставка отменена."); return; }
            if (Draft.Count > 0) { Draft.RemoveAt(Draft.Count - 1); Changed(false); return; }
            if (history.CanUndo) { Map = history.Undo(Map); Selection.Clear(); if(VisualActive)visualView.ForgetSelection(); Tool = VisualActive ? MobileTool.Visual : MobileTool.Select; cutSector = -1; Changed(); }
        }
        void Redo() { if(BridgingLines){CancelBridge();return;}if(CurvingLines){CancelLinedefCurve();return;}CancelDrag(); if (Draft.Count == 0 && history.CanRedo) { Map = history.Redo(Map); Selection.Clear(); if(VisualActive)visualView.ForgetSelection(); Tool = VisualActive ? MobileTool.Visual : MobileTool.Select; cutSector = -1; Changed(); } }
        void DeleteSelection()
        {
            if(Selection.Count==0){Notify("Сначала выделите элементы для удаления.");return;}
            CancelDrag();var context=CaptureOpenContext();var seeds=new HashSet<ElementRef>(Selection);bool together=includeSectorThings;
            var targets=MapTransforms.WithSectorThings(Map,seeds,together);int contained=targets.Count-seeds.Count;
            bool verticesOnly=targets.Count>0;foreach(var target in targets)if(target.kind!=ElementKind.Vertex){verticesOnly=false;break;}
            ShowDialog("Удалить " + targets.Count + " элементов?", body =>
            {
                AddLabel(body,verticesOnly?"У вершины с двумя стенами они соединятся, сохранив свойства первой стены. В остальных случаях связанные стены удаляются. Действие можно отменить.":"Выбранные элементы будут удалены. Действие можно отменить.");
                if(contained>0)AddLabel(body,"Включены объекты внутри выбранных секторов: "+contained+". Настройка «Объекты вместе с сектором» включена.");
                DialogApply(body,()=>
                {
                    if(!CurrentOpenContext(context)||!Selection.SetEquals(seeds)||includeSectorThings!=together){Notify("Карта, выделение или настройка изменились. Откройте удаление снова.");return false;}
                    return Edit(m=>{m.Delete(targets);Selection.Clear();if(VisualActive)visualView.ForgetSelection();},"Удалено.");
                },"Удалить",true);
            });
        }
        void BuildUI()
        {
            var canvasObject = new GameObject("Mobile editor UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false); var canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            uiScaler = canvasObject.GetComponent<CanvasScaler>(); uiScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; uiScaler.referenceResolution = new Vector2(420, 720); uiScaler.matchWidthOrHeight = 0;
            safeRoot = Rect("Safe area", canvasObject.transform); Stretch(safeRoot);
            Image safeImage = safeRoot.gameObject.AddComponent<Image>(); safeImage.color = Background;
            var header = Rect("Header", safeRoot); header.anchorMin = new Vector2(0, 1); header.anchorMax = Vector2.one; header.pivot = new Vector2(0.5f, 1); header.sizeDelta = new Vector2(0, 56);
            var file = IconButton(header, "Файл: открыть, сохранить, экспортировать", EditorIcon.Symbol.File, FileMenu);
            var fileRect = file.GetComponent<RectTransform>(); fileRect.anchorMin = new Vector2(0, 0); fileRect.anchorMax = new Vector2(0, 1); fileRect.sizeDelta = new Vector2(48, -8); fileRect.anchoredPosition = new Vector2(28, 0);
            undoButton = IconButton(header, "Отменить последнее действие или точку", EditorIcon.Symbol.Undo, Undo);
            redoButton = IconButton(header, "Повторить отменённое действие", EditorIcon.Symbol.Redo, Redo);
            var ur = undoButton.GetComponent<RectTransform>(); ur.anchorMin = new Vector2(0, 0); ur.anchorMax = new Vector2(0, 1); ur.sizeDelta = new Vector2(44, -8); ur.anchoredPosition = new Vector2(78, 0);
            var rr = redoButton.GetComponent<RectTransform>(); rr.anchorMin = new Vector2(0, 0); rr.anchorMax = new Vector2(0, 1); rr.sizeDelta = new Vector2(44, -8); rr.anchoredPosition = new Vector2(126, 0);
            title = Label(header, "", 22); var tr = title.rectTransform; Stretch(tr); tr.offsetMin = new Vector2(158, 0); tr.offsetMax = new Vector2(-194, 0); title.alignment = TextAnchor.MiddleLeft; title.resizeTextForBestFit = true; title.resizeTextMinSize = 12; title.resizeTextMaxSize = 22;
            var fit = IconButton(header, "Показать всю карту", EditorIcon.Symbol.Fit, Fit); var fr = fit.GetComponent<RectTransform>(); fr.anchorMin = new Vector2(1, 0); fr.anchorMax = Vector2.one; fr.sizeDelta = new Vector2(48, -8); fr.anchoredPosition = new Vector2(-72, 0);
            BuildHeaderGridButtons(header);
            mapStatisticsButton=IconButton(header,"Статистика карты",EditorIcon.Symbol.Statistics,MapStatistics);
            var stats=mapStatisticsButton.GetComponent<RectTransform>();stats.anchorMin=new Vector2(1,0);stats.anchorMax=Vector2.one;stats.sizeDelta=new Vector2(44,-8);stats.anchoredPosition=new Vector2(-24,0);
            var viewportRect = Rect("Map viewport", safeRoot); Stretch(viewportRect); viewportRect.offsetMin = new Vector2(0, 148); viewportRect.offsetMax = new Vector2(0, -56);
            viewportRoot = viewportRect;
            viewportRect.gameObject.AddComponent<RectMask2D>();
            var mapGraphic = Rect("Map drawing",viewportRect); Stretch(mapGraphic);
            View = mapGraphic.gameObject.AddComponent<MapViewport>(); View.editor = this; View.raycastTarget = true;
            BuildPaintSelectionUI(viewportRect);
            BuildThingAimUI(viewportRect);
            BuildMakeSectorUI(viewportRect);
            BuildLinedefCurveUI(viewportRect);
            BuildBridgeUI(viewportRect);
            var footer = Rect("Commands", safeRoot); footer.anchorMin = Vector2.zero; footer.anchorMax = new Vector2(1, 0); footer.pivot = new Vector2(0.5f, 0); footer.sizeDelta = new Vector2(0, 148);
            footerRoot = footer;
            var modes = ToolbarGroup(footer, ToolbarCategory.Mode, "РЕЖИМ ВЫБОРА");
            kindButtons[ElementKind.Vertex] = IconButton(modes, "Выбирать вершины", EditorIcon.Symbol.Vertex, () => SetKind(ElementKind.Vertex));
            kindButtons[ElementKind.Line] = IconButton(modes, "Выбирать стены", EditorIcon.Symbol.Wall, () => SetKind(ElementKind.Line));
            kindButtons[ElementKind.Sector] = IconButton(modes, "Выбирать секторы", EditorIcon.Symbol.Sector, () => SetKind(ElementKind.Sector));
            kindButtons[ElementKind.Thing] = IconButton(modes, "Выбирать объекты", EditorIcon.Symbol.Thing, () => SetKind(ElementKind.Thing));
            var tools = ToolbarGroup(footer, ToolbarCategory.Tool, "ИНСТРУМЕНТ");
            toolButtons[MobileTool.Select] = IconButton(tools, "Выбрать касанием или рамкой: проведите пальцем", EditorIcon.Symbol.Select, () => {selectionBrush=SelectionBrush.Off;SetTool(MobileTool.Select);});
            toolButtons[MobileTool.Pan] = IconButton(tools, "Перемещать вид одним пальцем", EditorIcon.Symbol.Pan, () => SetTool(MobileTool.Pan));
            toolButtons[MobileTool.Move] = IconButton(tools, "Двигать выделенные элементы", EditorIcon.Symbol.Move, () => SetTool(MobileTool.Move));
            toolButtons[MobileTool.EditVertices]=IconButton(tools,"Добавлять и двигать вершины",EditorIcon.Symbol.Vertex,BeginVertexTool);Badge(toolButtons[MobileTool.EditVertices].transform,"+",false);
            toolButtons[MobileTool.DrawRoom] = IconButton(tools, "Цепочка стен · удерживайте больше секунды для выбора рисования", EditorIcon.Symbol.Room, BeginWallChain);
            var chainButton = (MobileIconButton)toolButtons[MobileTool.DrawRoom]; chainButton.holdSeconds = 1; chainButton.holdAction = DrawingMenu;
            toolButtons[MobileTool.AddThing] = IconButton(tools, "Добавлять объекты", EditorIcon.Symbol.AddThing, () => SetTool(MobileTool.AddThing));
            toolButtons[MobileTool.MakeSector] = IconButton(tools, "Из контура · создать по выделенным секторам и сохранить текстуры", EditorIcon.Symbol.MakeSector, BeginMakeSectorKeepTextures);
            toolButtons[MobileTool.Visual] = IconButton(tools, "3D-режим: поверхности, высоты и текстуры", EditorIcon.Symbol.Visual, () => SetTool(MobileTool.Visual));
            CubeModeLabel(toolButtons[MobileTool.Visual], "3D");
            flipLinesButton=IconButton(tools,"Развернуть выбранные лайндефы",EditorIcon.Symbol.WallFlip,FlipSelectedLines);
            var actions = ToolbarGroup(footer, ToolbarCategory.Action, "ДЕЙСТВИЯ");
            massSelectionButton=IconButton(actions,"Массовый выбор вершин / стен / секторов · включить или выключить",EditorIcon.Symbol.Select,ToggleMassSelection);Badge(massSelectionButton.transform,"±",false);
            BuildFloorTextureToggle(actions);BuildDisplayLightingToggle(actions);BuildDisplayThingsToggle(actions);
            propertiesButton = IconButton(actions, "Свойства выделения", EditorIcon.Symbol.Properties, Properties);
            BuildSectorToolbarButtons(actions,false);
            deleteButton = IconButton(actions, "Удалить выбранные элементы", EditorIcon.Symbol.Delete, DeleteSelection);
            copyButton = IconButton(actions, "Копировать выделение", EditorIcon.Symbol.Copy, CopySelection);
            pasteButton = IconButton(actions, "Вставить копию: разместите контур и нажмите «Готово»", EditorIcon.Symbol.Paste, StartPaste);
            IconButton(actions, "Настройки: сетка, выбор, объединение, справка", EditorIcon.Symbol.Configuration, MoreMenu);
            status = Label(footer, "", 16); var sr = status.rectTransform; sr.anchorMin = Vector2.zero; sr.anchorMax = new Vector2(1, 0); sr.pivot = new Vector2(0.5f, 0); sr.sizeDelta = new Vector2(-16, 38); sr.anchoredPosition = new Vector2(0, 1); status.alignment = TextAnchor.MiddleLeft; status.resizeTextForBestFit = true; status.resizeTextMinSize = 12; status.resizeTextMaxSize = 16;
            finishButton = Button(viewportRect, "Готово", FinishDraft); var finishRect = finishButton.GetComponent<RectTransform>(); finishRect.anchorMin = finishRect.anchorMax = new Vector2(1, 1); finishRect.pivot = new Vector2(1, 1); finishRect.sizeDelta = new Vector2(136, 56); finishRect.anchoredPosition = new Vector2(-12, -12);
            closeChainButton = Button(viewportRect,"Замкнуть",() => {if(DrawingCurve)FinishCurveDrawing(true);else FinishLines(true);});
            var closeChainRect = closeChainButton.GetComponent<RectTransform>(); closeChainRect.anchorMin = closeChainRect.anchorMax = new Vector2(.5f,1); closeChainRect.pivot = new Vector2(.5f,1); closeChainRect.sizeDelta = new Vector2(110,56); closeChainRect.anchoredPosition = new Vector2(-8,-12);
            shapeOptionsButton=Button(viewportRect,"Фигура",()=>ShapeSettingsMenu(drawingShape,true));
            var shapeRect=shapeOptionsButton.GetComponent<RectTransform>();shapeRect.anchorMin=shapeRect.anchorMax=new Vector2(.5f,1);shapeRect.pivot=new Vector2(.5f,1);shapeRect.sizeDelta=new Vector2(110,56);shapeRect.anchoredPosition=new Vector2(-8,-12);
            pasteCancelButton = Button(viewportRect,"Отмена",() => { if(Tool==MobileTool.PlaceCamera){SetTool(MobileTool.Select);Notify("Размещение камеры отменено.");} else if (Tool == MobileTool.Transform) { SetTool(MobileTool.Select); Notify("Преобразование закрыто. Применённые изменения можно отменить кнопкой Undo."); } else if (Tool == MobileTool.Paste) { CancelPaste(); Changed(false); Notify("Вставка отменена."); } else CancelDraft(); });
            var cancelRect = pasteCancelButton.GetComponent<RectTransform>(); cancelRect.anchorMin = cancelRect.anchorMax = new Vector2(0,1); cancelRect.pivot = new Vector2(0,1); cancelRect.sizeDelta = new Vector2(116,56); cancelRect.anchoredPosition = new Vector2(12,-12);
            if (EventSystem.current == null) new GameObject("Mobile event system", typeof(EventSystem), typeof(InputSystemUIInputModule));
            BuildAutomapUI();BuildSoundPropagationUI();BuildBlockmapUI();ApplyToolbarLayout();
        }
        void ApplyToolbarLayout(){ApplyToolbarLayoutCore(true);}
        void ApplyToolbarLayoutCore(bool cancelNavigation)
        {
            if (uiScaler == null || Screen.width == 0 || Screen.height == 0) return;
            if(toolbarLayoutInProgress)return;toolbarLayoutInProgress=true;
            try {
            CloseToolbarOverflow();RefreshSectorToolbarButtons();SyncToolbarOverflowGroups();
            var navigation = GetComponent<TouchNavigation>(); if (cancelNavigation&&navigation != null) navigation.CancelForLayout();
            lastScreenSize = new Vector2(Screen.width, Screen.height); bool landscape = Screen.width > Screen.height;
            uiScaler.referenceResolution = new Vector2(landscape ? 420f * Screen.width / Screen.height : 420, 720)/InterfaceScaleFactor;
            lastSafe = Screen.safeArea; Rect area = lastSafe;
            if (area.width <= 0 || area.height <= 0) area = new Rect(0,0,Screen.width,Screen.height);
            safeRoot.anchorMin = new Vector2(area.xMin / Screen.width,area.yMin / Screen.height);
            safeRoot.anchorMax = new Vector2(area.xMax / Screen.width,area.yMax / Screen.height);
            float width = area.width * uiScaler.referenceResolution.x / Screen.width;
            if(BlockmapActive){LayoutBlockmap();return;}
            if(SoundPropagationActive){LayoutSoundPropagation();return;}
            if(AutomapActive){LayoutAutomap();return;}
            viewportRoot.offsetMax=new Vector2(0,-56);
            int[] counts = new int[3];
            for (int i = 0; i < counts.Length; i++)
                counts[i] = ToolbarButtonCount(VisualActive ? visualGroups[i] : categoryGroups[(ToolbarCategory)i]);
            var plan = MobileToolbarLayout.Create(width,landscape,counts,VisualActive ? VisualToolbarSpacing : MobileToolbarLayout.Spacing);
            footerRoot.sizeDelta = new Vector2(0,plan.footerHeight); viewportRoot.offsetMin = new Vector2(0,plan.footerHeight);
            if (VisualActive) ApplyVisualLayout(plan,width);
            else foreach (var pair in categoryGroups)
            {
                ApplyToolbarGroup(pair.Value,plan.groups[(int)pair.Key],width);
            }
            if (View != null) View.SetVerticesDirty();
            RefreshLabels();
            }finally{toolbarLayoutInProgress=false;}
        }
        void ApplyToolbarGroup(RectTransform group,ToolbarGroupLayout layout,float width)
        {
            group.anchorMin = new Vector2(layout.x / width,0); group.anchorMax = new Vector2((layout.x + layout.width) / width,0);
            group.pivot = new Vector2(.5f,0); group.sizeDelta = new Vector2(-12,layout.height); group.anchoredPosition = new Vector2(0,layout.bottom);
            var grid = group.GetComponentInChildren<GridLayoutGroup>(true);
            var row = (RectTransform)grid.transform;
            row.sizeDelta = new Vector2(-12,layout.height - MobileToolbarLayout.CaptionHeight);
            grid.constraintCount = layout.columns; grid.cellSize = new Vector2(layout.cellWidth,MobileToolbarLayout.ButtonHeight);
        }
        RectTransform ToolbarGroup(Transform parent, ToolbarCategory category, string caption)
        {
            RectTransform group; var buttons = MakeToolbarGroup(parent, caption, out group); categoryGroups[category] = group; return buttons;
        }
        RectTransform MakeToolbarGroup(Transform parent, string caption, out RectTransform group)
        {
            group = Rect(caption, parent);
            group.gameObject.AddComponent<Image>().color = new Color32(18, 28, 41, 255);
            var text = Label(group, caption, 12); text.verticalOverflow=VerticalWrapMode.Overflow; text.color = new Color32(147, 168, 188, 255);
            text.alignment = TextAnchor.MiddleLeft; text.rectTransform.anchorMin = new Vector2(0, 1); text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.pivot = new Vector2(0.5f, 1); text.rectTransform.sizeDelta = new Vector2(-8, 14);
            text.rectTransform.anchoredPosition = Vector2.zero;
            var buttons = Rect("Buttons",group); buttons.anchorMin = new Vector2(0,1); buttons.anchorMax = Vector2.one;
            buttons.pivot = new Vector2(.5f,1); buttons.sizeDelta = new Vector2(-12,50); buttons.anchoredPosition = new Vector2(0,-14);
            var grid = buttons.gameObject.AddComponent<GridLayoutGroup>(); grid.cellSize = new Vector2(48,50); grid.spacing = new Vector2(4,4);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount; grid.constraintCount = 1; grid.childAlignment = TextAnchor.UpperCenter;
            if(parent==footerRoot)RegisterToolbarOverflow(group,buttons);
            return buttons;
        }
        static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false); return go.GetComponent<RectTransform>();
        }
        static void Stretch(RectTransform r) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; }
        RectTransform Row(Transform parent, float y, float height)
        {
            var r = Rect("Row", parent); r.anchorMin = Vector2.zero; r.anchorMax = new Vector2(1, 0); r.pivot = new Vector2(0.5f, 0); r.sizeDelta = new Vector2(-12, height); r.anchoredPosition = new Vector2(0, y);
            var layout = r.gameObject.AddComponent<HorizontalLayoutGroup>(); layout.spacing = 4; layout.childControlWidth = layout.childControlHeight = true; layout.childForceExpandWidth = layout.childForceExpandHeight = true; return r;
        }
        Text Label(Transform parent, string text, int size = 20)
        {
            var r = Rect("Text", parent); var label = r.gameObject.AddComponent<MobileLocalizedText>(); label.font = font; label.SetUntranslated(parent.GetComponentInParent<InputField>()!=null);label.text = text; label.fontSize = size; label.color = new Color32(219, 231, 242, 255); label.alignment = TextAnchor.MiddleCenter; label.raycastTarget = false; return label;
        }
        Button Button(Transform parent, string text, Action action)
        {
            var r = Rect(text, parent); var image = r.gameObject.AddComponent<Image>(); image.color = Panel;
            var button = r.gameObject.AddComponent<Button>(); button.targetGraphic = image; button.onClick.AddListener(() => { try { action(); } catch (Exception e) { Notify(e.Message); Debug.LogException(e); } });
            var label = Label(r, text, 20); Stretch(label.rectTransform); label.resizeTextForBestFit = true; label.resizeTextMinSize = 14; label.resizeTextMaxSize = 20;
            var colors = button.colors; colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f); colors.pressedColor = new Color(0.7f, 0.85f, 0.95f); colors.disabledColor = new Color(0.55f, 0.55f, 0.55f); button.colors = colors;
            return button;
        }
        Button IconButton(Transform parent, string hint, EditorIcon.Symbol symbol, Action action)
        {
            var r = Rect(hint, parent); var image = r.gameObject.AddComponent<Image>(); image.color = Panel;
            var button = r.gameObject.AddComponent<MobileIconButton>(); button.targetGraphic = image; button.hint = hint; button.showHint = Notify;
            button.onClick.AddListener(() => { try { action(); } catch (Exception e) { Notify(e.Message); Debug.LogException(e); } });
            var iconRect = Rect("Icon", r); iconRect.anchorMin = iconRect.anchorMax = new Vector2(0.5f, 0.5f); iconRect.sizeDelta = new Vector2(32, 32);
            var icon = iconRect.gameObject.AddComponent<EditorIcon>(); icon.symbol = symbol; icon.color = Color.white; icon.raycastTarget = false;
            var colors = button.colors; colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f); colors.pressedColor = new Color(0.7f, 0.85f, 0.95f); colors.disabledColor = new Color(0.35f, 0.35f, 0.35f); button.colors = colors;
            return button;
        }
        void ToolbarHelp()
        {
            var buttons = GetComponentsInChildren<MobileIconButton>(true);
            ShowDialog("Значки инструментов", body =>
            {
                AddLabel(body, "Касание выполняет действие. Удержание значка показывает подсказку. У «Цепочки стен» удержание больше секунды открывает выбор рисования. Активный режим указан под панелью.");
                foreach (var button in buttons)
                {
                    var row = Rect(button.hint, body); row.gameObject.AddComponent<LayoutElement>().preferredHeight = 54;
                    var iconRect = Rect("Icon", row); iconRect.anchorMin = iconRect.anchorMax = new Vector2(0, 0.5f); iconRect.pivot = new Vector2(0, 0.5f); iconRect.sizeDelta = new Vector2(32, 32);
                    var icon = iconRect.gameObject.AddComponent<EditorIcon>(); icon.symbol = button.GetComponentInChildren<EditorIcon>().symbol; icon.color = Color.white; icon.raycastTarget = false;
                    var text = Label(row, button.hint, 20); Stretch(text.rectTransform); text.rectTransform.offsetMin = new Vector2(48, 0); text.alignment = TextAnchor.MiddleLeft;
                }
            });
        }
        void ShowDialog(string heading, Action<RectTransform> content)
        {
            CancelDrag(); CloseDialog();
            modal = Rect("Dialog overlay", safeRoot); RefreshVisualTexturePanelVisibility(); Stretch(modal); var shade = modal.gameObject.AddComponent<Image>(); shade.color = new Color(0, 0, 0, 0.8f);
            var card = Rect("Dialog", modal); ScaleDialogBounds(card,.9f); card.gameObject.AddComponent<Image>().color = Background;
            var headingText = Label(card, heading, 25); var h = headingText.rectTransform; h.anchorMin = new Vector2(0, 1); h.anchorMax = Vector2.one; h.pivot = new Vector2(0.5f, 1); h.offsetMin = new Vector2(12,-62); h.offsetMax = new Vector2(-120,0);
            headingText.alignment = TextAnchor.MiddleLeft; headingText.resizeTextForBestFit = true; headingText.resizeTextMinSize = 16; headingText.resizeTextMaxSize = 25;
            var close = Button(card, "Закрыть", CloseDialog); var c = close.GetComponent<RectTransform>(); c.anchorMin = c.anchorMax = Vector2.one; c.pivot = Vector2.one; c.sizeDelta = new Vector2(108, 56); c.anchoredPosition = new Vector2(-4, -4);
            var scroll = Rect("Scroll", card); Stretch(scroll); scroll.offsetMin = new Vector2(12, 12); scroll.offsetMax = new Vector2(-12, -68);
            scroll.gameObject.AddComponent<Image>().color = Background; scroll.gameObject.AddComponent<RectMask2D>(); var sv = scroll.gameObject.AddComponent<ScrollRect>(); sv.horizontal = false; sv.movementType = ScrollRect.MovementType.Clamped;
            var body = Rect("Content", scroll); body.anchorMin = new Vector2(0, 1); body.anchorMax = Vector2.one; body.pivot = new Vector2(0.5f, 1); body.sizeDelta = Vector2.zero;
            var layout = body.gameObject.AddComponent<VerticalLayoutGroup>(); layout.spacing = 8; layout.childControlHeight = layout.childControlWidth = true; layout.childForceExpandHeight = false; layout.childForceExpandWidth = true;
            var fitter = body.gameObject.AddComponent<ContentSizeFitter>(); fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            sv.viewport = scroll; sv.content = body; content(body);
            modal.gameObject.AddComponent<MobileKeyboardLayout>();
        }
        void CloseDialog()
        {
            CloseToolbarOverflow();ClearPropertyPreviews();CloseBackgroundPreview();CancelFileRead();CancelDocumentOpen();CancelWadExport();CancelProjectSave();CancelResourceImport();
            if(validationRoutine!=null){StopCoroutine(validationRoutine);validationRoutine=null;}
            CloseTexturePicker();var closing=modal;modal=null;
            if(closing!=null){ReleaseDialogInput(closing);closing.gameObject.SetActive(false);Destroy(closing.gameObject);}
            RefreshVisualTexturePanelVisibility();if(closing!=null)RefreshLabels();
        }
        Button AddButton(Transform body, string text, Action action)
        {
            var b = Button(body, text, action); b.gameObject.AddComponent<LayoutElement>().preferredHeight = 60; return b;
        }
        Button DialogApply(RectTransform body, Func<bool> apply, string title="Применить", bool closeOnSuccess=false)
        {
            // The body scrolls independently; the action stays inside the safe-area card.
            var scroll=(RectTransform)body.parent;var card=(RectTransform)scroll.parent;
            scroll.offsetMin=new Vector2(scroll.offsetMin.x,80);
            var feedback=Label(card,"",16);feedback.name="Apply feedback";
            var fr=feedback.rectTransform;fr.anchorMin=Vector2.zero;fr.anchorMax=new Vector2(1,0);fr.pivot=new Vector2(.5f,0);
            fr.sizeDelta=new Vector2(-24,40);fr.anchoredPosition=new Vector2(0,72);
            feedback.resizeTextForBestFit=true;feedback.resizeTextMinSize=12;feedback.resizeTextMaxSize=16;feedback.gameObject.SetActive(false);
            var button=Button(card,title,()=>
            {
                bool success=apply();if(success&&closeOnSuccess){CloseDialog();return;}
                feedback.text=message;feedback.color=success?Color.white:new Color(1,.65f,.55f);
                feedback.gameObject.SetActive(true);scroll.offsetMin=new Vector2(scroll.offsetMin.x,120);
            });
            button.name="Apply properties";
            var r=button.GetComponent<RectTransform>();r.anchorMin=Vector2.zero;r.anchorMax=new Vector2(1,0);r.pivot=new Vector2(.5f,0);
            r.sizeDelta=new Vector2(-24,60);r.anchoredPosition=new Vector2(0,8);
            return button;
        }
        void AddLabel(Transform body, string text)
        {
            var l = Label(body, text); l.alignment = TextAnchor.MiddleLeft; l.gameObject.AddComponent<LayoutElement>().minHeight = 80;
        }
        InputField Field(Transform body, string name, string value, bool number = true)
        {
            var row = Rect(name, body); row.gameObject.AddComponent<LayoutElement>().preferredHeight = 58;
            var label = Label(row, name); var lr = label.rectTransform; lr.anchorMin = Vector2.zero; lr.anchorMax = new Vector2(0.46f, 1); lr.offsetMin = lr.offsetMax = Vector2.zero; label.alignment = TextAnchor.MiddleLeft;
            var inputRect = Rect("Input", row); inputRect.anchorMin = new Vector2(0.47f, 0); inputRect.anchorMax = Vector2.one; inputRect.offsetMin = inputRect.offsetMax = Vector2.zero;
            inputRect.gameObject.AddComponent<Image>().color = Panel; var input = inputRect.gameObject.AddComponent<MobileInputField>();input.owner=this;
            var text = Label(inputRect, value, 22); Stretch(text.rectTransform); text.rectTransform.offsetMin = new Vector2(10, 0); text.rectTransform.offsetMax = new Vector2(-10, 0); text.alignment = TextAnchor.MiddleLeft;
            input.textComponent = text; input.text = value; input.contentType = number ? InputField.ContentType.IntegerNumber : InputField.ContentType.Standard; input.characterLimit = number ? 11 : 255;
            return input;
        }
        int Number(InputField f, int min, int max)
        {
            int n; if (!int.TryParse(f.text, out n) || n < min || n > max) throw new InvalidOperationException("Число должно быть от " + min + " до " + max + "."); return n;
        }
        string TextureName(InputField f)
        {
            string s = f.text.Trim(); int limit = MapFormats.IsUniversal(Map.format) ? 255 : 8;
            if (s.Length == 0 || s.Length > limit) throw new InvalidOperationException("Имя текстуры: 1–" + limit + " символов.");
            if (!MapFormats.IsUniversal(Map.format)) { foreach (char ch in s) if (ch < 33 || ch > 126) throw new InvalidOperationException("Имя текстуры должно быть ASCII."); s = s.ToUpperInvariant(); }
            return s;
        }
        float RealNumber(InputField field, float min, float max)
        {
            float number;
            if ((!float.TryParse(field.text, NumberStyles.Float, CultureInfo.InvariantCulture, out number) && !float.TryParse(field.text, NumberStyles.Float, CultureInfo.CurrentCulture, out number)) || float.IsNaN(number) || float.IsInfinity(number) || number < min || number > max)
                throw new InvalidOperationException("Введите число от " + min + " до " + max + ".");
            return number;
        }
        void BooleanControl(Transform body, string label, bool value, List<Action<MapDocument>> apply, Action<MapDocument, bool> setter)
        {
            bool enabled = value, touched = false; Button button = null;
            button = AddButton(body, label + ": " + (enabled ? "Да" : "Нет"), () => { enabled = !enabled; touched = true; button.GetComponentInChildren<Text>().text = label + ": " + (enabled ? "Да" : "Нет"); });
            // Omitted UDMF flags retain namespace-specific defaults until the user changes them.
            apply.Add(m => { if (touched) setter(m, enabled); });
        }
        InputField BinaryFlags(Transform body, int value, bool line, List<Action<MapDocument>> apply, Action<MapDocument, int> setter)
        {
            var field = Field(body, "Флаги (число)", value.ToString()); apply.Add(m => setter(m, Number(field, 0, 65535)));
            var names=BinaryFlagNames(line,true);var masks=new List<int>(names.Keys);masks.Sort();
            foreach (int valueMask in masks)
            {
                int mask = valueMask; string label = names[mask]; Button button = null;
                Action refresh = () => { int flags; int.TryParse(field.text, out flags); button.GetComponentInChildren<Text>().text = label + ": " + ((flags & mask) != 0 ? "Да" : "Нет"); };
                button = AddButton(body, label, () => { int flags = Number(field, 0, 65535); field.text = (flags ^ mask).ToString(); });
                field.onValueChanged.AddListener(_ => refresh()); refresh();
            }
            return field;
        }
        MapElementData Element(MapDocument map, ElementRef e)
        {
            if (e.kind == ElementKind.Vertex) return map.vertices[e.index];
            if (e.kind == ElementKind.Line) return map.lines[e.index];
            if (e.kind == ElementKind.Sector) return map.sectors[e.index];
            return map.things[e.index];
        }
        void Properties()
        {
            if(MultipleSectorsSelected()){SectorBatchProperties();return;}
            if(MultipleSelected(ElementKind.Thing)){ThingBatchProperties();return;}
            if(MultipleSelected(ElementKind.Line)){LineBatchProperties();return;}
            if(MultipleSelected(ElementKind.Vertex)){VertexBatchProperties();return;}
            if (Selection.Count != 1) return;
            ElementRef e = default(ElementRef); foreach (var selected in Selection) e = selected;
            string elementName = e.kind == ElementKind.Vertex ? "вершина" : e.kind == ElementKind.Line ? "стена" : e.kind == ElementKind.Sector ? "сектор" : "объект";
            ShowDialog("Свойства · " + elementName + " #" + e.index, body =>
            {
                if(e.kind==ElementKind.Line)LineLengthInfo(body,new[]{e.index});
                var refreshSectorHeight=e.kind==ElementKind.Sector?SectorHeightInfo(body,new[]{e.index}):null;
                var apply = new List<Action<MapDocument>>();
                var numericNote=Label(body,"У числа: = задать, + / − сдвинуть, × / ÷ масштабировать.",16);
                numericNote.alignment=TextAnchor.MiddleLeft;numericNote.gameObject.AddComponent<LayoutElement>().minHeight=40;
                Action<string, Func<MapDocument,int>, int, int, Action<MapDocument, int>> numeric = (name, get, min, max, setter) =>
                { SingleNumber(body,name,m=>get(m),(m,n)=>setter(m,checked((int)n)),min,max,true,apply); };
                Action<string, Func<MapDocument,string>, bool, Action<MapDocument, string>> texture = (name, get, flat, setter) =>
                { var field = Field(body, name, get(Map), false); apply.Add(m => {var next=TextureName(field);if(!string.Equals(get(m),next,StringComparison.OrdinalIgnoreCase))setter(m,next);});
                    PropertyTextureButton(body, "Выбрать: " + name + "…", field, flat ? ResourceKind.Flat : ResourceKind.Wall); };
                bool universal = MapFormats.IsUniversal(Map.format);
                int integerMin = universal ? int.MinValue : 0, integerMax = universal ? int.MaxValue : 65535;
                Action<string, Func<MapDocument,float>, float, float, Action<MapDocument, float>> real = (name, get, min, max, setter) =>
                {
                    SingleNumber(body,name,m=>get(m),(m,n)=>setter(m,(float)n),min,max,!universal,apply,true);
                };
                if (e.kind == ElementKind.Vertex)
                {
                    real("X", m=>m.vertices[e.index].x, -32768, 32767, (m, n) => m.vertices[e.index].x = n);
                    real("Y", m=>m.vertices[e.index].y, -32768, 32767, (m, n) => m.vertices[e.index].y = n);
                }
                if (e.kind == ElementKind.Thing)
                {
                    var t = Map.things[e.index];
                    var typeField = Field(body,"Тип объекта",t.type.ToString()); apply.Add(m => m.things[e.index].type = Number(typeField,integerMin,integerMax));
                    AddLabel(body,thingCatalog.Find(t.type).title + " · " + thingCatalog.Find(t.type).category);
                    AddButton(body,"Выбрать тип по спрайту…",() => ThingPicker(id => typeField.text = id.ToString()));
                    bool polyMarker=Map.format==MapFormat.Hexen&&PolyobjectChecks.IsMarker(thingCatalog.Find(t.type));
                    if(polyMarker)
                    {
                        var number=SingleNumber(body,"Номер полиобъекта",m=>m.things[e.index].angle,(m,n)=>m.things[e.index].angle=(float)n,0,65535,true,apply,true);
                        AddButton(body,"Выбрать номер полиобъекта…",()=>PolyobjectNumberPicker(number,65535));PolyobjectNumberHint(body);
                    }
                    else real("Угол", m=>m.things[e.index].angle, universal ? -36000 : 0, universal ? 36000 : 65535, (m, n) => m.things[e.index].angle = n);
                    real("X", m=>m.things[e.index].x, -32768, 32767, (m, n) => m.things[e.index].x = n);
                    real("Y", m=>m.things[e.index].y, -32768, 32767, (m, n) => m.things[e.index].y = n);
                    if (Map.format != MapFormat.Doom)
                    {
                        real("Высота", m=>m.things[e.index].z, -32768, 32767, (m, n) => m.things[e.index].z = n);
                        TagControl(body,"ID / тег",t.tag,integerMin,integerMax,14,apply,(m,n)=>m.things[e.index].tag=n);
                        ActionControls(body,t.action,t.args,apply,m => m.things[e.index],(m,n) => m.things[e.index].action = n,(m,a,n) => m.things[e.index].args[a] = n);
                    }
                    
#if MOBILEUDB_UDMF // Deferred from the first release.
if (universal)
                    {
                        string[] names = { "skill1", "skill2", "skill3", "skill4", "skill5", "ambush", "single", "coop", "dm", "class1", "class2", "class3" };
                        string[] labels = { "Сложность 1", "Сложность 2", "Сложность 3", "Сложность 4", "Сложность 5", "Засадный", "Одиночная игра", "Кооператив", "Deathmatch", "Класс 1", "Класс 2", "Класс 3" };
                        for (int f = 0; f < names.Length; f++) { string key = names[f]; bool fallback = !t.udmfInitialized && (f != 5 || (t.flags & 8) != 0); BooleanControl(body, labels[f], UdmfText.Boolean(t.fields, key, fallback), apply, (m, n) => UdmfText.SetBool(m.things[e.index].fields, key, n)); }
                    }
                    else
#endif
 BinaryFlags(body, t.flags, false, apply, (m, n) => m.things[e.index].flags = n);
                }
                if (e.kind == ElementKind.Sector)
                {
                    var s = Map.sectors[e.index];
                    real("Пол", m=>m.sectors[e.index].floor, -32768, 32767, (m, n) => m.sectors[e.index].floor = n);
                    real("Потолок", m=>m.sectors[e.index].ceiling, -32768, 32767, (m, n) => m.sectors[e.index].ceiling = n);
                    numeric("Свет", m=>m.sectors[e.index].light, universal ? int.MinValue : -32768, universal ? int.MaxValue : 32767, (m, n) => m.sectors[e.index].light = n);
                    ActionControls(body,s.special,null,apply,m => m.sectors[e.index],(m,n) => m.sectors[e.index].special = n,null,true);
                    TagControl(body,"Тег",s.tag,integerMin,integerMax,13,apply,(m,n)=>m.sectors[e.index].tag=n);
                    texture("Текстура пола", m=>m.sectors[e.index].floorTexture, true, (m, n) => m.sectors[e.index].floorTexture = n);
                    texture("Потолка", m=>m.sectors[e.index].ceilingTexture, true, (m, n) => m.sectors[e.index].ceilingTexture = n);
                    apply.Add(m => { if (m.sectors[e.index].floor > m.sectors[e.index].ceiling) throw new InvalidOperationException("Пол выше потолка."); });
                }
                if (e.kind == ElementKind.Line)
                {
                    var l = Map.lines[e.index];
                    var lineAction=ActionControls(body,l.action,l.args,apply,m => m.lines[e.index],(m,n) => m.lines[e.index].action = n,(m,a,n) => m.lines[e.index].args[a] = n);
                    if (Map.format != MapFormat.Hexen) TagControl(body,"Тег",l.tag,integerMin,integerMax,Map.format==MapFormat.Doom?13:15,apply,(m,n)=>m.lines[e.index].tag=n,()=>Map.format==MapFormat.Doom?BinaryLineTagType(lineAction):15);
                    SingleLineSidePanel(body,e.index,true,universal,apply);
                    SingleLineSidePanel(body,e.index,false,universal,apply);
                    InputField flagField = null;
                    
#if MOBILEUDB_UDMF // Deferred from the first release.
if (universal)
                    {
                        string[] labels = { "Непроходимая", "Блокирует монстров", "Двусторонняя", "Не крепить верх", "Не крепить низ", "Секрет", "Блокирует звук", "Скрыта на карте", "Показана на карте" };
                        for (int f = 0; f < labels.Length; f++) { int mask = 1 << f; BooleanControl(body, labels[f], (l.flags & mask) != 0, apply, (m, n) => m.lines[e.index].flags = n ? m.lines[e.index].flags | mask : m.lines[e.index].flags & ~mask); }
                    }
                    else
#endif
 flagField = BinaryFlags(body, l.flags, true, apply, (m, n) => m.lines[e.index].flags = n);
                    ActivationControls(body,l,flagField,apply,e.index);
                }
                var applyButton=DialogApply(body, () => Edit(m =>
                {
                    string before=JsonUtility.ToJson(m);foreach(var change in apply)change(m);
                    if(!universal&&m.sides.Count>65535)throw new InvalidOperationException("Превышен предел сторон Doom / Hexen.");
                    if(JsonUtility.ToJson(m)==before)throw new InvalidOperationException("Нет изменений: заполните нужное поле или измените флаг.");
                }, "Свойства обновлены."));
                if(refreshSectorHeight!=null)applyButton.onClick.AddListener(()=>refreshSectorHeight());
                
#if MOBILEUDB_UDMF // Deferred from the first release.
if (universal)
                {
                    AddButton(body, "Дополнительные поля UDMF…", () => UniversalFields(e));
                    if (e.kind == ElementKind.Line)
                    {
                        var l = Map.lines[e.index];
                        if (l.front >= 0) { int side = l.front; AddButton(body, "Поля передней стороны…", () => UniversalSideFields(side)); }
                        if (l.back >= 0) { int side = l.back; AddButton(body, "Поля задней стороны…", () => UniversalSideFields(side)); }
                    }
                }
#endif

            });
        }
#if MOBILEUDB_UDMF
        void UniversalFields(ElementRef e)
        {
            ShowUniversalFields("Дополнительные поля", m => Element(m, e), key => UdmfText.IsManaged(e.kind, key));
        }
        void UniversalSideFields(int index)
        {
            var managed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "sector", "offsetx", "offsety", "texturetop", "texturebottom", "texturemiddle" };
            ShowUniversalFields("Поля стороны #" + index, m => m.sides[index], key => managed.Contains(key));
        }
        void ShowUniversalFields(string heading, Func<MapDocument, MapElementData> element, Func<string, bool> managed)
        {
            ShowDialog(heading, body =>
            {
                var apply = new List<Action<MapDocument>>();
                foreach (var f in element(Map).fields)
                {
                    string key = f.name; if (managed(key)) continue;
                    bool boolean;
                    if (bool.TryParse(f.value, out boolean)) BooleanControl(body, key, boolean, apply, (m, n) => UdmfText.SetBool(element(m).fields, key, n));
                    else
                    {
                        bool quoted = f.value.StartsWith("\"", StringComparison.Ordinal);
                        var value = Field(body, key, quoted ? UdmfText.DecodeString(f.value) : f.value, false); value.characterLimit = 4096;
                        apply.Add(m => UdmfText.SetRaw(element(m).fields, key, quoted ? UdmfText.Quote(value.text) : value.text));
                    }
                }
                var name = Field(body, "Новое поле", "", false); name.characterLimit = 64;
                var raw = Field(body, "Значение", "", false); raw.characterLimit = 4096;
                AddLabel(body, "Новое значение: число, true / false или строка в кавычках. Основные поля редактируются в «Свойствах».");
                DialogApply(body, () => Edit(m =>
                {
                    foreach (var change in apply) change(m);
                    if (!string.IsNullOrWhiteSpace(name.text))
                    {
                        string key = name.text.Trim(); if (managed(key)) throw new InvalidOperationException("Это основное поле. Измените его в «Свойствах».");
                        UdmfText.SetRaw(element(m).fields, key, raw.text.Trim());
                    }
                }, "Поля UDMF обновлены."));
            });
        }
#endif
        void MoreMenu()
        {ShowDialog("Инструменты",BuildToolsMenu);}
        void FileMenu() {ShowDialog("Файл",BuildFileMenu);}
        void ConfirmReplace(Action replace)
        {
            if(!wadDirty){if(wadReference!=null)SaveWadSettingsOnly(replace);else {CloseDialog();replace();}return;}
            ShowDialog("Сохранить изменения?", body => { AddLabel(body, "Сохраните изменения текущего WAD перед закрытием."); AddButton(body, "Сохранить и продолжить", () => SaveWadMap(false,replace)); AddButton(body, "Продолжить без сохранения", () => { CloseDialog(); replace(); }); });
        }
        void NewMapMenu()
        {
            ShowDialog("Новая карта", body =>
            {
                var name = Field(body, "Имя в WAD", "MAP01", false); name.characterLimit = 8;
                AddLabel(body, "Doom: Vanilla / Boom / MBF / MBF21. Профиль выбирается в «Файл → Действия / движок». Hexen — отдельный бинарный формат.");
                var formats = new List<MapFormat> { MapFormat.Doom, MapFormat.Hexen };
#if MOBILEUDB_UDMF
                formats.Add(MapFormat.Udmf); // Deferred until a later release.
#endif
                foreach (MapFormat format in formats)
                {
                    MapFormat selected = format;
                    AddButton(body, selected.ToString(), () =>
                    {
                        string mapName = name.text.Trim().ToUpperInvariant();
                        if (mapName.Length == 0 || mapName.Length > 8) throw new InvalidOperationException("Имя карты: 1–8 символов.");
                        foreach (char c in mapName) if (c < 33 || c > 126) throw new InvalidOperationException("Имя карты должно быть ASCII.");
                        ConfirmReplace(() => { SaveRecovery();archive = null; sourceWad = null; sourceMapPackage = null; wadReference=null;wadConfiguration=null;wadMapResources.Clear();missingWadResources.Clear(); editedMaps.Clear(); mapGrids.Clear();mapViews.Clear();mapBackgrounds.Clear(); Replace(new MapDocument { name = mapName, format = selected }); });
                    });
                }
            });
        }
        void RememberCurrentMap() { RememberWadResources(); RememberClassicView();string key=MapNaming.Key(Map);editedMaps[key] = SnapshotMapWithCamera(); mapGrids[key]=gridFrame.Validated(); }
        public void PathDialog(string heading, Action<string> action, string initial = "")
        {
            ShowDialog(heading, body =>
            {
                AddLabel(body, "Введите полный путь к файлу."); var path = Field(body, "Путь", initial, false); path.characterLimit = 4096;
                AddButton(body, "Готово", () => { var chosen = path.text; CloseDialog(); action(chosen); });
            });
        }
        void ChooseWadMap()
        {
            var names=archive==null?new List<string>():archive.EditableMapNames();foreach(var key in editedMaps.Keys)if(!names.Exists(n=>string.Equals(n,key,StringComparison.OrdinalIgnoreCase)))names.Add(key);
            string current=MapNaming.Key(Map);if(!names.Exists(n=>string.Equals(n,current,StringComparison.OrdinalIgnoreCase)))names.Add(current);
            ShowDialog("Карты архива", body => { AddLabel(body, "Сохранение WAD включает изменения всех открытых карт."); foreach (string n in names) { string name = n; AddButton(body, DisplayMapName(name), () => { BeginMapSwitch(name); }); } });
        }
        void Replace(MapDocument map,DoomResources preparedCatalog = null,string preparedError = null)
        {
            ClearThingAimState();
            ClearLinedefCurve();
            map.Validate(); // Reject unsupported formats before replacing editor state.
            if(BlockmapActive)ExitBlockmap();
            if(SoundPropagationActive)ExitSoundPropagation();
            if(AutomapActive)ExitAutomap();
            CloseVisualTextureControls();
            GridFrame savedGrid;gridFrame=mapGrids.TryGetValue(MapNaming.Key(map),out savedGrid)?savedGrid.Validated():new GridFrame().Validated();
            CancelPaste(false);
            selectionBrush=SelectionBrush.Off;
            CancelDrag(); Map = map; Selection.Clear(); Draft.Clear(); history.Clear(); cutSector = -1; Tool = VisualActive ? MobileTool.Visual : MobileTool.Select; if (preparedCatalog == null) RebuildResources(); else { CloseTexturePicker(); ClearThumbnails(); resources = preparedCatalog; resourceError = preparedError; RebuildThingCatalog(); if (visualView != null) visualView.ResourcesChanged(); } if (VisualActive) visualView.ForgetSelection(); Changed();QueueClassicViewRestore(.05f); Notify("Открыто: " + map.name + ResourceWarning());
        }
    }
}




