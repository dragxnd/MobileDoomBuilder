using System;
using System.Collections;
using System.Collections.Generic;
using MobileUDB.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        Coroutine validationRoutine;
        sealed class MapCheckContext
        {
            public OpenContext document;
            public DoomResources resources;
            public ActionCatalog actions;
            public ThingCatalog things;
            public GameSky sky;
        }
        bool CurrentMapCheck(MapCheckContext context)
        {return CurrentOpenContext(context.document)&&ReferenceEquals(context.resources,resources)&&ReferenceEquals(context.actions,Actions)&&ReferenceEquals(context.things,thingCatalog)&&ReferenceEquals(context.sky,Map.sky);}
        void MapCheckExpired()
        {
            validationRoutine=null;
            ShowDialog("Проверка устарела",body=>
            {AddLabel(body,"Карта, профиль или ресурсы изменились. Запустите проверку снова, чтобы перейти к актуальным элементам.");AddButton(body,"Проверить снова",CheckMap);});
        }
        void CheckMap()
        {
            if (Draft.Count != 0 || Tool == MobileTool.Paste) { Notify("Сначала завершите или отмените построение."); return; }
            var actions=Actions;
            var context=new MapCheckContext{document=CaptureOpenContext(),resources=resources,actions=actions,things=thingCatalog,sky=Map.sky};
            ShowDialog("Проверка карты",body =>
            {
                var progress = Label(body,"Проверка геометрии, объектов, текстур, действий и тегов…",20); progress.gameObject.AddComponent<LayoutElement>().minHeight = 90;
                AddLabel(body,"Действия: "+actions.label+". Неизвестные коды сравниваются с выбранным профилем движка.");
                AddLabel(body,"Можно закрыть окно, чтобы прервать проверку. Карта не изменяется.");
                validationRoutine = StartCoroutine(RunMapChecks(progress,context));
            });
        }
        IEnumerator RunMapChecks(Text progress,MapCheckContext context)
        {
            // First yield guarantees the modal and coroutine handle exist before completion.
            yield return null;
            var found = new List<MapIssue>(); bool limited = false; string failure = null;
            using (var checks = MapChecks.Scan(context.document.map,context.resources,2000000,context.actions,context.things).GetEnumerator())
            {
                bool running = true;
                while (running)
                {
                    if(!CurrentMapCheck(context)){MapCheckExpired();yield break;}
                    var slice = System.Diagnostics.Stopwatch.StartNew();
                    do
                    {
                        try { running = checks.MoveNext(); }
                        catch (Exception error) { failure = error.Message; running = false; }
                        if (running && checks.Current != null) found.Add(checks.Current);
                        if (found.Count >= 500) { limited = true; running = false; }
                    } while (running && slice.ElapsedMilliseconds < 6);
                    if (progress != null) progress.text = "Проверка… Найдено: " + found.Count;
                    if (running) yield return null;
                }
            }
            validationRoutine = null;
            ShowMapIssues(found,0,limited,failure,context);
        }
        void ShowMapIssues(List<MapIssue> issues,int page,bool limited,string failure,MapCheckContext context)
        {
            if(!CurrentMapCheck(context)){MapCheckExpired();return;}
            const int pageSize = 6; int pages = Math.Max(1,(issues.Count + pageSize - 1) / pageSize); page = Math.Max(0,Math.Min(page,pages - 1));
            ShowDialog("Результаты проверки",body =>
            {
                int errors = 0; foreach (var issue in issues) if (issue.error) errors++;
                AddLabel(body,issues.Count == 0 && failure == null ? "В выполненных проверках проблем не найдено." : "Ошибки: " + errors + " · Замечания: " + (issues.Count - errors) + "\nСтраница " + (page + 1) + " / " + pages);
                if (limited) AddLabel(body,"Показаны первые 500 проблем. Исправьте их и запустите проверку снова.");
                if (failure != null) AddLabel(body,"Проверка не завершена: " + failure);
                bool mixed = MapFormats.IsUniversal(Map.format);
                bool noWalls = resources == null || (resources.WallCount == 0 && (!mixed || resources.FlatCount == 0));
                bool noFlats = resources == null || (resources.FlatCount == 0 && (!mixed || resources.WallCount == 0));
                if (noWalls || noFlats) AddLabel(body,"Поиск неизвестных текстур пропущен: " + (noWalls && noFlats ? "стены и плоскости" : noWalls ? "стены" : "плоскости") + ". Подключите IWAD / ресурсы.");
                for (int i = page * pageSize; i < Math.Min(issues.Count,(page + 1) * pageSize); i++)
                {
                    var issue = issues[i];
                    var button = AddButton(body,(issue.error ? "Ошибка · " : "Замечание · ") + issue.message,() => FocusMapIssue(issue,context));
                    button.GetComponent<LayoutElement>().preferredHeight = 90;
                    var label = button.GetComponentInChildren<Text>(); label.fontSize = 18; label.resizeTextMaxSize = 18; label.resizeTextMinSize = 14;
                    button.interactable = issue.elements.Length > 0 && issue.position.HasValue;
                    if(issue.unusedTexture!=null)
                        AddButton(body,"Удалить текстуру · #"+issue.unusedTexture.line+(issue.unusedTexture.front?" П · ":" З · ")+(issue.unusedTexture.upper?"верх…":"низ…"),()=>RemoveUnusedTexture(issue,context));
                }
                if (pages > 1)
                {
                    var row = Rect("Check pages",body); row.gameObject.AddComponent<LayoutElement>().preferredHeight = 60;
                    var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>(); layout.spacing = 8; layout.childControlHeight = layout.childControlWidth = true; layout.childForceExpandWidth = true;
                    AddButton(row,"←",() => ShowMapIssues(issues,page - 1,limited,failure,context)).interactable = page > 0;
                    AddButton(row,"→",() => ShowMapIssues(issues,page + 1,limited,failure,context)).interactable = page + 1 < pages;
                }
                AddButton(body,"Проверить снова",CheckMap);
                AddLabel(body,"Нажмите проблему, чтобы выделить место на карте. Проверяются также флаги появления и размещение объектов по правилам UDB. Это диагностика редактора; механика движка здесь не симулируется.");
            });
        }
        void FocusMapIssue(MapIssue issue,MapCheckContext context)
        {
            if(!CurrentMapCheck(context)){MapCheckExpired();return;}
            if (issue.elements.Length == 0 || !issue.position.HasValue) return;
            CloseDialog(); SetTool(MobileTool.Select); SetKind(issue.elements[0].kind); Selection.Clear();
            foreach (var element in issue.elements) Selection.Add(element);
            var p = issue.position.Value; View.center = new Vector2(p.x,p.y); View.zoom = Mathf.Clamp(View.zoom,.5f,3); View.SetVerticesDirty();
            RefreshLabels(); Notify(issue.message + ". Изменения — через «Свойства» или инструменты.");
        }
        void RemoveUnusedTexture(MapIssue issue,MapCheckContext context)
        {
            if(!CurrentMapCheck(context)){MapCheckExpired();return;}
            var target=issue.unusedTexture;if(target==null)return;
            ShowDialog("Удалить лишнюю текстуру",body=>
            {
                AddLabel(body,issue.message+". Будет удалена только эта текстура; общие сдвиги X/Y сохранятся. Доступна отмена.");
                var feedback=Label(body,"",16);feedback.gameObject.SetActive(false);
                DialogApply(body,()=>
                {
                    if(!CurrentMapCheck(context)||Draft.Count!=0||Tool==MobileTool.Paste)
                    {feedback.text="Карта, профиль или ресурсы изменились. Запустите проверку снова.";feedback.gameObject.SetActive(true);return false;}
                    return Edit(map=>
                    {
                        var line=map.lines[target.line];int index=target.front?line.front:line.back;
                        if(index!=target.side||(target.upper?map.sides[index].upper:map.sides[index].lower)!=target.name)
                            throw new InvalidOperationException("Текстура изменилась. Запустите проверку снова.");
                        var side=ExclusiveBatchSide(map,target.line,target.front);
                        if(map.sides.Count>65535)throw new InvalidOperationException("Превышен предел сторон Doom / Hexen. Общую сторону нельзя разделить.");
                        if(target.upper)side.upper="-";else side.lower="-";
                    },"Лишняя текстура удалена. Доступна отмена.");
                },"Удалить",true);
            });
        }
    }
}
