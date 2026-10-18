using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace MobileUDB.Core
{
    // Original .dbs selectiongroups: maps.<name>.selectiongroups.<0..9>,
    // with space-separated raw element indices. Other settings are not applied.
    public sealed class DbsSelectionGroups
    {
        public const int MaximumBytes = 8 * 1024 * 1024;
        sealed class MapGroups
        {
            public readonly Dictionary<ElementRef,int> members = new Dictionary<ElementRef,int>();
        }
        readonly Dictionary<string,MapGroups> maps = new Dictionary<string,MapGroups>(StringComparer.OrdinalIgnoreCase);
        readonly List<string> names = new List<string>();
        public IReadOnlyList<string> MapNames { get { return names.AsReadOnly(); } }
        public static bool IsFilename(string filename)
        { return filename != null && filename.EndsWith(".dbs",StringComparison.OrdinalIgnoreCase); }

        public static DbsSelectionGroups Read(byte[] bytes)
        { return ReadRoot(ReadConfiguration(bytes),false); }
        internal static ConfigNode ReadConfiguration(byte[] bytes)
        {
            if(bytes == null || bytes.Length == 0 || bytes.Length > MaximumBytes) throw new InvalidDataException("Файл .dbs пуст или превышает 8 МиБ.");
            string text = new UTF8Encoding(false,true).GetString(bytes).TrimStart('\uFEFF');
            var root = GameConfigText.Read(text,null,true);
            if(root.Text("type") != "Doom Builder Map Settings Configuration") throw new InvalidDataException("Это не файл настроек карты UDB (.dbs).");
            return root;
        }
        internal static DbsSelectionGroups ReadRoot(ConfigNode root,bool optional)
        {
            ConfigNode mapList;
            if(!root.children.TryGetValue("maps",out mapList)) throw new InvalidDataException("В .dbs нет настроек карт.");
            var result = new DbsSelectionGroups();
            string[] keys = {"vertices","linedefs","sectors","things"};
            foreach(var entry in mapList.children)
            {
                ConfigNode groups;
                if(!entry.Value.children.TryGetValue("selectiongroups",out groups)) continue;
                if(groups.values.Count > 0) throw new InvalidDataException("Некорректный список групп в .dbs.");
                var map = new MapGroups();
                foreach(var group in groups.children)
                {
                    int slot;
                    if(!int.TryParse(group.Key,NumberStyles.Integer,CultureInfo.InvariantCulture,out slot) || slot < 0 || slot >= SelectionGroups.SlotCount)
                        throw new InvalidDataException("Номер группы .dbs должен быть от 0 до 9.");
                    for(int kind=0;kind<keys.Length;kind++)
                    {
                        if(group.Value.children.ContainsKey(keys[kind])) throw new InvalidDataException("Некорректные индексы группы .dbs.");
                        string indices;
                        if(!group.Value.values.TryGetValue(keys[kind],out indices) || string.IsNullOrEmpty(indices)) continue;
                        // UDB splits on ordinary spaces; valid files use quoted strings.
                        foreach(string token in indices.Split(new[]{' '},StringSplitOptions.RemoveEmptyEntries))
                        {
                            int index;
                            if(!int.TryParse(token,NumberStyles.Integer,CultureInfo.InvariantCulture,out index) || index < 0)
                                throw new InvalidDataException("Некорректный индекс " + keys[kind] + " в группе .dbs.");
                            var id = new ElementRef((ElementKind)kind,index); int bits;
                            map.members.TryGetValue(id,out bits); map.members[id] = bits | (1 << slot);
                        }
                    }
                }
                result.maps.Add(entry.Key,map); result.names.Add(entry.Key);
            }
            if(result.maps.Count == 0 && !optional) throw new InvalidDataException("В .dbs нет сохранённых групп выделения.");
            return result;
        }
        public bool HasMap(string name) { return name != null && maps.ContainsKey(name); }

        MapGroups ForMap(MapDocument map)
        {
            if(map == null || (map.format != MapFormat.Doom && map.format != MapFormat.Hexen)) throw new InvalidOperationException("Импорт групп поддерживает бинарные карты Doom / Hexen.");
            MapGroups groups;
            if(!maps.TryGetValue(map.name,out groups)) throw new InvalidOperationException("В .dbs нет групп для карты " + map.name + ". Карты с группами: " + string.Join(", ",names) + ".");
            foreach(var entry in groups.members)
                if(entry.Key.index >= MapSelection.Count(map,entry.Key.kind)) throw new InvalidOperationException("Индексы .dbs не соответствуют текущей карте " + map.name + ". Откройте исходную карту из того же WAD.");
            return groups;
        }
        public int[,] Counts(MapDocument map)
        {
            var groups = ForMap(map); var result = new int[SelectionGroups.SlotCount,4];
            foreach(var entry in groups.members)for(int slot=0;slot<SelectionGroups.SlotCount;slot++)if((entry.Value & (1 << slot)) != 0)result[slot,(int)entry.Key.kind]++;
            return result;
        }
        public DbsSelectionGroupPlan Plan(MapDocument map,bool replace)
        {
            var groups = ForMap(map); map.Validate(); var writes = new SelectionGroupPlan(map);
            foreach(ElementKind kind in new[]{ElementKind.Vertex,ElementKind.Line,ElementKind.Sector,ElementKind.Thing})
                for(int index=0;index<MapSelection.Count(map,kind);index++)
                {
                    var id = new ElementRef(kind,index); var element = SelectionGroups.Element(map,id); int bits;
                    groups.members.TryGetValue(id,out bits); writes.Add(id,element,replace ? bits : element.selectionGroups | bits);
                }
            return new DbsSelectionGroupPlan(map,writes);
        }
    }
    public sealed class DbsSelectionGroupPlan
    {
        readonly MapDocument original;
        readonly byte[] state;
        readonly SelectionGroupPlan writes;
        internal DbsSelectionGroupPlan(MapDocument map,SelectionGroupPlan writes)
        {original = map; state = BinaryMapState.Capture(map); this.writes = writes;}
        public int ChangeCount { get { return writes.ChangeCount; } }
        public int Apply(MapDocument map)
        {
            if(!ReferenceEquals(original,map)) throw new InvalidOperationException("Карта изменилась. Импортируйте .dbs снова.");
            var current = BinaryMapState.Capture(map);
            if(current.Length != state.Length) throw new InvalidOperationException("Карта изменилась. Импортируйте .dbs снова.");
            for(int i=0;i<state.Length;i++)if(state[i] != current[i]) throw new InvalidOperationException("Карта изменилась. Импортируйте .dbs снова.");
            return writes.Apply(map);
        }
    }
}
