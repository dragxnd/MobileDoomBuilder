using MobileUDB.Core;using UnityEngine;using UnityEngine.UI;
namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        void BuildFileMenu(RectTransform body)
        {
            body.GetComponent<VerticalLayoutGroup>().spacing=5;
            ToolsCaption(body,"КАРТЫ WAD");
            FileCommand(body,"Новая карта…",EditorIcon.Symbol.NewMap,NewMapMenu);
            FileCommand(body,"Открыть карту…",EditorIcon.Symbol.OpenMap,()=>ConfirmReplace(()=>GetComponent<MobileFileBridge>().OpenWad()));
            FileCommand(body,"Открыть карту из текущего WAD…",EditorIcon.Symbol.OpenMap,ChooseWadMap,archive!=null||editedMaps.Count>0);
            FileCommand(body,"Восстановить последнюю карту",EditorIcon.Symbol.Undo,RestoreLastMap,HasLastMapRecovery);
            FileCommand(body,"Закрыть карту",EditorIcon.Symbol.File,()=>ConfirmReplace(CloseWadMap));
            FileCommand(body,"Сохранить карту",EditorIcon.Symbol.SaveMap,()=>SaveWadMap(false));
            FileCommand(body,"Сохранить карту как…",EditorIcon.Symbol.SaveMap,()=>SaveWadMap(true));
            FileCommand(body,"Сохранить карту в…",EditorIcon.Symbol.SaveMap,()=>GetComponent<MobileFileBridge>().OpenWadForSaveInto());
            FileCommand(body,"Имя карты…",EditorIcon.Symbol.Properties,MapNameMenu);
            ToolsCaption(body,"РЕСУРСЫ И ПРОФИЛИ");var row=ShapeOptionRow(body);
            ToolsCommand(row,"Ресурсы WAD / PK3…","Ресурсы…",EditorIcon.Symbol.File,ResourcesMenu);
            ToolsCommand(row,"Проверить карту…","Проверить карту…",EditorIcon.Symbol.Check,CheckMap);
            row=ShapeOptionRow(body);ToolsCommand(row,"Игра / типы объектов · "+effectiveProfile+"…","Игра / типы…",EditorIcon.Symbol.Configuration,ProfileMenu);
            ToolsCommand(row,"Действия / движок · "+Actions.label+"…","Действия / движок…",EditorIcon.Symbol.Configuration,ActionProfileMenu);
            FileCommand(body,"Импорт…",EditorIcon.Symbol.OpenMap,()=>ShowDialog("Импорт",b=>FileCommand(b,"Настройки UDB (.dbs)…",EditorIcon.Symbol.OpenMap,PickDbsGroups,DbsGroupsReady)));
            FileCommand(body,"Экспорт…",EditorIcon.Symbol.SaveMap,()=>ShowDialog("Экспорт",b=>{FileCommand(b,"WAD без узлов…",EditorIcon.Symbol.SaveMap,()=>SaveWadMap(true,null,false));FileCommand(b,"Настройки UDB (.dbs)…",EditorIcon.Symbol.SaveMap,DbsExportMenu,DbsGroupsReady);}));
            FileCommand(body,"Настройки…",EditorIcon.Symbol.Configuration,SettingsMenu);
            FileCommand(body,"Выход",EditorIcon.Symbol.File,ExitMenu);
            ToolsCaption(body,"ПОСЛЕДНИЕ КАРТЫ");var recent=LoadRecentWads();
            for(int i=0;i<recent.Count;i++){var item=recent[i];var recentRow=ShapeOptionRow(body);var recentButton=ToolsCommand(recentRow,(i+1)+" · "+item.map+" · "+item.file.name,(i+1)+" · "+item.map+" · "+item.file.name,EditorIcon.Symbol.OpenMap,()=>ConfirmReplace(()=>GetComponent<MobileFileBridge>().OpenWadReference(item.file,item.map)));var recentText=(MobileLocalizedText)recentButton.GetComponentInChildren<Text>();recentText.SetUntranslated(true);}
            if(recent.Count==0)AddLabel(body,"Недавно открытых карт пока нет.");
        }
        void FileCommand(RectTransform body,string label,EditorIcon.Symbol icon,System.Action action,bool enabled=true)
        {ToolsCommand(ShapeOptionRow(body),label,label,icon,action,enabled);}
    }
}
