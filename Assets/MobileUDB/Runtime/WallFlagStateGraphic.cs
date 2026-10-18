using UnityEngine;
using UnityEngine.UI;
namespace MobileUDB
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class WallFlagStateGraphic : MaskableGraphic
    {
        int state=-2;
        public int State {get{return state;}set{if(state==value)return;state=value;SetVerticesDirty();}}
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();var r=rectTransform.rect;var min=new Vector2(r.xMin+1,r.yMin+1);var max=new Vector2(r.xMax-1,r.yMax-1);
            Stroke(mesh,min,new Vector2(min.x,max.y));Stroke(mesh,new Vector2(min.x,max.y),max);Stroke(mesh,max,new Vector2(max.x,min.y));Stroke(mesh,new Vector2(max.x,min.y),min);
            if(state==-1)Stroke(mesh,new Vector2(r.xMin+5,r.center.y),new Vector2(r.xMax-5,r.center.y));
            else if(state==1){Stroke(mesh,new Vector2(r.xMin+4,r.center.y),new Vector2(r.xMin+8,r.yMin+5));Stroke(mesh,new Vector2(r.xMin+8,r.yMin+5),new Vector2(r.xMax-4,r.yMax-5));}
        }
        void Stroke(VertexHelper mesh,Vector2 a,Vector2 b)
        {var d=new Vector2(a.y-b.y,b.x-a.x).normalized;int n=mesh.currentVertCount;mesh.AddVert(a-d,color,Vector2.zero);mesh.AddVert(a+d,color,Vector2.zero);mesh.AddVert(b+d,color,Vector2.zero);mesh.AddVert(b-d,color,Vector2.zero);mesh.AddTriangle(n,n+1,n+2);mesh.AddTriangle(n,n+2,n+3);}
    }
}
