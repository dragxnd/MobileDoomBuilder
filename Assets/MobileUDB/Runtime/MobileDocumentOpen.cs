using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using MobileUDB.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        sealed class OpenContext
        {
            public MapDocument map;
            public long revision;
            public DoomWad archive;
            public List<MobileResourceArchive> descriptors;
            public List<ResourceArchive> sources;
        }
        sealed class PreparedDocument
        {
            public DoomWad archive;
            public byte[] source;
            public MobileProject project;
            public List<MobileResourceArchive> descriptors;
            public List<ResourceArchive> sources;
            public MobileResourceArchive package;
            public DoomResources catalog;
            public string resourceError;
            public Dictionary<string,MapDocument> maps;
        }
        sealed class DocumentOpenJob
        {
            public volatile bool cancelled;
            public volatile string progress = "Подготовка…";
            public object result;
            public string failure;
            public void Check(string stage = null)
            { if (cancelled) throw new OperationCanceledException(); if (stage != null) progress = stage; }
        }
        DocumentOpenJob documentOpenJob;
        Task documentOpenTask;
        internal bool DocumentOpeningBusy { get { return documentOpenTask != null && !documentOpenTask.IsCompleted; } }
        void CancelDocumentOpen()
        {
            var job = documentOpenJob; documentOpenJob = null;
            if (job != null) { job.cancelled = true; job.result = null; }
        }
        OpenContext CaptureOpenContext()
        {
            return new OpenContext { map = Map,revision = recoveryRevision.Current,archive = archive,
                descriptors = resourceArchives,sources = resourceSources };
        }
        bool CurrentOpenContext(OpenContext context)
        {
            return ReferenceEquals(context.map,Map) && context.revision == recoveryRevision.Current &&
                ReferenceEquals(context.archive,archive) && ReferenceEquals(context.descriptors,resourceArchives) && ReferenceEquals(context.sources,resourceSources);
        }
        static List<MobileResourceArchive> CopyDescriptors(List<MobileResourceArchive> originals)
        {
            var copies = new List<MobileResourceArchive>();
            foreach (var item in originals) copies.Add(new MobileResourceArchive { name = item.name,data = item.data,id = item.id,kind = item.kind,location=item.location });
            return copies;
        }
        void BeginDocumentWork(OpenContext context,Func<DocumentOpenJob,object> prepare,Action<object> complete)
        {BeginDocumentWork(context,prepare,complete,"Открытие файла");}
        void BeginDocumentWork(OpenContext context,Func<DocumentOpenJob,object> prepare,Action<object> complete,string title)
        {
            if (DocumentOpeningBusy) { Notify("Предыдущее открытие ещё завершается. Дождитесь его окончания."); return; }
            if (!CurrentOpenContext(context)) { Notify("Проект изменился. Выберите файл повторно."); return; }
            var job = new DocumentOpenJob(); Text progress = null;
            ShowDialog(title,body =>
            {
                progress = Label(body,"Подготовка…",20); progress.gameObject.AddComponent<LayoutElement>().minHeight = 100;
                AddButton(body,"Отменить",CloseDialog);
                AddLabel(body,"Разбор карты и каталог ресурсов готовятся в фоне. Отмена сохраняет текущий проект; текущий этап завершится без применения результата.");
            });
            documentOpenJob = job;
            documentOpenTask = Task.Run(() =>
            {
                try { job.Check(); job.result = prepare(job); job.Check(); }
                catch (OperationCanceledException) { if (!job.cancelled) job.failure = "Открытие отменено."; }
                catch (Exception error) { job.failure = error.Message; }
                finally { if (job.cancelled) job.result = null; }
            });
            StartCoroutine(WaitForDocumentOpen(job,documentOpenTask,context,progress,complete));
        }
        IEnumerator WaitForDocumentOpen(DocumentOpenJob job,Task task,OpenContext context,Text progress,Action<object> complete)
        {
            // Even a tiny completed file gives the modal a frame before handoff.
            yield return null;
            while (!task.IsCompleted)
            {
                if (job.cancelled || documentOpenJob != job) yield break;
                if (progress != null) progress.text = job.progress;
                yield return null;
            }
            if (job.cancelled || documentOpenJob != job) yield break;
            documentOpenJob = null; object result = job.result; job.result = null;
            if (job.failure != null || task.IsFaulted)
            { CloseDialog(); Notify("Не удалось открыть: " + (job.failure ?? task.Exception.GetBaseException().Message)); yield break; }
            if (!CurrentOpenContext(context)) { CloseDialog(); Notify("Проект изменился во время открытия. Результат отменён."); yield break; }
            CloseDialog();
            // Only the completed current result can mutate Unity/editor state.
            try { complete(result); } catch (Exception error) { Notify("Не удалось открыть: " + error.Message); }
        }
        static void CheckPreparedBudget(PreparedDocument prepared)
        {
            long backgroundCost=prepared.project==null?0:BackgroundBytes(prepared.project.backgrounds);long cost = 0;
            for (int i = 0; i < prepared.sources.Count; i++) cost += ResourceCost(prepared.sources[i],(int)(prepared.descriptors[i].data.Length * 3L / 4));
            if (cost > MaxResourceBytes || cost + backgroundCost + (prepared.source == null ? 0 : prepared.source.LongLength) > MaxProjectWadBytes)
                throw new InvalidDataException("Карта вместе с ресурсами превышает предел памяти проекта.");
        }
        static void PrepareDocumentCatalog(PreparedDocument prepared,DocumentOpenJob job)
        {
            job.Check("Каталог текстур и спрайтов…");
            var external = DoomResources.BuildArchives(prepared.sources); job.Check();
            prepared.catalog = external;
            if (prepared.archive != null)
            {
                var sources = new List<ResourceArchive>(prepared.sources) { ResourceArchive.FromWad(prepared.archive) };
                try { prepared.catalog = DoomResources.BuildArchives(sources); }
                catch (Exception error) { prepared.resourceError = "Ресурсы карты не загружены: " + error.Message; }
            }
            job.Check();
        }
        public void ImportBytes(byte[] bytes,string filename = null)
        {
            if(DbsSelectionGroups.IsFilename(filename)){ImportDbsBytes(bytes,filename);return;}
            if (bytes == null || bytes.Length > 360L * 1024 * 1024) { Notify("Файл слишком большой для мобильного редактора."); return; }
            CancelDrag(); var context = CaptureOpenContext();
            var descriptors = CopyDescriptors(resourceArchives); var sources = new List<ResourceArchive>(resourceSources);
            BeginDocumentWork(context,job =>
            {
                var prepared = new PreparedDocument { descriptors = descriptors,sources = sources };
                if (bytes.Length >= 2 && bytes[0] == 'P' && bytes[1] == 'K') PreparePk3Document(prepared,bytes,filename,job);
                else if (bytes.Length >= 4 && (Encoding.ASCII.GetString(bytes,0,4) == "IWAD" || Encoding.ASCII.GetString(bytes,0,4) == "PWAD"))
                { job.Check("Разбор WAD…"); prepared.archive = DoomWad.Read(bytes); prepared.source = bytes; }
                else PrepareSavedDocument(prepared,bytes,job);
                job.Check(); CheckPreparedBudget(prepared);
                if (prepared.project == null && prepared.archive.EditableMapNames().Count == 0) throw new InvalidDataException("В архиве нет доступных бинарных карт Doom / Hexen. Ресурсники подключайте через «Файл → Ресурсы».");
                PrepareDocumentCatalog(prepared,job); return prepared;
            },result =>
            {
                var prepared = (PreparedDocument)result;
                if (prepared.project == null) ShowPreparedMaps(prepared,context);
                else CommitPreparedDocument(prepared,prepared.project.map);
            });
        }
        static void PrepareSavedDocument(PreparedDocument prepared,byte[] bytes,DocumentOpenJob job)
        {
            job.Check("Разбор проекта…");
            // JsonUtility.FromJson supports plain serializable classes on workers.
            var project = JsonUtility.FromJson<MobileProject>(Encoding.UTF8.GetString(bytes)); job.Check();
            if (project == null || project.format != "MobileUDB" || project.map == null) throw new InvalidDataException("Это не проект MobileUDB.");
            project.gameProfile = ValidProfile(project.gameProfile); project.actionEngine = ValidActionEngine(project.actionEngine);
            project.geometryMergeMode = ValidGeometryMode(project.geometryMergeMode).ToString(); project.pasteTagMode = ValidPasteTagMode(project.pasteTagMode).ToString();
            project.drawTextures = (project.drawTextures ?? new DrawTextureOptions()).Validated(MapFormat.Doom);
            project.grid=(project.grid??new GridFrame()).Validated();
            var gridNames=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if(project.grids!=null)foreach(var entry in project.grids)
            {job.Check();if(entry==null||entry.grid==null)throw new InvalidDataException("Пустые настройки сетки карты.");new MapDocument{name=entry.name}.Validate();if(!gridNames.Add(entry.name))throw new InvalidDataException("Повторяющиеся настройки сетки карты.");entry.grid=entry.grid.Validated();}
            BackgroundBytes(project.backgrounds,true);project.map.Validate(); prepared.maps = new Dictionary<string,MapDocument>(StringComparer.OrdinalIgnoreCase);
            var viewNames=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if(project.views!=null)foreach(var entry in project.views)
            {job.Check();if(entry==null||entry.view==null)throw new InvalidDataException("Пустые настройки обзора карты.");MapNaming.RequireName(entry.name);if(!viewNames.Add(entry.name))throw new InvalidDataException("Повторяющиеся настройки обзора карты.");entry.view=entry.view.Validated();}
            if (project.editedMaps != null) foreach (var map in project.editedMaps)
            { job.Check(); if (map == null) throw new InvalidDataException("Пустая карта в проекте."); map.Validate();string key=MapNaming.Key(map);if (prepared.maps.ContainsKey(key)) throw new InvalidDataException("Повторяющаяся исходная карта в проекте."); prepared.maps.Add(key,map); }
            var allMaps=new List<MapDocument>{project.map};allMaps.AddRange(prepared.maps.Values);MapNaming.Sources(allMaps);
            job.Check("Исходные карты проекта…");
            if (project.sourceWad != null && project.sourceWad.Length > MaxProjectWadBytes * 4 / 3 + 4) throw new InvalidDataException("Исходный WAD превышает предел памяти проекта.");
            prepared.source = string.IsNullOrEmpty(project.sourceWad) ? null : Convert.FromBase64String(project.sourceWad);
            project.sourceWad = null;
            job.Check(); prepared.archive = prepared.source == null ? null : DoomWad.Read(prepared.source);
            if(prepared.archive!=null)
            {
                var names=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);var sourceMaps=new HashSet<string>(prepared.archive.MapNames(),StringComparer.OrdinalIgnoreCase);
                foreach(var map in allMaps){string key=MapNaming.Key(map);if(!string.IsNullOrEmpty(map.sourceName)&&!sourceMaps.Contains(key))throw new InvalidDataException("Исходная карта переименования отсутствует: "+key);names.Add(key,map.name);}
                prepared.archive.ValidateMapNames(names);
            }
            prepared.descriptors = project.resources ?? new List<MobileResourceArchive>();
            prepared.sources = ParseResourceArchives(prepared.descriptors,prepared.source == null ? 0 : prepared.source.Length,() => job.cancelled,text => job.progress = text);
            prepared.package = FindSourcePackage(project.sourcePk3ResourceId,prepared.descriptors,prepared.sources);
            if (prepared.package != null && prepared.archive == null) throw new InvalidDataException("В проекте PK3 отсутствуют исходные карты.");
            prepared.project = project; job.Check();
        }
        static void PreparePk3Document(PreparedDocument prepared,byte[] bytes,string filename,DocumentOpenJob job)
        {
            job.Check("Распаковка PK3…"); var package = ResourceArchive.ReadAttachment(bytes,filename,"archive",MaxResourceBytes); job.Check();
            string encoded = Convert.ToBase64String(bytes); job.Check();
            int existing = prepared.descriptors.FindIndex(d => d.data == encoded);
            if (existing < 0)
            {
                if (prepared.descriptors.Count >= 32) throw new InvalidDataException("Допускается не более 32 архивов ресурсов.");
                string name = string.IsNullOrEmpty(filename) || filename == "Ресурсы" || filename == "MobileUDB-import" ? "Карты и ресурсы PK3" : Path.GetFileName(filename.Replace('\\','/'));
                prepared.descriptors.Add(new MobileResourceArchive { name = name,data = encoded,id = Guid.NewGuid().ToString("N") });
                prepared.sources.Add(package); existing = prepared.descriptors.Count - 1;
            }
            CheckPreparedBudget(prepared); long cost = 0;
            for (int i = 0; i < prepared.sources.Count; i++) cost += ResourceCost(prepared.sources[i],(int)(prepared.descriptors[i].data.Length * 3L / 4));
            job.Check("Карты PK3…"); prepared.archive = Pk3Maps.Read(package,MaxProjectWadBytes - cost);
            job.Check(); prepared.source = prepared.archive.Export(new MapDocument[0]);
            prepared.package = prepared.descriptors[existing];
            if (string.IsNullOrEmpty(prepared.package.id)) prepared.package.id = Guid.NewGuid().ToString("N");
        }
        void ShowPreparedMaps(PreparedDocument prepared,OpenContext context)
        {
            ShowDialog(prepared.package == null ? "Выберите карту" : "Карты PK3",body =>
            {
                if (prepared.package != null) AddLabel(body,"Архив подключится как ресурс и сохранится внутри проекта. Правки можно экспортировать в PK3 или отдельный WAD.");
                foreach (string name in prepared.archive.EditableMapNames())
                {
                    string chosen = name;
                    AddButton(body,chosen,() => BeginDocumentWork(context,job =>
                    { job.Check("Карта: " + chosen); var map = prepared.archive.Import(chosen); map.Validate(); job.Check(); return map; },
                    result => CommitPreparedDocument(prepared,(MapDocument)result)));
                }
            });
        }
        void CommitPreparedDocument(PreparedDocument prepared,MapDocument map)
        {
            SaveRecovery();wadReference=null;wadConfiguration=null;wadMapResources.Clear();missingWadResources.Clear();
            archive = prepared.archive; sourceWad = prepared.source; sourceMapPackage = prepared.package;
            resourceArchives = prepared.descriptors; resourceSources = prepared.sources; editedMaps.Clear();mapGrids.Clear();mapViews.Clear();mapBackgrounds.Clear();
            if (prepared.maps != null) foreach (var pair in prepared.maps) editedMaps.Add(pair.Key,pair.Value);
            if (prepared.project != null)
            {
                gameProfile = prepared.project.gameProfile; actionEngine = prepared.project.actionEngine;
                geometryMergeMode = ValidGeometryMode(prepared.project.geometryMergeMode); pasteTagMode = ValidPasteTagMode(prepared.project.pasteTagMode);
                drawTextures = prepared.project.drawTextures;
                if(prepared.project.grids!=null)foreach(var entry in prepared.project.grids)mapGrids[entry.name]=entry.grid.Validated();
                string key=MapNaming.Key(prepared.project.map);if(!mapGrids.ContainsKey(key))mapGrids[key]=prepared.project.grid.Validated();
                if(prepared.project.views!=null)foreach(var entry in prepared.project.views)mapViews[entry.name]=entry.view.Validated();
                if(prepared.project.backgrounds!=null)foreach(var entry in prepared.project.backgrounds)mapBackgrounds[entry.name]=entry.background.Validated();
            }
            Replace(map,prepared.catalog,prepared.resourceError);
        }
        void BeginMapSwitch(string name)
        {
            if (string.Equals(name,MapNaming.Key(Map),StringComparison.OrdinalIgnoreCase)) { CloseDialog(); Notify("Карта уже открыта: " + Map.name); return; }
            if(wadReference!=null)
            {
                var prepared=new PreparedWadMap{file=wadReference,settings=wadConfiguration,document=new PreparedDocument{archive=archive,source=sourceWad,descriptors=CopyDescriptors(resourceArchives),sources=new List<ResourceArchive>(resourceSources)}};
                OpenPreparedWad(prepared,name,CaptureOpenContext(),false);return;
            }
            CancelDrag(); var context = CaptureOpenContext(); MapDocument saved;
            editedMaps.TryGetValue(name,out saved); var catalog = resources; string warning = resourceError;
            BeginDocumentWork(context,job =>
            { job.Check("Карта: " + name); var map = saved == null ? context.archive.Import(name) : saved.Clone(); map.Validate(); job.Check(); return map; },
            result => { SaveRecovery();RememberCurrentMap(); Replace((MapDocument)result,catalog,warning); });
        }
    }
}
