using System;
using System.Collections;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using MobileUDB.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MobileUDB
{
    public sealed partial class MobileFileBridge : MonoBehaviour
    {
        public MobileEditor editor;
        bool resourceImport, nativePending;
        Action<byte[],string> settingsImport;
        sealed class ExportCopyJob { public volatile bool cancelled; public string cache,path; public Task task; }
        ExportCopyJob exportCopy;
        sealed class ImportReadJob
        {
            public volatile bool cancelled;
            public volatile int percent;
            public string path,filename,cache,failure,cleanupFailure;
            public bool resources,nativeCopy;
            public byte[] bytes;
            public Action<byte[],string> settings;
        }
        ImportReadJob importRead;
        Task importReadTask;
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void MUDBImport(string target,string cacheDirectory,long maximumBytes);
        [DllImport("__Internal")] static extern void MUDBExport(string target, string path);
#endif
        public void Import() { OpenPicker(false); }
        public void ImportResources() { OpenPicker(true); }
        public void ImportSettings(Action<byte[],string> complete) { if(complete==null)throw new ArgumentNullException("complete");OpenPicker(false,complete); }
        public void ImportImage(Action<byte[],string> complete) { if(complete==null)throw new ArgumentNullException("complete");OpenPicker(false,complete,true); }
        void OpenPicker(bool resources,Action<byte[],string> settings = null,bool image=false)
        {
            if (editor.DocumentOpeningBusy) { editor.Notify("Предыдущее открытие ещё завершается. Дождитесь его окончания."); return; }
            if (importReadTask != null && !importReadTask.IsCompleted) { editor.Notify("Предыдущее чтение ещё завершается."); return; }
            if (nativePending) { editor.Notify("Дождитесь завершения выбора файла."); return; }
            resourceImport = resources;
            settingsImport = settings;
            try
            {
#if UNITY_EDITOR
            string path = image ? UnityEditor.EditorUtility.OpenFilePanelWithFilters("Фон карты", "", new[] { "Изображения", "png,jpg,jpeg,bmp,gif", "Все файлы", "*" }) : settings != null ? UnityEditor.EditorUtility.OpenFilePanel("Настройки UDB (.dbs)","","dbs") : resources ? UnityEditor.EditorUtility.OpenFilePanelWithFilters("Подключить ресурсы WAD / PK3 / DEH / BEX", "", new[] { "Ресурсы Doom", "wad,pk3,zip,deh,bex", "Все файлы", "*" }) : UnityEditor.EditorUtility.OpenFilePanel("Импорт WAD / PK3 / MobileUDB / DBS", "", "");
            if (!string.IsNullOrEmpty(path)) ImportPath(path); else ResetImport();
#elif UNITY_ANDROID
            nativePending = true;
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var bridge = new AndroidJavaClass("org.mobileudb.DocumentBridge"))
                bridge.CallStatic("open", activity, gameObject.name,Application.temporaryCachePath,settings!=null?DbsSelectionGroups.MaximumBytes:resources?128L*1024*1024:360L*1024*1024);
#elif UNITY_IOS
            nativePending = true;
            MUDBImport(gameObject.name,Application.temporaryCachePath,settings!=null?DbsSelectionGroups.MaximumBytes:resources?128L*1024*1024:360L*1024*1024);
#else
            editor.PathDialog(image ? "Выбрать изображение" : settings != null ? "Открыть .dbs" : resources ? "Подключить ресурсы WAD / PK3 / DEH / BEX" : "Открыть файл", ImportPath);
#endif
            }
            catch (Exception e) { ResetImport(); editor.Notify("Не удалось открыть выбор файла: " + e.Message); }
        }
        public void Export(byte[] bytes, string filename)
        {
            if (nativePending) { editor.Notify("Дождитесь завершения выбора файла."); return; }
            resourceImport = false;
            try
            {
#if UNITY_EDITOR
            string path = UnityEditor.EditorUtility.SaveFilePanel("Экспорт", "", filename, Path.GetExtension(filename).TrimStart('.'));
            if (!string.IsNullOrEmpty(path)) { File.WriteAllBytes(path, bytes); editor.Notify("Сохранено: " + path); }
#elif UNITY_ANDROID
            PrepareNativeExport(bytes,filename);
#elif UNITY_IOS
            PrepareNativeExport(bytes,filename);
#else
            editor.PathDialog("Сохранить файл", path => { File.WriteAllBytes(path, bytes); editor.Notify("Сохранено: " + path); }, Path.Combine(Application.persistentDataPath, filename));
#endif
            }
            catch (Exception e) { ResetImport(); editor.Notify("Ошибка сохранения: " + e.Message); }
        }
        void PrepareNativeExport(byte[] bytes,string filename)
        {
            filename = DocumentCache.ExportName(filename);
            var job = new ExportCopyJob { cache = Application.temporaryCachePath };
            nativePending = true; exportCopy = job;
            job.task = Task.Run(() =>
            {
                try { if (!job.cancelled) job.path = DocumentCache.WriteExportCopy(job.cache,filename,bytes); }
                finally { if (job.cancelled) DeleteExportCopy(job); }
            });
            editor.Notify("Подготовка файла для сохранения…"); StartCoroutine(WaitForExportCopy(job,filename));
        }
        IEnumerator WaitForExportCopy(ExportCopyJob job,string filename)
        {
            while (!job.task.IsCompleted) { if (job.cancelled) yield break; yield return null; }
            if (job.cancelled || exportCopy != job) yield break;
            try
            {
                if (job.task.IsFaulted) throw job.task.Exception.GetBaseException();
#if UNITY_ANDROID && !UNITY_EDITOR
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var bridge = new AndroidJavaClass("org.mobileudb.DocumentBridge"))
                    bridge.CallStatic("saveFile",activity,gameObject.name,filename,job.path);
#elif UNITY_IOS && !UNITY_EDITOR
                MUDBExport(gameObject.name,job.path);
#endif
            }
            catch (Exception error) { ResetImport(); editor.Notify("Ошибка сохранения: " + error.Message); }
        }
        void OnDisable() { CancelImportRead();CancelWadIO(); }
        void OnDestroy() { CancelImportRead(); CancelExportCopy();CancelWadIO(); }
        void CancelExportCopy()
        {
            var job = exportCopy; exportCopy = null;
            if (job == null) return;
            job.cancelled = true;
            // path is assigned only after writing closes; cancellation during the write
            // is handled by the worker's finally without deleting an open file.
            DeleteExportCopy(job);
        }
        static void DeleteExportCopy(ExportCopyJob job)
        {
            try { DocumentCache.DeleteExportCopy(job.cache,job.path); }
            catch (Exception error) { Debug.LogWarning("Не удалось удалить временную копию экспорта: " + error.Message); }
        }
        void ImportPath(string path) {ImportPath(path,Path.GetFileName(path));}
        public void OpenProjectPath(string path)
        {
            if (editor.DocumentOpeningBusy) { editor.Notify("Предыдущее открытие ещё завершается. Дождитесь его окончания."); return; }
            if (nativePending) { editor.Notify("Дождитесь завершения выбора файла."); return; }
            resourceImport = false; settingsImport = null; ImportPath(path);
        }
        void ImportPath(string path,string filename,bool nativeCopy = false)
        {
            bool workerStarted = false;
            try
            {
                if (editor.DocumentOpeningBusy) throw new InvalidOperationException("Предыдущее открытие ещё завершается. Дождитесь его окончания.");
                if (importReadTask != null && !importReadTask.IsCompleted) throw new InvalidOperationException("Предыдущее чтение ещё завершается.");
                if(resourceImport&&settingsImport==null&&!DbsSelectionGroups.IsFilename(filename))
                {
                    // The resource worker owns the file until both parsing and
                    // portable payload preparation finish. Do not read a second
                    // whole-file byte[] or delete a native cache copy earlier.
                    string cache=Application.temporaryCachePath;Action cleanup=nativeCopy?(Action)(()=>DocumentCache.DeleteImportCopy(cache,path)):null;
                    resourceImport=false;nativePending=false;settingsImport=null;
                    editor.ImportResourcePath(path,filename,cleanup);workerStarted=true;return;
                }
                long limit=settingsImport!=null||DbsSelectionGroups.IsFilename(filename)?DbsSelectionGroups.MaximumBytes:resourceImport?(ResourceArchive.IsDehackedName(filename)?ResourceArchive.MaxDehackedBytes:128L*1024*1024):360L*1024*1024;
                var job = new ImportReadJob { path = path,filename = filename,resources = resourceImport,settings = settingsImport,nativeCopy = nativeCopy,cache = Application.temporaryCachePath };
                Text progress = editor.ShowFileReadProgress(CancelImportRead);
                importRead = job; nativePending = true;
                importReadTask = Task.Run(() =>
                {
                    try { job.bytes = DocumentFile.Read(job.path,limit,()=>job.cancelled,(done,total)=>job.percent=total == 0 ? 100 : (int)(done * 100 / total)); }
                    catch (OperationCanceledException) { if (!job.cancelled) job.failure = "Чтение отменено."; }
                    catch (Exception error) { job.failure = error.Message; }
                    finally
                    {
                        if (job.cancelled) job.bytes = null;
                        if (job.nativeCopy) try { DocumentCache.DeleteImportCopy(job.cache,job.path); } catch (Exception error) { job.cleanupFailure = error.Message; }
                    }
                });
                workerStarted = true;
                StartCoroutine(WaitForImportRead(job,importReadTask,progress));
            }
            catch (Exception e) { ResetImport(); editor.CloseFileReadProgress(); editor.Notify("Ошибка чтения: " + e.Message); if (nativeCopy && !workerStarted) DeleteImportCopy(path); }
        }
        IEnumerator WaitForImportRead(ImportReadJob job,Task task,Text progress)
        {
            while (!task.IsCompleted)
            {
                if (job.cancelled || importRead != job) yield break;
                if (progress != null) progress.text = "Чтение: " + job.percent + "%";
                yield return null;
            }
            if (job.cancelled || importRead != job) yield break;
            importRead = null; var bytes = job.bytes; job.bytes = null;
            ResetImport(); editor.CloseFileReadProgress();
            if (job.cleanupFailure != null) Debug.LogWarning("Не удалось удалить временную копию импорта: " + job.cleanupFailure);
            if (job.failure != null || task.IsFaulted) { editor.Notify("Ошибка чтения: " + (job.failure ?? task.Exception.GetBaseException().Message)); yield break; }
            // Dispatch and every Unity/editor mutation stay on Unity's thread.
            if(job.settings!=null)job.settings(bytes,job.filename);else if (job.resources) editor.ImportResourceBytes(bytes,job.filename); else editor.ImportBytes(bytes,job.filename);
        }
        void CancelImportRead()
        {
            var job = importRead; importRead = null;
            if (job == null) return;
            job.cancelled = true; nativePending = resourceImport = false;settingsImport=null;
            // The worker owns cleanup after its FileStream has closed.
        }
        void DispatchImport(byte[] bytes, string filename = "Ресурсы")
        { if(settingsImport!=null)settingsImport(bytes,filename);else if (resourceImport) editor.ImportResourceBytes(bytes, filename); else editor.ImportBytes(bytes,filename); }
        void ResetImport() { nativePending = false; resourceImport = false; settingsImport=null; CancelImportRead(); CancelExportCopy(); }
        // Invoked by Android / iOS document picker. Keep names stable for UnitySendMessage.
        [UnityEngine.Scripting.Preserve] public void OnNativeImport(string base64)
        {
            try { DispatchImport(Convert.FromBase64String(base64)); } catch (Exception e) { editor.Notify("Ошибка импорта: " + e.Message); }
            finally { ResetImport(); }
        }
        [UnityEngine.Scripting.Preserve] public void OnNativePath(string path) { ImportPath(path); }
        [Serializable, UnityEngine.Scripting.Preserve] sealed class DocumentImport {public string name,path;}
        [UnityEngine.Scripting.Preserve] public void OnNativeDocument(string json)
        {
            DocumentImport document=null;
            try
            {
                if(string.IsNullOrEmpty(json)||json.Length>32768)throw new InvalidDataException("Некорректный ответ выбора файла.");
                document=JsonUtility.FromJson<DocumentImport>(json);
                if(document==null||string.IsNullOrEmpty(document.path))throw new InvalidDataException("Выбранный файл недоступен.");
                if (!DocumentCache.IsImportCopy(Application.temporaryCachePath,document.path)) throw new InvalidDataException("Некорректный путь временной копии.");
                ImportPath(document.path,string.IsNullOrEmpty(document.name)?"Ресурсы":document.name,true);
            }
            catch(Exception error)
            {
                ResetImport(); editor.Notify("Ошибка импорта: "+error.Message);
                if(document!=null) DeleteImportCopy(document.path);
            }
        }
        static void DeleteImportCopy(string path)
        {
            try
            {
                DocumentCache.DeleteImportCopy(Application.temporaryCachePath,path);
            }
            catch(Exception error){Debug.LogWarning("Не удалось удалить временную копию импорта: "+error.Message);}
        }
        [UnityEngine.Scripting.Preserve] public void OnNativeSaved(string text) { ResetImport(); editor.Notify("Файл сохранён."); }
        [UnityEngine.Scripting.Preserve] public void OnNativeError(string error) { ResetImport();WadNativeError(error); }
    }
}
