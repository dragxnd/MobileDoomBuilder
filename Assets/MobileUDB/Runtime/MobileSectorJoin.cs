using System;
using System.Collections.Generic;
using MobileUDB.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        Button sectorJoinButton,sectorMergeButton,visualSectorJoinButton,visualSectorMergeButton;
        void BuildSectorToolbarButtons(Transform parent,bool visual)
        {
            var join=IconButton(parent,"Join · соединить секторы, сохранить стены · J",EditorIcon.Symbol.Join,()=>JoinSelectedSectors(false));
            var merge=IconButton(parent,"Merge · слить секторы, удалить общие стены · Shift+J",EditorIcon.Symbol.Merge,()=>JoinSelectedSectors(true));
            SectorToolbarCaption(join,"JOIN");SectorToolbarCaption(merge,"MERGE");
            if(visual){visualSectorJoinButton=join;visualSectorMergeButton=merge;}else{sectorJoinButton=join;sectorMergeButton=merge;}
            join.gameObject.SetActive(false);merge.gameObject.SetActive(false);
        }
        void SectorToolbarCaption(Button button,string text)
        {
            var icon=button.GetComponentInChildren<EditorIcon>().rectTransform;
            icon.sizeDelta=new Vector2(28,28);icon.anchoredPosition=new Vector2(0,6);
            var label=Label(button.transform,text,10);label.fontStyle=FontStyle.Bold;label.color=new Color32(255,218,127,255);
            var rect=label.rectTransform;rect.anchorMin=Vector2.zero;rect.anchorMax=new Vector2(1,0);rect.pivot=new Vector2(.5f,0);
            rect.sizeDelta=new Vector2(-4,14);rect.anchoredPosition=new Vector2(0,1);
        }
        List<int> ToolbarSectorTargets()
        {
            var result=new List<int>();var seen=new HashSet<int>();
            if(VisualActive)
            {
                foreach(var surface in visualView.SelectedSurfaces)
                    if(surface.IsFlat&&SurfaceTextureEdits.Valid(Map,surface)&&seen.Add(surface.sector))result.Add(surface.sector);
            }
            else if(Kind==ElementKind.Sector)foreach(var item in Selection)
                if(item.kind==ElementKind.Sector&&item.index>=0&&item.index<Map.sectors.Count&&seen.Add(item.index))result.Add(item.index);
            return result;
        }
        bool ToolbarSectorJoinReady()
        {
            if(Draft.Count!=0||MakingSector||CurvingLines||BridgingLines||Tool==MobileTool.Paste)return false;
            if(!VisualActive)return SectorJoinReady;
            if(visualView.SelectedThings.Count>0)return false;
            foreach(var surface in visualView.SelectedSurfaces)if(!surface.IsFlat)return false;
            return Tool==MobileTool.Visual;
        }
        void RefreshSectorToolbarButtons()
        {
            bool visible=ToolbarSectorTargets().Count>1,ready=visible&&ToolbarSectorJoinReady();
            var join=VisualActive?visualSectorJoinButton:sectorJoinButton;var merge=VisualActive?visualSectorMergeButton:sectorMergeButton;
            if(join==null||merge==null)return;
            join.gameObject.SetActive(visible);merge.gameObject.SetActive(visible);join.interactable=merge.interactable=ready;
        }
        void JoinSelectedSectors(bool removeWalls)
        {
            var targets=ToolbarSectorTargets();
            if(targets.Count<2||!ToolbarSectorJoinReady()){Notify("Выберите минимум два сектора; в 3D выберите их полы или потолки.");return;}
            bool visual=VisualActive;
            Edit(map=>
            {
                int sector=map.JoinSectors(targets,removeWalls);
                if(visual)visualView.ForgetSelection();
                Selection.Clear();if(!visual)Selection.Add(new ElementRef(ElementKind.Sector,sector));Kind=ElementKind.Sector;
            },removeWalls?"Merge: секторы слиты, общие стены удалены. Доступна отмена.":"Join: секторы соединены, стены сохранены. Доступна отмена.");
        }
        bool SectorJoinReady {get{return DoorsReady&&Kind==ElementKind.Sector&&Selection.Count>=2;}}
        void SectorJoinButton(RectTransform body)
        {LineBatchCaption(body,"ГЕОМЕТРИЯ СЕКТОРОВ");var row=ShapeOptionRow(body);VisualThingCommand(row,"Объединить секторы…",EditorIcon.Symbol.Join,JoinSectorsMenu).interactable=SectorJoinReady;}
        void JoinSectorsMenu()
        {
            if(!SectorJoinReady){Notify("Выделите минимум два сектора и завершите рисование или вставку.");return;}
            CancelDrag();var context=CaptureOpenContext();var indices=new List<int>();foreach(var item in Selection)indices.Add(item.index);var catalog=resources;
            var set=new HashSet<int>(indices);int common=0;foreach(var line in Map.lines)if(line.front>=0&&line.back>=0)
            {int front=Map.sides[line.front].sector,back=Map.sides[line.back].sector;if(front!=back&&set.Contains(front)&&set.Contains(back))common++;}
            bool removeWalls=false;
            ShowDialog("Объединить секторы",body=>
            {
                var summary=Label(body,"Секторов: "+indices.Count+". Общих стен: "+common+". Высоты, свет, текстуры, эффект и тег — из первого выделенного сектора #"+indices[0]+". Объекты сохраняются.",18);
                summary.alignment=TextAnchor.UpperLeft;summary.gameObject.AddComponent<LayoutElement>().minHeight=48;
                LineBatchCaption(body,"ОБЩИЕ СТЕНЫ");var row=ShapeOptionRow(body);Button keep=null,remove=null;Text note=null;
                Action refresh=()=>{keep.GetComponent<Image>().color=!removeWalls?Accent:Panel;remove.GetComponent<Image>().color=removeWalls?Accent:Panel;note.text=removeWalls?"Слить: общие стены и их действия исчезнут. Служебные стены внутри одного сектора сохраняются.":"Соединить: все стены и их действия сохраняются. Оба варианта отменяются одним Undo.";};
                keep=VisualThingCommand(row,"Сохранить",EditorIcon.Symbol.Join,()=>{removeWalls=false;refresh();});
                remove=VisualThingCommand(row,"Удалить",EditorIcon.Symbol.Merge,()=>{removeWalls=true;refresh();});
                note=Label(body,"",18);note.alignment=TextAnchor.UpperLeft;note.gameObject.AddComponent<LayoutElement>().minHeight=40;refresh();
                DialogApply(body,()=>
                {
                    if(!CurrentOpenContext(context)||!SectorJoinReady||!SameSectorJoinOrder(indices)||!ReferenceEquals(catalog,resources))
                    {Notify("Карта, ресурсы или порядок выделения изменились. Откройте объединение снова.");return false;}
                    return Edit(map=>{int sector=map.JoinSectors(indices,removeWalls);Selection.Clear();Selection.Add(new ElementRef(ElementKind.Sector,sector));Kind=ElementKind.Sector;},"Секторы объединены. Доступна отмена.");
                },"Объединить",true);
            });
        }
        bool SameSectorJoinOrder(List<int> indices)
        {if(indices.Count!=Selection.Count)return false;int i=0;foreach(var item in Selection)if(item.kind!=ElementKind.Sector||item.index!=indices[i++])return false;return true;}
    }
}
