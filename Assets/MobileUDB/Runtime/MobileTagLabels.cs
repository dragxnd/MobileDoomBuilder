using System;
using System.Collections.Generic;
using MobileUDB.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        static string TagLabelDisplay(string text){return text.Replace('\n',' ').Replace('\r',' ').Replace('\t',' ');}
        void TagLabelsMenu()
        {
            if(!DbsGroupsReady){Notify("Перейдите в обычный 2D-режим и завершите текущую операцию.");return;}
            var context=CaptureOpenContext();var choices=new List<PickerChoice>();var numbers=new SortedSet<int>();
            if(Map.tagLabels!=null)foreach(var label in Map.tagLabels)numbers.Add(label.tag);
            foreach(int kind in new[]{13,14,15})foreach(var target in MapTagTargets.Find(Map,kind,null,Actions))numbers.Add(target.tag);
            foreach(int tag in numbers){string name=MapTagLabels.Find(Map,tag);choices.Add(new PickerChoice{value=tag,title=name==null?"Без подписи":TagLabelDisplay(name)});}
            ShowDialog("Подписи тегов",body=>
            {
                LineBatchCaption(body,"КАРТА · "+Map.name);
                AddLabel(body,"Подпись помогает искать числовой тег по имени. Номера и связи карты не меняются. Изменения подписей отменяются через Undo и сохраняются в соседнем .dbs при сохранении WAD.");
                AddButton(body,"Новая подпись…",()=>{if(CurrentOpenContext(context))TagLabelEdit(context,null);else Notify("Карта изменилась. Откройте подписи снова.");});
                AddButton(body,"Найти / изменить подпись…",()=>
                {if(!CurrentOpenContext(context)){Notify("Карта изменилась. Откройте подписи снова.");return;}ChoicePicker("Тег / подпись",choices,entry=>TagLabelEdit(context,entry.value),true,null,false);}).interactable=choices.Count>0;
                var help=Label(body,"Подписей: "+(Map.tagLabels==null?0:Map.tagLabels.Count)+". Подпись без элементов не резервирует номер для автоматического создания тегов.",16);help.supportRichText=false;help.gameObject.AddComponent<LayoutElement>().preferredHeight=70;
            });
        }
        void TagLabelEdit(OpenContext context,int? existing)
        {
            if(!DbsGroupsReady||!CurrentOpenContext(context)){Notify("Карта изменилась. Откройте подписи снова.");return;}
            int tag;try{tag=existing??PasteTags.NextFree(Map,Actions,65535,0);}catch(Exception error){Notify(error.Message);return;}string original=MapTagLabels.Find(Map,tag);
            ShowDialog("Подпись тега",body=>
            {
                LineBatchCaption(body,"НОМЕР И ИМЯ");var number=Field(body,"Тег",tag.ToString());number.readOnly=existing.HasValue;
                var name=Field(body,"Подпись",original??"",false);name.characterLimit=0;name.textComponent.supportRichText=false;
                var nameRow=(RectTransform)name.transform.parent;nameRow.GetComponent<LayoutElement>().preferredHeight=90;
                var caption=(RectTransform)nameRow.GetChild(0);caption.anchorMin=new Vector2(0,.7f);caption.anchorMax=Vector2.one;caption.offsetMin=caption.offsetMax=Vector2.zero;
                var input=(RectTransform)name.transform;input.anchorMin=Vector2.zero;input.anchorMax=new Vector2(1,.68f);input.offsetMin=input.offsetMax=Vector2.zero;
                AddButton(body,"Сохранить подпись",()=>
                {
                    if(!DbsGroupsReady||!CurrentOpenContext(context)){Notify("Карта изменилась. Откройте подписи снова.");return;}
                    try {int value=existing??Number(number,1,65535);ApplyTagLabel(context,value,name.text);}catch(Exception error){Notify(error.Message);}
                });
                if(original!=null)AddButton(body,"Убрать подпись",()=>ApplyTagLabel(context,tag,""));
                AddLabel(body,"Меняется только имя тега. Одинаковый номер использует одну подпись у секторов, стен и объектов. Неподходящие формату номера из .dbs сохраняются как подписи, но недоступны в выборе числовых тегов.");
            });
        }
        void ApplyTagLabel(OpenContext context,int tag,string name)
        {
            if(!DbsGroupsReady||!CurrentOpenContext(context)){Notify("Карта изменилась. Откройте подписи снова.");return;}
            try {var plan=MapTagLabels.Set(Map,tag,name);if(plan.ChangeCount==0){Notify("Подпись уже совпадает.");return;}if(Edit(map=>plan.Apply(map),"Подпись изменена. Доступно одно Undo."))TagLabelsMenu();}
            catch(Exception error){Notify(error.Message);}
        }
    }
}
