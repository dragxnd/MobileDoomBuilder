using System;using System.Collections.Generic;using MobileUDB.Core;using UnityEngine;using UnityEngine.UI;
namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        List<MapDocument> DocumentMaps()
        {var maps=new List<MapDocument>{Map};string key=MapNaming.Key(Map);foreach(var pair in editedMaps)if(!string.Equals(pair.Key,key,StringComparison.OrdinalIgnoreCase))maps.Add(pair.Value);return maps;}
        void MapNameMenu()
        {
            if(Draft.Count!=0||Tool==MobileTool.Paste){Notify("Сначала завершите или отмените построение.");return;}
            CancelDrag();var context=CaptureOpenContext();
            ShowDialog("Имя карты",body=>
            {
                ToolsCaption(body,"КАРТА · "+Map.format);var field=Field(body,sourceMapPackage==null?"Имя в WAD":"Имя в PK3",Map.name,false);field.characterLimit=8;
                AddLabel(body,"1–8 символов ASCII, например MAP03 или E1M2. Карта будет переименована в экспортируемой копии архива; доступна отмена.");
                AddLabel(body,"Ссылки на карту в MAPINFO / UMAPINFO и скриптах сохраняются как есть. Если они используют прежнее имя, обновите их отдельно.");
                var feedback=Label(body,"",16);feedback.alignment=TextAnchor.UpperLeft;feedback.gameObject.AddComponent<LayoutElement>().minHeight=48;feedback.gameObject.SetActive(false);
                DialogApply(body,()=>
                {
                    try
                    {
                        if(!CurrentOpenContext(context)||Draft.Count!=0||Tool==MobileTool.Paste)throw new InvalidOperationException("Карта или проект изменились. Откройте имя карты снова.");
                        string name=MapNaming.Normalize(field.text,sourceMapPackage!=null);
                        if(string.Equals(name,Map.name,StringComparison.OrdinalIgnoreCase)){Notify("Имя карты не изменено.");return true;}
                        var names=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
                        foreach(var map in DocumentMaps())names.Add(MapNaming.Key(map),ReferenceEquals(map,Map)?name:map.name);
                        (archive??new DoomWad()).ValidateMapNames(names);
                        if(!Edit(map=>MapNaming.Rename(map,name,sourceMapPackage!=null),"Карта переименована: "+name+". Доступна отмена."))return false;
                        RebuildThingCatalog();View.RefreshFill();if(visualView!=null)visualView.RequestRebuild();return true;
                    }
                    catch(Exception error){feedback.text=error.Message;feedback.gameObject.SetActive(true);Notify(error.Message);return false;}
                },"Переименовать",true);
            });
        }
        string DisplayMapName(string source)
        {if(string.Equals(source,MapNaming.Key(Map),StringComparison.OrdinalIgnoreCase))return Map.name;MapDocument saved;return editedMaps.TryGetValue(source,out saved)?saved.name:source;}
    }
}
