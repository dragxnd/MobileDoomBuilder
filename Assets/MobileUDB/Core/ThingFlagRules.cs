// GPL-3.0. Binary flags derived from UDB Config/ThingsFlagsCompare.cs.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace MobileUDB.Core
{
    public sealed class ThingFlagRules
    {
        sealed class Flag
        {
            public string name, required;
            public int mask;
            public bool equal, invert, ignoreUnset;
            public string[] requiredGroups, ignoredGroups;
            public bool Set(int flags){bool set=(flags&mask)!=0;return invert?!set:set;}
            public bool Compare(int a,int b)
            {bool first=Set(a),second=Set(b);return !first&&!second&&ignoreUnset?false:equal?first==second:first&&second;}
        }
        sealed class Group
        {
            public string name;public bool optional;
            public readonly List<Flag> flags=new List<Flag>();
        }
        sealed class Result
        {
            public int value=-1;
            public readonly HashSet<string> required=new HashSet<string>(),ignored=new HashSet<string>();
        }
        readonly List<Group> groups=new List<Group>();
        readonly Dictionary<string,Flag> flags=new Dictionary<string,Flag>();
        static string[] Names(ConfigNode node,string key)
        {return node.Text(key).Split(new[]{','},StringSplitOptions.RemoveEmptyEntries);}
        public static ThingFlagRules Load(ConfigNode definitions)
        {
            var rules=new ThingFlagRules();
            foreach(var pair in definitions.children)
            {
                var group=new Group{name=pair.Key,optional=pair.Value.Boolean("optional")};
                // Bare `1;` is the null shorthand in the original configuration.
                foreach(var scalar in pair.Value.values)
                    if(scalar.Value==null||scalar.Value.Length==0)rules.AddFlag(group,scalar.Key,new ConfigNode());
                foreach(var child in pair.Value.children)rules.AddFlag(group,child.Key,child.Value);
                rules.groups.Add(group);
            }
            return rules;
        }
        void AddFlag(Group group,string name,ConfigNode node)
        {
            int mask;if(!int.TryParse(name,NumberStyles.Integer,CultureInfo.InvariantCulture,out mask)||mask<=0)
                throw new InvalidDataException("Некорректный бинарный флаг объекта: "+name);
            var flag=new Flag{name=name,mask=mask,required=node.Text("requiredflag"),
                equal=node.Text("comparemethod","and")=="equal",invert=node.Boolean("invert"),
                // Spelling is part of the original UDB configuration format.
                ignoreUnset=node.Boolean("ingnorethisgroupwhenunset"),
                requiredGroups=Names(node,"requiredgroups"),ignoredGroups=Names(node,"ignoredgroups")};
            group.flags.Add(flag);if(!flags.ContainsKey(name))flags.Add(name,flag);
        }
        Flag Find(Group group,string name)
        {foreach(var flag in group.flags)if(flag.name==name)return flag;return flags[name];}
        public bool Overlap(int first,int second)
        {
            if(groups.Count==0)return true;
            var results=new Dictionary<string,Result>();
            foreach(var group in groups)
            {
                var result=new Result();results.Add(group.name,result);
                foreach(var flag in group.flags)
                {
                    if(flag.required.Length>0&&!Find(group,flag.required).Compare(first,second)){result.value=-1;continue;}
                    bool overlaps=flag.Compare(first,second);
                    if(!overlaps&&flag.ignoreUnset){result.value=0;break;}
                    if(!overlaps)continue;result.value=1;
                    foreach(string name in flag.ignoredGroups)result.ignored.Add(name);
                    foreach(string name in flag.requiredGroups){result.required.Add(name);result.ignored.Remove(name);}
                }
            }
            // Preserve the original dependency processing order.
            foreach(var result in results.Values)
            {
                if(result.value!=1)continue;
                foreach(string name in result.required)if(results[name].value!=1){result.value=0;break;}
                if(result.value==1)foreach(string name in result.ignored)results[name].value=0;
            }
            int total=results.Count,overlapsCount=0;
            foreach(var result in results.Values)
            {if(result.value==1)overlapsCount++;else if(result.value==0)total--;else return false;}
            return total>0&&overlapsCount==total;
        }
        public List<string> UnusedGroups(int value)
        {
            var active=new Dictionary<string,List<Flag>>();var ignored=new HashSet<string>();
            foreach(var group in groups)
            {
                var used=new List<Flag>();active.Add(group.name,used);
                foreach(var flag in group.flags)
                {
                    if(flag.Set(value)&&(flag.required.Length==0||Find(group,flag.required).Set(value)))
                    {used.Add(flag);foreach(string name in flag.ignoredGroups)ignored.Add(name);}
                    else if(flag.ignoreUnset){active.Remove(group.name);break;}
                }
            }
            foreach(var used in active.Values)foreach(var flag in used)foreach(string name in flag.requiredGroups)ignored.Remove(name);
            var missing=new List<string>();
            foreach(var group in groups)
            {List<Flag> used;if(!ignored.Contains(group.name)&&active.TryGetValue(group.name,out used)&&used.Count==0&&!group.optional)missing.Add(group.name);}
            return missing;
        }
    }
}
