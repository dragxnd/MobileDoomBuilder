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
        string actionEngine = "auto", actionCatalogKey;
        ActionCatalog actionCatalog;
        readonly Dictionary<string,string> actionTexts = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        static string ValidActionEngine(string value)
        {
            if (string.IsNullOrEmpty(value)) return "auto";
            foreach (string engine in new[] { "auto","vanilla","Boom","MBF","MBF21","ZDoom" }) if (string.Equals(engine,value,StringComparison.OrdinalIgnoreCase)) return engine;
            throw new InvalidOperationException("Неизвестный профиль действий: " + value);
        }
        string ActionConfig(string name)
        {
            // Resolver is limited to the bundled profile assets. It never reads user paths.
            if (name.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 || !name.EndsWith(".cfg",StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Недопустимый include профиля.");
            string text; if (actionTexts.TryGetValue(name,out text)) return text;
            var asset = Resources.Load<TextAsset>("MobileUDB/Actions/" + name.Substring(0,name.Length - 4));
            if (asset == null) throw new InvalidOperationException("Нет конфигурации действий: " + name);
            actionTexts[name] = asset.text; return asset.text;
        }
        internal ActionCatalog Actions
        {
            get
            {
                string key = effectiveProfile + "/" + Map.format + "/" + Map.udmfNamespace + "/" + actionEngine;
                if (key != actionCatalogKey || actionCatalog == null) { actionCatalog = ActionCatalog.Build(ActionConfig,effectiveProfile,Map.format,Map.udmfNamespace,actionEngine); actionCatalogKey = key; }
                return actionCatalog;
            }
        }
        void ActionProfileMenu()
        {
            ShowDialog("Профиль действий",body =>
            {
                AddLabel(body,Actions.label + "\n" + Actions.warning);
                foreach (string engine in new[] { "auto","vanilla","Boom","MBF","MBF21","ZDoom" })
                {
                    string chosen = engine;
                    if (MapFormats.IsUniversal(Map.format) && engine != "auto" && engine != "ZDoom") continue;
                    if (Map.format == MapFormat.Hexen && effectiveProfile != "Hexen" && engine == "vanilla") continue;
                    if ((engine == "Boom" || engine == "MBF" || engine == "MBF21") && (Map.format != MapFormat.Doom || (effectiveProfile != "Doom" && effectiveProfile != "Doom2"))) continue;
                    AddButton(body,(actionEngine == engine ? "✓ " : "") + (engine == "auto" ? "Авто по игре и формату" : engine == "vanilla" ? "Базовая игра" : engine == "ZDoom" ? "ZDoom · бинарные карты" : engine),() => { actionEngine = chosen; CloseDialog(); RebuildThingCatalog(); Changed(); Notify("Действия: " + Actions.label); });
                }
                AddLabel(body,"Профиль сохраняется в проекте. MBF включает Boom, MBF21 — MBF. Флаги и эффекты доступны кнопками в свойствах.");
            });
        }
        sealed class PickerChoice { public int value; public string title, category = ""; public bool flag; }
        void ChoicePicker(string heading, List<PickerChoice> choices, Action<PickerChoice> selected, bool closeOnSelect = true, Func<PickerChoice,string> titleFor = null,bool richText=true)
        {
            if (modal == null) return; CloseTexturePicker();
            texturePicker = Rect("Action picker",modal); ScaleDialogBounds(texturePicker,1f); texturePicker.gameObject.AddComponent<Image>().color = Background;
            var title = Label(texturePicker,heading,24); var h = title.rectTransform; h.anchorMin = new Vector2(0,1); h.anchorMax = Vector2.one; h.pivot = new Vector2(.5f,1); h.sizeDelta = new Vector2(-130,62); h.anchoredPosition = new Vector2(-60,0);
            var close = Button(texturePicker,closeOnSelect ? "Назад" : "Готово",CloseTexturePicker); var cr = close.GetComponent<RectTransform>(); cr.anchorMin = cr.anchorMax = Vector2.one; cr.pivot = Vector2.one; cr.sizeDelta = new Vector2(108,56); cr.anchoredPosition = new Vector2(-8,-4);
            var scroll = Rect("Action scroll",texturePicker); Stretch(scroll); scroll.offsetMin = new Vector2(16,12); scroll.offsetMax = new Vector2(-16,-68); scroll.gameObject.AddComponent<Image>().color = Background;
            scroll.gameObject.AddComponent<RectMask2D>(); var sv = scroll.gameObject.AddComponent<ScrollRect>(); sv.horizontal = false; sv.movementType = ScrollRect.MovementType.Clamped;
            var body = Rect("Action content",scroll); body.anchorMin = new Vector2(0,1); body.anchorMax = Vector2.one; body.pivot = new Vector2(.5f,1); body.sizeDelta = Vector2.zero;
            var layout = body.gameObject.AddComponent<VerticalLayoutGroup>(); layout.spacing = 8; layout.childControlHeight = layout.childControlWidth = true; layout.childForceExpandHeight = false;
            body.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize; sv.viewport = scroll; sv.content = body;
            var search = Field(body,"Имя / код","",false); search.characterLimit = 80;
            var categories = new List<string> { "Все категории" }; foreach (var c in choices) if (c.category != "" && !categories.Contains(c.category)) categories.Add(c.category);
            string category = ""; int page = 0; const int pageSize = 24; Action render = null; Button group = null;
            if (categories.Count > 1)
            {
                group = AddButton(body,"Все категории ▾",() =>
                {
                    var list = Rect("Action categories",body); list.SetSiblingIndex(group.transform.GetSiblingIndex() + 1); var l = list.gameObject.AddComponent<VerticalLayoutGroup>(); l.spacing = 4; l.childControlWidth = l.childControlHeight = true; l.childForceExpandHeight = false;
                    list.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize; group.interactable = false;
                    for (int i = 0; i < categories.Count; i++) { int index = i; AddButton(list,categories[i],() => { category = index == 0 ? "" : categories[index]; group.GetComponentInChildren<Text>().text = categories[index] + " ▾"; group.interactable = true; list.gameObject.SetActive(false); Destroy(list.gameObject); page = 0; render(); }); }
                });
            }
            var info = Label(body,"",16); info.gameObject.AddComponent<LayoutElement>().preferredHeight = 38;
            var items = Rect("Action choices",body); var il = items.gameObject.AddComponent<GridLayoutGroup>(); il.spacing = new Vector2(6,6); il.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            var itemSize = items.gameObject.AddComponent<LayoutElement>();
            var navigation = Rect("Action pages",body); navigation.gameObject.AddComponent<LayoutElement>().preferredHeight = 56; var row = navigation.gameObject.AddComponent<HorizontalLayoutGroup>(); row.spacing = 8; row.childControlWidth = row.childControlHeight = true; row.childForceExpandWidth = true;
            Button previous = null, next = null; var filtered = new List<PickerChoice>();
            render = () =>
            {
                texturePickerSize = texturePicker.rect.size;
                bool landscape = Screen.width > Screen.height; il.constraintCount = landscape ? 2 : 1;
                h.sizeDelta = new Vector2(-130,landscape ? 50 : 62); cr.sizeDelta = new Vector2(108,landscape ? 48 : 56); scroll.offsetMax = new Vector2(-16,landscape ? -56 : -68);
                search.transform.parent.GetComponent<LayoutElement>().preferredHeight = landscape ? 48 : 58;
                if (group != null) group.GetComponent<LayoutElement>().preferredHeight = landscape ? 48 : 60;
                info.GetComponent<LayoutElement>().preferredHeight = landscape ? 26 : 38;
                for (int i = items.childCount - 1; i >= 0; i--) { items.GetChild(i).gameObject.SetActive(false); Destroy(items.GetChild(i).gameObject); }
                filtered.Clear(); string query = search.text.Trim(); foreach (var entry in choices) if ((category == "" || entry.category == category) && (entry.title.IndexOf(query,StringComparison.OrdinalIgnoreCase) >= 0 || entry.value.ToString().IndexOf(query,StringComparison.Ordinal) >= 0)) filtered.Add(entry);
                int pages = Math.Max(1,(filtered.Count + pageSize - 1) / pageSize); page = Math.Max(0,Math.Min(page,pages - 1)); previous.interactable = page > 0; next.interactable = page + 1 < pages;
                info.text = filtered.Count + " вариантов · " + (page + 1) + " / " + pages;
                Canvas.ForceUpdateCanvases(); il.cellSize = new Vector2(Math.Max(48,(scroll.rect.width - (il.constraintCount - 1) * 6) / il.constraintCount),60);
                int count = Math.Min(pageSize,filtered.Count - page * pageSize); itemSize.preferredHeight = Math.Max(0,((count + il.constraintCount - 1) / il.constraintCount) * 66 - 6);
                for (int i = page * pageSize; i < Math.Min(filtered.Count,(page + 1) * pageSize); i++)
                {
                    var entry = filtered[i]; var button = AddButton(items,titleFor == null ? entry.value + " · " + entry.title : titleFor(entry),() => { selected(entry); if (closeOnSelect) CloseTexturePicker(); else render(); });
                    var text = button.GetComponentInChildren<Text>(); text.resizeTextMaxSize = 18; text.resizeTextMinSize = 12;
                    text.supportRichText=richText;
                }
            };
            previous = Button(navigation,"← Назад",() => { page--; render(); }); next = Button(navigation,"Далее →",() => { page++; render(); });
            refreshTexturePicker = render; search.onValueChanged.AddListener(_ => { page = 0; render(); }); render();
        }
        void ActionPicker(InputField target, bool sector)
        {
            var choices = new List<PickerChoice>(); foreach (var action in sector ? Actions.sectorChoices : Actions.lineChoices)
                choices.Add(new PickerChoice { value = action.id,title = (action.prefix == "" ? "" : action.prefix + " · ") + action.title,category = action.category });
            ChoicePicker(sector ? "Эффекты сектора" : "Действия · " + Actions.label,choices,entry => target.text = entry.value.ToString());
        }
        static string ClassicTrigger(string prefix)
        {
            if (prefix.Length != 2) return prefix;
            string start = prefix[0] == 'W' ? "Пересечь стену" : prefix[0] == 'G' ? "Попасть выстрелом" : prefix[0] == 'S' ? "Использовать выключатель" : prefix[0] == 'D' ? "Использовать дверь" : prefix;
            return start + (prefix[1] == 'R' ? " · повторяется" : prefix[1] == '1' ? " · один раз" : "");
        }
        InputField ActionControls(Transform body, int value, int[] arguments, List<Action<MapDocument>> apply, Func<MapDocument,MapElementData> element, Action<MapDocument,int> setAction, Action<MapDocument,int,int> setArg, bool sector = false)
        {
            int min = MapFormats.IsUniversal(Map.format) ? int.MinValue : 0, max = sector || Map.format != MapFormat.Hexen ? (MapFormats.IsUniversal(Map.format) ? int.MaxValue : 65535) : 255;
            var code = Field(body,sector ? "Эффект" : "Действие",value.ToString()); apply.Add(m => setAction(m,Number(code,min,max)));
            var description = Label(body,"",17); description.alignment = TextAnchor.MiddleLeft; description.resizeTextForBestFit = true; description.resizeTextMinSize = 12; description.resizeTextMaxSize = 17; description.gameObject.AddComponent<LayoutElement>().preferredHeight = 80;
            AddButton(body,sector ? "Выбрать эффект по названию…" : "Выбрать действие по названию…",() => ActionPicker(code,sector));
            if(sector ? Actions.generalizedSectors.Count>0 : Actions.generalizedLines.Count>0)
                AddButton(body,sector ? "Настроить обобщённый эффект…" : "Настроить действие Boom…",()=>GeneralizedPicker(code,sector));
            var fields = new InputField[5]; var labels = new Text[5]; var valueButtons = new Button[5]; var strings = new InputField[5]; var stringLabels = new Text[5];
            bool hasFields = !sector && Map.format != MapFormat.Doom;
            if (hasFields) for (int i = 0; i < 5; i++)
            {
                int index = i; fields[i] = Field(body,"Аргумент " + i,arguments[i].ToString()); labels[i] = fields[i].transform.parent.GetComponentInChildren<Text>(); labels[i].resizeTextForBestFit = true; labels[i].resizeTextMinSize = 11; labels[i].resizeTextMaxSize = 18;
                apply.Add(m => setArg(m,index,Number(fields[index],MapFormats.IsUniversal(Map.format) ? int.MinValue : 0,MapFormats.IsUniversal(Map.format) ? int.MaxValue : 255)));
                valueButtons[i] = AddButton(body,"Выбрать значение…",() => { int id; int.TryParse(code.text,out id); ArgumentPicker(fields[index],Actions.Find(id).arguments[index],id,index,fields[1],code); });
                
#if MOBILEUDB_UDMF // Deferred from the first release.
if (MapFormats.IsUniversal(Map.format))
                {
                    string key = "arg" + i + "str", raw = UdmfText.Raw(element(Map).fields,key); bool touched = false;
                    strings[i] = Field(body,"Строковый аргумент " + i,raw == null ? "" : UdmfText.DecodeString(raw),false); strings[i].characterLimit = 512; stringLabels[i] = strings[i].transform.parent.GetComponentInChildren<Text>();
                    strings[i].onValueChanged.AddListener(_ => touched = true);
                    apply.Add(m => { if (touched) UdmfText.SetRaw(element(m).fields,key,UdmfText.Quote(strings[index].text)); });
                }
#endif

            }
            Button defaults = null;
            if (hasFields) defaults = AddButton(body,"Аргументы: значения по умолчанию",() => { int id; int.TryParse(code.text,out id); var action = Actions.Find(id); for (int i = 0; i < 5; i++) if (action.arguments[i].used) fields[i].text = action.arguments[i].defaultValue.ToString(); });
            Action refreshHexenTag=hasFields&&Map.format==MapFormat.Hexen&&element(Map) is MapLine?HexenTagControl(body,code,fields,valueButtons,apply):null;
            Action refresh = () =>
            {
                int id; int.TryParse(code.text,out id); var action = Actions.Find(id,sector);
                description.text = action.title + (action.prefix == "" ? "" : " · " + action.prefix) + "\n" + (Actions.warning ?? (action.prefix != "" ? ClassicTrigger(action.prefix) : Actions.hasArguments && !sector && !action.requiresActivation ? "Не требует триггера активации." : Actions.label));
                bool used = false;
                if (hasFields) for (int i = 0; i < 5; i++)
                {
                    var arg = action.arguments[i]; used |= arg.used;
                    labels[i].text = (i + 1) + " · " + (arg.used ? arg.title : "Аргумент " + i);
                    valueButtons[i].interactable = IsTagArgument(arg) || IsPolyobjectArgument(arg) || IsScriptArgument(id,i) || arg.choices.Count > 0 || arg.flags.Count > 0;
                    valueButtons[i].GetComponentInChildren<Text>().text = "Значение " + (i + 1) + ": " + (arg.used ? arg.title : "вручную") + "…";
                    if (strings[i] != null) { strings[i].transform.parent.gameObject.SetActive(arg.stringValue); stringLabels[i].text = arg.stringTitle; }
                }
                if (defaults != null) defaults.interactable = used;
                if (refreshHexenTag != null) refreshHexenTag();
            };
            code.onValueChanged.AddListener(_ => refresh()); refresh(); return code;
        }
        void ArgumentPicker(InputField field, ActionArgument argument,int action=0,int index=-1,InputField mapField=null,InputField actionField=null,bool zeroFromBlank=false)
        {
            if(IsScriptArgument(action,index)){ScriptNumberPicker(field,mapField,actionField,action);return;}
            if(IsPolyobjectArgument(argument)){PolyobjectNumberPicker(field,255,argument);return;}
            if(IsTagArgument(argument)){TagPicker(field,argument.type,Map.format==MapFormat.Hexen?255:int.MaxValue,argument);return;}
            var choices = new List<PickerChoice>(); bool bits = argument.type == 12, multiple = bits || argument.flags.Count > 0;
            int baseMask = 0, flagMask = 0;
            foreach (var option in argument.choices) { choices.Add(new PickerChoice { value = option.value,title = option.title,flag = bits }); if (bits) flagMask |= option.value; else baseMask |= option.value; }
            foreach (var option in argument.flags) { choices.Add(new PickerChoice { value = option.value,title = option.title,flag = true }); flagMask |= option.value; }
            ChoicePicker(argument.title,choices,entry =>
            {
                int current = !entry.flag&&!multiple||zeroFromBlank&&string.IsNullOrWhiteSpace(field.text)?0:Number(field,MapFormats.IsUniversal(Map.format) ? int.MinValue : 0,MapFormats.IsUniversal(Map.format) ? int.MaxValue : 255);
                if (entry.flag) current = entry.value == 0 ? current & ~flagMask : (current & entry.value) == entry.value ? current & ~entry.value : current | entry.value;
                else current = multiple ? (current & ~baseMask) | entry.value : entry.value;
                field.text = current.ToString();
            },!multiple,entry =>
            {
                int current; int.TryParse(field.text,out current); bool on = entry.flag ? entry.value == 0 ? (current & flagMask) == 0 : (current & entry.value) == entry.value : multiple ? (current & baseMask) == entry.value : current == entry.value;
                return (on ? "✓ " : "") + entry.value + " · " + entry.title;
            });
        }
        void ActivationControls(Transform body, MapLine line, InputField binaryFlags, List<Action<MapDocument>> apply, int index)
        {
            if (!Actions.hasArguments) return;
            if (Map.format == MapFormat.Hexen)
            {
                var choices = new List<PickerChoice>(); foreach (var option in Actions.activations) choices.Add(new PickerChoice { value = option.value,title = option.title });
                Button trigger = null; Action refresh = () => { int flags; int.TryParse(binaryFlags.text,out flags); string name = "Неизвестный триггер"; foreach (var option in Actions.activations) if (option.value == (flags & 7168)) name = option.title; trigger.GetComponentInChildren<Text>().text = "Активация: " + name + "…"; };
                trigger = AddButton(body,"Активация…",() => ChoicePicker("Активация стены",choices,choice => { binaryFlags.text = ActionCatalog.SetActivation(Number(binaryFlags,0,65535),choice.value).ToString(); }));
                binaryFlags.onValueChanged.AddListener(_ => refresh()); refresh();
                foreach (var option in new[] { new ActionOption { value = 512,title = "Повторять действие" },new ActionOption { value = 8192,title = "Монстры активируют" },new ActionOption { value = 32768,title = "Блокировать всех" } })
                {
                    var item = option; Button button = null; Action update = () => { int flags; int.TryParse(binaryFlags.text,out flags); button.GetComponentInChildren<Text>().text = item.title + ": " + ((flags & item.value) != 0 ? "Да" : "Нет"); };
                    button = AddButton(body,item.title,() => binaryFlags.text = (Number(binaryFlags,0,65535) ^ item.value).ToString()); binaryFlags.onValueChanged.AddListener(_ => update()); update();
                }
            }
            
#if MOBILEUDB_UDMF // Deferred from the first release.
else if (MapFormats.IsUniversal(Map.format))
            {
                var activations = Actions.extended ? GameConfigText.Read(ActionConfig("ZDoom_misc.cfg"),ActionConfig).Section("linedefactivations_udmf") : GameConfigText.Read("playercross=\"Player walks over\";playeruse=\"Player presses use\";monstercross=\"Monster walks over\";impact=\"Projectile hits\";playerpush=\"Player bumps\";missilecross=\"Projectile crosses\";repeatspecial=\"Repeatable action\";monsteractivate=\"Monster activates\";");
                foreach (var p in activations.values) { string key = p.Key; BooleanControl(body,p.Value,UdmfText.Boolean(line.fields,key,false),apply,(m,on) => UdmfText.SetBool(m.lines[index].fields,key,on)); }
                foreach (var p in activations.children) { string key = p.Key; BooleanControl(body,p.Value.Text("name",key),UdmfText.Boolean(line.fields,key,false),apply,(m,on) => UdmfText.SetBool(m.lines[index].fields,key,on)); }
            }
#endif

        }
    }
}


