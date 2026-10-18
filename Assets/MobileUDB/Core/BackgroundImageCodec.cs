// Bounded portable decoding for UDB file backgrounds. RGBA rows are top-down.
using System;using System.IO;using System.Text;
namespace MobileUDB.Core
{
    public sealed class BackgroundImageInfo
    {public readonly string format;public readonly int width,height;public BackgroundImageInfo(string format,int width,int height){this.format=format;this.width=width;this.height=height;}}
    public static class BackgroundImageCodec
    {
        static void Need(byte[] data,int at,long count){if(at<0||count<0||at+count>data.Length)throw new InvalidDataException("Повреждено изображение фона.");}
        static ushort U16(byte[] data,int at){Need(data,at,2);return (ushort)(data[at]|data[at+1]<<8);}
        static uint U32(byte[] data,int at){Need(data,at,4);return (uint)(data[at]|data[at+1]<<8|data[at+2]<<16|data[at+3]<<24);}
        static int Big16(byte[] data,int at){Need(data,at,2);return data[at]<<8|data[at+1];}
        static int Big32(byte[] data,int at){Need(data,at,4);return checked((int)((uint)data[at]<<24|(uint)data[at+1]<<16|(uint)data[at+2]<<8|data[at+3]));}
        static BackgroundImageInfo Info(string format,int width,int height)
        {if(width<1||height<1||width>4096||height>4096||(long)width*height>4*1024*1024)throw new InvalidDataException("Фон: не больше 4096 по стороне и 4 млн пикселей.");return new BackgroundImageInfo(format,width,height);}
        public static BackgroundImageInfo Describe(byte[] data)
        {
            if(data==null||data.Length==0||data.Length>MapBackground.MaximumImageBytes)throw new InvalidDataException("Файл фона пуст или превышает 8 МиБ.");
            if(data.Length>=24&&data[0]==137&&data[1]==80&&data[2]==78&&data[3]==71&&data[4]==13&&data[5]==10&&data[6]==26&&data[7]==10&&Encoding.ASCII.GetString(data,12,4)=="IHDR")return Info("PNG",Big32(data,16),Big32(data,20));
            if(data.Length>=10&&(Encoding.ASCII.GetString(data,0,6)=="GIF87a"||Encoding.ASCII.GetString(data,0,6)=="GIF89a"))return Info("GIF",U16(data,6),U16(data,8));
            if(data.Length>=26&&data[0]=='B'&&data[1]=='M')
            {uint header=U32(data,14);if(header==12)return Info("BMP",U16(data,18),U16(data,20));if(header<40)throw new NotSupportedException("Неизвестный заголовок BMP.");Need(data,14,header);int width=unchecked((int)U32(data,18)),height=unchecked((int)U32(data,22));if(height==int.MinValue)throw new InvalidDataException("Некорректная высота BMP.");return Info("BMP",width,Math.Abs(height));}
            if(data.Length>=4&&data[0]==255&&data[1]==216)
            {
                int at=2;while(at<data.Length)
                {if(data[at++]!=255)throw new InvalidDataException("Повреждён JPEG.");while(at<data.Length&&data[at]==255)at++;Need(data,at,1);int marker=data[at++];if(marker==217||marker==218)break;if(marker==1||marker>=208&&marker<=216)continue;int count=Big16(data,at);if(count<2)throw new InvalidDataException("Повреждён JPEG.");Need(data,at,count);if(marker>=192&&marker<=195||marker>=197&&marker<=199||marker>=201&&marker<=203||marker>=205&&marker<=207){if(count<8)throw new InvalidDataException("Повреждён JPEG SOF.");return Info("JPEG",Big16(data,at+5),Big16(data,at+3));}at+=count;}
                throw new InvalidDataException("В JPEG не найден размер изображения.");
            }
            throw new NotSupportedException("Поддерживаются PNG, JPEG, BMP и GIF.");
        }
        public static ResourceImage Decode(byte[] data,Func<byte[],ResourceImage> pngJpegDecoder=null)
        {
            var info=Describe(data);ResourceImage image;
            if(info.format=="BMP")image=Bmp(data,info);else if(info.format=="GIF")image=Gif(data,info);else
            {if(pngJpegDecoder==null)throw new NotSupportedException("Для PNG/JPEG нужен декодер Unity.");image=pngJpegDecoder(data);}
            if(image==null||image.width!=info.width||image.height!=info.height||image.rgba==null||image.rgba.Length!=info.width*info.height*4)throw new InvalidDataException("Размер декодированного фона не совпадает с заголовком.");return image;
        }
        static void Pixel(byte[] pixels,int at,byte r,byte g,byte b,byte alpha=255){pixels[at]=r;pixels[at+1]=g;pixels[at+2]=b;pixels[at+3]=alpha;}
        static byte Channel(uint pixel,uint mask)
        {if(mask==0)return 255;int shift=0;while((mask&1)==0){mask>>=1;shift++;}return (byte)(((ulong)(pixel>>shift&mask)*255+mask/2)/mask);}
        static ResourceImage Bmp(byte[] data,BackgroundImageInfo info)
        {
            int header=checked((int)U32(data,14)),offset=checked((int)U32(data,10)),bits=U16(data,header==12?24:28),planes=U16(data,header==12?22:26);uint compression=header==12?0:U32(data,30);bool top=header!=12&&unchecked((int)U32(data,22))<0;
            if(planes!=1||bits!=1&&bits!=4&&bits!=8&&bits!=16&&bits!=24&&bits!=32||compression!=0&&compression!=3&&compression!=6||compression!=0&&bits!=16&&bits!=32)throw new NotSupportedException("Этот вариант сжатого BMP пока не поддержан.");
            int stride=checked(((info.width*bits+31)/32)*4);Need(data,offset,(long)stride*info.height);if(offset<14+header)throw new InvalidDataException("Повреждён BMP.");
            uint red=bits==16?0x7c00u:0xff0000u,green=bits==16?0x3e0u:0xff00u,blue=bits==16?0x1fu:0xffu,alpha=0;
            if(compression==3||compression==6)
            {int masks=14+40;Need(data,masks,compression==6||header>=56?16:12);red=U32(data,masks);green=U32(data,masks+4);blue=U32(data,masks+8);if(compression==6||header>=56)alpha=U32(data,masks+12);if(red==0||green==0||blue==0||(red&green)!=0||(red&blue)!=0||(green&blue)!=0)throw new InvalidDataException("Некорректные маски BMP.");if(header==40&&offset<masks+(compression==6?16:12))throw new InvalidDataException("Повреждён BMP.");}
            int palette=14+header,paletteSize=header==12?3:4,colors=bits<=8?(header==12?1<<bits:checked((int)U32(data,46))):0;if(bits<=8&&colors==0)colors=1<<bits;if(bits<=8){if(colors>1<<bits)throw new InvalidDataException("Некорректная палитра BMP.");Need(data,palette,(long)colors*paletteSize);if(offset<palette+colors*paletteSize)throw new InvalidDataException("Повреждён BMP.");}
            var pixels=new byte[info.width*info.height*4];
            for(int y=0;y<info.height;y++)for(int x=0;x<info.width;x++)
            {
                int row=offset+(top?y:info.height-1-y)*stride,at=(y*info.width+x)*4;
                if(bits<=8){int index=bits==8?data[row+x]:bits==4?(data[row+x/2]>>(x%2==0?4:0))&15:(data[row+x/8]>>(7-x%8))&1;if(index>=colors)throw new InvalidDataException("Некорректный индекс BMP.");int color=palette+index*paletteSize;Pixel(pixels,at,data[color+2],data[color+1],data[color]);}
                else if(bits==24){int color=row+x*3;Pixel(pixels,at,data[color+2],data[color+1],data[color]);}
                else{uint value=bits==16?U16(data,row+x*2):U32(data,row+x*4);Pixel(pixels,at,Channel(value,red),Channel(value,green),Channel(value,blue),Channel(value,alpha));}
            }
            return new ResourceImage(info.width,info.height,pixels);
        }
        static byte[] Palette(byte[] data,ref int at,int count){Need(data,at,count*3);var colors=new byte[count*3];Buffer.BlockCopy(data,at,colors,0,colors.Length);at+=colors.Length;return colors;}
        static byte[] Blocks(byte[] data,ref int at)
        {using(var bytes=new MemoryStream()){while(true){Need(data,at,1);int count=data[at++];if(count==0)return bytes.ToArray();Need(data,at,count);bytes.Write(data,at,count);at+=count;}}}
        static ResourceImage Gif(byte[] data,BackgroundImageInfo info)
        {
            Need(data,0,13);int at=13,flags=data[10],transparent=-1;byte[] global=(flags&128)!=0?Palette(data,ref at,1<<((flags&7)+1)):null;
            while(at<data.Length)
            {
                int marker=data[at++];if(marker==59)break;
                if(marker==33){Need(data,at,1);int type=data[at++];if(type==249){Need(data,at,6);if(data[at]!=4||data[at+5]!=0)throw new InvalidDataException("Повреждён GIF.");transparent=(data[at+1]&1)!=0?data[at+4]:-1;at+=6;}else Blocks(data,ref at);continue;}
                if(marker!=44)throw new InvalidDataException("Повреждён GIF.");Need(data,at,9);int left=U16(data,at),top=U16(data,at+2),width=U16(data,at+4),height=U16(data,at+6),packed=data[at+8];at+=9;if(width<1||height<1||left+width>info.width||top+height>info.height)throw new InvalidDataException("Размер кадра GIF не совпадает с холстом.");var colors=(packed&128)!=0?Palette(data,ref at,1<<((packed&7)+1)):global;if(colors==null)throw new InvalidDataException("Нет палитры GIF.");Need(data,at,1);int minimum=data[at++];if(minimum<2||minimum>8)throw new InvalidDataException("Некорректный код GIF.");var indices=Lzw(Blocks(data,ref at),minimum,width*height);var pixels=new byte[info.width*info.height*4];
                if(global!=null&&data[11]!=transparent&&data[11]<global.Length/3){int background=data[11]*3;for(int pixel=0;pixel<pixels.Length;pixel+=4)Pixel(pixels,pixel,global[background],global[background+1],global[background+2]);}
                int sourceRow=0;Action<int> row=y=>{for(int x=0;x<width;x++){int index=indices[sourceRow*width+x];if(index>=colors.Length/3)throw new InvalidDataException("Некорректный индекс GIF.");if(index!=transparent)Pixel(pixels,((top+y)*info.width+left+x)*4,colors[index*3],colors[index*3+1],colors[index*3+2]);}sourceRow++;};
                if((packed&64)!=0){int[] starts={0,4,2,1},steps={8,8,4,2};for(int pass=0;pass<4;pass++)for(int y=starts[pass];y<height;y+=steps[pass])row(y);}else for(int y=0;y<height;y++)row(y);
                return new ResourceImage(info.width,info.height,pixels); // Static first frame, as the desktop background.
            }
            throw new InvalidDataException("В GIF нет кадра.");
        }
        static byte[] Lzw(byte[] input,int minimum,int count)
        {
            int clear=1<<minimum,end=clear+1,next=end+1,size=minimum+1,bit=0,previous=-1,first=0,used=0;var prefix=new int[4096];var suffix=new byte[4096];var stack=new byte[4097];var output=new byte[count];
            for(int i=0;i<clear;i++)suffix[i]=(byte)i;
            while(bit+size<=input.Length*8)
            {
                int code=0;for(int i=0;i<size;i++)code|=((input[(bit+i)/8]>>((bit+i)%8))&1)<<i;bit+=size;
                if(code==clear){next=end+1;size=minimum+1;previous=-1;continue;}if(code==end){if(used!=count)throw new InvalidDataException("GIF содержит неполный кадр.");return output;}int original=code,depth=0;
                if(previous<0){if(code>=clear||used>=count)throw new InvalidDataException("Некорректный поток GIF.");output[used++]=suffix[code];first=suffix[code];previous=code;continue;}
                if(code==next){stack[depth++]=(byte)first;code=previous;}else if(code>next)throw new InvalidDataException("Некорректный поток GIF.");
                while(code>=clear){if(code>=next||depth>=4096)throw new InvalidDataException("Некорректный словарь GIF.");stack[depth++]=suffix[code];code=prefix[code];}
                first=suffix[code];stack[depth++]=(byte)first;while(depth>0){if(used>=count)throw new InvalidDataException("GIF содержит лишние пиксели.");output[used++]=stack[--depth];}
                if(next<4096){prefix[next]=previous;suffix[next++]=(byte)first;if(next==1<<size&&size<12)size++;}previous=original;
            }
            throw new InvalidDataException("Оборван поток GIF.");
        }
    }
}
