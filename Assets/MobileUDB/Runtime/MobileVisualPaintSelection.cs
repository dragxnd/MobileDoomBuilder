using UnityEngine;
using UnityEngine.UI;

namespace MobileUDB
{
    internal enum VisualSelectionBrush { Off, Add, Remove, Toggle }

    public sealed partial class MobileEditor
    {
        internal VisualSelectionBrush VisualBrush { get; private set; }
        public bool PaintingVisualSelection { get { return VisualActive && VisualBrush != VisualSelectionBrush.Off; } }
        Button visualPaintStop;
        RectTransform visualPaintControls;
        Text visualPaintCaption;
        string VisualPaintLabel { get { return VisualBrush == VisualSelectionBrush.Add ? "КИСТЬ +" : VisualBrush == VisualSelectionBrush.Remove ? "КИСТЬ −" : "КИСТЬ ±"; } }

        void BuildVisualPaintControls(RectTransform viewport)
        {
            visualPaintControls = Rect("Кисть 3D", viewport);
            visualPaintControls.anchorMin = visualPaintControls.anchorMax = visualPaintControls.pivot = Vector2.one;
            visualPaintControls.sizeDelta = new Vector2(72,80); visualPaintControls.anchoredPosition = new Vector2(-12,-12);
            visualPaintCaption = Label(visualPaintControls,"",12);
            visualPaintCaption.alignment = TextAnchor.MiddleCenter;
            var caption = visualPaintCaption.rectTransform; caption.anchorMin = new Vector2(0,1); caption.anchorMax = Vector2.one;
            caption.pivot = new Vector2(.5f,1); caption.sizeDelta = new Vector2(0,20); caption.anchoredPosition = Vector2.zero;
            visualPaintStop = IconButton(visualPaintControls,"Выключить кисть 3D",EditorIcon.Symbol.Select,DisableVisualPaintSelection);
            var button = visualPaintStop.GetComponent<RectTransform>(); button.anchorMin = button.anchorMax = button.pivot = new Vector2(.5f,0);
            button.sizeDelta = new Vector2(56,56); button.anchoredPosition = Vector2.zero; Badge(button,"×",false);
            visualPaintControls.gameObject.SetActive(false);
        }

        void RefreshVisualPaintControls()
        {
            if(visualPaintControls == null) return;
            visualPaintControls.gameObject.SetActive(PaintingVisualSelection);
            visualPaintCaption.text = VisualPaintLabel;
        }

        void VisualPaintSelectionMenu()
        {
            if(!VisualActive) return;
            var context = CaptureOpenContext();
            ShowDialog("Кисть выделения 3D", body =>
            {
                LineBatchCaption(body,"ВЫБОР ПРОВЕДЕНИЕМ"); var row = ShapeOptionRow(body);
                Button(row,"Добавлять +",()=>StartVisualPaintSelection(VisualSelectionBrush.Add,context));
                Button(row,"Убирать −",()=>StartVisualPaintSelection(VisualSelectionBrush.Remove,context));
                row = ShapeOptionRow(body);
                Button(row,"Переключать ±",()=>StartVisualPaintSelection(VisualSelectionBrush.Toggle,context));
                Button(row,"Обычный выбор",()=>
                {
                    if(!VisualActive || !CurrentOpenContext(context)){Notify("Карта изменилась. Откройте кисть снова.");return;}
                    CloseDialog(); DisableVisualPaintSelection();
                });
                var note = Label(body,"Начните на стене, полу / потолке или объекте. Каждый проход выбирает только этот тип. Два пальца / Alt — движение камеры. × или Back выключает кисть, сохраняя отметки.",16);
                note.gameObject.AddComponent<LayoutElement>().preferredHeight = 120;
            });
        }

        void StartVisualPaintSelection(VisualSelectionBrush brush,OpenContext context)
        {
            if(!VisualActive || !CurrentOpenContext(context)){Notify("Карта изменилась. Откройте кисть снова.");return;}
            CancelDrag(); CloseDialog(); CloseVisualTextureControls(); VisualBrush = brush; VisualMultipleSelection = true; Changed(false);
            Notify(VisualPaintLabel + ": проведите пальцем по элементам. Два пальца — обзор.");
        }

        void DisableVisualPaintSelection()
        {
            CancelDrag(); VisualBrush = VisualSelectionBrush.Off; Changed(false);
            Notify("Кисть выключена. Касание выбирает, движение пальца поворачивает камеру.");
        }
    }
}
