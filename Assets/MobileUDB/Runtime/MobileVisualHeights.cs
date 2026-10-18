using System;
using System.Collections.Generic;
using MobileUDB.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        int selectionHeightStep=8;HeightWallMode selectionHeightWalls=HeightWallMode.Ignore;bool heightWithinSelection;
        void VisualHeightSelectionMenu()
        {
            var context=CaptureOpenContext();var expected=visualView.SnapshotSelection();Text feedback=null;
            Action<int,bool> execute=(direction,nearest)=>
            {
                if(!CurrentOpenContext(context)||!SameVisualTextureSelection(expected))Notify("Карта или порядок выделения изменились. Откройте высоты снова.");
                else if(VisualHeightSelectionChange(direction,nearest)){context=CaptureOpenContext();expected=visualView.SnapshotSelection();}
                if(feedback!=null)feedback.text=message;
            };
            ShowDialog("Высоты выделения",body=>
            {
                LineBatchCaption(body,"ИЗМЕНИТЬ ОТМЕЧЕННОЕ");
                AddLabel(body,"Пол и потолок двигаются по своим отметкам. Один сектор обрабатывается один раз; отмеченные объекты Hexen тоже меняют высоту.");
                var steps=Rect("Шаг высоты выделения",body);steps.gameObject.AddComponent<LayoutElement>().preferredHeight=52;
                var layout=steps.gameObject.AddComponent<HorizontalLayoutGroup>();layout.spacing=8;layout.childControlWidth=layout.childForceExpandWidth=layout.childControlHeight=layout.childForceExpandHeight=true;
                var buttons=new List<Button>();Action refreshSteps=null;
                foreach(int step in new[]{1,8,128,0})
                {int choice=step;buttons.Add(Button(steps,step==0?"Сетка":step.ToString(),()=>{selectionHeightStep=choice;refreshSteps();}));}
                refreshSteps=()=>{for(int i=0;i<buttons.Count;i++)buttons[i].GetComponent<Image>().color=selectionHeightStep==(i==0?1:i==1?8:i==2?128:0)?Accent:Panel;};refreshSteps();
                var moves=Rect("Высота выше / ниже",body);moves.gameObject.AddComponent<LayoutElement>().preferredHeight=60;
                var row=moves.gameObject.AddComponent<HorizontalLayoutGroup>();row.spacing=8;row.childControlWidth=row.childForceExpandWidth=row.childControlHeight=row.childForceExpandHeight=true;
                foreach(int direction in new[]{-1,1})
                {
                    int sign=direction;var button=IconButton(moves,direction>0?"Выделение выше":"Выделение ниже",direction>0?EditorIcon.Symbol.Up:EditorIcon.Symbol.Down,()=>
                    {execute(sign,false);});Badge(button.transform,direction>0?"+":"−",true);
                }
                Button wall=null;Action wallLabel=()=>wall.GetComponentInChildren<Text>().text="Стены: "+new[]{"не менять высоты","двигать пол","двигать потолок","двигать обе плоскости"}[(int)selectionHeightWalls];
                wall=AddButton(body,"",()=>{selectionHeightWalls=(HeightWallMode)(((int)selectionHeightWalls+1)%4);wallLabel();});wallLabel();
                LineBatchCaption(body,"ПЛОСКОСТИ · ИСТОЧНИК УРОВНЯ");
                Button source=null;Action sourceLabel=()=>source.GetComponentInChildren<Text>().text="Уровень: "+(heightWithinSelection?"внутри выделения":"по соседним секторам");
                source=AddButton(body,"",()=>{heightWithinSelection=!heightWithinSelection;sourceLabel();});sourceLabel();
                foreach(int direction in new[]{-1,1})
                {int sign=direction;AddButton(body,direction>0?"Выделение к уровню выше":"Выделение к уровню ниже",()=>execute(sign,true));}
                feedback=Label(body,"Сетка: "+Grid+". Одно действие — один Undo.",16);feedback.alignment=TextAnchor.MiddleLeft;feedback.gameObject.AddComponent<LayoutElement>().preferredHeight=72;
                AddLabel(body,"Плоскости: соседние сектора или крайняя высота внутри выделения. Объекты Hexen: ближайший уровень пересекающегося объекта, пол или потолок; порядок отметок учитывается. Висящие объекты считают Z от потолка. Стены сохраняются. Doom не хранит высоту объектов.");
            });
        }
        bool VisualHeightSelectionChange(int direction,bool nearest)
        {
            try
            {
                var marks=visualView.SnapshotSelection();int step=selectionHeightStep==0?Grid:selectionHeightStep;
                var plan=nearest?VisualHeightEdits.NearestTargets(Map,marks.surfaces,marks.things,direction>0,heightWithinSelection,type=>thingCatalog.Find(type)):VisualHeightEdits.Shift(Map,marks.surfaces,marks.things,direction*step,selectionHeightWalls,type=>thingCatalog.Find(type).hangs);
                if(plan.ChangeCount==0){Notify("Подходящие высоты уже совпадают либо нет отмеченных целей.");return false;}
                SurfaceBatchResult result=null;
                if(Edit(m=>result=plan.Apply(m),nearest?"Высоты выровнены.":"Высоты изменены на "+direction*step+".")){visualView.RebindSelection(result,marks);return true;}
                return false;
            }
            catch(Exception error){Notify(error.Message);return false;}
        }
    }
}
