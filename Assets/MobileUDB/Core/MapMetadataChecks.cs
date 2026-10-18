// GPL-3.0. Unknown-code/type checks follow UDB BuilderModes/ErrorChecks.
// Tag diagnostics use config argument types and HexenLineTags load encodings.
using System;
using System.Collections.Generic;

namespace MobileUDB.Core
{
    public static class MapMetadataChecks
    {
        public static IEnumerable<MapIssue> Scan(MapDocument map,ActionCatalog actions,ThingCatalog things)
        {
            var sectorTags=new HashSet<int>();var lineTags=new Dictionary<int,int>();
            var thingTags=new Dictionary<int,HashSet<string>>();var locations=new MapPoint?[map.sectors.Count];
            for(int i=0;i<map.sectors.Count;i++)
            {
                if(map.sectors[i]!=null&&map.sectors[i].tag!=0)sectorTags.Add(map.sectors[i].tag);
                if((i&127)==0)yield return null;
            }
            for(int i=0;i<map.lines.Count;i++)
            {
                var line=map.lines[i];if(line==null)continue;var position=LinePoint(map,line);
                foreach(int side in new[]{line.front,line.back})if(side>=0&&side<map.sides.Count&&map.sides[side]!=null)
                {
                    int sector=map.sides[side].sector;if(sector>=0&&sector<locations.Length&&!locations[sector].HasValue)locations[sector]=position;
                }
                int tag=map.format==MapFormat.Doom?line.tag:0;
                if(map.format==MapFormat.Hexen&&line.args!=null&&line.args.Length==5)
                {var encoding=HexenLineTags.Encoding(line,actions);if(encoding!=null&&encoding.type==15)tag=encoding.Read(line);}
                if(tag!=0){int count;lineTags.TryGetValue(tag,out count);lineTags[tag]=count+1;}
                if((i&127)==0)yield return null;
            }
            for(int i=0;i<map.things.Count;i++)
            {
                var thing=map.things[i];if(thing!=null&&map.format==MapFormat.Hexen&&thing.tag!=0)
                {
                    HashSet<string> classes;if(!thingTags.TryGetValue(thing.tag,out classes))thingTags[thing.tag]=classes=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    classes.Add(things==null?"":things.Find(thing.type).className??"");
                }
                if((i&127)==0)yield return null;
            }
            bool knownActions=actions!=null&&string.IsNullOrEmpty(actions.warning);
            if(actions!=null&&!knownActions)yield return new MapIssue("actions-unavailable","Действия не проверены: "+actions.warning,false,null);
            for(int i=0;i<map.lines.Count;i++)
            {
                var line=map.lines[i];if(line==null)continue;var element=new ElementRef(ElementKind.Line,i);var point=LinePoint(map,line);
                if(knownActions&&line.action!=0)
                {
                    if(!actions.IsKnown(line.action))yield return new MapIssue("unknown-line-action","Стена #"+i+": код "+line.action+" не найден в профиле «"+actions.label+"»",false,point,element);
                    else
                    {
                        var info=actions.Find(line.action);
                        if(map.format==MapFormat.Doom&&line.tag!=0)
                        {
                            int count;
                            if(actions.mbf21&&line.action>=1024&&line.action<=1026&&(!lineTags.TryGetValue(line.tag,out count)||count<2))
                                yield return new MapIssue("scroller-target","Стена #"+i+": тег "+line.tag+" прокрутки MBF21 не связан с другими стенами",false,point,element);
                            if(actions.generalizedLines.Exists(category=>category.Contains(line.action))&&!info.prefix.StartsWith("D",StringComparison.Ordinal)&&!sectorTags.Contains(line.tag))
                                yield return new MapIssue("sector-tag-target","Стена #"+i+": обобщённое действие Boom ссылается на отсутствующий тег сектора "+line.tag,false,point,element);
                        }
                        if(map.format==MapFormat.Hexen&&line.args!=null&&line.args.Length==5)
                        {
                            var encoding=HexenLineTags.Encoding(line,actions);int skip=DefinitionArguments(info);
                            if(encoding!=null)
                            {
                                skip|=encoding.ArgumentMask;
                                if(encoding.type==13&&encoding.Read(line)!=0&&!sectorTags.Contains(encoding.Read(line)))
                                    yield return new MapIssue("sector-tag-target","Стена #"+i+": составной тег сектора "+encoding.Read(line)+" не найден на карте",false,point,element);
                            }
                            foreach(var issue in Arguments(info,line.args,skip,element,point,sectorTags,lineTags,thingTags,things!=null))yield return issue;
                        }
                    }
                }
                if((i&63)==0)yield return null;
            }
            for(int i=0;i<map.sectors.Count;i++)
            {
                var sector=map.sectors[i];if(sector!=null&&knownActions&&!actions.IsKnown(sector.special,true))
                    yield return new MapIssue("unknown-sector-effect","Сектор #"+i+": эффект "+sector.special+" не найден в профиле «"+actions.label+"»",false,locations[i]??new MapPoint(0,0),new ElementRef(ElementKind.Sector,i));
                if((i&127)==0)yield return null;
            }
            for(int i=0;i<map.things.Count;i++)
            {
                var thing=map.things[i];var element=new ElementRef(ElementKind.Thing,i);
                if(thing==null){yield return new MapIssue("invalid-thing","Объект #"+i+": повреждённая запись",true,null,element);continue;}
                if(float.IsNaN(thing.x)||float.IsInfinity(thing.x)||float.IsNaN(thing.y)||float.IsInfinity(thing.y)||float.IsNaN(thing.z)||float.IsInfinity(thing.z))
                {yield return new MapIssue("invalid-thing","Объект #"+i+": некорректные координаты",true,null,element);continue;}
                var point=thing.Position;
                if(things!=null&&!things.Contains(thing.type))yield return new MapIssue("unknown-thing-type","Объект #"+i+": тип "+thing.type+" не найден в игровом профиле / ресурсах",false,point,element);
                if(knownActions&&map.format==MapFormat.Hexen&&thing.action!=0)
                {
                    if(!actions.IsKnown(thing.action))yield return new MapIssue("unknown-thing-action","Объект #"+i+": код действия "+thing.action+" не найден в профиле «"+actions.label+"»",false,point,element);
                    else if(thing.args!=null&&thing.args.Length==5)
                    {var info=actions.Find(thing.action);foreach(var issue in Arguments(info,thing.args,DefinitionArguments(info),element,point,sectorTags,lineTags,thingTags,things!=null))yield return issue;}
                }
                if((i&63)==0)yield return null;
            }
        }
        static IEnumerable<MapIssue> Arguments(MapActionType info,int[] args,int skip,ElementRef element,MapPoint? position,HashSet<int> sectors,Dictionary<int,int> lines,Dictionary<int,HashSet<string>> things,bool classInfo)
        {
            for(int i=0;i<5;i++)
            {
                var argument=info.arguments[i];int tag=args[i],type=argument.type;
                // Native Hexen overrides mark this destination ID as plain byte
                // input. Its role is still a linedef reference, identified by ID.
                if(i==1&&string.Equals(info.identifier,"Teleport_Line",StringComparison.OrdinalIgnoreCase))type=15;
                if((skip&(1<<i))!=0||tag==0||!argument.used||type<13||type>15||argument.choices.Exists(choice=>choice.value==tag))continue;
                bool exists=type==13?sectors.Contains(tag):type==15?lines.ContainsKey(tag):things.ContainsKey(tag);
                string kind=type==13?"сектора":type==15?"стены":"объекта",source=element.kind==ElementKind.Line?"Стена":"Объект";
                string message=source+" #"+element.index+" · "+argument.title+": тег "+kind+" "+tag;
                if(!exists)yield return new MapIssue("argument-tag-target",message+" не найден на карте",false,position,element);
                else if(type==14&&classInfo&&argument.targetClasses.Count>0&&!argument.targetClasses.Exists(name=>things[tag].Contains(name)))
                    yield return new MapIssue("argument-target-class",message+" не содержит объектов требуемого класса",false,position,element);
            }
        }
        static int DefinitionArguments(MapActionType info)
        {
            // Output TIDs are allocated by these actions, not references to
            // existing map things. Use identifiers, never translated captions.
            switch((info.identifier??"").ToLowerInvariant())
            {
                case "thing_spawn":case "thing_spawnnofog":case "thing_spawnfacing":return 1<<3;
                case "thing_projectileintercept":case "thing_projectileaimed":return 1<<4;
                case "thing_changetid":return 1<<1;
                default:return 0;
            }
        }
        static MapPoint? LinePoint(MapDocument map,MapLine line)
        {
            if(line.start<0||line.start>=map.vertices.Count||line.end<0||line.end>=map.vertices.Count||map.vertices[line.start]==null||map.vertices[line.end]==null)return null;
            var a=map.vertices[line.start].Position;var b=map.vertices[line.end].Position;return new MapPoint((a.x+b.x)*.5f,(a.y+b.y)*.5f);
        }
    }
}
