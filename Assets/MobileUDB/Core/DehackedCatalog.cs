// GPL-3.0. Editor metadata follows UDB DehackedParser/Thing/Frame and Doom config.
// Patches are never executed or rewritten; gameplay/code-pointer data stays opaque.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace MobileUDB.Core
{
    public sealed partial class ThingCatalog
    {
        sealed class DehackedPatch
        {
            public readonly Dictionary<int,ConfigNode> actors=new Dictionary<int,ConfigNode>();
            public readonly Dictionary<int,ConfigNode> frames=new Dictionary<int,ConfigNode>();
            public readonly Dictionary<int,string> sprites=new Dictionary<int,string>();
            public readonly Dictionary<string,string> replacements=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string,string> texts=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            public void Merge(DehackedPatch next)
            {
                MergeNodes(actors,next.actors);MergeNodes(frames,next.frames);
                foreach(var p in next.sprites)sprites[p.Key]=p.Value;
                foreach(var p in next.replacements)replacements[p.Key]=p.Value;
                foreach(var p in next.texts)texts[p.Key]=p.Value;
            }
            static void MergeNodes(Dictionary<int,ConfigNode> target,Dictionary<int,ConfigNode> source)
            {
                foreach(var p in source){ConfigNode node;if(!target.TryGetValue(p.Key,out node))target[p.Key]=node=new ConfigNode();node.Merge(p.Value);}
            }
        }
        public void ApplyDehacked(string definitions,DoomResources resources)
        {
            var root=GameConfigText.Read(definitions);var baseActors=root.Section("things");var baseFrames=root.Section("frames");
            var state=new DehackedPatch();
            foreach(var file in resources.dehackedPatches)
            {
                try {state.Merge(ReadDehacked(file));}
                catch(InvalidDataException error){if(warnings.Count<30)warnings.Add(file.path+": DEHACKED пропущен — "+error.Message);}
            }
            var sprites=new Dictionary<int,string>();var renamed=new Dictionary<string,string>(state.texts,StringComparer.OrdinalIgnoreCase);
            foreach(var p in root.Section("sprites").values)
            {
                int id;if(!int.TryParse(p.Key,out id))continue;string name=p.Value,replace;
                if(state.replacements.TryGetValue(name,out replace)||state.texts.TryGetValue(name,out replace))name=replace;
                if(state.sprites.TryGetValue(id,out replace))name=replace;
                sprites[id]=name;renamed[p.Value]=name;
            }
            foreach(var p in state.sprites)sprites[p.Key]=p.Value;
            // Rename the unmodified catalog too. Each replacement is evaluated once
            // against the original prefix, so chains cannot rename a sprite twice.
            foreach(var type in types.Values)
            {
                string replace;if(type.sprite.Length>=4&&renamed.TryGetValue(type.sprite.Substring(0,4),out replace))type.sprite=replace+type.sprite.Substring(4);
            }
            var originals=new Dictionary<int,ThingType>(types);
            // A changed editor number removes its previous map entry before any
            // new mappings are assigned, allowing two actors to exchange numbers.
            foreach(var p in state.actors)
            {
                if(!p.Value.values.ContainsKey("id #"))continue;
                var original=baseActors.Section(p.Key.ToString(CultureInfo.InvariantCulture));int id;
                if(int.TryParse(original.Text("doomednum"),out id)&&p.Value.Integer("id #",id)!=id)types.Remove(id);
            }
            var actorIds=new HashSet<int>(state.actors.Keys);
            foreach(var p in baseActors.children){int id;if(int.TryParse(p.Key,out id))actorIds.Add(id);}
            var ordered=new List<int>(actorIds);ordered.Sort();
            foreach(int actor in ordered)
            {
                var original=baseActors.Section(actor.ToString(CultureInfo.InvariantCulture));ConfigNode patch;
                bool touched=state.actors.TryGetValue(actor,out patch);if(!touched)patch=new ConfigNode();
                int frame=patch.Integer("initial frame",original.Integer("initialframe"));
                if(!touched&&!state.frames.ContainsKey(frame))continue;
                int id=patch.Integer("id #",original.Integer("doomednum",-1));if(id<0||id>65535)continue;
                ThingType source;int originalId=original.Integer("doomednum",id);
                ThingType type=originals.TryGetValue(originalId,out source)?CopyDehackedType(source,id):new ThingType{id=id,title=original.Text("name","DeHackEd thing "+actor),category="DEHACKED",sprite="",errorCheck=1};
                type.title=patch.Text("$name",type.title);
                float checkRadius=type.checkRadius??(type.fixedSize||type.radius<4?14:type.radius);
                float checkHeight=type.checkHeight??type.height;
                int checkWidth=patch.values.ContainsKey("width")?patch.Integer("width")>>16:original.Integer("width");
                int checkTall=patch.values.ContainsKey("height")?patch.Integer("height")>>16:original.Integer("height");
                type.checkRadius=checkWidth!=0?checkWidth:checkRadius;
                type.checkHeight=checkTall!=0?checkTall:checkHeight;
                if(patch.values.ContainsKey("$editor category"))type.category=patch.Text("$editor category");
                // Bundled dimensions are map units; patch dimensions are 16.16.
                type.radius=Math.Max(1,patch.values.ContainsKey("width")?patch.Integer("width")/65536f:original.Integer("width",(int)type.radius));
                type.height=Math.Max(1,patch.values.ContainsKey("height")?patch.Integer("height")/65536f:original.Integer("height",(int)type.height));
                type.hangs=DehackedFlag(patch.Text("bits",original.Text("bits","0")),256,"SPAWNCEILING");
                // ModifyByDehackedThing changes blocking, retaining the inherited
                // error-check level (a newly solid decoration is not promoted).
                type.blocking=DehackedFlag(patch.Text("bits",original.Text("bits","0")),2,"SOLID")?1:0;
                ConfigNode framePatch;if(!state.frames.TryGetValue(frame,out framePatch))framePatch=new ConfigNode();
                var baseFrame=baseFrames.Section(frame.ToString(CultureInfo.InvariantCulture));
                int spriteId=framePatch.Integer("sprite number",baseFrame.Integer("spritenumber",baseFrames.Section("default").Integer("spritenumber",138)));
                int sub=framePatch.Integer("sprite subnumber",baseFrame.Integer("spritesubnumber"));type.bright=(sub&32768)!=0;sub&=32767;
                string sprite;
                if(patch.values.ContainsKey("$editor sprite"))type.sprite=patch.Text("$editor sprite").ToUpperInvariant();
                else if(sprites.TryGetValue(spriteId,out sprite)&&sub<29)type.sprite=sprite+(char)('A'+sub);
                else {type.sprite="";if(warnings.Count<30)warnings.Add("DEHACKED: объект "+actor+" ссылается на неизвестный спрайт / кадр.");}
                types[id]=type;
            }
            choices.Clear();choices.AddRange(types.Values);
            choices.Sort((a,b)=>{int c=StringComparer.OrdinalIgnoreCase.Compare(a.category,b.category);return c!=0?c:StringComparer.OrdinalIgnoreCase.Compare(a.title,b.title);});
        }
        static ThingType CopyDehackedType(ThingType source,int id)
        {
            return new ThingType{id=id,title=source.title,category=source.category,sprite=source.sprite,className=source.className,
                radius=source.radius,height=source.height,spriteScale=source.spriteScale,alpha=source.alpha,
                hangs=source.hangs,absoluteZ=source.absoluteZ,fixedRotation=source.fixedRotation,bright=source.bright,fixedSize=source.fixedSize,argumentTypes=(int[])source.argumentTypes.Clone(),blocking=source.blocking,errorCheck=source.errorCheck,checkRadius=source.checkRadius,checkHeight=source.checkHeight};
        }
        static bool DehackedFlag(string value,long mask,string name)
        {
            long bits;if(long.TryParse(value,NumberStyles.Integer,CultureInfo.InvariantCulture,out bits))return (bits&mask)!=0;
            foreach(string flag in value.Split('+','|',',',' ','\t'))if(string.Equals(flag,name,StringComparison.OrdinalIgnoreCase))return true;return false;
        }
        static DehackedPatch ReadDehacked(ResourceFile file)
        {
            if(file.data.Length>8*1024*1024)throw new InvalidDataException("Патч превышает 8 МиБ.");
            // ASCII decoding preserves byte counts for the legacy Text block.
            string text=Encoding.ASCII.GetString(file.data);int at=0,block=0;ConfigNode node=null;var result=new DehackedPatch();
            while(at<text.Length)
            {
                if(result.actors.Count+result.frames.Count+result.sprites.Count+result.replacements.Count+result.texts.Count>16384)throw new InvalidDataException("Слишком много определений в патче.");
                int end=text.IndexOf('\n',at);if(end<0)end=text.Length;
                string line=text.Substring(at,end-at).Trim();at=Math.Min(text.Length,end+1);
                if(line.Length==0){block=0;node=null;continue;}
                if(line.StartsWith("#$",StringComparison.Ordinal))line=line.Substring(1);
                else if(line.StartsWith("#",StringComparison.Ordinal))continue;
                int number;string name;
                if(DehackedHeader(line,"Thing",out number,out name))
                {
                    if(!result.actors.TryGetValue(number,out node))result.actors[number]=node=new ConfigNode();
                    if(name.Length>0)node.values["$name"]=name;block=1;continue;
                }
                if(DehackedHeader(line,"Frame",out number,out name))
                {if(!result.frames.TryGetValue(number,out node))result.frames[number]=node=new ConfigNode();block=2;continue;}
                if(line.Equals("[SPRITES]",StringComparison.OrdinalIgnoreCase)){block=3;node=null;continue;}
                if(line.StartsWith("Text ",StringComparison.OrdinalIgnoreCase)||line.StartsWith("Text\t",StringComparison.OrdinalIgnoreCase))
                {
                    var parts=line.Split(new[]{' ','\t'},StringSplitOptions.RemoveEmptyEntries);int oldCount,newCount;
                    if(parts.Length<3||!int.TryParse(parts[1],out oldCount)||!int.TryParse(parts[2],out newCount)||oldCount<0||newCount<0)throw new InvalidDataException("Неверный размер блока Text.");
                    string oldText=DehackedText(text,ref at,oldCount),newText=DehackedText(text,ref at,newCount);
                    if(oldCount==4&&newCount==4)result.texts[oldText.ToUpperInvariant()]=newText.ToUpperInvariant();block=0;node=null;continue;
                }
                if(line.StartsWith("include ",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("INCLUDE пока не поддерживается; объедините патчи в DEHACKED.");
                int equal=line.IndexOf('=');
                if(line[0]=='['||equal<0){block=0;node=null;continue;}
                string key=line.Substring(0,equal).Trim().ToLowerInvariant(),value=line.Substring(equal+1).Trim();
                int comment=value.IndexOf('#');if(comment>=0)value=value.Substring(0,comment).Trim();
                if(block==3)
                {
                    if(value.Length!=4)throw new InvalidDataException("Имя спрайта должно содержать 4 символа.");
                    value=value.ToUpperInvariant();
                    if(int.TryParse(key,NumberStyles.Integer,CultureInfo.InvariantCulture,out number)&&number>=0)result.sprites[number]=value;
                    else if(key.Length==4)result.replacements[key]=value;
                    else throw new InvalidDataException("Неверный номер / имя спрайта.");
                }
                else if(block==1||block==2)
                {
                    bool numeric=block==1?(key=="id #"||key=="initial frame"||key=="width"||key=="height"):(key=="sprite number"||key=="sprite subnumber");
                    if(numeric)
                    {
                        if(!int.TryParse(value,NumberStyles.Integer,CultureInfo.InvariantCulture,out number)||number<(key=="id #"?-1:0)||(key=="id #"&&number>65535))throw new InvalidDataException("Неверное значение "+key+".");
                        node.values[key]=value;
                    }
                    else if(block==1&&(key=="bits"||key=="$editor sprite"||key=="$editor category"||key=="$editor angled"))
                    {
                        long bits;
                        if(key=="bits"&&value.Length>0&&(char.IsDigit(value[0])||value[0]=='-')&&!long.TryParse(value,NumberStyles.Integer,CultureInfo.InvariantCulture,out bits))throw new InvalidDataException("Неверное значение bits.");
                        node.values[key]=value;
                    }
                }
                if(result.actors.Count+result.frames.Count+result.sprites.Count>16384)throw new InvalidDataException("Слишком много определений в патче.");
            }
            return result;
        }
        static bool DehackedHeader(string line,string keyword,out int number,out string name)
        {
            number=0;name="";if(line.Length<=keyword.Length||!line.StartsWith(keyword,StringComparison.OrdinalIgnoreCase)||!char.IsWhiteSpace(line[keyword.Length]))return false;
            string value=line.Substring(keyword.Length).TrimStart();int end=0;while(end<value.Length&&!char.IsWhiteSpace(value[end])&&value[end]!='(')end++;
            if(!int.TryParse(value.Substring(0,end),NumberStyles.Integer,CultureInfo.InvariantCulture,out number)||number<0)throw new InvalidDataException("Неверный номер "+keyword+".");
            int first=value.IndexOf('('),last=value.LastIndexOf(')');if(first>=0&&last>first)name=value.Substring(first+1,last-first-1).Trim();return true;
        }
        static string DehackedText(string text,ref int at,int count)
        {
            if(count>text.Length-at)throw new InvalidDataException("Неполный блок Text.");
            var value=new StringBuilder(Math.Min(count,4096));
            while(count>0)
            {
                if(at>=text.Length)throw new InvalidDataException("Неполный блок Text.");
                char c=text[at++];if(c=='\r')continue;value.Append(c);count--;
            }
            return value.ToString();
        }
    }
}
