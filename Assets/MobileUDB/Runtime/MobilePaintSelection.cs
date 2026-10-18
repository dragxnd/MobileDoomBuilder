using System;
using MobileUDB.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        enum SelectionBrush { Off, Add, Remove, Toggle }
        SelectionBrush selectionBrush;
        bool paintTracking;ElementRef? paintLast;
        MapDocument paintMap;long paintRevision;ElementKind paintKind;SelectionHitIndex paintIndex;
        Button paintSelectionButton;
        public bool PaintingSelection {get{return Tool==MobileTool.Select&&selectionBrush!=SelectionBrush.Off&&!VisualActive;}}
        string PaintLabel {get{return selectionBrush==SelectionBrush.Add?"Кисть +":selectionBrush==SelectionBrush.Remove?"Кисть −":"Кисть ±";}}
        void BuildPaintSelectionUI(RectTransform viewport)
        {
            paintSelectionButton=Button(viewport,"",DisablePaintSelection);
            paintSelectionButton.name="Отключить кисть выбора";paintSelectionButton.GetComponentInChildren<Text>().fontSize=16;
            var r=paintSelectionButton.GetComponent<RectTransform>();r.anchorMin=r.anchorMax=r.pivot=Vector2.one;r.sizeDelta=new Vector2(210,52);r.anchoredPosition=new Vector2(-12,-12);
            paintSelectionButton.gameObject.SetActive(false);
        }
        void RefreshPaintSelectionUI()
        {RefreshMassSelectionButton();if(paintSelectionButton==null)return;paintSelectionButton.gameObject.SetActive(PaintingSelection&&!massSelection);paintSelectionButton.GetComponentInChildren<Text>().text=PaintLabel+" · выключить";}
        void PaintSelectionButtons(RectTransform body)
        {
            LineBatchCaption(body,"ВЫБОР ПРОВЕДЕНИЕМ");var row=ShapeOptionRow(body);
            Button(row,"Добавлять +",()=>StartPaintSelection(SelectionBrush.Add));Button(row,"Убирать −",()=>StartPaintSelection(SelectionBrush.Remove));
            row=ShapeOptionRow(body);Button(row,"Переключать ±",()=>StartPaintSelection(SelectionBrush.Toggle));Button(row,"Обычный выбор",()=>{if(!StartSelectionCommand())return;DisablePaintSelection();});
            var note=Label(body,"Кисть меняет выделение под пальцем. Два пальца — обзор. «Выключить» или Back возвращает касание / рамку.",16);note.gameObject.AddComponent<LayoutElement>().preferredHeight=72;
        }
        void StartPaintSelection(SelectionBrush brush)
        {if(!StartSelectionCommand())return;selectionBrush=brush;Changed(false);Notify(PaintLabel+": проведите пальцем по элементам.");}
        void DisablePaintSelection()
        {CancelDrag();selectionBrush=SelectionBrush.Off;Changed(false);Notify("Обычный выбор: касание или рамка.");}
        void CancelPaintSelectionGesture(){paintTracking=false;paintLast=null;massStroke.Clear();}
        void ForgetPaintSelectionIndex(){paintIndex=null;paintMap=null;}
        bool BeginPaintSelection(Vector2 screen)
        {
            if(!PaintingSelection||!View.ScreenRect.Contains(screen))return false;
            if(!ReferenceEquals(paintMap,Map)||paintRevision!=recoveryRevision.Current||paintKind!=Kind||paintIndex==null)
            {paintIndex=new SelectionHitIndex(Map,Kind);paintMap=Map;paintRevision=recoveryRevision.Current;paintKind=Kind;}
            paintTracking=true;paintLast=null;PaintAt(screen);return true;
        }
        void PaintAt(Vector2 screen)
        {
            if(!View.ScreenRect.Contains(screen))return;var hit=paintIndex.Hit(View.World(screen),26/View.zoom);
            // Original highlight transitions: a gap does not retrigger the last
            // element; another hit does. Selection alone never creates history.
            if(!hit.HasValue||paintLast.HasValue&&paintLast.Value.Equals(hit.Value))return;paintLast=hit;
            if(massSelection&&!massStroke.Add(hit.Value))return;
            if(selectionBrush==SelectionBrush.Add)Selection.Add(hit.Value);
            else if(selectionBrush==SelectionBrush.Remove)Selection.Remove(hit.Value);
            else if(!Selection.Add(hit.Value))Selection.Remove(hit.Value);Changed(false);
        }
        bool MovePaintSelection(Vector2 previous,Vector2 screen)
        {
            if(!paintTracking)return false;
            // Sample movement to avoid skipping elements at low input frame rates.
            int steps=Math.Min(256,Math.Max(1,Mathf.CeilToInt(Vector2.Distance(previous,screen)/8)));
            for(int i=1;i<=steps;i++)PaintAt(Vector2.Lerp(previous,screen,i/(float)steps));return true;
        }
        bool EndPaintSelection(Vector2 screen)
        {if(!PaintingSelection)return false;if(paintTracking)PaintAt(screen);CancelPaintSelectionGesture();Notify(PaintLabel+": выбрано "+Selection.Count+".");return true;}
    }
}
