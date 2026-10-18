using System;
namespace MobileUDB.Core
{
    public sealed partial class MapDocument
    {
        public int InsertVertexOnLine(int index,MapPoint point,DoomResources resources=null)
        {
            ThingClipboard.Binary(this);
            if(index<0||index>=lines.Count)throw new InvalidOperationException("Стена больше не существует.");
            var line=lines[index];var a=vertices[line.start].Position;var b=vertices[line.end].Position;
            double dx=b.x-(double)a.x,dy=b.y-(double)a.y,length=dx*dx+dy*dy;
            if(length==0||float.IsNaN(point.x)||float.IsNaN(point.y)||float.IsInfinity(point.x)||float.IsInfinity(point.y))throw new InvalidOperationException("Некорректная точка стены.");
            double t=Math.Max(0,Math.Min(1,((point.x-a.x)*dx+(point.y-a.y)*dy)/length));
            point=new MapPoint((float)Math.Round(a.x+t*dx),(float)Math.Round(a.y+t*dy));
            if((point-a).LengthSquared<.01f||(point-b).LengthSquared<.01f)throw new InvalidOperationException("Здесь уже находится конец стены.");
            if(point.x< -32768||point.x>32767||point.y< -32768||point.y>32767)throw new InvalidOperationException("Вершина вне бинарных координат.");
            int vertex=FindOrAddVertex(point);SplitLineAt(index,vertex,resources);return vertex;
        }
    }
}
