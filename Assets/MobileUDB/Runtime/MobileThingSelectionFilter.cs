using System;
using System.Collections.Generic;
using MobileUDB.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        void FilterSelectedThingsMenu()
        {
            if(!SelectionToolsReady||Kind!=ElementKind.Thing){Notify("Сначала выделите объекты.");return;}
            var seeds=new HashSet<ElementRef>(Selection);SortedDictionary<int,int> counts;
            try{counts=SimilarSelection.SelectedThingTypes(Map,seeds);}catch(Exception error){Notify(error.Message);return;}
            var context=CaptureOpenContext();var catalog=thingCatalog;
            // Start with every type checked so opening/Apply preserves the user's
            // selection until they explicitly narrow it. Pending choices are local.
            var chosen=new HashSet<int>(counts.Keys);var types=new List<int>(counts.Keys);
            ShowDialog("Оставить объекты по типам",body=>
            {
                var note=Label(body,"Оставьте отметки у нужных типов. Другие объекты снимутся с выделения. Карта не меняется.",16);
                note.gameObject.AddComponent<LayoutElement>().preferredHeight=68;
                LineBatchCaption(body,"ТИПЫ В ВЫДЕЛЕНИИ");
                string query="";int page=0;const int pageSize=12;Action render=null;
                InputField search=null;if(types.Count>8){search=Field(body,"Тип / название","",false);search.characterLimit=80;}
                var summary=Label(body,"",16);summary.gameObject.AddComponent<LayoutElement>().preferredHeight=44;
                var actions=ShapeOptionRow(body);
                Button(actions,"Все типы",()=>{chosen.UnionWith(types);render();});Button(actions,"Снять отметки",()=>{chosen.Clear();render();});
                Button previous=null,next=null;Text pages=null;
                if(types.Count>pageSize)
                {
                    var navigation=ShapeOptionRow(body);previous=Button(navigation,"← Назад",()=>{page--;render();});next=Button(navigation,"Далее →",()=>{page++;render();});
                    pages=Label(body,"",16);pages.gameObject.AddComponent<LayoutElement>().preferredHeight=30;
                }
                var items=Rect("Thing filter types",body);var layout=items.gameObject.AddComponent<VerticalLayoutGroup>();
                layout.spacing=6;layout.childControlHeight=layout.childControlWidth=true;layout.childForceExpandHeight=false;
                items.gameObject.AddComponent<ContentSizeFitter>().verticalFit=ContentSizeFitter.FitMode.PreferredSize;
                render=()=>
                {
                    int kept=0;foreach(int type in chosen)kept+=counts[type];
                    summary.text=chosen.Count==0?"Нет отметок — выделение сохранится.":"Останется: "+kept+" из "+seeds.Count+" · типов: "+chosen.Count;
                    var filtered=new List<int>();foreach(int type in types)
                    {if(query.Length==0||type.ToString().IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0||catalog.Find(type).title.IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0)filtered.Add(type);}
                    int total=Math.Max(1,(filtered.Count+pageSize-1)/pageSize);page=Math.Max(0,Math.Min(page,total-1));
                    if(previous!=null){previous.interactable=page>0;next.interactable=page+1<total;pages.text=(page+1)+" / "+total+" · найдено типов: "+filtered.Count;}
                    for(int i=items.childCount-1;i>=0;i--){items.GetChild(i).gameObject.SetActive(false);Destroy(items.GetChild(i).gameObject);}
                    if(filtered.Count==0){var empty=Label(items,"Типы не найдены. Отметки вне поиска сохранены.",16);empty.gameObject.AddComponent<LayoutElement>().preferredHeight=60;}
                    for(int i=page*pageSize;i<Math.Min(filtered.Count,(page+1)*pageSize);i++)
                    {
                        int type=filtered[i];Button button=null;Action update=()=>
                        {bool active=chosen.Contains(type);button.GetComponentInChildren<Text>().text=(active?"✓ ":"○ ")+type+" · "+catalog.Find(type).title+"\n"+counts[type]+" в выделении";button.GetComponent<Image>().color=active?Accent:Panel;};
                        button=AddButton(items,"",()=>{if(!chosen.Remove(type))chosen.Add(type);update();int count=0;foreach(int id in chosen)count+=counts[id];summary.text=chosen.Count==0?"Нет отметок — выделение сохранится.":"Останется: "+count+" из "+seeds.Count+" · типов: "+chosen.Count;});
                        button.name="Filter thing type · "+type;var label=button.GetComponentInChildren<Text>();label.resizeTextForBestFit=true;label.resizeTextMinSize=12;label.resizeTextMaxSize=18;label.supportRichText=false;update();
                    }
                };
                if(search!=null)search.onValueChanged.AddListener(value=>{query=value.Trim();page=0;render();});render();
                DialogApply(body,()=>
                {
                    try
                    {
                        if(!CurrentOpenContext(context)||thingCatalog!=catalog||!SelectionToolsReady||Kind!=ElementKind.Thing||!Selection.SetEquals(seeds))throw new InvalidOperationException("Карта или выделение изменились. Откройте фильтр снова.");
                        var result=SimilarSelection.FilterThingTypes(Map,seeds,chosen);
                        CancelDrag();Selection.Clear();Selection.UnionWith(result);Tool=MobileTool.Select;Changed(false);
                        Notify(chosen.Count==0?"Выделение сохранено: "+result.Count+".":"Оставлены объекты: "+result.Count+" из "+seeds.Count+".");return true;
                    }
                    catch(Exception error){Notify(error.Message);return false;}
                },"Оставить выбранные",true);
            });
        }
    }
}
