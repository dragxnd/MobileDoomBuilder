// GPL-3.0. UDB CheckUnusedThings and CheckStuckThings, binary-map scope.
using System;
using System.Collections.Generic;

namespace MobileUDB.Core
{
    public static class ThingMapChecks
    {
        const double AllowedStuckDistance=6;
        static ElementRef Thing(int index){return new ElementRef(ElementKind.Thing,index);}
        static bool Finite(float value){return !float.IsNaN(value)&&!float.IsInfinity(value);}
        public static IEnumerable<MapIssue> Scan(MapDocument map,ThingCatalog catalog,ThingFlagRules flags,int maximumPairChecks=2000000,bool checkCoordinates=true)
        {
            if(catalog==null)yield break;
            var types=new ThingType[map.things.Count];var radii=new double[map.things.Count];
            for(int i=0;i<map.things.Count;i++)
            {
                var thing=map.things[i];
                if(thing==null||!Finite(thing.x)||!Finite(thing.y)||!Finite(thing.z))
                {if(checkCoordinates)yield return new MapIssue("invalid-thing","Объект #"+i+": некорректные координаты",true,null,Thing(i));continue;}
                var type=types[i]=catalog.Find(thing.type);radii[i]=Radius(thing,type);
                if(flags!=null)
                {
                    var missing=flags.UnusedGroups(thing.flags);
                    if(missing.Count>0)
                    {
                        var names=new List<string>();foreach(string name in missing)
                            names.Add(name=="skills"?"сложность":name=="gamemodes"?"режим игры":name=="classes"?"класс игрока":name);
                        yield return new MapIssue("unused-thing","Объект #"+i+": не появляется — не выбраны параметры: "+string.Join(", ",names.ToArray()),false,thing.Position,Thing(i));
                    }
                }
                if((i&63)==0)yield return null;
            }
            var lines=new ThingCheckIndex(map.lines.Count);var solids=new ThingCheckIndex(map.things.Count);
            for(int j=0;j<map.lines.Count;j++)
            {
                var line=map.lines[j];var a=map.vertices[line.start].Position;var b=map.vertices[line.end].Position;
                lines.Add(j,Math.Min(a.x,b.x),Math.Min(a.y,b.y),Math.Max(a.x,b.x),Math.Max(a.y,b.y));
                if((j&127)==0)yield return null;
            }
            for(int j=0;j<map.things.Count;j++)
            {
                if(types[j]!=null&&types[j].blocking>0)
                {var thing=map.things[j];double radius=Math.Abs(radii[j]-AllowedStuckDistance);solids.Add(j,thing.x-radius,thing.y-radius,thing.x+radius,thing.y+radius);}
                if((j&127)==0)yield return null;
            }
            foreach(var pause in lines.Build())yield return null;
            foreach(var pause in solids.Build())yield return null;
            var hits=new List<int>();
            // Only nearby candidates need exact comparisons. A dense/degenerate
            // map still has cooperative traversal, sorting and an explicit ceiling.
            // Usage/coordinate checks above remain complete if placement is partial.
            long comparisons=0;
            for(int i=0;i<map.things.Count;i++)
            {
                var info=types[i];if(info==null||info.errorCheck<1)continue;
                var thing=map.things[i];bool checkStuck=info.errorCheck==2&&info.blocking>0,stuck=false;
                double radius=Math.Abs(radii[i]-AllowedStuckDistance);
                if(checkStuck)
                {
                    hits.Clear();foreach(int j in lines.Range(thing.x-radius,thing.y-radius,thing.x+radius,thing.y+radius))
                    {if(j<0)yield return null;else hits.Add(j);}
                    foreach(var pause in ThingCheckIndex.SortHits(hits))yield return null;
                    foreach(int j in hits)
                    {
                        var line=map.lines[j];if(line.back>=0&&(line.flags&1)==0)continue;
                        if(++comparisons>maximumPairChecks){yield return Incomplete();yield break;}
                        var a=map.vertices[line.start].Position;var b=map.vertices[line.end].Position;
                        if(HitsSquare(a,b,thing.Position,radius))
                        {stuck=true;yield return new MapIssue("stuck-thing-line","Объект #"+i+": застрял в стене #"+j,true,thing.Position,Thing(i),new ElementRef(ElementKind.Line,j));}
                        if((comparisons&127)==0)yield return null;
                    }
                    hits.Clear();foreach(int j in solids.Range(thing.x-radius,thing.y-radius,thing.x+radius,thing.y+radius))
                    {if(j<0)yield return null;else hits.Add(j);}
                    foreach(var pause in ThingCheckIndex.SortHits(hits))yield return null;
                    foreach(int j in hits)
                    {
                        var otherInfo=types[j];
                        if(i==j||otherInfo==null||otherInfo.blocking==0)continue;
                        // Each pair is reported once, at its first eligible outer
                        // object. A non-checkable but solid object can still block it.
                        if(j<i&&otherInfo.errorCheck==2&&otherInfo.blocking>0)continue;
                        if(++comparisons>maximumPairChecks){yield return Incomplete();yield break;}
                        var other=map.things[j];
                        if(ThingsOverlap(thing,info,radii[i],other,otherInfo,radii[j])&&(flags==null||flags.Overlap(thing.flags,other.flags)))
                        {stuck=true;yield return new MapIssue("stuck-thing-thing","Объекты #"+i+" / #"+j+": перекрываются и появляются одновременно",true,thing.Position,Thing(i),Thing(j));}
                        if((comparisons&127)==0)yield return null;
                    }
                }
                if(!stuck)
                {
                    int nearest=-1;double distance=double.MaxValue;
                    foreach(int j in lines.Nearest(thing.Position,()=>distance))
                    {
                        if(j<0){yield return null;continue;}
                        if(++comparisons>maximumPairChecks){yield return Incomplete();yield break;}
                        var line=map.lines[j];var a=map.vertices[line.start].Position;var b=map.vertices[line.end].Position;
                        double candidate=SafeDistanceSquared(a,b,thing.Position);
                        // Original enumeration gives the first linedef an equal-distance tie.
                        if(candidate<distance||(candidate==distance&&(nearest<0||j<nearest))){distance=candidate;nearest=j;}
                        if((comparisons&127)==0)yield return null;
                    }
                    bool outside=nearest<0;
                    if(nearest>=0)
                    {
                        var line=map.lines[nearest];var a=map.vertices[line.start].Position;var b=map.vertices[line.end].Position;
                        double side=((double)thing.y-a.y)*((double)b.x-a.x)-((double)thing.x-a.x)*((double)b.y-a.y);
                        outside=side<=0?line.front<0:line.back<0;
                    }
                    // Empty maps have no nearest line; report placement, not a null dereference.
                    if(outside)yield return new MapIssue("outside-thing","Объект #"+i+": находится вне карты",true,thing.Position,Thing(i));
                }
                yield return null;
            }
        }
        static MapIssue Incomplete()
        {return new MapIssue("incomplete-things","Проверка размещения объектов остановлена на пределе сложности. Флаги проверены; часть столкновений / объектов вне карты ещё не проверена.",false,null);}
        // Source uses raw stored Z and type height, not rendered/world floor-relative
        // height or the height-argument override, and full-height blockers ignore Z.
        public static bool ThingsOverlap(MapThing a,ThingType ai,double ar,MapThing b,ThingType bi,double br)
        {
            ar-=AllowedStuckDistance;br-=AllowedStuckDistance;
            if(a.x+ar<b.x-br||a.x-ar>b.x+br||a.y-ar>b.y+br||a.y+ar<b.y-br)return false;
            return ai.blocking==1||bi.blocking==1||!(a.z>(double)b.z+(bi.checkHeight??bi.height)||(double)a.z+(ai.checkHeight??ai.height)<b.z);
        }
        static float Radius(MapThing thing,ThingType type)
        {
            float size=type.checkRadius??(type.fixedSize||type.radius<4?14:type.radius);
            if(thing.args!=null&&type.argumentTypes!=null)for(int i=0;i<Math.Min(5,Math.Min(thing.args.Length,type.argumentTypes.Length));i++)
                if(type.argumentTypes[i]==23&&thing.args[i]>0)size=thing.args[i];
            return size;
        }
        static double SafeDistanceSquared(MapPoint a,MapPoint b,MapPoint point)
        {
            double dx=(double)b.x-a.x,dy=(double)b.y-a.y,length=dx*dx+dy*dy;
            if(length==0){dx=(double)point.x-a.x;dy=(double)point.y-a.y;return dx*dx+dy*dy;}
            double t=(((double)point.x-a.x)*dx+((double)point.y-a.y)*dy)/length,inverse=1/Math.Sqrt(length);
            t=inverse>1?Math.Max(0,Math.Min(1,t)):Math.Max(inverse,Math.Min(1-inverse,t));
            dx=point.x-(a.x+t*dx);dy=point.y-(a.y+t*dy);return dx*dx+dy*dy;
        }
        static bool Clip(double start,double delta,double min,double max,ref double low,ref double high)
        {
            if(delta==0)return start>=min&&start<=max;
            double a=(min-start)/delta,b=(max-start)/delta;if(a>b){double swap=a;a=b;b=swap;}
            low=Math.Max(low,a);high=Math.Min(high,b);return low<=high;
        }
        static bool HitsSquare(MapPoint a,MapPoint b,MapPoint center,double radius)
        {
            // Correct UDB's reversed Y containment comparisons: a short blocking
            // segment wholly inside the square must also be found.
            radius=Math.Abs(radius);double low=0,high=1;
            return Clip(a.x,(double)b.x-a.x,center.x-radius,center.x+radius,ref low,ref high)&&
                Clip(a.y,(double)b.y-a.y,center.y-radius,center.y+radius,ref low,ref high);
        }
    }
}
