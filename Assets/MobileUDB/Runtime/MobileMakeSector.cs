using System;
using System.Collections.Generic;
using MobileUDB.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        OpenContext makeSectorContext;
        MapPoint? makeSectorPoint;
        readonly HashSet<int> makeSectorLines=new HashSet<int>();
        SectorCreationResult makeSectorPreview;
        bool makeSectorTracking,makeSectorValid,makeSectorAutoClear=true;
        RectTransform makeSectorUI;
        Text makeSectorInfo;
        Button makeSectorApply;
        string MakeSectorSky {get{return GameSky.FlatName(Map);}}
        public bool MakingSector {get{return Tool==MobileTool.MakeSector;}}
        public bool MakeSectorPreviewLine(int index){return MakingSector&&makeSectorValid&&makeSectorLines.Contains(index);}
        void MakeSectorButton(RectTransform body)
        {
            var row=ShapeOptionRow(body);
            VisualThingCommand(row,"Создать сектор из контура",EditorIcon.Symbol.MakeSector,BeginMakeSectorKeepTextures).interactable=SelectedSectorCreationReady;
        }
        void MakeSectorMenu()
        {
            if(VisualActive||Draft.Count>0||Tool==MobileTool.Paste){Notify("Завершите рисование или вставку и откройте 2D-карту.");return;}
            makeSectorAutoClear=PlayerPrefs.GetInt("MobileUDB.MakeSector.AutoClear",1)!=0;
            ShowDialog("Сектор из контура",body=>
            {
                LineBatchCaption(body,"ОБЛАСТЬ");
                AddLabel(body,"Коснитесь области между существующими стенами. Проверьте подсвеченную границу и нажмите «Создать». Поддерживаются внутренние контуры и отдельные стены внутри. Перетаскивание перемещает карту.");
                LineBatchCaption(body,"ТЕКСТУРЫ");
                var row=ShapeOptionRow(body);Button clean=null,keep=null;
                Action refresh=()=>{clean.GetComponent<Image>().color=makeSectorAutoClear?Accent:Panel;keep.GetComponent<Image>().color=makeSectorAutoClear?Panel:Accent;};
                clean=Button(row,"Очистка",()=>{makeSectorAutoClear=true;PlayerPrefs.SetInt("MobileUDB.MakeSector.AutoClear",1);refresh();});
                keep=Button(row,"Сохранить",()=>{makeSectorAutoClear=false;PlayerPrefs.SetInt("MobileUDB.MakeSector.AutoClear",0);refresh();});refresh();
                AddLabel(body,"Как в UDB: настройки берутся со сторон границы, затем с ближайшей стены. Для пустой карты используются настройки рисования. Очистка убирает лишние верхние / нижние текстуры; средняя убирается при появлении второй стороны.");
                DialogApply(body,()=>{makeSectorContext=CaptureOpenContext();ClearMakeSectorPreview();SetTool(MobileTool.MakeSector);Changed(false);Notify("Коснитесь области. «Создать» применит сектор одним Undo.");return true;},"Выбрать область",true);
            });
        }
        void ClearMakeSectorPreview()
        {makeSectorPoint=null;makeSectorPreview=null;makeSectorLines.Clear();makeSectorValid=false;makeSectorTracking=false;}
        void CancelMakeSector(){ClearMakeSectorPreview();SetTool(MobileTool.Select);Notify("Создание сектора закрыто.");}
        bool BeginMakeSectorPointer(){if(!MakingSector)return false;makeSectorTracking=true;return true;}
        bool MoveMakeSectorPointer(Vector2 previous,Vector2 screen){if(!MakingSector)return false;if(moved)View.Pan(previous,screen);return true;}
        bool EndMakeSectorPointer(Vector2 screen)
        {
            if(!MakingSector)return false;bool tapped=makeSectorTracking&&!moved&&View.ScreenRect.Contains(screen);makeSectorTracking=false;
            if(tapped)
            {
                ClearMakeSectorPreview();
                try
                {
                    if(makeSectorContext==null||!CurrentOpenContext(makeSectorContext))throw new InvalidOperationException("Карта изменилась. Закройте создание сектора и откройте снова.");
                    var point=View.World(screen);var result=SectorCreation.Build(Map,point,drawTextures,MakeSectorSky,makeSectorAutoClear);
                    makeSectorPoint=point;makeSectorPreview=result;foreach(int edge in result.directedEdges)makeSectorLines.Add(edge/2);
                    Notify("Проверьте границу · "+makeSectorLines.Count+" стен. «Создать» применит сектор.");
                }
                catch(Exception error){Notify(error.Message);}
                Changed(false);
            }
            return true;
        }
        void ApplyMakeSector()
        {
            if(!MakingSector||!makeSectorPoint.HasValue||makeSectorPreview==null)return;
            if(makeSectorContext==null||!CurrentOpenContext(makeSectorContext)){Notify("Карта изменилась. Закройте создание сектора и откройте снова.");return;}
            var point=makeSectorPoint.Value;
            if(Edit(map=>{int sector=map.MakeSector(point,drawTextures,MakeSectorSky,makeSectorAutoClear);Selection.Clear();Selection.Add(new ElementRef(ElementKind.Sector,sector));Kind=ElementKind.Sector;},"Сектор создан. Один Undo отменяет изменения."))
            {ClearMakeSectorPreview();makeSectorContext=CaptureOpenContext();Changed(false);}
        }
        void BuildMakeSectorUI(RectTransform viewport)
        {
            makeSectorUI=Rect("Создание сектора",viewport);makeSectorUI.anchorMin=new Vector2(0,1);makeSectorUI.anchorMax=Vector2.one;makeSectorUI.pivot=new Vector2(.5f,1);makeSectorUI.sizeDelta=new Vector2(-24,116);makeSectorUI.anchoredPosition=new Vector2(0,-10);makeSectorUI.gameObject.AddComponent<Image>().color=Background;
            makeSectorInfo=Label(makeSectorUI,"",16);var info=makeSectorInfo.rectTransform;info.anchorMin=new Vector2(0,.5f);info.anchorMax=Vector2.one;info.offsetMin=new Vector2(8,0);info.offsetMax=new Vector2(-8,0);
            makeSectorApply=Button(makeSectorUI,"Создать",ApplyMakeSector);makeSectorApply.name="Применить создание сектора";var ar=makeSectorApply.GetComponent<RectTransform>();ar.anchorMin=Vector2.zero;ar.anchorMax=new Vector2(.5f,0);ar.pivot=new Vector2(.5f,0);ar.sizeDelta=new Vector2(-12,52);ar.anchoredPosition=new Vector2(0,4);
            var cancel=Button(makeSectorUI,"Выйти",CancelMakeSector);cancel.name="Отменить создание сектора";var cr=cancel.GetComponent<RectTransform>();cr.anchorMin=new Vector2(.5f,0);cr.anchorMax=new Vector2(1,0);cr.pivot=new Vector2(.5f,0);cr.sizeDelta=new Vector2(-12,52);cr.anchoredPosition=new Vector2(0,4);makeSectorUI.gameObject.SetActive(false);
        }
        void RefreshMakeSectorUI()
        {
            if(!MakingSector&&makeSectorPreview!=null)ClearMakeSectorPreview();
            if(makeSectorUI==null)return;makeSectorUI.gameObject.SetActive(MakingSector);if(!MakingSector)return;
            makeSectorValid=makeSectorContext!=null&&CurrentOpenContext(makeSectorContext);makeSectorApply.interactable=makeSectorValid&&makeSectorPreview!=null;
            makeSectorInfo.text=!makeSectorValid?"Карта изменилась · закройте создание сектора":makeSectorPreview==null?"Создание сектора\nКоснитесь замкнутой области":("Граница · "+makeSectorLines.Count+" стен\nПол "+makeSectorPreview.map.sectors[makeSectorPreview.sector].floor+" · потолок "+makeSectorPreview.map.sectors[makeSectorPreview.sector].ceiling);
        }
    }
}
