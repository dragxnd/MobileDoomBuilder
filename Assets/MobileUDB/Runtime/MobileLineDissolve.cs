using System;
using System.Collections.Generic;
using MobileUDB.Core;
using UnityEngine;

namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        bool LineDissolveReady {get{if(!SelectionToolsReady||Kind!=ElementKind.Line)return false;foreach(var item in Selection)if(item.kind!=ElementKind.Line)return false;return true;}}
        void LineDissolveButton(RectTransform body)
        {LineBatchCaption(body,"ГЕОМЕТРИЯ СТЕН");var row=ShapeOptionRow(body);LineSplitButton(row);VisualThingCommand(row,"Растворить стены…",EditorIcon.Symbol.Merge,LineDissolveMenu).interactable=LineDissolveReady;}
        void LineDissolveMenu()
        {
            if(!LineDissolveReady){Notify("Выделите стены и завершите рисование или вставку.");return;}
            try
            {
                CancelDrag();var context=CaptureOpenContext();var seeds=new HashSet<ElementRef>(Selection);var indices=new List<int>();foreach(var seed in seeds)indices.Add(seed.index);
                var plan=Map.PrepareLineDissolve(indices,drawTextures);var catalog=resources;
                ShowDialog("Растворить стены",body=>
                {
                    AddLabel(body,"Стены к удалению: "+plan.LineCount+". Объединений секторов: "+plan.JoinCount+". Восстановленных областей: "+plan.RepairCount+".");
                    AddLabel(body,"Сохраняются свойства сектора с большей площадью его прямоугольной рамки; при равенстве — заднего. Действия удаляемых стен исчезнут. Объекты сохраняются. Один Undo восстановит всю операцию.");
                    DialogApply(body,()=>
                    {
                        if(!CurrentOpenContext(context)||!Selection.SetEquals(seeds)||Kind!=ElementKind.Line||!ReferenceEquals(catalog,resources)){Notify("Карта, ресурсы или выделение изменились. Откройте растворение снова.");return false;}
                        return Edit(map=>{plan.Apply(map);Selection.Clear();},"Стены растворены: "+plan.LineCount+". Доступна отмена.");
                    },"Растворить",true);
                });
            }
            catch(Exception error){Notify(error.Message);ShowDialog("Растворение недоступно",body=>{AddLabel(body,error.Message);AddButton(body,"Вернуться к инструментам",MoreMenu);});}
        }
    }
}
