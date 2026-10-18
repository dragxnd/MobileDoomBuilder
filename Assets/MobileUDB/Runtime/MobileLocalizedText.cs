// GPL-3.0. Keep canonical UI text separate from its translated display value.
using UnityEngine.UI;
namespace MobileUDB
{
    public sealed class MobileLocalizedText : Text
    {
        string source;bool untranslated;
        public string Source {get{return source??base.text;}}
        public override string text {get{return base.text;}set{source=value??string.Empty;RefreshLanguage();}}
        public void SetUntranslated(bool value){untranslated=value;RefreshLanguage();}
        void RefreshLanguage(){if(source==null)source=base.text;base.text=untranslated?source:UiLanguage.Translate(source);}
        protected override void OnEnable(){base.OnEnable();UiLanguage.Changed+=RefreshLanguage;RefreshLanguage();}
        protected override void OnDisable(){UiLanguage.Changed-=RefreshLanguage;base.OnDisable();}
    }
}
