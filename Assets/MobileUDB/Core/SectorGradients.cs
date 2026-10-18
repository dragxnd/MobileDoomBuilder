// GPL-3.0. Binary SectorsMode gradients and InterpolationTools sine formulas.
using System;
using System.Collections.Generic;

namespace MobileUDB.Core
{
    public enum SectorGradientTarget { Brightness, Floor, Ceiling }
    public enum GradientInterpolation { Linear, EaseInOutSine, EaseInSine, EaseOutSine }
    public static class SectorGradients
    {
        public static double Interpolate(double start,double end,double delta,GradientInterpolation mode)
        {
            switch(mode)
            {
                case GradientInterpolation.Linear:return delta*end+(1.0-delta)*start;
                case GradientInterpolation.EaseInOutSine:return -(end-start)/2*(Math.Cos(Math.PI*delta)-1)+start;
                case GradientInterpolation.EaseInSine:return -(end-start)*Math.Cos(delta*Math.PI*.5)+(end-start)+start;
                case GradientInterpolation.EaseOutSine:return (end-start)*Math.Sin(delta*Math.PI*.5)+start;
                default:throw new ArgumentOutOfRangeException("mode");
            }
        }
        internal static double Value(MapSector sector,SectorGradientTarget target)
        {return target==SectorGradientTarget.Brightness?sector.light:target==SectorGradientTarget.Floor?sector.floor:sector.ceiling;}
        static void Coordinate(double value)
        {if(double.IsNaN(value)||double.IsInfinity(value)||value<-32768||value>32767)throw new InvalidOperationException("Значение градиента выходит за границы Doom / Hexen.");}
        public static SectorGradientPlan Plan(MapDocument map,IEnumerable<int> selected,SectorGradientTarget target,GradientInterpolation mode)
        {
            ThingClipboard.Binary(map);if(selected==null)throw new ArgumentNullException("selected");
            if(!Enum.IsDefined(typeof(SectorGradientTarget),target)||!Enum.IsDefined(typeof(GradientInterpolation),mode))throw new InvalidOperationException("Неизвестный вид градиента.");
            var order=new List<int>();var seen=new HashSet<int>();
            foreach(int index in selected)
            {if(index<0||index>=map.sectors.Count||map.sectors[index]==null)throw new InvalidOperationException("Выделенный сектор больше не существует.");if(seen.Add(index))order.Add(index);}
            if(order.Count<3)throw new InvalidOperationException("Выберите не менее трёх секторов по порядку.");
            var plan=new SectorGradientPlan(map,target);double start=Value(map.sectors[order[0]],target),end=Value(map.sectors[order[order.Count-1]],target);Coordinate(start);Coordinate(end);
            for(int i=0;i<order.Count;i++)
            {
                int index=order[i];var sector=map.sectors[index];double old=Value(sector,target);Coordinate(old);
                // Source uses double brightness fractions, float height fractions,
                // Math.Round (ties to even), and fixed endpoint values.
                double fraction=target==SectorGradientTarget.Brightness?i/(double)(order.Count-1):(double)(i/(float)(order.Count-1));
                double value=i==0||i==order.Count-1?old:Math.Round(Interpolate(start,end,fraction,mode));Coordinate(value);
                plan.Add(index,sector,old,value);
            }
            return plan;
        }
    }
    public sealed class SectorGradientPlan
    {
        struct Write {public int index;public MapSector sector;public double before,after;}
        readonly MapDocument original;readonly MapFormat format;readonly SectorGradientTarget target;
        readonly List<Write> writes=new List<Write>();bool applied;
        public int ChangeCount {get;private set;}
        internal SectorGradientPlan(MapDocument map,SectorGradientTarget target){original=map;format=map.format;this.target=target;}
        internal void Add(int index,MapSector sector,double before,double after)
        {writes.Add(new Write{index=index,sector=sector,before=before,after=after});if(before!=after)ChangeCount++;}
        public void Apply(MapDocument map)
        {
            if(applied||!ReferenceEquals(map,original)||map.format!=format)throw new InvalidOperationException("Карта изменилась. Откройте градиенты снова.");
            foreach(var write in writes)if(write.index>=map.sectors.Count||!ReferenceEquals(map.sectors[write.index],write.sector)||SectorGradients.Value(write.sector,target)!=write.before)
                throw new InvalidOperationException("Секторы изменились. Откройте градиенты снова.");
            applied=true;foreach(var write in writes)if(write.before!=write.after)
            {if(target==SectorGradientTarget.Brightness)write.sector.light=(int)write.after;else if(target==SectorGradientTarget.Floor)write.sector.floor=(float)write.after;else write.sector.ceiling=(float)write.after;}
        }
    }
}
