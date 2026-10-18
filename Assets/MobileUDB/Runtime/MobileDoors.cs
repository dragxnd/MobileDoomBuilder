using System;
using System.Collections.Generic;
using MobileUDB.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        DoorOptions lastDoorOptions;
        string lastDoorProfile;
        bool DoorsReady
        {
            get
            {
                if(VisualActive||Draft.Count!=0||Tool==MobileTool.Paste||Selection.Count==0)return false;
                foreach(var item in Selection)if(item.kind!=ElementKind.Sector||item.index<0||item.index>=Map.sectors.Count)return false;
                return true;
            }
        }
        void DoorButton(RectTransform body)
        {
            LineBatchCaption(body,"СЕКТОРЫ");var row=ShapeOptionRow(body);
            VisualThingCommand(row,"Создать двери…",EditorIcon.Symbol.Door,DoorMenu).interactable=DoorsReady;
            VisualThingCommand(row,"Градиенты…",EditorIcon.Symbol.BrightnessGradient,SectorGradientMenu).interactable=GradientsReady;
        }
        InputField DoorTextureField(RectTransform body,string title,string value,ResourceKind kind)
        {
            var field=Field(body,title,value??"",false);field.characterLimit=8;field.textComponent.fontSize=18;
            var row=(RectTransform)field.transform.parent;row.GetComponentInChildren<Text>().fontSize=18;
            field.GetComponent<RectTransform>().offsetMax=new Vector2(-60,0);
            var pick=Button(row,"",()=>TexturePicker(field,kind));pick.name="Выбрать: "+title;
            var rect=pick.GetComponent<RectTransform>();rect.anchorMin=rect.anchorMax=Vector2.one;rect.pivot=Vector2.one;rect.sizeDelta=new Vector2(52,52);rect.anchoredPosition=new Vector2(0,-3);
            var icon=Rect("Каталог текстур",rect);icon.anchorMin=icon.anchorMax=new Vector2(.5f,.5f);icon.sizeDelta=new Vector2(24,24);icon.gameObject.AddComponent<EditorIcon>().symbol=EditorIcon.Symbol.Texture;
            return field;
        }
        void DoorMenu()
        {
            if(!DoorsReady){Notify("Завершите рисование и выберите только секторы дверей в 2D.");return;}
            DoorConfiguration config;
            try{config=DoorConfiguration.Load(ActionConfig,effectiveProfile,Map.format);}
            catch(Exception error){Notify(error.Message);return;}
            var context=CaptureOpenContext();var selection=new HashSet<ElementRef>(Selection);var indices=new List<int>();foreach(var item in selection)indices.Add(item.index);indices.Sort();
            string profile=effectiveProfile+"/"+Map.format;
            var remembered=profile==lastDoorProfile?lastDoorOptions:null;
            var next=new DoorOptions{door=remembered==null?config.door:remembered.door,track=remembered==null?config.track:remembered.track,ceiling=remembered==null?config.ceiling:remembered.ceiling,
                resetOffsets=PlayerPrefs.GetInt("MobileUDB.Doors.ResetOffsets",1)!=0,applyActions=PlayerPrefs.GetInt("MobileUDB.Doors.Actions",1)!=0,applyTag=PlayerPrefs.GetInt("MobileUDB.Doors.Tag",0)!=0};
            string floor=Map.sectors[indices[0]].floorTexture;foreach(int index in indices)if(!string.Equals(floor,Map.sectors[index].floorTexture,StringComparison.OrdinalIgnoreCase)){floor="";break;}
            ShowDialog("Создать двери · "+indices.Count,body=>
            {
                LineBatchCaption(body,"ТЕКСТУРЫ");
                var door=DoorTextureField(body,"Полотно",next.door,ResourceKind.Wall);
                var track=DoorTextureField(body,"Боковины",next.track,ResourceKind.Wall);
                var ceiling=DoorTextureField(body,"Потолок",next.ceiling,ResourceKind.Flat);
                var floorField=DoorTextureField(body,"Пол",floor,ResourceKind.Flat);
                LineBatchCaption(body,"НАСТРОЙКИ");
                var row=ShapeOptionRow(body);ShapeToggle(row,"Сброс сдвигов",next.resetOffsets,value=>next.resetOffsets=value);
                row=ShapeOptionRow(body);ShapeToggle(row,"Действия стен",next.applyActions,value=>next.applyActions=value);
                row=ShapeOptionRow(body);ShapeToggle(row,"Новый тег сектора",next.applyTag,value=>next.applyTag=value);
                var note=Label(body,"Пустые боковины / потолок / пол — сохранить текстуры. Потолок опустится до пола. Входные стены будут обращены наружу. Аргументы входных стен задаются по UDB даже при выключенных действиях. Один Undo отменяет всю команду.",16);
                note.alignment=TextAnchor.MiddleLeft;note.gameObject.AddComponent<LayoutElement>().preferredHeight=144;
                DialogApply(body,()=>
                {
                    try
                    {
                        if(!CurrentOpenContext(context)||!DoorsReady||!Selection.SetEquals(selection))throw new InvalidOperationException("Карта или выделение изменились. Откройте создание дверей снова.");
                        next.door=door.text;next.track=track.text;next.ceiling=ceiling.text;next.floor=floorField.text;
                        var plan=DoorCreation.Prepare(Map,indices,next,config,Actions);
                        if(!Edit(map=>plan.Apply(map),"Дверей создано: "+plan.SectorCount+". Один Undo отменяет изменения."))return false;
                        lastDoorOptions=next;lastDoorProfile=profile;
                        PlayerPrefs.SetInt("MobileUDB.Doors.ResetOffsets",next.resetOffsets?1:0);PlayerPrefs.SetInt("MobileUDB.Doors.Actions",next.applyActions?1:0);PlayerPrefs.SetInt("MobileUDB.Doors.Tag",next.applyTag?1:0);
                        if(plan.SectorCount==1)Selection.Clear();Changed(false);return true;
                    }
                    catch(Exception error){Notify(error.Message);return false;}
                },"Создать",true);
            });
        }
    }
}
