using System;
using System.Collections.Generic;
using MobileUDB.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MobileUDB
{
    // The reference's left-hand metadata becomes two rows above the previews on narrow screens.
    public sealed class MobileSidePanelLayout : MonoBehaviour
    {
        public RectTransform metadata,textures;
        public LayoutElement height;
        public Action refreshSelection;
        float previousWidth=-1;
        void LateUpdate()
        {
            if(refreshSelection!=null)refreshSelection();
            float width=((RectTransform)transform).rect.width;if(width<=0||Mathf.Abs(width-previousWidth)<.1f)return;previousWidth=width;
            bool wide=width>=500;float left=wide?230:width;
            Position(metadata,left,138,0,0);Position(textures,wide?width-left-8:width,166,wide?left+8:0,wide?0:-146);
            height.preferredHeight=wide?166:312;LayoutRebuilder.MarkLayoutForRebuild((RectTransform)transform.parent);
        }
        static void Position(RectTransform rect,float width,float height,float x,float y)
        {rect.anchorMin=rect.anchorMax=new Vector2(0,1);rect.pivot=new Vector2(0,1);rect.sizeDelta=new Vector2(width,height);rect.anchoredPosition=new Vector2(x,y);}
    }
    public sealed partial class MobileEditor
    {
        static string SideTexturePart(int part) {return part==0?"Верх":part==1?"Середина":"Низ";}
        static string SideTextureValue(MapSide side,int part) {return part==0?side.upper:part==1?side.middle:side.lower;}
        static void SetSideTextureValue(MapSide side,int part,string value) {if(part==0)side.upper=value;else if(part==1)side.middle=value;else side.lower=value;}
        bool VisualSideTextureSelected(HashSet<int> lines,bool front,int part)
        {
            if(!VisualActive)return false;
            var role=part==0?SurfacePart.Upper:part==1?SurfacePart.Middle:SurfacePart.Lower;
            foreach(var id in visualView.SelectedSurfaces)
                if(id.part==role&&id.line>=0&&id.line<Map.lines.Count&&lines.Contains(id.line)&&(front?Map.lines[id.line].front:Map.lines[id.line].back)==id.side)return true;
            return false;
        }
        void SingleLineSidePanel(RectTransform body,int line,bool front,bool universal,List<Action<MapDocument>> apply)
        {
            string prefix=front?"Передняя":"Задняя";var lines=new HashSet<int>{line};
            if((front?Map.lines[line].front:Map.lines[line].back)<0){LineBatchCaption(body,prefix+" сторона · отсутствует");return;}
            BuildSideTexturePanel(body,prefix+" сторона",meta=>
            {
                var field=Field(meta,prefix+" · Сектор",BatchSide(Map,line,front).sector.ToString());
                apply.Add(map=>{int value=Number(field,0,map.sectors.Count-1);if(BatchSide(map,line,front).sector!=value)ExclusiveBatchSide(map,line,front).sector=value;});return field;
            },(meta,x)=>SingleNumber(meta,prefix+" · Сдвиг "+(x?"X":"Y"),map=>x?BatchSide(map,line,front).offsetX:BatchSide(map,line,front).offsetY,
                (map,n)=>{if(x)ExclusiveBatchSide(map,line,front).offsetX=checked((int)n);else ExclusiveBatchSide(map,line,front).offsetY=checked((int)n);},universal?int.MinValue:-32768,universal?int.MaxValue:32767,true,apply),
                (card,part)=>
                {
                    var field=Field(card,prefix+" · "+SideTexturePart(part),SideTextureValue(BatchSide(Map,line,front),part),false);
                    apply.Add(map=>{var next=TextureName(field);if(!string.Equals(SideTextureValue(BatchSide(map,line,front),part),next,StringComparison.OrdinalIgnoreCase))SetSideTextureValue(ExclusiveBatchSide(map,line,front),part,next);});return field;
                },part=>VisualSideTextureSelected(lines,front,part));
        }
        void BuildSideTexturePanel(RectTransform body,string title,Func<RectTransform,InputField> sector,Func<RectTransform,bool,InputField> offset,Func<RectTransform,int,InputField> texture,Func<int,bool> selected)
        {
            var panel=Rect("Отделка · "+title,body);panel.gameObject.AddComponent<Image>().color=new Color32(24,36,52,255);
            var outer=panel.gameObject.AddComponent<VerticalLayoutGroup>();outer.padding=new RectOffset(8,8,8,8);outer.spacing=6;outer.childControlWidth=outer.childControlHeight=outer.childForceExpandWidth=true;outer.childForceExpandHeight=false;
            LineBatchCaption(panel,title);
            var content=Rect("Параметры и текстуры · "+title,panel);var height=content.gameObject.AddComponent<LayoutElement>();height.preferredHeight=312;
            var meta=Rect("Сектор и смещения · "+title,content);var numbers=meta.gameObject.AddComponent<VerticalLayoutGroup>();numbers.spacing=6;numbers.childControlWidth=numbers.childControlHeight=numbers.childForceExpandWidth=true;numbers.childForceExpandHeight=false;
            var sectorField=sector(meta);var sectorRow=(RectTransform)sectorField.transform.parent;sectorRow.GetComponent<LayoutElement>().preferredHeight=58;
            var sectorLabel=sectorRow.GetChild(0).GetComponent<Text>();sectorLabel.text="Сектор";sectorLabel.fontSize=16;sectorLabel.rectTransform.anchorMax=new Vector2(.34f,1);
            var sr=sectorField.GetComponent<RectTransform>();sr.anchorMin=new Vector2(.36f,0);sectorField.textComponent.fontSize=18;
            var offsets=Rect("Смещения X / Y · "+title,meta);offsets.gameObject.AddComponent<LayoutElement>().preferredHeight=74;
            var xy=offsets.gameObject.AddComponent<HorizontalLayoutGroup>();xy.spacing=8;xy.childControlWidth=xy.childControlHeight=xy.childForceExpandWidth=true;xy.childForceExpandHeight=false;
            StyleSideOffset(offset(offsets,true),"Смещение X");StyleSideOffset(offset(offsets,false),"Смещение Y");
            var textures=Rect("Три текстуры · "+title,content);var columns=textures.gameObject.AddComponent<HorizontalLayoutGroup>();columns.spacing=8;columns.childControlWidth=columns.childControlHeight=columns.childForceExpandWidth=true;columns.childForceExpandHeight=false;
            Action refreshSelection=null;
            for(int part=0;part<3;part++)
            {
                var card=Rect(title+" · "+SideTexturePart(part),textures);var element=card.gameObject.AddComponent<LayoutElement>();element.minWidth=0;element.preferredWidth=0;element.flexibleWidth=1;element.preferredHeight=166;
                var stack=card.gameObject.AddComponent<VerticalLayoutGroup>();stack.spacing=4;stack.childControlWidth=stack.childControlHeight=stack.childForceExpandWidth=true;stack.childForceExpandHeight=false;
                var caption=Label(card,SideTexturePart(part),14);caption.gameObject.AddComponent<LayoutElement>().preferredHeight=18;
                InputField field=null;var preview=AddButton(card,"",()=>TexturePicker(field,ResourceKind.Wall));preview.name="Текстура · "+title+" · "+SideTexturePart(part);preview.GetComponent<LayoutElement>().preferredHeight=92;
                var frame=Rect("Превью",preview.transform);Stretch(frame);frame.offsetMin=new Vector2(4,4);frame.offsetMax=new Vector2(-4,-4);
                var picture=Rect("Texture preview",frame);Stretch(picture);var image=picture.gameObject.AddComponent<RawImage>();image.raycastTarget=false;
                var aspect=picture.gameObject.AddComponent<AspectRatioFitter>();aspect.aspectMode=AspectRatioFitter.AspectMode.FitInParent;
                var note=Label(frame,"",12);Stretch(note.rectTransform);note.raycastTarget=false;
                var dimensions=Label(preview.transform,"",11);var dr=dimensions.rectTransform;dr.anchorMin=new Vector2(0,1);dr.anchorMax=Vector2.one;dr.pivot=new Vector2(.5f,1);dr.sizeDelta=new Vector2(-8,16);dr.anchoredPosition=new Vector2(0,-2);dimensions.color=Color.white;dimensions.alignment=TextAnchor.UpperLeft;dimensions.raycastTarget=false;
                field=texture(card,part);field.characterLimit=MapFormats.IsUniversal(Map.format)?255:8;
                var nameRow=(RectTransform)field.transform.parent;nameRow.GetComponent<LayoutElement>().preferredHeight=48;nameRow.GetChild(0).gameObject.SetActive(false);Stretch(field.GetComponent<RectTransform>());
                field.textComponent.fontSize=16;field.textComponent.resizeTextForBestFit=true;field.textComponent.resizeTextMinSize=12;field.textComponent.resizeTextMaxSize=16;field.textComponent.alignment=TextAnchor.MiddleCenter;field.textComponent.rectTransform.offsetMin=new Vector2(4,0);field.textComponent.rectTransform.offsetMax=new Vector2(-4,0);
                if(field.placeholder!=null){var pr=field.placeholder.rectTransform;Stretch(pr);var pt=field.placeholder as Text;if(pt!=null){pt.fontSize=12;pt.alignment=TextAnchor.MiddleCenter;}}
                BindPropertyTexturePreview(field,ResourceKind.Wall,image,aspect,note,dimensions);
                int slot=part;var previewFrame=preview.GetComponent<Image>();var nameFrame=field.GetComponent<Image>();
                refreshSelection+=()=>
                {
                    bool active=selected(slot);previewFrame.color=active?Accent:Panel;nameFrame.color=active?Accent:Panel;
                    caption.color=active?(Color)new Color32(255,218,127,255):Color.white;
                };
            }
            var layout=content.gameObject.AddComponent<MobileSidePanelLayout>();layout.metadata=meta;layout.textures=textures;layout.height=height;layout.refreshSelection=refreshSelection;refreshSelection();
        }
        void StyleSideOffset(InputField field,string caption)
        {
            var row=(RectTransform)field.transform.parent;var element=row.GetComponent<LayoutElement>();element.preferredHeight=74;element.minWidth=0;element.preferredWidth=0;element.flexibleWidth=1;
            var label=row.GetChild(0).GetComponent<Text>();label.text=caption;label.fontSize=14;label.resizeTextMaxSize=14;label.resizeTextMinSize=11;
            var lr=label.rectTransform;lr.anchorMin=new Vector2(0,1);lr.anchorMax=Vector2.one;lr.pivot=new Vector2(.5f,1);lr.sizeDelta=new Vector2(0,20);lr.anchoredPosition=Vector2.zero;
            var input=field.GetComponent<RectTransform>();Stretch(input);input.offsetMax=new Vector2(-52,-24);field.textComponent.fontSize=16;field.textComponent.rectTransform.offsetMin=new Vector2(4,0);field.textComponent.rectTransform.offsetMax=new Vector2(-4,0);
            var mode=row.GetComponentInChildren<Button>();var mr=mode.GetComponent<RectTransform>();mr.anchorMin=mr.anchorMax=new Vector2(1,0);mr.pivot=new Vector2(1,0);mr.sizeDelta=new Vector2(48,50);mr.anchoredPosition=Vector2.zero;
        }
    }
}
