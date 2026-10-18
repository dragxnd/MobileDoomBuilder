using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Threading.Tasks;
using MobileUDB.Core;
using UnityEngine;

namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        readonly SaveRevision recoveryRevision = new SaveRevision();
        bool dirty { get { return recoveryRevision.Dirty||viewRecoveryRevision.Dirty; } }
        sealed class ProjectSnapshot { public MobileProject project; public byte[] wad; public long revision,viewRevision; }
        sealed class RecoveryJob { public volatile bool cancelled; public long revision,viewRevision; public bool saved; public string failure; public Task task; }
        RecoveryJob recoveryJob;
        bool recoveryMapActive;
        bool HasLastMapRecovery { get { return File.Exists(RecoveryPath); } }
        void RestoreLastMap()
        {
            if(!HasLastMapRecovery){Notify("Автокопии последней карты пока нет.");return;}
            CancelDrag();CancelRecovery();CompleteRecovery(true);var context=CaptureOpenContext();string path=RecoveryPath;
            BeginDocumentWork(context,job=>
            {
                job.Check("Последняя карта…");var bytes=DocumentFile.Read(path,360L*1024*1024,()=>job.cancelled);
                var prepared=new PreparedDocument();PrepareSavedDocument(prepared,bytes,job);CheckPreparedBudget(prepared);PrepareDocumentCatalog(prepared,job);return prepared;
            },result=>ConfirmReplace(()=>CommitPreparedDocument((PreparedDocument)result,((PreparedDocument)result).project.map)),"Восстановить последнюю карту");
        }
        ProjectSnapshot SnapshotProject()
        {
            RememberClassicView();Map.Validate(); var current = SnapshotMapWithCamera();string key=MapNaming.Key(Map);editedMaps[key] = current;
            mapGrids[key]=gridFrame.Validated();var grids=new List<MapGridFrame>();foreach(var pair in mapGrids)grids.Add(new MapGridFrame{name=pair.Key,grid=pair.Value.Validated()});
            var views=new List<MapViewFrame>();foreach(var pair in mapViews)views.Add(new MapViewFrame{name=pair.Key,view=pair.Value.Validated()});
            var other = new List<MapDocument>(); foreach (var pair in editedMaps) if (!string.Equals(pair.Key,key,StringComparison.OrdinalIgnoreCase)) other.Add(pair.Value.Clone());
            var attachments = new List<MobileResourceArchive>();
            foreach (var resource in resourceArchives) attachments.Add(new MobileResourceArchive { name = resource.name,id = resource.id,kind = resource.kind,data = resource.data,location=resource.location });
            return new ProjectSnapshot { revision = recoveryRevision.Current,viewRevision=viewRecoveryRevision.Current,wad = sourceWad,
                project = new MobileProject { map = current,editedMaps = other,gameProfile = gameProfile,actionEngine = actionEngine,
                    geometryMergeMode = geometryMergeMode.ToString(),pasteTagMode = pasteTagMode.ToString(),drawTextures = drawTextures.Validated(Map.format),grid=gridFrame.Validated(),grids=grids,views=views,backgrounds=BackgroundFrames(),
                    resources = attachments,sourcePk3ResourceId = sourceMapPackage == null ? null : sourceMapPackage.id } };
        }
        static byte[] SnapshotBytes(ProjectSnapshot snapshot,Func<bool> cancelled=null,Action<string> progress=null)
        {
            if(cancelled!=null&&cancelled())throw new OperationCanceledException();
            if(progress!=null)progress("Исходный архив…");
            snapshot.project.sourceWad = snapshot.wad == null ? null : Convert.ToBase64String(snapshot.wad);
            if(cancelled!=null&&cancelled())throw new OperationCanceledException();
            if(progress!=null)progress("Карты и ресурсы…");
            // Unity 6000.3 JsonUtility supports background use with an immutable object.
            string json=JsonUtility.ToJson(snapshot.project);
            if(cancelled!=null&&cancelled())throw new OperationCanceledException();
            if(progress!=null)progress("Подготовка файла…");
            byte[] bytes=Encoding.UTF8.GetBytes(json);
            if(cancelled!=null&&cancelled())throw new OperationCanceledException();
            return bytes;
        }
        void UpdateRecovery()
        {
            CompleteRecovery(false);
            if (!recoveryMapActive || !dirty || DocumentOpeningBusy || recoveryJob != null || ProjectSavingBusy || dragOrigin != null || Time.unscaledTime < saveAt) return;
            try
            {
                var snapshot = SnapshotProject(); string path = RecoveryPath;
                var job = new RecoveryJob { revision = snapshot.revision,viewRevision=snapshot.viewRevision }; recoveryJob = job;
                job.task = Task.Run(() =>
                {
                    try { if (!job.cancelled) job.saved = AtomicProjectFile.Write(path,SnapshotBytes(snapshot),()=>job.cancelled); }
                    catch (Exception failure) { job.failure = failure.Message; }
                });
            }
            catch (Exception failure) { recoveryJob = null; saveAt = Time.unscaledTime + 10; Notify("Не удалось сохранить автокопию: " + failure.Message); }
        }
        void CompleteRecovery(bool wait)
        {
            var job = recoveryJob; if (job == null || job.task == null) return;
            if (wait) try { job.task.GetAwaiter().GetResult(); } catch (Exception failure) { job.failure = failure.Message; }
            if (!job.task.IsCompleted) return;
            recoveryJob = null;
            if (job.saved){recoveryRevision.MarkSaved(job.revision);viewRecoveryRevision.MarkSaved(job.viewRevision);}
            if (job.failure != null)
            {
                saveAt = Time.unscaledTime + 10;
                if (wait) Debug.LogWarning("Не удалось сохранить автокопию: " + job.failure);
                else Notify("Не удалось сохранить автокопию: " + job.failure);
            }
        }
        void CancelRecovery()
        { if (recoveryJob != null) recoveryJob.cancelled = true; }
        void OnApplicationPause(bool pause) { if (pause) SaveRecovery(); }
        void OnApplicationQuit() { SaveRecovery(); }
        void SaveRecovery()
        {
            if(!recoveryMapActive)return;
            CancelDrag();EnsureClassicView();ObserveClassicView();ObserveVisualCamera(); CompleteRecovery(true);
            if (!dirty) return;
            try
            {
                var snapshot = SnapshotProject(); AtomicProjectFile.Write(RecoveryPath,SnapshotBytes(snapshot)); recoveryRevision.MarkSaved(snapshot.revision);viewRecoveryRevision.MarkSaved(snapshot.viewRevision);
            }
            catch (Exception failure) { Debug.LogWarning("Не удалось сохранить автокопию: " + failure.Message); }
        }
        static void WriteAtomic(string path,byte[] bytes) { AtomicProjectFile.Write(path,bytes); }
        public byte[] ProjectBytes() { return SnapshotBytes(SnapshotProject()); }
    }
}
