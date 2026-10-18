using System;
using System.Collections.Generic;
using MobileUDB.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MobileUDB
{
    // Doom angles: east = 0, north = 90. Touch selection matches UDB's 45-degree dial.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class MobileAngleDial : MaskableGraphic, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public int angle;
        public bool hasAngle = true;
        public Action<int> changed;
        int? pointer;
        public void OnPointerDown(PointerEventData data)
        { if(data.button!=PointerEventData.InputButton.Left||pointer.HasValue)return; pointer=data.pointerId; Select(data); }
        public void OnDrag(PointerEventData data) { if(pointer==data.pointerId)Select(data); }
        public void OnPointerUp(PointerEventData data) { if(pointer==data.pointerId)pointer=null; }
        protected override void OnDisable() { pointer=null; base.OnDisable(); }
        void Select(PointerEventData data)
        {
            Vector2 p; RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform,data.position,data.pressEventCamera,out p);p-=rectTransform.rect.center;
            if(p.sqrMagnitude<36)return;
            angle=((Mathf.RoundToInt(Mathf.Atan2(p.y,p.x)*Mathf.Rad2Deg/45)*45)%360+360)%360;hasAngle=true;SetVerticesDirty();changed?.Invoke(angle);
        }
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();var center=rectTransform.rect.center;float radius=Mathf.Min(rectTransform.rect.width,rectTransform.rect.height)*.5f-4;
            for(int i=0;i<64;i++)Line(mesh,center+Direction(i*360f/64)*radius,center+Direction((i+1)*360f/64)*radius,2,new Color32(143,170,190,255));
            for(int i=0;i<8;i++){var d=Direction(i*45);Line(mesh,center+d*(radius-13),center+d*(radius-4),2,new Color32(143,170,190,255));}
            if(!hasAngle)return;
            var tip=center+Direction(angle)*(radius-18);var side=Direction(angle+90);var back=tip-Direction(angle)*13;
            Line(mesh,center,tip,3,new Color32(104,217,247,255));Line(mesh,tip,back+side*7,3,new Color32(104,217,247,255));Line(mesh,tip,back-side*7,3,new Color32(104,217,247,255));
        }
        static Vector2 Direction(float a) {return new Vector2(Mathf.Cos(a*Mathf.Deg2Rad),Mathf.Sin(a*Mathf.Deg2Rad));}
        static void Line(VertexHelper mesh,Vector2 a,Vector2 b,float width,Color color)
        {
            var o=new Vector2(a.y-b.y,b.x-a.x).normalized*width*.5f;int n=mesh.currentVertCount;
            mesh.AddVert(a-o,color,Vector2.zero);mesh.AddVert(a+o,color,Vector2.zero);mesh.AddVert(b+o,color,Vector2.zero);mesh.AddVert(b-o,color,Vector2.zero);mesh.AddTriangle(n,n+1,n+2);mesh.AddTriangle(n,n+2,n+3);
        }
    }

    public sealed partial class MobileEditor
    {
        Button headerGridSmaller,headerGridLarger;
        readonly Dictionary<ResourceTexture,Texture2D> propertyPreviews=new Dictionary<ResourceTexture,Texture2D>();
        void BeginWallChain()
        {
            if(Draft.Count>0){Notify("Завершите или отмените текущее рисование.");return;}
            SetTool(MobileTool.Select);StartDrawing(true);
        }
        bool SelectedSectorCreationReady
        {
            get
            {
                if(VisualActive||Draft.Count>0||Tool==MobileTool.Paste||Selection.Count==0)return false;
                foreach(var item in Selection)if(item.kind!=ElementKind.Sector||item.index<0||item.index>=Map.sectors.Count)return false;
                return true;
            }
        }
        void BeginMakeSectorKeepTextures()
        {
            if(!SelectedSectorCreationReady){Notify("Выделите сектор или несколько секторов и завершите рисование / вставку.");return;}
            var selected=new List<ElementRef>(Selection);CloseDialog();makeSectorAutoClear=false;ClearMakeSectorPreview();
            if(Edit(map=>
            {
                var created=map.MakeSelectedSectors(selected,drawTextures,MakeSectorSky,false);
                Selection.Clear();foreach(int index in created)Selection.Add(new ElementRef(ElementKind.Sector,index));Kind=ElementKind.Sector;
            },"Сектор из контура создан · текстуры сохранены. Один Undo отменяет операцию."))SetTool(MobileTool.Select);
        }
        void CubeModeLabel(Button button,string text)
        {
            var plate=Rect(text+" badge",button.transform);plate.anchorMin=plate.anchorMax=new Vector2(.5f,.5f);plate.sizeDelta=new Vector2(28,22);plate.anchoredPosition=new Vector2(0,-8);
            var background=plate.gameObject.AddComponent<Image>();background.color=new Color32(15,23,35,240);background.raycastTarget=false;
            var outline=plate.gameObject.AddComponent<Outline>();outline.effectColor=new Color32(255,218,127,220);outline.effectDistance=new Vector2(1,-1);
            var label=Label(plate,text,13);Stretch(label.rectTransform);((MobileLocalizedText)label).SetUntranslated(true);label.fontStyle=FontStyle.Bold;label.verticalOverflow=VerticalWrapMode.Overflow;label.horizontalOverflow=HorizontalWrapMode.Overflow;label.raycastTarget=false;
        }
        void BuildHeaderGridButtons(RectTransform header)
        {
            headerGridSmaller=IconButton(header,"Уменьшить шаг сетки вдвое",EditorIcon.Symbol.GridDecrease,()=>ChangeHeaderGrid(.5));
            headerGridLarger=IconButton(header,"Увеличить шаг сетки вдвое",EditorIcon.Symbol.GridIncrease,()=>ChangeHeaderGrid(2));
            int i=0;foreach(var button in new[]{headerGridLarger,headerGridSmaller}){var r=button.GetComponent<RectTransform>();r.anchorMin=new Vector2(1,0);r.anchorMax=Vector2.one;r.sizeDelta=new Vector2(44,-8);r.anchoredPosition=new Vector2(-120-48*i++,0);}
        }
        void ChangeHeaderGrid(double factor)
        {
            if(VisualActive||dragOrigin!=null)return;
            gridFrame.step=Math.Max(1,Math.Min(65536,gridFrame.step*factor));gridFrame.dynamicGrid=false;
            mapGrids[MapNaming.Key(Map)]=gridFrame.Copy();viewRecoveryRevision.Changed();saveAt=Time.unscaledTime+2;Changed(false);Notify("Сетка · "+GridStepText);
        }
        void AddAngleDial(RectTransform body,InputField field)
        {
            bool landscape=Screen.width>Screen.height;
            var row=Rect("Направление объекта",body);row.gameObject.AddComponent<LayoutElement>().preferredHeight=landscape?110:170;
            var rect=Rect("Циферблат угла",row);rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f);rect.sizeDelta=Vector2.one*(landscape?100:160);
            var dial=rect.gameObject.AddComponent<MobileAngleDial>();
            Action<string> refresh=value=>{int n;dial.hasAngle=int.TryParse(value,out n);dial.angle=(n%360+360)%360;dial.SetVerticesDirty();};
            dial.changed=value=>field.text=value.ToString();field.onValueChanged.AddListener(value=>refresh(value));refresh(field.text);
        }
        void PropertyTextureButton(RectTransform body,string label,InputField field,ResourceKind kind)
        {
            var button=AddButton(body,label,()=>TexturePicker(field,kind));button.gameObject.GetComponent<LayoutElement>().preferredHeight=96;
            var text=button.GetComponentInChildren<Text>();text.rectTransform.offsetMin=new Vector2(96,0);text.resizeTextForBestFit=true;text.resizeTextMinSize=14;text.resizeTextMaxSize=20;
            var frame=Rect("Превью выбранной текстуры",button.transform);frame.anchorMin=frame.anchorMax=new Vector2(0,.5f);frame.pivot=new Vector2(0,.5f);frame.sizeDelta=new Vector2(80,80);frame.anchoredPosition=new Vector2(8,0);
            var imageRect=Rect("Texture preview",frame);Stretch(imageRect);var image=imageRect.gameObject.AddComponent<RawImage>();image.raycastTarget=false;
            var aspect=imageRect.gameObject.AddComponent<AspectRatioFitter>();aspect.aspectMode=AspectRatioFitter.AspectMode.FitInParent;
            var note=Label(frame,"",12);Stretch(note.rectTransform);note.raycastTarget=false;
            BindPropertyTexturePreview(field,kind,image,aspect,note);
        }
        void BindPropertyTexturePreview(InputField field,ResourceKind kind,RawImage image,AspectRatioFitter aspect,Text note,Text dimensions=null)
        {
            Action refresh=()=>
            {
                image.texture=null;image.color=Color.clear;note.text="";if(dimensions!=null)dimensions.text="";
                if(string.IsNullOrWhiteSpace(field.text)){note.text="Разные";return;}
                if(field.text.Trim()=="-"){note.text="Нет";return;}
                try
                {
                    var source=resources.Find(field.text.Trim(),kind);if(source==null){note.text="Нет\nресурса";return;}
                    Texture2D texture;if(!propertyPreviews.TryGetValue(source,out texture))
                    {var decoded=resources.Decode(source,DecodePng,128);texture=new Texture2D(decoded.width,decoded.height,TextureFormat.RGBA32,false);texture.name="UDB properties "+source.name;texture.filterMode=FilterMode.Point;texture.LoadRawTextureData(FlipRows(decoded.rgba,decoded.width,decoded.height));texture.Apply(false,true);propertyPreviews.Add(source,texture);}
                    image.texture=texture;image.color=Color.white;aspect.aspectRatio=texture.width/(float)texture.height;
                    if(dimensions!=null&&source.width>0&&source.height>0)dimensions.text=source.width+"×"+source.height;
                    if(propertyPreviews.Count>32&&modal!=null)
                    {
                        var used=new HashSet<Texture>();foreach(var visible in modal.GetComponentsInChildren<RawImage>(true))if(visible.texture!=null)used.Add(visible.texture);
                        var remove=new List<ResourceTexture>();foreach(var item in propertyPreviews)if(!used.Contains(item.Value))remove.Add(item.Key);
                        foreach(var key in remove){if(propertyPreviews.Count<=32)break;Destroy(propertyPreviews[key]);propertyPreviews.Remove(key);}
                    }
                }
                catch(Exception){note.text="Нет\nпревью";}
            };
            field.onValueChanged.AddListener(_=>refresh());refresh();
        }
        void ClearPropertyPreviews() {foreach(var texture in propertyPreviews.Values)if(texture!=null)Destroy(texture);propertyPreviews.Clear();}
    }
}
