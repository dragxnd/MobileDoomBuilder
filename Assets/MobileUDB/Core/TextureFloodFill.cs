using System;
using System.Collections.Generic;

namespace MobileUDB.Core
{
    // Binary-map counterpart of BuilderModesTools.FloodfillTextures / Tools.FloodfillFlats.
    // Plan against the rendered wall parts before applying the replacement texture.
    public static class TextureFloodFill
    {
        struct WallJob { public int role; public bool forward; }
        static bool Same(string a,string b) { return string.Equals(a,b,StringComparison.OrdinalIgnoreCase); }
        public static List<SurfaceRef> Targets(MapDocument map,SurfaceRef start,IEnumerable<SurfaceRef> visible,IEnumerable<SurfaceRef> selection,bool restrictToSelection=false)
        {
            if(!SurfaceTextureEdits.Valid(map,start))throw new InvalidOperationException("Выберите существующую поверхность.");
            var selected=new HashSet<SurfaceRef>();
            if(selection!=null)foreach(var id in selection)
            {if(!SurfaceTextureEdits.Valid(map,id))throw new InvalidOperationException("Выделение устарело. Выберите поверхности снова.");selected.Add(id);}
            string original=VisualGeometry.Texture(map,start);
            bool restricted=selected.Count>1||restrictToSelection;
            if(start.IsFlat)return Flats(map,start,original,selected,restricted);
            if(visible==null)throw new ArgumentNullException("visible");
            var allowed=new HashSet<int>();foreach(var id in selected)if(!id.IsFlat)allowed.Add(id.line);
            var parts=new Dictionary<int,List<SurfaceRef>>();
            foreach(var id in visible)
            {
                if(!SurfaceTextureEdits.Valid(map,id))throw new InvalidOperationException("Геометрия устарела. Выберите поверхность снова.");
                if(id.IsFlat||!Same(VisualGeometry.Texture(map,id),original)||(restricted&&!allowed.Contains(id.line)))continue;
                int role=id.line*2+(map.lines[id.line].front==id.side?0:1);List<SurfaceRef> list;
                if(!parts.TryGetValue(role,out list)){list=new List<SurfaceRef>();parts[role]=list;}if(!list.Contains(id))list.Add(id);
            }
            var atVertex=new Dictionary<int,List<int>>();
            for(int i=0;i<map.lines.Count;i++)
            {var line=map.lines[i];Add(atVertex,line.start,i);if(line.end!=line.start)Add(atVertex,line.end,i);}
            var todo=new Stack<WallJob>();todo.Push(new WallJob{role=start.line*2+(map.lines[start.line].front==start.side?0:1),forward=true});
            var visited=new HashSet<int>();var result=new List<SurfaceRef>();
            while(todo.Count>0)
            {
                var job=todo.Pop();int lineIndex=job.role/2;List<SurfaceRef> matching;
                if(visited.Contains(lineIndex)||!parts.TryGetValue(job.role,out matching))continue;
                visited.Add(lineIndex);result.AddRange(matching);var line=map.lines[lineIndex];bool front=(job.role&1)==0;
                int left=front?line.start:line.end,right=front?line.end:line.start;
                PushWalls(map,atVertex,parts,visited,todo,job.forward?right:left,job.forward);
                PushWalls(map,atVertex,parts,visited,todo,job.forward?left:right,!job.forward);
            }
            return result;
        }
        static void Add(Dictionary<int,List<int>> links,int key,int value)
        {List<int> list;if(!links.TryGetValue(key,out list)){list=new List<int>();links[key]=list;}list.Add(value);}
        static void PushWalls(MapDocument map,Dictionary<int,List<int>> links,Dictionary<int,List<SurfaceRef>> parts,HashSet<int> visited,Stack<WallJob> todo,int vertex,bool forward)
        {
            List<int> lines;if(!links.TryGetValue(vertex,out lines))return;
            foreach(int index in lines)
            {
                if(visited.Contains(index))continue;var line=map.lines[index];
                bool front=line.start==vertex?forward:!forward;int role=index*2+(front?0:1);
                if(parts.ContainsKey(role))todo.Push(new WallJob{role=role,forward=forward});
            }
        }
        static List<SurfaceRef> Flats(MapDocument map,SurfaceRef start,string original,HashSet<SurfaceRef> selection,bool restricted)
        {
            var allowed=new HashSet<int>();foreach(var id in selection)if(id.IsFlat)allowed.Add(id.sector);
            var links=new Dictionary<int,List<int>>();
            foreach(var line in map.lines)if(line.front>=0&&line.back>=0)
            {int a=map.sides[line.front].sector,b=map.sides[line.back].sector;if(a!=b){Add(links,a,b);Add(links,b,a);}}
            var todo=new Stack<int>();todo.Push(start.sector);var visited=new HashSet<int>();var result=new List<SurfaceRef>();
            while(todo.Count>0)
            {
                int sector=todo.Pop();if(!visited.Add(sector)||(restricted&&!allowed.Contains(sector)))continue;
                var id=new SurfaceRef{sector=sector,line=-1,side=-1,part=start.part};
                if(!Same(VisualGeometry.Texture(map,id),original))continue;result.Add(id);
                List<int> neighbors;if(links.TryGetValue(sector,out neighbors))foreach(int next in neighbors)if(!visited.Contains(next))todo.Push(next);
            }
            return result;
        }
    }
}
