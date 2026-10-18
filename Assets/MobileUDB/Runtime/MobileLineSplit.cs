using System;using System.Collections.Generic;using MobileUDB.Core;using UnityEngine;using UnityEngine.UI;
namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        void LineSplitButton(RectTransform row)
        {VisualThingCommand(row,"Разделить…",EditorIcon.Symbol.Vertex,LineSplitMenu).interactable=LineDissolveReady;}
        void LineSplitMenu()
        {
            if(!LineDissolveReady){Notify("Выделите стены и завершите рисование или вставку.");return;}
            CancelDrag();var context=CaptureOpenContext();var order=new List<int>();foreach(var item in Selection)order.Add(item.index);var catalog=resources;
            int stored=PlayerPrefs.GetInt("MobileUDB.SplitLines.Offsets",0);var offsets=stored>=0&&stored<4?(CurveSplitOffsets)stored:CurveSplitOffsets.Interpolate;
            ShowDialog("Разделить стены",body=>
            {
                body.GetComponent<VerticalLayoutGroup>().spacing=5;
                var summary=Label(body,"Стен: "+order.Count+". Деление в середине, целые координаты. Новые точки не сшиваются. Действия, флаги и свойства сохраняются.",18);summary.alignment=TextAnchor.UpperLeft;summary.gameObject.AddComponent<LayoutElement>().minHeight=40;
                CurveCaption(body,"СМЕЩЕНИЯ ТЕКСТУР");var choices=new List<Button>();Text note=null;
                Action refresh=()=>{for(int i=0;i<choices.Count;i++)choices[i].GetComponent<Image>().color=(int)offsets==i?Accent:Panel;note.text=CurveOffsetLabel(offsets)+". Сброс / копирование — для новой части. «Продолжать» учитывает направление сторон. Вся группа — один Undo.";};
                string[] labels={"Продолжать","Копировать","X = 0","X / Y = 0"};
                for(int i=0;i<4;i+=2){var row=ShapeOptionRow(body);for(int j=i;j<i+2;j++){var chosen=(CurveSplitOffsets)j;choices.Add(Button(row,labels[j],()=>{offsets=chosen;refresh();}));}}
                note=Label(body,"",16);note.alignment=TextAnchor.UpperLeft;note.gameObject.AddComponent<LayoutElement>().minHeight=40;
                var feedback=Label(body,"",16);feedback.alignment=TextAnchor.UpperLeft;feedback.color=new Color(1,.72f,.38f);feedback.gameObject.AddComponent<LayoutElement>().minHeight=40;feedback.gameObject.SetActive(false);refresh();
                DialogApply(body,()=>
                {
                    try
                    {
                        if(!CurrentOpenContext(context)||!LineDissolveReady||!SameLineSplitOrder(order)||!ReferenceEquals(catalog,resources))throw new InvalidOperationException("Карта, ресурсы или выделение изменились. Откройте разделение снова.");
                        var plan=MidpointLineSplit.Prepare(Map,order,offsets,resources);
                        if(!Edit(map=>{var parts=plan.Apply(map);Selection.Clear();Selection.UnionWith(parts);Kind=ElementKind.Line;},"Стены разделены: "+plan.LineCount+". "+(plan.DegenerateCount>0?"Нулевых частей: "+plan.DegenerateCount+"; см. проверку карты. ":"")+"Доступна отмена."))return false;
                        PlayerPrefs.SetInt("MobileUDB.SplitLines.Offsets",(int)offsets);return true;
                    }
                    catch(Exception error){feedback.text=error.Message;feedback.gameObject.SetActive(true);Notify(error.Message);return false;}
                },"Разделить",true);
            });
        }
        bool SameLineSplitOrder(List<int> order)
        {if(Selection.Count!=order.Count)return false;int i=0;foreach(var item in Selection)if(item.kind!=ElementKind.Line||item.index!=order[i++])return false;return true;}
    }
}
