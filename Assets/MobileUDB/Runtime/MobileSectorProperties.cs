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
        bool MultipleSectorsSelected()
        {return MultipleSelected(ElementKind.Sector);}
        InputField SectorBatchField<T>(RectTransform body,string title,List<int> indices,Func<MapSector,T> get,Func<T,string> format,Func<InputField,T> parse,Action<MapSector,T> set,List<Func<MapDocument,bool>> changes,bool numeric=true)
        {
            return BatchField(body,title,indices,map=>map.sectors,get,format,parse,set,changes,numeric);
        }
        Action SectorHeightInfo(RectTransform body,IList<int> indices)
        {
            var label=Label(body,"",18);label.name="Sector height";label.alignment=TextAnchor.MiddleLeft;
            var layout=label.gameObject.AddComponent<LayoutElement>();layout.minHeight=layout.preferredHeight=32;
            Action refresh=()=>
            {
                double first=Map.sectors[indices[0]].ceiling-(double)Map.sectors[indices[0]].floor;bool common=true;
                foreach(int index in indices)if(Map.sectors[index].ceiling-(double)Map.sectors[index].floor!=first){common=false;break;}
                label.text="Высота сектора: "+(common?first.ToString("0.##",CultureInfo.CurrentCulture)+" маппикс.":"— (разные)");
            };
            refresh();return refresh;
        }
        void SectorBatchProperties()
        {
            var indices=new List<int>();foreach(var item in Selection)indices.Add(item.index);indices.Sort();
            ShowDialog("Свойства · секторов: "+indices.Count,body=>
            {
                var refreshHeight=SectorHeightInfo(body,indices);
                var note=Label(body,"Пусто — не менять. Кнопка у числа выбирает = / + / − / × / ÷.",16);note.alignment=TextAnchor.MiddleLeft;note.gameObject.AddComponent<LayoutElement>().minHeight=48;
                var changes=new List<Func<MapDocument,bool>>();bool universal=MapFormats.IsUniversal(Map.format);
                BatchReal(body,"Пол",indices,(m,index)=>m.sectors[index].floor,(m,index,n)=>m.sectors[index].floor=n,-32768,32767,changes);
                BatchReal(body,"Потолок",indices,(m,index)=>m.sectors[index].ceiling,(m,index,n)=>m.sectors[index].ceiling=n,-32768,32767,changes);
                BatchInteger(body,"Свет",indices,(m,index)=>m.sectors[index].light,(m,index,n)=>m.sectors[index].light=n,universal?int.MinValue:-32768,universal?int.MaxValue:32767,changes);
                int min=universal?int.MinValue:0,max=universal?int.MaxValue:65535;
                var effect=SectorBatchField(body,"Эффект",indices,s=>s.special,n=>n.ToString(),field=>Number(field,min,max),(s,n)=>s.special=n,changes);
                AddButton(body,"Выбрать эффект по названию…",()=>ActionPicker(effect,true));
                if(Actions.generalizedSectors.Count>0)AddButton(body,"Настроить обобщённый эффект…",()=>GeneralizedPicker(effect,true,true));
                var tag=SectorBatchField(body,"Тег",indices,s=>s.tag,n=>n.ToString(),field=>Number(field,min,max),(s,n)=>s.tag=n,changes);
                AddButton(body,"Выбрать тег / свободный номер…",()=>TagPicker(tag,13,max,null,true));
                var floor=SectorBatchField(body,"Текстура пола",indices,s=>s.floorTexture,n=>n,TextureName,(s,n)=>s.floorTexture=n,changes,false);
                PropertyTextureButton(body,"Выбрать: Текстура пола…",floor,ResourceKind.Flat);
                var ceiling=SectorBatchField(body,"Потолка",indices,s=>s.ceilingTexture,n=>n,TextureName,(s,n)=>s.ceilingTexture=n,changes,false);
                PropertyTextureButton(body,"Выбрать: Потолка…",ceiling,ResourceKind.Flat);
                
#if MOBILEUDB_UDMF
BatchElementFields(body,indices,ElementKind.Sector,changes);
#endif

                var applyButton=DialogApply(body,()=>Edit(map=>
                {
                    bool changed=false;foreach(var change in changes)changed|=change(map);
                    foreach(int index in indices)if(map.sectors[index].floor>map.sectors[index].ceiling)
                        throw new InvalidOperationException("Сектор #"+index+": пол выше потолка. Изменения не применены.");
                    if(!changed)throw new InvalidOperationException("Нет изменений: заполните нужное поле.");
                },"Свойства секторов обновлены. Один Undo отменяет все изменения."));
                applyButton.onClick.AddListener(()=>refreshHeight());
            });
        }
    }
}
