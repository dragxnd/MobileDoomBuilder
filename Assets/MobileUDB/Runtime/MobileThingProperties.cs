using System;
using System.Collections.Generic;
using System.Globalization;
using MobileUDB.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        void PolyobjectNumberHint(Transform body)
        {var hint=Label(body,"Номер маркера хранится в поле угла. Для связи с аргументами стен Hexen используйте номера 0–255.",14);hint.alignment=TextAnchor.UpperLeft;hint.gameObject.AddComponent<LayoutElement>().minHeight=48;}
        void ThingBatchProperties()
        {
            var indices=new List<int>();foreach(var item in Selection)indices.Add(item.index);indices.Sort();
            ShowDialog("Объекты · "+indices.Count,body=>
            {
                var note=Label(body,"Пусто — не менять. Кнопка у числа выбирает = / + / − / × / ÷.",16);note.alignment=TextAnchor.MiddleLeft;note.gameObject.AddComponent<LayoutElement>().minHeight=48;
                bool universal=MapFormats.IsUniversal(Map.format);int min=universal?int.MinValue:0,max=universal?int.MaxValue:65535;
                var changes=new List<Func<MapDocument,bool>>();
                var type=BatchField(body,"Тип объекта",indices,m=>m.things,t=>t.type,n=>n.ToString(),field=>Number(field,min,max),(t,n)=>t.type=n,changes);
                AddButton(body,"Выбрать тип по спрайту…",()=>ThingPicker(n=>type.text=n.ToString()));
                Func<string,Func<MapThing,float>,Action<MapThing,float>,float,float,InputField> real=(title,get,set,low,high)=>
                {
                    return BatchReal(body,title,indices,(m,index)=>get(m.things[index]),(m,index,n)=>set(m.things[index],n),low,high,changes);
                };
                bool poly=false,ordinary=false;foreach(int index in indices){if(Map.format==MapFormat.Hexen&&PolyobjectChecks.IsMarker(thingCatalog.Find(Map.things[index].type)))poly=true;else ordinary=true;}
                var number=real(poly?(ordinary?"Угол / номер полиобъекта":"Номер полиобъекта"):"Угол",t=>t.angle,(t,n)=>t.angle=n,universal?-36000:0,universal?36000:65535);
                if(poly){AddButton(body,"Выбрать номер полиобъекта…",()=>PolyobjectNumberPicker(number,65535));PolyobjectNumberHint(body);}
                real("X",t=>t.x,(t,n)=>t.x=n,-32768,32767);real("Y",t=>t.y,(t,n)=>t.y=n,-32768,32767);
                if(Map.format!=MapFormat.Doom)
                {
                    real("Высота",t=>t.z,(t,n)=>t.z=n,-32768,32767);
                    var tag=BatchField(body,"ID / тег",indices,m=>m.things,t=>t.tag,n=>n.ToString(),field=>Number(field,min,max),(t,n)=>t.tag=n,changes);
                    AddButton(body,"Выбрать тег / свободный номер…",()=>TagPicker(tag,14,max,null,true));
                    int argMax=universal?int.MaxValue:255;
                    var action=BatchField(body,"Действие",indices,m=>m.things,t=>t.action,n=>n.ToString(),field=>Number(field,min,argMax),(t,n)=>t.action=n,changes);
                    AddButton(body,"Выбрать действие по названию…",()=>ActionPicker(action,false));
                    var args=new InputField[5];var labels=new Text[5];var buttons=new Button[5];var strings=new InputField[5];var stringLabels=new Text[5];Action refresh=null;
                    for(int i=0;i<5;i++)
                    {
                        int index=i;
                        args[i]=BatchField(body,"Аргумент "+i,indices,m=>m.things,t=>t.args[index],n=>n.ToString(),field=>Number(field,min,argMax),(t,n)=>t.args[index]=n,changes);
                        labels[i]=args[i].transform.parent.GetComponentInChildren<Text>();labels[i].resizeTextForBestFit=true;labels[i].resizeTextMinSize=11;labels[i].resizeTextMaxSize=18;
                        buttons[i]=AddButton(body,"Выбрать значение…",()=>{int code;if(int.TryParse(action.text,out code))ArgumentPicker(args[index],Actions.Find(code).arguments[index],code,index,args[1],action);});
                        args[i].onValueChanged.AddListener(_=>{if(refresh!=null)refresh();});
                        
#if MOBILEUDB_UDMF // Deferred from the first release.
if(universal)
                        {
                            string key="arg"+i+"str";
                            strings[i]=BatchUniversalRow(body,"Строковый аргумент "+i,indices,()=>key,(m,id)=>m.things[id],(m,id)=>m.things[id],_=>{},changes,true);
                            strings[i].characterLimit=512;stringLabels[i]=strings[i].transform.parent.GetComponentInChildren<Text>();
                        }
#endif

                    }
                    refresh=()=>
                    {
                        int code;bool known=int.TryParse(action.text,out code);var info=known?Actions.Find(code):null;
                        for(int i=0;i<5;i++)
                        {
                            var arg=known?info.arguments[i]:null;
                            labels[i].text=(i+1)+" · "+(arg!=null&&arg.used?arg.title:"Аргумент "+i);
                            bool multiple=arg!=null&&(arg.type==12||arg.flags.Count>0);
                            buttons[i].interactable=arg!=null&&(IsTagArgument(arg)||IsPolyobjectArgument(arg)||IsScriptArgument(code,i)||arg.choices.Count>0||arg.flags.Count>0)&&(!multiple||!string.IsNullOrWhiteSpace(args[i].text));
                            buttons[i].GetComponentInChildren<Text>().text="Значение "+(i+1)+": "+(arg!=null&&arg.used?arg.title:"вручную")+"…";
                            
#if MOBILEUDB_UDMF
if(strings[i]!=null){string key="arg"+i+"str";strings[i].transform.parent.gameObject.SetActive((arg!=null&&arg.stringValue)||indices.Exists(id=>UdmfText.Raw(Map.things[id].fields,key)!=null));stringLabels[i].text=arg!=null&&arg.stringValue?arg.stringTitle:"Строковый аргумент "+i;}
#endif

                        }
                    };action.onValueChanged.AddListener(_=>refresh());refresh();
                }
                
#if MOBILEUDB_UDMF // Deferred from the first release.
if(universal)
                {
                    string[] keys={"skill1","skill2","skill3","skill4","skill5","ambush","single","coop","dm","class1","class2","class3"};
                    string[] names={"Сложность 1","Сложность 2","Сложность 3","Сложность 4","Сложность 5","Засадный","Одиночная игра","Кооператив","Deathmatch","Класс 1","Класс 2","Класс 3"};
                    for(int i=0;i<keys.Length;i++)
                    {
                        string key=keys[i];bool ambush=key=="ambush";
                        BatchBoolean(body,names[i],indices,(m,index)=>
                        {var t=m.things[index];return UdmfText.Boolean(t.fields,key,!t.udmfInitialized&&(!ambush||(t.flags&8)!=0));},
                        (m,index,on)=>UdmfText.SetBool(m.things[index].fields,key,on),changes);
                    }
                }
                else
#endif

                {
                    var flags=BinaryFlagNames(false);
                    var masks=new List<int>(flags.Keys);masks.Sort();
                    foreach(int value in masks)
                    {
                        int mask=value;BatchBoolean(body,flags[mask],indices,(m,index)=>(m.things[index].flags&mask)!=0,
                        (m,index,on)=>m.things[index].flags=on?m.things[index].flags|mask:m.things[index].flags&~mask,changes);
                    }
                }
                
#if MOBILEUDB_UDMF
BatchElementFields(body,indices,ElementKind.Thing,changes);
#endif

                AddLabel(body,"Флаги: Да / Нет / Не менять. У X и Y кнопка операции выбирает абсолютное значение или относительную правку.");
                DialogApply(body,()=>Edit(map=>
                {
                    bool changed=false;foreach(var change in changes)changed|=change(map);
                    if(!changed)throw new InvalidOperationException("Нет изменений: заполните нужное поле или измените флаг.");
                },"Свойства объектов обновлены. Один Undo отменяет все изменения."));
            });
        }
    }
}

