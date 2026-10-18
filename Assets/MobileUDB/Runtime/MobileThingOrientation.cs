using System;
using System.Collections;
using System.Collections.Generic;
using MobileUDB.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        sealed class ThingAimState
        {public OpenContext context;public List<int> targets;public bool away,snap45;public MapPoint? point;public ThingAnglePlan plan;}
        ThingAimState thingAim;
        bool thingAimTracking,thingAimValid;
        RectTransform thingAimUI;
        Text thingAimInfo;
        Button thingAimApply;
        public bool ThingAimActive {get{return Tool==MobileTool.PointThings&&thingAim!=null;}}
        string ThingOrientationPreference(string suffix){return "MobileUDB.ThingOrientation."+effectiveProfile+"."+Map.format+"."+actionEngine+"."+suffix;}
        public MapPoint? ThingAimPoint {get{return ThingAimActive&&thingAimValid?thingAim.point:null;}}
        public bool TryThingAimAngle(int index,out float angle)
        {
            angle=0;return ThingAimActive&&thingAimValid&&thingAim.plan!=null&&thingAim.plan.TryAngle(index,out angle)&&index<Map.things.Count&&angle!=Map.things[index].angle;
        }
        List<int> OrientationTargets()
        {
            var targets=new List<int>();if(Draft.Count>0||Tool==MobileTool.Paste||ThingAimActive)return targets;
            if(VisualActive)targets.AddRange(visualView.SelectedThings);
            else foreach(var item in Selection){if(item.kind!=ElementKind.Thing)return new List<int>();targets.Add(item.index);}
            foreach(int index in targets)if(index<0||index>=Map.things.Count)return new List<int>();return targets;
        }
        bool SameOrientationTargets(List<int> targets,bool visual)
        {
            if(VisualActive!=visual)return false;
            if(visual)return visualView.SelectedThings.Count==targets.Count&&new HashSet<int>(visualView.SelectedThings).SetEquals(targets);
            if(Selection.Count!=targets.Count)return false;foreach(int index in targets)if(!Selection.Contains(new ElementRef(ElementKind.Thing,index)))return false;return true;
        }
        void ThingOrientationButton(RectTransform body,bool visual=false)
        {
            LineBatchCaption(body,visual?"ПОВОРОТ":"ОБЪЕКТЫ");var row=ShapeOptionRow(body);
            VisualThingCommand(row,"Направление объектов…",EditorIcon.Symbol.ThingPoint,ThingOrientationMenu).interactable=OrientationTargets().Count>0;
        }
        void ThingOrientationMenu()
        {
            var targets=OrientationTargets();if(targets.Count==0){Notify("Выберите объекты и завершите рисование или вставку.");return;}
            // Base binary configurations include Doom_common.common (true);
            // ZDoom_common omits the flag and GameConfiguration defaults to false.
            int defaultSnap=actionEngine=="ZDoom"||(Map.format==MapFormat.Hexen&&effectiveProfile!="Hexen")?0:1;
            string snapKey=ThingOrientationPreference("Snap45"),awayKey=ThingOrientationPreference("Away");
            var context=CaptureOpenContext();bool visual=VisualActive,snap45=PlayerPrefs.GetInt(snapKey,defaultSnap)!=0,away=PlayerPrefs.GetInt(awayKey,0)!=0;
            int directional=0;foreach(int index in targets)if(!thingCatalog.Find(Map.things[index].type).fixedRotation)directional++;
            MapPoint point=new MapPoint();bool hasPoint=visual&&visualView.TryPlacementPoint(out point);
            ShowDialog("Направление · "+targets.Count,body=>
            {
                LineBatchCaption(body,"ТОЧНОСТЬ НАПРАВЛЕНИЯ");var row=ShapeOptionRow(body);Button coarse=null,fine=null,cw=null,ccw=null;
                Action refresh=()=>{coarse.GetComponent<Image>().color=snap45?Accent:Panel;fine.GetComponent<Image>().color=snap45?Panel:Accent;cw.GetComponentInChildren<Text>().text="−"+(snap45?45:5)+"°";ccw.GetComponentInChildren<Text>().text="+"+(snap45?45:5)+"°";};
                coarse=Button(row,"45°",()=>{snap45=true;PlayerPrefs.SetInt(snapKey,1);refresh();});
                fine=Button(row,"1°",()=>{snap45=false;PlayerPrefs.SetInt(snapKey,0);refresh();});
                LineBatchCaption(body,"ШАГОВЫЙ ПОВОРОТ");row=ShapeOptionRow(body);
                cw=Button(row,"",()=>RotateOrientation(context,targets,visual,true,snap45));ccw=Button(row,"",()=>RotateOrientation(context,targets,visual,false,snap45));refresh();
                LineBatchCaption(body,"К ТОЧКЕ / ОТ ТОЧКИ");row=ShapeOptionRow(body);Button toward=null,opposite=null;
                Action refreshAway=()=>{toward.GetComponent<Image>().color=away?Panel:Accent;opposite.GetComponent<Image>().color=away?Accent:Panel;};
                toward=Button(row,"К точке",()=>{away=false;PlayerPrefs.SetInt(awayKey,0);refreshAway();});opposite=Button(row,"От точки",()=>{away=true;PlayerPrefs.SetInt(awayKey,1);refreshAway();});refreshAway();
                var info=Label(body,"Направляются к точке: "+directional+" из "+targets.Count+". Типы с фиксированным направлением сохраняют угол в этой команде. Шаговый поворот меняет все выбранные объекты, как в UDB.",16);info.alignment=TextAnchor.MiddleLeft;info.gameObject.AddComponent<LayoutElement>().preferredHeight=112;
                if(visual)
                {
                    var destination=Label(body,hasPoint?"Цель: X "+Math.Round(point.x)+" · Y "+Math.Round(point.y)+". Касание поверхности, после движения камеры — прицел.":"Для цели коснитесь поверхности или наведите прицел на карту.",16);destination.alignment=TextAnchor.MiddleLeft;destination.gameObject.AddComponent<LayoutElement>().preferredHeight=88;
                    var apply=DialogApply(body,()=>PointVisualThings(context,targets,point,away,snap45),"Направить",true);apply.interactable=hasPoint&&directional>0;
                    if(!visualView.PlacementReady){destination.text="Обновление вида…";StartCoroutine(WaitOrientationPoint(context,targets,destination,apply,directional,value=>point=value));}
                }
                else
                {
                    var help=Label(body,"«Выбрать точку» открывает карту: коснитесь цели, проверьте стрелки и нажмите «Готово». Перетаскивание двигает вид; два пальца меняют масштаб. В режиме 1° шаговый поворот составляет 5°. Одно действие — один Undo.",16);help.alignment=TextAnchor.MiddleLeft;help.gameObject.AddComponent<LayoutElement>().preferredHeight=168;
                    DialogApply(body,()=>StartThingAim(context,targets,away,snap45),"Выбрать точку",true).interactable=directional>0;
                }
            });
        }
        IEnumerator WaitOrientationPoint(OpenContext context,List<int> targets,Text destination,Button apply,int directional,Action<MapPoint> set)
        {
            var owner=modal;float until=Time.realtimeSinceStartup+30;
            while(destination!=null&&modal==owner&&CurrentOpenContext(context)&&SameOrientationTargets(targets,true)&&!visualView.PlacementReady&&Time.realtimeSinceStartup<until)yield return null;
            if(destination==null||modal!=owner||!CurrentOpenContext(context)||!SameOrientationTargets(targets,true))yield break;
            MapPoint point;bool hit=visualView.TryPlacementPoint(out point);if(hit)set(point);
            destination.text=hit?"Цель: X "+Math.Round(point.x)+" · Y "+Math.Round(point.y)+". Касание поверхности, после движения камеры — прицел.":"Для цели коснитесь поверхности или наведите прицел на карту.";
            apply.interactable=hit&&directional>0;
        }
        void RotateOrientation(OpenContext context,List<int> targets,bool visual,bool clockwise,bool snap45)
        {
            try
            {
                if(!CurrentOpenContext(context)||!SameOrientationTargets(targets,visual))throw new InvalidOperationException("Карта или выделение изменились. Откройте направление снова.");
                var plan=ThingOrientation.Step(Map,targets,clockwise,snap45);
                if(plan.ChangeCount==0){Notify("Нет изменений углов.");return;}
                if(Edit(map=>plan.Apply(map),"Объекты повёрнуты · "+plan.ChangeCount+". Один Undo отменяет изменения."))ThingOrientationMenu();
            }
            catch(Exception error){Notify(error.Message);}
        }
        bool PointVisualThings(OpenContext context,List<int> targets,MapPoint point,bool away,bool snap45)
        {
            try
            {
                if(!CurrentOpenContext(context)||!SameOrientationTargets(targets,true))throw new InvalidOperationException("Карта или выделение изменились. Откройте направление снова.");
                var plan=ThingOrientation.PointAt(Map,targets,point,away,snap45,type=>thingCatalog.Find(type).fixedRotation);
                if(plan.ChangeCount==0){Notify("Объекты уже имеют нужное направление.");return false;}
                return Edit(map=>plan.Apply(map),"Направление изменено · "+plan.ChangeCount+". Один Undo отменяет изменения.");
            }
            catch(Exception error){Notify(error.Message);return false;}
        }
        bool StartThingAim(OpenContext context,List<int> targets,bool away,bool snap45)
        {
            if(!CurrentOpenContext(context)||!SameOrientationTargets(targets,false)){Notify("Карта или выделение изменились. Откройте направление снова.");return false;}
            SetTool(MobileTool.PointThings);thingAim=new ThingAimState{context=context,targets=new List<int>(targets),away=away,snap45=snap45};Changed(false);
            Notify("Коснитесь цели и проверьте стрелки. «Готово» применит направление; «Отмена» сохранит прежние углы.");return true;
        }
        void ClearThingAimState(){thingAim=null;thingAimTracking=thingAimValid=false;if(thingAimUI!=null)thingAimUI.gameObject.SetActive(false);if(View!=null)View.SetVerticesDirty();}
        void CancelThingAim(){ClearThingAimState();SetTool(MobileTool.Select);Notify("Выбор цели отменён.");}
        bool BeginThingAimPointer(){if(!ThingAimActive)return false;thingAimTracking=true;return true;}
        bool MoveThingAimPointer(Vector2 previous,Vector2 screen)
        {if(!ThingAimActive)return false;if(moved)View.Pan(previous,screen);return true;}
        bool EndThingAimPointer(Vector2 screen)
        {
            if(!ThingAimActive)return false;bool tapped=thingAimTracking&&!moved&&View.ScreenRect.Contains(screen);thingAimTracking=false;
            if(tapped)
            {
                try
                {
                    if(!CurrentOpenContext(thingAim.context)||!SameOrientationTargets(thingAim.targets,false))throw new InvalidOperationException("Карта или выделение изменились. Отмените выбор цели.");
                    var point=View.World(screen);var plan=ThingOrientation.PointAt(Map,thingAim.targets,point,thingAim.away,thingAim.snap45,type=>thingCatalog.Find(type).fixedRotation);
                    thingAim.point=point;thingAim.plan=plan;Changed(false);Notify("Предпросмотр · изменится объектов: "+plan.ChangeCount+". «Готово» — применить.");
                }
                catch(Exception error){Notify(error.Message);}
            }
            return true;
        }
        void ApplyThingAim()
        {
            try
            {
                if(!ThingAimActive||thingAim.plan==null)throw new InvalidOperationException("Сначала коснитесь цели.");
                if(!CurrentOpenContext(thingAim.context)||!SameOrientationTargets(thingAim.targets,false))throw new InvalidOperationException("Карта или выделение изменились. Отмените выбор цели.");
                var plan=thingAim.plan;if(plan.ChangeCount==0){ClearThingAimState();SetTool(MobileTool.Select);Notify("Объекты уже имеют нужное направление.");return;}
                if(Edit(map=>plan.Apply(map),"Направление изменено · "+plan.ChangeCount+". Один Undo отменяет изменения.")){ClearThingAimState();SetTool(MobileTool.Select);}
            }
            catch(Exception error){Notify(error.Message);}
        }
        void BuildThingAimUI(RectTransform viewport)
        {
            thingAimUI=Rect("Направление объектов",viewport);thingAimUI.anchorMin=new Vector2(0,1);thingAimUI.anchorMax=Vector2.one;thingAimUI.pivot=new Vector2(.5f,1);thingAimUI.sizeDelta=new Vector2(-24,116);thingAimUI.anchoredPosition=new Vector2(0,-10);thingAimUI.gameObject.AddComponent<Image>().color=Background;
            thingAimInfo=Label(thingAimUI,"",16);var info=thingAimInfo.rectTransform;info.anchorMin=new Vector2(0,.5f);info.anchorMax=Vector2.one;info.offsetMin=new Vector2(8,0);info.offsetMax=new Vector2(-8,0);
            var apply=Button(thingAimUI,"Готово",ApplyThingAim);apply.name="Применить направление";var ar=apply.GetComponent<RectTransform>();ar.anchorMin=Vector2.zero;ar.anchorMax=new Vector2(.5f,0);ar.pivot=new Vector2(.5f,0);ar.sizeDelta=new Vector2(-12,52);ar.anchoredPosition=new Vector2(0,4);thingAimApply=apply;
            var cancel=Button(thingAimUI,"Отмена",CancelThingAim);cancel.name="Отменить направление";var cr=cancel.GetComponent<RectTransform>();cr.anchorMin=new Vector2(.5f,0);cr.anchorMax=new Vector2(1,0);cr.pivot=new Vector2(.5f,0);cr.sizeDelta=new Vector2(-12,52);cr.anchoredPosition=new Vector2(0,4);
            thingAimUI.gameObject.SetActive(false);
        }
        void RefreshThingAimUI()
        {
            if(thingAim!=null&&(Tool!=MobileTool.PointThings||VisualActive))ClearThingAimState();
            if(thingAimUI==null)return;thingAimUI.gameObject.SetActive(ThingAimActive);if(!ThingAimActive)return;
            thingAimValid=CurrentOpenContext(thingAim.context)&&SameOrientationTargets(thingAim.targets,false);
            thingAimApply.interactable=thingAimValid&&thingAim.plan!=null;
            thingAimInfo.text=!thingAimValid?"Карта изменилась · отмените выбор цели":(thingAim.away?"От точки":"К точке")+" · "+(thingAim.snap45?"45°":"1°")+"\n"+(thingAim.point.HasValue?"Цель: "+Math.Round(thingAim.point.Value.x)+", "+Math.Round(thingAim.point.Value.y)+" · изменится "+thingAim.plan.ChangeCount:"Коснитесь цели на карте");
        }
    }
}
