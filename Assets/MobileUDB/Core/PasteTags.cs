// GPL-3.0. Tag traversal follows UDB MapSet.ForAllTags / Tools.RenumberMarkedTags.
using System;
using System.Collections.Generic;
using System.Globalization;

namespace MobileUDB.Core
{
    public enum PasteTagMode { Keep, Renumber, Remove }
    public static class PasteTags
    {
        public static int NextFree(MapDocument map,ActionCatalog actions,int maximum,int pending=0)
        {
            if(maximum<1)throw new ArgumentOutOfRangeException("maximum");
            var used=new HashSet<int>{pending};foreach(var slot in Collect(map,actions))if(slot.value>0)used.Add(slot.value);
            long next=1;while(next<=maximum&&used.Contains((int)next))next++;
            if(next>maximum)throw new InvalidOperationException("Нет свободных тегов в диапазоне 1–"+maximum+".");
            return (int)next;
        }
        sealed class Slot
        {
            public int value, maximum;
            public Action<int> set;
        }
        // Plan every change before mutating the detached paste snapshot. The target is read-only.
        public static void Apply(MapDocument copy, MapDocument target, PasteTagMode mode, ActionCatalog actions = null)
        {
            if (!Enum.IsDefined(typeof(PasteTagMode),mode)) throw new InvalidOperationException("Неизвестный режим тегов вставки.");
            if (mode == PasteTagMode.Keep) return;
            if (copy.format != target.format || (copy.format == MapFormat.Udmf && !string.Equals(copy.udmfNamespace,target.udmfNamespace,StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Форматы карты и копии должны совпадать.");
            if (copy.format != MapFormat.Doom && actions == null) throw new InvalidOperationException("Для изменения тегов нужен профиль аргументов действий.");
            var finish = new List<Action>(); var slots = Collect(copy,actions,finish); var mapping = new Dictionary<int,int>();
            if (mode == PasteTagMode.Renumber)
            {
                var used = new HashSet<int>(); foreach(var slot in Collect(target,actions)) if(slot.value > 0) used.Add(slot.value);
                var limits = new Dictionary<int,int>();
                foreach(var slot in slots) if(slot.value != 0)
                {
                    int limit; limits[slot.value] = limits.TryGetValue(slot.value,out limit) ? Math.Min(limit,slot.maximum) : slot.maximum;
                }
                var old = new List<int>(limits.Keys);
                // Allocate the scarce byte-range IDs first; the same old ID always gets one new ID.
                old.Sort((a,b) => { int order = limits[a].CompareTo(limits[b]); return order == 0 ? a.CompareTo(b) : order; });
                long next = 1;
                foreach(int tag in old)
                {
                    while(next <= limits[tag] && used.Contains((int)next)) next++;
                    if(next > limits[tag]) throw new InvalidOperationException("Нет свободных тегов в диапазоне 1–" + limits[tag] + ". Вставка не выполнена.");
                    mapping[tag] = (int)next; used.Add((int)next); next++;
                }
            }
            foreach(var slot in slots) if(slot.value != 0) slot.set(mode == PasteTagMode.Remove ? 0 : mapping[slot.value]);
            foreach(var apply in finish) apply();
        }
        static List<Slot> Collect(MapDocument map, ActionCatalog actions, List<Action> finish = null)
        {
            var slots = new List<Slot>(); int maximum = map.format == MapFormat.Udmf ? int.MaxValue : 65535;
            foreach(var s in map.sectors) { Add(slots,s.tag,maximum,n=>s.tag=n); More(slots,s,map.format,maximum,finish); }
            if(map.format != MapFormat.Doom)
                foreach(var t in map.things) { Add(slots,t.tag,maximum,n=>t.tag=n); Arguments(slots,t.args,t.action,map.format,actions); }
            foreach(var l in map.lines)
            {
                if(map.format != MapFormat.Hexen) { Add(slots,l.tag,maximum,n=>l.tag=n); More(slots,l,map.format,maximum,finish); }
                var encoding=map.format==MapFormat.Hexen?HexenLineTags.Encoding(l,actions):null;
                if(encoding!=null)Add(slots,encoding.Read(l),encoding.Maximum,n=>encoding.Write(l,n));
                if(map.format != MapFormat.Doom) Arguments(slots,l.args,l.action,map.format,actions,encoding==null?0:encoding.ArgumentMask);
            }
            return slots;
        }
        static void Add(List<Slot> slots, int value, int maximum, Action<int> set)
        { slots.Add(new Slot { value=value,maximum=maximum,set=set }); }
        static void Arguments(List<Slot> slots, int[] args, int action, MapFormat format, ActionCatalog actions,int skipMask=0)
        {
            if(actions == null || !actions.hasArguments) return;
            var info = actions.Find(action);
            for(int i=0;i<5;i++) if((skipMask&(1<<i))==0 && info.arguments[i].used && info.arguments[i].type >= 13 && info.arguments[i].type <= 15)
            { int index=i; Add(slots,args[i],format == MapFormat.Hexen ? 255 : int.MaxValue,n=>args[index]=n); }
        }
        static void More(List<Slot> slots, MapElementData element, MapFormat format, int maximum, List<Action> finish)
        {
            if(format != MapFormat.Udmf) return;
            string raw = UdmfText.Raw(element.fields,"moreids"); if(raw == null) return;
            string text = UdmfText.DecodeString(raw);
            string[] tokens = text.Split((char[])null,StringSplitOptions.RemoveEmptyEntries); var values = new int[tokens.Length];
            for(int i=0;i<tokens.Length;i++)
                if(!int.TryParse(tokens[i],NumberStyles.Integer,CultureInfo.InvariantCulture,out values[i]))
                    throw new InvalidOperationException("Некорректный moreids: " + text);
            for(int i=0;i<values.Length;i++)
            {
                int index=i;
                Add(slots,values[i],maximum,n=>values[index]=n);
            }
            if(finish != null) finish.Add(()=>
            {
                var parts=new List<string>(); var seen=new HashSet<int>();
                foreach(int n in values) if(n!=0&&seen.Add(n)) parts.Add(n.ToString(CultureInfo.InvariantCulture));
                if(parts.Count==0) element.fields.RemoveAll(field=>string.Equals(field.name,"moreids",StringComparison.OrdinalIgnoreCase));
                else UdmfText.SetRaw(element.fields,"moreids",UdmfText.Quote(string.Join(" ",parts.ToArray())));
            });
        }
    }
}
