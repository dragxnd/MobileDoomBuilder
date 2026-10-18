// Mobile integration of ZDBSP, GPL-2.0-or-later (upstream/COPYING).
#include "zdbsp.h"
#include "processor.h"
#include "sc_man.h"
#include <mutex>
#include <cmath>
#include <cstdarg>
#include <cstdio>
#include <cstring>
#include <string>
#include <algorithm>

#ifdef _WIN32
#define MOBILE_EXPORT extern "C" __declspec(dllexport)
#else
#define MOBILE_EXPORT extern "C" __attribute__((visibility("default")))
#endif

const char *Map = NULL, *InName = NULL, *OutName = NULL;
bool BuildNodes = true, BuildGLNodes = false, ConformNodes = false, GLOnly = false, WriteComments = false;
bool NoPrune = true, CheckPolyobjs = true, ShowMap = false;
bool CompressNodes = false, CompressGLNodes = false, ForceCompression = false, V5GLNodes = false;
bool HaveSSE1 = false, HaveSSE2 = false;
int SSELevel = 0, MaxSegs = 64, SplitCost = 8, AAPreference = 16;
EBlockmapMode BlockmapMode = EBM_Rebuild;
ERejectMode RejectMode = ERM_CreateZeroes;
static std::mutex buildMutex;
static std::string warnings;

angle_t PointToAngle(fixed_t x,fixed_t y)
{
    double bam = atan2(double(y),double(x)) * (double(1 << 30) / 3.14159265358979323846);
    return angle_t(int(bam)) << 1;
}
void Warn(const char *format,...)
{
    if(warnings.size() >= 4096) return;
    char text[1024]; va_list args; va_start(args,format); vsnprintf(text,sizeof(text),format,args); va_end(args);
    warnings.append(text, std::min(strlen(text),size_t(4096 - warnings.size())));
}
static void Message(char *output,int capacity,const char *value)
{
    if(output != NULL && capacity > 0) { size_t count = std::min(strlen(value),size_t(capacity - 1)); memcpy(output,value,count); output[count] = 0; }
}
MOBILE_EXPORT int mobileudb_nodes_version() { return 1; }
// Input must be a validated, newly exported map WAD. Paths are UTF-8, distinct,
// app-owned temporary files. Upstream globals and scanner require serialization.
MOBILE_EXPORT int mobileudb_nodes_build(const char *input,const char *output,char *message,int capacity)
{
    std::lock_guard<std::mutex> lock(buildMutex); warnings.clear(); Message(message,capacity,"");
    if(input == NULL || output == NULL || strcmp(input,output) == 0) { Message(message,capacity,"Input and output paths must differ."); return -1; }
    try
    {
        FWadReader source(input); FWadWriter result(output,false); int maps = 0;
        for(int lump = 0; lump < source.NumLumps();)
        {
            if(source.IsMap(lump))
            {
                // UDMF processing changes these globals; reset for each map and call.
                BuildNodes = true; BuildGLNodes = false; ConformNodes = false; GLOnly = false;
                CompressNodes = false; CompressGLNodes = false; ForceCompression = false; V5GLNodes = false;
                FProcessor builder(source,lump); builder.Write(result); maps++; lump = source.LumpAfterMap(lump);
            }
            else if(source.IsGLNodes(lump)) lump = source.SkipGLNodes(lump);
            else { result.CopyLump(source,lump); lump++; }
        }
        if(maps == 0) throw std::runtime_error("No maps found.");
        result.Close(); SC_Close(); Message(message,capacity,warnings.c_str()); return maps;
    }
    catch(const std::exception &error) { SC_Close(); Message(message,capacity,error.what()); return -1; }
    catch(...) { SC_Close(); Message(message,capacity,"Unexpected node builder error."); return -1; }
}
