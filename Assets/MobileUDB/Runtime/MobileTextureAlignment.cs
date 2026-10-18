using System;
using System.Collections.Generic;
using MobileUDB.Core;

namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        void TextureAlignmentMenu()
        {
            if (VisualActive && visualView.Picked.HasValue && !visualView.Picked.Value.IsFlat) { TextureAlignmentAxes(visualView.Picked.Value); return; }
            int line = -1;
            if (Selection.Count == 1) foreach (var selected in Selection) if (selected.kind == ElementKind.Line) line = selected.index;
            if (line < 0) { Notify("Выберите одну стену — опору для выравнивания."); return; }
            ShowDialog("Опорная текстура",body =>
            {
                int count = 0;
                foreach (var wall in VisualGeometry.BuildWalls(Map,resources)) if (wall.id.line == line)
                {
                    var surface = wall.id; if (resources.Find(wall.texture,ResourceKind.Wall,MapFormats.IsUniversal(Map.format)) == null) continue;
                    string side = Map.lines[line].front == surface.side ? "Передняя" : "Обратная";
                    string part = surface.part == SurfacePart.Upper ? "верх" : surface.part == SurfacePart.Lower ? "низ" : "середина";
                    AddButton(body,side + " · " + part + " · " + wall.texture,() => TextureAlignmentAxes(surface)); count++;
                }
                if (count == 0) AddLabel(body,"На этой стене нет видимых текстур из подключённых ресурсов.");
            });
        }
        void TextureAlignmentAxes(SurfaceRef surface)
        {
            ShowDialog("Выровнять текстуру",body =>
            {
                AddLabel(body,VisualGeometry.Texture(Map,surface) + " · стена #" + surface.line + "\nВыбранная поверхность остаётся на месте. Выравниваются соседние стены с той же текстурой; несколько отметок ограничивают область выбранными стенами.");
                AddButton(body,"По X",() => AlignWallTexture(surface,true,false));
                AddButton(body,"По Y",() => AlignWallTexture(surface,false,true));
                AddButton(body,"По X и Y",() => AlignWallTexture(surface,true,true));
                AddLabel(body,MapFormats.IsUniversal(Map.format) ? "Для выбранных осей соседние поверхности получают масштаб опорной текстуры. Действие отменяется одним Undo." : "В Doom / Hexen смещения общие для верха, середины и низа одной стороны стены. Действие отменяется одним Undo.");
            });
        }
        void AlignWallTexture(SurfaceRef surface,bool x,bool y)
        {
            if(!SurfaceTextureEdits.Valid(Map,surface)){Notify("Выберите текстуру стены снова.");return;}
            int count=0;var marks=VisualActive?visualView.SnapshotSelection():null;
            var fronts=new Dictionary<SurfaceRef,bool>();
            if(marks!=null)foreach(var id in marks.surfaces)if(!id.IsFlat)fronts[id]=Map.lines[id.line].front==id.side;
            var limit=marks!=null&&marks.surfaces.Count>1?marks.surfaces:null;
            CloseDialog();
            if (Edit(m => { count = WallTextureAlignment.Align(m,surface,resources,x,y,limit); if (count == 0) throw new InvalidOperationException("Соседние текстуры уже выровнены либо отсутствуют."); },"Текстуры выровнены."))
            {
                if(marks!=null)
                {
                    var result=new SurfaceBatchResult();foreach(var id in marks.surfaces){var next=id;if(!id.IsFlat)next.side=fronts[id]?Map.lines[id.line].front:Map.lines[id.line].back;result.rebound[id]=next;}visualView.RebindSelection(result,marks);
                }
                Notify("Выровнено поверхностей: " + count + ".");
            }
        }
    }
}
