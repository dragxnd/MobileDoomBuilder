// GPL-3.0. Read-only ACS directory metadata; this does not execute or compile ACS.
// Layout reference: ZDoom/gzdoom src/playsim/p_acs.cpp (Init/LoadScriptsDirectory)
// and p_acs.h (ScriptPtr1/2/3). Only numeric IDs addressable by binary args are returned.
using System;
using System.Collections.Generic;

namespace MobileUDB.Core
{
    public static class HexenScriptDirectory
    {
        static uint UInt(byte[] data,int offset)
        {return (uint)(data[offset]|data[offset+1]<<8|data[offset+2]<<16|data[offset+3]<<24);}
        static bool Tag(byte[] data,int offset,char last)
        {return data[offset]=='A'&&data[offset+1]=='C'&&data[offset+2]=='S'&&data[offset+3]==last;}
        static bool Range(long offset,long length,long end){return offset>=8&&length>=0&&offset<=end&&length<=end-offset;}
        static bool Fail(out int[] numbers,out string reason,string message)
        {numbers=new int[0];reason=message;return false;}
        public static bool Read(MapDocument map,out int[] numbers,out string reason)
        {
            if(map==null||map.format!=MapFormat.Hexen)return Fail(out numbers,out reason,"Выбор ACS доступен в формате Hexen.");
            MapAuxiliaryLump behavior=null;
            if(map.auxiliary!=null)foreach(var lump in map.auxiliary)
                if(lump!=null&&string.Equals(lump.name,"BEHAVIOR",StringComparison.OrdinalIgnoreCase))
                {if(behavior!=null)return Fail(out numbers,out reason,"Несколько BEHAVIOR: список скриптов неоднозначен.");behavior=lump;}
            if(behavior==null)return Fail(out numbers,out reason,"В карте нет BEHAVIOR. Введите номер скрипта вручную.");
            return Read(behavior.data,out numbers,out reason);
        }
        public static bool Read(byte[] data,out int[] numbers,out string reason)
        {
            if(data==null||data.Length<8||!Tag(data,0,(char)data[3])||(data[3]!=0&&data[3]!='E'&&data[3]!='e'))
                return Fail(out numbers,out reason,"Неизвестный заголовок BEHAVIOR. Введите номер вручную.");
            long directory=UInt(data,4),end=data.Length;
            bool classic=data[3]==0,compact=false;
            long records=0,count=0,codeEnd=0;int stride=12;
            if(classic)
            {
                if(!Range(directory,4,end))return Fail(out numbers,out reason,"Повреждён каталог BEHAVIOR.");
                // Redesigned enhanced objects retain ACS\0 and a compatibility directory.
                if(directory>=24&&(Tag(data,(int)directory-4,'E')||Tag(data,(int)directory-4,'e')))
                {classic=false;compact=true;end=directory-8;directory=UInt(data,(int)directory-8);}
            }
            if(classic)
            {
                count=UInt(data,(int)directory);records=directory+4;codeEnd=directory;
                if(count>65536||!Range(records,count*12,end))return Fail(out numbers,out reason,"Повреждён каталог скриптов BEHAVIOR.");
            }
            else
            {
                if(!Range(directory,0,end))return Fail(out numbers,out reason,"Повреждён каталог блоков BEHAVIOR.");
                codeEnd=directory;long cursor=directory;int chunks=0;bool found=false;stride=compact?8:12;
                while(cursor<end)
                {
                    if(++chunks>65536||!Range(cursor,8,end))return Fail(out numbers,out reason,"Повреждён блок BEHAVIOR.");
                    int at=(int)cursor;long size=UInt(data,at+4);
                    if(!Range(cursor+8,size,end))return Fail(out numbers,out reason,"Повреждён размер блока BEHAVIOR.");
                    if(!found&&data[at]=='S'&&data[at+1]=='P'&&data[at+2]=='T'&&data[at+3]=='R')
                    {
                        if(size%stride!=0||size/stride>65536)return Fail(out numbers,out reason,"Повреждён блок скриптов BEHAVIOR.");
                        found=true;records=cursor+8;count=size/stride;
                    }
                    cursor+=8+size;
                }
            }
            var ids=new SortedSet<int>();
            for(long i=0;i<count;i++)
            {
                int at=(int)(records+i*stride);
                int number=classic?(int)(UInt(data,at)%1000):(short)(data[at]|data[at+1]<<8);
                long address=UInt(data,at+4);
                if(!Range(address,1,codeEnd))return Fail(out numbers,out reason,"Адрес скрипта вне кода BEHAVIOR.");
                // Negative enhanced IDs name scripts; numbers >255 cannot be stored in arg0.
                if(number>=0&&number<=255)ids.Add(number);
            }
            numbers=new int[ids.Count];ids.CopyTo(numbers);reason=null;return true;
        }
    }
}
