using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using MobileUDB.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        sealed class ResourceImportJob
        {
            public volatile bool cancelled;
            public volatile string progress = "Подготовка…";
            public PreparedResourceAttachment prepared;
            public string failure;
            public string cleanupFailure,location;
        }
        ResourceImportJob resourceImportJob;
        Task resourceImportTask;
        void CancelResourceImport()
        {
            if (resourceImportJob == null) return;
            resourceImportJob.cancelled = true; resourceImportJob.prepared = null; resourceImportJob = null;
        }
        public void ImportResourceBytes(byte[] bytes,string filename = "Ресурсы")
        { BeginResourceImport(bytes,null,filename,null); }
        public void ImportResourcePath(string path,string filename,Action cleanup=null)
        { BeginResourceImport(null,path,filename,cleanup); }
        void BeginResourceImport(byte[] bytes,string path,string filename,Action cleanup)
        {
            bool workerStarted=false;
            try
            {
                if (resourceImportTask != null && !resourceImportTask.IsCompleted) throw new InvalidOperationException("Предыдущая загрузка ещё завершается. Дождитесь её окончания.");
                if ((path==null&&bytes==null)||(bytes!=null&&bytes.Length>MaxResourceBytes)) throw new System.IO.InvalidDataException("Ресурсы: не более 128 МБ суммарно.");
                if (resourceArchives.Count >= 32) throw new System.IO.InvalidDataException("Допускается не более 32 архивов ресурсов.");
                long existing = 0; for (int i = 0; i < resourceSources.Count; i++) existing += ResourceCost(resourceSources[i],(int)(resourceArchives[i].data.Length * 3L / 4));
                long available = Math.Min(MaxResourceBytes - existing,MaxProjectWadBytes - (sourceWad == null ? 0 : sourceWad.Length) - existing - BackgroundBytes(BackgroundFrames()));
                var snapshot = new List<ResourceArchive>(resourceSources); var mapArchive = archive;
                var job = new ResourceImportJob{location=cleanup==null?path:null}; Text progress = null;
                ShowDialog("Подключение ресурсов",body =>
                {
                    progress = Label(body,"Подготовка…",20); progress.gameObject.AddComponent<LayoutElement>().minHeight = 100;
                    AddButton(body,"Отменить",CloseDialog);
                    AddLabel(body,"Архив проверяется в фоне. После отмены текущий этап чтения завершится, ресурс подключён не будет.");
                });
                resourceImportJob = job;
                resourceImportTask = Task.Run(() =>
                {
                    try { job.prepared = path==null?ResourceAttachment.Prepare(bytes,filename,snapshot,mapArchive,available,()=>job.cancelled,text=>job.progress=text):ResourceAttachment.PrepareFile(path,filename,snapshot,mapArchive,available,()=>job.cancelled,text=>job.progress=text); }
                    catch (OperationCanceledException) { if (!job.cancelled) job.failure = "Загрузка отменена."; }
                    catch (Exception error) { job.failure = error.Message; }
                    finally { if (job.cancelled) job.prepared = null;if(cleanup!=null)try{cleanup();}catch(Exception error){job.cleanupFailure=error.Message;} }
                });
                workerStarted=true;
                StartCoroutine(WaitForResourceImport(job,resourceImportTask,snapshot,mapArchive,progress));
            }
            catch (Exception error) { Notify("Не удалось подключить ресурсы: " + error.Message); }
            finally {if(!workerStarted&&cleanup!=null)try{cleanup();}catch(Exception error){Debug.LogWarning("Не удалось удалить временную копию ресурса: "+error.Message);} }
        }
        IEnumerator WaitForResourceImport(ResourceImportJob job,Task task,List<ResourceArchive> snapshot,DoomWad mapArchive,Text progress)
        {
            while (!task.IsCompleted)
            {
                if (resourceImportJob != job || job.cancelled) yield break;
                if (progress != null) progress.text = job.progress;
                yield return null;
            }
            if (resourceImportJob != job || job.cancelled) yield break;
            resourceImportJob = null;
            if(job.cleanupFailure!=null)Debug.LogWarning("Не удалось удалить временную копию ресурса: "+job.cleanupFailure);
            if (job.failure != null || task.IsFaulted)
            {
                string error = job.failure ?? task.Exception.GetBaseException().Message;
                ShowDialog("Ресурс не подключён",body => { AddLabel(body,error); AddButton(body,"Вернуться к ресурсам",ResourcesMenu); });
                yield break;
            }
            bool current = ReferenceEquals(mapArchive,archive) && snapshot.Count == resourceSources.Count;
            if (current) for (int i = 0; i < snapshot.Count; i++) if (!ReferenceEquals(snapshot[i],resourceSources[i])) { current = false; break; }
            if (!current)
            {
                job.prepared = null; CloseDialog(); Notify("Карта или ресурсы изменились во время загрузки. Подключите файл повторно."); yield break;
            }
            var prepared = job.prepared; job.prepared = null;
            resourceSources.Add(prepared.source);
            resourceArchives.Add(new MobileResourceArchive { name = prepared.name,kind = prepared.kind,data = prepared.data,location=job.location });
            CloseDialog(); ClearThumbnails(); resources = prepared.catalog; resourceError = prepared.error;
            RebuildThingCatalog(); if (visualView != null) visualView.ResourcesChanged();
            Changed(); Notify("Ресурсы подключены: " + resources.WallCount + " стен, " + resources.FlatCount + " плоскостей." + ResourceWarning());
        }
    }
}
