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
        sealed class WadExportJob
        {
            public volatile bool cancelled;
            public volatile string progress = "Подготовка…";
            public byte[] bytes;
            public string failure, warnings = "";
        }
        WadExportJob wadExportJob;
        Task wadExportTask;
        void OnDisable() { CancelFileRead(); CancelDocumentOpen(); CancelWadExport(); CancelProjectSave(); CancelResourceImport(); CancelRecovery(); }
        void CancelWadExport()
        {
            if (wadExportJob != null) { wadExportJob.cancelled = true; wadExportJob = null; }
        }
        void WadExportMenu()
        { MapExportMenu(false); }
        void Pk3ExportMenu()
        { MapExportMenu(true); }
        void MapExportMenu(bool pk3)
        {
            ShowDialog(pk3 ? "Экспорт PK3" : "Экспорт WAD",body =>
            {
                AddButton(body,"С узлами — подготовить к запуску…",() => StartMapExport(true,pk3));
                AddButton(body,"Без узлов — для дальнейшего редактирования…",() => StartMapExport(false,pk3));
                AddLabel(body,pk3 ? "Текущая и ранее открытые карты с правками заменяются в копии исходного PK3. Остальные карты и файлы сохраняются. Отдельно подключённые ресурсы остаются внешними." : "Включаются текущая и ранее открытые карты с вашими изменениями. Остальные карты и ресурсы исходного WAD сохраняются. Отдельно подключённые текстурпаки остаются внешними ресурсами.");
                if (!NativeNodeBuilder.Available) AddLabel(body,"Построитель узлов недоступен на этой платформе. Доступен экспорт без узлов.");
            });
        }
        void StartMapExport(bool nodes,bool pk3)
        {
            string filename = pk3 ? "edited.pk3" : Map.name + ".wad";
            BeginMapExport(nodes,pk3,bytes => GetComponent<MobileFileBridge>().Export(bytes,filename));
        }
        void BeginWadExport(bool nodes,Action<byte[]> deliver)
        { BeginMapExport(nodes,false,deliver); }
        void BeginPk3Export(bool nodes,Action<byte[]> deliver)
        { BeginMapExport(nodes,true,deliver); }
        void BeginMapExport(bool nodes,bool pk3,Action<byte[]> deliver)
        {BeginMapExportCore(nodes,pk3,deliver,true);}
        void BeginMapExportCore(bool nodes,bool pk3,Action<byte[]> deliver,bool prompt)
        {
            if (Draft.Count != 0 || Tool == MobileTool.Paste) { Notify("Сначала завершите или отмените построение."); return; }
            if (wadExportTask != null && !wadExportTask.IsCompleted) { Notify("Предыдущее построение ещё завершается. Повторите экспорт после его окончания."); return; }
            if (nodes && !NativeNodeBuilder.Available) { Notify("Построитель узлов недоступен на этой платформе."); return; }
            if (pk3 && sourceMapPackage == null) { Notify("Сначала откройте PK3 с картами. Его исходный архив должен оставаться подключённым."); return; }
            CancelDrag();EnsureClassicView();
            // Snapshot on Unity's thread. The worker never reads mutable editor state.
            var snapshots = new Dictionary<string,MapDocument>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in editedMaps) snapshots[pair.Key] = pair.Value.Clone();
            string cameraWarning;snapshots[MapNaming.Key(Map)] = SnapshotMapWithCamera(out cameraWarning);var sourceNames=MapNaming.Sources(snapshots.Values);
            var source = archive ?? new DoomWad(); string tempRoot = Application.temporaryCachePath;
            string originalPk3 = pk3 ? sourceMapPackage.data : null;
            var job = new WadExportJob{warnings=cameraWarning==null?"":cameraWarning+"\n"}; Text progress = null;
            ShowDialog(pk3 ? "Подготовка PK3" : "Подготовка WAD",body =>
            {
                progress = Label(body,"Подготовка…",20); progress.gameObject.AddComponent<LayoutElement>().minHeight = 100;
                AddButton(body,"Отменить",CloseDialog);
                AddLabel(body,nodes ? "Построение выполняется в фоне. После отмены текущая карта завершится, файл сохранён не будет." : "Подготавливается снимок карт. Исходный проект не изменяется.");
            });
            wadExportJob = job;
            wadExportTask = Task.Run(() =>
            {
                try
                {
                    if (job.cancelled) return;
                    if (nodes || pk3)
                    {
                        var built = new List<DoomWad>(); int index = 0;
                        foreach (var map in snapshots.Values)
                        {
                            if (job.cancelled) return;
                            job.progress = (nodes ? "Узлы: " : "Карта: ") + map.name + " · " + (++index) + " / " + snapshots.Count;
                            string warning = null; byte[] bytes = nodes ? NativeNodeBuilder.BuildMap(map,tempRoot,out warning) : new DoomWad().Export(map);
                            if (job.cancelled) return;
                            built.Add(DoomWad.Read(bytes));
                            if (!string.IsNullOrEmpty(warning) && job.warnings.Length < 4000) job.warnings += map.name + ": " + warning + "\n";
                        }
                        if (job.cancelled) return;
                        job.progress = "Объединение карт и ресурсов…";
                        job.bytes = pk3 ? Pk3Maps.Export(Convert.FromBase64String(originalPk3),built,MaxResourceBytes,() => job.cancelled,sourceNames) : source.ExportBuiltMaps(built,sourceNames);
                    }
                    else job.bytes = source.Export(snapshots.Values);
                    if (job.cancelled) job.bytes = null;
                }
                catch (Exception error) { job.bytes = null; job.failure = error.Message; }
            });
            StartCoroutine(WaitForWadExport(job,wadExportTask,progress,nodes,pk3,deliver,prompt));
        }
        IEnumerator WaitForWadExport(WadExportJob job,Task task,Text progress,bool nodes,bool pk3,Action<byte[]> deliver,bool prompt)
        {
            while (!task.IsCompleted)
            {
                if (wadExportJob != job || job.cancelled) yield break;
                if (progress != null) progress.text = job.progress;
                yield return null;
            }
            if (wadExportJob != job || job.cancelled) yield break;
            wadExportJob = null;
            if(!prompt&&job.failure==null){CloseDialog();deliver(job.bytes);yield break;}
            string kind = pk3 ? "PK3" : "WAD";
            ShowDialog(job.failure == null ? kind + " подготовлен" : "Экспорт не выполнен",body =>
            {
                if (job.failure != null)
                {
                    AddLabel(body,job.failure);
                    AddButton(body,"Вернуться к экспорту",() => MapExportMenu(pk3));
                    AddButton(body,"Проверить карту…",CheckMap);
                    return;
                }
                AddLabel(body,nodes ? "Узлы построены. Выберите место сохранения " + kind + "." : kind + " подготовлен без узлов. Перед запуском в классическом движке их нужно построить.");
                if (!string.IsNullOrEmpty(job.warnings)) AddLabel(body,job.warnings);
                AddButton(body,"Сохранить " + kind + "…",() => { CloseDialog(); deliver(job.bytes); });
            });
        }
    }
}
