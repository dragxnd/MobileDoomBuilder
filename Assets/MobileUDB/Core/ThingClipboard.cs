using System;
using System.Collections.Generic;

namespace MobileUDB.Core
{
    // Separate from the geometry, texture and property clipboards.
    public sealed class ThingClipboard
    {
        readonly List<MapThing> copies;
        readonly MapFormat format;
        public int Count { get { return copies.Count; } }
        ThingClipboard(MapFormat format,List<MapThing> copies) { this.format=format;this.copies=copies; }
        internal static void Binary(MapDocument map)
        {if(map==null||(map.format!=MapFormat.Doom&&map.format!=MapFormat.Hexen))throw new InvalidOperationException("Поддерживаются бинарные карты Doom / Hexen.");}
        internal static MapThing Copy(MapThing thing)
        {
            var copy=new MapThing{x=thing.x,y=thing.y,z=thing.z,angle=thing.angle,type=thing.type,flags=thing.flags,tag=thing.tag,action=thing.action,args=(int[])thing.args.Clone(),udmfInitialized=thing.udmfInitialized};
            thing.CopyFieldsTo(copy);return copy;
        }
        internal static void Coordinate(float value)
        {if(float.IsNaN(value)||float.IsInfinity(value)||value<-32768||value>32767)throw new InvalidOperationException("Координата объекта выходит за допустимые границы Doom / Hexen.");}
        static void Unsigned(int value,int max)
        {if(value<0||value>max)throw new InvalidOperationException("Свойство объекта выходит за допустимые границы формата.");}
        internal static void Properties(MapThing thing,MapFormat format)
        {
            Unsigned(thing.type,65535);Unsigned(thing.flags,65535);
            if(float.IsNaN(thing.angle)||float.IsInfinity(thing.angle)||Math.Round(thing.angle)<0||Math.Round(thing.angle)>65535)throw new InvalidOperationException("Некорректный угол объекта.");
            if(format==MapFormat.Hexen){Unsigned(thing.tag,65535);Unsigned(thing.action,255);
                if(thing.args==null||thing.args.Length!=5)throw new InvalidOperationException("Объект Hexen должен иметь пять аргументов.");foreach(int arg in thing.args)Unsigned(arg,255);}
        }
        public static ThingClipboard Capture(MapDocument map,IEnumerable<int> indices)
        {
            Binary(map);if(indices==null)throw new ArgumentNullException("indices");
            var targets=new SortedSet<int>();foreach(int index in indices)
            {if(index<0||index>=map.things.Count||map.things[index]==null)throw new InvalidOperationException("Объект больше не существует. Выберите его снова.");targets.Add(index);}
            if(targets.Count==0)throw new InvalidOperationException("Сначала отметьте объекты.");
            var copies=new List<MapThing>();foreach(int index in targets)copies.Add(Copy(map.things[index]));return new ThingClipboard(map.format,copies);
        }
        public ThingInsertPlan PlanPaste(MapDocument map,MapPoint position)
        {
            Binary(map);if(map.format!=format)throw new InvalidOperationException("Буфер объектов использует другой формат карты. Копируйте между картами одного формата.");
            Coordinate(position.x);Coordinate(position.y);
            double x=Math.Round(position.x),y=Math.Round(position.y);
            double minX=copies[0].x,maxX=minX,minY=copies[0].y,maxY=minY;
            foreach(var thing in copies){Coordinate(thing.x);Coordinate(thing.y);minX=Math.Min(minX,thing.x);maxX=Math.Max(maxX,thing.x);minY=Math.Min(minY,thing.y);maxY=Math.Max(maxY,thing.y);}
            double centerX=minX+(maxX-minX)/2,centerY=minY+(maxY-minY)/2;
            var inserted=new List<MapThing>();foreach(var source in copies)
            {
                var thing=Copy(source);thing.x=(float)Math.Round(x-centerX+source.x);thing.y=(float)Math.Round(y-centerY+source.y);
                if(copies.Count>1)thing.z=(float)Math.Round(thing.z);
                Coordinate(thing.x);Coordinate(thing.y);if(map.format==MapFormat.Hexen)Coordinate(thing.z);Properties(thing,map.format);inserted.Add(thing);
            }
            return new ThingInsertPlan(map,inserted);
        }
        public static ThingInsertPlan PlanInsert(MapDocument map,MapPoint position,int type)
        {
            Binary(map);Coordinate(position.x);Coordinate(position.y);if(type<0||type>65535)throw new InvalidOperationException("Тип объекта за пределами Doom / Hexen.");
            return new ThingInsertPlan(map,new List<MapThing>{new MapThing{x=position.x,y=position.y,type=type,flags=map.format==MapFormat.Hexen?2023:7}});
        }
    }
    public sealed class ThingInsertPlan
    {
        readonly MapDocument original;
        readonly List<MapThing> inserted;
        readonly int first;
        readonly MapFormat format;
        bool applied;
        public int Count {get{return inserted.Count;}}
        internal ThingInsertPlan(MapDocument original,List<MapThing> inserted)
        {this.original=original;this.inserted=inserted;first=original.things.Count;format=original.format;}
        public List<int> Apply(MapDocument map)
        {
            if(applied||!ReferenceEquals(map,original)||map.format!=format||map.things.Count!=first)throw new InvalidOperationException("Карта изменилась. Повторите вставку объектов.");
            applied=true;var result=new List<int>();foreach(var thing in inserted){result.Add(map.things.Count);map.things.Add(ThingClipboard.Copy(thing));}return result;
        }
    }
}
