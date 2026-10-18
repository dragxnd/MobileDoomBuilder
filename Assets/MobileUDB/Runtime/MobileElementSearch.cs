using System;
using System.Collections.Generic;
using MobileUDB.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        readonly int[] elementSearchCodes={3004,0,0,0};
        bool elementSearchSelection;
        InputField ElementCodeControl(RectTransform body,string title,int value,ElementSearchKind search)
        {
            var input=Field(body,title,value.ToString());
            var description=Label(body,"",17);description.gameObject.AddComponent<LayoutElement>().preferredHeight=52;
            description.resizeTextForBestFit=true;description.resizeTextMinSize=12;description.resizeTextMaxSize=17;
            Action describe=()=>{int code;description.text=int.TryParse(input.text,out code)?(search==ElementSearchKind.ThingType?thingCatalog.Find(code).title:Actions.Find(code,search==ElementSearchKind.SectorEffect).title):"Введите целый код.";};
            input.onValueChanged.AddListener(_=>describe());describe();
            if(search==ElementSearchKind.ThingType)AddButton(body,"Выбрать тип по спрайту…",()=>ThingPicker(code=>input.text=code.ToString()));
            else
            {
                AddButton(body,"Выбрать по названию…",()=>ActionPicker(input,search==ElementSearchKind.SectorEffect));
                if(search==ElementSearchKind.SectorEffect?Actions.generalizedSectors.Count>0:Actions.generalizedLines.Count>0)
                    AddButton(body,"Настроить обобщённый код…",()=>GeneralizedPicker(input,search==ElementSearchKind.SectorEffect));
            }
            return input;
        }
        static string ElementSearchTitle(ElementSearchKind search)
        {
            switch(search)
            {
                case ElementSearchKind.ThingType:return "Объекты по типу";
                case ElementSearchKind.LineAction:return "Стены по действию";
                case ElementSearchKind.SectorEffect:return "Секторы по эффекту";
                default:return "Объекты по действию";
            }
        }
        void ElementSearchMenu()
        {
            if(Draft.Count>0||Tool==MobileTool.Paste){Notify("Сначала завершите или отмените построение.");return;}
            ShowDialog("Поиск элементов",body=>
            {
                foreach(ElementSearchKind item in Enum.GetValues(typeof(ElementSearchKind)))
                {
                    var search=item;if(search==ElementSearchKind.ThingAction&&Map.format==MapFormat.Doom)continue;
                    AddButton(body,ElementSearchTitle(search),()=>ElementSearchForm(search));
                }
                AddLabel(body,"Поиск в текущей карте. Можно выбрать код по названию или ввести вручную, затем перейти к результату или выделить все совпадения.");
                AddButton(body,"Теги / TID…",TagSearchMenu);
            });
        }
        void ElementSearchForm(ElementSearchKind search)
        {
            var context=CaptureOpenContext();var actions=Actions;
            ShowDialog(ElementSearchTitle(search),body=>
            {
                var input=ElementCodeControl(body,"Код",elementSearchCodes[(int)search],search);
                SearchMatchingControl(body,input,search);
                Button scopeButton=null;Action scopeText=()=>scopeButton.GetComponentInChildren<Text>().text="Область: "+(elementSearchSelection?"выделение":"вся карта");
                scopeButton=AddButton(body,"",()=>{elementSearchSelection=!elementSearchSelection;scopeText();});scopeText();
                int[] remembered;elementSearchArguments.TryGetValue(search,out remembered);var args=SearchArgumentControls(body,input,search,remembered,false);
                var error=Label(body,"",16);error.color=new Color(1,.65f,.55f);error.gameObject.SetActive(false);error.gameObject.AddComponent<LayoutElement>().preferredHeight=44;
                error.transform.SetAsFirstSibling();
                var scroll=(RectTransform)body.parent;scroll.offsetMin=new Vector2(scroll.offsetMin.x,80);
                var findButton=Button(scroll.parent,"Найти",()=>
                {
                    try
                    {
                        if(!CurrentSearchContext(context,actions))throw new InvalidOperationException("Карта или профиль изменились. Откройте поиск снова.");
                        int min=search==ElementSearchKind.ThingType?0:-1;
                        int max=MapElementSearch.Maximum(Map.format,search);
                        int code=Number(input,min,max);elementSearchCodes[(int)search]=code;
                        var query=new ElementSearchQuery{kind=search,code=code,nativeGeneralized=elementSearchGeneralized,arguments=ReadSearchArguments(args)}.Validated(Map);elementSearchArguments[search]=query.arguments;
                        var scope=elementSearchSelection?new HashSet<ElementRef>(Selection):null;
                        ElementSearchResults(query,MapActionSearch.Find(Map,query,actions,scope),0,scope!=null,context,actions);
                    }
                    catch(Exception exception){error.text=exception.Message;error.gameObject.SetActive(true);Canvas.ForceUpdateCanvases();scroll.GetComponent<ScrollRect>().verticalNormalizedPosition=1;}
                });
                var findRect=findButton.GetComponent<RectTransform>();findRect.anchorMin=Vector2.zero;findRect.anchorMax=new Vector2(1,0);findRect.pivot=new Vector2(.5f,0);
                findRect.sizeDelta=new Vector2(-24,60);findRect.anchoredPosition=new Vector2(0,8);
                AddLabel(body,"0 и неизвестные коды можно искать точно. «Любой ненулевой код» исключает 0; пустой аргумент допускает любое значение. В области «выделение» проверяются только элементы выбранного вида.");
            });
        }
        void ElementSearchResults(ElementSearchKind search,int code,List<ElementRef> found,int page,bool scoped)
        {ElementSearchResults(new ElementSearchQuery{kind=search,code=code},found,page,scoped,CaptureOpenContext(),Actions);}
        void ElementSearchResults(ElementSearchQuery query,List<ElementRef> found,int page,bool scoped,OpenContext context,ActionCatalog actions)
        {
            var search=query.kind;int code=query.code;Action<Action> current=action=>{if(!CurrentSearchContext(context,actions)){Notify("Карта или профиль изменились. Повторите поиск.");return;}action();};
            const int pageSize=6;int pages=Math.Max(1,(found.Count+pageSize-1)/pageSize);page=Math.Max(0,Math.Min(page,pages-1));
            ShowDialog(ElementSearchTitle(search),body=>
            {
                AddLabel(body,query.Caption()+" · найдено: "+found.Count+" · "+(scoped?"выделение":"вся карта")+"\nСтраница "+(page+1)+" / "+pages);
                AddButton(body,"Выделить все найденные",()=>current(()=>FocusSearchElements(found))).interactable=found.Count>0;
                AddButton(body,"Заменить найденные…",()=>current(()=>ElementReplaceForm(query,found,context,actions))).interactable=found.Count>0;
                for(int i=page*pageSize;i<Math.Min(found.Count,(page+1)*pageSize);i++)
                {
                    var hit=found[i];string title=(hit.kind==ElementKind.Line?"Стена":hit.kind==ElementKind.Sector?"Сектор":"Объект")+" #"+hit.index;
                    if(hit.kind==ElementKind.Thing)title+=" · "+thingCatalog.Find(Map.things[hit.index].type).title;
                    var b=AddButton(body,title,()=>current(()=>FocusSearchElements(new List<ElementRef>{hit})));
                    var text=b.GetComponentInChildren<Text>();text.resizeTextForBestFit=true;text.resizeTextMinSize=14;text.resizeTextMaxSize=20;
                }
                if(pages>1)
                {
                    var row=Rect("Element search pages",body);row.gameObject.AddComponent<LayoutElement>().preferredHeight=60;
                    var layout=row.gameObject.AddComponent<HorizontalLayoutGroup>();layout.spacing=8;layout.childControlWidth=layout.childControlHeight=true;layout.childForceExpandWidth=true;
                    AddButton(row,"←",()=>current(()=>ElementSearchResults(query,found,page-1,scoped,context,actions))).interactable=page>0;
                    AddButton(row,"→",()=>current(()=>ElementSearchResults(query,found,page+1,scoped,context,actions))).interactable=page+1<pages;
                }
                AddButton(body,"Изменить поиск",()=>ElementSearchForm(search));
                AddLabel(body,"Нажмите результат для перехода к элементу. Замена доступна отдельной кнопкой и охватывает все страницы.");
            });
        }
        void ElementReplaceForm(ElementSearchKind search,int code,List<ElementRef> found)
        {ElementReplaceForm(new ElementSearchQuery{kind=search,code=code},found,CaptureOpenContext(),Actions);}
        void ElementReplaceForm(ElementSearchQuery query,List<ElementRef> found,OpenContext context,ActionCatalog actions)
        {
            var search=query.kind;int code=query.code;
            var scope=new HashSet<ElementRef>(found);
            ShowDialog("Замена · "+ElementSearchTitle(search),body=>
            {
                string oldTitle=search==ElementSearchKind.ThingType?thingCatalog.Find(code).title:Actions.Find(code,search==ElementSearchKind.SectorEffect).title;
                AddLabel(body,"Поиск: "+query.Caption()+" · "+oldTitle+"\nНайдено для замены: "+scope.Count);
                var input=ElementCodeControl(body,"Новый код",code<0?0:code,search);
                var args=SearchArgumentControls(body,input,search,null,true);
                AddLabel(body,"Меняется код и только явно указанные аргументы. Пустые аргументы сохраняют разные исходные значения. Остальные параметры сохраняются. Один Undo отменяет всю замену.");
                Button commit=null;commit=DialogApply(body,()=>
                {
                    int changed=0;
                    if(!CurrentSearchContext(context,actions)){Notify("Карта или профиль изменились. Повторите поиск.");return false;}
                    bool success=Edit(m=>
                    {
                        int replacement=Number(input,MapFormats.IsUniversal(m.format)?int.MinValue:0,MapElementSearch.Maximum(m.format,search));
                        changed=MapActionSearch.Replace(m,query,new ElementSearchQuery{kind=search,code=replacement,arguments=ReadSearchArguments(args)},actions,scope);
                        if(changed==0)throw new InvalidOperationException("Нет изменений: код совпадает или совпадений больше нет.");
                    },"Замена выполнена. Один Undo отменяет её целиком.");
                    if(!success)context=CaptureOpenContext(); // Allow correction after transactional rollback.
                    if(success){commit.interactable=false;body.gameObject.AddComponent<CanvasGroup>().interactable=false;if(VisualActive)visualView.ForgetSelection();Notify("Заменено элементов: "+changed+". Доступна отмена.");}
                    return success;
                },"Заменить ("+scope.Count+")");
                Action clearFeedback=()=>
                {
                    if(!commit.interactable)return;
                    commit.transform.parent.Find("Apply feedback").gameObject.SetActive(false);
                    var scroll=(RectTransform)body.parent;scroll.offsetMin=new Vector2(scroll.offsetMin.x,80);
                };input.onValueChanged.AddListener(_=>clearFeedback());if(args!=null)foreach(var field in args)field.onValueChanged.AddListener(_=>clearFeedback());
            });
        }
        void FocusSearchElements(List<ElementRef> found)
        {
            if(found.Count==0)return;
            CloseDialog();SetTool(MobileTool.Select);SetKind(found[0].kind);Selection.Clear();foreach(var hit in found)Selection.Add(hit);
            var min=new Vector2(float.MaxValue,float.MaxValue);var max=new Vector2(float.MinValue,float.MinValue);bool positioned=false;
            Action<float,float> include=(x,y)=>{var p=new Vector2(x,y);min=Vector2.Min(min,p);max=Vector2.Max(max,p);positioned=true;};
            if(found[0].kind==ElementKind.Thing)foreach(var hit in found){var t=Map.things[hit.index];include(t.x,t.y);}
            else
            {
                Action<MapLine> includeLine=line=>{var a=Map.vertices[line.start];var b=Map.vertices[line.end];include(a.x,a.y);include(b.x,b.y);};
                if(found[0].kind==ElementKind.Line)foreach(var hit in found)includeLine(Map.lines[hit.index]);
                else
                {
                    var sectors=new HashSet<int>();foreach(var hit in found)sectors.Add(hit.index);
                    foreach(var line in Map.lines)
                        if((line.front>=0&&sectors.Contains(Map.sides[line.front].sector))||(line.back>=0&&sectors.Contains(Map.sides[line.back].sector)))includeLine(line);
                }
            }
            if(positioned)
            {
                View.center=(min+max)*.5f;View.zoom=Mathf.Clamp(Mathf.Min(View.rectTransform.rect.width/Mathf.Max(128,max.x-min.x+128),View.rectTransform.rect.height/Mathf.Max(128,max.y-min.y+128)),.025f,3);
            }
            View.SetVerticesDirty();RefreshLabels();Notify("Найдено и выделено: "+found.Count+". Свойства доступны кнопкой.");
        }
    }
}
