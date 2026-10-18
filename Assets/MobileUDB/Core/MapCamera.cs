// GPL-3.0. Project-local editor camera; coordinates use Doom map units.
using System;
namespace MobileUDB.Core
{
    [Serializable] public sealed class MapCamera
    {
        public float x,y,z,yaw,pitch;
        // UDB VisualMode.MoveCameraToCursor keeps a quarter of the distance.
        public MapCamera Approach(float targetX,float targetY,float targetZ)
        {
            var result=Validated();
            result.x=(float)(targetX+((double)x-targetX)*.25);
            result.y=(float)(targetY+((double)y-targetY)*.25);
            result.z=(float)(targetZ+((double)z-targetZ)*.25);
            return result.Validated();
        }
        public MapCamera Validated()
        {
            if(!Finite(x)||!Finite(y)||!Finite(z)||!Finite(yaw)||!Finite(pitch)||pitch < -85||pitch > 85)
                throw new InvalidOperationException("Позиция и направление камеры должны быть конечными; наклон — от −85 до 85°.");
            return (MapCamera)MemberwiseClone();
        }
        static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
        public static bool Same(MapCamera a,MapCamera b)=>a!=null&&b!=null&&a.x==b.x&&a.y==b.y&&a.z==b.z&&a.yaw==b.yaw&&a.pitch==b.pitch;
    }

    // Common.cfg start3dmode and VisualCamera PositionAtThing / ApplyToThing.
    public static class MapCameraMarker
    {
        public const int Type=32000;
        public static int Find(MapDocument map)
        {for(int i=0;i<map.things.Count;i++)if(map.things[i].type==Type)return i;return -1;}
        static void Binary(MapDocument map)
        {if(map.format!=MapFormat.Doom&&map.format!=MapFormat.Hexen)throw new InvalidOperationException("Камера WAD доступна для бинарных карт Doom / Hexen.");}
        public static bool TryRead(MapDocument map,out MapCamera camera)
        {
            Binary(map);camera=null;int index=Find(map);if(index<0)return false;
            var thing=map.things[index];int sector=ThingPlacement.Sector(map,thing.Position);
            camera=new MapCamera{x=thing.x,y=thing.y,z=(sector<0?0:map.sectors[sector].floor)+thing.z+41,yaw=(float)((90.0-thing.angle)%360),pitch=0}.Validated();return true;
        }
        static int Coordinate(double value)
        {
            value=Math.Truncate(value);
            if(double.IsNaN(value)||double.IsInfinity(value)||value<short.MinValue||value>short.MaxValue)
                throw new InvalidOperationException("Позиция камеры выходит за границы бинарного WAD (−32768…32767).");
            return (int)value;
        }
        static float TemporaryHeight(double value)
        {
            value=Math.Truncate(value);
            if(double.IsNaN(value)||double.IsInfinity(value)||value<int.MinValue||value>int.MaxValue)
                throw new InvalidOperationException("Высота камеры слишком велика для служебного объекта UDB.");
            return (float)value;
        }
        static MapThing Values(MapDocument map,MapCamera camera)
        {
            Binary(map);camera=camera.Validated();int sector=ThingPlacement.Sector(map,new MapPoint(camera.x,camera.y));
            double floor=sector<0?0:map.sectors[sector].floor;
            double angle=((90.0-camera.yaw)%360+360)%360;
            return new MapThing{type=Type,flags=0,x=Coordinate(camera.x),y=Coordinate(camera.y),
                z=map.format==MapFormat.Doom?TemporaryHeight(Math.Truncate(camera.z)-floor-41):Coordinate(Math.Truncate(camera.z)-floor-41),angle=(float)Math.Round(angle)};
        }
        static bool Same(MapThing a,MapThing b)=>a!=null&&a.x==b.x&&a.y==b.y&&a.z==b.z&&a.angle==b.angle;
        public static bool NeedsSave(MapDocument map,MapCamera camera)
        {var wanted=Values(map,camera);int index=Find(map);return index<0||!Same(map.things[index],wanted);}
        public static bool Save(MapDocument map,MapCamera camera)
        {
            var wanted=Values(map,camera);int index=Find(map);
            if(index<0){map.things.Add(wanted);return true;}
            var thing=map.things[index];if(Same(thing,wanted))return false;
            thing.x=wanted.x;thing.y=wanted.y;thing.z=wanted.z;thing.angle=wanted.angle;return true;
        }
        // Native ApplyToThing updates only an existing camera. Doom can retain
        // its temporary Z in a project, although the WAD record omits that field.
        public static bool Synchronize(MapDocument map,MapCamera camera)
        {
            Binary(map);int index=Find(map);if(index<0)return false;
            var wanted=Values(map,camera);var thing=map.things[index];if(Same(thing,wanted))return false;
            thing.x=wanted.x;thing.y=wanted.y;thing.z=wanted.z;thing.angle=wanted.angle;return true;
        }
        static MapThing Placement(MapDocument map,MapPoint point)
        {
            Binary(map);int x=Coordinate(point.x),y=Coordinate(point.y),index=Find(map);
            float z=index<0?0:map.things[index].z;if(z<0)z=0;
            int sector=ThingPlacement.Sector(map,new MapPoint(x,y));
            if(sector>=0&&z+50>map.sectors[sector].ceiling-map.sectors[sector].floor)
                z=map.sectors[sector].ceiling-map.sectors[sector].floor-50;
            return new MapThing{type=Type,flags=0,x=x,y=y,z=map.format==MapFormat.Doom?TemporaryHeight(z):Coordinate(z)};
        }
        public static bool NeedsPlacement(MapDocument map,MapPoint point)
        {
            var wanted=Placement(map,point);int first=Find(map);if(first<0)return true;
            var thing=map.things[first];if(thing.x!=wanted.x||thing.y!=wanted.y||thing.z!=wanted.z)return true;
            for(int i=first+1;i<map.things.Count;i++)if(map.things[i].type==Type)return true;return false;
        }
        // BaseClassicMode.PlaceVisualStartThing: first marker keeps its properties;
        // later markers are removed. Low ceilings may produce a negative Z offset.
        public static int Place(MapDocument map,MapPoint point)
        {
            var wanted=Placement(map,point);int first=Find(map);
            if(first<0){map.things.Add(wanted);return map.things.Count-1;}
            var thing=map.things[first];thing.x=wanted.x;thing.y=wanted.y;thing.z=wanted.z;
            for(int i=map.things.Count-1;i>first;i--)if(map.things[i].type==Type)map.things.RemoveAt(i);
            return first;
        }
    }
}
