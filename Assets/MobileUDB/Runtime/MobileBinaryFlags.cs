using System.Collections.Generic;
using MobileUDB.Core;

namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        Dictionary<int,string> BinaryFlagNames(bool line,bool single=false)
        {
            var flags=new Dictionary<int,string>();
            string[] names=line?new[]{"Непроходимая","Блокирует монстров","Двусторонняя","Не крепить верх","Не крепить низ","Секрет","Блокирует звук","Скрыта на карте","Показана на карте"}:
                new[]{"Лёгкая сложность","Средняя сложность","Высокая сложность","Засадный"};
            for(int i=0;i<names.Length;i++)flags[1<<i]=names[i];
            string profile=Map.format==MapFormat.Hexen?"Hexen":effectiveProfile=="Doom2"?"Doom":effectiveProfile;
            var configs=new List<string>{profile+"_misc.cfg"};
            if(Actions.boomCompatible)configs.Add("Boom_misc.cfg");
            if(Actions.mbf)configs.Add("MBF_misc.cfg");
            if(Actions.mbf21)configs.Add("MBF21_misc.cfg");
            if(Actions.extended)configs.Add("ZDoom_misc.cfg");
            foreach(string config in configs)
                foreach(var entry in GameConfigText.Read(ActionConfig(config),ActionConfig).Section(line?"linedefflags":"thingflags").values)
                {
                    int bit;if(!int.TryParse(entry.Key,out bit)||bit<=0||bit>65535||(bit&(bit-1))!=0)continue;
                    if(line&&Map.format==MapFormat.Hexen&&((bit&7168)!=0||(single&&(bit==512||bit==8192||bit==32768))))continue;
                    flags[bit]=entry.Value;
                }
            return flags;
        }
    }
}
