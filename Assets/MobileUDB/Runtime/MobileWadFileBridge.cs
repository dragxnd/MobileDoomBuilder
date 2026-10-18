using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using MobileUDB.Core;
using UnityEngine;
namespace MobileUDB
{
    public sealed partial class MobileFileBridge
    {
        [Serializable] sealed class WadReceipt {public string name,path,settingsPath,uri,tree,warning;}
        sealed class WadIO {public volatile bool cancelled;public Task task;public byte[] bytes,settings;public string failure,warning;public WadReference file;}
        WadIO wadIO;
        bool wadInto;
        string wadRequestedMap;
        string nativeWadExport,nativeDbsExport;
        Action<WadReference> savedWad;
        public void OpenWad(){if(BusyWad())return;wadInto=false;wadRequestedMap=null;
#if UNITY_ANDROID && !UNITY_EDITOR
            nativePending=true;NativeWad("openWad",gameObject.name,Application.temporaryCachePath,360L*1024*1024);
#elif UNITY_EDITOR
            string path=UnityEditor.EditorUtility.OpenFilePanel("Открыть карту из WAD","","wad");if(!string.IsNullOrEmpty(path))OpenWadReference(new WadReference{path=path,name=Path.GetFileName(path)},null);
#else
            editor.PathDialog("Открыть WAD",path=>OpenWadReference(new WadReference{path=path,name=Path.GetFileName(path)},null));
#endif
        }
        public void OpenWadForSaveInto(){if(BusyWad())return;wadInto=true;wadRequestedMap=null;
#if UNITY_ANDROID && !UNITY_EDITOR
            nativePending=true;NativeWad("openWad",gameObject.name,Application.temporaryCachePath,360L*1024*1024);
#elif UNITY_EDITOR
            string path=UnityEditor.EditorUtility.OpenFilePanel("Сохранить карту в WAD","","wad");if(!string.IsNullOrEmpty(path))ReadWad(new WadReference{path=path,name=Path.GetFileName(path)},null,null);
#else
            editor.PathDialog("Выберите целевой WAD",path=>ReadWad(new WadReference{path=path,name=Path.GetFileName(path)},null,null));
#endif
        }
        public void OpenWadReference(WadReference file,string map)
        {if(BusyWad())return;wadInto=false;wadRequestedMap=map;if(file.Native){nativePending=true;NativeWad("readWad",gameObject.name,file.uri,Application.temporaryCachePath,360L*1024*1024);}else ReadWad(file,null,null);}
        bool BusyWad(){if(nativePending||wadIO!=null||editor.DocumentOpeningBusy){editor.Notify("Дождитесь завершения файловой операции.");return true;}return false;}
        void ReadWad(WadReference file,string copy,string settingsCopy,string warning=null)
        {
            if(!file.name.EndsWith(".wad",StringComparison.OrdinalIgnoreCase)){editor.Notify("Открывайте WAD; ресурсники подключаются отдельно.");return;}
            var job=new WadIO{file=file,warning=warning};wadIO=job;
            string path=copy??file.path;string cache=Application.temporaryCachePath,privateRoot=Application.persistentDataPath;bool into=wadInto;string requested=wadRequestedMap;
            var progress=editor.ShowFileReadProgress(()=>{job.cancelled=true;wadIO=null;});
            job.task=Task.Run(()=>
            {
                try{job.bytes=DocumentFile.Read(path,360L*1024*1024,()=>job.cancelled);try{job.settings=settingsCopy!=null?DocumentFile.Read(settingsCopy,DbsSelectionGroups.MaximumBytes,()=>job.cancelled):copy==null?WadSettingsStore.Read(privateRoot,file.path,()=>job.cancelled):null;}catch(OperationCanceledException){throw;}catch(Exception error){job.warning="Настройки WAD недоступны: "+error.Message;}}
                catch(Exception error){job.failure=error.Message;}
                finally {if(copy!=null){try{DocumentCache.DeleteImportCopy(cache,copy);if(settingsCopy!=null)DocumentCache.DeleteImportCopy(cache,settingsCopy);}catch(Exception error){job.warning="Ошибка очистки кэша: "+error.Message;}}}
            });
            StartCoroutine(WaitWadIO(job,()=>{if(into)editor.SaveWadInto(job.bytes,job.settings,file);else editor.ImportWadDocument(job.bytes,job.settings,file,requested,job.warning);}));
        }
        IEnumerator WaitWadIO(WadIO job,Action complete)
        {
            yield return null;while(!job.task.IsCompleted){if(job.cancelled||wadIO!=job)yield break;yield return null;}
            if(job.cancelled||wadIO!=job)yield break;wadIO=null;editor.CloseFileReadProgress();
            if(job.failure!=null||job.task.IsFaulted){editor.Notify("Ошибка WAD: "+(job.failure??job.task.Exception.GetBaseException().Message));yield break;}complete();
        }
        public void SaveWadPair(byte[] bytes,byte[] settings,WadReference target,string filename,Action<WadReference> complete)
        {
            if(BusyWad())return;
#if UNITY_ANDROID && !UNITY_EDITOR
            var job=new WadIO();wadIO=job;string cache=Application.temporaryCachePath;
            job.task=Task.Run(()=>{try{nativeWadExport=bytes==null?null:DocumentCache.WriteExportCopy(cache,filename,bytes);nativeDbsExport=DocumentCache.WriteExportCopy(cache,Path.ChangeExtension(filename,".dbs"),settings);}catch(Exception error){job.failure=error.Message;}finally{if(job.cancelled){DocumentCache.DeleteExportCopy(cache,nativeWadExport);DocumentCache.DeleteExportCopy(cache,nativeDbsExport);nativeWadExport=nativeDbsExport=null;}}});
            var progress=editor.ShowFileReadProgress(()=>{job.cancelled=true;wadIO=null;});
            StartCoroutine(WaitWadIO(job,()=>{savedWad=complete;nativePending=true;if(target!=null)NativeWad("writeWad",gameObject.name,target.uri,target.name,nativeWadExport,nativeDbsExport);else NativeWad("createWad",gameObject.name,filename,nativeWadExport,nativeDbsExport);}));
#elif UNITY_EDITOR
            string path=target==null?UnityEditor.EditorUtility.SaveFilePanel("Сохранить карту как WAD","",filename,"wad"):target.path;
            if(!string.IsNullOrEmpty(path))WriteLocalWadPair(path,bytes,settings,complete);
#else
            if(target!=null)WriteLocalWadPair(target.path,bytes,settings,complete);else editor.PathDialog("Сохранить карту как WAD",path=>WriteLocalWadPair(path,bytes,settings,complete),Path.Combine(Application.persistentDataPath,filename));
#endif
        }
        internal void WriteLocalWadPair(string path,byte[] bytes,byte[] settings,Action<WadReference> complete)
        {
            if(BusyWad())return;path=Path.GetFullPath(path);string privateRoot=Application.persistentDataPath;var job=new WadIO{file=new WadReference{path=path,name=Path.GetFileName(path)}};wadIO=job;
            editor.ShowFileReadProgress(()=>{job.cancelled=true;wadIO=null;});job.task=Task.Run(()=>{try{WadSettingsStore.WriteWad(privateRoot,path,bytes,settings,()=>job.cancelled);}catch(Exception error){job.failure=error.Message;}});
            StartCoroutine(WaitWadIO(job,()=>complete(job.file)));
        }
        void NativeWad(string method,params object[] arguments)
        {
            try
            {
#if UNITY_ANDROID && !UNITY_EDITOR
            using(var player=new AndroidJavaClass("com.unity3d.player.UnityPlayer"))using(var activity=player.GetStatic<AndroidJavaObject>("currentActivity"))using(var bridge=new AndroidJavaClass("org.mobileudb.DocumentBridge"))
            {var all=new object[arguments.Length+1];all[0]=activity;Array.Copy(arguments,0,all,1,arguments.Length);bridge.CallStatic(method,all);}
#else
            throw new InvalidOperationException("Системный выбор документов доступен на Android.");
#endif
            }catch(Exception error){WadNativeError("Ошибка выбора документа: "+error.Message);}
        }
        [UnityEngine.Scripting.Preserve] public void OnNativeWadRead(string json)
        {
            try {var receipt=JsonUtility.FromJson<WadReceipt>(json);if(receipt==null||!DocumentCache.IsImportCopy(Application.temporaryCachePath,receipt.path)||(receipt.settingsPath!=null&&!DocumentCache.IsImportCopy(Application.temporaryCachePath,receipt.settingsPath)))throw new InvalidDataException("Некорректная копия WAD / DBS.");nativePending=false;ReadWad(new WadReference{name=receipt.name,uri=receipt.uri,tree=receipt.tree},receipt.path,receipt.settingsPath,receipt.warning);}
            catch(Exception error){WadNativeError(error.Message);}
        }
        [UnityEngine.Scripting.Preserve] public void OnNativeWadSaved(string json)
        {
            try {var file=JsonUtility.FromJson<WadReference>(json);if(file==null||string.IsNullOrEmpty(file.uri)||string.IsNullOrEmpty(file.name))throw new InvalidDataException("Нет подтверждения сохранения WAD.");var complete=savedWad;savedWad=null;nativePending=false;CleanWadCopies();if(complete!=null)complete(file);}
            catch(Exception error){WadNativeError(error.Message);}
        }
        void CleanWadCopies(){DocumentCache.DeleteExportCopy(Application.temporaryCachePath,nativeWadExport);DocumentCache.DeleteExportCopy(Application.temporaryCachePath,nativeDbsExport);nativeWadExport=nativeDbsExport=null;}
        void WadNativeError(string error){nativePending=false;savedWad=null;CleanWadCopies();editor.Notify(error);}
        void CancelWadIO(){if(wadIO!=null)wadIO.cancelled=true;wadIO=null;}
    }
}
