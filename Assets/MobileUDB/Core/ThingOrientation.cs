// GPL-3.0. Binary ThingsMode ThingPointAtCursor / RotateThings behavior.
using System;
using System.Collections.Generic;

namespace MobileUDB.Core
{
    public static class ThingOrientation
    {
        public static int PointAngle(MapPoint position,MapPoint target,bool away,bool snap45)
        {
            if(!Finite(position.x)||!Finite(position.y)||!Finite(target.x)||!Finite(target.y))throw new InvalidOperationException("Некорректная точка направления.");
            // Vector2D.GetAngle(target, position), then Angle2D.RealToDoom.
            double real=-Math.Atan2(-((double)target.y-position.y),(double)target.x-position.x)+Math.PI*.5;
            if(away)real+=Math.PI;
            double angle=real-Math.PI*.5;while(angle<0)angle+=Math.PI*2;while(angle>=Math.PI*2)angle-=Math.PI*2;
            int degrees=(int)Math.Round(angle*57.295779513082320876798154814105);
            // Source keeps 360 as a raw PointAt result; do not change it to zero.
            return snap45?(degrees+22)/45*45:degrees;
        }
        internal static bool Finite(float value){return !float.IsNaN(value)&&!float.IsInfinity(value);}
        static ThingAnglePlan Targets(MapDocument map,IEnumerable<int> indices)
        {
            ThingClipboard.Binary(map);if(indices==null)throw new ArgumentNullException("indices");
            var plan=new ThingAnglePlan(map);var seen=new HashSet<int>();
            foreach(int index in indices)
            {
                if(index<0||index>=map.things.Count||map.things[index]==null)throw new InvalidOperationException("Объект больше не существует. Выберите его снова.");
                var thing=map.things[index];if(!Finite(thing.x)||!Finite(thing.y)||!Finite(thing.angle))throw new InvalidOperationException("Некорректные свойства объекта.");
                if(seen.Add(index))plan.Add(index,thing);
            }
            if(plan.Count==0)throw new InvalidOperationException("Выберите объекты.");return plan;
        }
        public static ThingAnglePlan PointAt(MapDocument map,IEnumerable<int> indices,MapPoint target,bool away=false,bool snap45=true,Func<int,bool> fixedRotation=null)
        {
            if(!Finite(target.x)||!Finite(target.y))throw new InvalidOperationException("Некорректная точка направления.");var plan=Targets(map,indices);
            for(int i=0;i<plan.Count;i++)
            {var thing=plan.Thing(i);if(fixedRotation==null||!fixedRotation(thing.type))plan.Angle(i,PointAngle(thing.Position,target,away,snap45));}
            return plan;
        }
        public static ThingAnglePlan Step(MapDocument map,IEnumerable<int> indices,bool clockwise,bool snap45=true)
        {
            var plan=Targets(map,indices);int increment=(snap45?45:5)*(clockwise?-1:1);
            for(int i=0;i<plan.Count;i++)
            {
                var thing=plan.Thing(i);double raw=Math.Round(thing.angle);
                if(raw<0||raw>65535)throw new InvalidOperationException("Угол объекта выходит за границы Doom / Hexen.");
                int angle=(int)raw+increment;if(snap45)angle=angle/45*45;angle=(angle%360+360)%360;
                // Source step commands rotate every selected type, unlike PointAt.
                plan.Angle(i,angle);
            }
            return plan;
        }
    }
    public sealed class ThingAnglePlan
    {
        sealed class Entry {public int index,type;public MapThing thing;public float x,y,before,after;}
        readonly MapDocument original;readonly MapFormat format;readonly List<Entry> entries=new List<Entry>();
        readonly Dictionary<int,float> preview=new Dictionary<int,float>();bool applied;
        public int Count {get{return entries.Count;}}
        public int ChangeCount {get;private set;}
        internal ThingAnglePlan(MapDocument map){original=map;format=map.format;}
        internal void Add(int index,MapThing thing){entries.Add(new Entry{index=index,type=thing.type,thing=thing,x=thing.x,y=thing.y,before=thing.angle,after=thing.angle});preview[index]=thing.angle;}
        internal MapThing Thing(int at){return entries[at].thing;}
        internal void Angle(int at,float value){var entry=entries[at];if(entry.after!=entry.before)ChangeCount--;entry.after=value;preview[entry.index]=value;if(value!=entry.before)ChangeCount++;}
        public bool TryAngle(int index,out float angle){return preview.TryGetValue(index,out angle);}
        public void Apply(MapDocument map)
        {
            if(applied||!ReferenceEquals(map,original)||map.format!=format)throw new InvalidOperationException("Карта изменилась. Повторите поворот объектов.");
            foreach(var entry in entries)if(entry.index>=map.things.Count||!ReferenceEquals(map.things[entry.index],entry.thing)||entry.thing.type!=entry.type||entry.thing.x!=entry.x||entry.thing.y!=entry.y||entry.thing.angle!=entry.before)
                throw new InvalidOperationException("Объекты изменились. Повторите поворот.");
            applied=true;foreach(var entry in entries)if(entry.after!=entry.before)entry.thing.angle=entry.after;
        }
    }
}
