// GPL-3.0. Footer overflow keeps original buttons, listeners, flags and hold behavior.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        sealed class ToolbarOverflowGroup
        {
            public RectTransform group,row,stash;
            public Button more;
            public readonly List<RectTransform> commands=new List<RectTransform>();
            public int visibleCount,totalCount,signature;
        }
        readonly List<ToolbarOverflowGroup> overflowGroups=new List<ToolbarOverflowGroup>();
        ToolbarOverflowGroup openOverflow;
        RectTransform toolbarOverflowPanel,toolbarOverflowRow;
        int toolbarOverflowFrame;
        bool toolbarLayoutInProgress;
        void RegisterToolbarOverflow(RectTransform group,RectTransform row)
        {
            var state=new ToolbarOverflowGroup{group=group,row=row,stash=Rect("Hidden toolbar commands",group)};
            state.stash.gameObject.SetActive(false);overflowGroups.Add(state);
            state.more=IconButton(row,"Ещё значки · открыть дополнительную панель",EditorIcon.Symbol.Overflow,()=>ToggleToolbarOverflow(state));
            state.more.gameObject.SetActive(false);
        }
        bool SyncToolbarOverflowGroups()
        {
            bool layoutChanged=false;
            foreach(var state in overflowGroups)
            {
                for(int i=0;i<state.row.childCount;i++)
                {
                    var command=(RectTransform)state.row.GetChild(i);
                    if(command==state.more.transform||state.commands.Contains(command))continue;
                    state.commands.Add(command);var button=command.GetComponent<Button>();
                    if(button!=null)button.onClick.AddListener(()=>{if(openOverflow!=null)CloseToolbarOverflow();});
                }
                int total=0,signature=1;
                foreach(var command in state.commands){if(command.gameObject.activeSelf)total++;unchecked{signature=signature*31+(command.gameObject.activeSelf?command.GetInstanceID():0);}}
                if(state.signature!=signature&&openOverflow!=null)CloseToolbarOverflow();
                int visible=total>9?10:Mathf.Max(1,total);layoutChanged|=visible!=state.visibleCount;
                state.totalCount=total;state.signature=signature;state.visibleCount=visible;
                int rank=0;
                foreach(var command in state.commands)
                {
                    bool primary=command.gameObject.activeSelf&&rank++<9;
                    Transform destination=primary?state.row:openOverflow==state&&command.gameObject.activeSelf?toolbarOverflowRow:state.stash;
                    if(command.parent!=destination)command.SetParent(destination,false);
                    if(primary)command.SetAsLastSibling();
                }
                state.more.gameObject.SetActive(total>9);state.more.transform.SetAsLastSibling();
            }
            return layoutChanged;
        }
        int ToolbarButtonCount(RectTransform group)
        {foreach(var state in overflowGroups)if(state.group==group)return state.visibleCount;return 1;}
        void ToggleToolbarOverflow(ToolbarOverflowGroup state)
        {
            bool close=openOverflow==state;CloseToolbarOverflow();if(close||state.totalCount<=9)return;
            CancelDrag();openOverflow=state;toolbarOverflowFrame=Time.frameCount;
            toolbarOverflowPanel=Rect("Дополнительные значки · "+state.group.name,safeRoot);
            var background=toolbarOverflowPanel.gameObject.AddComponent<Image>();background.color=new Color32(43,61,82,255);
            var dismiss=toolbarOverflowPanel.gameObject.AddComponent<Button>();dismiss.targetGraphic=background;dismiss.onClick.AddListener(()=>CloseToolbarOverflow());
            var caption=Label(toolbarOverflowPanel,"ЕЩЁ",12);caption.verticalOverflow=VerticalWrapMode.Overflow;caption.color=new Color32(147,168,188,255);caption.alignment=TextAnchor.MiddleLeft;
            var labelRect=caption.rectTransform;labelRect.anchorMin=new Vector2(0,1);labelRect.anchorMax=Vector2.one;labelRect.pivot=new Vector2(.5f,1);labelRect.sizeDelta=new Vector2(-16,14);
            toolbarOverflowRow=Rect("Hidden buttons",toolbarOverflowPanel);Stretch(toolbarOverflowRow);toolbarOverflowRow.offsetMin=new Vector2(8,8);toolbarOverflowRow.offsetMax=new Vector2(-8,-18);
            int hidden=state.totalCount-9;
            float available=safeRoot.rect.width-16;
            int columns=Mathf.Min(hidden,Mathf.Max(1,Mathf.Min(5,Mathf.FloorToInt((available-16+4)/52))));
            int rows=(hidden+columns-1)/columns;
            var grid=toolbarOverflowRow.gameObject.AddComponent<GridLayoutGroup>();grid.cellSize=new Vector2(48,50);grid.spacing=new Vector2(4,4);grid.constraint=GridLayoutGroup.Constraint.FixedColumnCount;grid.constraintCount=columns;grid.childAlignment=TextAnchor.UpperCenter;
            float width=16+columns*48+(columns-1)*4,height=26+rows*50+(rows-1)*4;
            toolbarOverflowPanel.anchorMin=toolbarOverflowPanel.anchorMax=toolbarOverflowPanel.pivot=Vector2.zero;toolbarOverflowPanel.sizeDelta=new Vector2(width,height);
            var corners=new Vector3[4];state.group.GetWorldCorners(corners);var right=safeRoot.InverseTransformPoint(corners[2]);
            float x=right.x-safeRoot.rect.xMin-width,y=right.y-safeRoot.rect.yMin+6;
            toolbarOverflowPanel.anchoredPosition=new Vector2(Mathf.Clamp(x,8,Mathf.Max(8,safeRoot.rect.width-width-8)),Mathf.Clamp(y,8,Mathf.Max(8,safeRoot.rect.height-height-8)));
            state.more.GetComponent<Image>().color=Accent;SyncToolbarOverflowGroups();Canvas.ForceUpdateCanvases();
        }
        bool CloseToolbarOverflow()
        {
            if(openOverflow==null)return false;var state=openOverflow;openOverflow=null;
            foreach(var command in state.commands)if(command.parent==toolbarOverflowRow)command.SetParent(state.stash,false);
            state.more.GetComponent<Image>().color=Panel;
            if(toolbarOverflowPanel!=null){toolbarOverflowPanel.gameObject.SetActive(false);Destroy(toolbarOverflowPanel.gameObject);}
            toolbarOverflowPanel=toolbarOverflowRow=null;return true;
        }
        void DismissToolbarOverflowAt(Vector2 point)
        {
            if(openOverflow==null)return;
            if(RectTransformUtility.RectangleContainsScreenPoint(toolbarOverflowPanel,point)||RectTransformUtility.RectangleContainsScreenPoint(openOverflow.more.GetComponent<RectTransform>(),point))return;
            CloseToolbarOverflow();
        }
        void UpdateToolbarOverflow()
        {
            if(openOverflow==null)return;
            if(!openOverflow.group.gameObject.activeInHierarchy||ModalOpen||texturePicker!=null){CloseToolbarOverflow();return;}
            if(toolbarOverflowFrame==Time.frameCount)return;
            var mouse=Mouse.current;if(mouse!=null&&(mouse.leftButton.wasPressedThisFrame||mouse.rightButton.wasPressedThisFrame))DismissToolbarOverflowAt(mouse.position.ReadValue());
            foreach(var touch in Touch.activeTouches)if(touch.phase==UnityEngine.InputSystem.TouchPhase.Began)DismissToolbarOverflowAt(touch.screenPosition);
        }
    }
}
