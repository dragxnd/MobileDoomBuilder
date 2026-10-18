// GPL-3.0. MBF p_spec.c / dsda-doom r_plane.c: static sky-transfer preview.
using System;
using System.Collections.Generic;

namespace MobileUDB.Core
{
    public sealed class SkyTransfer
    {
        public int line, side, action, offsetX, offsetY;
        public string texture;
        // 271 reverses renderer columns; 272 keeps the normal Doom sky orientation.
        public bool ReverseColumns { get { return action == 271; } }
        public double AngleTurns { get { return offsetX / 65536.0; } }
        public int HorizonRow { get { return offsetY - 28; } }
    }
    public static class SkyTransfers
    {
        public static Dictionary<int,SkyTransfer> Build(MapDocument map, bool enabled)
        {
            var result = new Dictionary<int,SkyTransfer>();
            if (!enabled || map.format != MapFormat.Doom) return result;
            // Spawn order is linedef order: the last matching transfer wins, including tag 0.
            for (int i = 0; i < map.lines.Count; i++)
            {
                var line = map.lines[i];
                if ((line.action != 271 && line.action != 272) || line.front < 0 || line.front >= map.sides.Count) continue;
                var side = map.sides[line.front];
                result[line.tag] = new SkyTransfer { line = i,side = line.front,action = line.action,
                    texture = side.upper,offsetX = side.offsetX,offsetY = side.offsetY };
            }
            return result;
        }
        public static string DefaultTexture(MapDocument map)
        {
            if(map.sky!=null)return map.sky.Texture(map.name);
            string name = (map.name ?? "").ToUpperInvariant();
            if (name.Length >= 2 && name[0] == 'E' && name[1] >= '1' && name[1] <= '4') return "SKY" + name[1];
            int number; if (name.StartsWith("MAP") && int.TryParse(name.Substring(3),out number)) return number <= 11 ? "SKY1" : number <= 20 ? "SKY2" : "SKY3";
            return "SKY1";
        }
    }
}
