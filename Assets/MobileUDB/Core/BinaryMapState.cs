// Exact binary/editor snapshot for read-only plan guards.
using System;
using System.Collections.Generic;
using System.IO;
namespace MobileUDB.Core
{
    internal static class BinaryMapState
    {
        static void Text(BinaryWriter w,string value){w.Write(value!=null);if(value!=null)w.Write(value);}
        static void Fields(BinaryWriter w,List<UniversalField> fields)
        {w.Write(fields==null?-1:fields.Count);if(fields!=null)foreach(var f in fields){Text(w,f.name);Text(w,f.value);}}
        static void Blocks(BinaryWriter w,List<string> blocks)
        {w.Write(blocks==null?-1:blocks.Count);if(blocks!=null)foreach(var s in blocks)Text(w,s);}
        static void Extra(BinaryWriter w,MapElementData data)
        {var s=data as SelectableMapElementData;if(s!=null)w.Write(s.selectionGroups);Fields(w,data.fields);Blocks(w,data.extraBlocks);}
        static void Args(BinaryWriter w,int[] args){w.Write(args.Length);foreach(int n in args)w.Write(n);}
        // Exact stale-plan guard includes raw coordinates and editor metadata, without
        // rebuilding nodes or serializing a second project clone during preview.
        public static byte[] Capture(MapDocument map)
        {
            using(var stream=new MemoryStream())using(var w=new BinaryWriter(stream))
            {
                w.Write(map.version);Text(w,map.name);w.Write((int)map.format);Text(w,map.udmfNamespace);Fields(w,map.udmfFields);Blocks(w,map.udmfBlocks);
                Text(w,map.sky==null?null:map.sky.profile);Text(w,GameSky.FlatName(map));Text(w,map.sky==null?null:map.sky.Texture(map.name));
                w.Write(map.tagLabels==null?-1:map.tagLabels.Count);if(map.tagLabels!=null)foreach(var label in map.tagLabels){w.Write(label.tag);Text(w,label.label);}
                w.Write(map.vertices.Count);foreach(var v in map.vertices){w.Write(v.x);w.Write(v.y);Extra(w,v);}
                w.Write(map.lines.Count);foreach(var l in map.lines){w.Write(l.start);w.Write(l.end);w.Write(l.front);w.Write(l.back);w.Write(l.firstSideOrder);w.Write(l.flags);w.Write(l.action);w.Write(l.tag);Args(w,l.args);Extra(w,l);}
                w.Write(map.sides.Count);foreach(var s in map.sides){w.Write(s.sector);w.Write(s.offsetX);w.Write(s.offsetY);Text(w,s.upper);Text(w,s.middle);Text(w,s.lower);Extra(w,s);}
                w.Write(map.sectors.Count);foreach(var s in map.sectors){w.Write(s.floor);w.Write(s.ceiling);w.Write(s.light);w.Write(s.special);w.Write(s.tag);Text(w,s.floorTexture);Text(w,s.ceilingTexture);Extra(w,s);}
                w.Write(map.things.Count);foreach(var t in map.things){w.Write(t.x);w.Write(t.y);w.Write(t.z);w.Write(t.angle);w.Write(t.type);w.Write(t.flags);w.Write(t.tag);w.Write(t.action);w.Write(t.udmfInitialized);Args(w,t.args);Extra(w,t);}
                w.Write(map.auxiliary.Count);foreach(var lump in map.auxiliary){Text(w,lump.name);w.Write(lump.data==null?-1:lump.data.Length);if(lump.data!=null)w.Write(lump.data);}
                w.Flush();return stream.ToArray();
            }
        }
    }
}

