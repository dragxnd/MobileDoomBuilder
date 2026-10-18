using UnityEngine;using UnityEngine.UI;using UnityEngine.EventSystems;
namespace MobileUDB
{
    // Native dropdown flips upward when required. Limit its height to the space
    // actually available so that both portrait and short landscape screens fit.
    public sealed class TextureCategoryDropdown : Dropdown
    {
        void FitTemplate()
        {
            if(template==null)return;var rect=(RectTransform)transform;var canvas=GetComponentInParent<Canvas>();if(canvas==null)return;
            var corners=new Vector3[4];var canvasCorners=new Vector3[4];rect.GetWorldCorners(corners);((RectTransform)canvas.rootCanvas.transform).GetWorldCorners(canvasCorners);
            float scale=(corners[2].y-corners[0].y)/Mathf.Max(1,rect.rect.height);
            float below=(corners[0].y-canvasCorners[0].y)/Mathf.Max(.001f,scale)-8,above=(canvasCorners[2].y-corners[2].y)/Mathf.Max(.001f,scale)-8;
            var size=template.sizeDelta;size.y=Mathf.Clamp(Mathf.Max(below,above),56,280);template.sizeDelta=size;
        }
        public override void OnPointerClick(PointerEventData data){FitTemplate();base.OnPointerClick(data);}
        public override void OnSubmit(BaseEventData data){FitTemplate();base.OnSubmit(data);}
    }
}
