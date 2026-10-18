using System;

namespace MobileUDB.Core
{
    // First release: binary Doom/Hexen maps, including Boom extensions.
    // MOBILEUDB_UDMF is intentionally undefined. Deferred code is kept in
    // conditional blocks so it can be restored without deleting the work.
    public static class MapFormats
    {
        public static bool IsUniversal(MapFormat format)
        {
#if MOBILEUDB_UDMF
            return format == MapFormat.Udmf;
#else
            return false;
#endif
        }
        public static void RequireSupported(MapFormat format)
        {
            if (format != MapFormat.Doom && format != MapFormat.Hexen && !IsUniversal(format))
                throw new NotSupportedException("Первая версия поддерживает только бинарные карты Doom / Hexen (включая Boom).");
        }
    }
}
