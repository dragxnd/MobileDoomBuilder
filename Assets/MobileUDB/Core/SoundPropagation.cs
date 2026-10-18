// GPL-3.0. Binary rules from UDB SoundPropagationDomain / SoundPropagationMode.
using System;
using System.Collections.Generic;
namespace MobileUDB.Core
{
    // Immutable adjacency snapshot. Impassable is unrelated to sound; closed openings block it.
    public sealed class SoundPropagation
    {
        public const int BlockSoundFlag=64;
        public const byte Free=0,BehindBlock=1,Unreachable=2;
        struct Edge {public int sector;public bool blocked;public Edge(int sector,bool blocked){this.sector=sector;this.blocked=blocked;}}
        readonly List<Edge>[] neighbors;
        public SoundPropagation(MapDocument map)
        {
            if(map==null)throw new ArgumentNullException("map");neighbors=new List<Edge>[map.sectors.Count];
            for(int i=0;i<neighbors.Length;i++)neighbors[i]=new List<Edge>();
            foreach(var line in map.lines)
            {
                if(line.front<0||line.back<0)continue;
                int a=map.sides[line.front].sector,b=map.sides[line.back].sector;
                if(a==b||BlockedByHeight(map.sectors[a],map.sectors[b]))continue;
                bool blocked=(line.flags&BlockSoundFlag)!=0;
                neighbors[a].Add(new Edge(b,blocked));neighbors[b].Add(new Edge(a,blocked));
            }
        }
        public static bool BlockedByHeight(MapSector a,MapSector b)
        {return a.ceiling<=b.floor||a.floor>=b.ceiling||a.ceiling<=a.floor||b.ceiling<=b.floor;}
        public byte[] Levels(int source)
        {
            var levels=new byte[neighbors.Length];for(int i=0;i<levels.Length;i++)levels[i]=Unreachable;
            if(source<0||source>=levels.Length)return levels;
            var pending=new Queue<int>();levels[source]=Free;pending.Enqueue(source);Flood(Free);
            // Finish the whole free domain first: an alternate open route must win over a blocked one.
            for(int i=0;i<levels.Length;i++)if(levels[i]==Free)
                foreach(var edge in neighbors[i])if(edge.blocked&&levels[edge.sector]==Unreachable)
                {levels[edge.sector]=BehindBlock;pending.Enqueue(edge.sector);}
            Flood(BehindBlock);return levels;
            void Flood(byte level)
            {
                while(pending.Count>0)
                {
                    int sector=pending.Dequeue();
                    foreach(var edge in neighbors[sector])if(!edge.blocked&&levels[edge.sector]==Unreachable)
                    {levels[edge.sector]=level;pending.Enqueue(edge.sector);}
                }
            }
        }
    }
}
