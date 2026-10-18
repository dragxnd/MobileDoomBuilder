using System;using System.Collections.Generic;using MobileUDB.Core;using UnityEngine;using UnityEngine.UI;
namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        List<TextureCategory> PickerCategories(List<ResourceTexture> choices)
        {
            string profile=effectiveProfile=="Doom2"?"Doom":effectiveProfile;
            var asset=Resources.Load<TextAsset>("MobileUDB/Actions/"+profile+"_texturesets");
            return TextureCatalogFilters.Categories(choices,asset==null?new ConfigNode():GameConfigText.Read(asset.text));
        }
        Dropdown TextureCategoryDropdown(RectTransform parent,List<TextureCategory> categories)
        {
            var rect=Rect("Texture category",parent);rect.gameObject.AddComponent<LayoutElement>().preferredHeight=56;
            var image=rect.gameObject.AddComponent<Image>();image.color=Panel;var dropdown=rect.gameObject.AddComponent<TextureCategoryDropdown>();dropdown.targetGraphic=image;
            var caption=Label(rect,"Все",18);Stretch(caption.rectTransform);caption.rectTransform.offsetMin=new Vector2(12,0);caption.rectTransform.offsetMax=new Vector2(-40,0);caption.alignment=TextAnchor.MiddleLeft;caption.horizontalOverflow=HorizontalWrapMode.Wrap;
            var arrow=Label(rect,"▾",22);var ar=arrow.rectTransform;ar.anchorMin=ar.anchorMax=new Vector2(1,.5f);ar.pivot=new Vector2(1,.5f);ar.sizeDelta=new Vector2(32,48);ar.anchoredPosition=new Vector2(-4,0);
            var template=Rect("Template",rect);template.anchorMin=Vector2.zero;template.anchorMax=new Vector2(1,0);template.pivot=new Vector2(.5f,1);template.sizeDelta=new Vector2(0,Mathf.Min(280,texturePicker.rect.height-100));template.anchoredPosition=new Vector2(0,-4);template.gameObject.AddComponent<Image>().color=Background;
            var scroll=template.gameObject.AddComponent<ScrollRect>();scroll.horizontal=false;scroll.movementType=ScrollRect.MovementType.Clamped;
            var viewport=Rect("Viewport",template);Stretch(viewport);viewport.gameObject.AddComponent<RectMask2D>();viewport.gameObject.AddComponent<Image>().color=Background;
            var content=Rect("Content",viewport);content.anchorMin=new Vector2(0,1);content.anchorMax=Vector2.one;content.pivot=new Vector2(.5f,1);content.sizeDelta=new Vector2(0,56);
            var item=Rect("Item",content);item.anchorMin=new Vector2(0,.5f);item.anchorMax=new Vector2(1,.5f);item.sizeDelta=new Vector2(0,56);item.gameObject.AddComponent<Image>().color=Panel;var toggle=item.gameObject.AddComponent<Toggle>();toggle.targetGraphic=item.GetComponent<Image>();
            var mark=Label(item,"✓",18);var mr=mark.rectTransform;mr.anchorMin=new Vector2(0,0);mr.anchorMax=new Vector2(0,1);mr.pivot=new Vector2(0,.5f);mr.sizeDelta=new Vector2(28,0);toggle.graphic=mark;
            var label=Label(item,"",16);Stretch(label.rectTransform);label.rectTransform.offsetMin=new Vector2(32,0);label.rectTransform.offsetMax=new Vector2(-8,0);label.alignment=TextAnchor.MiddleLeft;
            scroll.viewport=viewport;scroll.content=content;dropdown.template=template;dropdown.captionText=caption;dropdown.itemText=label;
            dropdown.options.Add(new Dropdown.OptionData("Все"));foreach(var category in categories)dropdown.options.Add(new Dropdown.OptionData(category.name));
            dropdown.SetValueWithoutNotify(0);dropdown.RefreshShownValue();dropdown.interactable=categories.Count>0;template.gameObject.SetActive(false);return dropdown;
        }
    }
}
