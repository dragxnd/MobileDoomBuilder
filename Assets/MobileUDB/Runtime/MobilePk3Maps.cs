using System;
using System.Collections.Generic;
using System.IO;
using MobileUDB.Core;

namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        MobileResourceArchive sourceMapPackage;
        static MobileResourceArchive FindSourcePackage(string id,List<MobileResourceArchive> descriptors,List<ResourceArchive> sources)
        {
            if (string.IsNullOrEmpty(id)) return null;
            MobileResourceArchive found = null;
            for (int i = 0; i < descriptors.Count; i++) if (descriptors[i].id == id)
            {
                if (found != null || !sources[i].isPk3) throw new InvalidDataException("Недопустимая ссылка на исходный PK3.");
                found = descriptors[i];
            }
            if (found == null) throw new InvalidDataException("Исходный PK3 отсутствует среди ресурсов проекта.");
            return found;
        }
    }
}
