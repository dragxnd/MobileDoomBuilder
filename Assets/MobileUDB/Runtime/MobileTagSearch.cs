// GPL-3.0. Native binary tag/TID Find and Replace, with explicit touch controls.
using System;using System.Collections.Generic;using MobileUDB.Core;using UnityEngine;using UnityEngine.UI;
namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        ElementKind tagSearchKind=ElementKind.Sector;readonly int[] tagSearchNumbers=new int[4];bool tagSearchSelection;
        static string TagSearchTitle(ElementKind kind)=>kind==ElementKind.Line?"Теги стен":kind==ElementKind.Sector?"Теги секторов":"TID объектов";
        static int TagSearchType(ElementKind kind)=>kind==ElementKind.Line?15:kind==ElementKind.Sector?13:14;
        bool TagSearchReady=>Draft.Count==0&&Tool!=MobileTool.Paste;
        bool CurrentTagSearch(OpenContext context)=>TagSearchReady&&CurrentOpenContext(context);
        void TagSearchMenu()
        {
            if(!TagSearchReady){Notify("Сначала завершите или отмените построение.");return;}
            CancelDrag();if(!MapTagSearch.Supported(Map.format,tagSearchKind))tagSearchKind=ElementKind.Sector;var context=CaptureOpenContext();
            ShowDialog("Поиск тегов / TID",body=>
            {
                ToolsCaption(body,"ВИД ЭЛЕМЕНТОВ");var families=ShapeOptionRow(body);
                foreach(var value in new[]{ElementKind.Line,ElementKind.Sector,ElementKind.Thing})
                {
                    var kind=value;if(!MapTagSearch.Supported(Map.format,kind))continue;
                    var button=ToolsCommand(families,TagSearchTitle(kind),TagSearchTitle(kind),kind==ElementKind.Line?EditorIcon.Symbol.Wall:kind==ElementKind.Sector?EditorIcon.Symbol.Sector:EditorIcon.Symbol.Thing,()=>{tagSearchKind=kind;TagSearchMenu();});
                    if(kind==tagSearchKind)button.GetComponent<Image>().color=Accent;
                }
                var input=Field(body,tagSearchKind==ElementKind.Thing?"TID":"Тег",tagSearchNumbers[(int)tagSearchKind].ToString());
                AddButton(body,"Выбрать тег / TID…",()=>TagPicker(input,TagSearchType(tagSearchKind),ushort.MaxValue,null,true));
                var label=Label(body,"",16);label.gameObject.AddComponent<LayoutElement>().preferredHeight=38;input.onValueChanged.AddListener(_=>{int number;label.text=int.TryParse(input.text,out number)?TagLabelDisplay(MapTagLabels.Find(Map,number)??""):"";});int initial=tagSearchNumbers[(int)tagSearchKind];label.text=TagLabelDisplay(MapTagLabels.Find(Map,initial)??"");
                Button scope=null;Action scopeText=()=>scope.GetComponentInChildren<Text>().text="Область: "+(tagSearchSelection?"выделение":"вся карта");scope=AddButton(body,"",()=>{tagSearchSelection=!tagSearchSelection;scopeText();});scopeText();
                AddLabel(body,"Точное совпадение номера, включая 0. В области «выделение» проверяются только элементы выбранного вида. Нажатие на результат покажет его на карте.");
                if(Map.format==MapFormat.Hexen)AddLabel(body,"В Hexen здесь доступны теги секторов и TID объектов. Стены хранят ссылки / ID в аргументах действий: используйте «Объекты / действия → Стены по действию».");
                var error=Label(body,"",16);error.color=new Color(1,.65f,.55f);error.gameObject.AddComponent<LayoutElement>().preferredHeight=48;error.gameObject.SetActive(false);error.transform.SetAsFirstSibling();
                var scroll=(RectTransform)body.parent;scroll.offsetMin=new Vector2(scroll.offsetMin.x,80);
                var find=Button(scroll.parent,"Найти",()=>
                {
                    try
                    {if(!CurrentTagSearch(context))throw new InvalidOperationException("Карта изменилась. Откройте поиск снова.");int tag=Number(input,0,ushort.MaxValue);tagSearchNumbers[(int)tagSearchKind]=tag;var selected=tagSearchSelection?new HashSet<ElementRef>(Selection):null;var found=MapTagSearch.Find(Map,tagSearchKind,tag,selected);TagSearchResults(tagSearchKind,tag,found,0,selected!=null,context);}
                    catch(Exception exception){error.text=exception.Message;error.gameObject.SetActive(true);Canvas.ForceUpdateCanvases();scroll.GetComponent<ScrollRect>().verticalNormalizedPosition=1;}
                });
                var rect=find.GetComponent<RectTransform>();rect.anchorMin=Vector2.zero;rect.anchorMax=new Vector2(1,0);rect.pivot=new Vector2(.5f,0);rect.sizeDelta=new Vector2(-24,60);rect.anchoredPosition=new Vector2(0,8);
            });
        }
        void TagSearchResults(ElementKind kind,int tag,List<ElementRef> found,int page,bool selected,OpenContext context)
        {
            const int pageSize=6;int pages=Math.Max(1,(found.Count+pageSize-1)/pageSize);page=Math.Max(0,Math.Min(pages-1,page));
            Action<Action> current=action=>{if(!CurrentTagSearch(context)){Notify("Карта изменилась. Повторите поиск.");return;}action();};
            ShowDialog(TagSearchTitle(kind),body=>
            {
                string name=MapTagLabels.Find(Map,tag);AddLabel(body,(kind==ElementKind.Thing?"TID ":"Тег ")+tag+(string.IsNullOrEmpty(name)?"":" · "+TagLabelDisplay(name))+" · найдено: "+found.Count+"\n"+(selected?"Выделение":"Вся карта")+" · страница "+(page+1)+" / "+pages);
                AddButton(body,"Выделить все найденные",()=>current(()=>FocusSearchElements(found))).interactable=found.Count>0;
                AddButton(body,"Заменить найденные…",()=>current(()=>TagReplaceMenu(kind,tag,found,context))).interactable=found.Count>0;
                for(int i=page*pageSize;i<Math.Min(found.Count,(page+1)*pageSize);i++)
                {var hit=found[i];AddButton(body,(kind==ElementKind.Line?"Стена":kind==ElementKind.Sector?"Сектор":"Объект")+" #"+hit.index,()=>current(()=>FocusSearchElements(new List<ElementRef>{hit})));}
                if(pages>1){var row=ShapeOptionRow(body);AddButton(row,"←",()=>current(()=>TagSearchResults(kind,tag,found,page-1,selected,context))).interactable=page>0;AddButton(row,"→",()=>current(()=>TagSearchResults(kind,tag,found,page+1,selected,context))).interactable=page+1<pages;}
                AddButton(body,"Изменить поиск",TagSearchMenu);AddLabel(body,"Замена меняет только теги / TID найденных элементов. Ссылки в действиях и скриптах, подписи тегов и элементы других видов сохраняются; проверьте связанные номера отдельно.");
            });
        }
        void TagReplaceMenu(ElementKind kind,int tag,List<ElementRef> found,OpenContext context)
        {
            var reviewed=new HashSet<ElementRef>(found);int maximum=MapTagSearch.MaximumReplacement(kind);
            ShowDialog("Замена · "+TagSearchTitle(kind),body=>
            {
                AddLabel(body,"Было: "+tag+" · найдено для замены: "+reviewed.Count);var input=Field(body,"Новый номер",tag.ToString());
                AddButton(body,"Выбрать тег / TID…",()=>TagPicker(input,TagSearchType(kind),maximum,null,true));
                AddLabel(body,"Меняется только номер в найденных элементах. Их параметры, геометрия, ссылки в действиях / скриптах и подписи тегов сохраняются. Один Undo отменяет всю замену.");
                if(kind==ElementKind.Thing)AddLabel(body,"Как в поиске UDB: замена TID ограничена 0–32767. Поиск принимает также исходные TID 32768–65535.");
                Button commit=null;commit=DialogApply(body,()=>
                {
                    if(!CurrentTagSearch(context)){Notify("Карта изменилась. Повторите поиск.");return false;}
                    int changed=0;bool success=Edit(map=>{int replacement=Number(input,0,maximum);changed=MapTagSearch.Replace(map,kind,tag,replacement,reviewed);if(changed==0)throw new InvalidOperationException("Нет изменений: номер совпадает или совпадений больше нет.");},"Теги / TID заменены. Доступно одно Undo.");
                    if(!success)context=CaptureOpenContext(); // Edit restored an identical cloned map.
                    if(success){commit.interactable=false;body.gameObject.AddComponent<CanvasGroup>().interactable=false;if(VisualActive)visualView.ForgetSelection();Notify("Заменено элементов: "+changed+". Доступна отмена.");}return success;
                },"Заменить ("+reviewed.Count+")");
                input.onValueChanged.AddListener(_=>{if(!commit.interactable)return;commit.transform.parent.Find("Apply feedback").gameObject.SetActive(false);var scroll=(RectTransform)body.parent;scroll.offsetMin=new Vector2(scroll.offsetMin.x,80);});
            });
        }
    }
}
