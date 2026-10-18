// Deferred UDMF UI; intentionally excluded from the first release.
#if MOBILEUDB_UDMF
using System;
using System.Collections.Generic;
using MobileUDB.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        Func<string,bool> BatchManagedFields(ElementKind kind)
        {
            var extra=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if(kind==ElementKind.Line||kind==ElementKind.Thing)for(int i=0;i<5;i++)extra.Add("arg"+i+"str");
            if(kind==ElementKind.Thing)
                foreach(string flag in new[]{"skill1","skill2","skill3","skill4","skill5","ambush","single","coop","dm","class1","class2","class3"})extra.Add(flag);
            // Activation rows already own these fields in the primary form.
            if(kind==ElementKind.Line&&Actions.hasArguments)
            {
                if(Actions.extended)
                {var flags=GameConfigText.Read(ActionConfig("ZDoom_misc.cfg"),ActionConfig).Section("linedefactivations_udmf");foreach(string flag in flags.values.Keys)extra.Add(flag);foreach(string flag in flags.children.Keys)extra.Add(flag);}
                else foreach(string flag in new[]{"playercross","playeruse","monstercross","impact","playerpush","missilecross","repeatspecial","monsteractivate"})extra.Add(flag);
            }
            return key=>UdmfText.IsManaged(kind,key)||extra.Contains(key);
        }
        void BatchElementFields(RectTransform body,List<int> indices,ElementKind kind,List<Func<MapDocument,bool>> changes)
        {
            if(Map.format!=MapFormat.Udmf)return;
            Func<MapDocument,int,MapElementData> element=(map,index)=>Element(map,new ElementRef(kind,index));
            BatchUniversalPanel(body,"Поля UDMF",indices,element,element,BatchManagedFields(kind),changes);
        }
        void BatchUniversalPanel(RectTransform body,string title,List<int> indices,Func<MapDocument,int,MapElementData> read,Func<MapDocument,int,MapElementData> edit,Func<string,bool> managed,List<Func<MapDocument,bool>> changes)
        {
            var keys=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(int index in indices)foreach(var field in read(Map,index).fields)if(!managed(field.name))keys.Add(field.name);
            var sorted=new List<string>(keys);sorted.Sort(StringComparer.OrdinalIgnoreCase);
            var panel=Rect(title,body);var layout=panel.gameObject.AddComponent<VerticalLayoutGroup>();layout.spacing=8;layout.childControlHeight=layout.childControlWidth=true;layout.childForceExpandHeight=false;
            panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit=ContentSizeFitter.FitMode.PreferredSize;
            var note=Label(panel,"Пусто — не менять. Меню ⋯: удалить, пустая строка, true / false или режим текста.",16);note.alignment=TextAnchor.MiddleLeft;note.gameObject.AddComponent<LayoutElement>().minHeight=48;
            foreach(string key in sorted)
            {string name=key;BatchUniversalRow(panel,name,indices,()=>name,read,edit,_=>{},changes);}
            var newName=Field(panel,title+" · Новое поле","",false);newName.characterLimit=64;
            newName.transform.parent.GetComponentInChildren<Text>().text="Новое поле";
            BatchUniversalRow(panel,title+" · Значение",indices,()=>newName.text.Trim(),read,edit,key=>
            {
                if(string.IsNullOrWhiteSpace(key))throw new InvalidOperationException("Укажите имя нового поля UDMF.");
                if(managed(key))throw new InvalidOperationException("Это основное поле. Измените его выше в свойствах.");
                if(keys.Contains(key))throw new InvalidOperationException("Поле уже есть в списке. Измените его строку выше.");
            },changes,false,"Значение");
            panel.gameObject.SetActive(false);
            var caption=Label(body,"Дополнительные параметры",16);caption.alignment=TextAnchor.MiddleLeft;caption.color=new Color(.7f,.76f,.82f);caption.gameObject.AddComponent<LayoutElement>().preferredHeight=28;
            // Create the header last, then place it above the collapsed content.
            Button toggle=null;toggle=AddButton(body,"▶ "+title+" · "+keys.Count,()=>
            {panel.gameObject.SetActive(!panel.gameObject.activeSelf);toggle.GetComponentInChildren<Text>().text=(panel.gameObject.activeSelf?"▼ ":"▶ ")+title+" · "+keys.Count;});
            caption.transform.SetSiblingIndex(panel.GetSiblingIndex());toggle.transform.SetSiblingIndex(panel.GetSiblingIndex());
        }
        InputField BatchUniversalRow(RectTransform body,string title,List<int> indices,Func<string> key,Func<MapDocument,int,MapElementData> read,Func<MapDocument,int,MapElementData> edit,Action<string> validate,List<Func<MapDocument,bool>> changes,bool textOnly=false,string displayTitle=null)
        {
            bool strings=textOnly,present=false;
            if(!textOnly)
            {
                strings=true;foreach(int index in indices){string value=UdmfText.Raw(read(Map,index).fields,key());if(value==null)continue;present=true;if(!value.StartsWith("\"",StringComparison.Ordinal))strings=false;}
                strings&=present;
            }
            bool originalStrings=strings,touched=false,syncing=false,remove=false,emptyString=false;
            Func<string> commonValue=()=>
            {
                string first=UdmfText.Raw(read(Map,indices[0]).fields,key());foreach(int index in indices)if(UdmfText.Raw(read(Map,index).fields,key())!=first)return "";
                return first==null?"":strings&&first.StartsWith("\"",StringComparison.Ordinal)?UdmfText.DecodeString(first):first;
            };
            var input=Field(body,title,commonValue(),false);input.characterLimit=4096;BatchPlaceholder(input);
            var label=input.transform.parent.GetComponentInChildren<Text>();label.text=displayTitle??title;label.resizeTextForBestFit=true;label.resizeTextMinSize=11;label.resizeTextMaxSize=18;
            input.onValueChanged.AddListener(_=>{if(!syncing){touched=true;emptyString=false;((Text)input.placeholder).text="Не менять";}});
            Button menu=null;menu=Button(input.transform.parent,"⋯",()=>
            {
                var choices=new List<PickerChoice>{new PickerChoice{value=0,title="Не менять"},new PickerChoice{value=1,title="Удалить поле"},new PickerChoice{value=4,title="Пустая строка"}};
                if(!textOnly){choices.Add(new PickerChoice{value=2,title="true · Да"});choices.Add(new PickerChoice{value=3,title="false · Нет"});choices.Add(new PickerChoice{value=5,title="Вводить текст без кавычек"});choices.Add(new PickerChoice{value=6,title="Вводить значение UDMF"});}
                ChoicePicker("Поле: "+(displayTitle??title),choices,choice=>
                {
                    bool wasStrings=strings;remove=choice.value==1;emptyString=choice.value==4;input.interactable=!remove;syncing=true;
                    try
                    {
                        if(choice.value==0){strings=originalStrings;touched=false;input.text=commonValue();}
                        else
                        {
                            touched=true;
                            if(choice.value==2||choice.value==3){strings=false;input.text=choice.value==2?"true":"false";}
                            else if(choice.value==4){strings=true;input.text="";}
                            else if(choice.value==5){strings=true;if(!wasStrings&&input.text.StartsWith("\"",StringComparison.Ordinal))input.text=UdmfText.DecodeString(input.text);}
                            else if(choice.value==6){strings=false;if(wasStrings&&!string.IsNullOrEmpty(input.text))input.text=UdmfText.Quote(input.text);}
                        }
                        menu.GetComponentInChildren<Text>().text=remove?"×":emptyString?"\"\"":"⋯";((Text)input.placeholder).text=emptyString?"Пустая строка":remove?"Удалить поле":"Не менять";
                    }
                    finally{syncing=false;}
                });
            });
            menu.name="UDMF field menu · "+title;var rect=menu.GetComponent<RectTransform>();rect.anchorMin=new Vector2(1,0);rect.anchorMax=Vector2.one;rect.pivot=new Vector2(1,.5f);rect.sizeDelta=new Vector2(52,0);rect.anchoredPosition=Vector2.zero;
            input.GetComponent<RectTransform>().offsetMax=new Vector2(-60,0);
            changes.Add(map=>
            {
                if(!touched||(!remove&&!emptyString&&(string.IsNullOrEmpty(input.text)||(!strings&&string.IsNullOrWhiteSpace(input.text)))))return false;
                string name=key();validate(name);
                string value=emptyString?"\"\"":strings?UdmfText.Quote(input.text):input.text.Trim();
                // Validate before side isolation or any write. Edit also rolls back all other rows.
                UdmfText.SetRaw(new List<UniversalField>(),name,remove?"0":value);
                bool changed=false;
                foreach(int index in indices)
                {
                    string current=UdmfText.Raw(read(map,index).fields,name);if(remove?current==null:current==value)continue;
                    var fields=edit(map,index).fields;
                    if(remove)fields.RemoveAll(field=>string.Equals(field.name,name,StringComparison.OrdinalIgnoreCase));else UdmfText.SetRaw(fields,name,value);
                    changed=true;
                }
                return changed;
            });return input;
        }
    }
}

#endif
