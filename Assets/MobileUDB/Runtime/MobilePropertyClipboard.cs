using System;
using System.Collections.Generic;
using MobileUDB.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        readonly BinaryPropertyClipboard propertyClipboard=new BinaryPropertyClipboard();
        PropertyFields propertyPasteOptions=PropertyFields.All;
        sealed class PropertyTargets
        {
            public List<ElementRef> elements=new List<ElementRef>();public List<SurfaceRef> surfaces;
            public VisualView.SelectionSnapshot visual;public PropertyFields fields;
        }
        bool PropertySource(out ElementRef source,out SurfaceRef? wall)
        {
            source=default(ElementRef);wall=null;
            if(VisualActive)
            {
                if(visualView.PickedThing>=0){source=new ElementRef(ElementKind.Thing,visualView.PickedThing);return source.index<Map.things.Count;}
                if(!visualView.Picked.HasValue)return false;var id=visualView.Picked.Value;
                if(!SurfaceTextureEdits.Valid(Map,id))return false;
                source=new ElementRef(id.IsFlat?ElementKind.Sector:ElementKind.Line,id.IsFlat?id.sector:id.line);if(!id.IsFlat)wall=id;return true;
            }
            bool found=false;foreach(var id in Selection)if(id.kind==Kind&&id.kind!=ElementKind.Vertex&&(!found||id.index<source.index)){source=id;found=true;}
            return found&&Draft.Count==0&&Tool!=MobileTool.Paste;
        }
        PropertyTargets PropertySelection()
        {
            var result=new PropertyTargets();
            if(VisualActive)
            {
                result.visual=visualView.SnapshotSelection();result.surfaces=result.visual.surfaces;
                foreach(int index in result.visual.things)result.elements.Add(new ElementRef(ElementKind.Thing,index));
                foreach(var id in result.surfaces)result.fields|=id.IsFlat?PropertyFields.Sectors:PropertyFields.Lines|PropertyFields.Sides;
            }
            else if(Draft.Count==0&&Tool!=MobileTool.Paste)foreach(var id in Selection)result.elements.Add(id);
            foreach(var id in result.elements)result.fields|=id.kind==ElementKind.Line?PropertyFields.Lines|PropertyFields.Sides:id.kind==ElementKind.Sector?PropertyFields.Sectors:id.kind==ElementKind.Thing?PropertyFields.Things:PropertyFields.None;
            result.fields&=propertyClipboard.Available(Map.format,!VisualActive);return result;
        }
        void PropertyClipboardButtons(Transform body)
        {
            ElementRef source;SurfaceRef? wall;bool copy=PropertySource(out source,out wall);bool paste=PropertySelection().fields!=PropertyFields.None;
            AddButton(body,"Копировать свойства",()=>{CloseDialog();CopyProperties();}).interactable=copy;
            AddButton(body,"Вставить свойства",()=>{CloseDialog();PasteProperties(PropertyFields.All,null);}).interactable=paste;
            AddButton(body,"Выбрать свойства для вставки…",PropertyPasteOptions).interactable=paste;
        }
        void CopyProperties()
        {
            ElementRef source;SurfaceRef? wall;if(!PropertySource(out source,out wall)){Notify("Выберите стену, сектор или объект.");return;}
            try
            {
                if(source.kind==ElementKind.Line){if(wall.HasValue)propertyClipboard.CopyWall(Map,wall.Value);else propertyClipboard.CopyLine(Map,source.index);}
                else if(source.kind==ElementKind.Sector)propertyClipboard.CopySector(Map,source.index);
                else propertyClipboard.CopyThing(Map,source.index);
                Notify("Свойства скопированы · "+(source.kind==ElementKind.Line?"стена":source.kind==ElementKind.Sector?"сектор":"объект")+" #"+source.index+". Выберите цель → «Ещё → Вставить свойства».");
            }
            catch(Exception error){Notify(error.Message);}
        }
        bool PasteProperties(PropertyFields fields,PropertyTargets targets)
        {
            CancelDrag();if(targets==null)targets=PropertySelection();
            try
            {
                var plan=propertyClipboard.Plan(Map,targets.elements,targets.surfaces,fields&targets.fields);
                if(plan.ChangeCount==0){Notify("Подходящие свойства уже совпадают либо ни одно поле не отмечено.");return false;}
                SurfaceBatchResult result=null;
                if(!Edit(m=>result=plan.Apply(m),"Свойства вставлены. Один Undo отменяет всю группу."))return false;
                if(targets.visual!=null)visualView.RebindSelection(result,targets.visual);return true;
            }
            catch(Exception error){Notify(error.Message);return false;}
        }
        void PropertyPasteOptions()
        {
            var targets=PropertySelection();if(targets.fields==PropertyFields.None){Notify("Сначала скопируйте подходящие свойства.");return;}
            var context=CaptureOpenContext();var pending=propertyPasteOptions&targets.fields;
            ShowDialog("Какие свойства вставить",body=>
            {
                AddLabel(body,"Отметьте нужные свойства. Геометрия и положение объектов сохраняются. Типы и действия копируются по номеру; проверьте совместимость настроек карт.");
                var refresh=new List<Action>();
                AddButton(body,"Отметить все поля",()=>{pending=targets.fields;foreach(var update in refresh)update();});
                AddButton(body,"Снять все поля",()=>{pending=PropertyFields.None;foreach(var update in refresh)update();});
                Action<PropertyFields,string> option=(field,title)=>
                {
                    if((targets.fields&field)==0)return;Button button=null;
                    Action update=()=>button.GetComponentInChildren<Text>().text=((pending&field)!=0?"✓ ":"○ ")+title;
                    button=AddButton(body,"",()=>{pending^=field;update();});refresh.Add(update);update();
                };
                if((targets.fields&PropertyFields.Lines)!=0)
                {
                    LineBatchCaption(body,"СТЕНА · ДЕЙСТВИЕ И ФЛАГИ");
                    option(PropertyFields.LineAction,"Действие стены");option(PropertyFields.LineArguments,"Аргументы стены");option(PropertyFields.Activation,"Активация стены");option(PropertyFields.LineTag,"Тег стены");option(PropertyFields.LineFlags,"Флаги стены");
                }
                if((targets.fields&PropertyFields.Sides)!=0)
                {
                    LineBatchCaption(body,"СТОРОНА · ТЕКСТУРЫ И СМЕЩЕНИЯ");
                    option(PropertyFields.Upper,"Верхняя текстура");option(PropertyFields.Middle,"Средняя текстура");option(PropertyFields.Lower,"Нижняя текстура");option(PropertyFields.OffsetX,"Смещение X");option(PropertyFields.OffsetY,"Смещение Y");
                }
                if((targets.fields&PropertyFields.Sectors)!=0)
                {
                    LineBatchCaption(body,"СЕКТОР");
                    option(PropertyFields.FloorHeight,"Высота пола");option(PropertyFields.CeilingHeight,"Высота потолка");option(PropertyFields.FloorTexture,"Текстура пола");option(PropertyFields.CeilingTexture,"Текстура потолка");option(PropertyFields.Light,"Свет сектора");option(PropertyFields.SectorEffect,"Эффект сектора");option(PropertyFields.SectorTag,"Тег сектора");
                }
                if((targets.fields&PropertyFields.Things)!=0)
                {
                    LineBatchCaption(body,"ОБЪЕКТ");
                    option(PropertyFields.ThingType,"Тип объекта");option(PropertyFields.ThingAngle,"Угол объекта");option(PropertyFields.ThingHeight,"Высота объекта");option(PropertyFields.ThingFlags,"Флаги объекта");option(PropertyFields.ThingAction,"Действие объекта");option(PropertyFields.ThingArguments,"Аргументы объекта");option(PropertyFields.ThingTag,"Тег объекта");
                }
                DialogApply(body,()=>
                {
                    if(!CurrentOpenContext(context)){Notify("Карта изменилась. Выберите цели и откройте вставку снова.");return false;}
                    bool applied=PasteProperties(pending,targets);if(applied)propertyPasteOptions=pending;return applied;
                },closeOnSuccess:true);
            });
        }
    }
}
