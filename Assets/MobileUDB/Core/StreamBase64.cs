using System;
using System.IO;

namespace MobileUDB.Core
{
    internal static class StreamBase64
    {
        sealed class State { public Stream input;public long length;public Func<bool> cancelled; }
        internal static string Read(Stream input,Func<bool> cancelled=null)
        {
            if(input==null||!input.CanRead||!input.CanSeek)throw new InvalidDataException("Требуется читаемый поток ресурсов.");
            if(cancelled!=null&&cancelled())throw new OperationCanceledException();
            long length=input.Length,encoded=checked((length+2)/3*4);if(encoded>int.MaxValue)throw new InvalidDataException("Ресурс слишком велик для проекта.");
            input.Position=0;var state=new State{input=input,length=length,cancelled=cancelled};
            // Fill the final portable JSON payload directly. A full byte[] or
            // StringBuilder/char[] copy would defeat the file-import memory gain.
            return string.Create((int)encoded,state,(output,source)=>
            {
                var buffer=new byte[48*1024];long remaining=source.length;int written=0;
                while(remaining>0)
                {
                    if(source.cancelled!=null&&source.cancelled())throw new OperationCanceledException();
                    int wanted=(int)Math.Min(buffer.Length,remaining),read=0;
                    while(read<wanted)
                    {
                        if(source.cancelled!=null&&source.cancelled())throw new OperationCanceledException();
                        int got=source.input.Read(buffer,read,wanted-read);if(got==0)throw new InvalidDataException("Файл ресурса изменился во время чтения.");read+=got;
                    }
                    int count;if(!Convert.TryToBase64Chars(new ReadOnlySpan<byte>(buffer,0,read),output.Slice(written),out count))throw new InvalidDataException("Не удалось подготовить ресурс проекта.");
                    written+=count;remaining-=read;
                }
                if(written!=output.Length||source.input.ReadByte()!=-1)throw new InvalidDataException("Файл ресурса изменился во время чтения.");
            });
        }
    }
}
