// GPL-3.0. GridSetup background fields and Renderer2D.SetupBackground UVs.
using System;using System.IO;
namespace MobileUDB.Core
{
    [Serializable] public sealed class MapBackgroundFrame {public string name;public MapBackground background;}
    [Serializable] public sealed class MapBackground
    {
        public const int Texture=0,Flat=1,File=2,MaximumImageBytes=8*1024*1024,MaximumPortableBytes=32*1024*1024;
        public bool visible;public string name="",fileData="";
        public int source,offsetX,offsetY,scaleX=100,scaleY=100,opacity=100;
        public MapBackground Copy(){return (MapBackground)MemberwiseClone();}
        public MapBackground Validated()
        {
            if(source<Texture||source>File||scaleX<0||scaleY<0||opacity<0||opacity>100)throw new InvalidDataException("Фон: источник 0–2, масштаб неотрицательный, видимость 0–100%.");
            if(name==null||name.Length>1024||(visible&&name.Length==0))throw new InvalidDataException("Укажите изображение фона.");
            if(fileData!=null&&fileData.Length>MaximumImageBytes*4L/3+4)throw new InvalidDataException("Изображение фона превышает 8 МиБ.");
            if(source!=File&&!string.IsNullOrEmpty(fileData))throw new InvalidDataException("Данные файла не относятся к ресурсному фону.");
            return Copy();
        }
        public byte[] Attachment()
        {
            if(source!=File||string.IsNullOrEmpty(fileData))return null;
            Validated();byte[] bytes=Convert.FromBase64String(fileData);BackgroundImageCodec.Describe(bytes);return bytes;
        }
        // Native screen UV is u=(x-offsetX)/ScaledWidth, v=-(y-offsetY)/ScaledHeight.
        // RGBA is uploaded to Unity with flipped rows, so Unity v=1-nativeV.
        public MapPoint UV(MapPoint point,double scaledWidth,double scaledHeight)
        {
            double width=scaledWidth*(scaleX/100.0),height=scaledHeight*(scaleY/100.0);
            if(!Finite(width)||!Finite(height)||width<=0||height<=0)throw new InvalidDataException("Некорректный размер фона.");
            return new MapPoint((float)((point.x-offsetX)/width),(float)(1+(point.y-offsetY)/height));
        }
        static bool Finite(double value)=>!double.IsNaN(value)&&!double.IsInfinity(value);
    }
}
