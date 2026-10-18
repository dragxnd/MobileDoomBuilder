// GPL-3.0. Touch-only preview of UDB's binary Sound Propagation Mode.
using System.Collections.Generic;
using MobileUDB.Core;
using UnityEngine;
using UnityEngine.UI;
namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        public bool SoundPropagationActive {get;private set;}
        internal int SoundSource {get;private set;}=-1;
        internal byte[] SoundLevels {get;private set;}
        MapDocument soundMap;long soundRevision=-1;SoundPropagation soundGraph;SelectionHitIndex soundHit;
        RectTransform soundRoot;Button soundCloseButton;Text soundHint;
        readonly Dictionary<GameObject,bool> soundHiddenUI=new Dictionary<GameObject,bool>();
        readonly List<ElementRef> soundPreviousSelection=new List<ElementRef>();
        MobileTool soundPreviousTool;ElementKind soundPreviousKind;SelectionBrush soundPreviousBrush;bool soundPreviousMass;
        bool soundPointerActive,soundMoved;Vector2 soundPointerStart;OpenContext soundPointerContext;
        void BuildSoundPropagationUI()
        {
            soundRoot=Rect("Sound propagation controls",safeRoot);Stretch(soundRoot);
            soundCloseButton=IconButton(soundRoot,"Закрыть режим звука",EditorIcon.Symbol.Delete,ExitSoundPropagation);
            var close=soundCloseButton.GetComponent<RectTransform>();close.anchorMin=close.anchorMax=close.pivot=Vector2.one;close.sizeDelta=new Vector2(52,52);close.anchoredPosition=new Vector2(-8,-8);
            var backdrop=Rect("Sound hint backdrop",soundRoot);backdrop.anchorMin=Vector2.zero;backdrop.anchorMax=new Vector2(1,0);backdrop.pivot=new Vector2(.5f,0);backdrop.sizeDelta=new Vector2(-16,64);backdrop.anchoredPosition=new Vector2(0,8);
            var background=backdrop.gameObject.AddComponent<Image>();background.color=new Color32(15,23,35,180);background.raycastTarget=false;
            soundHint=Label(backdrop,"",14);soundHint.raycastTarget=false;Stretch(soundHint.rectTransform);
            soundHint.rectTransform.offsetMin=new Vector2(8,4);soundHint.rectTransform.offsetMax=new Vector2(-8,-4);
            soundRoot.gameObject.SetActive(false);
        }
        void EnterSoundPropagation()
        {
            if(SoundPropagationActive||AutomapActive||BlockmapActive||VisualActive)return;
            if(Draft.Count>0||Tool==MobileTool.Paste||ThingAimActive||MakingSector||CurvingLines||BridgingLines){Notify("Завершите или отмените текущую операцию перед просмотром звука.");return;}
            CancelDrag();CloseDialog();CloseTexturePicker();EnsureClassicView();
            soundPreviousTool=Tool;soundPreviousKind=Kind;soundPreviousSelection.Clear();soundPreviousSelection.AddRange(Selection);
            soundPreviousBrush=selectionBrush;soundPreviousMass=massSelection;selectionBrush=SelectionBrush.Off;massSelection=false;Tool=MobileTool.Pan;
            SoundPropagationActive=true;SoundSource=-1;soundMap=null;soundHiddenUI.Clear();
            foreach(Transform child in safeRoot)if(child!=viewportRoot&&child!=soundRoot)HideSoundUI(child.gameObject);
            foreach(Transform child in viewportRoot)if(child!=View.transform)HideSoundUI(child.gameObject);
            soundRoot.gameObject.SetActive(true);soundRoot.SetAsLastSibling();ApplyToolbarLayout();Changed(false);
        }
        void HideSoundUI(GameObject target){soundHiddenUI[target]=target.activeSelf;target.SetActive(false);}
        void ExitSoundPropagation()
        {
            if(!SoundPropagationActive)return;CancelDrag();SoundPropagationActive=false;
            soundRoot.gameObject.SetActive(false);foreach(var pair in soundHiddenUI)if(pair.Key!=null)pair.Key.SetActive(pair.Value);soundHiddenUI.Clear();
            Tool=soundPreviousTool;Kind=soundPreviousKind;Selection.Clear();
            foreach(var item in soundPreviousSelection)if(item.index>=0&&item.index<MapSelection.Count(Map,item.kind))Selection.Add(item);soundPreviousSelection.Clear();
            selectionBrush=soundPreviousBrush;massSelection=soundPreviousMass;soundMap=null;soundGraph=null;soundHit=null;SoundLevels=null;SoundSource=-1;
            ApplyToolbarLayout();Changed(false);Notify("Просмотр распространения звука закрыт.");
        }
        void LayoutSoundPropagation(){viewportRoot.offsetMin=viewportRoot.offsetMax=Vector2.zero;RefreshSoundPropagationUI();View.SetVerticesDirty();}
        internal void RefreshSoundData()
        {
            if(ReferenceEquals(soundMap,Map)&&soundRevision==MapContentRevision)return;
            if(!ReferenceEquals(soundMap,Map))SoundSource=-1;
            soundMap=Map;soundRevision=MapContentRevision;soundGraph=new SoundPropagation(Map);soundHit=new SelectionHitIndex(Map,ElementKind.Sector);
            if(SoundSource>=Map.sectors.Count)SoundSource=-1;SoundLevels=soundGraph.Levels(SoundSource);View.SetVerticesDirty();RefreshSoundHint();
        }
        void RefreshSoundPropagationUI(){RefreshSoundData();RefreshSoundHint();}
        void RefreshSoundHint()
        {
            soundHint.text=SoundSource<0?"Коснитесь сектора — источника звука\nОдин палец — обзор · два — масштаб":
                "Источник: сектор "+SoundSource+"\n<color=#48F579>Зелёный: свободно</color> · <color=#FFDE4A>жёлтый: через Block Sound</color>";
        }
        void CancelSoundPointer(){soundPointerActive=false;soundMoved=false;}
        void BeginSoundPointer(Vector2 point)
        {CancelDrag();if(!View.ScreenRect.Contains(point))return;soundPointerActive=true;soundPointerStart=point;soundPointerContext=CaptureOpenContext();}
        void MoveSoundPointer(Vector2 previous,Vector2 point)
        {if(!soundPointerActive)return;if(Vector2.Distance(soundPointerStart,point)>8)soundMoved=true;if(soundMoved)View.Pan(previous,point);}
        void EndSoundPointer(Vector2 point)
        {
            bool tap=soundPointerActive&&!soundMoved&&Vector2.Distance(soundPointerStart,point)<=8;var context=soundPointerContext;CancelSoundPointer();
            if(!tap||!CurrentOpenContext(context)||!View.ScreenRect.Contains(point))return;
            RefreshSoundData();var hit=soundHit.Hit(View.World(point),0);SoundSource=hit.HasValue?hit.Value.index:-1;
            SoundLevels=soundGraph.Levels(SoundSource);RefreshSoundHint();View.SetVerticesDirty();
        }
    }
}
