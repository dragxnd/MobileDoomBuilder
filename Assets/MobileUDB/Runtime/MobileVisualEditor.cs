using System;
using System.Collections.Generic;
using MobileUDB.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        VisualView visualView;
        readonly List<RectTransform> visualGroups = new List<RectTransform>();
        readonly List<Button> visualEditButtons = new List<Button>();
        RectTransform visualSurfaceGroup, visualThingGroup;
        Button visualPropertiesButton;
        readonly List<Button> visualThingButtons = new List<Button>();
        const float VisualToolbarSpacing = 2;
        int visualStep = 8;
        readonly List<Button> visualLightButtons = new List<Button>();
        public bool VisualActive { get { return visualView != null && visualView.gameObject.activeSelf; } }
        public bool IsVisualViewport(GameObject hit) { return VisualActive && visualView.gameObject == hit; }
        public void GestureNavigation(Vector2 oldA, Vector2 oldB, Vector2 a, Vector2 b)
        { if (VisualActive) visualView.Gesture(oldA,oldB,a,b); else View.Gesture(oldA,oldB,a,b); }
        public void WheelNavigation(Vector2 position, float amount)
        { if (VisualActive) visualView.Move(Vector3.forward,amount * .3f); else View.ZoomAt(position,Mathf.Exp(amount * .001f)); }
        void EnterVisual()
        {
            if (visualView == null) BuildVisualUI();
            View.enabled = false; visualView.gameObject.SetActive(true);
            EnsureClassicView();RestoreVisualCamera();
            foreach (var group in categoryGroups.Values) group.gameObject.SetActive(false);
            foreach (var group in visualGroups) group.gameObject.SetActive(true);
            Tool = MobileTool.Visual; Selection.Clear(); ApplyToolbarLayout(); Changed(false);
            Notify("Касание выбирает поверхность или объект; движение пальца поворачивает камеру. Два пальца — движение камеры.");
        }
        void ExitVisual()
        {
            CancelDrag(); CloseVisualTextureControls(); VisualBrush=VisualSelectionBrush.Off;
            ObserveVisualCamera();
            bool markerChanged;string cameraWarning=SynchronizeCameraMarker(Map,out markerChanged);
            if(markerChanged){viewRecoveryRevision.Changed();saveAt=Time.unscaledTime+2;}
            if(cameraWarning!=null)Notify(cameraWarning);
            if (visualView != null) visualView.gameObject.SetActive(false);
            foreach (var group in visualGroups) group.gameObject.SetActive(false);
            if (visualThingGroup != null) visualThingGroup.gameObject.SetActive(false);
            foreach (var group in categoryGroups.Values) group.gameObject.SetActive(true);
            Selection.RemoveWhere(id=>id.kind!=Kind);
            View.enabled = true; Tool = MobileTool.Select; ApplyToolbarLayout(); Changed(false);
        }
        void BuildVisualUI()
        {
            var rect = Rect("3D viewport",viewportRoot); Stretch(rect); rect.gameObject.AddComponent<RawImage>().color = Color.white;
            visualView = rect.gameObject.AddComponent<VisualView>(); visualView.Initialize(this); rect.gameObject.SetActive(false);
            BuildVisualPaintControls(rect);
            BuildVisualTextureControls(rect);
            var cross = Label(rect,"+",30); cross.color = new Color32(235,240,245,160); cross.rectTransform.anchorMin = cross.rectTransform.anchorMax = new Vector2(.5f,.5f); cross.rectTransform.sizeDelta = new Vector2(30,30);
            RectTransform group;
            var navigation = MakeToolbarGroup(footerRoot,"ДВИЖЕНИЕ · УДЕРЖИВАЙТЕ",out group); visualGroups.Add(group);
            VisualMove(navigation,"Влево",Vector3.left,EditorIcon.Symbol.Up,90,false);
            VisualMove(navigation,"Вперёд",Vector3.forward,EditorIcon.Symbol.Up,0,false);
            VisualMove(navigation,"Назад",Vector3.back,EditorIcon.Symbol.Down,0,false);
            VisualMove(navigation,"Вправо",Vector3.right,EditorIcon.Symbol.Up,-90,false);
            VisualMove(navigation,"Выше",Vector3.up,EditorIcon.Symbol.Up,0,true);
            VisualMove(navigation,"Ниже",Vector3.down,EditorIcon.Symbol.Down,0,true);
            var surfaces = MakeToolbarGroup(footerRoot,"ВЫСОТЫ И ТЕКСТУРА",out group); visualGroups.Add(group); visualSurfaceGroup = group;
            VisualHeightButton(surfaces,false,-1); VisualHeightButton(surfaces,false,1);
            VisualHeightButton(surfaces,false,-1,1); VisualHeightButton(surfaces,false,1,1);
            VisualHeightButton(surfaces,true,-1); VisualHeightButton(surfaces,true,1);
            BuildVisualTextureToolbar(surfaces);
            var actions = MakeToolbarGroup(footerRoot,"ДЕЙСТВИЯ",out group); visualGroups.Add(group);
            // Overflow keeps the first nine commands. Navigation takes priority
            // over display options, which stay at the end of the action group.
            var exit = IconButton(actions,"Вернуться к 2D-редактору",EditorIcon.Symbol.Visual,ExitVisual); Badge(exit.transform,"2D",false);
            visualSelectionButton=IconButton(actions,"Групповой выбор поверхностей и объектов: повторное касание снимает отметку",EditorIcon.Symbol.Select,ToggleVisualSelection);Badge(visualSelectionButton.transform,"+",false);
            visualTextureControlsButton=IconButton(actions,"Ручное выравнивание текстуры · стрелки",EditorIcon.Symbol.Texture,ToggleVisualTextureControls);Badge(visualTextureControlsButton.transform,"XY",false);
            visualPropertiesButton = IconButton(actions,"Свойства выбранного элемента",EditorIcon.Symbol.Properties,VisualProperties);
            BuildSectorToolbarButtons(actions,true);
            visualCopyTextureButton = IconButton(actions,"Копировать текстуру выбранной поверхности",EditorIcon.Symbol.Copy,VisualCopyTexture);
            visualPasteTextureButton = IconButton(actions,"Вставить скопированную текстуру поверхности",EditorIcon.Symbol.Paste,VisualPasteTexture);
            foreach(int delta in new[]{-16,16}) { int step=delta; var light=IconButton(actions,step<0?"Свет сектора −16":"Свет сектора +16",EditorIcon.Symbol.BrightnessGradient,()=>VisualLight(step)); Badge(light.transform,step<0?"−16":"+16",false); visualLightButtons.Add(light); }
            IconButton(actions,"3D: шаг высоты, свет, смещения текстуры",EditorIcon.Symbol.Configuration,VisualMoreMenu);
            BuildDisplayLightingToggle(actions);BuildDisplayThingsToggle(actions);
            var objects = MakeToolbarGroup(footerRoot,"ОБЪЕКТ · XY И ТИП",out group); visualThingGroup = group;
            VisualThingMove(objects,"Объект: X −",-1,0,90); VisualThingMove(objects,"Объект: Y +",0,1,0); VisualThingMove(objects,"Объект: Y −",0,-1,180); VisualThingMove(objects,"Объект: X +",1,0,-90);
            visualThingButtons.Add(IconButton(objects,"Выбрать тип объекта по спрайту",EditorIcon.Symbol.Thing,VisualThingTypeMenu));
            visualThingGroup.gameObject.SetActive(false);
            foreach (var item in visualGroups) item.gameObject.SetActive(false);
        }
        void VisualHeightButton(Transform parent, bool ceiling, int direction, int fixedStep = 0)
        {
            string hint=(ceiling ? "Потолок" : "Пол") + (direction < 0 ? " ниже" : " выше") + (fixedStep>0 ? " на "+fixedStep : "");
            var button = IconButton(parent,hint,ceiling ? EditorIcon.Symbol.Ceiling : EditorIcon.Symbol.Floor,() => VisualHeightAmount(ceiling,direction * (fixedStep>0 ? fixedStep : visualStep)));
            Badge(button.transform,direction < 0 ? "−" : "+",true); visualEditButtons.Add(button);
            if(fixedStep>0)
            {
                var digit=Label(button.transform,fixedStep.ToString(),15);digit.color=new Color32(255,218,127,255);
                var rect=digit.rectTransform;rect.anchorMin=rect.anchorMax=new Vector2(1,0);rect.pivot=new Vector2(1,0);rect.sizeDelta=new Vector2(18,20);rect.anchoredPosition=new Vector2(-1,1);
            }
        }
        void Badge(Transform parent, string text, bool large)
        {
            var badge = Label(parent,text,large ? 25 : 13); badge.verticalOverflow=VerticalWrapMode.Overflow; badge.color = new Color32(255,218,127,255); var rect = badge.rectTransform;
            rect.anchorMin = rect.anchorMax = Vector2.one; rect.pivot = Vector2.one; rect.sizeDelta = new Vector2(24,large ? 28 : 18); rect.anchoredPosition = new Vector2(-1,-1);
        }
        void VisualMove(Transform parent, string hint, Vector3 direction, EditorIcon.Symbol symbol, float angle, bool vertical)
        {
            var rect = Rect(hint,parent); var image = rect.gameObject.AddComponent<Image>(); image.color = Panel;
            var button = rect.gameObject.AddComponent<VisualMoveButton>(); button.targetGraphic = image; button.owner = this; button.direction = direction; button.hint = hint;
            var iconRect = Rect("Icon",rect); iconRect.anchorMin = iconRect.anchorMax = new Vector2(.5f,.5f); iconRect.sizeDelta = new Vector2(32,32); iconRect.localRotation = Quaternion.Euler(0,0,angle);
            var icon = iconRect.gameObject.AddComponent<EditorIcon>(); icon.symbol = symbol; icon.color = Color.white; icon.raycastTarget = false;
            if (vertical) Badge(rect,"Z",false);
        }
        internal void VisualMoveHeld(Vector3 direction, float seconds) { if (VisualActive && !ModalOpen) visualView.Move(direction,seconds * 192); }
        void ApplyVisualLayout(MobileToolbarLayout plan,float width)
        {
            for (int i = 0; i < visualGroups.Count; i++)
            {
                var layout=plan.groups[i];var grid=visualGroups[i].GetComponentInChildren<GridLayoutGroup>(true);grid.spacing=new Vector2(VisualToolbarSpacing,MobileToolbarLayout.Spacing);int count=ToolbarButtonCount(visualGroups[i]);
                if(layout.rows>1){layout.columns=(count+layout.rows-1)/layout.rows;layout.cellWidth=(layout.width-24-(layout.columns-1)*VisualToolbarSpacing)/layout.columns;}
                ApplyToolbarGroup(visualGroups[i],layout,width);
            }
            if (visualThingGroup != null)
            {
                visualThingGroup.GetComponentInChildren<GridLayoutGroup>(true).spacing=new Vector2(VisualToolbarSpacing,MobileToolbarLayout.Spacing);
                ApplyToolbarGroup(visualThingGroup,plan.groups[1],width);
            }
        }
        void RefreshVisualLabels()
        {
            RefreshVisualPaintControls();
            if (!VisualActive) return;
            if(visualView.Picked.HasValue&&!SurfaceTextureEdits.Valid(Map,visualView.Picked.Value))visualView.ForgetSelection();
            bool selected = visualView.Picked.HasValue;
            bool objectSelected = visualView.PickedThing >= 0 && visualView.PickedThing < Map.things.Count;
            visualSurfaceGroup.gameObject.SetActive(!objectSelected); visualThingGroup.gameObject.SetActive(objectSelected);
            foreach (var button in visualEditButtons) button.interactable = selected;
            foreach (var button in visualLightButtons) button.interactable = VisualSectorTargets().Count > 0;
            foreach (var button in visualThingButtons) button.interactable = objectSelected;
            visualPropertiesButton.interactable = (selected || objectSelected)&&VisualPropertiesAvailable();
            visualSelectionButton.GetComponent<Image>().color=VisualMultipleSelection&&!PaintingVisualSelection?Accent:Panel;
            RefreshVisualClipboardButtons();
            RefreshVisualTextureControls();
            string surface = "Коснитесь стены, пола, потолка или объекта";
            if (objectSelected) { var thing = Map.things[visualView.PickedThing]; surface = thingCatalog.Find(thing.type).title + " · #" + visualView.PickedThing + " · " + thing.type + " · угол " + thing.angle + " · Z " + thing.z; }
            if (selected)
            {
                var id = visualView.Picked.Value;
                if (id.sector < Map.sectors.Count)
                {
                    string[] names = { "Пол","Потолок","Верх стены","Середина стены","Низ стены" }; var sector = Map.sectors[id.sector];
                    surface = names[(int)id.part] + " · " + VisualGeometry.Texture(Map,id) + " · пол " + sector.floor + " / потолок " + sector.ceiling;
                    if(visualView.SelectedSurfaces.Count>1)surface="Выбрано поверхностей: "+visualView.SelectedSurfaces.Count+" · "+VisualGeometry.Texture(Map,id);
                }
            }
            if(visualView.SelectionCount>1)surface="Поверхностей: "+visualView.SelectedSurfaces.Count+" · объектов: "+visualView.SelectedThings.Count+" · "+(objectSelected?"#"+visualView.PickedThing:visualView.Picked.HasValue?VisualGeometry.Texture(Map,visualView.Picked.Value):"Выбор");
            status.text = "3D · шаг " + visualStep + " · " + surface + "\n" + message;
        }
        internal void SelectVisualSurface(SurfaceRef? selected)
        {
            Selection.Clear();
            if(visualView.PickedThing>=0)Kind=ElementKind.Thing;
            else if (selected.HasValue)
            {
                var id = selected.Value; Kind = id.IsFlat ? ElementKind.Sector : ElementKind.Line;
            }
            foreach(var surface in visualView.SelectedSurfaces)Selection.Add(new ElementRef(surface.IsFlat?ElementKind.Sector:ElementKind.Line,surface.IsFlat?surface.sector:surface.line));
            foreach(int index in visualView.SelectedThings)Selection.Add(new ElementRef(ElementKind.Thing,index));
            Changed(false);
        }
        void VisualThingMove(Transform parent, string hint, int x, int y, float angle)
        {
            var button = IconButton(parent,hint,EditorIcon.Symbol.Up,() => MoveVisualThings(x,y,0));
            var icon = button.GetComponentInChildren<EditorIcon>(); icon.transform.localRotation = Quaternion.Euler(0,0,angle); visualThingButtons.Add(button);
        }
        void VisualThingTypeMenu()
        {
            int index = visualView.PickedThing; if (index < 0) return;
            var indices=new List<int>(visualView.SelectedThings);
            ShowDialog("Тип объекта",body => { var field = Field(body,"Тип",Map.things[index].type.ToString()); AddButton(body,"Каталог типов…",() => ThingPicker(id => field.text = id.ToString()));
                AddButton(body,"Применить",() => { int type = Number(field,0,65535); CloseDialog(); TypeVisualThings(indices,type); }); });
            ThingPicker(id => { CloseDialog(); TypeVisualThings(indices,id); });
        }
        void VisualHeight(bool ceiling, int direction) { VisualHeightAmount(ceiling,direction * visualStep); }
        void VisualHeightAmount(bool ceiling, int amount)
        {
            if (!visualView.Picked.HasValue) return;var sectors=VisualSectorTargets();
            Edit(m => {foreach(int sector in sectors)VisualGeometry.ChangeHeight(m,sector,ceiling,amount);},(ceiling ? "Потолок" : "Пол") + " изменён на " + amount + " · секторов: "+sectors.Count+".");
        }
        void VisualTextureMenu()
        {
            if (!visualView.Picked.HasValue) return; var surface = visualView.Picked.Value;
            ShowDialog("Текстура поверхности",body =>
            {
                var name = Field(body,"Текстура",VisualGeometry.Texture(Map,surface),false);
                AddButton(body,"Выбрать по миниатюрам…",() => TexturePicker(name,surface.IsFlat ? ResourceKind.Flat : ResourceKind.Wall));
                AddButton(body,"Применить",() => { string texture = TextureName(name); CloseDialog(); ApplyVisualTexture(surface,texture); });
                var transfer = VisualSkyTransfer(surface);
                if (transfer != null) AddButton(body,"Небо: текстура и смещение…",() => VisualSkyTransferMenu(transfer.line));
                if (!surface.IsFlat) AddButton(body,"Выровнять по соседним стенам…",() => TextureAlignmentAxes(surface));
            });
        }
        SkyTransfer VisualSkyTransfer(SurfaceRef surface)
        {
            if (!surface.IsFlat || !GameSky.Is(Map,VisualGeometry.Texture(Map,surface))) return null;
            SkyTransfer transfer; SkyTransfers.Build(Map,Actions.mbf || Actions.extended).TryGetValue(Map.sectors[surface.sector].tag,out transfer); return transfer;
        }
        void VisualSkyTransferMenu(int line)
        {
            ShowDialog("Небо · стена #" + line,body =>
            {
                AddLabel(body,"Верхняя текстура первой стороны задаёт небо всех секторов с этим тегом. "+MakeSectorSky+" остаётся на полу / потолке.");
                var name = Field(body,"Текстура неба",BatchSide(Map,line,true).upper,false);
                AddButton(body,"Выбрать по миниатюрам…",() => TexturePicker(name,ResourceKind.Wall));
                var apply = new List<Action<MapDocument>>();
                SingleNumber(body,"Поворот · сдвиг X",m=>BatchSide(m,line,true).offsetX,(m,n)=>ExclusiveBatchSide(m,line,true).offsetX=(int)n,-32768,32767,true,apply);
                SingleNumber(body,"Сдвиг Y",m=>BatchSide(m,line,true).offsetY,(m,n)=>ExclusiveBatchSide(m,line,true).offsetY=(int)n,-32768,32767,true,apply);
                AddLabel(body,"X: 16384 = 90°. Y: 128 даёт обычное положение неба Doom. Превью статическое.");
                AddButton(body,"Применить",() =>
                {
                    string texture = TextureName(name);
                    if (Edit(m =>
                    {
                        string before = JsonUtility.ToJson(m);
                        foreach (var change in apply) change(m);
                        if (!string.Equals(BatchSide(m,line,true).upper,texture,StringComparison.OrdinalIgnoreCase)) ExclusiveBatchSide(m,line,true).upper = texture;
                        if (JsonUtility.ToJson(m) == before) throw new InvalidOperationException("Ничего не изменено.");
                    },"Небо изменено.")) CloseDialog();
                });
            });
        }
        void VisualMoreMenu(){ShowDialog("3D-инструменты",BuildVisualToolsMenu);}
    }
    public sealed class VisualMoveButton : Button
    {
        public MobileEditor owner; public Vector3 direction; public string hint;
        bool held;
        public override void OnPointerDown(PointerEventData eventData)
        {
            base.OnPointerDown(eventData);
            if (eventData.button == PointerEventData.InputButton.Left && IsInteractable()) { held = true; owner.Notify(hint + ": удерживайте для движения."); }
        }
        public override void OnPointerUp(PointerEventData eventData) { base.OnPointerUp(eventData); held = false; }
        protected override void OnDisable() { held = false; base.OnDisable(); }
        public override void OnPointerExit(PointerEventData eventData) { base.OnPointerExit(eventData); held = false; }
        void OnApplicationFocus(bool focus) { if (!focus) held = false; }
        void OnApplicationPause(bool pause) { if (pause) held = false; }
        void Update() { if (owner == null || owner.ModalOpen) held = false; if (held) owner.VisualMoveHeld(direction,Time.unscaledDeltaTime); }
    }
}

