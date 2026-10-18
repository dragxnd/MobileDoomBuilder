using System;
using System.Collections;
using System.Threading.Tasks;
using MobileUDB.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        sealed class ProjectSaveJob
        {
            public volatile bool cancelled;
            public volatile string progress="Подготовка…";
            public byte[] bytes;
            public long viewRevision;
            public bool saved;
            public string failure;
            public void Check(string stage=null)
            { if(cancelled)throw new OperationCanceledException();if(stage!=null)progress=stage; }
        }
        ProjectSaveJob projectSaveJob;
        Task projectSaveTask;
        bool ProjectSavingBusy { get { return projectSaveTask!=null&&!projectSaveTask.IsCompleted; } }
        void CancelProjectSave()
        {
            var job=projectSaveJob;projectSaveJob=null;
            if(job!=null){job.cancelled=true;job.bytes=null;}
        }
        void Save() { BeginProjectSave(SavePath,null,null); }
        void ExportProject()
        {
            string filename=Map.name+".mobileudb";
            BeginProjectSave(null,null,bytes=>GetComponent<MobileFileBridge>().Export(bytes,filename));
        }
        void BeginProjectSave(string path,Action afterSave,Action<byte[]> deliver)
        {
            if(ProjectSavingBusy){Notify("Предыдущее сохранение ещё завершается. Дождитесь его окончания.");return;}
            if(Draft.Count!=0||Tool==MobileTool.Paste){Notify("Сначала завершите или отмените построение.");return;}
            CancelDrag();
            ProjectSnapshot snapshot;
            try { snapshot=SnapshotProject(); }
            catch(Exception error){Notify("Не удалось подготовить проект: "+error.Message);return;}
            var context=CaptureOpenContext();
            var previousRecovery=recoveryJob==null?null:recoveryJob.task;
            // Finish any already-running recovery serializer before creating another large JSON buffer.
            // Waiting happens on the worker; the main thread never waits for this job.
            CancelRecovery();
            var job=new ProjectSaveJob{viewRevision=snapshot.viewRevision};Text progress=null;
            ShowDialog(path==null?"Подготовка проекта":"Сохранение проекта",body=>
            {
                progress=Label(body,"Подготовка…",20);progress.gameObject.AddComponent<LayoutElement>().minHeight=100;
                AddButton(body,"Отменить",CloseDialog);
                AddLabel(body,"Готовится снимок проекта со всеми открытыми картами и ресурсами. Можно отменить и вернуться к карте.");
            });
            projectSaveJob=job;
            projectSaveTask=Task.Run(()=>
            {
                try
                {
                    job.Check("Ожидание автокопии…");
                    if(previousRecovery!=null)previousRecovery.GetAwaiter().GetResult();
                    job.Check("Подготовка проекта и ресурсов…");
                    var bytes=SnapshotBytes(snapshot,()=>job.cancelled,stage=>job.Check(stage));
                    job.Check();
                    if(path!=null)
                    {
                        job.Check("Запись проекта…");
                        job.saved=AtomicProjectFile.Write(path,bytes,()=>job.cancelled);
                    }
                    else job.bytes=bytes;
                }
                catch(OperationCanceledException){if(!job.cancelled)job.failure="Сохранение отменено.";}
                catch(Exception error){job.failure=error.Message;}
                finally {if(job.cancelled)job.bytes=null;}
            });
            StartCoroutine(WaitForProjectSave(job,projectSaveTask,context,progress,path!=null,afterSave,deliver));
        }
        IEnumerator WaitForProjectSave(ProjectSaveJob job,Task task,OpenContext context,Text progress,bool local,Action afterSave,Action<byte[]> deliver)
        {
            // Render the cancel control even when a small snapshot completes immediately.
            yield return null;
            while(!task.IsCompleted)
            {
                if(job.cancelled||projectSaveJob!=job)yield break;
                if(progress!=null)progress.text=job.progress;
                yield return null;
            }
            if(job.cancelled||projectSaveJob!=job)yield break;
            projectSaveJob=null;var bytes=job.bytes;job.bytes=null;
            if(job.failure!=null||task.IsFaulted||local&&!job.saved)
            {CloseDialog();Notify("Не удалось сохранить проект: "+(job.failure??(task.IsFaulted?task.Exception.GetBaseException().Message:"Запись отменена.")));yield break;}
            ObserveClassicView();ObserveVisualCamera();bool current=CurrentOpenContext(context)&&viewRecoveryRevision.Current==job.viewRevision;CloseDialog();
            if(!current)
            {Notify(local?"Снимок сохранён. В текущем проекте есть новые изменения; замена отменена.":"Проект изменился во время подготовки. Повторите экспорт.");yield break;}
            try
            {
                if(local){Notify("Проект сохранён.");if(afterSave!=null)afterSave();}
                else if(deliver!=null)deliver(bytes);
            }
            catch(Exception error){Notify("Не удалось завершить сохранение: "+error.Message);}
        }
    }
}
