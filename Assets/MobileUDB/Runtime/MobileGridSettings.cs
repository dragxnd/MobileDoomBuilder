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
        string GridStepText {get{return gridFrame.step.ToString("0.########",CultureInfo.InvariantCulture)+(gridFrame.dynamicGrid?" · авто":"");}}
        internal void MatchDynamicGrid()
        {
            if(Map==null||View==null||VisualActive||classicViewRestorePending||dragOrigin!=null||!gridFrame.dynamicGrid)return;
            var rect=View.rectTransform.rect;if(rect.width<=0||rect.height<=0)return;
            double step=GridFrame.DynamicStep(rect.width,rect.height,View.zoom);if(gridFrame.step==step)return;
            gridFrame.step=step;mapGrids[MapNaming.Key(Map)]=gridFrame.Copy();
            viewRecoveryRevision.Changed();saveAt=Time.unscaledTime+2;Changed(false);
        }
        void GridSettingsButton(RectTransform body)
        {CurveCaption(body,"СЕТКА");var row=ShapeOptionRow(body);VisualThingCommand(row,"Сетка · шаг "+GridStepText+"…",EditorIcon.Symbol.Grid,GridSettingsMenu);}
        void GridSettingsMenu()
        {
            var context=CaptureOpenContext();var selection=new List<ElementRef>(Selection);var next=gridFrame.Validated();var mode=Tool;
            int line=-1,vertices=0,things=0,lines=0;MapPoint? origin=null;
            foreach(var item in selection)
            {
                if(item.kind==ElementKind.Line){lines++;line=item.index;}
                if(item.kind==ElementKind.Vertex){vertices++;origin=Map.vertices[item.index].Position;}
            }
            if(vertices==0)foreach(var item in selection)if(item.kind==ElementKind.Thing){things++;origin=Map.things[item.index].Position;}
            bool canOrigin=vertices==1||(vertices==0&&things==1);
            ShowDialog("Сетка",body=>
            {
                body.GetComponent<VerticalLayoutGroup>().spacing=5;InputField size=null;Action refreshSize=null;Button manual=null,automatic=null;
                Action refreshMode=()=>{manual.GetComponent<Image>().color=next.dynamicGrid?Panel:Accent;automatic.GetComponent<Image>().color=next.dynamicGrid?Accent:Panel;};
                CurveCaption(body,"РАЗМЕР СЕТКИ");var row=ShapeOptionRow(body);
                manual=Button(row,"Вручную",()=>{next.dynamicGrid=false;refreshMode();});
                automatic=VisualThingCommand(row,"Авто",EditorIcon.Symbol.GridDynamic,()=>{next.dynamicGrid=true;next.step=GridFrame.DynamicStep(View.rectTransform.rect.width,View.rectTransform.rect.height,View.zoom);size.SetTextWithoutNotify(next.step.ToString("R",CultureInfo.InvariantCulture));refreshMode();refreshSize();});refreshMode();
                var autoHelp=Label(body,"Авто: шаг меняется вместе с масштабом. Ввод шага и ÷2 / ×2 включают ручной режим.",16);autoHelp.gameObject.AddComponent<LayoutElement>().preferredHeight=64;
                CurveCaption(body,"ШАГ · 1–65536");row=ShapeOptionRow(body);
                var smaller=VisualThingCommand(row,"÷2",EditorIcon.Symbol.GridDecrease,()=>{next.dynamicGrid=false;refreshMode();StepGridSize(size,.5);});
                size=GridInlineField(row,"",next.step.ToString("R",CultureInfo.InvariantCulture));
                var larger=VisualThingCommand(row,"×2",EditorIcon.Symbol.GridIncrease,()=>{next.dynamicGrid=false;refreshMode();StepGridSize(size,2);});
                refreshSize=()=>{double n;bool valid=double.TryParse(size.text,NumberStyles.Float,CultureInfo.InvariantCulture,out n)&&n>=1&&n<=65536;smaller.interactable=valid&&n>1;larger.interactable=valid&&n<65536;};size.onValueChanged.AddListener(_=>{next.dynamicGrid=false;refreshMode();refreshSize();});refreshSize();
                row=ShapeOptionRow(body);row.GetComponent<LayoutElement>().preferredHeight=77;
                var snapGroup=GridControlGroup(row,"ПРИВЯЗКА",1);var moveGroup=GridControlGroup(row,"ПЕРЕНОС",2);Button enabled=null,absolute=null,increment=null;
                Action refreshSnap=()=>{enabled.GetComponentInChildren<Text>().text=next.snap?"✓ Вкл":"Выкл";enabled.GetComponent<Image>().color=next.snap?Accent:Panel;absolute.GetComponent<Image>().color=!next.increment?Accent:Panel;increment.GetComponent<Image>().color=next.increment?Accent:Panel;};
                enabled=Button(snapGroup,"",()=>{next.snap=!next.snap;refreshSnap();});absolute=Button(moveGroup,"К сетке",()=>{next.increment=false;refreshSnap();});increment=Button(moveGroup,"С шагом",()=>{next.increment=true;refreshSnap();});refreshSnap();
                CurveCaption(body,"НАПРАВЛЕНИЕ ПЕРЕНОСА");row=ShapeOptionRow(body);Button freeDirection=null,cardinalDirection=null;
                Action refreshDirection=()=>{freeDirection.GetComponent<Image>().color=next.cardinalDrag?Panel:Accent;cardinalDirection.GetComponent<Image>().color=next.cardinalDrag?Accent:Panel;};
                freeDirection=Button(row,"Свободно",()=>{next.cardinalDrag=false;refreshDirection();});cardinalDirection=VisualThingCommand(row,"90°",EditorIcon.Symbol.Move,()=>{next.cardinalDrag=true;refreshDirection();});refreshDirection();
                var directionHelp=Label(body,"90° включает сетку и перенос с шагом. Привязка к соседним элементам сохраняет приоритет.",16);directionHelp.gameObject.AddComponent<LayoutElement>().preferredHeight=76;
                CurveCaption(body,"СОЕДИНЕНИЕ ГЕОМЕТРИИ");row=ShapeOptionRow(body);Button nearby=null;
                Action refreshNearby=()=>{nearby.GetComponentInChildren<Text>().text=next.disableNearbySnap?"Без сшивки":"✓ К вершинам / стенам";nearby.GetComponent<Image>().color=next.disableNearbySnap?Panel:Accent;};
                nearby=VisualThingCommand(row,"",EditorIcon.Symbol.Merge,()=>{next.disableNearbySnap=!next.disableNearbySnap;refreshNearby();});refreshNearby();
                var nearbyHelp=Label(body,"Привязка и сшивка при переносе вершин, стен и секторов.",16);nearbyHelp.gameObject.AddComponent<LayoutElement>().preferredHeight=40;
                CurveCaption(body,"ПРИВЯЗКА ОБЪЕКТОВ");row=ShapeOptionRow(body);Button nearbyThings=null;
                Action refreshThings=()=>{nearbyThings.GetComponentInChildren<Text>().text=next.disableNearbyThings?"Только сетка":"✓ К объектам";nearbyThings.GetComponent<Image>().color=next.disableNearbyThings?Panel:Accent;};
                nearbyThings=VisualThingCommand(row,"",EditorIcon.Symbol.Thing,()=>{next.disableNearbyThings=!next.disableNearbyThings;next.nearbyThingsPreferenceSet=true;refreshThings();});refreshThings();
                var thingsHelp=Label(body,"По умолчанию — только сетка. «К объектам» притягивает перемещаемый объект к центру соседнего.",16);thingsHelp.gameObject.AddComponent<LayoutElement>().preferredHeight=76;
                CurveCaption(body,"КРУГИ ОБЪЕКТОВ");row=ShapeOptionRow(body);Button mapSize=null,cappedSize=null;
                Action refreshCircles=()=>{mapSize.GetComponent<Image>().color=next.fixedThingScale?Panel:Accent;cappedSize.GetComponent<Image>().color=next.fixedThingScale?Accent:Panel;};
                mapSize=Button(row,"По масштабу",()=>{next.fixedThingScale=false;refreshCircles();});cappedSize=Button(row,"До 48",()=>{next.fixedThingScale=true;refreshCircles();});refreshCircles();
                CurveCaption(body,"ПОВОРОТ · ГРАДУСЫ");row=ShapeOptionRow(body);InputField angle=null;
                Button(row,"−15°",()=>StepGridAngle(angle,-15));angle=GridInlineField(row,"",Degrees(next.rotation));Button(row,"+15°",()=>StepGridAngle(angle,15));
                CurveCaption(body,"НАЧАЛО · X / Y");row=ShapeOptionRow(body);
                var x=GridInlineField(row,"X",next.originX.ToString("R",CultureInfo.InvariantCulture));var y=GridInlineField(row,"Y",next.originY.ToString("R",CultureInfo.InvariantCulture));
                CurveCaption(body,"ПО ВЫДЕЛЕНИЮ");row=ShapeOptionRow(body);
                VisualThingCommand(row,"По стене",EditorIcon.Symbol.Wall,()=>
                {try{next.Align(Map,line);angle.text=Degrees(next.rotation);x.text=next.originX.ToString("R",CultureInfo.InvariantCulture);y.text=next.originY.ToString("R",CultureInfo.InvariantCulture);}catch(Exception error){Notify(error.Message);}}).interactable=lines==1;
                VisualThingCommand(row,"Начало по выбору",EditorIcon.Symbol.Vertex,()=>{x.text=origin.Value.x.ToString("R",CultureInfo.InvariantCulture);y.text=origin.Value.y.ToString("R",CultureInfo.InvariantCulture);}).interactable=canOrigin;
                row=ShapeOptionRow(body);Button(row,"Сброс поворота и начала",()=>{next.rotation=next.originX=next.originY=0;angle.text=x.text=y.text="0";});
                DialogApply(body,()=>
                {
                    try
                    {
                        if(!CurrentOpenContext(context)||Tool!=mode||Selection.Count!=selection.Count)throw new InvalidOperationException("Карта, режим или выделение изменились.");
                        int i=0;foreach(var item in Selection)if(!item.Equals(selection[i++]))throw new InvalidOperationException("Порядок выделения изменился.");
                        next.step=GridNumber(size,1,65536);double degrees=GridNumber(angle,-360,360);if(angle.text!=Degrees(next.rotation))next.rotation=degrees/57.295779513082320876798154814105;next.originX=GridNumber(x,-32768,32767);next.originY=GridNumber(y,-32768,32767);
                        if(next.dynamicGrid)next.step=GridFrame.DynamicStep(View.rectTransform.rect.width,View.rectTransform.rect.height,View.zoom);
                        gridFrame=next.Validated();mapGrids[MapNaming.Key(Map)]=gridFrame.Copy();Changed();Notify("Сетка настроена. Карта не изменена; параметры включены в сохранение проекта.");return true;
                    }
                    catch(Exception error){Notify(error.Message);return false;}
                },"Применить",true);
            });
        }
        RectTransform GridControlGroup(RectTransform parent,string caption,float weight)
        {
            var group=Rect(caption,parent);var element=group.gameObject.AddComponent<LayoutElement>();element.minWidth=0;element.preferredWidth=0;element.flexibleWidth=weight;
            var layout=group.gameObject.AddComponent<VerticalLayoutGroup>();layout.spacing=5;layout.childControlWidth=layout.childForceExpandWidth=layout.childControlHeight=true;layout.childForceExpandHeight=false;
            CurveCaption(group,caption);return ShapeOptionRow(group);
        }
        InputField GridInlineField(RectTransform row,string name,string value,bool integer=false)
        {
            var field=Field(row,name,value,false);field.name="Сетка число · "+(name==""?value:name);field.contentType=integer?InputField.ContentType.IntegerNumber:InputField.ContentType.DecimalNumber;
            var parent=(RectTransform)field.transform.parent;var layout=parent.GetComponent<LayoutElement>();layout.preferredHeight=52;layout.preferredWidth=110;layout.flexibleWidth=1;
            var label=parent.GetChild(0).GetComponent<Text>();label.fontSize=16;label.rectTransform.anchorMax=new Vector2(.16f,1);if(name=="")label.gameObject.SetActive(false);
            var rect=field.GetComponent<RectTransform>();rect.anchorMin=new Vector2(name==""?0:.18f,0);rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;return field;
        }
        static string Degrees(double radians)
        {double degrees=radians*57.295779513082320876798154814105;degrees%=360;return degrees.ToString("0.########",CultureInfo.InvariantCulture);}
        static double GridNumber(InputField field,double min,double max)
        {
            double n;if((!double.TryParse(field.text,NumberStyles.Float,CultureInfo.InvariantCulture,out n)&&!double.TryParse(field.text,NumberStyles.Float,CultureInfo.CurrentCulture,out n))||double.IsNaN(n)||double.IsInfinity(n)||n<min||n>max)throw new InvalidOperationException("Число должно быть от "+min+" до "+max+".");return n;
        }
        static void StepGridAngle(InputField field,int step)
        {double n;if(double.TryParse(field.text,NumberStyles.Float,CultureInfo.InvariantCulture,out n)&&n>= -360&&n<=360)field.text=Math.Max(-360,Math.Min(360,n+step)).ToString("0.########",CultureInfo.InvariantCulture);}
        static void StepGridSize(InputField field,double factor)
        {double n;if(double.TryParse(field.text,NumberStyles.Float,CultureInfo.InvariantCulture,out n)&&n>=1&&n<=65536)field.text=Math.Max(1,Math.Min(65536,n*factor)).ToString("0.########",CultureInfo.InvariantCulture);}
        MapPoint SnappedMoveDelta(MapPoint delta)
        {
            if(dragThingSnap!=null)return dragThingSnap.Offset(delta,Math.Max(.0001,View.zoom));
            if(dragSnap!=null)return dragSnap.Offset(delta,20/Math.Max(.0001,View.zoom));
            if(dragOrigin==null||dragSelection==null)return gridFrame.MoveOffset(dragStart,delta);
            var anchor=gridFrame.NearestAnchor(dragOrigin,dragSelection,dragStart);var offset=gridFrame.MoveOffset(anchor,delta);
            float minX=float.MaxValue,minY=float.MaxValue,maxX=float.MinValue,maxY=float.MinValue;
            Action<MapPoint> bounds=p=>{minX=Math.Min(minX,p.x);maxX=Math.Max(maxX,p.x);minY=Math.Min(minY,p.y);maxY=Math.Max(maxY,p.y);};
            foreach(int index in dragOrigin.SelectedVertices(dragSelection))bounds(dragOrigin.vertices[index].Position);
            foreach(var item in dragSelection)if(item.kind==ElementKind.Thing)bounds(dragOrigin.things[item.index].Position);
            if(minX!=float.MaxValue){offset.x=Math.Max(-32768-minX,Math.Min(32767-maxX,offset.x));offset.y=Math.Max(-32768-minY,Math.Min(32767-maxY,offset.y));}
            return offset;
        }
    }
}
