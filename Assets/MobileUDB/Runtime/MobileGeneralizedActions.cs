using System;
using System.Collections.Generic;
using MobileUDB.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        void GeneralizedPicker(InputField target, bool sector, bool allowEmpty=false)
        {
            int pending;if(!int.TryParse(target.text,out pending))
            {if(allowEmpty&&string.IsNullOrWhiteSpace(target.text))pending=0;else{Notify("Сначала исправьте числовой код действия.");return;}}
            var catalog=Actions;var category=sector?null:catalog.generalizedLines.Find(x=>x.Contains(pending));
            if(!sector && category==null){category=catalog.generalizedLines[0];pending=category.offset;}
            CloseTexturePicker();texturePicker=Rect("Generalized picker",modal);ScaleDialogBounds(texturePicker,1f);texturePicker.gameObject.AddComponent<Image>().color=Background;
            var heading=Label(texturePicker,sector?"Обобщённый эффект":"Действие Boom",24);var hr=heading.rectTransform;
            hr.anchorMin=new Vector2(0,1);hr.anchorMax=Vector2.one;hr.pivot=new Vector2(.5f,1);hr.sizeDelta=new Vector2(-140,62);hr.anchoredPosition=new Vector2(-65,0);
            var cancel=Button(texturePicker,"Назад",CloseTexturePicker);var cr=cancel.GetComponent<RectTransform>();cr.anchorMin=cr.anchorMax=Vector2.one;cr.pivot=Vector2.one;cr.sizeDelta=new Vector2(108,56);cr.anchoredPosition=new Vector2(-8,-4);
            var apply=Button(texturePicker,"Выбрать код",()=>{target.text=pending.ToString();CloseTexturePicker();});var ar=apply.GetComponent<RectTransform>();
            ar.anchorMin=new Vector2(0,0);ar.anchorMax=new Vector2(1,0);ar.pivot=new Vector2(.5f,0);ar.sizeDelta=new Vector2(-24,56);ar.anchoredPosition=new Vector2(0,8);
            var preview=Label(texturePicker,"",17);var pr=preview.rectTransform;pr.anchorMin=new Vector2(0,0);pr.anchorMax=new Vector2(1,0);pr.pivot=new Vector2(.5f,0);pr.sizeDelta=new Vector2(-24,42);pr.anchoredPosition=new Vector2(0,66);
            preview.resizeTextForBestFit=true;preview.resizeTextMinSize=12;preview.resizeTextMaxSize=17;
            var scroll=Rect("Generalized scroll",texturePicker);Stretch(scroll);scroll.offsetMin=new Vector2(12,112);scroll.offsetMax=new Vector2(-12,-68);scroll.gameObject.AddComponent<Image>().color=Background;scroll.gameObject.AddComponent<RectMask2D>();
            var sv=scroll.gameObject.AddComponent<ScrollRect>();sv.horizontal=false;sv.movementType=ScrollRect.MovementType.Clamped;sv.viewport=scroll;
            var body=Rect("Content",scroll);body.anchorMin=new Vector2(0,1);body.anchorMax=Vector2.one;body.pivot=new Vector2(.5f,1);body.sizeDelta=Vector2.zero;sv.content=body;
            var layout=body.gameObject.AddComponent<VerticalLayoutGroup>();layout.spacing=8;layout.childControlHeight=layout.childControlWidth=true;layout.childForceExpandHeight=false;
            body.gameObject.AddComponent<ContentSizeFitter>().verticalFit=ContentSizeFitter.FitMode.PreferredSize;
            RectTransform expanded=null;Action render=null;
            Action<Button,List<ActionOption>,Action<int>> expand=(button,choices,choose)=>
            {
                if(expanded!=null){expanded.gameObject.SetActive(false);Destroy(expanded.gameObject);}
                expanded=Rect("Options",body);expanded.SetSiblingIndex(button.transform.GetSiblingIndex()+1);
                var group=expanded.gameObject.AddComponent<VerticalLayoutGroup>();group.spacing=4;group.childControlHeight=group.childControlWidth=true;group.childForceExpandHeight=false;
                expanded.gameObject.AddComponent<ContentSizeFitter>().verticalFit=ContentSizeFitter.FitMode.PreferredSize;
                foreach(var item in choices){var entry=item;AddButton(expanded,entry.title,()=>{choose(entry.value);render();});}
            };
            render=()=>
            {
                float position=sv.verticalNormalizedPosition;
                for(int i=body.childCount-1;i>=0;i--){body.GetChild(i).gameObject.SetActive(false);Destroy(body.GetChild(i).gameObject);}expanded=null;
                if(!sector)
                {
                    var choices=new List<ActionOption>();foreach(var c in catalog.generalizedLines)choices.Add(new ActionOption{value=c.offset,title=c.title});
                    Button button=null;button=AddButton(body,"Тип: "+category.title+"…",()=>expand(button,choices,n=>{category=catalog.generalizedLines.Find(c=>c.offset==n);pending=category.offset;}));
                }
                else
                {
                    int basic=pending&~catalog.GeneralizedSectorMask;var choices=new List<ActionOption>();
                    foreach(var c in catalog.sectorChoices)if((c.id&catalog.GeneralizedSectorMask)==0)choices.Add(new ActionOption{value=c.id,title=c.id+" · "+c.title});
                    Button button=null;button=AddButton(body,"Основной эффект: "+basic+" · "+catalog.Find(basic,true).title+"…",()=>expand(button,choices,n=>pending=(pending&catalog.GeneralizedSectorMask)|n));
                }
                foreach(var item in sector?catalog.generalizedSectors:category.options)
                {
                    var option=item;Button button=null;int bits=sector?pending:pending-category.offset;
                    button=AddButton(body,option.title+": "+option.Describe(bits)+"…",()=>expand(button,option.choices,n=>pending=sector?option.Set(pending,n):category.Set(pending,option,n)));
                }
                preview.text="Код: "+pending+" · затем «Применить» в свойствах";
                Canvas.ForceUpdateCanvases();sv.verticalNormalizedPosition=position;
            };
            render();sv.verticalNormalizedPosition=1;
        }
    }
}
