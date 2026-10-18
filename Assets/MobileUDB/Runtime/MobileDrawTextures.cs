using System;
using MobileUDB.Core;

namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        DrawTextureOptions drawTextures = new DrawTextureOptions();
        int DrawSampleLine()
        {
            if (VisualActive)
            {
                if (!visualView.Picked.HasValue || visualView.Picked.Value.IsFlat) return -1;
                int line = visualView.Picked.Value.line;
                return line >= 0 && line < Map.lines.Count ? line : -1;
            }
            if (Selection.Count == 1) foreach (var selected in Selection)
                if (selected.kind == ElementKind.Line && selected.index >= 0 && selected.index < Map.lines.Count) return selected.index;
            return -1;
        }
        void SampleDrawWallMenu()
        {
            int index = DrawSampleLine();
            if (index < 0) { Notify("Выберите одну стену в 2D или её поверхность в 3D."); return; }
            var line = Map.lines[index];
            ShowDialog("Образец стены #" + index,body =>
            {
                int count = 0;
                foreach (bool front in new[] { true,false })
                {
                    int sideIndex = front ? line.front : line.back;
                    if (sideIndex < 0 || sideIndex >= Map.sides.Count) continue;
                    if (VisualActive && visualView.Picked.Value.side != sideIndex) continue;
                    var side = Map.sides[sideIndex]; string label = front ? "Передняя сторона" : "Обратная сторона";
                    AddLabel(body,label + " · сектор #" + side.sector + "\nВерх: " + (side.upper ?? "-") + " · середина: " + (side.middle ?? "-") + " · низ: " + (side.lower ?? "-"));
                    AddButton(body,"Взять: " + label.ToLowerInvariant(),() =>
                    {
                        drawTextures = drawTextures.FromSide(side,Map.format);
                        Changed(); DrawTexturesMenu(); Notify("Текстуры рисования: стена #" + index + ", " + label.ToLowerInvariant() + ".");
                    }); count++;
                    // Shared sidedefs need only one option when picked in 3D.
                    if (VisualActive) break;
                }
                if (count == 0) AddLabel(body,"У выбранной стены нет стороны для образца.");
                AddButton(body,"Назад без изменений",DrawSettingsMenu);
                AddLabel(body,"Образец заменяет три настройки текстур стен, включая «-». Высоты, свет, пол и потолок сохраняются. Смещения и масштаб не копируются. Изменения карты начнутся при рисовании.");
            });
        }
        void DrawTexturesMenu()
        {
            ShowDialog("Текстуры рисования",body =>
            {
                AddLabel(body,"Авто — наследовать отделку. Заданная текстура используется при создании комнаты или цепочки стен.");
                Action<string,string,ResourceKind,Action<string>> option = (label,value,kind,set) =>
                    AddButton(body,label + ": " + (string.IsNullOrEmpty(value) ? "Авто" : value) + "…",() => DrawTextureOption(label,value,kind,set));
                option("Верх стены",drawTextures.upper,ResourceKind.Wall,value => drawTextures.upper = value);
                option("Середина стены",drawTextures.middle,ResourceKind.Wall,value => drawTextures.middle = value);
                option("Низ стены",drawTextures.lower,ResourceKind.Wall,value => drawTextures.lower = value);
                option("Пол",drawTextures.floor,ResourceKind.Flat,value => drawTextures.floor = value);
                option("Потолок",drawTextures.ceiling,ResourceKind.Flat,value => drawTextures.ceiling = value);
                AddButton(body,"Вернуть всё в Авто",() => { drawTextures.upper = drawTextures.middle = drawTextures.lower = drawTextures.floor = drawTextures.ceiling = null; Changed(); DrawTexturesMenu(); });
                AddButton(body,"К рисованию",DrawingMenu);
                AddLabel(body,"Меняются только нужные части новых стен и поверхности новых секторов. Проходы остаются открытыми. Настройки сохраняются в проекте; отмена построения выполняется одним Undo.");
            });
        }
        void DrawTextureOption(string label,string value,ResourceKind kind,Action<string> set)
        {
            ShowDialog(label,body =>
            {
                var name = Field(body,"Текстура",value ?? "",false);
                AddButton(body,"Выбрать по миниатюрам…",() => TexturePicker(name,kind));
                AddButton(body,"Использовать",() =>
                {
                    string texture = TextureName(name);
                    // Share validation with project loading and the drawing operation.
                    new DrawTextureOptions { middle = texture }.Validated(Map.format);
                    set(texture); Changed(); DrawTexturesMenu();
                });
                AddButton(body,"Авто · наследовать",() => { set(null); Changed(); DrawTexturesMenu(); });
                AddButton(body,"Назад без изменений",DrawTexturesMenu);
                AddLabel(body,"Выбор в каталоге заполняет поле. Нажмите «Использовать», чтобы сохранить настройку.");
            });
        }
    }
}
