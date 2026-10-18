using System;
using System.Collections.Generic;
using MobileUDB.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        readonly BinaryPropertyClipboard wallOffsetClipboard=new BinaryPropertyClipboard();
        int wallOffsetStep=8;
        List<SurfaceRef> VisualWallTargets(){return new List<SurfaceRef>(visualView.SelectedSurfaces);}
        SurfaceRef? VisualPrimaryWall()
        {
            var picked=visualView.Picked;
            return picked.HasValue&&!picked.Value.IsFlat&&SurfaceTextureEdits.Valid(Map,picked.Value)?picked:null;
        }
        SurfaceRef? VisualFlagWall()
        {
            var primary=VisualPrimaryWall();if(primary.HasValue)return primary;
            foreach(var id in visualView.SelectedSurfaces)if(!id.IsFlat&&SurfaceTextureEdits.Valid(Map,id)&&(!primary.HasValue||id.line<primary.Value.line))primary=id;
            return primary;
        }
        void VisualWallToolsMenu()
        {
            var context=CaptureOpenContext();var marks=visualView.SnapshotSelection();
            Action<Action> execute=action=>
            {
                if(!CurrentOpenContext(context)||!SameVisualTextureSelection(marks))
                {Notify("Карта или выделение изменились. Откройте текстуры стен снова.");return;}
                action();context=CaptureOpenContext();marks=visualView.SnapshotSelection();
            };
            ShowDialog("Текстуры стен",body=>
            {
                LineBatchCaption(body,"ПЕРЕМЕЩЕНИЕ ТЕКСТУРЫ");
                var value=AddWallOffsetLabel(body);
                var steps=Rect("Шаг смещения",body);steps.gameObject.AddComponent<LayoutElement>().preferredHeight=52;
                var row=steps.gameObject.AddComponent<HorizontalLayoutGroup>();row.spacing=8;row.childControlWidth=row.childForceExpandWidth=true;row.childControlHeight=row.childForceExpandHeight=true;
                var stepButtons=new List<Button>();Action refreshSteps=null;
                foreach(int step in new[]{1,8,0})
                {
                    int choice=step;var button=Button(steps,step==0?"Сетка · "+Grid:step.ToString(),()=>{wallOffsetStep=choice;refreshSteps();});stepButtons.Add(button);
                }
                refreshSteps=()=>{for(int i=0;i<stepButtons.Count;i++)stepButtons[i].GetComponent<Image>().color=wallOffsetStep==(i==0?1:i==1?8:0)?Accent:Panel;};refreshSteps();
                var offsets=Rect("Сдвиг X / Y",body);offsets.gameObject.AddComponent<LayoutElement>().preferredHeight=60;
                var directions=offsets.gameObject.AddComponent<HorizontalLayoutGroup>();directions.spacing=8;directions.childControlWidth=directions.childForceExpandWidth=true;directions.childControlHeight=directions.childForceExpandHeight=true;
                foreach(var delta in new[]{new Vector2Int(-1,0),new Vector2Int(1,0),new Vector2Int(0,-1),new Vector2Int(0,1)})
                {
                    var direction=delta;bool positive=delta.x+delta.y>0;string title=delta.x<0?"Текстуру влево":delta.x>0?"Текстуру вправо":delta.y<0?"Текстуру вверх":"Текстуру вниз";
                    var button=IconButton(offsets,title,EditorIcon.Symbol.Up,()=>execute(()=>
                    {
                        int amount=wallOffsetStep==0?Grid:wallOffsetStep;VisualMoveWallTextures(direction.x*amount,direction.y*amount);RefreshWallOffsetLabel(value);
                    }));
                    button.GetComponentInChildren<EditorIcon>().transform.localRotation=Quaternion.Euler(0,0,delta.x!=0?(positive?-90:90):(positive?180:0));
                }
                AddButton(body,"Сбросить смещения в 0",()=>execute(()=>{VisualResetWallOffsets();RefreshWallOffsetLabel(value);}));
                var movementHelp=Label(body,"Стрелки двигают рисунок. X повторяется по ширине, Y — по высоте верхней / нижней части. Средняя часть по Y не повторяется.",14);movementHelp.alignment=TextAnchor.MiddleLeft;movementHelp.gameObject.AddComponent<LayoutElement>().preferredHeight=60;
                LineBatchCaption(body,"БУФЕР СМЕЩЕНИЙ");
                Button paste=null;
                var copy=AddButton(body,"Копировать смещения X / Y",()=>execute(()=>{VisualCopyWallOffsets();paste.interactable=wallOffsetClipboard.HasWalls;}));copy.interactable=VisualPrimaryWall().HasValue;
                paste=AddButton(body,"Вставить смещения X / Y",()=>execute(()=>{VisualPasteWallOffsets();RefreshWallOffsetLabel(value);}));paste.interactable=wallOffsetClipboard.HasWalls;
                LineBatchCaption(body,"ПРИВЯЗКА · ФЛАГ СТЕНЫ");
                var flags=new List<Action>();
                foreach(int flag in new[]{8,16})
                {
                    int bit=flag;Button button=null;
                    Action refresh=()=>
                    {
                        int state=VisualWallFlagState(bit);
                        button.GetComponentInChildren<Text>().text=(state==1?"✓ ":state==-1?"— ":"○ ")+(bit==8?"Не крепить верх":"Не крепить низ");button.interactable=state!=-2;
                    };
                    button=AddButton(body,"",()=>execute(()=>{VisualToggleWallUnpegged(bit);foreach(var update in flags)update();}));flags.Add(refresh);refresh();
                }
                AddLabel(body,"Смещения действуют на отмеченные стороны; привязка — на всю стену. Прочерк означает разные значения. Нажатие включает флаг у всех, повторное — выключает. Плоскости и объекты сохраняются. Один Undo отменяет действие.");
            });
        }
        Text AddWallOffsetLabel(Transform body)
        {
            var label=Label(body,"",18);label.gameObject.AddComponent<LayoutElement>().preferredHeight=44;RefreshWallOffsetLabel(label);return label;
        }
        void RefreshWallOffsetLabel(Text label)
        {
            var primary=VisualFlagWall();if(!primary.HasValue){label.text="Отметьте сторону стены";return;}
            var side=Map.sides[primary.Value.side];label.text="Стена #"+primary.Value.line+" · X "+side.offsetX+" · Y "+side.offsetY;
        }
        void VisualCopyWallOffsets()
        {
            var primary=VisualPrimaryWall();if(!primary.HasValue){Notify("Последним выберите сторону стены.");return;}
            wallOffsetClipboard.CopyWall(Map,primary.Value);var side=Map.sides[primary.Value.side];Notify("Смещения скопированы: X "+side.offsetX+" / Y "+side.offsetY+".");
        }
        void VisualPasteWallOffsets()
        {
            var marks=visualView.SnapshotSelection();var plan=wallOffsetClipboard.Plan(Map,null,VisualWallTargets(),PropertyFields.OffsetX|PropertyFields.OffsetY);
            if(plan.ChangeCount==0){Notify("Смещения уже совпадают либо буфер пуст.");return;}
            SurfaceBatchResult result=null;
            if(Edit(m=>result=plan.Apply(m),"Смещения вставлены."))visualView.RebindSelection(result,marks);
        }
        void VisualResetWallOffsets()
        {
            var targets=VisualWallTargets();bool changes=false;
            foreach(var id in targets)if(!id.IsFlat&&SurfaceTextureEdits.Valid(Map,id)){var side=Map.sides[id.side];if(side.offsetX!=0||side.offsetY!=0)changes=true;}
            if(!changes){Notify("Смещения уже равны 0.");return;}
            var marks=visualView.SnapshotSelection();SurfaceBatchResult result=null;
            if(Edit(m=>result=SurfaceBatchEdits.SetWallOffsets(m,targets,0,0),"Смещения сброшены."))visualView.RebindSelection(result,marks);
        }
        void VisualToggleWallUnpegged(int flag)
        {
            int state=VisualWallFlagState(flag);if(state==-2)return;bool enabled=state!=1;
            var targets=VisualWallTargets();var marks=visualView.SnapshotSelection();SurfaceBatchResult result=null;
            if(Edit(m=>result=SurfaceBatchEdits.SetWallUnpegged(m,targets,flag,enabled),"Привязка текстур изменена."))visualView.RebindSelection(result,marks);
        }
    }
}
