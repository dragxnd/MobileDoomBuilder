// GPL-3.0. Binary Hexen diagnostics derived from UDB CheckPolyobjects.
using System;using System.Collections.Generic;
namespace MobileUDB.Core
{
    public static class PolyobjectChecks
    {
        static readonly HashSet<string> actions=new HashSet<string>(StringComparer.OrdinalIgnoreCase){"Polyobj_StartLine","Polyobj_ExplicitLine","Polyobj_RotateLeft","Polyobj_RotateRight","Polyobj_Move","Polyobj_MoveTimes8","Polyobj_DoorSwing","Polyobj_DoorSlide","Polyobj_OR_MoveToSpot","Polyobj_MoveToSpot","Polyobj_Stop","Polyobj_MoveTo","Polyobj_OR_MoveTo","Polyobj_OR_RotateLeft","Polyobj_OR_RotateRight","Polyobj_OR_Move","Polyobj_OR_MoveTimes8"};
        public static bool IsMarker(ThingType type)
        {string name=(type==null?null:type.className)??"";return string.Equals(name,"$PolyAnchor",StringComparison.OrdinalIgnoreCase)||IsSpawn(name);}
        static bool IsSpawn(string name){return string.Equals(name,"$PolySpawn",StringComparison.OrdinalIgnoreCase)||string.Equals(name,"$PolySpawnCrush",StringComparison.OrdinalIgnoreCase)||string.Equals(name,"$PolySpawnHurt",StringComparison.OrdinalIgnoreCase);}
        public static List<int> AnchorNumbers(MapDocument map,ThingCatalog types)
        {
            var unique=new HashSet<int>();if(types!=null)foreach(var thing in map.things)
                if(thing!=null&&string.Equals(types.Find(thing.type).className,"$PolyAnchor",StringComparison.OrdinalIgnoreCase)&&!float.IsNaN(thing.angle)&&!float.IsInfinity(thing.angle)&&thing.angle>=0&&thing.angle<=65535)unique.Add((int)Math.Round(thing.angle));
            var result=new List<int>(unique);result.Sort((a,b)=>b.CompareTo(a));return result;
        }
        static void Add(Dictionary<int,List<int>> groups,int number,int index){List<int> items;if(!groups.TryGetValue(number,out items))groups[number]=items=new List<int>();items.Add(index);}
        public static IEnumerable<MapIssue> Scan(MapDocument map,ActionCatalog catalog,ThingCatalog types)
        {
            if(map.format!=MapFormat.Hexen||catalog==null||types==null)yield break;
            if(!string.IsNullOrEmpty(catalog.warning)){yield return new MapIssue("polyobjects-unavailable","Полиобъекты не проверены: недоступен профиль действий.",false,null);yield break;}
            var lines=new Dictionary<string,Dictionary<int,List<int>>>();var anchors=new Dictionary<int,List<int>>();var spawns=new Dictionary<int,List<int>>();
            for(int i=0;i<map.lines.Count;i++)
            {
                var line=map.lines[i];var action=line.action>0?catalog.Find(line.action):null;
                if(action!=null&&actions.Contains(action.identifier??""))
                {
                    if(line.args==null||line.args.Length!=5)yield return Lines(map,"poly-invalid-args","Стена #"+i+": полиобъект требует пяти аргументов",new List<int>{i});
                    else{Dictionary<int,List<int>> group;if(!lines.TryGetValue(action.identifier,out group))lines[action.identifier]=group=new Dictionary<int,List<int>>();Add(group,line.args[0],i);}
                }
                if((i&127)==0)yield return null;
            }
            for(int i=0;i<map.things.Count;i++)
            {
                var thing=map.things[i];if(thing!=null)
                {
                    var type=types.Find(thing.type);string name=type.className??"";
                    if(IsMarker(type)&&!float.IsNaN(thing.angle)&&!float.IsInfinity(thing.angle))
                    {
                        if(thing.angle<0||thing.angle>65535)yield return Things(map,"poly-invalid-number","Объект #"+i+": номер полиобъекта не помещается в бинарное поле угла",new List<int>{i});
                        else{int number=(int)Math.Round(thing.angle);Add(string.Equals(name,"$PolyAnchor",StringComparison.OrdinalIgnoreCase)?anchors:spawns,number,i);}
                    }
                }
                if((i&127)==0)yield return null;
            }
            foreach(var group in lines)foreach(var number in group.Value)
            {if(!spawns.ContainsKey(number.Key))yield return Lines(map,"poly-line-start",group.Key+": нет стартовой точки полиобъекта "+number.Key,number.Value);yield return null;}
            foreach(string identifier in new[]{"Polyobj_StartLine","Polyobj_ExplicitLine"})
            {
                Dictionary<int,List<int>> group;if(!lines.TryGetValue(identifier,out group))continue;bool start=identifier=="Polyobj_StartLine";
                foreach(var number in group)
                {
                    if(start&&number.Value.Count>1)yield return Lines(map,"poly-start-duplicate","Несколько стартовых стен полиобъекта "+number.Key,number.Value);
                    foreach(int index in number.Value)
                    {
                        int mirror=map.lines[index].args[start?1:2];if(mirror>0)
                        {
                            var one=new List<int>{index};if(!spawns.ContainsKey(mirror))yield return Lines(map,"poly-mirror-missing",identifier+": нет зеркального полиобъекта "+mirror,one);
                            if(mirror==number.Key)yield return Lines(map,"poly-mirror-self",identifier+": полиобъект "+number.Key+" зеркалит самого себя",one);
                        }
                        yield return null;
                    }
                }
            }
            foreach(var number in anchors)
            {
                if(!spawns.ContainsKey(number.Key))yield return Things(map,"poly-anchor-start","Якорь: нет стартовой точки полиобъекта "+number.Key,number.Value);
                if(number.Value.Count>1)yield return Things(map,"poly-anchor-duplicate","Несколько якорей полиобъекта "+number.Key,number.Value);yield return null;
            }
            foreach(var number in spawns)
            {
                if(!anchors.ContainsKey(number.Key))yield return Things(map,"poly-spawn-anchor","Стартовая точка: нет якоря полиобъекта "+number.Key,number.Value);
                if(number.Value.Count>1)yield return Things(map,"poly-spawn-duplicate","Несколько стартовых точек полиобъекта "+number.Key,number.Value);yield return null;
            }
        }
        static MapIssue Lines(MapDocument map,string code,string message,List<int> indices)
        {var refs=new ElementRef[indices.Count];for(int i=0;i<refs.Length;i++)refs[i]=new ElementRef(ElementKind.Line,indices[i]);var line=map.lines[indices[0]];var a=map.vertices[line.start];var b=map.vertices[line.end];return new MapIssue(code,message,true,new MapPoint((float)(((double)a.x+b.x)/2),(float)(((double)a.y+b.y)/2)),refs);}
        static MapIssue Things(MapDocument map,string code,string message,List<int> indices)
        {var refs=new ElementRef[indices.Count];for(int i=0;i<refs.Length;i++)refs[i]=new ElementRef(ElementKind.Thing,indices[i]);return new MapIssue(code,message,true,map.things[indices[0]].Position,refs);}
    }
}
