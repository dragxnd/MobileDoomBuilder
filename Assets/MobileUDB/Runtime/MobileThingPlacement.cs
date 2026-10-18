using System;
using System.Collections.Generic;
using MobileUDB.Core;
using UnityEngine;

namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        bool ThingPlacementReady
        {
            get {if(!SelectionToolsReady||Kind==ElementKind.Thing)return false;foreach(var item in Selection)if(item.kind!=Kind)return false;return true;}
        }
        void ThingPlacementButton(RectTransform body)
        {
            LineBatchCaption(body,"СОЗДАНИЕ ОБЪЕКТОВ");var row=ShapeOptionRow(body);
            VisualThingCommand(row,"По выделению…",EditorIcon.Symbol.PlaceThings,ThingPlacementMenu).interactable=ThingPlacementReady;
        }
        void ThingPlacementMenu()
        {
            if(!ThingPlacementReady){Notify("Выделите вершины, стены или секторы и завершите построение.");return;}
            try
            {
                CancelDrag();var context=CaptureOpenContext();var seeds=new HashSet<ElementRef>(Selection);var kind=Kind;var catalog=thingCatalog;var actions=Actions;
                int count=SelectionThingPlacement.Positions(Map,seeds).Count;int pendingFlags=Map.format==MapFormat.Hexen?2023:7;
                ShowDialog("Объекты по выделению",body=>
                {
                    AddLabel(body,"Новых объектов: "+count+". "+(kind==ElementKind.Vertex?"В координатах вершин.":kind==ElementKind.Line?"На концах стен; общие координаты не повторяются.":"Один внутри каждого сектора; без привязки к сетке."));
                    var type=Field(body,"Тип объекта",placementType.ToString());
                    AddButton(body,"Выбрать тип по спрайту…",()=>ThingPicker(value=>type.text=value.ToString()));
                    var angle=Field(body,"Угол",placementAngle.ToString());AddAngleDial(body,angle);UnityEngine.UI.InputField height=null,tag=null,action=null;var args=new UnityEngine.UI.InputField[5];
                    if(Map.format==MapFormat.Hexen)
                    {
                        height=Field(body,"Высота","0");tag=Field(body,"ID / тег","0");AddButton(body,"Выбрать тег / свободный номер…",()=>TagPicker(tag,14,65535,null,true));
                        action=Field(body,"Действие","0");AddButton(body,"Выбрать действие по названию…",()=>ActionPicker(action,false));
                        for(int i=0;i<5;i++){int index=i;args[i]=Field(body,"Аргумент "+i,"0");AddButton(body,"Значение аргумента "+(i+1)+"…",()=>{int code;if(int.TryParse(action.text,out code)&&code>=0&&code<=255)ArgumentPicker(args[index],actions.Find(code).arguments[index]);});}
                    }
                    LineBatchCaption(body,"ФЛАГИ ОБЪЕКТОВ");var flags=BinaryFlagNames(false);var masks=new List<int>(flags.Keys);masks.Sort();
                    foreach(int value in masks){int mask=value;var row=ShapeOptionRow(body);ShapeToggle(row,flags[mask],(pendingFlags&mask)!=0,on=>pendingFlags=on?pendingFlags|mask:pendingFlags&~mask);}
                    AddLabel(body,"Создание — одна операция Undo. После создания объекты выделяются; дополнительные изменения доступны в «Свойства». Назад отменяет всё до создания.");
                    DialogApply(body,()=>
                    {
                        try
                        {
                        if(!CurrentOpenContext(context)||!Selection.SetEquals(seeds)||Kind!=kind||!ReferenceEquals(catalog,thingCatalog)||!ReferenceEquals(actions,Actions)){Notify("Карта, профиль или выделение изменились. Откройте размещение снова.");return false;}
                        var template=new MapThing{type=Number(type,0,65535),angle=Number(angle,0,65535),flags=pendingFlags};
                        if(Map.format==MapFormat.Hexen){template.z=Number(height,-32768,32767);template.tag=Number(tag,0,65535);template.action=Number(action,0,255);for(int i=0;i<5;i++)template.args[i]=Number(args[i],0,255);}
                        var plan=SelectionThingPlacement.Prepare(Map,seeds,template);
                        bool done=Edit(map=>{var inserted=plan.Apply(map);Selection.Clear();foreach(int index in inserted)Selection.Add(new ElementRef(ElementKind.Thing,index));Kind=ElementKind.Thing;},"Созданы объекты: "+plan.Count+". Один Undo отменяет размещение.");
                        if(done)placementType=template.type;return done;
                        }
                        catch(Exception error){Notify(error.Message);return false;}
                    },"Создать",true);
                });
            }
            catch(Exception error){Notify(error.Message);}
        }
    }
}
