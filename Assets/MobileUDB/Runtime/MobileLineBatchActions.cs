using System;
using System.Collections.Generic;
using System.Text;
using MobileUDB.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        InputField BatchLineActions(RectTransform body,List<int> indices,List<Func<MapDocument,bool>> changes)
        {
            bool universal=MapFormats.IsUniversal(Map.format);int min=universal?int.MinValue:0,max=universal?int.MaxValue:Map.format==MapFormat.Hexen?255:65535;
            var action=BatchField(body,"Действие",indices,m=>m.lines,l=>l.action,n=>n.ToString(),field=>Number(field,min,max),(l,n)=>l.action=n,changes);
            AddButton(body,"Выбрать действие по названию…",()=>ActionPicker(action,false));
            if(Actions.generalizedLines.Count>0)AddButton(body,"Настроить действие Boom…",()=>GeneralizedPicker(action,false,true));
            if(Map.format==MapFormat.Doom)return action;
            int argMax=universal?int.MaxValue:255;
            var args=new InputField[5];var labels=new Text[5];var buttons=new Button[5];var strings=new InputField[5];var stringLabels=new Text[5];Action refresh=null;
            for(int i=0;i<5;i++)
            {
                int index=i;args[i]=BatchField(body,"Аргумент "+i,indices,m=>m.lines,l=>l.args[index],n=>n.ToString(),field=>Number(field,min,argMax),(l,n)=>l.args[index]=n,changes);
                labels[i]=args[i].transform.parent.GetComponentInChildren<Text>();labels[i].resizeTextForBestFit=true;labels[i].resizeTextMinSize=11;labels[i].resizeTextMaxSize=18;
                buttons[i]=AddButton(body,"Выбрать значение…",()=>{int code;if(int.TryParse(action.text,out code))ArgumentPicker(args[index],Actions.Find(code).arguments[index],code,index,args[1],action);});
                args[i].onValueChanged.AddListener(_=>{if(refresh!=null)refresh();});
                
#if MOBILEUDB_UDMF // Deferred from the first release.
if(universal)
                {
                    string key="arg"+i+"str";
                    strings[i]=BatchUniversalRow(body,"Строковый аргумент "+i,indices,()=>key,(m,id)=>m.lines[id],(m,id)=>m.lines[id],_=>{},changes,true);
                    strings[i].characterLimit=512;stringLabels[i]=strings[i].transform.parent.GetComponentInChildren<Text>();
                }
#endif

            }
            Action refreshTag=Map.format==MapFormat.Hexen?BatchHexenTagControl(body,indices,action,args,buttons,changes):null;
            refresh=()=>
            {
                int code;bool known=int.TryParse(action.text,out code);var info=known?Actions.Find(code):null;
                for(int i=0;i<5;i++)
                {
                    var arg=known?info.arguments[i]:null;labels[i].text=(i+1)+" · "+(arg!=null&&arg.used?arg.title:"Аргумент "+i);
                    bool multiple=arg!=null&&(arg.type==12||arg.flags.Count>0);
                    buttons[i].interactable=arg!=null&&(IsTagArgument(arg)||IsPolyobjectArgument(arg)||IsScriptArgument(code,i)||arg.choices.Count>0||arg.flags.Count>0)&&(!multiple||!string.IsNullOrWhiteSpace(args[i].text));
                    buttons[i].GetComponentInChildren<Text>().text="Значение "+(i+1)+": "+(arg!=null&&arg.used?arg.title:"вручную")+"…";
                    
#if MOBILEUDB_UDMF
if(strings[i]!=null){string key="arg"+i+"str";strings[i].transform.parent.gameObject.SetActive((arg!=null&&arg.stringValue)||indices.Exists(id=>UdmfText.Raw(Map.lines[id].fields,key)!=null));stringLabels[i].text=arg!=null&&arg.stringValue?arg.stringTitle:"Строковый аргумент "+i;}
#endif

                }
                if(refreshTag!=null)refreshTag();
            };action.onValueChanged.AddListener(_=>refresh());refresh();return action;
        }
        Action BatchHexenTagControl(RectTransform body,List<int> indices,InputField action,InputField[] args,Button[] buttons,List<Func<MapDocument,bool>> changes)
        {
            // This field owns its pending value. Display synchronization never touches raw bytes.
            var combined=Field(body,"ID стены","",true);var row=combined.transform.parent;
            var placeholder=Label(combined.transform,"Не менять",16);Stretch(placeholder.rectTransform);placeholder.rectTransform.offsetMin=new Vector2(10,0);placeholder.rectTransform.offsetMax=new Vector2(-10,0);placeholder.alignment=TextAnchor.MiddleLeft;placeholder.gameObject.SetActive(true);combined.placeholder=placeholder;
            var label=row.GetComponentInChildren<Text>();label.resizeTextForBestFit=true;label.resizeTextMinSize=12;label.resizeTextMaxSize=18;
            HexenLineTag encoding=null;bool syncing=false,touched=false;string lastSource=null;
            combined.onValueChanged.AddListener(_=>{if(!syncing)touched=true;});
            var choose=AddButton(body,"Выбрать ID / свободный номер…",()=>{if(encoding!=null)TagPicker(combined,encoding.type,encoding.Maximum,null,encoding.type==15);});
            int first=args[0].transform.parent.GetSiblingIndex();row.SetSiblingIndex(first);choose.transform.SetSiblingIndex(first+1);
            Action refresh=()=>
            {
                var values=new List<int>();HexenLineTag common=null;bool valid=true;var source=new StringBuilder();
                foreach(int index in indices)
                {
                    var pending=Map.lines[index].Clone();int n;
                    if(!string.IsNullOrWhiteSpace(action.text)){if(!int.TryParse(action.text,out n)||n<0||n>255){valid=false;break;}pending.action=n;}
                    for(int i=0;i<5;i++)if(!string.IsNullOrWhiteSpace(args[i].text))
                    {if(!int.TryParse(args[i].text,out n)||n<0||n>255){valid=false;break;}pending.args[i]=n;}
                    var next=valid?HexenLineTags.Encoding(pending,Actions):null;
                    if(next==null){valid=false;break;}
                    if(common==null)common=next;else if(common.type!=next.type||common.low!=next.low||common.high!=next.high){valid=false;break;}
                    int value=next.Read(pending);values.Add(value);source.Append(value).Append(';');
                }
                encoding=valid?common:null;row.gameObject.SetActive(encoding!=null);choose.gameObject.SetActive(encoding!=null);
                for(int i=0;i<5;i++){bool hidden=encoding!=null&&(encoding.ArgumentMask&(1<<i))!=0;args[i].transform.parent.gameObject.SetActive(!hidden);buttons[i].gameObject.SetActive(!hidden);}
                if(encoding==null){lastSource=null;touched=false;return;}
                label.text=(encoding.type==15?"ID стены":"Тег сектора")+" · 0–"+encoding.Maximum;
                string nextSource=encoding.type+":"+encoding.low+":"+encoding.high+":"+source;
                if(nextSource==lastSource)return;lastSource=nextSource;bool same=values.TrueForAll(n=>n==values[0]);
                syncing=true;try{combined.text=same?values[0].ToString():"";touched=false;}finally{syncing=false;}
            };
            changes.Add(map=>
            {
                if(encoding==null||!touched||string.IsNullOrWhiteSpace(combined.text))return false;
                int value=Number(combined,0,encoding.Maximum);bool changed=false;
                foreach(int index in indices)
                {
                    var line=map.lines[index];var current=HexenLineTags.Encoding(line,Actions);
                    if(current==null||current.type!=encoding.type||current.low!=encoding.low||current.high!=encoding.high)throw new InvalidOperationException("ID: выбранные стены имеют разные способы кодирования.");
                    if(current.Read(line)==value)continue;current.Write(line,value);changed=true;
                }
                return changed;
            });return refresh;
        }
    }
}

