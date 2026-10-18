// GPL-3.0. MapOptions viewpositionx/viewpositiony/viewscale settings.
using System;
namespace MobileUDB.Core
{
    [Serializable] public sealed class MapViewFrame {public string name;public MapView view;}
    [Serializable] public sealed class MapView
    {
        public float x,y,scale;
        public MapCamera camera;
        public MapView Copy(){var copy=(MapView)MemberwiseClone();copy.camera=camera==null?null:camera.Validated();return copy;}
        public MapView Validated()
        {
            if(!Finite(x)||!Finite(y)||!Finite(scale)||scale<=0)throw new InvalidOperationException("Центр обзора должен быть конечным; масштаб — конечным положительным числом.");
            return Copy();
        }
        static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
        public static bool Same(MapView a,MapView b)=>a!=null&&b!=null&&a.x==b.x&&a.y==b.y&&a.scale==b.scale;
    }
}
