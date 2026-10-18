using MobileUDB.Core;using UnityEngine;using UnityEngine.UI;
namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        Button floorTextureToggle;
        WallFlagStateGraphic floorTextureCheck;
        ClassicViewMode floorTexturePrevious=ClassicViewMode.Wireframe;
        void BuildFloorTextureToggle(RectTransform parent)
        {
            floorTextureToggle=IconButton(parent,"Текстуры полов в 2D · включить или выключить",EditorIcon.Symbol.ViewTextureFloor,ToggleFloorTextures);
            var icon=floorTextureToggle.GetComponentInChildren<EditorIcon>().rectTransform;icon.sizeDelta=new Vector2(26,32);icon.anchoredPosition=new Vector2(-6,0);
            var square=Rect("Текстуры полов · флажок",floorTextureToggle.transform);square.anchorMin=square.anchorMax=square.pivot=Vector2.one;square.sizeDelta=new Vector2(20,20);square.anchoredPosition=new Vector2(-2,-2);
            floorTextureCheck=square.gameObject.AddComponent<WallFlagStateGraphic>();floorTextureCheck.color=new Color32(255,218,127,255);floorTextureCheck.raycastTarget=false;RefreshFloorTextureToggle();
        }
        void ToggleFloorTextures()
        {
            if(VisualActive)return;CancelDrag();
            if(View.mode==ClassicViewMode.Floors)View.mode=floorTexturePrevious;
            else {floorTexturePrevious=View.mode;View.mode=ClassicViewMode.Floors;}
            View.RefreshFill();RefreshFloorTextureToggle();Notify(View.mode==ClassicViewMode.Floors?"Текстуры полов включены.":"Текстуры полов выключены.");
        }
        void RefreshFloorTextureToggle()
        {
            if(floorTextureToggle==null||View==null)return;bool enabled=View.mode==ClassicViewMode.Floors;
            floorTextureToggle.interactable=!VisualActive;floorTextureToggle.GetComponent<Image>().color=enabled?Accent:Panel;floorTextureCheck.State=enabled?1:0;
        }
        internal long MapContentRevision{get{return recoveryRevision.Current;}}
        void ClassicViewMenu()
        {
            ShowDialog("Вид карты",body=>
            {
                ToolsCaption(body,"ЗАЛИВКА СЕКТОРОВ");var row=ShapeOptionRow(body);
                ViewModeButton(row,ClassicViewMode.Wireframe,"Линии",EditorIcon.Symbol.ViewNormal);
                ViewModeButton(row,ClassicViewMode.Brightness,"Свет",EditorIcon.Symbol.ViewBrightness);
                row=ShapeOptionRow(body);ViewModeButton(row,ClassicViewMode.Floors,"Пол",EditorIcon.Symbol.ViewTextureFloor);
                ViewModeButton(row,ClassicViewMode.Ceilings,"Потолок",EditorIcon.Symbol.ViewTextureCeiling);
                ToolsCaption(body,"ОСВЕЩЕНИЕ ТЕКСТУР");row=ShapeOptionRow(body);
                ToolsCommand(row,"Полная яркость: "+(View.fullBright?"Да":"Нет"),"Без затемнения: "+(View.fullBright?"Да":"Нет"),EditorIcon.Symbol.ViewBrightness,()=>{ToggleDisplayBrightness();ClassicViewMenu();});
                AddLabel(body,"Пол и потолок используют подключённые ресурсы. Клетчатая заливка означает недоступную текстуру. Режим «Свет» всегда показывает яркость секторов. Эти настройки меняют только вид.");
            });
        }
        void ViewModeButton(UnityEngine.RectTransform row,ClassicViewMode mode,string title,EditorIcon.Symbol icon)
        {ToolsCommand(row,title,(View.mode==mode?"✓ ":"")+title,icon,()=>{View.mode=mode;View.RefreshFill();CloseDialog();});}
    }
}
