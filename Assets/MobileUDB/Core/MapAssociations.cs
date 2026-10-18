// GPL-3.0. Binary association rules follow UDB Association and Linedef.IsAssociatedWith.
using System;using System.Collections.Generic;
namespace MobileUDB.Core
{
    public struct MapAssociationEdge : IEquatable<MapAssociationEdge>
    {
        public ElementRef source,target;
        public MapAssociationEdge(ElementRef source,ElementRef target){this.source=source;this.target=target;}
        public bool Equals(MapAssociationEdge other){return source.Equals(other.source)&&target.Equals(other.target);}
        public override bool Equals(object other){return other is MapAssociationEdge&&Equals((MapAssociationEdge)other);}
        public override int GetHashCode(){return source.GetHashCode()*397^target.GetHashCode();}
    }
    public sealed class MapAssociationPlan
    {
        public readonly HashSet<ElementRef> related=new HashSet<ElementRef>();
        public readonly List<MapAssociationEdge> edges=new List<MapAssociationEdge>();
        public readonly Dictionary<ElementRef,MapPoint> positions=new Dictionary<ElementRef,MapPoint>();
        public readonly List<MapTriangle> sectorFill=new List<MapTriangle>();
        readonly HashSet<MapAssociationEdge> distinct=new HashSet<MapAssociationEdge>();
        void Add(ElementRef source,ElementRef target,ElementRef focus)
        {if(source.Equals(target))return;var edge=new MapAssociationEdge(source,target);if(!distinct.Add(edge))return;edges.Add(edge);if(!source.Equals(focus))related.Add(source);if(!target.Equals(focus))related.Add(target);}
        public static MapAssociationPlan Build(MapDocument map,ActionCatalog actions,ElementRef focus)
        {
            var plan=new MapAssociationPlan();int tag=Tag(map,actions,focus);bool doom=map.format==MapFormat.Doom;
            if(doom)
            {
                if(tag>0&&focus.kind==ElementKind.Line)
                {
                    for(int i=0;i<map.sectors.Count;i++)if(map.sectors[i].tag==tag)plan.Add(focus,new ElementRef(ElementKind.Sector,i),focus);
                    var info=actions.Find(map.lines[focus.index].action);
                    if(map.lines[focus.index].action>0&&info.lineToLineTag)for(int i=0;i<map.lines.Count;i++)if(map.lines[i].tag==tag&&(!info.lineToLineSameAction||map.lines[i].action==info.id))plan.Add(focus,new ElementRef(ElementKind.Line,i),focus);
                }
                if(tag>0&&(focus.kind==ElementKind.Sector||focus.kind==ElementKind.Line))for(int i=0;i<map.lines.Count;i++)if(map.lines[i].tag==tag)
                {
                    var info=actions.Find(map.lines[i].action);
                    if(focus.kind==ElementKind.Sector||(map.lines[i].action>0&&info.lineToLineTag&&(!info.lineToLineSameAction||map.lines[focus.index].action==info.id)))plan.Add(new ElementRef(ElementKind.Line,i),focus,focus);
                }
            }
            else if(map.format==MapFormat.Hexen)
            {
                for(int i=0;i<map.lines.Count;i++)plan.ActionLinks(map,actions,new ElementRef(ElementKind.Line,i),focus,tag);
                for(int i=0;i<map.things.Count;i++)plan.ActionLinks(map,actions,new ElementRef(ElementKind.Thing,i),focus,tag);
            }
            if(plan.edges.Count==0)return plan;
            var points=new HashSet<ElementRef>(plan.related);points.Add(focus);SectorTessellation tessellation=null;
            foreach(var item in points)
            {
                if(item.kind==ElementKind.Line){var line=map.lines[item.index];plan.positions[item]=new MapPoint((map.vertices[line.start].x+map.vertices[line.end].x)*.5f,(map.vertices[line.start].y+map.vertices[line.end].y)*.5f);}
                else if(item.kind==ElementKind.Thing)plan.positions[item]=map.things[item.index].Position;
                else if(item.kind==ElementKind.Sector)
                {
                    MapPoint center;try{center=MapTransforms.Center(map,new HashSet<ElementRef>{item});}catch(InvalidOperationException){center=new MapPoint(0,0);}var label=center;
                    if(tessellation==null)tessellation=new SectorTessellation(map);
                    try
                    {
                        var triangles=tessellation.Triangles(item.index);float closest=float.MaxValue;
                        foreach(var triangle in triangles)
                        {
                            var p=new MapPoint((triangle.a.x+triangle.b.x+triangle.c.x)/3,(triangle.a.y+triangle.b.y+triangle.c.y)/3);float distance=(p-center).LengthSquared;
                            if(distance<closest){closest=distance;label=p;}
                            if(plan.related.Contains(item))plan.sectorFill.Add(triangle);
                        }
                        if(map.Contains(item.index,center))label=center;
                    }
                    catch(InvalidOperationException){/* Broken contours still retain their outline association. */}
                    plan.positions[item]=label;
                }
            }
            return plan;
        }
        static int Tag(MapDocument map,ActionCatalog actions,ElementRef item)
        {
            if(item.kind==ElementKind.Sector)return map.sectors[item.index].tag;
            if(item.kind==ElementKind.Thing)return map.format==MapFormat.Doom?0:map.things[item.index].tag;
            if(item.kind!=ElementKind.Line)return 0;
            if(map.format==MapFormat.Doom)return map.lines[item.index].tag;
            var encoding=HexenLineTags.Encoding(map.lines[item.index],actions);return encoding!=null&&encoding.type==15?encoding.Read(map.lines[item.index]):0;
        }
        static ElementKind? ArgumentKind(int type){return type==13?ElementKind.Sector:type==14?ElementKind.Thing:type==15?(ElementKind?)ElementKind.Line:null;}
        void ActionLinks(MapDocument map,ActionCatalog actions,ElementRef source,ElementRef focus,int focusTag)
        {
            int action=source.kind==ElementKind.Line?map.lines[source.index].action:map.things[source.index].action;
            if(action<=0)return;var info=actions.Find(action);var args=source.kind==ElementKind.Line?map.lines[source.index].args:map.things[source.index].args;
            var encoding=source.kind==ElementKind.Line?HexenLineTags.Encoding(map.lines[source.index],actions):null;int skip=encoding==null?0:encoding.ArgumentMask;
            for(int arg=0;arg<5;arg++)
            {
                var kind=ArgumentKind(info.arguments[arg].type);int value=args[arg];if(!info.arguments[arg].used||!kind.HasValue||value<=0||(skip&(1<<arg))!=0)continue;
                if(source.Equals(focus))
                {
                    int count=kind.Value==ElementKind.Sector?map.sectors.Count:kind.Value==ElementKind.Line?map.lines.Count:map.things.Count;
                    for(int i=0;i<count;i++){var target=new ElementRef(kind.Value,i);if(Tag(map,actions,target)==value)Add(source,target,focus);}
                }
                else if(kind.Value==focus.kind&&focusTag>0&&value==focusTag)Add(source,focus,focus);
            }
        }
    }
}
