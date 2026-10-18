using System;
using System.Collections.Generic;
using System.Globalization;
using MobileUDB.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        SectorGradientTarget gradientTarget;
        bool GradientsReady
        {
            get
            {
                if(!DoorsReady||Selection.Count<3)return false;
                return Map.format==MapFormat.Doom||Map.format==MapFormat.Hexen;
            }
        }
        static double GradientValue(MapSector sector,SectorGradientTarget target)
        {return target==SectorGradientTarget.Brightness?sector.light:target==SectorGradientTarget.Floor?sector.floor:sector.ceiling;}
        void SectorGradientMenu()
        {
            if(!GradientsReady){Notify("Выберите по порядку не менее трёх секторов в 2D; завершите рисование или вставку.");return;}
            var context=CaptureOpenContext();var order=new List<int>();foreach(var item in Selection)order.Add(item.index);
            var startLevels=new double[3];var endLevels=new double[3];for(int i=0;i<3;i++){startLevels[i]=GradientValue(Map.sectors[order[0]],(SectorGradientTarget)i);endLevels[i]=GradientValue(Map.sectors[order[order.Count-1]],(SectorGradientTarget)i);}
            var target=gradientTarget;int stored=PlayerPrefs.GetInt("MobileUDB.Gradient.Interpolation",0);
            var mode=stored>=0&&stored<=3?(GradientInterpolation)stored:GradientInterpolation.Linear;
            ShowDialog("Градиенты · "+order.Count,body=>
            {
                LineBatchCaption(body,"ИЗМЕНЯТЬ");var targetButtons=new List<Button>();var row=ShapeOptionRow(body);Text endpoints=null;
                string[] names={"Свет","Пол","Потолок"};var icons=new[]{EditorIcon.Symbol.BrightnessGradient,EditorIcon.Symbol.FloorsGradient,EditorIcon.Symbol.CeilsGradient};
                Action refresh=()=>
                {
                    for(int i=0;i<targetButtons.Count;i++)targetButtons[i].GetComponent<Image>().color=(int)target==i?Accent:Panel;
                    endpoints.text="Начало: #"+order[0]+" · "+names[(int)target].ToLowerInvariant()+" "+startLevels[(int)target].ToString(CultureInfo.InvariantCulture)+
                        "\nКонец: #"+order[order.Count-1]+" · "+endLevels[(int)target].ToString(CultureInfo.InvariantCulture);
                };
                for(int i=0;i<3;i++)
                {var chosen=(SectorGradientTarget)i;targetButtons.Add(VisualThingCommand(row,names[i],icons[i],()=>{target=chosen;refresh();}));}
                LineBatchCaption(body,"ПЕРЕХОД");var modeButtons=new List<Button>();Action refreshModes=()=>{for(int i=0;i<modeButtons.Count;i++)modeButtons[i].GetComponent<Image>().color=(int)mode==i?Accent:Panel;};
                string[] transitions={"Линейно","Плавно","Разгон","Замедление"};
                for(int i=0;i<4;i+=2)
                {row=ShapeOptionRow(body);for(int j=i;j<i+2;j++){var chosen=(GradientInterpolation)j;modeButtons.Add(Button(row,transitions[j],()=>{mode=chosen;refreshModes();}));}}
                LineBatchCaption(body,"ПОРЯДОК ВЫДЕЛЕНИЯ");
                string chain="";for(int i=0;i<Math.Min(order.Count,12);i++)chain+=(i==0?"":" → ")+"#"+order[i];if(order.Count>12)chain+=" → … → #"+order[order.Count-1];
                var sequence=Label(body,chain,16);sequence.alignment=TextAnchor.MiddleLeft;sequence.gameObject.AddComponent<LayoutElement>().preferredHeight=order.Count>6?68:40;
                endpoints=Label(body,"",18);endpoints.alignment=TextAnchor.MiddleLeft;endpoints.gameObject.AddComponent<LayoutElement>().preferredHeight=64;
                var note=Label(body,"Первый и последний сектор сохраняют свои значения. Промежуточные меняются в порядке выделения. Выбирайте секторы с включённым множественным выбором или кистью. Рамка и «Выделить всё» добавляют их в порядке обхода карты. «Плавно» сглаживает оба края, «Разгон» — начало, «Замедление» — конец. Один Undo отменяет весь градиент.",16);
                note.alignment=TextAnchor.MiddleLeft;note.gameObject.AddComponent<LayoutElement>().preferredHeight=208;
                refresh();refreshModes();
                DialogApply(body,()=>
                {
                    try
                    {
                        if(!CurrentOpenContext(context)||!GradientsReady||!SameGradientOrder(order))throw new InvalidOperationException("Карта или порядок выделения изменились. Откройте градиенты снова.");
                        var plan=SectorGradients.Plan(Map,order,target,mode);
                        if(plan.ChangeCount==0){Notify("Значения уже образуют выбранный градиент.");return false;}
                        if(!Edit(map=>plan.Apply(map),"Градиент: "+names[(int)target].ToLowerInvariant()+". Изменено секторов: "+plan.ChangeCount+". Один Undo отменяет изменения."))return false;
                        gradientTarget=target;PlayerPrefs.SetInt("MobileUDB.Gradient.Interpolation",(int)mode);return true;
                    }
                    catch(Exception error){Notify(error.Message);return false;}
                },"Применить",true);
            });
        }
        bool SameGradientOrder(List<int> order)
        {if(Selection.Count!=order.Count)return false;int i=0;foreach(var item in Selection)if(item.kind!=ElementKind.Sector||item.index!=order[i++])return false;return true;}
    }
}
