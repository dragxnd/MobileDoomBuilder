using System;
using System.Collections.Generic;
using MobileUDB.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        double LineLengthSquared(int index)
        {
            var line=Map.lines[index];var a=Map.vertices[line.start];var b=Map.vertices[line.end];
            double dx=b.x-(double)a.x,dy=b.y-(double)a.y;return dx*dx+dy*dy;
        }
        void LineLengthInfo(RectTransform body,IList<int> indices)
        {
            double squared=LineLengthSquared(indices[0]);bool common=true;
            foreach(int index in indices)if(LineLengthSquared(index)!=squared){common=false;break;}
            string value=common?Math.Sqrt(squared).ToString("0.##",System.Globalization.CultureInfo.CurrentCulture)+" маппикс.":"— (разные)";
            var label=Label(body,"Длина: "+value,18);label.name="Line length";label.alignment=TextAnchor.MiddleLeft;
            var layout=label.gameObject.AddComponent<LayoutElement>();layout.minHeight=layout.preferredHeight=32;
        }
        void LineBatchProperties()
        {
            var indices=new List<int>();foreach(var item in Selection)indices.Add(item.index);indices.Sort();
            ShowDialog("Стены · "+indices.Count,body=>
            {
                LineLengthInfo(body,indices);
                var note=Label(body,"Пусто — не менять. Флаги: Да / Нет / Не менять.",16);note.alignment=TextAnchor.MiddleLeft;note.gameObject.AddComponent<LayoutElement>().minHeight=48;
                var changes=new List<Func<MapDocument,bool>>();bool universal=MapFormats.IsUniversal(Map.format);
                var action=BatchLineActions(body,indices,changes);
                if(Map.format!=MapFormat.Hexen)
                {
                    int min=universal?int.MinValue:0,max=universal?int.MaxValue:65535;
                    var tag=BatchField(body,universal?"ID стены":"Тег",indices,m=>m.lines,l=>l.tag,n=>n.ToString(),field=>Number(field,min,max),(l,n)=>l.tag=n,changes);
                    AddButton(body,"Выбрать тег / свободный номер…",()=>TagPicker(tag,universal?15:BinaryLineTagType(action,indices),max,null,true));
                }
                BatchLineSide(body,indices,true,changes);BatchLineSide(body,indices,false,changes);
                LineBatchCaption(body,"Флаги стены");
                var flags=BinaryFlagNames(true);var masks=new List<int>(flags.Keys);masks.Sort();
                foreach(int value in masks)
                {
                    int mask=value;BatchBoolean(body,flags[mask],indices,(m,index)=>(m.lines[index].flags&mask)!=0,
                        (m,index,on)=>m.lines[index].flags=on?m.lines[index].flags|mask:m.lines[index].flags&~mask,changes);
                }
                BatchLineActivation(body,indices,changes);
                
#if MOBILEUDB_UDMF
BatchElementFields(body,indices,ElementKind.Line,changes);
#endif


                var sideNote=Label(body,"Отделка меняется только у выделенных стен с существующей стороной. Общие записи отделяются автоматически.",16);sideNote.alignment=TextAnchor.MiddleLeft;sideNote.gameObject.AddComponent<LayoutElement>().minHeight=48;
                DialogApply(body,()=>Edit(map=>
                {
                    bool changed=false;foreach(var change in changes)changed|=change(map);
                    if(!MapFormats.IsUniversal(map.format)&&map.sides.Count>65535)throw new InvalidOperationException("Превышен предел сторон Doom / Hexen.");
                    if(!changed)throw new InvalidOperationException("Нет изменений: заполните нужное поле или измените флаг.");
                },"Свойства стен обновлены. Один Undo отменяет все изменения."));
            });
        }
        void LineBatchCaption(RectTransform body,string title)
        {var text=Label(body,title,16);text.alignment=TextAnchor.MiddleLeft;text.color=new Color(.7f,.76f,.82f);text.gameObject.AddComponent<LayoutElement>().preferredHeight=28;}
        void BatchLineActivation(RectTransform body,List<int> indices,List<Func<MapDocument,bool>> changes)
        {
            if(!Actions.hasArguments)return;
            if(Map.format==MapFormat.Hexen)
            {
                var field=BatchValue(body,"Активация · код",indices,(m,index)=>m.lines[index].flags&7168,n=>n.ToString(),input=>
                {int n=Number(input,0,7168);if((n&~7168)!=0)throw new InvalidOperationException("Код активации: кратное 1024 от 0 до 7168.");return n;},
                (m,index,n)=>m.lines[index].flags=ActionCatalog.SetActivation(m.lines[index].flags,n),changes);
                var choices=new List<PickerChoice>();foreach(var item in Actions.activations)choices.Add(new PickerChoice{value=item.value,title=item.title});
                AddButton(body,"Выбрать активацию…",()=>ChoicePicker("Активация стены",choices,entry=>field.text=entry.value.ToString()));
            }
            
#if MOBILEUDB_UDMF // Deferred from the first release.
else if(MapFormats.IsUniversal(Map.format))
            {
                var activations=Actions.extended?GameConfigText.Read(ActionConfig("ZDoom_misc.cfg"),ActionConfig).Section("linedefactivations_udmf"):
                    GameConfigText.Read("playercross=\"Player walks over\";playeruse=\"Player presses use\";monstercross=\"Monster walks over\";impact=\"Projectile hits\";playerpush=\"Player bumps\";missilecross=\"Projectile crosses\";repeatspecial=\"Repeatable action\";monsteractivate=\"Monster activates\";");
                foreach(var entry in activations.values){string key=entry.Key;BatchBoolean(body,entry.Value,indices,(m,index)=>UdmfText.Boolean(m.lines[index].fields,key,false),(m,index,on)=>UdmfText.SetBool(m.lines[index].fields,key,on),changes);}
                foreach(var entry in activations.children){string key=entry.Key;BatchBoolean(body,entry.Value.Text("name",key),indices,(m,index)=>UdmfText.Boolean(m.lines[index].fields,key,false),(m,index,on)=>UdmfText.SetBool(m.lines[index].fields,key,on),changes);}
            }
#endif

        }
        MapSide BatchSide(MapDocument map,int line,bool front)
        {return map.sides[front?map.lines[line].front:map.lines[line].back];}
        MapSide ExclusiveBatchSide(MapDocument map,int line,bool front)
        {
            var target=map.lines[line];int id=front?target.front:target.back,uses=0;
            foreach(var wall in map.lines){if(wall.front==id)uses++;if(wall.back==id)uses++;}
            var side=map.sides[id];if(uses<=1)return side;
            var copy=new MapSide{sector=side.sector,offsetX=side.offsetX,offsetY=side.offsetY,upper=side.upper,middle=side.middle,lower=side.lower};side.CopyFieldsTo(copy);
            int added=map.sides.Count;map.sides.Add(copy);if(front)target.front=added;else target.back=added;return copy;
        }
        void BatchLineSide(RectTransform body,List<int> selected,bool front,List<Func<MapDocument,bool>> changes)
        {
            var indices=selected.FindAll(index=>(front?Map.lines[index].front:Map.lines[index].back)>=0);
            string prefix=front?"Передняя":"Задняя";
            if(indices.Count==0){LineBatchCaption(body,prefix+" сторона · отсутствует");return;}bool universal=MapFormats.IsUniversal(Map.format);var lines=new HashSet<int>(indices);
            BuildSideTexturePanel(body,prefix+" сторона · "+indices.Count+" / "+selected.Count,
                meta=>BatchValue(meta,prefix+" · Сектор",indices,(m,index)=>BatchSide(m,index,front).sector,n=>n.ToString(),field=>Number(field,0,Map.sectors.Count-1),(m,index,n)=>ExclusiveBatchSide(m,index,front).sector=n,changes),
                (meta,x)=>BatchInteger(meta,prefix+" · Сдвиг "+(x?"X":"Y"),indices,(m,index)=>x?BatchSide(m,index,front).offsetX:BatchSide(m,index,front).offsetY,(m,index,n)=>{if(x)ExclusiveBatchSide(m,index,front).offsetX=n;else ExclusiveBatchSide(m,index,front).offsetY=n;},universal?int.MinValue:-32768,universal?int.MaxValue:32767,changes),
                (card,part)=>BatchValue(card,prefix+" · "+SideTexturePart(part),indices,(m,index)=>SideTextureValue(BatchSide(m,index,front),part),n=>n,TextureName,(m,index,n)=>SetSideTextureValue(ExclusiveBatchSide(m,index,front),part,n),changes,false),part=>VisualSideTextureSelected(lines,front,part));
            
#if MOBILEUDB_UDMF // Deferred from the first release.
if(universal)
            {
                var managed=new HashSet<string>(StringComparer.OrdinalIgnoreCase){"sector","offsetx","offsety","texturetop","texturemiddle","texturebottom"};
                BatchUniversalPanel(body,prefix+" · Поля UDMF",indices,(m,index)=>BatchSide(m,index,front),(m,index)=>ExclusiveBatchSide(m,index,front),key=>managed.Contains(key),changes);
            }
#endif

        }
    }
}
