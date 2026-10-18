using System;
using System.Collections.Generic;
using MobileUDB.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        string textureSearchMask = "",textureSearchReplacement = "";
        bool textureSearchSelection;
        void TextureSearchMenu()
        {
            if (Draft.Count > 0) { Notify("Завершите или отмените рисование перед поиском."); return; }
            ShowDialog("Поиск и замена текстур",body =>
            {
                AddButton(body,"Стены: верх / середина / низ",() => TextureSearchForm(false));
                AddButton(body,"Полы и потолки",() => TextureSearchForm(true));
                AddLabel(body,"Поиск в текущей карте по именам, включая скрытые поверхности. Ресурсы для поиска не обязательны.");
            });
        }
        void TextureSearchForm(bool flats)
        {
            ShowDialog(flats ? "Поиск плоскостей" : "Поиск стен",body =>
            {
                var error = Label(body,"",18); error.color = new Color32(255,170,140,255); error.alignment = TextAnchor.MiddleLeft;
                error.gameObject.AddComponent<LayoutElement>().minHeight = 56; error.gameObject.SetActive(false);
                var find = Field(body,"Искать",textureSearchMask,false);
                var replace = Field(body,"Заменить на",textureSearchReplacement,false);
                find.onValueChanged.AddListener(value => textureSearchMask = value); replace.onValueChanged.AddListener(value => textureSearchReplacement = value);
                AddButton(body,"Найти совпадения",() =>
                {
                    try
                    {
                        string replacement = string.IsNullOrWhiteSpace(replace.text) ? null : TextureName(replace);
                        if (replacement != null && replacement.IndexOfAny(new[] { '*','?' }) >= 0) throw new InvalidOperationException("Замена — точное имя без * и ?.");
                        var scope = textureSearchSelection ? new HashSet<ElementRef>(Selection) : null;
                        var found = MapTextureSearch.Find(Map,flats,find.text,scope);
                        TextureSearchResults(flats,find.text,replacement,scope,found,0);
                    }
                    catch (Exception exception) { error.text = exception.Message; error.gameObject.SetActive(true); }
                });
                Button scopeButton = null;
                Action scopeText = () => scopeButton.GetComponentInChildren<Text>().text = "Область: " + (textureSearchSelection ? "выделение" : "вся карта");
                scopeButton = AddButton(body,"",() => { textureSearchSelection = !textureSearchSelection; scopeText(); }); scopeText();
                AddButton(body,"Выбрать искомую текстуру…",() => TexturePicker(find,flats ? ResourceKind.Flat : ResourceKind.Wall));
                AddButton(body,"Выбрать текстуру замены…",() => TexturePicker(replace,flats ? ResourceKind.Flat : ResourceKind.Wall));
                AddLabel(body,"* — любое число символов, ? — один. Пустая замена — только поиск. «-» ищет отсутствующие обязательные текстуры стен; * также включает незаданные части. Выделение: стены или секторы, для плоскостей — секторы.");
            });
        }
        string TextureMatchLabel(MapTextureMatch hit)
        {
            string part = hit.surface.part == SurfacePart.Floor ? "пол" : hit.surface.part == SurfacePart.Ceiling ? "потолок" : hit.surface.part == SurfacePart.Upper ? "верх" : hit.surface.part == SurfacePart.Lower ? "низ" : "середина";
            return (hit.surface.IsFlat ? "Сектор #" + hit.surface.sector : "Стена #" + hit.surface.line + (hit.front ? " · перед" : " · зад")) + " · " + part + " · " + hit.texture;
        }
        void TextureSearchResults(bool flats,string pattern,string replacement,HashSet<ElementRef> scope,List<MapTextureMatch> found,int page)
        {
            const int pageSize = 6; int pages = Math.Max(1,(found.Count + pageSize - 1) / pageSize); page = Math.Max(0,Math.Min(page,pages - 1));
            ShowDialog("Совпадения текстур",body =>
            {
                var error = Label(body,"",18); error.color = new Color32(255,170,140,255); error.alignment = TextAnchor.MiddleLeft;
                error.gameObject.AddComponent<LayoutElement>().minHeight = 56; error.gameObject.SetActive(false);
                AddLabel(body,"Найдено поверхностей: " + found.Count + " · " + (scope == null ? "вся карта" : "выделение") + "\n" + pattern + (replacement == null ? "" : " → " + replacement) + "\nСтраница " + (page + 1) + " / " + pages);
                if (replacement != null && found.Count > 0) AddButton(body,"Заменить совпадения (" + found.Count + ")",() =>
                {
                    int changed = 0;
                    if (Edit(m => { changed = MapTextureSearch.Replace(m,flats,pattern,replacement,scope); if (changed == 0) throw new InvalidOperationException("Совпадения уже содержат эту текстуру."); },"Заменено текстур: " + found.Count + ". Доступна отмена."))
                    { CloseDialog(); if (VisualActive) visualView.ForgetSelection(); Notify("Заменено поверхностей: " + changed + ". Один Undo отменяет всю замену."); }
                    else { error.text = message; error.gameObject.SetActive(true); }
                });
                AddButton(body,"Выделить все найденные элементы",() => FocusTextureMatches(found)).interactable = found.Count > 0;
                for (int i = page * pageSize; i < Math.Min(found.Count,(page + 1) * pageSize); i++)
                {
                    var hit = found[i]; var button = AddButton(body,TextureMatchLabel(hit),() => FocusTextureMatches(new List<MapTextureMatch> { hit }));
                    button.GetComponent<LayoutElement>().preferredHeight = 74; var text = button.GetComponentInChildren<Text>(); text.resizeTextForBestFit = true; text.resizeTextMinSize = 14; text.resizeTextMaxSize = 20;
                }
                if (pages > 1)
                {
                    var row = Rect("Search pages",body); row.gameObject.AddComponent<LayoutElement>().preferredHeight = 60;
                    var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>(); layout.spacing = 8; layout.childForceExpandWidth = true;
                    AddButton(row,"←",() => TextureSearchResults(flats,pattern,replacement,scope,found,page - 1)).interactable = page > 0;
                    AddButton(row,"→",() => TextureSearchResults(flats,pattern,replacement,scope,found,page + 1)).interactable = page + 1 < pages;
                }
                AddButton(body,"Изменить поиск",() => TextureSearchForm(flats));
                AddLabel(body,"Нажмите результат для перехода к элементу. Замена касается всех найденных поверхностей, включая другие страницы, и отменяется одним Undo.");
            });
        }
        void FocusTextureMatches(List<MapTextureMatch> found)
        {
            if (found.Count == 0) return;
            CloseDialog(); SetTool(MobileTool.Select); SetKind(found[0].Element.kind); Selection.Clear();
            foreach (var hit in found) Selection.Add(hit.Element);
            var vertices = Map.SelectedVertices(Selection);
            if (vertices.Count > 0)
            {
                var min = new Vector2(float.MaxValue,float.MaxValue); var max = new Vector2(float.MinValue,float.MinValue);
                foreach (int index in vertices) { var v = Map.vertices[index]; var p = new Vector2(v.x,v.y); min = Vector2.Min(min,p); max = Vector2.Max(max,p); }
                View.center = (min + max) * .5f;
                View.zoom = Mathf.Clamp(Mathf.Min(View.rectTransform.rect.width / Mathf.Max(128,max.x - min.x + 128),View.rectTransform.rect.height / Mathf.Max(128,max.y - min.y + 128)),.025f,3);
            }
            View.SetVerticesDirty(); RefreshLabels(); Notify(found.Count == 1 ? TextureMatchLabel(found[0]) : "Выделено элементов: " + Selection.Count + ".");
        }
    }
}
