using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using MobileUDB.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MobileUDB
{
    [Serializable] public sealed class MobileResourceArchive
    {
        public string name, data, id, kind, location;
    }
    public sealed partial class MobileEditor
    {
        const long MaxResourceBytes = 128L * 1024 * 1024, MaxProjectWadBytes = 240L * 1024 * 1024;
        List<MobileResourceArchive> resourceArchives = new List<MobileResourceArchive>();
        List<ResourceArchive> resourceSources = new List<ResourceArchive>();
        DoomResources resources;
        internal DoomResources ResourceCatalog { get { return resources; } }
        internal ResourceImage DecodeVisualTexture(ResourceTexture entry) { return resources.Decode(entry, DecodePng, 512); }
        internal static byte[] VisualPixelRows(ResourceImage image) { return FlipRows(image.rgba, image.width, image.height); }
        string resourceError;
        RectTransform texturePicker;
        Coroutine thumbnailRoutine;
        Action refreshTexturePicker;
        Vector2 texturePickerSize;
        readonly Dictionary<ResourceTexture, Texture2D> thumbnails = new Dictionary<ResourceTexture, Texture2D>();
        readonly Queue<ResourceTexture> thumbnailOrder = new Queue<ResourceTexture>();
        readonly Dictionary<ResourceTexture, string> thumbnailErrors = new Dictionary<ResourceTexture, string>();

        static long ResourceCost(ResourceArchive resourceArchive, int fileSize)
        {
            return Math.Max(fileSize, resourceArchive.ContentBytes);
        }
        static List<ResourceArchive> ParseResourceArchives(List<MobileResourceArchive> descriptors, int mapWadSize,Func<bool> cancelled = null,Action<string> progress = null)
        {
            var parsed = new List<ResourceArchive>(); if (descriptors == null) return parsed;
            if (descriptors.Count > 32) throw new InvalidDataException("Допускается не более 32 архивов ресурсов.");
            long total = 0;
            foreach (var descriptor in descriptors)
            {
                if (cancelled != null && cancelled()) throw new OperationCanceledException();
                if (progress != null) progress("Ресурсы проекта: " + (parsed.Count + 1) + " / " + descriptors.Count);
                if (descriptor == null || string.IsNullOrEmpty(descriptor.data) || descriptor.data.Length > MaxResourceBytes * 4 / 3 + 4)
                    throw new InvalidDataException("Недопустимый архив ресурсов в проекте.");
                var bytes = Convert.FromBase64String(descriptor.data);
                var resourceArchive = ResourceArchive.ReadAttachment(bytes,descriptor.name,descriptor.kind,Math.Min(MaxResourceBytes - total,MaxProjectWadBytes - mapWadSize - total));
                total += ResourceCost(resourceArchive, bytes.Length);
                if (total > MaxResourceBytes || total + mapWadSize > MaxProjectWadBytes) throw new InvalidDataException("Ресурсы превышают предел памяти мобильного проекта.");
                parsed.Add(resourceArchive);
            }
            if (cancelled != null && cancelled()) throw new OperationCanceledException();
            return parsed;
        }
        void CheckMapResourceBudget(int mapWadSize)
        {
            if (resourceSources.Count == 0) return;
            long total = mapWadSize;
            for (int i = 0; i < resourceSources.Count; i++) total += ResourceCost(resourceSources[i],(int)(resourceArchives[i].data.Length * 3L / 4));
            if (total > MaxProjectWadBytes) throw new InvalidDataException("WAD карты вместе с ресурсами превышает предел памяти проекта. Отключите лишние ресурсы.");
        }
        void RebuildResources()
        {
            CloseTexturePicker(); ClearThumbnails(); resourceError = null;
            var sources = new List<ResourceArchive>(resourceSources); if (archive != null) sources.Add(ResourceArchive.FromWad(archive));
            try { resources = DoomResources.BuildArchives(sources); }
            catch (Exception e)
            {
                resourceError = "Ресурсы карты не загружены: " + e.Message;
                try { resources = DoomResources.BuildArchives(resourceSources); }
                catch (Exception external) { resources = DoomResources.BuildArchives(new ResourceArchive[0]); resourceError += " " + external.Message; }
            }
            RebuildThingCatalog(); if (visualView != null) visualView.ResourcesChanged();
        }
        string ResourceWarning()
        {
            if (!string.IsNullOrEmpty(resourceError)) return "\n" + resourceError;
            string warning=resources != null && resources.warnings.Count > 0 ? "\n" + resources.warnings[0] : "";
            if(thingCatalog!=null&&thingCatalog.warnings.Count>0)warning+="\n"+thingCatalog.warnings[0];
            return warning;
        }
        void MoveResource(int index,int direction)
        {
            int destination = index + direction;
            if ((direction != -1 && direction != 1) || index < 0 || index >= resourceSources.Count || destination < 0 || destination >= resourceSources.Count) return;
            var sources = new List<ResourceArchive>(resourceSources);
            var descriptors = new List<MobileResourceArchive>(resourceArchives);
            var source = sources[index]; var descriptor = descriptors[index];
            sources.RemoveAt(index); sources.Insert(destination,source);
            descriptors.RemoveAt(index); descriptors.Insert(destination,descriptor);
            BeginResourceChange(sources,descriptors,true,null);
        }
        void RemoveResource(int index)
        {
            if(index<0||index>=resourceSources.Count||index>=resourceArchives.Count)return;
            var sources=new List<ResourceArchive>(resourceSources);var descriptors=new List<MobileResourceArchive>(resourceArchives);
            var removed=descriptors[index];sources.RemoveAt(index);descriptors.RemoveAt(index);
            BeginResourceChange(sources,descriptors,false,removed);
        }
        sealed class PreparedResourceChange {public DoomResources catalog;public string error;}
        void BeginResourceChange(List<ResourceArchive> sources,List<MobileResourceArchive> descriptors,bool reorder,MobileResourceArchive removed)
        {
            var oldScroll = modal == null ? null : modal.GetComponentInChildren<ScrollRect>();
            float offset = oldScroll == null ? 0 : oldScroll.content.anchoredPosition.y;
            var context=CaptureOpenContext();var mapArchive=archive;
            BeginDocumentWork(context,job=>
            {
                job.Check("Каталог ресурсов…");DoomResources external=null;
                // Preserve native PNAMES ordering validation before any commit.
                if(reorder)
                {
                    try{external=DoomResources.BuildArchives(sources);}
                    catch(Exception error){throw new InvalidOperationException("Не удалось изменить порядок ресурсов: "+error.Message);}
                    job.Check();
                }
                var result=new PreparedResourceChange();var all=new List<ResourceArchive>(sources);
                if(mapArchive!=null)all.Add(ResourceArchive.FromWad(mapArchive));
                try{result.catalog=mapArchive==null&&external!=null?external:DoomResources.BuildArchives(all);}
                catch(Exception error)
                {
                    result.error="Ресурсы карты не загружены: "+error.Message;
                    try{result.catalog=external??DoomResources.BuildArchives(sources);}
                    catch(Exception failure){result.catalog=DoomResources.BuildArchives(new ResourceArchive[0]);result.error+=" "+failure.Message;}
                }
                job.Check();return result;
            },prepared=>
            {
                var result=(PreparedResourceChange)prepared;
                CloseTexturePicker();ClearThumbnails();resourceSources=sources;resourceArchives=descriptors;
                if(removed!=null&&ReferenceEquals(sourceMapPackage,removed))sourceMapPackage=null;
                resources=result.catalog;resourceError=result.error;RebuildThingCatalog();if(visualView!=null)visualView.ResourcesChanged();
                Changed();ResourcesMenu();Canvas.ForceUpdateCanvases();
                var scroll=modal.GetComponentInChildren<ScrollRect>();scroll.StopMovement();var position=scroll.content.anchoredPosition;
                position.y=Mathf.Clamp(offset,0,Mathf.Max(0,scroll.content.rect.height-scroll.viewport.rect.height));scroll.content.anchoredPosition=position;
                Notify((reorder?"Порядок ресурсов изменён.":"Ресурс отключён.")+ResourceWarning());
            },reorder?"Порядок ресурсов":"Отключение ресурса");
        }
        void ResourcesMenu()
        {
            ShowDialog("Ресурсы WAD / PK3 / DEH", body =>
            {
                AddButton(body, "Подключить WAD / PK3 / DEH / BEX…", () => { CloseDialog(); GetComponent<MobileFileBridge>().ImportResources(); });
                var hint = Label(body,"Приоритет растёт вниз. Удержание значка — подсказка.",16);
                hint.alignment = TextAnchor.MiddleLeft; hint.gameObject.AddComponent<LayoutElement>().minHeight = 36;
                foreach(string path in missingWadResources)
                {string missing=path;AddLabel(body,"Недоступен: "+missing);AddButton(body,"Убрать ссылку: "+System.IO.Path.GetFileName(missing.Replace('\\','/')),()=>{missingWadResources.Remove(missing);Changed();ResourcesMenu();});}
                for (int i = 0; i < resourceArchives.Count; i++)
                {
                    int index = i; string name = resourceArchives[i].name;
                    var caption = Label(body,(i + 1) + ". " + name + (sourceMapPackage == resourceArchives[i] ? " · исходный PK3" : ""),18);
                    caption.alignment = TextAnchor.MiddleLeft; caption.gameObject.AddComponent<LayoutElement>().minHeight = 28;
                    var row = Rect("Resource actions " + i,body); row.gameObject.AddComponent<LayoutElement>().preferredHeight = 56;
                    var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>(); layout.spacing = 8;
                    layout.childControlWidth = layout.childControlHeight = true; layout.childForceExpandWidth = layout.childForceExpandHeight = true;
                    var up = IconButton(row,"Раньше в списке: " + name,EditorIcon.Symbol.Up,() => MoveResource(index,-1)); up.interactable = i > 0;
                    var down = IconButton(row,"Позже в списке: " + name,EditorIcon.Symbol.Down,() => MoveResource(index,1)); down.interactable = i + 1 < resourceArchives.Count;
                    var remove = IconButton(row,"Отключить: " + name,EditorIcon.Symbol.Delete,() => RemoveResource(index));
                    foreach (var button in new[] { up,down,remove }) ((MobileIconButton)button).showHint = text => hint.text = text;
                }
                AddLabel(body, "Стены: " + resources.WallCount + " · Пол / потолок: " + resources.FlatCount + " · Спрайты: " + resources.SpriteCount + "\n" + (resources.HasPalette ? "PLAYPAL загружена." : "Для палитровых текстур подключите IWAD.") + ResourceWarning());
                AddLabel(body, "Порядок сверху вниз: базовый IWAD, затем дополнения. При совпадении имён побеждает ресурс ниже. WAD карты имеет высший приоритет.");
                if((effectiveProfile=="Doom"||effectiveProfile=="Doom2")&&resources.dehackedPatches.Count>0)
                    AddLabel(body,"DEHACKED / BEX: найдено патчей — "+resources.dehackedPatches.Count+". Каталог и 3D учитывают названия, номера, размеры и стартовые спрайты объектов. Ошибки показаны выше.");
                if (archive != null) AddLabel(body, "Ресурсы открытого WAD карты подключены автоматически.");
                AddLabel(body, "Поддерживаются изображения Doom / PNG и составные текстуры. Имена текстур в бинарных картах — до 8 ASCII-символов. Неподдерживаемые свойства текстур отмечаются предупреждением.");
            });
        }
        Texture2D Thumbnail(ResourceTexture entry)
        {
            Texture2D found; if (thumbnails.TryGetValue(entry, out found)) return found;
            var decoded = resources.Decode(entry, DecodePng, 128);
            var texture = new Texture2D(decoded.width, decoded.height, TextureFormat.RGBA32, false);
            texture.name = "UDB thumbnail " + entry.name; texture.filterMode = FilterMode.Point; texture.wrapMode = TextureWrapMode.Clamp;
            texture.LoadRawTextureData(FlipRows(decoded.rgba, decoded.width, decoded.height)); texture.Apply(false, true);
            thumbnails.Add(entry, texture); thumbnailOrder.Enqueue(entry);
            while (thumbnailOrder.Count > 36)
            {
                var oldest = thumbnailOrder.Dequeue(); Destroy(thumbnails[oldest]); thumbnails.Remove(oldest);
            }
            return texture;
        }
        static byte[] FlipRows(byte[] rgba, int width, int height)
        {
            var flipped = new byte[rgba.Length]; int stride = width * 4;
            for (int y = 0; y < height; y++) Buffer.BlockCopy(rgba, y * stride, flipped, (height - y - 1) * stride, stride);
            return flipped;
        }
        static ResourceImage DecodePng(byte[] bytes)
        {
            var image = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!ImageConversion.LoadImage(image, bytes, false)) throw new InvalidDataException("Не удалось прочитать PNG.");
                var colors = image.GetPixels32(); var rgba = new byte[colors.Length * 4];
                for (int i = 0; i < colors.Length; i++) { rgba[i * 4] = colors[i].r; rgba[i * 4 + 1] = colors[i].g; rgba[i * 4 + 2] = colors[i].b; rgba[i * 4 + 3] = colors[i].a; }
                return new ResourceImage(image.width, image.height, FlipRows(rgba, image.width, image.height));
            }
            finally { Destroy(image); }
        }
        void ClearThumbnails()
        {
            foreach (var image in thumbnails.Values) if (image != null) Destroy(image);
            thumbnails.Clear(); thumbnailOrder.Clear(); thumbnailErrors.Clear();
        }
        void OnDestroy() { ClearPropertyPreviews(); CloseBackgroundPreview();CancelWadExport(); CancelProjectSave(); CloseTexturePicker(); ClearThumbnails(); }
        void LateUpdate()
        {
            RefreshBridgeHandleLabels();
            if (texturePicker != null && refreshTexturePicker != null && texturePicker.rect.size != texturePickerSize) refreshTexturePicker();
        }
        void CloseTexturePicker()
        {
            refreshTexturePicker = null;
            if (thumbnailRoutine != null) { StopCoroutine(thumbnailRoutine); thumbnailRoutine = null; }
            if (texturePicker != null) { ReleaseDialogInput(texturePicker);texturePicker.gameObject.SetActive(false); Destroy(texturePicker.gameObject); texturePicker = null; }
        }
        void TexturePicker(InputField target, ResourceKind kind,bool fullNames=false,Action selected=null)
        {
            if (modal == null) return;
            CloseTexturePicker();
            texturePicker = Rect("Texture picker", modal); ScaleDialogBounds(texturePicker,1f); texturePicker.gameObject.AddComponent<Image>().color = Background;
            var heading = Label(texturePicker, kind == ResourceKind.Flat ? "Пол / потолок" : "Текстуры стен", 24); var h = heading.rectTransform;
            h.anchorMin = new Vector2(0,1); h.anchorMax = Vector2.one; h.pivot = new Vector2(.5f,1); h.sizeDelta = new Vector2(-130,62); h.anchoredPosition = new Vector2(-60,0);
            var close = Button(texturePicker, "Назад", CloseTexturePicker); var cr = close.GetComponent<RectTransform>(); cr.anchorMin = cr.anchorMax = Vector2.one; cr.pivot = Vector2.one; cr.sizeDelta = new Vector2(108,56); cr.anchoredPosition = new Vector2(-8,-4);
            var scroll = Rect("Texture scroll", texturePicker); Stretch(scroll); scroll.offsetMin = new Vector2(16,12); scroll.offsetMax = new Vector2(-16,-68);
            scroll.gameObject.AddComponent<Image>().color = Background; scroll.gameObject.AddComponent<RectMask2D>(); var sv = scroll.gameObject.AddComponent<ScrollRect>(); sv.horizontal = false; sv.movementType = ScrollRect.MovementType.Clamped;
            var body = Rect("Texture content", scroll); body.anchorMin = new Vector2(0,1); body.anchorMax = Vector2.one; body.pivot = new Vector2(.5f,1); body.sizeDelta = Vector2.zero;
            var layout = body.gameObject.AddComponent<VerticalLayoutGroup>(); layout.spacing = 8; layout.childControlHeight = layout.childControlWidth = true; layout.childForceExpandHeight = false;
            body.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize; sv.viewport = scroll; sv.content = body;
            bool longNames = fullNames || MapFormats.IsUniversal(Map.format);
            var search = Field(body,"Поиск", "", false); search.characterLimit = longNames ? 255 : 8;
            bool mixed=!fullNames&&longNames;var choices=resources.Textures(kind,mixed,longNames);
            var used=TextureCatalogFilters.Used(Map,resources,kind,mixed);var categories=PickerCategories(choices);
            bool onlyUsed=false;
            var filtersRow=Rect("Texture filters",body);filtersRow.gameObject.AddComponent<LayoutElement>().preferredHeight=56;var filtersLayout=filtersRow.gameObject.AddComponent<HorizontalLayoutGroup>();filtersLayout.spacing=8;filtersLayout.childControlWidth=filtersLayout.childControlHeight=true;filtersLayout.childForceExpandWidth=true;
            var usedButton=Button(filtersRow,"Только используемые",()=>{});usedButton.name="Used textures";usedButton.gameObject.AddComponent<LayoutElement>().preferredHeight=56;
            var usedLabel=usedButton.GetComponentInChildren<Text>();usedLabel.fontSize=16;usedLabel.resizeTextMaxSize=16;usedLabel.alignment=TextAnchor.MiddleLeft;usedLabel.rectTransform.offsetMin=new Vector2(44,0);
            var checkRect=Rect("Used textures check",usedButton.transform);checkRect.anchorMin=checkRect.anchorMax=new Vector2(0,.5f);checkRect.sizeDelta=new Vector2(24,24);checkRect.anchoredPosition=new Vector2(22,0);var usedCheck=checkRect.gameObject.AddComponent<WallFlagStateGraphic>();usedCheck.raycastTarget=false;usedCheck.State=0;
            var categoryDropdown=TextureCategoryDropdown(filtersRow,categories);
            var info = Label(body,"",16); info.gameObject.AddComponent<LayoutElement>().preferredHeight = 40;
            if (!fullNames && kind == ResourceKind.Wall) AddButton(body,"— Без текстуры", () => { target.text = "-"; CloseTexturePicker(); });
            else if(!fullNames) AddButton(body,MakeSectorSky+" — небо", () => { target.text = MakeSectorSky; CloseTexturePicker(); });
            var grid = Rect("Texture grid",body); var gl = grid.gameObject.AddComponent<GridLayoutGroup>(); gl.spacing = new Vector2(8,8); gl.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gl.constraintCount = Screen.width < Screen.height ? 3 : 4;
            var gridSize = grid.gameObject.AddComponent<LayoutElement>();
            var navigation = Rect("Texture pages",body); navigation.gameObject.AddComponent<LayoutElement>().preferredHeight = 56; var row = navigation.gameObject.AddComponent<HorizontalLayoutGroup>(); row.spacing = 8; row.childControlWidth = row.childControlHeight = true; row.childForceExpandWidth = true;
            int page = 0; const int pageSize = 12; var filtered = new List<ResourceTexture>();
            Action render = null; Button previous = null, next = null;
            render = () =>
            {
                texturePickerSize = texturePicker.rect.size; gl.constraintCount = Screen.width < Screen.height ? 3 : 4;
                if (thumbnailRoutine != null) { StopCoroutine(thumbnailRoutine); thumbnailRoutine = null; }
                for (int i = grid.childCount - 1; i >= 0; i--) { var child = grid.GetChild(i).gameObject; child.SetActive(false); Destroy(child); }
                filtered.Clear();var category=categoryDropdown.value==0?null:categories[categoryDropdown.value-1];
                foreach (var entry in choices) if ((!onlyUsed||used.Contains(entry))&&(category==null||category.textures.Contains(entry))&&entry.MapName(longNames).IndexOf(search.text.Trim(),StringComparison.OrdinalIgnoreCase) >= 0) filtered.Add(entry);
                int pages = Math.Max(1,(filtered.Count + pageSize - 1) / pageSize); page = Math.Max(0,Math.Min(page,pages - 1)); previous.interactable = page > 0; next.interactable = page + 1 < pages;
                info.text = filtered.Count + " текстур · " + (page + 1) + " / " + pages + (resources.HasPalette ? "" : "\nДля палитровых текстур подключите IWAD.");
                if (choices.Count == 0) info.text = "Нет текстур. Подключите ресурсы в меню «Файл → Ресурсы».";
                Canvas.ForceUpdateCanvases(); gl.cellSize = new Vector2(Math.Max(48,(scroll.rect.width - (gl.constraintCount - 1) * 8) / gl.constraintCount),112);
                int count = Math.Min(pageSize,filtered.Count - page * pageSize); gridSize.preferredHeight = Math.Max(0,((count + gl.constraintCount - 1) / gl.constraintCount) * 120 - 8);
                var entries = new List<ResourceTexture>(); var images = new List<RawImage>(); var labels = new List<Text>();
                for (int i = 0; i < count; i++)
                {
                    var entry = filtered[page * pageSize + i]; string mapName = entry.MapName(longNames);
                    var tile = Button(grid,mapName, () => { target.text = mapName; CloseTexturePicker();if(selected!=null)selected(); Notify("Выбрана текстура: " + mapName); });
                    var name = tile.GetComponentInChildren<Text>(); name.fontSize = 15; name.resizeTextMaxSize = 15; name.resizeTextMinSize = 12;
                    name.rectTransform.anchorMax = new Vector2(1,0); name.rectTransform.sizeDelta = new Vector2(0,36); name.rectTransform.pivot = new Vector2(.5f,0);
                    if (longNames && mapName.IndexOf('/') >= 0)
                    {
                        name.resizeTextForBestFit = false; name.fontSize = 12;
                        name.text = TextureCaption(name,Path.GetFileNameWithoutExtension(mapName),gl.cellSize.x - 8) + "\n" + TextureCaption(name,Path.GetDirectoryName(mapName).Replace('\\','/'),gl.cellSize.x - 8);
                    }
                    var preview = Rect("Preview",tile.transform); preview.anchorMin = preview.anchorMax = new Vector2(.5f,1); preview.pivot = new Vector2(.5f,1); preview.sizeDelta = new Vector2(66,66); preview.anchoredPosition = new Vector2(0,-4);
                    var image = preview.gameObject.AddComponent<RawImage>(); image.raycastTarget = false; image.color = new Color(1,1,1,0);
                    entries.Add(entry); images.Add(image); labels.Add(name);
                }
                sv.verticalNormalizedPosition = 1; thumbnailRoutine = StartCoroutine(LoadThumbnails(entries,images,labels));
            };
            previous = Button(navigation,"←", () => { page--; render(); }); next = Button(navigation,"→", () => { page++; render(); });
            refreshTexturePicker = render;
            usedButton.onClick.AddListener(()=>{onlyUsed=!onlyUsed;usedCheck.State=onlyUsed?1:0;usedButton.GetComponent<Image>().color=onlyUsed?Accent:Panel;page=0;render();});
            categoryDropdown.onValueChanged.AddListener(value=>{page=0;render();});
            search.onValueChanged.AddListener(value => { page = 0; render(); }); render();
        }
        static string TextureCaption(Text label,string value,float width)
        {
            var settings = label.GetGenerationSettings(Vector2.zero); settings.resizeTextForBestFit = false; settings.horizontalOverflow = HorizontalWrapMode.Overflow;
            if (label.cachedTextGeneratorForLayout.GetPreferredWidth(value,settings) / label.pixelsPerUnit <= width) return value;
            for (int count = value.Length - 1; count > 0; count--)
            {
                string shortValue = value.Substring(0,count) + "…";
                if (label.cachedTextGeneratorForLayout.GetPreferredWidth(shortValue,settings) / label.pixelsPerUnit <= width) return shortValue;
            }
            return "…";
        }
        IEnumerator LoadThumbnails(List<ResourceTexture> entries, List<RawImage> images, List<Text> labels)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                yield return null; if (texturePicker == null || images[i] == null) yield break;
                var entry = entries[i]; string error;
                try
                {
                    if (thumbnailErrors.TryGetValue(entry,out error)) throw new InvalidDataException(error);
                    var texture = Thumbnail(entry); images[i].texture = texture; images[i].color = Color.white;
                    float ratio = texture.width / (float)texture.height; images[i].rectTransform.sizeDelta = ratio > 1 ? new Vector2(66,66 / ratio) : new Vector2(66 * ratio,66);
                }
                catch (Exception e)
                {
                    if (thumbnailErrors.Count >= 64) thumbnailErrors.Clear(); thumbnailErrors[entry] = e.Message;
                    labels[i].text = entry.name + "\nнет превью";
                    var button = images[i].GetComponentInParent<Button>(); button.onClick.AddListener(() => Notify(entry.name + ": " + e.Message));
                }
            }
            thumbnailRoutine = null;
        }
    }
}
