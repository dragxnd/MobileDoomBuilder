using System;
using System.Collections.Generic;
using MobileUDB.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        bool SelectionToolsReady {get{return !VisualActive&&Draft.Count==0&&Tool!=MobileTool.Paste&&Selection.Count>0;}}
        void SelectionToolsButtons(RectTransform body)
        {
            LineBatchCaption(body,"ПОХОЖИЕ ЭЛЕМЕНТЫ");var row=ShapeOptionRow(body);
            VisualThingCommand(row,"Выбрать похожие…",EditorIcon.Symbol.Similar,SimilarSelectionMenu).interactable=SelectionToolsReady&&Kind!=ElementKind.Vertex;
            if(Kind==ElementKind.Thing)
            {
                LineBatchCaption(body,"ОСТАВИТЬ В ВЫДЕЛЕНИИ");row=ShapeOptionRow(body);
                VisualThingCommand(row,"По типам объектов…",EditorIcon.Symbol.FilterThings,FilterSelectedThingsMenu).interactable=SelectionToolsReady;
            }
            if(Kind!=ElementKind.Line)return;
            LineBatchCaption(body,"ОСТАВИТЬ В ВЫДЕЛЕНИИ");row=ShapeOptionRow(body);
            Button(row,"Односторонние",()=>FilterSelectedSides(false)).interactable=SelectionToolsReady;
            Button(row,"Двусторонние",()=>FilterSelectedSides(true)).interactable=SelectionToolsReady;
        }
        void FilterSelectedSides(bool doubleSided)
        {
            if(!SelectionToolsReady||Kind!=ElementKind.Line){Notify("Сначала выделите стены.");return;}
            try
            {
                var result=SimilarSelection.FilterSides(Map,Selection,doubleSided);if(!StartSelectionCommand())return;
                Selection.Clear();Selection.UnionWith(result);Changed(false);Notify("Оставлены "+(doubleSided?"двусторонние":"односторонние")+" стены: "+result.Count+".");
            }
            catch(Exception error){Notify(error.Message);}
        }
        void SimilarSelectionMenu()
        {
            if(!SelectionToolsReady||Kind==ElementKind.Vertex){Notify("Выделите стены, секторы или объекты одного режима.");return;}
            var context=CaptureOpenContext();var kind=Kind;var seeds=new HashSet<ElementRef>(Selection);var available=SimilarSelection.Available(Map.format,kind);
            string key="MobileUDB.Selection.Similar."+Map.format+"."+kind;
            var pending=(PropertyFields)PlayerPrefs.GetInt(key,(int)available)&available;
            ShowDialog("Выбрать похожие",body=>
            {
                var help=Label(body,"Сравнить с любым из выделенных образцов. Выбор расширится; карта не меняется. Отметьте только нужные свойства.",16);help.gameObject.AddComponent<LayoutElement>().preferredHeight=76;
                var updates=new List<Action>();var allRow=ShapeOptionRow(body);
                Button(allRow,"Все свойства",()=>{pending=available;foreach(var update in updates)update();});Button(allRow,"Снять все",()=>{pending=PropertyFields.None;foreach(var update in updates)update();});
                RectTransform row=null;int column=0;
                Action<string> group=title=>{LineBatchCaption(body,title);column=0;};
                Action<PropertyFields,string> option=(field,title)=>
                {
                    if((available&field)==0)return;if(column++%2==0)row=ShapeOptionRow(body);Button button=null;
                    Action update=()=>{button.GetComponentInChildren<Text>().text=((pending&field)!=0?"✓ ":"○ ")+title;button.GetComponent<Image>().color=(pending&field)!=0?Accent:Panel;};
                    button=Button(row,title,()=>{pending^=field;update();});button.name="Сравнить: "+title;button.GetComponentInChildren<Text>().fontSize=16;updates.Add(update);update();
                };
                if(kind==ElementKind.Line)
                {
                    group("СТЕНА");option(PropertyFields.LineAction,"Действие");option(PropertyFields.LineTag,"Тег");option(PropertyFields.LineFlags,"Флаги");option(PropertyFields.Activation,"Активация");option(PropertyFields.LineArguments,"Аргументы");
                    group("ЛЮБАЯ СТОРОНА");option(PropertyFields.Upper,"Верхняя текстура");option(PropertyFields.Middle,"Средняя текстура");option(PropertyFields.Lower,"Нижняя текстура");option(PropertyFields.OffsetX,"Смещение X");option(PropertyFields.OffsetY,"Смещение Y");
                    var note=Label(body,"Достаточно совпадения одной стороны, в любом направлении. Стены без сторон не добавляются.",16);note.gameObject.AddComponent<LayoutElement>().preferredHeight=64;
                }
                else if(kind==ElementKind.Sector)
                {
                    group("СЕКТОР");option(PropertyFields.FloorHeight,"Высота пола");option(PropertyFields.CeilingHeight,"Высота потолка");option(PropertyFields.FloorTexture,"Текстура пола");option(PropertyFields.CeilingTexture,"Текстура потолка");option(PropertyFields.Light,"Свет");option(PropertyFields.SectorEffect,"Эффект");option(PropertyFields.SectorTag,"Тег");
                }
                else
                {
                    group("ОБЪЕКТ");option(PropertyFields.ThingType,"Тип");option(PropertyFields.ThingAngle,"Угол");option(PropertyFields.ThingFlags,"Флаги");option(PropertyFields.ThingHeight,"Высота Z");option(PropertyFields.ThingTag,"Тег");option(PropertyFields.ThingAction,"Действие");option(PropertyFields.ThingArguments,"Аргументы");
                }
                DialogApply(body,()=>
                {
                    try
                    {
                        if(!CurrentOpenContext(context)||!SelectionToolsReady||Kind!=kind||!Selection.SetEquals(seeds))throw new InvalidOperationException("Карта или выделение изменились. Откройте выбор снова.");
                        int flagsMask=0;if(kind!=ElementKind.Sector)foreach(int bit in BinaryFlagNames(kind==ElementKind.Line).Keys)flagsMask|=bit;
                        var result=SimilarSelection.Expand(Map,kind,seeds,pending,Actions,flagsMask);int added=result.Count-seeds.Count;
                        CancelDrag();Selection.UnionWith(result);Tool=MobileTool.Select;Changed(false);
                        PlayerPrefs.SetInt(key,(int)pending);PlayerPrefs.Save();Notify("Добавлено похожих: "+added+". Всего выбрано: "+result.Count+".");return true;
                    }
                    catch(Exception error){Notify(error.Message);return false;}
                },"Выделить",true);
            });
        }
    }
}
