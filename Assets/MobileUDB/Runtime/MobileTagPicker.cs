using System;
using System.Collections.Generic;
using MobileUDB.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        static bool IsTagArgument(ActionArgument argument)
        {return argument.used&&argument.type>=13&&argument.type<=15;}
        int BinaryLineTagType(InputField action,List<int> indices=null)
        {
            int code;if(int.TryParse(action.text,out code))return Actions.Find(code).lineToLineTag?15:13;
            if(indices==null)return 13;
            int type=Actions.Find(Map.lines[indices[0]].action).lineToLineTag?15:13;
            foreach(int index in indices)if((Actions.Find(Map.lines[index].action).lineToLineTag?15:13)!=type)return 0;
            return type;
        }
        void TagControl(Transform body,string title,int value,int minimum,int maximum,int type,List<Action<MapDocument>> apply,Action<MapDocument,int> setter,Func<int> resolveType=null)
        {
            var field=Field(body,title,value.ToString());apply.Add(m=>setter(m,Number(field,minimum,maximum)));
            AddButton(body,"Выбрать тег / свободный номер…",()=>TagPicker(field,resolveType==null?type:resolveType(),maximum,null,true));
        }
        void TagPicker(InputField field,int type,int maximum,ActionArgument argument=null,bool assigning=false)
        {
            try
            {
                int current;bool valid=int.TryParse(field.text,out current);int minimum=MapFormats.IsUniversal(Map.format)?int.MinValue:0;
                Func<int,bool> accepts=null;
                if(type==14&&argument!=null&&argument.targetClasses.Count>0)
                    accepts=id=>argument.targetClasses.Exists(name=>string.Equals(name,thingCatalog.Find(id).className,StringComparison.OrdinalIgnoreCase));
                var targets=MapTagTargets.Find(Map,type==0?13:type,accepts,Actions);
                if(type==0)targets.AddRange(MapTagTargets.Find(Map,15,null,Actions));
                var choices=new List<PickerChoice>();var included=new HashSet<int>();
                Action<int,string,string> add=(n,title,category)=>{if(n>=minimum&&n<=maximum&&included.Add(n)){string name=MapTagLabels.Find(Map,n);choices.Add(new PickerChoice{value=n,title=string.IsNullOrEmpty(name)?title:TagLabelDisplay(name)+" · "+title,category=category});}};
                if(argument!=null)foreach(var value in argument.choices)add(value.value,value.title,"Значения действия");
                add(0,assigning?"Без тега":"Особое значение: зависит от действия","Значения действия");
                foreach(var entry in targets)
                {
                    string kind=entry.kind==ElementKind.Sector?"Секторов":entry.kind==ElementKind.Line?"Стен":"Объектов";
                    string title=kind+": "+entry.count+" · первый #"+entry.firstIndex;
                    if(entry.kind==ElementKind.Thing)title+=" · "+thingCatalog.Find(Map.things[entry.firstIndex].type).title;
                    if(accepts!=null)title+=" · подходящий класс";
                    add(entry.tag,title,"Цели на карте");
                }
                if(Map.tagLabels!=null)foreach(var label in Map.tagLabels)add(label.tag,"Подходящих целей этого типа нет","Подписи тегов");
                if(valid)add(current,"Текущее значение · подходящих целей не найдено","Текущее значение");
                try {int free=PasteTags.NextFree(Map,Actions,maximum,valid?current:0);add(free,"Свободный номер · цель ещё не назначена","Новый номер");}
                catch(InvalidOperationException){ /* Existing targets stay usable when the numeric range is full. */ }
                string heading=(type==0?"Теги секторов / стен":type==13?"Теги секторов":type==14?"Теги объектов":"Теги стен")+(maximum==255?" · 0–255":"");
                var context=CaptureOpenContext();
                ChoicePicker(heading,choices,entry=>{if(!CurrentOpenContext(context)){Notify("Карта изменилась. Откройте выбор тега снова.");return;}field.text=entry.value.ToString();},true,entry=>(valid&&entry.value==current?"✓ ":"")+entry.value+" · "+entry.title,false);
            }
            catch(Exception error){Notify(error.Message);}
        }
    }
}
