using System.Collections.Generic;
using MobileUDB.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        Button visualAlignTextureButton,visualUpperUnpeggedButton,visualLowerUnpeggedButton,visualTextureControlsButton;
        WallFlagStateGraphic visualUpperFlag,visualLowerFlag;
        RectTransform visualTextureControls;
        readonly List<Button> visualTextureArrows=new List<Button>();
        readonly List<Button> visualTextureSteps=new List<Button>();
        Text visualTextureOffsetCaption;
        Button visualTexturePanelClose,visualTextureCycleStep;
        bool? visualTextureCompact;
        bool visualTextureControlsOpen;

        // -2: no walls, -1: mixed, 0: clear, 1: set. A linedef is counted once.
        int VisualWallFlagState(int flag)
        {
            int state=-2;var lines=new HashSet<int>();
            foreach(var id in visualView.SelectedSurfaces)
            {
                if(id.IsFlat||!SurfaceTextureEdits.Valid(Map,id)||!lines.Add(id.line))continue;
                int next=(Map.lines[id.line].flags&flag)!=0?1:0;
                if(state==-2)state=next;else if(state!=next)return -1;
            }
            return state;
        }
        void BuildVisualTextureToolbar(Transform parent)
        {
            visualAlignTextureButton=IconButton(parent,"Выровнять одинаковые текстуры · X и Y",EditorIcon.Symbol.Texture,()=>
            {var wall=VisualFlagWall();if(wall.HasValue)AlignWallTexture(wall.Value,true,true);});Badge(visualAlignTextureButton.transform,"=",false);
            visualUpperUnpeggedButton=VisualUnpeggedButton(parent,8,out visualUpperFlag);
            visualLowerUnpeggedButton=VisualUnpeggedButton(parent,16,out visualLowerFlag);
        }
        Button VisualUnpeggedButton(Transform parent,int flag,out WallFlagStateGraphic state)
        {
            var button=IconButton(parent,flag==8?"Upper Unpegged · не крепить верх":"Lower Unpegged · не крепить низ",flag==8?EditorIcon.Symbol.UpperUnpegged:EditorIcon.Symbol.LowerUnpegged,()=>VisualToggleWallUnpegged(flag));
            var icon=button.GetComponentInChildren<EditorIcon>().rectTransform;icon.sizeDelta=new Vector2(26,32);icon.anchoredPosition=new Vector2(-6,0);
            var square=Rect("Флаг · "+flag,button.transform);square.anchorMin=square.anchorMax=square.pivot=Vector2.one;square.sizeDelta=new Vector2(20,20);square.anchoredPosition=new Vector2(-2,-2);
            state=square.gameObject.AddComponent<WallFlagStateGraphic>();state.color=new Color32(255,218,127,255);state.raycastTarget=false;return button;
        }
        void BuildVisualTextureControls(RectTransform viewport)
        {
            visualTextureControls=Rect("Ручное выравнивание текстуры",viewport);
            visualTextureControls.anchorMin=visualTextureControls.anchorMax=new Vector2(1,.5f);visualTextureControls.pivot=new Vector2(1,.5f);visualTextureControls.sizeDelta=new Vector2(172,254);visualTextureControls.anchoredPosition=new Vector2(-8,0);
            visualTextureControls.gameObject.AddComponent<Image>().color=new Color32(18,28,41,245);
            var title=Label(visualTextureControls,"ТЕКСТУРА · XY",12);title.rectTransform.anchorMin=title.rectTransform.anchorMax=new Vector2(.5f,1);title.rectTransform.sizeDelta=new Vector2(160,20);title.rectTransform.anchoredPosition=new Vector2(0,-12);
            visualTextureOffsetCaption=Label(visualTextureControls,"",12);var caption=visualTextureOffsetCaption.rectTransform;caption.anchorMin=caption.anchorMax=new Vector2(.5f,1);caption.sizeDelta=new Vector2(160,20);caption.anchoredPosition=new Vector2(0,-32);
            foreach(int step in new[]{1,8,0})
            {
                int choice=step;var button=Button(visualTextureControls,step==0?"Сетка":step.ToString(),()=>{wallOffsetStep=choice;RefreshVisualTextureControls();});
                var r=button.GetComponent<RectTransform>();r.anchorMin=r.anchorMax=new Vector2(0,1);r.pivot=new Vector2(0,1);r.sizeDelta=new Vector2(50,48);r.anchoredPosition=new Vector2(8+visualTextureSteps.Count*53,-44);button.GetComponentInChildren<Text>().fontSize=13;visualTextureSteps.Add(button);
            }
            TextureControlArrow(new Vector2Int(0,-1),new Vector2(0,-98),0,"Текстуру вверх");
            TextureControlArrow(new Vector2Int(-1,0),new Vector2(-52,-150),90,"Текстуру влево");
            TextureControlArrow(new Vector2Int(1,0),new Vector2(52,-150),-90,"Текстуру вправо");
            TextureControlArrow(new Vector2Int(0,1),new Vector2(0,-202),180,"Текстуру вниз");
            var close=Button(visualTextureControls,"×",()=>{CloseVisualTextureControls();Changed(false);});close.name="Закрыть ручное выравнивание";var cr=close.GetComponent<RectTransform>();cr.anchorMin=cr.anchorMax=new Vector2(.5f,1);cr.pivot=new Vector2(.5f,1);cr.sizeDelta=new Vector2(48,48);cr.anchoredPosition=new Vector2(0,-150);
            visualTexturePanelClose=close;
            visualTextureCycleStep=Button(visualTextureControls,"",()=>{wallOffsetStep=wallOffsetStep==1?8:wallOffsetStep==8?0:1;RefreshVisualTextureControls();});visualTextureCycleStep.name="Шаг ручного выравнивания";
            var stepRect=visualTextureCycleStep.GetComponent<RectTransform>();stepRect.anchorMin=stepRect.anchorMax=new Vector2(.5f,1);stepRect.pivot=new Vector2(.5f,1);stepRect.sizeDelta=new Vector2(48,48);stepRect.anchoredPosition=new Vector2(0,-80);visualTextureCycleStep.GetComponentInChildren<Text>().fontSize=12;visualTextureCycleStep.gameObject.SetActive(false);
            visualTextureControls.gameObject.SetActive(false);
        }
        void TextureControlArrow(Vector2Int direction,Vector2 position,float angle,string title)
        {
            var rect=Rect(title,visualTextureControls);var image=rect.gameObject.AddComponent<Image>();image.color=Panel;
            var button=rect.gameObject.AddComponent<TextureOffsetRepeatButton>();button.targetGraphic=image;
            OpenContext context=null;VisualView.SelectionSnapshot marks=null;
            button.begin=()=>{context=CaptureOpenContext();marks=visualView.SnapshotSelection();};
            button.valid=()=>VisualActive&&!ModalOpen&&context!=null&&CurrentOpenContext(context)&&SameVisualTextureSelection(marks);
            button.move=record=>
            {
                int amount=wallOffsetStep==0?Grid:wallOffsetStep;bool changed=VisualMoveWallTextures(direction.x*amount,direction.y*amount,record);
                if(changed){context=CaptureOpenContext();marks=visualView.SnapshotSelection();}return changed;
            };
            button.onClick.AddListener(()=>{button.begin();button.move(true);});
            var iconRect=Rect("Icon",rect);iconRect.anchorMin=iconRect.anchorMax=new Vector2(.5f,.5f);iconRect.sizeDelta=new Vector2(32,32);var icon=iconRect.gameObject.AddComponent<EditorIcon>();icon.symbol=EditorIcon.Symbol.Up;icon.color=Color.white;icon.raycastTarget=false;
            var colors=button.colors;colors.highlightedColor=new Color(1.15f,1.15f,1.15f);colors.pressedColor=new Color(.7f,.85f,.95f);colors.disabledColor=new Color(.35f,.35f,.35f);button.colors=colors;rect.anchorMin=rect.anchorMax=new Vector2(.5f,1);rect.pivot=new Vector2(.5f,1);rect.sizeDelta=new Vector2(48,48);rect.anchoredPosition=position;button.GetComponentInChildren<EditorIcon>().transform.localRotation=Quaternion.Euler(0,0,angle);visualTextureArrows.Add(button);
        }
        void ToggleVisualTextureControls()
        {
            if(!VisualActive)return;
            CancelDrag();CloseDialog();VisualBrush=VisualSelectionBrush.Off;visualTextureControlsOpen=!visualTextureControlsOpen;Changed(false);
        }
        void CloseVisualTextureControls()
        {visualTextureControlsOpen=false;if(visualTextureControls!=null)visualTextureControls.gameObject.SetActive(false);}
        void RefreshVisualTextureControls()
        {
            bool wall=VisualFlagWall().HasValue;
            visualAlignTextureButton.interactable=wall;
            RefreshVisualFlag(visualUpperUnpeggedButton,visualUpperFlag,8);
            RefreshVisualFlag(visualLowerUnpeggedButton,visualLowerFlag,16);
            visualTextureControlsButton.interactable=wall||visualTextureControlsOpen;visualTextureControlsButton.GetComponent<Image>().color=visualTextureControlsOpen?Accent:Panel;
            RefreshVisualTexturePanelVisibility();
            foreach(var button in visualTextureArrows)button.interactable=wall;
            for(int i=0;i<visualTextureSteps.Count;i++)visualTextureSteps[i].GetComponent<Image>().color=wallOffsetStep==(i==0?1:i==1?8:0)?Accent:Panel;
            visualTextureCycleStep.GetComponentInChildren<Text>().text="Шаг\n"+(wallOffsetStep==0?Grid.ToString():wallOffsetStep.ToString());
            LayoutVisualTextureControls();
            if(!visualTextureControlsOpen)return;
            int? x=null,y=null;bool mixedX=false,mixedY=false;
            foreach(var id in visualView.SelectedSurfaces)if(!id.IsFlat&&SurfaceTextureEdits.Valid(Map,id))
            {var side=Map.sides[id.side];if(x.HasValue){mixedX|=x.Value!=side.offsetX;mixedY|=y.Value!=side.offsetY;}else{x=side.offsetX;y=side.offsetY;}}
            visualTextureOffsetCaption.text=wall?"X "+(mixedX?"—":x.ToString())+" · Y "+(mixedY?"—":y.ToString()):"Выберите стену";
        }
        void LayoutVisualTextureControls()
        {
            if(visualTextureControls==null)return;
            bool compact=((RectTransform)visualTextureControls.parent).rect.height<270;
            if(visualTextureCompact==compact)return;visualTextureCompact=compact;
            visualTextureControls.sizeDelta=new Vector2(172,compact?184:254);
            visualTextureOffsetCaption.gameObject.SetActive(!compact);foreach(var step in visualTextureSteps)step.gameObject.SetActive(!compact);
            visualTexturePanelClose.gameObject.SetActive(!compact);visualTextureCycleStep.gameObject.SetActive(compact);
            var positions=compact?new[]{new Vector2(0,-28),new Vector2(-52,-80),new Vector2(52,-80),new Vector2(0,-132)}:new[]{new Vector2(0,-98),new Vector2(-52,-150),new Vector2(52,-150),new Vector2(0,-202)};
            for(int i=0;i<visualTextureArrows.Count;i++)visualTextureArrows[i].GetComponent<RectTransform>().anchoredPosition=positions[i];
        }
        void RefreshVisualTexturePanelVisibility()
        {if(visualTextureControls!=null)visualTextureControls.gameObject.SetActive(visualTextureControlsOpen&&VisualActive&&!ModalOpen);}
        void RefreshVisualFlag(Button button,WallFlagStateGraphic graphic,int flag)
        {int state=VisualWallFlagState(flag);button.interactable=state!=-2;graphic.State=state;graphic.color=new Color32(255,218,127,state==-2?(byte)80:(byte)255);}
    }
}
