using System;
using System.Collections.Generic;
using System.Globalization;

namespace MobileUDB.Core
{
    public sealed class ActionOption { public int value; public string title; }
    public sealed class ActionArgument
    {
        public string title = "", stringTitle = "", tooltip = ""; public int type, defaultValue; public bool used, stringValue;
        public readonly List<ActionOption> choices = new List<ActionOption>(), flags = new List<ActionOption>();
        public readonly List<string> targetClasses = new List<string>();
    }
    public sealed class MapActionType
    {
        public int id; public string title, category, prefix = "", identifier = ""; public bool requiresActivation = true, lineToLineTag, lineToLineSameAction;
        public bool requiresUpperTexture,ignoreUpperTexture,ignoreMiddleTexture,ignoreLowerTexture;
        public bool floorLowerToLowest,floorRaiseToNextHigher,floorRaiseToHighest;
        public readonly ActionArgument[] arguments = { new ActionArgument(),new ActionArgument(),new ActionArgument(),new ActionArgument(),new ActionArgument() };
    }
    public sealed class ActionCatalog
    {
        readonly Dictionary<int,MapActionType> lines = new Dictionary<int,MapActionType>(), sectors = new Dictionary<int,MapActionType>();
        int maximumLineAction=int.MaxValue;
        public readonly List<MapActionType> lineChoices = new List<MapActionType>(), sectorChoices = new List<MapActionType>();
        public readonly List<ActionOption> activations = new List<ActionOption>();
        public readonly List<GeneralizedCategory> generalizedLines = new List<GeneralizedCategory>();
        public readonly List<GeneralizedOption> generalizedSectors = new List<GeneralizedOption>();
        public int GeneralizedSectorMask { get { int mask=0;foreach(var option in generalizedSectors)mask|=option.mask;return mask; } }
        public string label, warning;
        public bool hasArguments, extended, boomCompatible, mbf, mbf21;
        public ThingFlagRules thingFlags = ThingFlagRules.Load(new ConfigNode());
        public bool IsKnown(int id,bool sector=false)
        {
            if(id==0)return true;if(id<0||(!sector&&id>maximumLineAction))return false;
            if((sector?sectors:lines).ContainsKey(id))return true;
            return sector?(id&GeneralizedSectorMask)!=0:generalizedLines.Exists(category=>category.Contains(id));
        }
        public MapActionType Find(int id, bool sector = false)
        {
            MapActionType type;if((sector ? sectors : lines).TryGetValue(id,out type))return type;
            var category=sector ? null : generalizedLines.Find(x=>x.Contains(id));
            if(category!=null)return new MapActionType { id=id,title="Обобщённое · "+category.title,category="Boom",prefix=new[]{"W1","WR","S1","SR","G1","GR","D1","DR"}[(id-category.offset)&7] };
            if(sector && id>=0 && (id&GeneralizedSectorMask)!=0)
            {
                int basic=id&~GeneralizedSectorMask;string title=sectors.TryGetValue(basic,out type)?type.title:"Код "+basic;
                foreach(var option in generalizedSectors)if(option.Read(id)!=0)title+=" · "+option.title+": "+option.Describe(id);
                return new MapActionType { id=id,title=title,category="Обобщённые эффекты" };
            }
            return new MapActionType { id = id,title = id == 0 ? "None" : "Неизвестный код " + id,category = "Другие" };
        }
        public static ActionCatalog Build(Func<string,string> texts, string game, MapFormat format, string udmfNamespace, string engine = "auto")
        {
            MapFormats.RequireSupported(format);
            var catalog = new ActionCatalog(); string baseGame = game == "Doom2" ? "Doom" : game;
            catalog.maximumLineAction=format==MapFormat.Hexen?255:format==MapFormat.Doom?65535:int.MaxValue;
            catalog.mbf21 = engine == "MBF21";
            catalog.mbf = catalog.mbf21 || engine == "MBF";
            if(catalog.mbf && (format != MapFormat.Doom || baseGame != "Doom"))
                throw new InvalidOperationException("MBF / MBF21 используют бинарный формат Doom и игру Doom / Doom II.");
            string ns = (udmfNamespace ?? "zdoom").ToLowerInvariant();
            if (format == MapFormat.Udmf && ns != "zdoom")
            {
                if (ns == "doom" || ns == "doom2") baseGame = "Doom";
                else if (ns == "heretic" || ns == "hexen" || ns == "strife") baseGame = char.ToUpperInvariant(ns[0]) + ns.Substring(1);
                else { catalog.label = "UDMF · " + ns; catalog.warning = "Таблица действий этого namespace ещё не перенесена. Коды доступны вручную."; return catalog; }
            }
            if (format == MapFormat.Doom && baseGame == "Hexen") { catalog.label = "Hexen / Doom"; catalog.warning = "Для базового Hexen нужен формат Hexen. Коды доступны вручную."; return catalog; }
            catalog.hasArguments = format == MapFormat.Hexen || (format == MapFormat.Udmf && (ns == "zdoom" || ns == "hexen"));
            catalog.extended = engine == "ZDoom" || (format == MapFormat.Udmf && ns == "zdoom") || (format == MapFormat.Hexen && baseGame != "Hexen");
            bool boom = !catalog.hasArguments && (engine == "Boom" || catalog.mbf || catalog.extended);
            catalog.boomCompatible = boom;
            catalog.label = (catalog.mbf21 ? "MBF21" : catalog.mbf ? "MBF" : catalog.extended ? "ZDoom / GZDoom" : boom ? "Boom" : baseGame) + " · " + format;
            var cache = new Dictionary<string,ConfigNode>(StringComparer.OrdinalIgnoreCase);
            Func<string,ConfigNode> read = name => { ConfigNode node; if (!cache.TryGetValue(name,out node)) { node = GameConfigText.Read(texts(name),texts); cache[name] = node; } return node; };
            var flagDefinitions=new ConfigNode();
            flagDefinitions.Merge(read(format==MapFormat.Hexen?"Hexen_misc.cfg":"Doom_misc.cfg").Section("thingflagscompare"));
            if(boom)flagDefinitions.Merge(read("Boom_misc.cfg").Section("thingflagscompare"));
            catalog.thingFlags=ThingFlagRules.Load(flagDefinitions);
            if(boom) catalog.generalizedLines.AddRange(GeneralizedCategory.Load(read("Boom_generalized.cfg").Section("gen_linedeftypes")));
            if(catalog.extended || boom)
            {
                var generalized = new ConfigNode();
                generalized.Merge(read(catalog.extended ? "ZDoom_generalized.cfg" : "Boom_generalized.cfg").Section("gen_sectortypes"));
                if(catalog.mbf21)generalized.Merge(read("MBF21_generalized.cfg").Section("gen_sectortypes"));
                catalog.generalizedSectors.AddRange(GeneralizedOption.Load(generalized));
            }
            var definitions = new ConfigNode(); var effects = new ConfigNode(); var enums = new ConfigNode();
            enums.Merge(read(baseGame + "_misc.cfg").Section("enums"));
            if (catalog.extended || catalog.hasArguments)
            {
                enums.Merge(read("Doom_misc.cfg").Section("enums"));
                var misc = read("ZDoom_misc.cfg"); enums.Merge(misc.Section("enums")); enums.Merge(misc.Section("enums_" + baseGame.ToLowerInvariant()));
            }
            if (catalog.hasArguments)
            {
                definitions.Merge(read("Hexen_linedefs.cfg"));
                if (catalog.extended) { var zdoom = read("ZDoom_linedefs.cfg"); definitions.Merge(zdoom.Section("zdoom")); definitions.Merge(zdoom.Section(format == MapFormat.Udmf ? "udmf" : "hexen")); }
                effects.Merge(catalog.extended ? read("ZDoom_sectors.cfg").Section("zdoom") : read("Hexen_sectors.cfg"));
                catalog.activations.AddRange(Options(read("Hexen_misc.cfg").Section("linedefactivations")));
                if (catalog.extended) catalog.activations.AddRange(Options(read("ZDoom_misc.cfg").Section("linedefactivations")));
            }
            else
            {
                definitions.Merge(read(baseGame + "_linedefs.cfg")); effects.Merge(read(baseGame + "_sectors.cfg"));
                if (boom) definitions.Merge(read("Boom_linedefs.cfg"));
                if (catalog.mbf21) definitions.Merge(read("MBF21_linedefs.cfg"));
                if (catalog.extended) { definitions.Merge(read("ZDoom_linedefs.cfg").Section("doom")); effects.Merge(read("ZDoom_sectors.cfg").Section("doom")); }
            }
            catalog.Collect(definitions,enums,"",catalog.lines);
            catalog.Collect(effects,enums,"Эффекты сектора",catalog.sectors);
            foreach (var pair in catalog.lines) if (format != MapFormat.Hexen || pair.Key <= 255) catalog.lineChoices.Add(pair.Value);
            catalog.sectorChoices.AddRange(catalog.sectors.Values);
            catalog.lineChoices.Sort(Compare); catalog.sectorChoices.Sort(Compare);
            return catalog;
        }
        static int Compare(MapActionType a, MapActionType b) { int category = StringComparer.OrdinalIgnoreCase.Compare(a.category,b.category); return category != 0 ? category : a.id.CompareTo(b.id); }
        void Collect(ConfigNode node, ConfigNode enums, string category, Dictionary<int,MapActionType> target)
        {
            foreach (var pair in node.values)
            {
                int id; if (pair.Value != null && int.TryParse(pair.Key,out id)) target[id] = new MapActionType { id = id,title = pair.Value,category = category };
            }
            foreach (var pair in node.children)
            {
                int id; var child = pair.Value;
                if (!int.TryParse(pair.Key,out id)) { Collect(child,enums,child.Text("title",pair.Key),target); continue; }
                var type = new MapActionType { id = id,title = child.Text("title",id.ToString()),category = category,identifier = child.Text("id"),prefix = child.Text("prefix"),requiresActivation = child.Boolean("requiresactivation",true),lineToLineTag = child.Boolean("linetolinetag"),lineToLineSameAction = child.Boolean("linetolinesameaction") };
                var checks=child.Section("errorchecker");type.requiresUpperTexture=checks.Boolean("requiresuppertexture");
                type.ignoreUpperTexture=checks.Boolean("ignoreuppertexture");type.ignoreMiddleTexture=checks.Boolean("ignoremiddletexture");type.ignoreLowerTexture=checks.Boolean("ignorelowertexture");
                type.floorLowerToLowest=checks.Boolean("floorlowertolowest");type.floorRaiseToNextHigher=checks.Boolean("floorraisetonexthigher");type.floorRaiseToHighest=checks.Boolean("floorraisetohighest");
                for (int i = 0; i < 5; i++)
                {
                    ConfigNode arg; if (!child.children.TryGetValue("arg" + i,out arg)) continue;
                    var info = type.arguments[i]; info.used = true; info.title = arg.Text("title","Аргумент " + (i + 1)); info.stringTitle = arg.Text("titlestr",info.title); info.tooltip = arg.Text("tooltip"); info.type = arg.Integer("type"); info.defaultValue = arg.Integer("default"); info.stringValue = arg.Boolean("str");
                    foreach(string name in arg.Text("targetclasses").Split(','))if(name.Trim().Length>0)info.targetClasses.Add(name.Trim());
                    ConfigNode options; info.choices.AddRange(Options(arg.children.TryGetValue("enum",out options) ? options : enums.Section(arg.Text("enum"))));
                    info.flags.AddRange(Options(arg.children.TryGetValue("flags",out options) ? options : enums.Section(arg.Text("flags"))));
                    if (info.type == 3 && info.choices.Count == 0) { info.choices.Add(new ActionOption { value = 0,title = "Нет" }); info.choices.Add(new ActionOption { value = 1,title = "Да" }); }
                }
                target[id] = type;
            }
        }
        static List<ActionOption> Options(ConfigNode node)
        {
            var options = new List<ActionOption>(); foreach (var p in node.values) { int number; if (p.Value != null && int.TryParse(p.Key,NumberStyles.Integer,CultureInfo.InvariantCulture,out number)) options.Add(new ActionOption { value = number,title = p.Value }); }
            options.Sort((a,b) => a.value.CompareTo(b.value)); return options;
        }
        public static int SetActivation(int flags, int activation) { return (flags & ~7168) | (activation & 7168); }
    }
}
