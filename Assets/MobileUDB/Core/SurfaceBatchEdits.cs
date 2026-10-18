using System;
using System.Collections.Generic;

namespace MobileUDB.Core
{
    public sealed class SurfaceBatchResult
    {
        public int changed;
        public readonly Dictionary<SurfaceRef,SurfaceRef> rebound=new Dictionary<SurfaceRef,SurfaceRef>();
    }
    // Preflight every target before changing anything; retain front/back identity
    // when one selected part detaches storage shared with another selected part.
    public static class SurfaceBatchEdits
    {
        sealed class Target
        {
            public SurfaceRef original;
            public bool front,change;
            public string texture;
            public int Role {get{return original.line*2+(front?0:1);}}
            public SurfaceRef Current(MapDocument map)
            {var id=original;if(!id.IsFlat)id.side=front?map.lines[id.line].front:map.lines[id.line].back;return id;}
        }
        static List<Target> Targets(MapDocument map,IEnumerable<SurfaceRef> surfaces)
        {
            if(surfaces==null)throw new ArgumentNullException("surfaces");
            var result=new List<Target>();var seen=new HashSet<SurfaceRef>();
            foreach(var id in surfaces)
            {
                if(!SurfaceTextureEdits.Valid(map,id))throw new InvalidOperationException("Поверхность больше не существует. Выберите её снова.");
                if(seen.Add(id))result.Add(new Target{original=id,front=!id.IsFlat&&map.lines[id.line].front==id.side});
            }
            return result;
        }
        static Dictionary<int,int> PreflightSides(MapDocument map,List<Target> targets)
        {
            var uses=new Dictionary<int,int>();
            foreach(var line in map.lines){CountUse(uses,line.front);CountUse(uses,line.back);}
            var selected=new Dictionary<int,HashSet<int>>();
            foreach(var item in targets)if(item.change&&!item.original.IsFlat)
            {HashSet<int> roles;if(!selected.TryGetValue(item.original.side,out roles)){roles=new HashSet<int>();selected[item.original.side]=roles;}roles.Add(item.Role);}
            int needed=0;foreach(var pair in selected)needed+=Math.Min(uses[pair.Key]-1,pair.Value.Count);
            if(map.sides.Count+needed>65535)throw new InvalidOperationException("Превышен предел сторон Doom / Hexen.");
            return uses;
        }
        static void CountUse(Dictionary<int,int> uses,int side)
        {if(side<0)return;int count;uses.TryGetValue(side,out count);uses[side]=count+1;}
        static SurfaceRef Exclusive(MapDocument map,Target target,Dictionary<int,int> uses)
        {
            var id=target.Current(map);if(id.IsFlat)return id;int before=id.side;
            id=SurfaceTextureEdits.Exclusive(map,id,uses[before]);
            if(id.side!=before){uses[before]--;uses[id.side]=1;}return id;
        }
        static SurfaceBatchResult Finish(MapDocument map,List<Target> targets,int changed)
        {var result=new SurfaceBatchResult{changed=changed};foreach(var item in targets)result.rebound[item.original]=item.Current(map);return result;}
        public static SurfaceBatchResult Textures(MapDocument map,IEnumerable<SurfaceRef> surfaces,string wall,string flat,IEnumerable<SurfaceRef> retained=null)
        {
            if(wall!=null)wall=SurfaceTextureEdits.Name(wall);if(flat!=null)flat=SurfaceTextureEdits.Name(flat);
            var targets=Targets(map,surfaces);var marks=retained==null?new List<Target>():Targets(map,retained);int changed=0;
            foreach(var item in targets)
            {item.texture=item.original.IsFlat?flat:wall;item.change=item.texture!=null&&!string.Equals(VisualGeometry.Texture(map,item.original),item.texture,StringComparison.OrdinalIgnoreCase);if(item.change)changed++;}
            var uses=PreflightSides(map,targets);
            foreach(var item in targets)if(item.change)VisualGeometry.SetTexture(map,Exclusive(map,item,uses),item.texture);
            var result=Finish(map,targets,changed);foreach(var item in marks)result.rebound[item.original]=item.Current(map);return result;
        }
        public static SurfaceBatchResult OffsetWalls(MapDocument map,IEnumerable<SurfaceRef> surfaces,int x,int y)
        {return WallOffsets(map,surfaces,x,y,true);}
        // Binary BaseVisualGeometrySidedef.OnChangeTextureOffset. C# signed
        // remainder is intentional; the middle part never wraps Y.
        public static WallTextureOffset DirectionalOffset(int x,int y,int horizontal,int vertical,SurfacePart part,WallTextureSize size)
        {
            int nextX=unchecked(x-horizontal),nextY=unchecked(y-vertical);
            if(size.Loaded){nextX%=size.width;if(part!=SurfacePart.Middle)nextY%=size.height;}
            // UDB writes Int16 SIDEDEFS even when the in-memory offset grows.
            return new WallTextureOffset(unchecked((short)nextX),unchecked((short)nextY));
        }
        public static SurfaceBatchResult MoveWallTextures(MapDocument map,IEnumerable<SurfaceRef> surfaces,int horizontal,int vertical,Func<SurfaceRef,WallTextureSize> textureSize)
        {
            Binary(map);var targets=Targets(map,surfaces);var changes=new Dictionary<int,WallTextureOffset>();
            foreach(var item in targets)if(!item.original.IsFlat&&!changes.ContainsKey(item.Role))
            {
                var side=map.sides[item.original.side];var size=textureSize==null?new WallTextureSize():textureSize(item.original);
                changes.Add(item.Role,DirectionalOffset(side.offsetX,side.offsetY,horizontal,vertical,item.original.part,size));
            }
            foreach(var item in targets)if(!item.original.IsFlat)
            {var side=map.sides[item.original.side];var next=changes[item.Role];item.change=next.x!=side.offsetX||next.y!=side.offsetY;}
            var uses=PreflightSides(map,targets);var done=new HashSet<int>();
            foreach(var item in targets)if(item.change&&done.Add(item.Role))
            {var id=Exclusive(map,item,uses);var next=changes[item.Role];map.sides[id.side].offsetX=next.x;map.sides[id.side].offsetY=next.y;}
            return Finish(map,targets,done.Count);
        }
        public static SurfaceBatchResult SetWallOffsets(MapDocument map,IEnumerable<SurfaceRef> surfaces,int x,int y)
        {
            Binary(map);return WallOffsets(map,surfaces,x,y,false);
        }
        static void Binary(MapDocument map)
        {if(map==null||(map.format!=MapFormat.Doom&&map.format!=MapFormat.Hexen))throw new InvalidOperationException("Поддерживаются бинарные карты Doom / Hexen.");}
        static SurfaceBatchResult WallOffsets(MapDocument map,IEnumerable<SurfaceRef> surfaces,int x,int y,bool relative)
        {
            var targets=Targets(map,surfaces);var roles=new HashSet<int>();
            foreach(var item in targets)if(!item.original.IsFlat)
            {
                var side=map.sides[item.original.side];long nextX=relative?(long)side.offsetX+x:x,nextY=relative?(long)side.offsetY+y:y;
                if(nextX<-32768||nextX>32767||nextY<-32768||nextY>32767)throw new InvalidOperationException("Смещение текстуры за пределами Doom / Hexen.");
                item.change=side.offsetX!=nextX||side.offsetY!=nextY;
            }
            var uses=PreflightSides(map,targets);
            foreach(var item in targets)if(item.change&&roles.Add(item.Role))
            {var id=Exclusive(map,item,uses);var side=map.sides[id.side];side.offsetX=relative?side.offsetX+x:x;side.offsetY=relative?side.offsetY+y:y;}
            return Finish(map,targets,roles.Count);
        }
        public static SurfaceBatchResult SetWallUnpegged(MapDocument map,IEnumerable<SurfaceRef> surfaces,int flag,bool enabled)
        {
            Binary(map);if(flag!=8&&flag!=16)throw new ArgumentOutOfRangeException("flag");
            var targets=Targets(map,surfaces);var lines=new HashSet<int>();int changed=0;
            foreach(var item in targets)if(!item.original.IsFlat)lines.Add(item.original.line);
            foreach(int index in lines)
            {
                var line=map.lines[index];int next=enabled?line.flags|flag:line.flags&~flag;
                if(next!=line.flags){line.flags=next;changed++;}
            }
            return Finish(map,targets,changed);
        }
    }
}
