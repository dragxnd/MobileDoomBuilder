using System;
using System.Globalization;
using MobileUDB.Core;
using UnityEngine;

namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        void DrawSettingsMenu()
        {
            ShowDialog("Настройки рисования",body =>
            {
                AddButton(body,"Текстуры стен, пола и потолка…",DrawTexturesMenu);
                AddButton(body,"Высоты и освещение…",DrawSectorSettings);
                AddButton(body,"Взять текстуры со стены…",SampleDrawWallMenu).interactable = DrawSampleLine() >= 0;
                bool sectorSelected = false;
                if (Selection.Count == 1) foreach (var selected in Selection) sectorSelected = selected.kind == ElementKind.Sector;
                AddButton(body,"Взять из выбранного сектора",SampleDrawSector).interactable = sectorSelected;
                AddButton(body,"К рисованию",DrawingMenu);
                AddLabel(body,"Образец задаёт базовые высоты, освещение, текстуры пола и потолка. Выберите один сектор перед открытием рисования.");
            });
        }
        void SampleDrawSector()
        {
            int index = -1;
            if (Selection.Count == 1) foreach (var selected in Selection) if (selected.kind == ElementKind.Sector) index = selected.index;
            if (index < 0) { Notify("Выберите один сектор для образца настроек рисования."); return; }
            drawTextures = drawTextures.FromSector(Map.sectors[index],Map.format);
            Changed(); DrawSectorSettings(); Notify("Настройки взяты из сектора #" + index + ".");
        }
        void DrawSectorSettings()
        {
            ShowDialog("Новый сектор",body =>
            {
                var error = Label(body,"",18); error.alignment = TextAnchor.MiddleLeft;
                error.color = new Color32(255,170,140,255); error.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().minHeight = 56;
                error.gameObject.SetActive(false);
                var floor = Field(body,"Высота пола",drawTextures.overrideFloorHeight ? drawTextures.floorHeight.ToString("R",CultureInfo.InvariantCulture) : "",false);
                var ceiling = Field(body,"Потолок",drawTextures.overrideCeilingHeight ? drawTextures.ceilingHeight.ToString("R",CultureInfo.InvariantCulture) : "",false);
                var light = Field(body,"Свет 0–255",drawTextures.overrideLight ? drawTextures.light.ToString() : "");
                floor.keyboardType = ceiling.keyboardType = TouchScreenKeyboardType.NumbersAndPunctuation;
                floor.characterLimit = ceiling.characterLimit = 24;
                AddButton(body,"Применить",() =>
                {
                    try
                    {
                    var next = drawTextures.Validated(MapFormat.Doom);
                    next.overrideFloorHeight = !string.IsNullOrWhiteSpace(floor.text); next.overrideCeilingHeight = !string.IsNullOrWhiteSpace(ceiling.text); next.overrideLight = !string.IsNullOrWhiteSpace(light.text);
                    if (next.overrideFloorHeight) next.floorHeight = RealNumber(floor,-32768,32767);
                    if (next.overrideCeilingHeight) next.ceilingHeight = RealNumber(ceiling,-32768,32767);
                    if (next.overrideLight) next.light = Number(light,0,255);
                    drawTextures = next.Validated(Map.format); Changed(); DrawSettingsMenu();
                    }
                    catch (Exception exception) { error.text = exception.Message; error.gameObject.SetActive(true); }
                });
                AddButton(body,"Высоты и свет: Авто",() => { drawTextures.overrideFloorHeight = drawTextures.overrideCeilingHeight = drawTextures.overrideLight = false; Changed(); DrawSectorSettings(); });
                AddButton(body,"Назад без изменений",DrawSettingsMenu);
                AddLabel(body,"Пустое поле — Авто: наследовать при рисовании. Настройки меняют только новые секторы. Наклонные плоскости наследуются и перемещаются вместе с высотой. В Doom / Hexen вводите целые числа.");
            });
        }
    }
}
