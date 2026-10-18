using System;using System.Collections.Generic;using MobileUDB.Core;using UnityEngine.UI;
namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        bool IsScriptArgument(int action,int index)
        {
            // Native GZGeneral.ACS_SPECIALS / CheckUnknownScripts, binary numeric arg0.
            if(Map.format!=MapFormat.Hexen||index!=0)return false;
            return action==80||action==81||action==82||action==83||action==84||action==85||action==226;
        }
        void ScriptNumberPicker(InputField field,InputField mapField,InputField actionField,int action)
        {
            int targetMap;
            if(field==null||mapField==null||!int.TryParse(mapField.text,out targetMap)||targetMap!=0)
            {Notify("Выбор из BEHAVIOR доступен для карты 0. Для другой карты введите номер вручную.");return;}
            int[] numbers;string reason;
            if(!HexenScriptDirectory.Read(Map,out numbers,out reason)){Notify(reason);return;}
            var context=CaptureOpenContext();var actions=Actions;
            string actionText=actionField==null?null:actionField.text,mapText=mapField.text;
            var choices=new List<PickerChoice>();var included=new HashSet<int>();
            foreach(int number in numbers){included.Add(number);choices.Add(new PickerChoice{value=number,title="В BEHAVIOR текущей карты"});}
            int current;bool hasCurrent=int.TryParse(field.text,out current)&&current>=0&&current<=255;
            if(hasCurrent&&included.Add(current))choices.Add(new PickerChoice{value=current,title="Текущее значение · в BEHAVIOR не найдено"});
            if(choices.Count==0){Notify("В BEHAVIOR нет числовых скриптов 0–255. Введите номер вручную.");return;}
            ChoicePicker("Скрипты · карта 0 · BEHAVIOR",choices,entry=>
            {
                if(!CurrentOpenContext(context)||field==null||mapField==null||mapField.text!=mapText||actionField==null||actionField.text!=actionText||!ReferenceEquals(actions,Actions)||!IsScriptArgument(action,0))
                {Notify("Карта или действие изменились. Откройте выбор скрипта снова.");return;}
                field.text=entry.value.ToString();
            },true,entry=>(hasCurrent&&entry.value==current?"✓ ":"")+entry.value+" · "+entry.title,false);
        }
    }
}
