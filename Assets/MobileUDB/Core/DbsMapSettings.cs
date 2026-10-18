using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace MobileUDB.Core
{
    // Explicit interoperability for map-local grid/view and ten groups.
    // Other original map settings remain independent, unapplied metadata.
    public sealed class DbsMapSettings
    {
        readonly Dictionary<string,GridFrame> grids=new Dictionary<string,GridFrame>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string,List<MapTagLabel>> labels=new Dictionary<string,List<MapTagLabel>>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string,MapView> views=new Dictionary<string,MapView>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string,MapBackground> backgrounds=new Dictionary<string,MapBackground>(StringComparer.OrdinalIgnoreCase);
        public bool HasBackground(string name){return name!=null&&backgrounds.ContainsKey(name);}
        public MapBackground BackgroundFor(string name){MapBackground state;if(!backgrounds.TryGetValue(name,out state))throw new InvalidOperationException("В .dbs нет фона карты "+name+".");return state.Validated();}
        public DbsSelectionGroups Groups {get;private set;}
        public bool HasGrid(string name){return name!=null&&grids.ContainsKey(name);}
        public bool HasView(string name){return name!=null&&views.ContainsKey(name);}
        public MapView ViewFor(string name){MapView view;if(!views.TryGetValue(name,out view))throw new InvalidOperationException("В .dbs нет обзора карты "+name+".");return view.Validated();}
        public bool HasLabels(string name){return name!=null&&labels.ContainsKey(name);}
        public IReadOnlyList<MapTagLabel> LabelsFor(string name)
        {List<MapTagLabel> found;if(!labels.TryGetValue(name,out found))throw new InvalidOperationException("В .dbs нет подписей для карты "+name+".");return MapTagLabels.Copy(found).AsReadOnly();}
        public TagLabelPlan PlanLabels(MapDocument map,bool replace){return MapTagLabels.Import(map,LabelsFor(map.name),replace);}
        public static DbsMapSettings Read(byte[] bytes,bool allowEmpty=false)
        {
            var root=DbsSelectionGroups.ReadConfiguration(bytes);var result=new DbsMapSettings{Groups=DbsSelectionGroups.ReadRoot(root,true)};
            foreach(var entry in root.children["maps"].children)
            {
                foreach(string key in new[]{"viewpositionx","viewpositiony","viewscale"})if(entry.Value.children.ContainsKey(key))throw new InvalidDataException("Некорректный параметр обзора .dbs: "+key);
                if(entry.Value.values.ContainsKey("viewpositionx")&&entry.Value.values.ContainsKey("viewpositiony")&&entry.Value.values.ContainsKey("viewscale"))
                {var view=new MapView{x=(float)Single(entry.Value,"viewpositionx",0),y=(float)Single(entry.Value,"viewpositiony",0),scale=(float)Single(entry.Value,"viewscale",1)};result.views.Add(entry.Key,view.Validated());}
                if(entry.Value.values.ContainsKey("taglabels"))throw new InvalidDataException("Подписи .dbs должны быть секцией.");
                ConfigNode labelSection;if(entry.Value.children.TryGetValue("taglabels",out labelSection))
                {
                    if(labelSection.values.Count>0)throw new InvalidDataException("Некорректный список подписей .dbs.");
                    var names=new List<MapTagLabel>();
                    foreach(var pair in labelSection.children)
                    {
                        if(pair.Value.children.ContainsKey("tag")||pair.Value.children.ContainsKey("label"))throw new InvalidDataException("Некорректная подпись .dbs.");
                        string value;int tag=0;if(pair.Value.values.TryGetValue("tag",out value)&&!int.TryParse(value,NumberStyles.Integer,CultureInfo.InvariantCulture,out tag))throw new InvalidDataException("Некорректный номер подписи .dbs.");
                        string name=pair.Value.Text("label");if(tag!=0&&!string.IsNullOrEmpty(name))names.Add(new MapTagLabel(tag,name));
                    }
                    MapTagLabels.Validate(names);result.labels.Add(entry.Key,names);
                }
                if(entry.Value.values.ContainsKey("grid"))throw new InvalidDataException("Сетка .dbs должна быть секцией настроек.");
                ConfigNode grid;if(!entry.Value.children.TryGetValue("grid",out grid))continue;
                foreach(string key in new[]{"background","backsource","backoffsetx","backoffsety","backscalex","backscaley"})if(grid.children.ContainsKey(key))throw new InvalidDataException("Некорректный параметр фона .dbs: "+key);
                if(grid.values.ContainsKey("background"))
                {string image=grid.Text("background")??"";result.backgrounds.Add(entry.Key,new MapBackground{name=image,visible=image.Length>0,source=(int)Integer(grid,"backsource"),offsetX=(int)Integer(grid,"backoffsetx"),offsetY=(int)Integer(grid,"backoffsety"),scaleX=(int)Integer(grid,"backscalex",100),scaleY=(int)Integer(grid,"backscaley",100)}.Validated());}
                // Match the overloads in GridSetup.ReadFromConfig: size/rotation
                // are read as Single, origins as Int32 (midpoints to even).
                var frame=new GridFrame{step=Math.Max(1,Single(grid,"gridsize",32)),rotation=Single(grid,"gridrotate",0),originX=Integer(grid,"gridoriginx"),originY=Integer(grid,"gridoriginy")};
                result.grids.Add(entry.Key,frame.Validated());
            }
            if(!allowEmpty&&result.grids.Count==0&&result.views.Count==0&&result.labels.Count==0&&result.Groups.MapNames.Count==0)throw new InvalidDataException("В .dbs нет сетки, обзора, групп или подписей тегов.");
            return result;
        }
        static double Number(ConfigNode node,string key,double fallback)
        {
            if(node.children.ContainsKey(key))throw new InvalidDataException("Некорректный параметр .dbs: "+key);
            string text;if(!node.values.TryGetValue(key,out text))return fallback;
            // Native Configuration writes Single values with an f postfix.
            // Parse at Single precision before later Int32 rounding/conversion.
            if(text!=null&&text.IndexOf('f')>=0)
            {float single;if(!float.TryParse(text.Trim().Replace("f",""),NumberStyles.Float,CultureInfo.InvariantCulture,out single)||float.IsNaN(single)||float.IsInfinity(single))throw new InvalidDataException("Некорректное число Single .dbs: "+key);return single;}
            double n;if(!double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out n)||double.IsNaN(n)||double.IsInfinity(n))throw new InvalidDataException("Некорректное число .dbs: "+key);
            return n;
        }
        static double Single(ConfigNode node,string key,double fallback)
        {float n=(float)Number(node,key,fallback);if(float.IsInfinity(n))throw new InvalidDataException("Число .dbs выходит за диапазон Single: "+key);return n;}
        static double Integer(ConfigNode node,string key,double fallback=0)
        {double n=Math.Round(Number(node,key,fallback),MidpointRounding.ToEven);if(n<int.MinValue||n>int.MaxValue)throw new InvalidDataException("Параметр .dbs выходит за диапазон Int32.");return n;}
        public GridFrame GridFor(string mapName,GridFrame current)
        {
            GridFrame imported;if(!grids.TryGetValue(mapName,out imported))throw new InvalidOperationException("В .dbs нет сетки для карты "+mapName+".");
            var result=current.Validated();result.step=imported.step;result.rotation=imported.rotation;result.originX=imported.originX;result.originY=imported.originY;return result.Validated();
        }
        public static bool SameGrid(GridFrame a,GridFrame b)
        {return a.step==b.step&&a.rotation==b.rotation&&a.originX==b.originX&&a.originY==b.originY&&a.snap==b.snap&&a.increment==b.increment&&a.disableNearbySnap==b.disableNearbySnap&&a.disableNearbyThings==b.disableNearbyThings&&a.nearbyThingsPreferenceSet==b.nearbyThingsPreferenceSet&&a.fixedThingScale==b.fixedThingScale&&a.cardinalDrag==b.cardinalDrag&&a.dynamicGrid==b.dynamicGrid;}
        public static byte[] Export(IEnumerable<MapDocument> maps,Func<string,GridFrame> gridFor,Func<string,MapView> viewFor=null,Func<string,MapBackground> backgroundFor=null)
        {
            if(maps==null||gridFor==null)throw new ArgumentNullException(maps==null?"maps":"gridFor");
            var text=new StringBuilder("type = \"Doom Builder Map Settings Configuration\";\n\nmaps\n{\n");var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string[] keys={"vertices","linedefs","sectors","things"};
            foreach(var map in maps)
            {
                ThingClipboard.Binary(map);map.Validate();Name(map.name);
                if(!names.Add(map.name))throw new InvalidOperationException("Повтор имени карты при экспорте .dbs: "+map.name);
                var grid=gridFor(map.name).Validated();text.Append("  ").Append(map.name).Append("\n  {\n");
                var view=viewFor==null?null:viewFor(map.name);if(view!=null){view=view.Validated();Write(text,"viewpositionx",view.x);Write(text,"viewpositiony",view.y);Write(text,"viewscale",view.scale);}
                text.Append("    grid\n    {\n");
                Write(text,"gridsize",grid.step);Write(text,"gridrotate",grid.rotation);Write(text,"gridoriginx",grid.originX);Write(text,"gridoriginy",grid.originY);
                var background=backgroundFor==null?null:backgroundFor(map.name);if(background!=null)
                {background=background.Validated();text.Append("      background = \"").Append(Escape(background.visible?background.name:"")).Append("\";\n");Write(text,"backsource",background.visible?background.source:0);Write(text,"backoffsetx",background.offsetX);Write(text,"backoffsety",background.offsetY);Write(text,"backscalex",background.scaleX);Write(text,"backscaley",background.scaleY);}
                text.Append("    }\n    selectiongroups\n    {\n");
                // Include an empty section: explicit replace in the mobile import
                // may clear all groups, while a fresh upstream map also stays empty.
                for(int slot=0;slot<SelectionGroups.SlotCount;slot++)
                {
                    var content=new StringBuilder();
                    for(int kind=0;kind<keys.Length;kind++)
                    {
                        var indices=new List<string>();for(int i=0;i<MapSelection.Count(map,(ElementKind)kind);i++)if((SelectionGroups.Element(map,new ElementRef((ElementKind)kind,i)).selectionGroups&(1<<slot))!=0)indices.Add(i.ToString(CultureInfo.InvariantCulture));
                        if(indices.Count>0)content.Append("        ").Append(keys[kind]).Append(" = \"").Append(string.Join(" ",indices)).Append("\";\n");
                    }
                    if(content.Length>0)text.Append("      ").Append(slot).Append("\n      {\n").Append(content).Append("      }\n");
                }
                text.Append("    }\n    taglabels\n    {\n");int labelIndex=1;
                if(map.tagLabels!=null)foreach(var label in map.tagLabels)
                {
                    text.Append("      taglabel").Append(labelIndex++).Append("\n      {\n        tag = ").Append(label.tag.ToString(CultureInfo.InvariantCulture)).Append(";\n        label = \"").Append(Escape(label.label)).Append("\";\n      }\n");
                }
                text.Append("    }\n  }\n");
                if(text.Length>DbsSelectionGroups.MaximumBytes)throw new InvalidOperationException("Настройки .dbs превышают 8 МиБ.");
            }
            if(names.Count==0)throw new InvalidOperationException("Нет карт для экспорта .dbs.");
            text.Append("}\n");var bytes=new UTF8Encoding(false,true).GetBytes(text.ToString());if(bytes.Length>DbsSelectionGroups.MaximumBytes)throw new InvalidOperationException("Настройки .dbs превышают 8 МиБ.");return bytes;
        }
        static void Write(StringBuilder text,string key,double value)
        {
            string number=value.ToString("R",CultureInfo.InvariantCulture);int exponent=number.IndexOf('E');
            // Original Configuration identifies floating numbers by their dot.
            // Unity Mono R can emit 1E-10; 1.0E-10 is independently readable there.
            if(exponent>=0&&number.IndexOf('.')<0)number=number.Insert(exponent,".0");
            text.Append("      ").Append(key).Append(" = ").Append(number).Append(";\n");
        }
        static string Escape(string text){return text.Replace("\\","\\\\").Replace("\n","\\n").Replace("\r","\\r").Replace("\t","\\t").Replace("\"","\\\"");}
        static void Name(string name)
        {
            if(string.IsNullOrEmpty(name)||name.Length>8)throw new InvalidOperationException("Имя карты .dbs должно содержать 1–8 знаков.");
            foreach(char c in name)if(c<33||c>126||"{}=;(),.\"\\/".IndexOf(c)>=0)throw new InvalidOperationException("Имя карты нельзя передать в исходном формате .dbs: "+name);
        }
    }
}
