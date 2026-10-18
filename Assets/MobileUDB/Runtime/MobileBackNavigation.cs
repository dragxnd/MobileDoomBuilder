using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        int lastSystemBackFrame=-1;
        float keyboardBackUntil=-1;
        internal void TextKeyboardBackHandled()
        {
            lastSystemBackFrame=Time.frameCount;keyboardBackUntil=Time.unscaledTime+.2f;
            var navigation=GetComponent<TouchNavigation>();if(navigation!=null)navigation.CancelForLayout();
            if(modal!=null){var layout=modal.GetComponent<MobileKeyboardLayout>();if(layout!=null)layout.ApplyOcclusion(0);}
        }
        void ReleaseDialogInput(Transform root)
        {
            if(root==null)return;
            foreach(var input in root.GetComponentsInChildren<InputField>(true))
            {var mobile=input as MobileInputField;if(mobile!=null)mobile.Dismiss();else if(input.isFocused)input.DeactivateInputField();}
            if(EventSystem.current!=null)
            {var selected=EventSystem.current.currentSelectedGameObject;if(selected!=null&&selected.transform.IsChildOf(root))EventSystem.current.SetSelectedGameObject(null);}
            var navigation=GetComponent<TouchNavigation>();if(navigation!=null)navigation.CancelForLayout();
        }
        bool DismissTextForBack()
        {
            if(Time.unscaledTime<keyboardBackUntil)return true;
            var selected=EventSystem.current==null?null:EventSystem.current.currentSelectedGameObject;
            var field=selected==null?null:selected.GetComponent<InputField>();
            // Native IME can deselect before the Android Back key reaches Unity.
            // Also handle queued activation, before isFocused becomes true.
            if(field==null&&modal!=null)foreach(var candidate in modal.GetComponentsInChildren<InputField>())if(candidate.isFocused){field=candidate;break;}
            if(field==null||!field.isFocused&&!(field is MobileInputField&&selected==field.gameObject))return false;
            var mobile=field as MobileInputField;
            if(mobile!=null)mobile.Dismiss();else
            {string pending=field.text;field.DeactivateInputField();field.SetTextWithoutNotify(pending);if(EventSystem.current!=null)EventSystem.current.SetSelectedGameObject(null);}
            TextKeyboardBackHandled();return true;
        }
        public void HandleBack()
        {
            // EventSystem and TouchNavigation can observe the same key in either order.
            if(lastSystemBackFrame==Time.frameCount)return;lastSystemBackFrame=Time.frameCount;
            if(DismissTextForBack())return;
            if(BlockmapActive){ExitBlockmap();return;}
            if(SoundPropagationActive){ExitSoundPropagation();return;}
            if(AutomapActive){ExitAutomap();return;}
            if(CloseToolbarOverflow())return;
            if(CancelAreaSelection()){Notify("Рамка отменена.");return;}
            CancelDrag();
            if (texturePicker != null) { CloseTexturePicker(); return; }
            if (ModalOpen) { CloseDialog(); return; }
            if (PaintingSelection) { DisablePaintSelection(); return; }
            if (PaintingVisualSelection) { DisableVisualPaintSelection(); return; }
            if (visualTextureControlsOpen) { CloseVisualTextureControls(); Changed(false); return; }
            if (Tool == MobileTool.Paste) { CancelPaste(); Changed(false); Notify("Вставка отменена."); return; }
            if (Draft.Count > 0 || Tool == MobileTool.DrawRoom || Tool == MobileTool.CutSector) { CancelDraft(); return; }
            if (Tool == MobileTool.Transform) { SetTool(MobileTool.Select); Notify("Преобразование закрыто. Применённые изменения доступны в Undo."); return; }
            if (VisualActive) { ExitVisual(); Changed(false); return; }
            if (Tool != MobileTool.Select) { SetTool(MobileTool.Select); return; }
            ExitMenu();
        }
        void ExitMenu()
        {ConfirmReplace(()=>{SaveRecovery();CloseDialog();Application.Quit();});}
    }
}
