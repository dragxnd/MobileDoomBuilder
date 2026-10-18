using System;
using System.Collections.Generic;

namespace MobileUDB.Core
{
    // Validate the entire binary thing group before mutating it.
    public static class ThingBatchEdits
    {
        struct Position {public int index;public float x,y,z;}
        static List<int> Targets(MapDocument map,IEnumerable<int> indices)
        {
            if(map==null||(map.format!=MapFormat.Doom&&map.format!=MapFormat.Hexen))throw new InvalidOperationException("Поддерживаются бинарные карты Doom / Hexen.");
            if(indices==null)throw new ArgumentNullException("indices");var seen=new HashSet<int>();var result=new List<int>();
            foreach(int index in indices)
            {if(index<0||index>=map.things.Count||map.things[index]==null)throw new InvalidOperationException("Объект больше не существует. Выберите его снова.");if(seen.Add(index))result.Add(index);}
            return result;
        }
        static bool Finite(float value){return !float.IsNaN(value)&&!float.IsInfinity(value);}
        static void Coordinate(float value)
        {if(!Finite(value)||value<-32768||value>32767)throw new InvalidOperationException("Координата объекта выходит за допустимые границы.");}
        public static int Move(MapDocument map,IEnumerable<int> indices,float x,float y,float z,Func<int,bool> hangsByType=null)
        {
            var targets=Targets(map,indices);
            if(!Finite(x)||!Finite(y)||!Finite(z))throw new InvalidOperationException("Некорректное смещение объекта.");
            if(map.format==MapFormat.Doom&&z!=0)throw new InvalidOperationException("Формат Doom не хранит высоту объекта.");
            var changes=new List<Position>();
            foreach(int index in targets)
            {
                var thing=map.things[index];float nz=thing.z+z*(z!=0&&hangsByType!=null&&hangsByType(thing.type)?-1:1),nx=thing.x+x,ny=thing.y+y;
                Coordinate(nx);Coordinate(ny);Coordinate(nz);
                if(nx!=thing.x||ny!=thing.y||nz!=thing.z)changes.Add(new Position{index=index,x=nx,y=ny,z=nz});
            }
            foreach(var p in changes){var t=map.things[p.index];t.x=p.x;t.y=p.y;t.z=p.z;}return changes.Count;
        }
        public static int Rotate(MapDocument map,IEnumerable<int> indices,float amount)
        {
            var targets=Targets(map,indices);if(!Finite(amount))throw new InvalidOperationException("Некорректный угол.");
            var angles=new List<float>();foreach(int index in targets){float angle=map.things[index].angle;if(!Finite(angle))throw new InvalidOperationException("Некорректный угол объекта.");angles.Add(amount%360==0?angle:((angle+amount)%360+360)%360);}
            int changed=0;for(int i=0;i<targets.Count;i++)if(map.things[targets[i]].angle!=angles[i]){map.things[targets[i]].angle=angles[i];changed++;}return changed;
        }
        public static int Type(MapDocument map,IEnumerable<int> indices,int type)
        {
            var targets=Targets(map,indices);if(type<0||type>65535)throw new InvalidOperationException("Тип объекта за пределами Doom / Hexen.");
            int changed=0;foreach(int index in targets)if(map.things[index].type!=type){map.things[index].type=type;changed++;}return changed;
        }
        public static int Delete(MapDocument map,IEnumerable<int> indices)
        {
            var targets=Targets(map,indices);targets.Sort();for(int i=targets.Count-1;i>=0;i--)map.things.RemoveAt(targets[i]);return targets.Count;
        }
    }
}
