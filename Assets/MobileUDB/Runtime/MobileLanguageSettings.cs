// GPL-3.0. App-level language choice; defaults to English on a clean install.
using UnityEngine;
using UnityEngine.UI;
namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        void SettingsMenu()
        {
            ShowDialog("Настройки",body=>
            {
                var button=AddButton(body,"ЯЗЫК",LanguageMenu);button.name="Language settings";
                var scale=AddButton(body,"МАСШТАБ ИНТЕРФЕЙСА",InterfaceScaleMenu);scale.name="Interface scale settings";

            });
        }
        void LanguageMenu()
        {
            ShowDialog("Язык",body=>
            {
                for(int i=0;i<UiLanguage.Codes.Length;i++)
                {
                    string code=UiLanguage.Codes[i];var button=AddButton(body,UiLanguage.NativeNames[i],()=>{UiLanguage.Set(code);LanguageMenu();});button.name="Language."+code;
                    var label=(MobileLocalizedText)button.GetComponentInChildren<Text>();label.SetUntranslated(true);label.text=UiLanguage.NativeNames[i];label.alignment=TextAnchor.MiddleLeft;label.rectTransform.offsetMin=new Vector2(16,0);label.rectTransform.offsetMax=new Vector2(-48,0);
                    var rect=Rect("Selected language",button.transform);rect.anchorMin=rect.anchorMax=new Vector2(1,.5f);rect.pivot=new Vector2(1,.5f);rect.sizeDelta=new Vector2(24,24);rect.anchoredPosition=new Vector2(-14,0);
                    var check=rect.gameObject.AddComponent<WallFlagStateGraphic>();check.State=UiLanguage.Code==code?1:0;check.raycastTarget=false;check.color=new Color32(255,218,127,255);button.GetComponent<Image>().color=UiLanguage.Code==code?Accent:Panel;
                }
            });
        }
    }
}
