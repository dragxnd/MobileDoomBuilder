using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using MobileUDB.Core;
using UnityEngine;
namespace MobileUDB
{
    [Serializable] public sealed class WadReference { public string path,uri,tree,name; public bool Native {get{return !string.IsNullOrEmpty(uri);}} }
    [Serializable] public sealed class RecentWad { public WadReference file;public string map; }
    [Serializable] sealed class RecentWads { public List<RecentWad> items=new List<RecentWad>(); }
    public sealed partial class MobileEditor
    {
        const string RecentWadKey="MobileUDB.RecentWads.v1";
        WadReference wadReference;
        WadConfiguration wadConfiguration;
        bool wadDirty;
        List<string> missingWadResources=new List<string>();
        readonly Dictionary<string,List<MobileResourceArchive>> wadMapResources=new Dictionary<string,List<MobileResourceArchive>>(StringComparer.OrdinalIgnoreCase);
        void RememberWadResources()
        {
            if(wadReference==null)return;var list=CopyDescriptors(resourceArchives);
            foreach(string path in missingWadResources)if(!list.Exists(x=>string.Equals(x.location,path,StringComparison.OrdinalIgnoreCase)))list.Add(new MobileResourceArchive{name=Path.GetFileName(path.Replace('\\','/')),location=path});
            wadMapResources[MapNaming.Key(Map)]=list;
        }
        sealed class PreparedWadMap {public PreparedDocument document;public MapDocument map;public GridFrame grid;public MapView view;public MapBackground background;public WadReference file;public WadConfiguration settings;public string warning;public List<string> missing=new List<string>();}
        static List<RecentWad> LoadRecentWads()
        {
            try {var data=JsonUtility.FromJson<RecentWads>(PlayerPrefs.GetString(RecentWadKey,"{}"));var list=data==null||data.items==null?new List<RecentWad>():data.items;list.RemoveAll(x=>x==null||x.file==null||string.IsNullOrEmpty(x.map)||string.IsNullOrEmpty(x.file.name)||(!x.file.Native&&string.IsNullOrEmpty(x.file.path)));if(list.Count>10)list.RemoveRange(10,list.Count-10);return list;}
            catch {return new List<RecentWad>();}
        }
        void RememberRecentWad(WadReference file,string map)
        {
            if(file==null)return;var list=LoadRecentWads();string key=file.Native?file.uri:Path.GetFullPath(file.path);
            list.RemoveAll(x=>string.Equals(x.file.Native?x.file.uri:Path.GetFullPath(x.file.path),key,file.Native?StringComparison.Ordinal:StringComparison.OrdinalIgnoreCase)&&string.Equals(x.map,map,StringComparison.OrdinalIgnoreCase));
            list.Insert(0,new RecentWad{file=file,map=map});if(list.Count>10)list.RemoveRange(10,list.Count-10);PlayerPrefs.SetString(RecentWadKey,JsonUtility.ToJson(new RecentWads{items=list}));PlayerPrefs.Save();
        }
        internal void ImportWadDocument(byte[] bytes,byte[] settings,WadReference file,string requestedMap=null,string warning=null)
        {
            if(bytes==null||bytes.Length<4||(System.Text.Encoding.ASCII.GetString(bytes,0,4)!="IWAD"&&System.Text.Encoding.ASCII.GetString(bytes,0,4)!="PWAD")){Notify("Открывайте карту из WAD. Ресурсники подключайте через «Ресурсы».");return;}
            CancelDrag();var context=CaptureOpenContext();
            BeginDocumentWork(context,job=>
            {
                job.Check("Разбор WAD…");var prepared=new PreparedDocument{archive=DoomWad.Read(bytes),source=bytes,descriptors=CopyDescriptors(resourceArchives),sources=new List<ResourceArchive>(resourceSources)};
                var names=prepared.archive.EditableMapNames();if(names.Count==0)throw new InvalidDataException("В WAD нет бинарных карт Doom / Hexen.");
                WadConfiguration config=null;if(settings!=null)try{config=new WadConfiguration(settings);}catch(Exception error){warning="DBS не загружен: "+error.Message;}
                return new PreparedWadMap{document=prepared,file=file,settings=config,warning=warning};
            },result=>
            {
                var prepared=(PreparedWadMap)result;var names=prepared.document.archive.EditableMapNames();
                if(!string.IsNullOrEmpty(requestedMap)){if(!names.Exists(n=>string.Equals(n,requestedMap,StringComparison.OrdinalIgnoreCase))){Notify("В WAD больше нет карты "+requestedMap+".");return;}OpenPreparedWad(prepared,requestedMap,context,true);}
                else if(names.Count==1)OpenPreparedWad(prepared,names[0],context,true);
                else ShowDialog("Открыть карту из WAD",body=>{foreach(string name in names){string chosen=name;AddButton(body,chosen,()=>OpenPreparedWad(prepared,chosen,context,true));}});
            });
        }
        void OpenPreparedWad(PreparedWadMap prepared,string name,OpenContext context,bool fresh)
        {
            string profile=gameProfile,engine=actionEngine;MapDocument remembered=null;if(!fresh)editedMaps.TryGetValue(name,out remembered);
            BeginDocumentWork(context,job=>
            {
                job.Check("Карта: "+name);prepared.map=remembered==null?prepared.document.archive.Import(name):remembered.Clone();
                if(prepared.settings!=null)
                {
                    profile=ValidProfile(prepared.settings.Profile);engine=ValidActionEngine(prepared.settings.Engine);
                    var descriptors=new List<MobileResourceArchive>();var paths=prepared.settings.Resources(name);
                    foreach(string location in paths)
                    {
                        job.Check("Ресурс: "+Path.GetFileName(location));string path=location;
                        if(!Path.IsPathRooted(path)&&!prepared.file.Native)path=Path.Combine(Path.GetDirectoryName(prepared.file.path),path);
                        try {var resource=DocumentFile.Read(path,MaxResourceBytes,()=>job.cancelled);descriptors.Add(new MobileResourceArchive{name=Path.GetFileName(path),location=Path.GetFullPath(path),data=Convert.ToBase64String(resource)});}
                        catch(OperationCanceledException){throw;}catch(Exception error){prepared.missing.Add(location);prepared.warning=(prepared.warning??"")+"\nРесурс недоступен: "+location+" · "+error.Message;}
                    }
                    prepared.document.descriptors=descriptors;prepared.document.sources=ParseResourceArchives(descriptors,prepared.document.source.Length,()=>job.cancelled);
                    if(remembered==null)
                    {
                        var config=prepared.settings.maps;
                        if(config.HasGrid(name))prepared.grid=config.GridFor(name,new GridFrame());
                        if(config.HasView(name))prepared.view=config.ViewFor(name);
                        if(config.HasBackground(name))prepared.background=config.BackgroundFor(name);
                        if(config.HasLabels(name))prepared.map.tagLabels=MapTagLabels.Copy(config.LabelsFor(name));
                        if(config.Groups.HasMap(name))try{config.Groups.Plan(prepared.map,true).Apply(prepared.map);}catch(Exception error){prepared.warning=(prepared.warning??"")+"\nГруппы DBS не применены: "+error.Message;}
                    }
                }
                prepared.map.Validate();CheckPreparedBudget(prepared.document);PrepareDocumentCatalog(prepared.document,job);return prepared;
            },result=>
            {
                SaveRecovery();if(fresh){wadMapResources.Clear();editedMaps.Clear();mapGrids.Clear();mapViews.Clear();mapBackgrounds.Clear();}else RememberCurrentMap();
                bool priorDirty=wadDirty;archive=prepared.document.archive;sourceWad=prepared.document.source;sourceMapPackage=null;
                resourceArchives=prepared.document.descriptors;resourceSources=prepared.document.sources;gameProfile=profile;actionEngine=engine;
                wadReference=prepared.file;wadConfiguration=prepared.settings;missingWadResources=prepared.missing;
                if(prepared.grid!=null)mapGrids[name]=prepared.grid;if(prepared.view!=null)mapViews[name]=prepared.view;if(prepared.background!=null)mapBackgrounds[name]=prepared.background;
                Replace(prepared.map,prepared.document.catalog,prepared.document.resourceError);wadDirty=fresh?false:priorDirty;
                RememberRecentWad(wadReference,Map.name);if(!string.IsNullOrEmpty(prepared.warning))Notify(prepared.warning.Trim());
            });
        }
        void CloseWadMap()
        {SaveRecovery();CloseDialog();if(VisualActive)ExitVisual();archive=null;sourceWad=null;sourceMapPackage=null;wadReference=null;wadConfiguration=null;missingWadResources.Clear();wadMapResources.Clear();editedMaps.Clear();mapGrids.Clear();mapViews.Clear();mapBackgrounds.Clear();Replace(new MapDocument{name="MAP01"});wadDirty=false;recoveryMapActive=false;Notify("Карта закрыта.");}
        void SaveWadSettingsOnly(Action complete)
        {
            var file=wadReference;var context=CaptureOpenContext();
            PrepareWadSettings(settings=>GetComponent<MobileFileBridge>().SaveWadPair(null,settings,file,file.name,saved=>
            {if(!CurrentOpenContext(context)){Notify("Настройки сохранены; карта изменилась, закрытие отменено.");return;}wadConfiguration=new WadConfiguration(settings);if(complete!=null)complete();}));
        }
        internal void SaveWadMap(bool saveAs,Action afterSave=null,bool nodes=true)
        {
            if(Draft.Count!=0||Tool==MobileTool.Paste){Notify("Сначала завершите построение.");return;}
            var context=CaptureOpenContext();WadReference target=saveAs?null:wadReference;
            PrepareWadSettings((settings)=>BeginMapExportCore(nodes&&NativeNodeBuilder.Available,false,bytes=>
            {
                if(!CurrentOpenContext(context)){Notify("Карта изменилась. Повторите сохранение.");return;}
                GetComponent<MobileFileBridge>().SaveWadPair(bytes,settings,target,Map.name+".wad",file=>
                {if(!CurrentOpenContext(context)){Notify("Снимок сохранён; текущая карта содержит новые изменения.");return;}wadReference=file;wadConfiguration=new WadConfiguration(settings);wadDirty=false;RememberRecentWad(file,Map.name);Notify("Сохранено: "+file.name+". Настройки — в приложении.");if(afterSave!=null)afterSave();});
            },false));
        }
        void PrepareWadSettings(Action<byte[]> deliver,WadConfiguration previous=null,IEnumerable<MapDocument> only=null)
        {
            RememberCurrentMap();var maps=new List<MapDocument>();if(only!=null){foreach(var map in only)maps.Add(map.Clone());}else foreach(var map in editedMaps.Values)maps.Add(map.Clone());
            var grids=new Dictionary<string,GridFrame>(StringComparer.OrdinalIgnoreCase);var views=new Dictionary<string,MapView>(StringComparer.OrdinalIgnoreCase);var backgrounds=new Dictionary<string,MapBackground>(StringComparer.OrdinalIgnoreCase);
            foreach(var map in maps){string key=MapNaming.Key(map);GridFrame grid;MapView view;MapBackground background;grids[map.name]=mapGrids.TryGetValue(key,out grid)?grid.Validated():new GridFrame();if(mapViews.TryGetValue(key,out view))views[map.name]=view.Validated();if(mapBackgrounds.TryGetValue(key,out background))backgrounds[map.name]=background.Validated();}
            var renamed=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);foreach(var map in maps)if(!string.Equals(MapNaming.Key(map),map.name,StringComparison.OrdinalIgnoreCase))renamed[MapNaming.Key(map)]=map.name;
            var resourcesByMap=new Dictionary<string,List<MobileResourceArchive>>(StringComparer.OrdinalIgnoreCase);
            foreach(var map in maps){List<MobileResourceArchive> refs;resourcesByMap[map.name]=wadMapResources.TryGetValue(MapNaming.Key(map),out refs)?CopyDescriptors(refs):CopyDescriptors(resourceArchives);}
            string profile=gameProfile=="auto"?effectiveProfile:gameProfile,engine=actionEngine=="auto"?(Actions.mbf21?"MBF21":Actions.mbf?"MBF":Actions.extended?"Boom":"vanilla"):actionEngine;string cache=Path.Combine(Application.persistentDataPath,"WadResources");var config=previous??wadConfiguration;var context=CaptureOpenContext();
            BeginDocumentWork(context,job=>
            {
                var locationsByMap=new Dictionary<string,List<string>>(StringComparer.OrdinalIgnoreCase);
                foreach(var pair in resourcesByMap)
                {
                var locations=new List<string>();foreach(var resource in pair.Value)
                {
                    if(string.IsNullOrEmpty(resource.data)){if(!string.IsNullOrEmpty(resource.location))locations.Add(resource.location);continue;}
                    job.Check("Настройки ресурсов…");string path=resource.location;
                    if(string.IsNullOrEmpty(path)||!File.Exists(path))
                    {
                        var bytes=Convert.FromBase64String(resource.data);string digest;using(var sha=SHA256.Create())digest=BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();
                        path=Path.Combine(cache,digest+"-"+DocumentCache.ExportName(resource.name));if(!File.Exists(path))AtomicProjectFile.Write(path,bytes,()=>job.cancelled);
                    }
                    locations.Add(Path.GetFullPath(path));
                }
                locationsByMap[pair.Key]=locations;
                }
                var settings=DbsMapSettings.Export(maps,name=>grids[name],name=>views.ContainsKey(name)?views[name]:null,name=>backgrounds.ContainsKey(name)?backgrounds[name]:null);
                return WadConfiguration.Export(config,settings,profile,engine,new string[0],renamed,locationsByMap);
            },result=>deliver((byte[])result),"Настройки WAD");
        }
        internal void SaveWadInto(byte[] targetBytes,byte[] targetSettings,WadReference target)
        {
            var current=SnapshotMapWithCamera();current.sourceName=null;var context=CaptureOpenContext();string tempRoot=Application.temporaryCachePath;bool nodes=NativeNodeBuilder.Available;
            BeginDocumentWork(context,job=>{job.Check("Целевой WAD…");return DoomWad.Read(targetBytes);},result=>
            {
                var destination=(DoomWad)result;WadConfiguration config=null;if(targetSettings!=null)try{config=new WadConfiguration(targetSettings);}catch(Exception error){Notify("Не удалось прочитать DBS целевого WAD: "+error.Message);return;}
                if(config==null)config=new WadConfiguration(System.Text.Encoding.UTF8.GetBytes("type=\"Doom Builder Map Settings Configuration\"; maps {}"));
                PrepareWadSettings(settings=>BeginDocumentWork(context,job=>{job.Check("Карта в WAD…");if(nodes){string warning;return destination.ExportBuiltMaps(new[]{DoomWad.Read(NativeNodeBuilder.BuildMap(current,tempRoot,out warning))});}return destination.Export(new[]{current});},bytes=>GetComponent<MobileFileBridge>().SaveWadPair((byte[])bytes,settings,target,target.name,file=>Notify("Карта "+current.name+" сохранена в "+file.name+".")),"Сохранить карту в WAD"),config,new[]{current});
            });
        }
    }
}
