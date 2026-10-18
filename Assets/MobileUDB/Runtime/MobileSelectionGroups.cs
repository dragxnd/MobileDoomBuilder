using System;
using MobileUDB.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        int selectionGroupSlot;
        Text selectionGroupFeedback;
        bool SelectionGroupsReady {get{return !VisualActive&&Draft.Count==0&&Tool!=MobileTool.Paste;}}
        void SelectionGroupsButton(RectTransform body)
        {
            LineBatchCaption(body,"СОХРАНЁННЫЕ ГРУППЫ");var row=ShapeOptionRow(body);
            VisualThingCommand(row,"Группы выделения…",EditorIcon.Symbol.Group,SelectionGroupsMenu).interactable=SelectionGroupsReady;
        }
        void SelectionGroupsMenu()
        {
            if(!SelectionGroupsReady){Notify("Завершите рисование или вставку перед работой с группами.");return;}
            var context=CaptureOpenContext();var counts=SelectionGroups.Counts(Map);int slot=selectionGroupSlot;
            ShowDialog("Группы выделения",body=>
            {
                LineBatchCaption(body,"ГРУППА · 1–10");Button[] slots=new Button[SelectionGroups.SlotCount];
                Text summary=null;Button whole=null,add=null,clear=null;var kindButtons=new Button[4];
                Action refresh=()=>
                {
                    int total=0;for(int k=0;k<4;k++)total+=counts[slot,k];
                    for(int i=0;i<slots.Length;i++){int n=0;for(int k=0;k<4;k++)n+=counts[i,k];slots[i].GetComponentInChildren<Text>().text=(i+1)+" · "+n;slots[i].GetComponent<Image>().color=i==slot?Accent:Panel;}
                    summary.text="Группа "+(slot+1)+" · элементов "+total;
                    whole.interactable=total>0;clear.interactable=total>0;add.interactable=Selection.Count>0;
                    string[] names={"Вершины","Стены","Секторы","Объекты"};
                    for(int k=0;k<4;k++){kindButtons[k].GetComponentInChildren<Text>().text=names[k]+" · "+counts[slot,k];kindButtons[k].interactable=counts[slot,k]>0;}
                };
                for(int i=0;i<10;i+=5)
                {
                    var row=ShapeOptionRow(body);for(int j=i;j<i+5;j++){int number=j;slots[j]=Button(row,"",()=>{slot=selectionGroupSlot=number;refresh();});slots[j].name="Группа "+(j+1);slots[j].GetComponentInChildren<Text>().fontSize=16;}
                }
                summary=Label(body,"",16);summary.gameObject.AddComponent<LayoutElement>().preferredHeight=28;
                selectionGroupFeedback=Label(body,"",16);selectionGroupFeedback.gameObject.AddComponent<LayoutElement>().preferredHeight=0;
                LineBatchCaption(body,"ВЫЗВАТЬ ВЫДЕЛЕНИЕ");var selectRow=ShapeOptionRow(body);
                whole=VisualThingCommand(selectRow,"Выделить группу целиком",EditorIcon.Symbol.Select,()=>RecallSelectionGroup(context,slot,null));
                for(int i=0;i<4;i+=2)
                {
                    var row=ShapeOptionRow(body);for(int j=i;j<i+2;j++){var kind=(ElementKind)j;kindButtons[j]=Button(row,"",()=>RecallSelectionGroup(context,slot,kind));kindButtons[j].name="Из группы: "+kind;kindButtons[j].GetComponentInChildren<Text>().fontSize=16;}
                }
                LineBatchCaption(body,"ИЗМЕНИТЬ ГРУППУ");var editRow=ShapeOptionRow(body);
                add=VisualThingCommand(editRow,"Добавить",EditorIcon.Symbol.GroupAdd,()=>ChangeSelectionGroup(context,slot,false));
                clear=VisualThingCommand(editRow,"Очистить",EditorIcon.Symbol.GroupRemove,()=>ChangeSelectionGroup(context,slot,true));
                LineBatchCaption(body,"ПЕРЕНОС ИЗ UDB");var importRow=ShapeOptionRow(body);
                VisualThingCommand(importRow,"Импорт .dbs…",EditorIcon.Symbol.File,PickDbsGroups).interactable=DbsGroupsReady;
                var exportRow=ShapeOptionRow(body);
                VisualThingCommand(exportRow,"Экспорт .dbs…",EditorIcon.Symbol.File,DbsExportMenu).interactable=DbsGroupsReady;
                var note=Label(body,"«Добавить» сохраняет выделение в группе, не заменяя прежние элементы. Можно добавлять из разных режимов. Изменения группы отменяются одним Undo. Группы сохраняются в проекте .mobileudb.",16);note.alignment=TextAnchor.MiddleLeft;note.gameObject.AddComponent<LayoutElement>().preferredHeight=116;
                refresh();
            });
        }
        bool CurrentGroupContext(OpenContext context)
        {if(CurrentOpenContext(context)&&SelectionGroupsReady)return true;GroupFeedback("Карта изменилась. Откройте группы снова.");return false;}
        void GroupFeedback(string text)
        {Notify(text);if(selectionGroupFeedback!=null){selectionGroupFeedback.text=text;selectionGroupFeedback.color=new Color(1,.65f,.55f);selectionGroupFeedback.GetComponent<LayoutElement>().preferredHeight=48;}}
        void RecallSelectionGroup(OpenContext context,int slot,ElementKind? kind)
        {
            if(!CurrentGroupContext(context))return;
            try
            {
                var members=SelectionGroups.Members(Map,slot,kind);CancelDrag();CloseDialog();selectionBrush=SelectionBrush.Off;
                if(kind.HasValue)SetKind(kind.Value);else SetTool(MobileTool.Select);
                Selection.Clear();Selection.UnionWith(members);Changed(false);Notify("Группа "+(slot+1)+": выбрано "+members.Count+".");
            }
            catch(Exception error){GroupFeedback(error.Message);}
        }
        void ChangeSelectionGroup(OpenContext context,int slot,bool clear)
        {
            if(!CurrentGroupContext(context))return;
            try
            {
                var plan=clear?SelectionGroups.PlanClear(Map,slot):SelectionGroups.PlanAdd(Map,Selection,slot);
                if(plan.ChangeCount==0){GroupFeedback(clear?"Группа уже пуста.":"Все выделенные элементы уже в группе.");return;}
                if(Edit(m=>plan.Apply(m),(clear?"Очищена группа ":"Добавлены элементы в группу ")+(slot+1)+". Доступно одно Undo.")){selectionGroupSlot=slot;SelectionGroupsMenu();}
            }
            catch(Exception error){GroupFeedback(error.Message);}
        }
    }
}
