// GPL-3.0. Read-only touch adaptation of classic UDB BlockMap Mode.
using System.Collections.Generic;
using MobileUDB.Core;
using UnityEngine;
using UnityEngine.UI;
namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        public bool BlockmapActive {get;private set;}
        internal BlockmapPreview BlockmapData {get;private set;}
        internal int BlockmapX {get;private set;}=-1;
        internal int BlockmapY {get;private set;}=-1;
        MapDocument blockmapMap;long blockmapRevision=-1;
        RectTransform blockmapRoot;Button blockmapCloseButton;
        readonly Dictionary<GameObject,bool> blockmapHiddenUI=new Dictionary<GameObject,bool>();
        readonly List<ElementRef> blockmapPreviousSelection=new List<ElementRef>();
        MobileTool blockmapPreviousTool;ElementKind blockmapPreviousKind;SelectionBrush blockmapPreviousBrush;bool blockmapPreviousMass;
        bool blockmapPointerActive,blockmapMoved;Vector2 blockmapPointerStart;OpenContext blockmapPointerContext;
        void BuildBlockmapUI()
        {
            blockmapRoot=Rect("Blockmap controls",safeRoot);Stretch(blockmapRoot);
            blockmapCloseButton=IconButton(blockmapRoot,"Закрыть блокмапу",EditorIcon.Symbol.Delete,ExitBlockmap);
            var button=(MobileIconButton)blockmapCloseButton;button.showHint=null;button.holdAction=ExitBlockmap;
            var close=blockmapCloseButton.GetComponent<RectTransform>();close.anchorMin=close.anchorMax=close.pivot=Vector2.one;close.sizeDelta=new Vector2(52,52);close.anchoredPosition=new Vector2(-8,-8);
            blockmapRoot.gameObject.SetActive(false);
        }
        void EnterBlockmap()
        {
            if(BlockmapActive||SoundPropagationActive||AutomapActive||VisualActive)return;
            if(Draft.Count>0||Tool==MobileTool.Paste||ThingAimActive||MakingSector||CurvingLines||BridgingLines){Notify("Завершите или отмените текущую операцию перед просмотром блокмапы.");return;}
            CancelDrag();CloseDialog();CloseTexturePicker();EnsureClassicView();
            blockmapPreviousTool=Tool;blockmapPreviousKind=Kind;blockmapPreviousSelection.Clear();blockmapPreviousSelection.AddRange(Selection);
            blockmapPreviousBrush=selectionBrush;blockmapPreviousMass=massSelection;selectionBrush=SelectionBrush.Off;massSelection=false;Tool=MobileTool.Pan;
            BlockmapActive=true;BlockmapX=BlockmapY=-1;blockmapMap=null;blockmapHiddenUI.Clear();
            foreach(Transform child in safeRoot)if(child!=viewportRoot&&child!=blockmapRoot)HideBlockmapUI(child.gameObject);
            foreach(Transform child in viewportRoot)if(child!=View.transform)HideBlockmapUI(child.gameObject);
            blockmapRoot.gameObject.SetActive(true);blockmapRoot.SetAsLastSibling();ApplyToolbarLayout();Changed(false);
        }
        void HideBlockmapUI(GameObject target){blockmapHiddenUI[target]=target.activeSelf;target.SetActive(false);}
        void ExitBlockmap()
        {
            if(!BlockmapActive)return;CancelDrag();BlockmapActive=false;
            blockmapRoot.gameObject.SetActive(false);foreach(var pair in blockmapHiddenUI)if(pair.Key!=null)pair.Key.SetActive(pair.Value);blockmapHiddenUI.Clear();
            Tool=blockmapPreviousTool;Kind=blockmapPreviousKind;Selection.Clear();
            foreach(var item in blockmapPreviousSelection)if(item.index>=0&&item.index<MapSelection.Count(Map,item.kind))Selection.Add(item);blockmapPreviousSelection.Clear();
            selectionBrush=blockmapPreviousBrush;massSelection=blockmapPreviousMass;blockmapMap=null;BlockmapData=null;BlockmapX=BlockmapY=-1;
            ApplyToolbarLayout();Changed(false);
        }
        void LayoutBlockmap(){viewportRoot.offsetMin=viewportRoot.offsetMax=Vector2.zero;RefreshBlockmapData();View.SetVerticesDirty();}
        internal void RefreshBlockmapData()
        {
            if(ReferenceEquals(blockmapMap,Map)&&blockmapRevision==MapContentRevision)return;
            blockmapMap=Map;blockmapRevision=MapContentRevision;BlockmapData=new BlockmapPreview(Map);BlockmapX=BlockmapY=-1;View.SetVerticesDirty();
        }
        void CancelBlockmapPointer(){blockmapPointerActive=false;blockmapMoved=false;}
        void BeginBlockmapPointer(Vector2 point)
        {CancelDrag();if(!View.ScreenRect.Contains(point))return;blockmapPointerActive=true;blockmapPointerStart=point;blockmapPointerContext=CaptureOpenContext();}
        void MoveBlockmapPointer(Vector2 previous,Vector2 point)
        {if(!blockmapPointerActive)return;if(Vector2.Distance(blockmapPointerStart,point)>8)blockmapMoved=true;if(blockmapMoved)View.Pan(previous,point);}
        void EndBlockmapPointer(Vector2 point)
        {
            bool tap=blockmapPointerActive&&!blockmapMoved&&Vector2.Distance(blockmapPointerStart,point)<=8;var context=blockmapPointerContext;CancelBlockmapPointer();
            if(!tap||!CurrentOpenContext(context)||!View.ScreenRect.Contains(point))return;
            RefreshBlockmapData();int x,y;bool inside=BlockmapData.TryCell(View.World(point),out x,out y);BlockmapX=inside?x:-1;BlockmapY=inside?y:-1;View.SetVerticesDirty();
        }
    }
}
