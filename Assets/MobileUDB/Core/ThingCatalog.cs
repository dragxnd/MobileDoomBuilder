// GPL-3.0. Thing/category defaults follow UDB ThingCategory and ThingTypeInfo.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace MobileUDB.Core
{
    public sealed class ThingType
    {
        public int id;
        public string title, category, sprite, className;
        public float radius = 10, height = 20, spriteScale = 1, alpha = 1;
        public bool hangs, absoluteZ, fixedRotation, bright;
        public bool fixedSize;
        // UDB: blocking 0=none, 1=full height, 2=own height;
        // error 0=ignore placement, 1=outside, 2=outside and stuck.
        public int blocking, errorCheck;
        // Original editor collision metadata can differ from renderer dimensions,
        // especially DeHackEd's integer-unit width/height and zero-as-no-override.
        public float? checkRadius, checkHeight;
        public int[] argumentTypes = new int[5];
    }
    public sealed partial class ThingCatalog
    {
        readonly Dictionary<int,ThingType> types = new Dictionary<int,ThingType>();
        public readonly List<ThingType> choices = new List<ThingType>();
        public readonly List<string> warnings = new List<string>();
        public bool Contains(int id) {return types.ContainsKey(id);}
        public ThingType Find(int id)
        {
            ThingType type; return types.TryGetValue(id,out type) ? type : new ThingType { id = id,title = "Неизвестный тип " + id,category = "Неизвестные",sprite = "" };
        }
        public static ThingCatalog Load(params string[] configurations)
        {
            var root = new ConfigNode();
            foreach (string text in configurations) root.Merge(GameConfigText.Read(text));
            var catalog = new ThingCatalog(); catalog.Collect(root,new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase),"");
            // The mobile game profiles omit Common.cfg's editor category.
            // Its camera has no gameplay collision/placement checks or sprite.
            if(!catalog.types.ContainsKey(MapCameraMarker.Type))catalog.types.Add(MapCameraMarker.Type,new ThingType
                {id=MapCameraMarker.Type,title="Камера 3D UDB",category="Редактор",sprite="",radius=16,height=0,checkHeight=0,fixedSize=true,errorCheck=0});
            catalog.choices.AddRange(catalog.types.Values);
            catalog.choices.Sort((a,b) => { int c = StringComparer.OrdinalIgnoreCase.Compare(a.category,b.category); return c != 0 ? c : StringComparer.OrdinalIgnoreCase.Compare(a.title,b.title); });
            return catalog;
        }
        void Collect(ConfigNode node, Dictionary<string,string> inherited, string category)
        {
            var values = new Dictionary<string,string>(inherited,StringComparer.OrdinalIgnoreCase);
            foreach (var pair in node.values) values[pair.Key] = pair.Value;
            // UDB also permits the shorthand: 1 = "Player 1 start";
            foreach (var pair in node.values)
            {
                int scalarId; if (int.TryParse(pair.Key,NumberStyles.Integer,CultureInfo.InvariantCulture,out scalarId)) AddType(scalarId,pair.Value,category,values,new int[5]);
            }
            foreach (var pair in node.children)
            {
                if(pair.Key.Length==4&&pair.Key.StartsWith("arg",StringComparison.OrdinalIgnoreCase)&&pair.Key[3]>='0'&&pair.Key[3]<='4')continue;
                int id;
                if (!int.TryParse(pair.Key,NumberStyles.Integer,CultureInfo.InvariantCulture,out id))
                {
                    // Category title is local; sprite/size/placement defaults are inherited.
                    string title = Value(pair.Value.values,"title",pair.Key);
                    Collect(pair.Value,values,string.IsNullOrEmpty(category) ? title : category + " / " + title);
                    continue;
                }
                var properties = new Dictionary<string,string>(values,StringComparer.OrdinalIgnoreCase);
                foreach (var p in pair.Value.values) properties[p.Key] = p.Value;
                AddType(id,Value(pair.Value.values,"title",id.ToString()),category,properties,DimensionArguments(pair.Value));
            }
        }
        static int[] DimensionArguments(ConfigNode node)
        {var args=new int[5];for(int i=0;i<5;i++){ConfigNode arg;if(node.children.TryGetValue("arg"+i,out arg))args[i]=arg.Integer("type",0);}return args;}
        void AddType(int id, string title, string category, Dictionary<string,string> properties,int[] args)
        {
                types[id] = new ThingType { id = id,title = title,category = category,className=Value(properties,"class",""),
                    sprite = Value(properties,"sprite","").ToUpperInvariant(),radius = Math.Max(1,Number(properties,"width",10)),height = Math.Max(1,Number(properties,"height",20)),
                    spriteScale = Math.Max(.001f,Number(properties,"spritescale",1)),alpha = Math.Max(0,Math.Min(1,Number(properties,"alpha",1))),
                    hangs = Bool(properties,"hangs")&&!Bool(properties,"absolutez"),absoluteZ = Bool(properties,"absolutez"),fixedRotation = Bool(properties,"fixedrotation"),fixedSize=Bool(properties,"fixedsize"),argumentTypes=(int[])args.Clone(),
                    blocking=(int)Number(properties,"blocking",0),errorCheck=(int)Number(properties,"error",1),checkHeight=Number(properties,"height",20) };
        }
        static string Value(Dictionary<string,string> d, string key, string fallback) { string value; return d.TryGetValue(key,out value) ? value : fallback; }
        static float Number(Dictionary<string,string> d, string key, float fallback)
        {
            float number; return float.TryParse(Value(d,key,"").TrimEnd('f','F'),NumberStyles.Float,CultureInfo.InvariantCulture,out number) && !float.IsNaN(number) && !float.IsInfinity(number) ? number : fallback;
        }
        static bool Bool(Dictionary<string,string> d, string key) { string value = Value(d,key,"0"); return value == "1" || string.Equals(value,"true",StringComparison.OrdinalIgnoreCase); }
    }
    public static class ThingPlacement
    {
        public static float Height(MapThing thing,ThingType type) { return Dimension(thing,type,type.height,24); }
        public static float Radius(MapThing thing,ThingType type) { return Dimension(thing,type,type.fixedSize||type.radius<4?14:type.radius,23); }
        static float Dimension(MapThing thing,ThingType type,float value,int kind)
        {if(thing.args!=null&&type.argumentTypes!=null)for(int i=0;i<Math.Min(5,Math.Min(thing.args.Length,type.argumentTypes.Length));i++)if(type.argumentTypes[i]==kind&&thing.args[i]>0)value=thing.args[i];return value;}
        public static int Sector(MapDocument map, MapPoint point)
        { for (int i = 0; i < map.sectors.Count; i++) if (map.Contains(i,point)) return i; return -1; }
        public static float BaseHeight(MapDocument map, MapThing thing, ThingType type, int sector)
        {
            float z = map.format == MapFormat.Doom ? 0 : thing.z;
            if (type.absoluteZ) return z;
            if (sector < 0 || sector >= map.sectors.Count) return z;
            return type.hangs ? VisualGeometry.Height(map.sectors[sector],true,thing.Position) - Height(thing,type) - z : VisualGeometry.Height(map.sectors[sector],false,thing.Position) + z;
        }
        public static int Rotation(MapThing thing, MapPoint camera)
        {
            double relative = Math.Atan2(camera.y - thing.y,camera.x - thing.x) * 180 / Math.PI - thing.angle + 22.5;
            return (int)(((relative % 360 + 360) % 360) / 45) + 1;
        }
        public static void Move(MapDocument map, int index, float x, float y, float z)
        {
            var thing = map.things[index];
            if (map.format == MapFormat.Doom && z != 0) throw new InvalidOperationException("Формат Doom не хранит высоту объекта.");
            float nx = thing.x + x, ny = thing.y + y, nz = thing.z + z;
            if (nx < -32768 || nx > 32767 || ny < -32768 || ny > 32767 || nz < -32768 || nz > 32767) throw new InvalidOperationException("Координата объекта выходит за допустимые границы.");
            thing.x = nx; thing.y = ny; thing.z = nz;
        }
    }
}
