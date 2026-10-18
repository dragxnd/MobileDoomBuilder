// GPL-3.0. Include the complete dynamic CJK font in Android builds.
using UnityEditor;
namespace MobileUDB.Editor
{
    public sealed class MobileLocalizationFontImporter : AssetPostprocessor
    {
        void OnPreprocessFont()
        {
            if(!assetPath.EndsWith("/Localization/NotoSansCJKsc-Regular.otf"))return;
            var importer=(TrueTypeFontImporter)assetImporter;importer.fontTextureCase=FontTextureCase.Dynamic;importer.includeFontData=true;importer.fontSize=20;
        }
    }
}
