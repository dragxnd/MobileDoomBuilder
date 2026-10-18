using System;
using UnityEngine;
using UnityEngine.UI;

namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        Action fileReadCancel;
        public Text ShowFileReadProgress(Action cancel)
        {
            Text progress = null;
            ShowDialog("Чтение файла",body =>
            {
                progress = Label(body,"Чтение…",20); progress.gameObject.AddComponent<LayoutElement>().minHeight = 100;
                AddButton(body,"Отменить",CloseDialog);
                AddLabel(body,"Чтение выполняется в фоне. Отмена сохраняет текущую карту и ресурсы.");
            });
            fileReadCancel = cancel; return progress;
        }
        void CancelFileRead()
        { var cancel = fileReadCancel; fileReadCancel = null; if (cancel != null) cancel(); }
        public void CloseFileReadProgress() { fileReadCancel = null; CloseDialog(); }
    }
}
