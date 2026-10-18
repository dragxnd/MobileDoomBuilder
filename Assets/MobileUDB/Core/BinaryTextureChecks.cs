// GPL-3.0. Binary branches of UDB CheckMissingTextures / CheckUnusedTextures,
// BaseCheckTextures and ActionFloor*Textures. No UDMF slope calculation.
using System;
using System.Collections.Generic;

namespace MobileUDB.Core
{
    public sealed class UnusedTextureTarget
    {
        public readonly int line,side;public readonly bool front,upper;public readonly string name;
        public UnusedTextureTarget(int line,int side,bool front,bool upper,string name)
        {this.line=line;this.side=side;this.front=front;this.upper=upper;this.name=name;}
    }
    public static class BinaryTextureChecks
    {
        const int Upper=1,Lower=2,Inside=4;
        static bool Empty(string name)=>string.IsNullOrEmpty(name)||name=="-";
        static int Arg(int[] args,int index)=>args!=null&&index<args.Length?args[index]:0;
        static bool Id(MapActionType action,string name)=>action!=null&&string.Equals(action.identifier,name,StringComparison.OrdinalIgnoreCase);
        static void Tags(MapFormat format,MapActionType action,int tag,int[] args,HashSet<int> lower,HashSet<int> next,HashSet<int> highest)
        {
            if(action==null||action.id<=0)return;
            if(format==MapFormat.Hexen)tag=action.arguments[0].type==13?Arg(args,0):0;
            if(tag<=0)return;
            if(action.floorLowerToLowest)lower.Add(tag);
            if(action.floorRaiseToNextHigher)next.Add(tag);
            if(action.floorRaiseToHighest)highest.Add(tag);
        }
        static bool FloorFlag(Dictionary<int,int> flags,int tag,int needed)
        {int value;return flags.TryGetValue(tag,out value)&&(value&needed)==needed;}
        // Called after structural validation by MapChecks. Null yields bound work
        // between UI frames; no changes or image decoding are performed here.
        public static IEnumerable<MapIssue> Scan(MapDocument map,ActionCatalog actions=null)
        {
            if(map.format!=MapFormat.Doom&&map.format!=MapFormat.Hexen)yield break;
            var lowerTags=new HashSet<int>();var nextTags=new HashSet<int>();var highestTags=new HashSet<int>();
            var floors=new Dictionary<int,int>();bool floorAction=actions!=null&&Id(actions.Find(160),"Sector_Set3dFloor");
            var lowest=new float[map.sectors.Count];var highest=new float[map.sectors.Count];var next=new float?[map.sectors.Count];
            for(int i=0;i<map.sectors.Count;i++){lowest[i]=highest[i]=map.sectors[i].floor;if((i&127)==0)yield return null;}
            for(int i=0;i<map.lines.Count;i++)
            {
                var line=map.lines[i];var action=actions==null?null:actions.Find(line.action);
                Tags(map.format,action,line.tag,line.args,lowerTags,nextTags,highestTags);
                if(floorAction&&line.action==160)
                {
                    int tag=Arg(line.args,0),value;floors.TryGetValue(tag,out value);
                    // Match native cache ordering: RenderInside initializes only a new tag.
                    if((Arg(line.args,1)&4)!=0&&!floors.ContainsKey(tag)){value=Inside;floors.Add(tag,value);}
                    if((Arg(line.args,2)&16)!=0){value|=Upper;floors[tag]=value;}
                    if((Arg(line.args,2)&32)!=0){value|=Lower;floors[tag]=value;}
                }
                if(line.front>=0&&line.back>=0)
                {
                    int a=map.sides[line.front].sector,b=map.sides[line.back].sector;
                    if(a!=b){Neighbour(map,a,b,lowest,highest,next);Neighbour(map,b,a,lowest,highest,next);}
                }
                if((i&127)==0)yield return null;
            }
            if(map.format==MapFormat.Hexen)for(int i=0;i<map.things.Count;i++)
            {var thing=map.things[i];if(thing!=null)Tags(map.format,actions==null?null:actions.Find(thing.action),0,thing.args,lowerTags,nextTags,highestTags);if((i&127)==0)yield return null;}
            for(int i=0;i<map.lines.Count;i++)
            {
                var line=map.lines[i];var action=actions==null?null:actions.Find(line.action);
                bool staticSky=Id(action,"static_init")&&Arg(line.args,1)==255;
                bool sky=staticSky||(action!=null&&action.requiresUpperTexture);
                bool plane=action!=null&&action.id>0&&Id(action,"plane_align");
                var a=map.vertices[line.start].Position;var b=map.vertices[line.end].Position;var position=new MapPoint((a.x+b.x)*.5f,(a.y+b.y)*.5f);
                for(int face=0;face<2;face++)
                {
                    bool front=face==0;int sideIndex=front?line.front:line.back,opposite=front?line.back:line.front;
                    if(sideIndex<0)continue;
                    var side=map.sides[sideIndex];int own=side.sector;var ownSector=map.sectors[own];
                    int other=opposite<0?-1:map.sides[opposite].sector;
                    bool high=other>=0&&!GameSky.Is(map,map.sectors[other].ceilingTexture)&&ownSector.ceiling>map.sectors[other].ceiling;
                    bool low=other>=0&&!GameSky.Is(map,map.sectors[other].floorTexture)&&ownSector.floor<map.sectors[other].floor;
                    bool futureHigh=other>=0&&(FloorFlag(floors,ownSector.tag,Upper|Inside)||FloorFlag(floors,map.sectors[other].tag,Upper));
                    bool floorLow=other>=0&&(FloorFlag(floors,ownSector.tag,Lower|Inside)||FloorFlag(floors,map.sectors[other].tag,Lower));
                    bool movingLow=other>=0&&other!=own&&ownSector.tag>0&&ownSector.tag!=map.sectors[other].tag&&lowerTags.Contains(ownSector.tag)&&map.sectors[other].floor>lowest[own];
                    if(other>=0&&other!=own&&map.sectors[other].tag>0&&map.sectors[other].tag!=ownSector.tag)
                    {
                        int tag=map.sectors[other].tag;
                        if(nextTags.Contains(tag)&&next[other].HasValue&&ownSector.floor<next[other].Value)movingLow=true;
                        if(highestTags.Contains(tag)&&ownSector.floor<highest[other])movingLow=true;
                    }
                    bool futureLow=floorLow||movingLow;
                    string prefix="Стена #"+i+(front?" · передняя: ":" · задняя: ");var element=new ElementRef(ElementKind.Line,i);
                    // Native CheckMissingTextures continues the entire sidedef when
                    // the required upper/lower is suppressed by Plane_Align.
                    bool skipMissing=Empty(side.upper)&&(high||sky)&&plane&&Arg(line.args,1)>0;
                    if(!skipMissing&&Empty(side.upper)&&(high||sky||futureHigh))yield return new MapIssue("missing-upper",prefix+"нет необходимой верхней текстуры",false,position,element);
                    if(!skipMissing&&opposite<0&&Empty(side.middle))yield return new MapIssue("missing-middle",prefix+"нет средней текстуры",false,position,element);
                    if(!skipMissing&&!(Empty(side.lower)&&low&&plane&&Arg(line.args,0)>0)&&Empty(side.lower)&&(low||futureLow))
                        yield return new MapIssue("missing-lower",prefix+"нет необходимой нижней текстуры"+(!low&&movingLow?" для движения пола":!low&&floorLow?" для 3D-пола":""),false,position,element);
                    if(!Empty(side.upper)&&!high&&!sky&&!futureHigh)
                        yield return new MapIssue("unused-upper",prefix+"лишняя верхняя текстура «"+side.upper+"»",position,new UnusedTextureTarget(i,sideIndex,front,true,side.upper),element);
                    if(!Empty(side.lower)&&!low&&!staticSky&&!futureLow)
                        yield return new MapIssue("unused-lower",prefix+"лишняя нижняя текстура «"+side.lower+"»",position,new UnusedTextureTarget(i,sideIndex,front,false,side.lower),element);
                }
                if((i&63)==0)yield return null;
            }
        }
        static void Neighbour(MapDocument map,int own,int other,float[] lowest,float[] highest,float?[] next)
        {
            float floor=map.sectors[other].floor;lowest[own]=Math.Min(lowest[own],floor);highest[own]=Math.Max(highest[own],floor);
            if(floor>map.sectors[own].floor&&(!next[own].HasValue||floor<next[own].Value))next[own]=floor;
        }
    }
}
