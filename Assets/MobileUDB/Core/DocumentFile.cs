using System;
using System.IO;

namespace MobileUDB.Core
{
    public static class DocumentFile
    {
        public static byte[] Read(string path,long maximumBytes,Func<bool> cancelled,Action<long,long> progress = null)
        {
            if (maximumBytes < 0 || maximumBytes > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
            CheckCancelled(cancelled);
            using (var stream = new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))
            {
                long length = stream.Length;
                if (length > maximumBytes) throw new InvalidDataException("Файл слишком большой для выбранного типа импорта.");
                var bytes = new byte[(int)length]; int offset = 0;
                if (progress != null) progress(0,length);
                while (offset < bytes.Length)
                {
                    CheckCancelled(cancelled);
                    int count = stream.Read(bytes,offset,Math.Min(65536,bytes.Length - offset));
                    if (count == 0) throw new EndOfStreamException("Файл изменился во время чтения.");
                    offset += count; if (progress != null) progress(offset,length);
                }
                CheckCancelled(cancelled);
                if (stream.ReadByte() != -1) throw new InvalidDataException("Файл изменился во время чтения.");
                return bytes;
            }
        }
        static void CheckCancelled(Func<bool> cancelled) { if (cancelled != null && cancelled()) throw new OperationCanceledException(); }
    }
}
