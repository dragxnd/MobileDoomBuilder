using System;using System.Collections.Generic;using MobileUDB.Core;using UnityEngine.UI;
namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        bool IsPolyobjectArgument(ActionArgument argument){return Map.format==MapFormat.Hexen&&argument!=null&&argument.used&&argument.type==25;}
        void PolyobjectNumberPicker(InputField field,int maximum,ActionArgument argument=null)
        {
            if(Map.format!=MapFormat.Hexen||field==null)return;
            var context=CaptureOpenContext();var types=thingCatalog;var actionCatalog=Actions;var resourceCatalog=resources;
            var choices=new List<PickerChoice>();var included=new HashSet<int>();
            Action<int,string> add=(number,title)=>{if(number>=0&&number<=maximum&&included.Add(number))choices.Add(new PickerChoice{value=number,title=title});};
            // Native PolyobjectNumberHandler lists unique anchors, descending.
            foreach(int number in PolyobjectChecks.AnchorNumbers(Map,types))add(number,"Якорь на карте");
            if(argument!=null)foreach(var option in argument.choices)add(option.value,option.title);
            add(0,"Нулевой номер / без зеркала — зависит от действия");
            var numeric=field.GetComponent<NumericFieldAssignment>();int current=0;bool hasCurrent=(numeric==null||numeric.absolute())&&int.TryParse(field.text,out current);if(hasCurrent)add(current,"Текущее значение · якоря нет");
            ChoicePicker(maximum==255?"Полиобъекты · 0–255":"Номер полиобъекта",choices,entry=>
            {
                if(!CurrentOpenContext(context)||field==null||!ReferenceEquals(types,thingCatalog)||!ReferenceEquals(actionCatalog,Actions)||!ReferenceEquals(resourceCatalog,resources))
                {Notify("Карта или профиль изменились. Откройте выбор номера снова.");return;}
                if(numeric!=null)numeric.assign(entry.value);else field.text=entry.value.ToString();
            },true,entry=>(hasCurrent&&entry.value==current?"✓ ":"")+entry.value+" · "+entry.title,false);
        }
    }
}
