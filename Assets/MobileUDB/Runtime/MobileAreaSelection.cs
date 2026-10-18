using System;
using System.Collections.Generic;
using MobileUDB.Core;
using UnityEngine;

namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        bool areaTracking,areaVisible,areaTouching;
        SelectionOperation areaOperation=SelectionOperation.Replace;
        Vector2 areaPointerStart;
        MapPoint areaStart,areaEnd;
        public bool AreaSelectionVisible { get { return areaVisible; } }
        public MapPoint AreaSelectionStart { get { return areaStart; } }
        public MapPoint AreaSelectionEnd { get { return areaEnd; } }

        void BeginAreaSelection(Vector2 screen)
        {
            if(Tool!=MobileTool.Select||!View.ScreenRect.Contains(screen))return;
            areaTracking=true;areaPointerStart=screen;areaStart=areaEnd=View.World(screen);
        }
        void MoveAreaSelection(Vector2 screen)
        {
            if(!areaTracking)return;
            Rect r=View.ScreenRect;
            screen=new Vector2(Mathf.Clamp(screen.x,r.xMin,r.xMax),Mathf.Clamp(screen.y,r.yMin,r.yMax));
            areaEnd=View.World(screen);
            areaVisible|=Vector2.Distance(areaPointerStart,screen)>8;
            if(areaVisible)View.SetVerticesDirty();
        }
        bool EndAreaSelection(Vector2 screen)
        {
            if(!areaTracking)return false;
            MoveAreaSelection(screen);bool select=areaVisible;
            MapPoint start=areaStart,end=areaEnd;CancelAreaSelection();
            if(!select)return false;
            if(start.x!=end.x&&start.y!=end.y)
            {
                var matches=MapSelection.Area(Map,Kind,start,end,areaTouching);
                MapSelection.Apply(Selection,matches,areaOperation);
                Notify("Рамка: " + matches.Count + ". Всего выбрано: " + Selection.Count + ".");
            }
            Changed(false);return true;
        }
        bool CancelAreaSelection()
        {
            bool active=areaTracking;areaTracking=areaVisible=false;
            if(active&&View!=null)View.SetVerticesDirty();return active;
        }
        bool StartSelectionCommand()
        {
            if(Draft.Count>0){Notify("Завершите или отмените текущее рисование.");return false;}
            CloseDialog();SetTool(MobileTool.Select);return true;
        }
        void SelectionAll(bool inverse)
        {
            if(!StartSelectionCommand())return;
            var result=new HashSet<ElementRef>();
            for(int i=0;i<MapSelection.Count(Map,Kind);i++)
            {
                var item=new ElementRef(Kind,i);if(!inverse||!Selection.Contains(item))result.Add(item);
            }
            Selection.Clear();Selection.UnionWith(result);Changed(false);Notify("Выбрано: " + Selection.Count + ".");
        }
        void AreaOperation(SelectionOperation operation)
        {
            if(!StartSelectionCommand())return;selectionBrush=SelectionBrush.Off;areaOperation=operation;Changed(false);
            Notify("Выбор рамкой: " + AreaOperationLabel(operation) + ". Проведите пальцем по карте.");
        }
        static string AreaOperationLabel(SelectionOperation operation)
        {
            switch(operation)
            {
                case SelectionOperation.Add:return "добавить";
                case SelectionOperation.Remove:return "убрать";
                case SelectionOperation.Intersect:return "оставить внутри";
                default:return "заменить выделение";
            }
        }
        void SelectionMenu()
        {
            ShowDialog("Выделение",body=>
            {
                AddLabel(body,"Коснитесь элемента или проведите пальцем для рамки. Два пальца — обзор карты.");
                AddButton(body,"Выделить всё в текущем режиме",()=>SelectionAll(false));
                AddButton(body,"Инвертировать выделение",()=>SelectionAll(true));
                AddButton(body,"Снять выделение",()=>{if(!StartSelectionCommand())return;Selection.Clear();Changed(false);Notify("Выделение снято.");});
                SelectionToolsButtons(body);
                SelectionGroupsButton(body);
                PaintSelectionButtons(body);
                LineBatchCaption(body,"ПРИВЯЗКА ВЫДЕЛЕНИЯ");
                var snapFeedback=Label(body,"Совпадения объединяются, схлопнувшиеся стены удаляются. Undo восстанавливает всю операцию.",16);snapFeedback.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().preferredHeight=72;
                AddButton(body,"Привязать выделение к сетке · "+GridStepText,()=>{SnapSelectionToGrid();if(ModalOpen)snapFeedback.text=message;}).interactable=Selection.Count>0&&Draft.Count==0&&Tool!=MobileTool.Paste;
                AddButton(body,"Касание: " + (MultiSelect?"несколько":"один элемент"),()=>{MultiSelect=!MultiSelect;SelectionMenu();});
                AddLabel(body,"Рамка: " + AreaOperationLabel(areaOperation));
                foreach(var mode in new[]{SelectionOperation.Replace,SelectionOperation.Add,SelectionOperation.Remove,SelectionOperation.Intersect})
                {var operation=mode;AddButton(body,"Рамка: " + AreaOperationLabel(operation),()=>AreaOperation(operation));}
                AddButton(body,"Стены / секторы: " + (areaTouching?"задета граница":"целиком внутри"),()=>{areaTouching=!areaTouching;SelectionMenu();});
                AddLabel(body,"Вершины и объекты — по точке. Для секторов учитываются все стены, включая внутренние контуры.");
            });
        }
        void SnapSelectionToGrid()
        {
            if(Draft.Count>0||Tool==MobileTool.Paste){Notify("Завершите рисование или вставку перед привязкой.");return;}
            try
            {
                CancelDrag();var plan=MapGridSnap.Plan(Map,Selection,gridFrame,includeSectorThings);
                if(plan.ChangeCount==0){Notify("Выделение уже на сетке.");return;}
                if(Edit(m=>{plan.Apply(m);Selection.Clear();},"Привязано к сетке: вершин "+plan.VertexCount+", объектов "+plan.ThingCount+". Одно Undo отменяет привязку."))CloseDialog();
            }
            catch(Exception error){Notify(error.Message);}
        }
    }
}
