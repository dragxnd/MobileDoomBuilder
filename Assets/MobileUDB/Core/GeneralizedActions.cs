// GPL-3.0. Uses the original UDB Boom/ZDoom generalized configurations.
using System;
using System.Collections.Generic;
using System.Globalization;

namespace MobileUDB.Core
{
    public sealed class GeneralizedOption
    {
        public string key, title;
        public int mask;
        public readonly List<ActionOption> choices = new List<ActionOption>();
        public int Read(int bits) { return bits & mask; }
        public int Set(int bits, int value)
        {
            if(!choices.Exists(x=>x.value==value)) throw new InvalidOperationException("Недопустимое значение: " + title);
            return (bits & ~mask) | value;
        }
        public string Describe(int bits)
        {
            int value=Read(bits);var entry=choices.Find(x=>x.value==value);
            return entry == null ? "Неизвестное значение " + value : entry.title;
        }
        internal static List<GeneralizedOption> Load(ConfigNode node)
        {
            var result=new List<GeneralizedOption>();int occupied=0;
            foreach(var child in node.children)
            {
                var option=new GeneralizedOption { key=child.Key,title=child.Value.Text("name",child.Key) };
                foreach(var pair in child.Value.values)
                {
                    int value;if(!int.TryParse(pair.Key,NumberStyles.Integer,CultureInfo.InvariantCulture,out value)) continue;
                    if(value<0) throw new InvalidOperationException("Отрицательные биты обобщённого действия.");
                    option.mask|=value;option.choices.Add(new ActionOption { value=value,title=pair.Value });
                }
                if(option.choices.Count==0) continue;
                if((occupied&option.mask)!=0) throw new InvalidOperationException("Перекрывающиеся параметры обобщённого действия.");
                occupied|=option.mask;option.choices.Sort((a,b)=>a.value.CompareTo(b.value));result.Add(option);
            }
            result.Sort((a,b)=>a.mask.CompareTo(b.mask));return result;
        }
    }
    public sealed class GeneralizedCategory
    {
        public string key,title;
        public int offset,length;
        public List<GeneralizedOption> options;
        public bool Contains(int code) { return code>=offset && (long)code< (long)offset+length; }
        public int Set(int code, GeneralizedOption option, int value)
        {
            if(!Contains(code)||!options.Contains(option)) throw new InvalidOperationException("Действие не принадлежит категории.");
            int result=offset+option.Set(code-offset,value);
            if(!Contains(result)) throw new InvalidOperationException("Код вне диапазона категории.");
            return result;
        }
        public static List<GeneralizedCategory> Load(ConfigNode node)
        {
            var result=new List<GeneralizedCategory>();
            foreach(var child in node.children)
            {
                int length=child.Value.Integer("length");if(length<=0) continue;
                var category=new GeneralizedCategory { key=child.Key,title=child.Value.Text("title",child.Key),offset=child.Value.Integer("offset"),length=length,options=GeneralizedOption.Load(child.Value) };
                if(category.offset<0||(long)category.offset+length>65536) throw new InvalidOperationException("Некорректный диапазон обобщённых действий.");
                int mask=0;foreach(var option in category.options)mask|=option.mask;
                if(mask>=length) throw new InvalidOperationException("Биты выходят за диапазон категории.");
                result.Add(category);
            }
            result.Sort((a,b)=>a.offset.CompareTo(b.offset));return result;
        }
    }
}
