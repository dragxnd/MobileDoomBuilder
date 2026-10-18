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
        string gameProfile = "auto", effectiveProfile, thingCatalogKey;
        int placementType = 1, placementAngle;
        ThingCatalog thingCatalog;
        public float ClassicThingRadius(MapThing thing,float scale)
        {
            var type=thingCatalog==null?null:thingCatalog.Find(thing.type);
            double size=type==null?10:ThingPlacement.Radius(thing,type);
            return (float)ThingDragSnap.DisplayRadius(size,type!=null&&type.fixedSize,scale,gridFrame.fixedThingScale)*scale;
        }
        DoomResources thingCatalogResources;
        readonly Dictionary<string,GameSky> skyProfiles=new Dictionary<string,GameSky>();
        GameSky SkyProfile(string id)
        {
            GameSky profile;if(skyProfiles.TryGetValue(id,out profile))return profile;
            var definitions=Resources.Load<TextAsset>("MobileUDB/SkyProfiles");
            if(definitions==null)throw new InvalidOperationException("Нет конфигураций неба.");
            return skyProfiles[id]=GameSky.Load(definitions.text,id);
        }
        internal ThingCatalog ThingTypes { get { return thingCatalog; } }
        static readonly string[] ProfileIds = { "auto","Doom","Doom2","Heretic","Hexen","Strife" };
        static string ValidProfile(string value)
        {
            if (string.IsNullOrEmpty(value)) return "auto";
            foreach (string id in ProfileIds) if (string.Equals(id,value,StringComparison.OrdinalIgnoreCase)) return id;
            throw new InvalidOperationException("Неизвестный игровой профиль: " + value);
        }
        void RebuildThingCatalog()
        {
            string id = gameProfile;
            if (id == "auto")
            {
                id = resources.FindSprite("CLERA") != null ? "Hexen" : resources.FindSprite("MUMMA") != null ? "Heretic" : resources.FindSprite("PEASA") != null ? "Strife" :
                    resources.FindSprite("BOS2A") != null || resources.FindSprite("CPOSA") != null || resources.SpriteCount == 0 ? "Doom2" : "Doom";
            }
            bool doom=Map.format==MapFormat.Doom&&(id=="Doom"||id=="Doom2");
            Map.sky=resources.ResolveMapSky(SkyProfile(id),Map.name,actionEngine!="ZDoom");
            if(!doom&&(actionEngine=="MBF"||actionEngine=="MBF21"))actionEngine="auto";
            bool mbf=doom&&(actionEngine=="MBF"||actionEngine=="MBF21");
            bool boom=doom&&(mbf||actionEngine=="Boom"||actionEngine=="ZDoom");
            string key=id+"/"+Map.format+"/"+boom+"/"+mbf;
            if (key == thingCatalogKey && thingCatalog != null && ReferenceEquals(thingCatalogResources,resources)) return;
            var text = Resources.Load<TextAsset>("MobileUDB/Profiles/" + (id == "Doom2" ? "Doom" : id));
            if (text == null) throw new InvalidOperationException("Нет определений типов: " + id);
            var configurations=new List<string>{text.text};
            if(id=="Doom2")configurations.Add(Resources.Load<TextAsset>("MobileUDB/Profiles/Doom2").text);
            if(boom)configurations.Add(Resources.Load<TextAsset>("MobileUDB/Profiles/Boom").text);
            if(mbf)configurations.Add(Resources.Load<TextAsset>("MobileUDB/Profiles/MBF").text);
            thingCatalog=ThingCatalog.Load(configurations.ToArray());
            if((id=="Doom"||id=="Doom2")&&resources.dehackedPatches.Count>0)
            {
                var definitions=Resources.Load<TextAsset>("MobileUDB/Profiles/DehackedDoom");
                if(definitions==null)thingCatalog.warnings.Add("Нет базовых определений DEHACKED.");
                else thingCatalog.ApplyDehacked(definitions.text,resources);
            }
            thingCatalogKey=key;thingCatalogResources=resources;
            effectiveProfile = id;
        }
        void ProfileMenu()
        {
            ShowDialog("Игра · " + effectiveProfile,body =>
            {
                AddLabel(body,"Профиль задаёт названия, категории и спрайты объектов. Сохраняется в проекте. Формат карты выбирается отдельно.");
                foreach (string id in ProfileIds)
                {
                    string selected = id;
                    AddButton(body,(id == gameProfile ? "✓ " : "") + (id == "auto" ? "Авто по спрайтам IWAD · " + effectiveProfile : id == "Doom2" ? "Doom II" : id),() =>
                    { gameProfile = selected; CloseDialog(); RebuildThingCatalog(); Changed(); Notify("Профиль: " + effectiveProfile); });
                }
                AddLabel(body,"Типы взяты из конфигураций UDB. Неизвестные номера доступны вручную. DECORATE / ZScript и модельные объекты ещё не перенесены.");
                if(effectiveProfile=="Doom"||effectiveProfile=="Doom2")AddLabel(body,"DEHACKED / BEX из ресурсов изменяют номера, названия, размеры и стартовые спрайты объектов.");
            });
        }
        void PlacementMenu()
        {
            ShowDialog("Добавление объекта",body =>
            {
                var field = Field(body,"Тип",placementType.ToString());
                AddButton(body,"Каталог типов…",() => ThingPicker(id => field.text = id.ToString()));
                var angle = Field(body,"Угол",placementAngle.ToString()); AddAngleDial(body,angle);
                AddButton(body,"Готово",() => { placementType = Number(field,0,65535); placementAngle = Number(angle,0,359); CloseDialog(); Notify("Коснитесь карты: " + thingCatalog.Find(placementType).title + " · " + placementAngle + "°"); });
            });

        }
        void ThingPicker(Action<int> selected)
        {
            if (modal == null) return; CloseTexturePicker();
            texturePicker = Rect("Thing picker",modal); ScaleDialogBounds(texturePicker,1f); texturePicker.gameObject.AddComponent<Image>().color = Background;
            var heading = Label(texturePicker,"Объекты · " + effectiveProfile,24); var h = heading.rectTransform;
            h.anchorMin = new Vector2(0,1); h.anchorMax = Vector2.one; h.pivot = new Vector2(.5f,1); h.sizeDelta = new Vector2(-130,62); h.anchoredPosition = new Vector2(-60,0);
            var close = Button(texturePicker,"Назад",CloseTexturePicker); var cr = close.GetComponent<RectTransform>(); cr.anchorMin = cr.anchorMax = Vector2.one; cr.pivot = Vector2.one; cr.sizeDelta = new Vector2(108,56); cr.anchoredPosition = new Vector2(-8,-4);
            var scroll = Rect("Thing scroll",texturePicker); Stretch(scroll); scroll.offsetMin = new Vector2(16,12); scroll.offsetMax = new Vector2(-16,-68);
            scroll.gameObject.AddComponent<Image>().color = Background; scroll.gameObject.AddComponent<RectMask2D>(); var sv = scroll.gameObject.AddComponent<ScrollRect>(); sv.horizontal = false; sv.movementType = ScrollRect.MovementType.Clamped;
            var body = Rect("Thing content",scroll); body.anchorMin = new Vector2(0,1); body.anchorMax = Vector2.one; body.pivot = new Vector2(.5f,1); body.sizeDelta = Vector2.zero;
            var layout = body.gameObject.AddComponent<VerticalLayoutGroup>(); layout.spacing = 8; layout.childControlHeight = layout.childControlWidth = true; layout.childForceExpandHeight = false;
            body.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize; sv.viewport = scroll; sv.content = body;
            var search = Field(body,"Имя / номер", "",false); search.characterLimit = 64;
            string category = ""; var categories = new List<string> { "Все категории" };
            foreach (var type in thingCatalog.choices) if (!categories.Contains(type.category)) categories.Add(type.category);
            Button categoryButton = null;
            var info = Label(body,"",16); info.gameObject.AddComponent<LayoutElement>().preferredHeight = 42;
            var grid = Rect("Thing grid",body); var gl = grid.gameObject.AddComponent<GridLayoutGroup>(); gl.spacing = new Vector2(8,8); gl.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            var gridSize = grid.gameObject.AddComponent<LayoutElement>();
            var navigation = Rect("Thing pages",body); navigation.gameObject.AddComponent<LayoutElement>().preferredHeight = 56; var row = navigation.gameObject.AddComponent<HorizontalLayoutGroup>(); row.spacing = 8; row.childControlWidth = row.childControlHeight = true; row.childForceExpandWidth = true;
            int page = 0; const int pageSize = 12; var filtered = new List<ThingType>(); Action render = null; Button previous = null, next = null;
            render = () =>
            {
                texturePickerSize = texturePicker.rect.size; gl.constraintCount = Screen.width < Screen.height ? 3 : 4;
                if (thumbnailRoutine != null) { StopCoroutine(thumbnailRoutine); thumbnailRoutine = null; }
                for (int i = grid.childCount - 1; i >= 0; i--) { var child = grid.GetChild(i).gameObject; child.SetActive(false); Destroy(child); }
                filtered.Clear(); string query = search.text.Trim();
                foreach (var type in thingCatalog.choices) if ((category == "" || type.category == category) &&
                    (type.title.IndexOf(query,StringComparison.OrdinalIgnoreCase) >= 0 || type.id.ToString().IndexOf(query,StringComparison.Ordinal) >= 0 || type.sprite.IndexOf(query,StringComparison.OrdinalIgnoreCase) >= 0)) filtered.Add(type);
                int pages = Math.Max(1,(filtered.Count + pageSize - 1) / pageSize); page = Math.Max(0,Math.Min(page,pages - 1)); previous.interactable = page > 0; next.interactable = page + 1 < pages;
                info.text = filtered.Count + " типов · " + (page + 1) + " / " + pages;
                Canvas.ForceUpdateCanvases(); gl.cellSize = new Vector2(Math.Max(48,(scroll.rect.width - (gl.constraintCount - 1) * 8) / gl.constraintCount),142);
                int count = Math.Min(pageSize,filtered.Count - page * pageSize); gridSize.preferredHeight = Math.Max(0,((count + gl.constraintCount - 1) / gl.constraintCount) * 150 - 8);
                var frames = new List<SpriteFrame>(); var images = new List<RawImage>(); var labels = new List<Text>();
                for (int i = 0; i < count; i++)
                {
                    var type = filtered[page * pageSize + i]; var tile = Button(grid,type.id + " · " + type.title,() => { CloseTexturePicker(); selected(type.id); });
                    var label = tile.GetComponentInChildren<Text>(); label.fontSize = 14; label.resizeTextMaxSize = 14; label.resizeTextMinSize = 11;
                    label.rectTransform.anchorMax = new Vector2(1,0); label.rectTransform.sizeDelta = new Vector2(0,68); label.rectTransform.pivot = new Vector2(.5f,0); label.rectTransform.anchoredPosition = new Vector2(0,2);
                    var ir = Rect("Sprite thumbnail",tile.transform); ir.anchorMin = ir.anchorMax = new Vector2(.5f,1); ir.pivot = new Vector2(.5f,1); ir.sizeDelta = new Vector2(66,66); ir.anchoredPosition = new Vector2(0,-4);
                    var image = ir.gameObject.AddComponent<RawImage>(); image.raycastTarget = false; image.color = Color.clear;
                    var frame=resources.FindSprite(type.sprite);
                    if(frame==null&&type.id==888&&Actions.mbf&&type.sprite.StartsWith("DOGS",StringComparison.OrdinalIgnoreCase))
                    {image.texture=Resources.Load<Texture2D>("MobileUDB/Profiles/MBFDog");image.color=Color.white;}
                    frames.Add(frame); images.Add(image); labels.Add(label);
                }
                thumbnailRoutine = StartCoroutine(LoadThingThumbnails(frames,images,labels));
            };
            categoryButton = AddButton(body,"Все категории ▾",() =>
            {
                // Category list is inline and scrollable; no tabs or cramped horizontal strip.
                var list = Rect("Category list",body); list.SetSiblingIndex(categoryButton.transform.GetSiblingIndex() + 1);
                var l = list.gameObject.AddComponent<VerticalLayoutGroup>(); l.spacing = 4; l.childControlHeight = l.childControlWidth = true; l.childForceExpandHeight = false;
                list.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize; categoryButton.interactable = false;
                for (int i = 0; i < categories.Count; i++) { int index = i; AddButton(list,categories[i],() => { category = index == 0 ? "" : categories[index]; categoryButton.GetComponentInChildren<Text>().text = categories[index] + " ▾"; categoryButton.interactable = true; list.gameObject.SetActive(false); Destroy(list.gameObject); page = 0; render(); }); }
            });
            categoryButton.transform.SetSiblingIndex(search.transform.parent.GetSiblingIndex() + 1);
            previous = Button(navigation,"← Назад",() => { page--; render(); }); next = Button(navigation,"Далее →",() => { page++; render(); });
            refreshTexturePicker = render; search.onValueChanged.AddListener(_ => { page = 0; render(); }); render();
        }
        IEnumerator LoadThingThumbnails(List<SpriteFrame> frames, List<RawImage> images, List<Text> labels)
        {
            for (int i = 0; i < frames.Count; i++)
            {
                yield return null; if (texturePicker == null || images[i] == null) yield break;
                if (frames[i] == null) { if(images[i].texture==null)labels[i].text += "\nнет спрайта"; continue; }
                try
                {
                    var frame = frames[i]; var texture = Thumbnail(frame.image); images[i].texture = texture; images[i].color = Color.white;
                    images[i].uvRect = frame.mirror ? new Rect(1,0,-1,1) : new Rect(0,0,1,1);
                    float ratio = texture.width / (float)texture.height; images[i].rectTransform.sizeDelta = ratio > 1 ? new Vector2(66,66 / ratio) : new Vector2(66 * ratio,66);
                }
                catch (Exception) { labels[i].text += "\nнет превью"; }
            }
            thumbnailRoutine = null;
        }
    }
}
