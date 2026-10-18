using System;
using System.Collections.Generic;
using System.Globalization;
using MobileUDB.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        bool DbsGroupsReady { get { return !VisualActive && Draft.Count == 0 && (Tool == MobileTool.Select || Tool == MobileTool.Pan || Tool == MobileTool.Move || Tool == MobileTool.Transform); } }
        void PickDbsGroups()
        {
            if(!DbsGroupsReady){Notify("Перейдите в обычный 2D-режим и завершите текущую операцию.");return;}
            CancelDrag(); var context = CaptureOpenContext(); CloseDialog();
            GetComponent<MobileFileBridge>().ImportSettings((bytes,name)=>PrepareDbsGroups(bytes,name,context));
        }
        public void ImportDbsBytes(byte[] bytes,string filename)
        { CancelDrag(); PrepareDbsGroups(bytes,filename,CaptureOpenContext()); }
        void PrepareDbsGroups(byte[] bytes,string filename,OpenContext context)
        {
            if(!DbsGroupsReady){Notify("Импорт .dbs: перейдите в обычный 2D-режим и завершите текущую операцию.");return;}
            if(bytes == null || bytes.Length == 0 || bytes.Length > DbsSelectionGroups.MaximumBytes){Notify("Файл .dbs пуст или превышает 8 МиБ.");return;}
            BeginDocumentWork(context,job=>
            {
                job.Check("Настройки UDB…"); var settings = DbsMapSettings.Read(bytes); job.Check(); return settings;
            },result=>PreviewDbsGroups((DbsMapSettings)result,filename,context));
        }
        void PreviewDbsGroups(DbsMapSettings settings,string filename,OpenContext context)
        {
            if(!DbsGroupsReady || !CurrentOpenContext(context)){Notify("Карта изменилась. Импортируйте .dbs снова.");return;}
            var groups=settings.Groups;bool hasGroups=groups.HasMap(Map.name),hasGrid=settings.HasGrid(Map.name),hasLabels=settings.HasLabels(Map.name),hasView=settings.HasView(Map.name),hasBackground=settings.HasBackground(Map.name);
            if(!hasGroups&&!hasGrid&&!hasLabels&&!hasView&&!hasBackground)throw new InvalidOperationException("В .dbs нет настроек для карты "+Map.name+".");
            var counts=hasGroups?groups.Counts(Map):null;var expectedGrid=gridFrame.Copy();
            var imported=hasGrid?settings.GridFor(Map.name,gridFrame):null;
            var expectedView=CaptureClassicView();
            var expectedBackground=CurrentBackground;
            ShowDialog("Настройки из UDB",body=>
            {
                LineBatchCaption(body,"КАРТА · " + Map.name);
                if(hasGroups)
                {
                var note = Label(body,"Группы .dbs хранят индексы элементов. Используйте исходную карту из того же WAD, сохранив порядок элементов. Проверка имени и диапазонов не доказывает совпадение геометрии.",16);
                note.gameObject.AddComponent<LayoutElement>().preferredHeight = 110;
                LineBatchCaption(body,"ГРУППА · ВЕРШ. / СТЕНЫ / СЕКТ. / ОБЪЕКТЫ");
                var table = new System.Text.StringBuilder();
                for(int slot=0;slot<SelectionGroups.SlotCount;slot++)
                {
                    if(slot>0)table.Append('\n');
                    table.Append((slot+1) + "    " + counts[slot,0] + " / " + counts[slot,1] + " / " + counts[slot,2] + " / " + counts[slot,3]);
                }
                var rows = Label(body,table.ToString(),16); rows.alignment = TextAnchor.UpperLeft;
                rows.gameObject.AddComponent<LayoutElement>().preferredHeight = 200;
                LineBatchCaption(body,"ПРИМЕНИТЬ К ТЕКУЩЕЙ КАРТЕ");var actions = ShapeOptionRow(body);
                VisualThingCommand(actions,"Добавить группы",EditorIcon.Symbol.GroupAdd,()=>ApplyDbsGroups(groups,context,false));
                VisualThingCommand(actions,"Заменить все",EditorIcon.Symbol.Group,()=>ApplyDbsGroups(groups,context,true));
                var help = Label(body,"Добавить: сохранить прежние отметки групп. Заменить: взять все 10 групп из файла. Одно Undo отменяет импорт; результат сохраняется в .mobileudb.",16);
                help.gameObject.AddComponent<LayoutElement>().preferredHeight = 80;
                }
                if(hasGrid)
                {
                    LineBatchCaption(body,"СЕТКА");var summary=Label(body,"Шаг: "+imported.step.ToString("R",CultureInfo.InvariantCulture)+" · Угол: "+Degrees(imported.rotation)+"°\nНачало: "+imported.originX.ToString("R",CultureInfo.InvariantCulture)+" / "+imported.originY.ToString("R",CultureInfo.InvariantCulture),16);
                    summary.gameObject.AddComponent<LayoutElement>().preferredHeight=60;
                    AddButton(body,"Применить сетку",()=>
                    {
                        if(!DbsGroupsReady||!CurrentOpenContext(context)||!DbsMapSettings.SameGrid(expectedGrid,gridFrame)){Notify("Карта или сетка изменились. Импортируйте .dbs снова.");return;}
                        if(DbsMapSettings.SameGrid(imported,gridFrame)){Notify("Сетка уже совпадает.");return;}
                        gridFrame=imported.Validated();mapGrids[MapNaming.Key(Map)]=gridFrame.Copy();Changed();context=CaptureOpenContext();expectedGrid=gridFrame.Copy();
                        Notify("Сетка UDB применена. Геометрия не изменена; сетка сохраняется в проекте.");
                    });
                    var help=Label(body,"Импорт меняет шаг, угол и начало. Привязка и режим переноса сохраняются. Сетка находится вне истории Undo геометрии; изменить её можно через «Ещё → Сетка».",16);
                    help.gameObject.AddComponent<LayoutElement>().preferredHeight=100;
                }
                if(hasLabels)
                {
                    LineBatchCaption(body,"ПОДПИСИ ТЕГОВ");AddLabel(body,"Подписей в файле: "+settings.LabelsFor(Map.name).Count+". Меняются только имена; числовые теги и связи сохраняются.");
                    AddButton(body,"Добавить / обновить подписи",()=>ApplyDbsLabels(settings,context,false));
                    AddButton(body,"Заменить все подписи",()=>ApplyDbsLabels(settings,context,true));
                    AddLabel(body,"Добавить: сохранить остальные имена. Заменить: оставить только подписи из файла. Одно Undo отменяет импорт.");
                }
                if(hasView)
                {
                    var view=settings.ViewFor(Map.name);LineBatchCaption(body,"ОБЗОР КАРТЫ");
                    AddLabel(body,"Центр: "+view.x.ToString("R",CultureInfo.InvariantCulture)+" / "+view.y.ToString("R",CultureInfo.InvariantCulture)+" · Масштаб: "+view.scale.ToString("R",CultureInfo.InvariantCulture));
                    AddButton(body,"Применить обзор",()=>
                    {
                        if(!DbsGroupsReady||!CurrentOpenContext(context)||!MapView.Same(expectedView,CaptureClassicView())){Notify("Карта или обзор изменились. Импортируйте .dbs снова.");return;}
                        ApplyClassicView(view);ObserveClassicView();expectedView=CaptureClassicView();Notify("Обзор UDB применён. Карта и Undo не изменены.");
                    });
                    AddLabel(body,"Меняются только центр и масштаб. Масштаб ограничен диапазоном мобильного обзора 0,025–20. Обзор сохраняется в проекте и автокопии; Undo геометрии его не меняет.");
                }
                if(hasBackground)
                {
                    var background=settings.BackgroundFor(Map.name);LineBatchCaption(body,"ФОН КАРТЫ");
                    AddLabel(body,(background.visible?background.name:"Выключен")+" · "+(background.source==2?"файл":background.source==1?"пол":"стена")+"\nСдвиг: "+background.offsetX+" / "+background.offsetY+" · Масштаб: "+background.scaleX+" / "+background.scaleY+"%");
                    AddButton(body,"Применить фон",()=>
                    {if(!DbsGroupsReady||!CurrentOpenContext(context)||!ReferenceEquals(expectedBackground,CurrentBackground)){Notify("Карта или фон изменились. Импортируйте .dbs снова.");return;}try {SetMapBackground(background);expectedBackground=CurrentBackground;Notify(background.visible&&background.source==2?"Настройки фона применены. Выберите изображение в «Ещё → Фон».":"Настройки фона UDB применены. Карта и Undo не изменены.");}catch(Exception error){Notify(error.Message);}});
                    AddLabel(body,".dbs хранит имя ресурса или путь к файлу. Ресурсы нужно подключить; файл изображения выбрать через «Ещё → Фон». Внешние пути автоматически не открываются.");
                }
                AddLabel(body,"Остальные настройки UDB пока не переносятся.");
            });
        }
        void DbsExportMenu()
        {
            if(!DbsGroupsReady){Notify("Перейдите в обычный 2D-режим и завершите текущую операцию.");return;}
            var context=CaptureOpenContext();
            ShowDialog("Экспорт .dbs",body=>
            {
                LineBatchCaption(body,"СЕТКА · ОБЗОР · ФОН · ГРУППЫ · ПОДПИСИ");
                AddLabel(body,"В файл попадут текущая и ранее открытые карты с сеткой, центром и масштабом обзора, размещением фона, 10 группами выделения и подписями тегов. Используйте его с экспортированным WAD, сохранив порядок элементов.");
                AddButton(body,"Сохранить .dbs…",()=>
                {if(!DbsGroupsReady||!CurrentOpenContext(context)){Notify("Проект изменился. Откройте экспорт снова.");return;}BeginDbsExport(bytes=>GetComponent<MobileFileBridge>().Export(bytes,"settings.dbs"));});
                AddLabel(body,"Изображение фона и его видимость в процентах не включаются в .dbs: UDB хранит только путь / имя и размещение. Для файла на компьютере потребуется указать путь заново. Обычное сохранение WAD автоматически создаёт соседний DBS с ресурсами и профилями.");
            });
        }
        void BeginDbsExport(Action<byte[]> deliver)
        {
            if(!DbsGroupsReady)throw new InvalidOperationException("Завершите текущую операцию перед экспортом .dbs.");
            CancelDrag();RememberClassicView();var context=CaptureOpenContext();var maps=new List<MapDocument>{Map.Clone()};
            var grids=new Dictionary<string,GridFrame>(StringComparer.OrdinalIgnoreCase){{Map.name,gridFrame.Validated()}};
            var views=new Dictionary<string,MapView>(StringComparer.OrdinalIgnoreCase){{Map.name,CaptureClassicView()}};
            var backgrounds=new Dictionary<string,MapBackground>(StringComparer.OrdinalIgnoreCase){{Map.name,CurrentBackground==null?null:CurrentBackground.Copy()}};
            foreach(var pair in editedMaps)
            {
                if(string.Equals(pair.Key,MapNaming.Key(Map),StringComparison.OrdinalIgnoreCase))continue;
                maps.Add(pair.Value.Clone());GridFrame frame;grids.Add(pair.Value.name,mapGrids.TryGetValue(pair.Key,out frame)?frame.Validated():new GridFrame());
                MapView view;views.Add(pair.Value.name,mapViews.TryGetValue(pair.Key,out view)?view.Validated():null);
                MapBackground background;backgrounds.Add(pair.Value.name,mapBackgrounds.TryGetValue(pair.Key,out background)?background.Copy():null);
            }
            long settingsRevision=viewRecoveryRevision.Current;
            BeginDocumentWork(context,job=>{job.Check("Настройки .dbs…");var bytes=DbsMapSettings.Export(maps,name=>grids[name],name=>views[name],name=>backgrounds[name]);job.Check();return bytes;},result=>{ObserveClassicView();if(settingsRevision!=viewRecoveryRevision.Current){Notify("Настройки изменились во время экспорта. Сохраните .dbs снова.");return;}deliver((byte[])result);});
        }
        void ApplyDbsLabels(DbsMapSettings settings,OpenContext context,bool replace)
        {
            if(!DbsGroupsReady||!CurrentOpenContext(context)){Notify("Карта изменилась. Импортируйте .dbs снова.");return;}
            try {var plan=settings.PlanLabels(Map,replace);if(plan.ChangeCount==0){Notify("Подписи уже совпадают.");return;}if(Edit(map=>plan.Apply(map),"Подписи UDB импортированы. Доступно одно Undo."))TagLabelsMenu();}
            catch(Exception error){Notify(error.Message);}
        }
        void ApplyDbsGroups(DbsSelectionGroups groups,OpenContext context,bool replace)
        {
            if(!DbsGroupsReady || !CurrentOpenContext(context)){Notify("Карта изменилась. Импортируйте .dbs снова.");return;}
            try
            {
                var plan = groups.Plan(Map,replace);
                if(plan.ChangeCount == 0){Notify("Группы уже совпадают. Ничего не изменено.");return;}
                int count = plan.ChangeCount;
                if(Edit(map=>plan.Apply(map),"Группы UDB импортированы · элементов: " + count + ". Доступно одно Undo.")){CloseDialog();SelectionGroupsMenu();}
            }
            catch(Exception error){Notify(error.Message);}
        }
    }
}
