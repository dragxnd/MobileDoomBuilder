// GPL-3.0. Binary automap semantics from UDB AutomapMode (Boris Iwanski).
using System;
namespace MobileUDB.Core
{
    public enum AutomapLineStyle { Hidden, SingleSided, FloorDifference, CeilingDifference, MatchingHeight }
    public enum AutomapLineAction { ToggleVisibility, ToggleSingleSided, VisibleSingleSided, VisibleTwoSided }
    public static class AutomapLines
    {
        public const int HiddenFlag=128,SingleSidedFlag=32;
        public static int ApplyAction(int flags,AutomapLineAction action)
        {
            switch(action)
            {
                case AutomapLineAction.ToggleVisibility:return flags^HiddenFlag;
                case AutomapLineAction.ToggleSingleSided:return flags^SingleSidedFlag;
                case AutomapLineAction.VisibleSingleSided:
                case AutomapLineAction.VisibleTwoSided:
                    int type=action==AutomapLineAction.VisibleSingleSided?SingleSidedFlag:0;
                    bool matching=(flags&(HiddenFlag|SingleSidedFlag))==type;
                    // Set both automap flags in one edit. A repeated tap on the
                    // requested visible state hides it; the next tap restores it.
                    return (flags&~(HiddenFlag|SingleSidedFlag))|type|(matching?HiddenFlag:0);
                default:throw new ArgumentOutOfRangeException("action");
            }
        }
        public static AutomapLineStyle Style(MapDocument map,int index)
        {
            var line=map.lines[index];
            if((line.flags&HiddenFlag)!=0)return AutomapLineStyle.Hidden;
            if(line.front<0||line.back<0||(line.flags&SingleSidedFlag)!=0)return AutomapLineStyle.SingleSided;
            var front=map.sectors[map.sides[line.front].sector];var back=map.sectors[map.sides[line.back].sector];
            if(front.floor!=back.floor)return AutomapLineStyle.FloorDifference;
            if(front.ceiling!=back.ceiling)return AutomapLineStyle.CeilingDifference;
            return AutomapLineStyle.MatchingHeight;
        }
        public static bool Visible(MapDocument map,int index,bool showAll)
        {var style=Style(map,index);return showAll||style!=AutomapLineStyle.Hidden&&style!=AutomapLineStyle.MatchingHeight;}
        public static int Hit(MapDocument map,MapPoint point,float radius,bool showAll)
        {
            int best=-1;double nearest=(double)radius*radius;
            for(int i=0;i<map.lines.Count;i++)
            {
                if(!Visible(map,i,showAll))continue;var line=map.lines[i];var a=map.vertices[line.start].Position;var b=map.vertices[line.end].Position;
                double dx=b.x-(double)a.x,dy=b.y-(double)a.y,length=dx*dx+dy*dy;
                double t=length==0?0:Math.Max(0,Math.Min(1,((point.x-a.x)*dx+(point.y-a.y)*dy)/length));
                double x=point.x-a.x-t*dx,y=point.y-a.y-t*dy,distance=x*x+y*y;
                if(distance<=nearest){nearest=distance;best=i;}
            }
            return best;
        }
    }
}
