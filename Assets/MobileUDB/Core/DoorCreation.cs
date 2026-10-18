// GPL-3.0. Binary subset of BuilderModes SectorsMode.MakeDoor.
using System;
using System.Collections.Generic;
using System.Globalization;

namespace MobileUDB.Core
{
    public sealed class DoorConfiguration
    {
        public string door, track, ceiling;
        public int action, activation;
        public readonly int[] args = new int[5];
        public readonly Dictionary<int,bool> flags = new Dictionary<int,bool>();
        public static DoorConfiguration Load(Func<string,string> texts,string game,MapFormat format)
        {
            if(format!=MapFormat.Doom&&format!=MapFormat.Hexen)throw new InvalidOperationException("Двери доступны для бинарных Doom / Hexen карт.");
            if(game=="Doom2")game="Doom";
            if((game!="Doom"&&game!="Heretic"&&game!="Hexen"&&game!="Strife")||(format==MapFormat.Doom&&game=="Hexen"))
                throw new InvalidOperationException("Для дверей нужен поддерживаемый профиль игры.");
            var textures=GameConfigText.Read(texts("DoorProfiles.cfg")).Section(game);
            var actions=GameConfigText.Read(texts("ZDoom_misc.cfg"),texts).Section(format==MapFormat.Hexen?"doormaking_hexen":"doormaking_doom");
            var result=new DoorConfiguration{door=textures.Text("makedoordoor","-"),track=textures.Text("makedoortrack","-"),ceiling=textures.Text("makedoorceil","-"),action=actions.Integer("makedooraction"),activation=actions.Integer("makedooractivate")};
            for(int i=0;i<5;i++)result.args[i]=actions.Integer("makedoorarg"+i);
            foreach(var flag in actions.Section("makedoorflags").values)
            {
                string key=flag.Key;bool enabled=!key.StartsWith("-",StringComparison.Ordinal);int bit;
                if(!int.TryParse(enabled?key:key.Substring(1),NumberStyles.Integer,CultureInfo.InvariantCulture,out bit)||bit<=0||bit>65535)
                    throw new InvalidOperationException("Некорректный флаг создания двери.");
                result.flags[bit]=enabled;
            }
            return result;
        }
    }
    public sealed class DoorOptions
    {
        public string door, track, ceiling, floor;
        public bool resetOffsets, applyActions, applyTag;
    }
    public static class DoorCreation
    {
        sealed class Role { public int line, side; }
        static string Optional(string texture){return string.IsNullOrWhiteSpace(texture)?null:SurfaceTextureEdits.Name(texture);}
        public static DoorPlan Prepare(MapDocument map,IEnumerable<int> sectors,DoorOptions options,DoorConfiguration config,ActionCatalog actions=null)
        {
            ThingClipboard.Binary(map);
            if(sectors==null||options==null||config==null)throw new ArgumentNullException();
            string door=SurfaceTextureEdits.Name(options.door),track=Optional(options.track),ceiling=Optional(options.ceiling),floor=Optional(options.floor);
            var selected=new SortedSet<int>(sectors);if(selected.Count==0)throw new InvalidOperationException("Выберите секторы двери.");
            foreach(int index in selected)if(index<0||index>=map.sectors.Count)throw new InvalidOperationException("Выделенный сектор больше не существует.");
            if(config.action<0||config.action>(map.format==MapFormat.Hexen?255:65535)||(config.activation&~7168)!=0)
                throw new InvalidOperationException("Некорректное действие создания двери.");
            bool argumentTag=false;foreach(int arg in config.args){if(arg==-1)argumentTag=true;else if(arg<0||arg>255)throw new InvalidOperationException("Аргумент двери должен помещаться в байт.");}
            // Only the detached snapshot changes until every texture / tag / side limit passes.
            var next=map.Clone();var roles=new Dictionary<int,List<Role>>();foreach(int sector in selected)roles[sector]=new List<Role>();
            var uses=new Dictionary<int,int>();var affected=new HashSet<int>();
            for(int i=0;i<next.lines.Count;i++)
            {
                var line=next.lines[i];Count(uses,line.front);Count(uses,line.back);
                bool front=Selected(next,line.front,selected),back=Selected(next,line.back,selected);
                if(front||back){if(line.front>=0)affected.Add(i*2);if(line.back>=0)affected.Add(i*2+1);}
            }
            var affectedUses=new Dictionary<int,int>();foreach(int role in affected){var line=next.lines[role/2];Count(affectedUses,role%2==0?line.front:line.back);}
            int needed=0;foreach(var pair in affectedUses)needed+=Math.Min(uses[pair.Key]-1,pair.Value);
            if(next.sides.Count+needed>65535)throw new InvalidOperationException("Превышен предел сторон Doom / Hexen. Двери не созданы.");
            var orderedRoles=new List<int>(affected);orderedRoles.Sort();
            foreach(int role in orderedRoles)
            {
                int index=role/2;bool front=role%2==0;var line=next.lines[index];int before=front?line.front:line.back;
                int side=SurfaceTextureEdits.Exclusive(next,index,front,uses[before]);
                if(side!=before){uses[before]--;uses[side]=1;}
                int sector=next.sides[side].sector;if(selected.Contains(sector))roles[sector].Add(new Role{line=index,side=side});
            }
            foreach(int sectorIndex in selected)
            {
                var sector=next.sectors[sectorIndex];sector.ceiling=sector.floor;int tag=0;
                foreach(var role in roles[sectorIndex])
                {
                    var line=next.lines[role.line];bool front=line.front==role.side;var side=next.sides[role.side];int otherIndex=front?line.back:line.front;
                    if(otherIndex<0)
                    {side.upper=side.lower="-";if(track!=null)side.middle=track;line.flags=(line.flags&~8)|16;}
                    else
                    {
                        var other=next.sides[otherIndex];if(floor!=null)sector.floorTexture=floor;if(ceiling!=null)sector.ceilingTexture=ceiling;other.upper=door;
                        line.flags&=~24;
                        if(options.applyActions)
                        {
                            line.action=config.action;if(next.format==MapFormat.Hexen)line.flags=(line.flags&~7168)|config.activation;
                            foreach(var flag in config.flags)line.flags=flag.Value?line.flags|flag.Key:line.flags&~flag.Key;
                        }
                        if(options.applyTag||argumentTag)
                        {if(tag==0)tag=PasteTags.NextFree(next,actions,argumentTag&&next.format==MapFormat.Hexen?255:65535);sector.tag=tag;}
                        // Matches upstream: arguments are initialized even when actions are unchecked.
                        for(int i=0;i<5;i++)line.args[i]=config.args[i]==-1?tag:config.args[i];
                        if(front){int vertex=line.start;line.start=line.end;line.end=vertex;line.SwapSides();}
                    }
                    if(options.resetOffsets)
                    {side.offsetX=side.offsetY=0;if(otherIndex>=0){next.sides[otherIndex].offsetX=next.sides[otherIndex].offsetY=0;}}
                }
            }
            next.Validate();return new DoorPlan(map,next,selected.Count);
        }
        static bool Selected(MapDocument map,int side,SortedSet<int> selected){return side>=0&&selected.Contains(map.sides[side].sector);}
        static void Count(Dictionary<int,int> uses,int side){if(side<0)return;int count;uses.TryGetValue(side,out count);uses[side]=count+1;}
    }
    public sealed class DoorPlan
    {
        readonly MapDocument original,prepared;bool applied;
        public readonly int SectorCount;
        internal DoorPlan(MapDocument original,MapDocument prepared,int count){this.original=original;this.prepared=prepared;SectorCount=count;}
        public void Apply(MapDocument map)
        {
            if(applied||!ReferenceEquals(map,original))throw new InvalidOperationException("Карта изменилась. Откройте создание дверей снова.");
            applied=true;map.lines=prepared.lines;map.sides=prepared.sides;map.sectors=prepared.sectors;
        }
    }
}
