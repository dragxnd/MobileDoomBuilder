using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MobileUDB.Core
{
    // Binary scope of upstream SelectSimilarElementOptionsPanel / PropertiesComparer.
    // Index signatures avoid comparing every candidate with every selected element.
    public static class SimilarSelection
    {
        public static PropertyFields Available(MapFormat format,ElementKind kind)
        {
            if(format!=MapFormat.Doom&&format!=MapFormat.Hexen)throw new InvalidOperationException("Поддерживаются бинарные карты Doom / Hexen.");
            switch(kind)
            {
                case ElementKind.Line:return PropertyFields.Sides|PropertyFields.LineFlags|PropertyFields.LineAction|(format==MapFormat.Hexen?PropertyFields.Activation|PropertyFields.LineArguments:PropertyFields.LineTag);
                case ElementKind.Sector:return PropertyFields.Sectors;
                case ElementKind.Thing:return PropertyFields.ThingType|PropertyFields.ThingAngle|PropertyFields.ThingFlags|(format==MapFormat.Hexen?PropertyFields.ThingHeight|PropertyFields.ThingTag|PropertyFields.ThingAction|PropertyFields.ThingArguments:PropertyFields.None);
                default:return PropertyFields.None;
            }
        }
        static void Index(int index,int count){if(index<0||index>=count)throw new InvalidOperationException("Элемент больше не существует. Выберите его снова.");}
        static void Add(StringBuilder key,PropertyFields fields,PropertyFields flag,int value)
        {if((fields&flag)!=0)key.Append(value.ToString(CultureInfo.InvariantCulture)).Append(';');}
        static void Add(StringBuilder key,PropertyFields fields,PropertyFields flag,float value)
        {if((fields&flag)!=0)key.Append(value.ToString("R",CultureInfo.InvariantCulture)).Append(';');}
        static void Add(StringBuilder key,PropertyFields fields,PropertyFields flag,string value)
        {if((fields&flag)!=0){value=(value??"").ToUpperInvariant();key.Append(value.Length).Append(':').Append(value).Append(';');}}
        static void Arguments(StringBuilder key,int[] args)
        {if(args==null||args.Length!=5)throw new InvalidOperationException("Требуются пять аргументов.");foreach(int value in args)key.Append(value).Append(';');}
        static string Signature(MapDocument map,ElementRef id,PropertyFields fields,int flagsMask)
        {
            var key=new StringBuilder();
            if(id.kind==ElementKind.Line)
            {
                var l=map.lines[id.index];Add(key,fields,PropertyFields.LineFlags,l.flags&flagsMask&(map.format==MapFormat.Hexen?~7168:65535));
                Add(key,fields,PropertyFields.Activation,l.flags&7168);Add(key,fields,PropertyFields.LineAction,l.action);Add(key,fields,PropertyFields.LineTag,l.tag);
                if((fields&PropertyFields.LineArguments)!=0)Arguments(key,l.args);
            }
            else if(id.kind==ElementKind.Sector)
            {
                var s=map.sectors[id.index];Add(key,fields,PropertyFields.FloorHeight,s.floor);Add(key,fields,PropertyFields.CeilingHeight,s.ceiling);
                Add(key,fields,PropertyFields.FloorTexture,s.floorTexture);Add(key,fields,PropertyFields.CeilingTexture,s.ceilingTexture);
                Add(key,fields,PropertyFields.Light,s.light);Add(key,fields,PropertyFields.SectorTag,s.tag);
            }
            else
            {
                var t=map.things[id.index];Add(key,fields,PropertyFields.ThingType,t.type);Add(key,fields,PropertyFields.ThingAngle,t.angle);
                Add(key,fields,PropertyFields.ThingHeight,t.z);Add(key,fields,PropertyFields.ThingFlags,t.flags&flagsMask);
                Add(key,fields,PropertyFields.ThingAction,t.action);Add(key,fields,PropertyFields.ThingTag,t.tag);
                if((fields&PropertyFields.ThingArguments)!=0)Arguments(key,t.args);
            }
            return key.ToString();
        }
        static string Side(MapDocument map,int index,PropertyFields fields)
        {
            Index(index,map.sides.Count);var side=map.sides[index];var key=new StringBuilder();
            Add(key,fields,PropertyFields.Upper,side.upper);Add(key,fields,PropertyFields.Middle,side.middle);Add(key,fields,PropertyFields.Lower,side.lower);
            Add(key,fields,PropertyFields.OffsetX,side.offsetX);Add(key,fields,PropertyFields.OffsetY,side.offsetY);return key.ToString();
        }
        static IEnumerable<string> Keys(MapDocument map,ElementRef id,PropertyFields fields,ActionCatalog actions,int flagsMask)
        {
            string main=Signature(map,id,fields,flagsMask);
            if(id.kind==ElementKind.Line)
            {
                var l=map.lines[id.index];
                // Upstream always requires at least one actual side pair, even with
                // no side properties enabled; front/back orientation is irrelevant.
                if(l.front>=0)yield return main+"|"+Side(map,l.front,fields);
                if(l.back>=0)yield return main+"|"+Side(map,l.back,fields);
            }
            else if(id.kind==ElementKind.Sector&&(fields&PropertyFields.SectorEffect)!=0)
            {
                int effect=map.sectors[id.index].special;yield return main+"|E"+effect;
                if(effect<=0||actions==null||actions.generalizedSectors.Count==0)yield break;
                int basic=effect;var bits=new HashSet<int>();
                // Same descending extraction as GameConfiguration.GetSectorEffectData.
                for(int i=actions.generalizedSectors.Count-1;i>=0;i--)
                {
                    var options=actions.generalizedSectors[i].choices;
                    for(int j=options.Count-1;j>=0;j--){int bit=options[j].value;if(bit>0&&(basic&bit)==bit){basic-=bit;bits.Add(bit);}}
                }
                foreach(int bit in bits)yield return main+"|G"+basic+":"+bits.Count+":"+bit;
            }
            else yield return main;
        }
        public static HashSet<ElementRef> Expand(MapDocument map,ElementKind kind,IEnumerable<ElementRef> selection,PropertyFields fields,ActionCatalog actions=null,int flagsMask=65535)
        {
            if(map==null)throw new ArgumentNullException("map");var available=Available(map.format,kind);
            if(available==PropertyFields.None)throw new InvalidOperationException("Выбор похожих доступен для стен, секторов и объектов.");
            if((fields&~available)!=0)throw new InvalidOperationException("Отмеченные свойства недоступны в текущем формате.");
            if(flagsMask<0||flagsMask>65535)throw new ArgumentOutOfRangeException("flagsMask");
            var result=new HashSet<ElementRef>();var signatures=new HashSet<string>(StringComparer.Ordinal);
            if(selection==null)throw new ArgumentNullException("selection");
            foreach(var id in selection)
            {
                if(id.kind!=kind)throw new InvalidOperationException("Выберите элементы одного режима.");Index(id.index,MapSelection.Count(map,kind));
                if(result.Add(id))foreach(string key in Keys(map,id,fields,actions,flagsMask))signatures.Add(key);
            }
            if(result.Count==0)throw new InvalidOperationException("Сначала выделите образцы для сравнения.");
            // Original seeds only: newly matched elements never broaden the criteria.
            for(int i=0;i<MapSelection.Count(map,kind);i++)
            {var id=new ElementRef(kind,i);if(result.Contains(id))continue;foreach(string key in Keys(map,id,fields,actions,flagsMask))if(signatures.Contains(key)){result.Add(id);break;}}
            return result;
        }
        public static HashSet<ElementRef> FilterSides(MapDocument map,IEnumerable<ElementRef> selection,bool doubleSided)
        {
            if(map==null)throw new ArgumentNullException("map");Available(map.format,ElementKind.Line);var result=new HashSet<ElementRef>();
            if(selection==null)throw new ArgumentNullException("selection");
            foreach(var id in selection)
            {
                if(id.kind!=ElementKind.Line)throw new InvalidOperationException("Фильтр сторон доступен для выбранных стен.");Index(id.index,map.lines.Count);
                var l=map.lines[id.index];if(l.front>=0)Index(l.front,map.sides.Count);if(l.back>=0)Index(l.back,map.sides.Count);
                // Actual references, not flag 4. Upstream single-sided filter also
                // retains orphan lines with neither side, for later repair.
                if((l.front>=0&&l.back>=0)==doubleSided)result.Add(id);
            }
            return result;
        }
        static HashSet<ElementRef> SelectedThings(MapDocument map,IEnumerable<ElementRef> selection)
        {
            if(map==null)throw new ArgumentNullException("map");if(selection==null)throw new ArgumentNullException("selection");
            var result=new HashSet<ElementRef>();
            foreach(var id in selection)
            {
                if(id.kind!=ElementKind.Thing)throw new InvalidOperationException("Фильтр типов доступен для выбранных объектов.");
                Index(id.index,map.things.Count);result.Add(id);
            }
            return result;
        }
        // FilterSelectedThingsForm groups only the original selection by Type,
        // sorts numeric IDs, and removes other types only when some are chosen.
        public static SortedDictionary<int,int> SelectedThingTypes(MapDocument map,IEnumerable<ElementRef> selection)
        {
            var counts=new SortedDictionary<int,int>();
            foreach(var id in SelectedThings(map,selection))
            {int type=map.things[id.index].type,count;counts.TryGetValue(type,out count);counts[type]=count+1;}
            return counts;
        }
        public static HashSet<ElementRef> FilterThingTypes(MapDocument map,IEnumerable<ElementRef> selection,IEnumerable<int> types)
        {
            if(types==null)throw new ArgumentNullException("types");var chosen=new HashSet<int>(types);var result=SelectedThings(map,selection);
            // Native Apply with no chosen rows leaves the selection intact.
            if(chosen.Count>0)result.RemoveWhere(id=>!chosen.Contains(map.things[id.index].type));
            return result;
        }
    }
}
