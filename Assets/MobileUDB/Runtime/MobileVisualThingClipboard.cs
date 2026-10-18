using System;
using System.Collections.Generic;
using MobileUDB.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        ThingClipboard visualThingClipboard;
        void VisualThingClipboardMenu()
        {
            var context=Map;MapPoint point;bool hasPoint=visualView.TryPlacementPoint(out point);
            var targets=new List<int>(visualView.SelectedThings);
            ShowDialog("Объекты в 3D",body=>
            {
                LineBatchCaption(body,"МЕСТО");
                var feedback=Label(body,hasPoint?"Касание: X "+Math.Round(point.x)+" · Y "+Math.Round(point.y)+". После движения — прицел.":"Коснитесь поверхности или наведите прицел на карту, затем откройте меню.",16);
                feedback.gameObject.AddComponent<LayoutElement>().preferredHeight=72;
                AddButton(body,"Создать объект здесь…",()=>VisualInsertThingMenu(context,point)).interactable=hasPoint;
                AddButton(body,"Вставить объекты здесь · "+(visualThingClipboard==null?0:visualThingClipboard.Count),()=>
                {
                    if(!VisualThingContext(context)){feedback.text=message;return;}
                    try {ApplyVisualThingInsertion(visualThingClipboard.PlanPaste(Map,point),"Вставлены объекты.");}
                    catch(Exception error){Notify(error.Message);feedback.text=message;}
                }).interactable=hasPoint&&visualThingClipboard!=null;
                LineBatchCaption(body,"ВЫДЕЛЕНИЕ · "+targets.Count);
                var row=Rect("Буфер объектов",body);row.gameObject.AddComponent<LayoutElement>().preferredHeight=60;
                var layout=row.gameObject.AddComponent<HorizontalLayoutGroup>();layout.spacing=8;layout.childControlWidth=layout.childForceExpandWidth=layout.childControlHeight=layout.childForceExpandHeight=true;
                VisualThingCommand(row,"Копировать",EditorIcon.Symbol.Copy,()=>CopyVisualThings(context,targets,false)).interactable=targets.Count>0;
                VisualThingCommand(row,"Вырезать",EditorIcon.Symbol.Cut,()=>CopyVisualThings(context,targets,true)).interactable=targets.Count>0;
                var help=Label(body,"Вставка сохраняет свойства и взаимное положение. Z — в Hexen. Буфер объектов отдельный.",16);
                help.alignment=TextAnchor.MiddleLeft;help.gameObject.AddComponent<LayoutElement>().preferredHeight=56;
                ThingOrientationButton(body,true);
                LineBatchCaption(body,"КАМЕРА");
                AddButton(body,"Камера из выбранного объекта",()=>
                {
                    if(!VisualThingContext(context))return;
                    try {visualView.CameraAtThing(targets[0]);CloseDialog();Notify("Камера в центре объекта; направление — его угол.");}
                    catch(Exception error){Notify(error.Message);}
                }).interactable=targets.Count==1;
            });
        }
        Button VisualThingCommand(RectTransform row,string title,EditorIcon.Symbol symbol,Action action)
        {
            var button=Button(row,title,action);var text=button.GetComponentInChildren<Text>();text.fontSize=16;text.rectTransform.offsetMin=new Vector2(38,0);
            var iconRect=Rect("Icon",button.transform);iconRect.anchorMin=iconRect.anchorMax=new Vector2(0,.5f);iconRect.sizeDelta=new Vector2(24,24);iconRect.anchoredPosition=new Vector2(20,0);
            var icon=iconRect.gameObject.AddComponent<EditorIcon>();icon.symbol=symbol;icon.color=Color.white;icon.raycastTarget=false;return button;
        }
        bool VisualThingContext(MapDocument context)
        {if(VisualActive&&ReferenceEquals(Map,context))return true;Notify("Карта изменилась. Повторите команду объектов.");return false;}
        void VisualInsertThingMenu(MapDocument context,MapPoint point)
        {
            ShowDialog("Создать объект в 3D",body=>
            {
                var type=Field(body,"Тип",placementType.ToString());
                AddButton(body,"Каталог типов…",()=>ThingPicker(id=>type.text=id.ToString()));
                AddLabel(body,"XY привязывается к текущей сетке, если привязка включена. Объект появляется на полу или потолке согласно своему типу.");
                var feedback=Label(body,"Новый объект: Z = 0 относительно пола / потолка.",16);
                feedback.gameObject.AddComponent<LayoutElement>().preferredHeight=72;
                AddButton(body,"Создать",()=>
                {
                    if(!VisualThingContext(context)){feedback.text=message;return;}
                    try {int id=Number(type,0,65535);var plan=ThingClipboard.PlanInsert(Map,Snapped(point),id);
                        if(ApplyVisualThingInsertion(plan,"Добавлен: "+thingCatalog.Find(id).title))placementType=id;}
                    catch(Exception error){Notify(error.Message);feedback.text=message;}
                });
            });
        }
        bool ApplyVisualThingInsertion(ThingInsertPlan plan,string text)
        {
            List<int> indices=null;
            if(!Edit(m=>indices=plan.Apply(m),text+" · "+plan.Count))return false;
            visualView.RebindInsertedThings(indices);CloseDialog();return true;
        }
        void CopyVisualThings(MapDocument context,List<int> targets,bool cut)
        {
            if(!VisualThingContext(context))return;
            try
            {
                var captured=ThingClipboard.Capture(Map,targets);
                if(cut)
                {if(!Edit(m=>ThingBatchEdits.Delete(m,targets),"Вырезаны объекты · "+captured.Count))return;visualView.ForgetThingSelection();}
                visualThingClipboard=captured;CloseDialog();Notify("Объектов в буфере: "+captured.Count);
            }
            catch(Exception error){Notify(error.Message);}
        }
    }
}
