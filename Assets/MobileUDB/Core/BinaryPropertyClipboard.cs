using System;
using System.Collections.Generic;

namespace MobileUDB.Core
{
    [Flags] public enum PropertyFields
    {
        None=0,Upper=1,Middle=2,Lower=4,OffsetX=8,OffsetY=16,
        LineFlags=32,Activation=64,LineAction=128,LineTag=256,LineArguments=512,
        FloorHeight=1024,CeilingHeight=2048,FloorTexture=4096,CeilingTexture=8192,Light=16384,SectorEffect=32768,SectorTag=65536,
        ThingType=131072,ThingAngle=262144,ThingHeight=524288,ThingFlags=1048576,ThingAction=2097152,ThingArguments=4194304,ThingTag=8388608,
        Sides=Upper|Middle|Lower|OffsetX|OffsetY,Lines=LineFlags|Activation|LineAction|LineTag|LineArguments,
        Sectors=FloorHeight|CeilingHeight|FloorTexture|CeilingTexture|Light|SectorEffect|SectorTag,
        Things=ThingType|ThingAngle|ThingHeight|ThingFlags|ThingAction|ThingArguments|ThingTag,All=Sides|Lines|Sectors|Things
    }
    public sealed class PropertyPastePlan
    {
        internal MapDocument map;
        internal readonly List<Action<MapDocument>> writes=new List<Action<MapDocument>>();
        internal readonly List<SurfaceRef> retained=new List<SurfaceRef>();
        internal readonly Dictionary<SurfaceRef,bool> fronts=new Dictionary<SurfaceRef,bool>();
        bool applied;
        public int ChangeCount {get{return writes.Count;}}
        // Plans are applied immediately to their original map, before another edit.
        public SurfaceBatchResult Apply(MapDocument target)
        {
            if(applied||!ReferenceEquals(map,target))throw new InvalidOperationException("Карта изменилась. Выберите элементы снова.");applied=true;
            foreach(var write in writes)write(target);
            var result=new SurfaceBatchResult{changed=ChangeCount};foreach(var original in retained)
            {var id=original;if(!id.IsFlat)id.side=fronts[original]?target.lines[id.line].front:target.lines[id.line].back;result.rebound[original]=id;}
            return result;
        }
    }
    // Independent immutable snapshots, following original UDB CopyStructures.cs.
    public sealed class BinaryPropertyClipboard
    {
        MapLine line;MapSide front,back,visualSide;MapSector sector;MapThing thing;
        MapFormat lineFormat,thingFormat;
        public bool HasWalls {get{return line!=null;}}
        public bool HasSectors {get{return sector!=null;}}
        public bool HasThings {get{return thing!=null;}}
        static bool On(PropertyFields fields,PropertyFields flag){return (fields&flag)!=0;}
        static void Binary(MapDocument map)
        {if(map==null||(map.format!=MapFormat.Doom&&map.format!=MapFormat.Hexen))throw new InvalidOperationException("Поддерживаются бинарные карты Doom / Hexen.");}
        static void Index(int index,int count){if(index<0||index>=count)throw new InvalidOperationException("Элемент больше не существует. Выберите его снова.");}
        static MapSide Side(MapSide s){return s==null?null:new MapSide{sector=s.sector,upper=s.upper,middle=s.middle,lower=s.lower,offsetX=s.offsetX,offsetY=s.offsetY};}
        static MapSector Sector(MapSector s){return new MapSector{floor=s.floor,ceiling=s.ceiling,floorTexture=s.floorTexture,ceilingTexture=s.ceilingTexture,light=s.light,special=s.special,tag=s.tag};}
        static MapThing Thing(MapThing t){return new MapThing{type=t.type,angle=t.angle,z=t.z,flags=t.flags,tag=t.tag,action=t.action,args=t.args==null?null:(int[])t.args.Clone()};}
        static MapLine Line(MapLine l){return new MapLine{flags=l.flags,action=l.action,tag=l.tag,args=l.args==null?null:(int[])l.args.Clone()};}
        public void CopyLine(MapDocument map,int index)
        {
            Binary(map);Index(index,map.lines.Count);var source=map.lines[index];MapSide a=null,b=null;
            if(source.front>=0){Index(source.front,map.sides.Count);a=Side(map.sides[source.front]);}
            if(source.back>=0){Index(source.back,map.sides.Count);b=Side(map.sides[source.back]);}
            line=Line(source);front=a;back=b;lineFormat=map.format;
        }
        public void CopyWall(MapDocument map,SurfaceRef surface)
        {
            if(surface.IsFlat||!SurfaceTextureEdits.Valid(map,surface))throw new InvalidOperationException("Выберите сторону стены.");
            CopyLine(map,surface.line);visualSide=Side(map.sides[surface.side]);
        }
        public void CopySector(MapDocument map,int index){Binary(map);Index(index,map.sectors.Count);sector=Sector(map.sectors[index]);}
        public void CopyThing(MapDocument map,int index){Binary(map);Index(index,map.things.Count);thing=Thing(map.things[index]);thingFormat=map.format;}
        static PropertyFields Supported(MapFormat format)
        {
            var result=PropertyFields.All;
            return format==MapFormat.Doom?result&~(PropertyFields.Activation|PropertyFields.LineArguments|PropertyFields.ThingHeight|PropertyFields.ThingTag|PropertyFields.ThingAction|PropertyFields.ThingArguments):result&~PropertyFields.LineTag;
        }
        public PropertyFields Available(MapFormat target,bool classicSides)
        {
            var fields=PropertyFields.None;
            if(line!=null){fields|=PropertyFields.Lines&Supported(lineFormat);if(classicSides?(front!=null||back!=null):visualSide!=null)fields|=PropertyFields.Sides;}
            if(sector!=null)fields|=PropertyFields.Sectors;
            if(thing!=null)fields|=PropertyFields.Things&Supported(thingFormat);
            return fields&Supported(target);
        }
        static void Range(float n,int min,int max)
        {if(float.IsNaN(n)||float.IsInfinity(n)||n<min||n>max)throw new InvalidOperationException("Скопированное значение за пределами бинарного формата.");}
        static void Args(int[] values)
        {if(values==null||values.Length!=5)throw new InvalidOperationException("Требуются пять аргументов.");foreach(int n in values)Range(n,0,255);}
        void SetLine(MapLine target,PropertyFields fields,MapFormat format)
        {
            if(On(fields,PropertyFields.LineFlags)){Range(line.flags,0,65535);target.flags=format==MapFormat.Hexen?(target.flags&7168)|(line.flags&~7168):line.flags;}
            if(On(fields,PropertyFields.Activation)){Range(line.flags,0,65535);target.flags=ActionCatalog.SetActivation(target.flags,line.flags);}
            if(On(fields,PropertyFields.LineAction)){Range(line.action,0,format==MapFormat.Hexen?255:65535);target.action=line.action;}
            if(On(fields,PropertyFields.LineTag)){Range(line.tag,0,65535);target.tag=line.tag;}
            if(On(fields,PropertyFields.LineArguments)){Args(line.args);target.args=(int[])line.args.Clone();}
        }
        static void SetSide(MapSide target,MapSide source,PropertyFields fields)
        {
            if(On(fields,PropertyFields.Upper))target.upper=SurfaceTextureEdits.Name(source.upper);
            if(On(fields,PropertyFields.Middle))target.middle=SurfaceTextureEdits.Name(source.middle);
            if(On(fields,PropertyFields.Lower))target.lower=SurfaceTextureEdits.Name(source.lower);
            if(On(fields,PropertyFields.OffsetX)){Range(source.offsetX,-32768,32767);target.offsetX=source.offsetX;}
            if(On(fields,PropertyFields.OffsetY)){Range(source.offsetY,-32768,32767);target.offsetY=source.offsetY;}
        }
        void SetSector(MapSector target,PropertyFields fields)
        {
            if(On(fields,PropertyFields.FloorHeight)){Range(sector.floor,-32768,32767);target.floor=sector.floor;}
            if(On(fields,PropertyFields.CeilingHeight)){Range(sector.ceiling,-32768,32767);target.ceiling=sector.ceiling;}
            if((On(fields,PropertyFields.FloorHeight)||On(fields,PropertyFields.CeilingHeight))&&target.floor>target.ceiling)throw new InvalidOperationException("Скопированная высота поднимает пол выше потолка.");
            if(On(fields,PropertyFields.FloorTexture))target.floorTexture=SurfaceTextureEdits.Name(sector.floorTexture);
            if(On(fields,PropertyFields.CeilingTexture))target.ceilingTexture=SurfaceTextureEdits.Name(sector.ceilingTexture);
            if(On(fields,PropertyFields.Light)){Range(sector.light,-32768,32767);target.light=sector.light;}
            if(On(fields,PropertyFields.SectorEffect)){Range(sector.special,0,65535);target.special=sector.special;}
            if(On(fields,PropertyFields.SectorTag)){Range(sector.tag,0,65535);target.tag=sector.tag;}
        }
        void SetThing(MapThing target,PropertyFields fields)
        {
            if(On(fields,PropertyFields.ThingType)){Range(thing.type,0,65535);target.type=thing.type;}
            if(On(fields,PropertyFields.ThingAngle)){Range(thing.angle,0,65535);target.angle=thing.angle;}
            if(On(fields,PropertyFields.ThingHeight)){Range(thing.z,-32768,32767);target.z=thing.z;}
            if(On(fields,PropertyFields.ThingFlags)){Range(thing.flags,0,65535);target.flags=thing.flags;}
            if(On(fields,PropertyFields.ThingAction)){Range(thing.action,0,255);target.action=thing.action;}
            if(On(fields,PropertyFields.ThingTag)){Range(thing.tag,0,65535);target.tag=thing.tag;}
            if(On(fields,PropertyFields.ThingArguments)){Args(thing.args);target.args=(int[])thing.args.Clone();}
        }
        static bool Names(string a,string b){return string.Equals(a,b,StringComparison.OrdinalIgnoreCase);}
        static bool SameArgs(int[] a,int[] b){if(ReferenceEquals(a,b))return true;if(a==null||b==null||a.Length!=b.Length)return false;for(int i=0;i<a.Length;i++)if(a[i]!=b[i])return false;return true;}
        static bool Same(MapLine a,MapLine b){return a.flags==b.flags&&a.action==b.action&&a.tag==b.tag&&SameArgs(a.args,b.args);}
        static bool Same(MapSide a,MapSide b){return a.offsetX==b.offsetX&&a.offsetY==b.offsetY&&Names(a.upper,b.upper)&&Names(a.middle,b.middle)&&Names(a.lower,b.lower);}
        static bool Same(MapSector a,MapSector b){return a.floor==b.floor&&a.ceiling==b.ceiling&&a.light==b.light&&a.special==b.special&&a.tag==b.tag&&Names(a.floorTexture,b.floorTexture)&&Names(a.ceilingTexture,b.ceilingTexture);}
        static bool Same(MapThing a,MapThing b){return a.type==b.type&&a.angle==b.angle&&a.z==b.z&&a.flags==b.flags&&a.action==b.action&&a.tag==b.tag&&SameArgs(a.args,b.args);}
        sealed class SideTarget {public int line,side;public bool front;public MapSide source;}
        static void Count(Dictionary<int,int> uses,int side){if(side<0)return;int n;uses.TryGetValue(side,out n);uses[side]=n+1;}
        public PropertyPastePlan Plan(MapDocument map,IEnumerable<ElementRef> elements,IEnumerable<SurfaceRef> surfaces,PropertyFields fields=PropertyFields.All)
        {
            Binary(map);if((fields&~PropertyFields.All)!=0)throw new ArgumentOutOfRangeException("fields");
            var source=new BinaryPropertyClipboard{line=line,front=front,back=back,visualSide=visualSide,sector=sector,thing=thing,lineFormat=lineFormat,thingFormat=thingFormat};
            var plan=new PropertyPastePlan{map=map};var lines=new HashSet<int>();var sectors=new HashSet<int>();var things=new HashSet<int>();var sides=new Dictionary<int,SideTarget>();bool classic=false;
            Action<int,bool,MapSide> addSide=(index,isFront,source)=>
            {
                if(source==null)return;int side=isFront?map.lines[index].front:map.lines[index].back;if(side<0)return;Index(side,map.sides.Count);
                sides[index*2+(isFront?0:1)]=new SideTarget{line=index,side=side,front=isFront,source=source};
            };
            if(elements!=null)foreach(var id in elements)
            {
                if(id.kind==ElementKind.Line){classic=true;Index(id.index,map.lines.Count);lines.Add(id.index);if(line!=null){addSide(id.index,true,front);addSide(id.index,false,back);}}
                else if(id.kind==ElementKind.Sector){Index(id.index,map.sectors.Count);sectors.Add(id.index);}
                else if(id.kind==ElementKind.Thing){Index(id.index,map.things.Count);things.Add(id.index);}
                else if(id.kind==ElementKind.Vertex)Index(id.index,map.vertices.Count);else throw new InvalidOperationException("Неизвестный вид элемента.");
            }
            if(surfaces!=null)foreach(var id in surfaces)
            {
                if(!SurfaceTextureEdits.Valid(map,id))throw new InvalidOperationException("Поверхность больше не существует. Выберите её снова.");
                plan.retained.Add(id);
                if(id.IsFlat)sectors.Add(id.sector);
                else {lines.Add(id.line);bool isFront=map.lines[id.line].front==id.side;plan.fronts[id]=isFront;addSide(id.line,isFront,visualSide);}
            }
            // Both target styles may be present in a mixed 3D selection: thing elements
            // do not make its wall properties use classic front/back source copies.
            fields&=Available(map.format,classic);
            if(line!=null)foreach(int index in lines)
            {var old=map.lines[index];var next=Line(old);source.SetLine(next,fields,map.format);if(!Same(old,next))plan.writes.Add(m=>source.SetLine(m.lines[index],fields,m.format));}
            if(sector!=null)foreach(int index in sectors)
            {var old=map.sectors[index];var next=Sector(old);source.SetSector(next,fields);if(!Same(old,next))plan.writes.Add(m=>source.SetSector(m.sectors[index],fields));}
            if(thing!=null)foreach(int index in things)
            {var old=map.things[index];var next=Thing(old);source.SetThing(next,fields);if(!Same(old,next))plan.writes.Add(m=>source.SetThing(m.things[index],fields));}
            var uses=new Dictionary<int,int>();foreach(var l in map.lines){Count(uses,l.front);Count(uses,l.back);}
            var changed=new List<SideTarget>();var roles=new Dictionary<int,int>();
            foreach(var item in sides.Values)
            {
                var old=map.sides[item.side];var next=Side(old);SetSide(next,item.source,fields);
                if(Same(old,next))continue;changed.Add(item);int n;roles.TryGetValue(item.side,out n);roles[item.side]=n+1;
            }
            int needed=0;foreach(var pair in roles)needed+=Math.Min(uses[pair.Key]-1,pair.Value);
            if(map.sides.Count+needed>65535)throw new InvalidOperationException("Превышен предел сторон Doom / Hexen.");
            foreach(var item in changed)plan.writes.Add(m=>
            {
                int previous=item.front?m.lines[item.line].front:m.lines[item.line].back;
                int current=SurfaceTextureEdits.Exclusive(m,item.line,item.front,uses[previous]);if(current!=previous){uses[previous]--;uses[current]=1;}
                SetSide(m.sides[current],item.source,fields);
            });
            return plan;
        }
    }
}
