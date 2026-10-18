using System;
using System.Collections.Generic;
using MobileUDB.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        Action HexenTagControl(Transform body,InputField code,InputField[] arguments,Button[] valueButtons,List<Action<MapDocument>> apply)
        {
            var combined=Field(body,"ID стены","0");var row=combined.transform.parent;
            var label=row.GetComponentInChildren<Text>();label.resizeTextForBestFit=true;label.resizeTextMinSize=12;label.resizeTextMaxSize=18;
            var choose=AddButton(body,"Выбрать ID / свободный номер…",()=>{});
            // Place the replacement above the argument rows, keeping the rest of the form intact.
            int first=arguments[0].transform.parent.GetSiblingIndex();row.SetSiblingIndex(first);choose.transform.SetSiblingIndex(first+1);
            HexenLineTag encoding=null;bool syncing=false;string lastSource=null;
            Func<string> source=()=>encoding==null?null:encoding.type+":"+encoding.low+":"+encoding.high+":"+arguments[encoding.low].text+":"+(encoding.high<0?"":arguments[encoding.high].text);
            Action refresh=()=>
            {
                if(syncing)return;
                int action;var pending=new MapLine();bool valid=int.TryParse(code.text,out action);pending.action=action;
                for(int i=0;i<5;i++)int.TryParse(arguments[i].text,out pending.args[i]);
                // Arg1 selects the 3D-floor interpretation. Invalid input must remain visible.
                int flags;bool validFlags=int.TryParse(arguments[1].text,out flags)&&flags>=0&&flags<=255;
                encoding=valid?HexenLineTags.Encoding(pending,Actions):null;
                if(encoding!=null&&string.Equals(Actions.Find(action).identifier,"Sector_Set3dFloor",StringComparison.OrdinalIgnoreCase)&&!validFlags)encoding=null;
                row.gameObject.SetActive(encoding!=null);choose.gameObject.SetActive(encoding!=null);
                for(int i=0;i<5;i++)
                {
                    bool hidden=encoding!=null&&(encoding.ArgumentMask&(1<<i))!=0;
                    arguments[i].transform.parent.gameObject.SetActive(!hidden);valueButtons[i].gameObject.SetActive(!hidden);
                }
                if(encoding==null){lastSource=null;return;}
                label.text=(encoding.type==15?"ID стены":"Тег сектора")+" · 0–"+encoding.Maximum;
                string nextSource=source();if(nextSource==lastSource)return;lastSource=nextSource;
                bool bytesValid=true;for(int i=0;i<5;i++)if((encoding.ArgumentMask&(1<<i))!=0)
                {int n;bytesValid&=int.TryParse(arguments[i].text,out n)&&n>=0&&n<=255;}
                syncing=true;
                try { combined.text=bytesValid?encoding.Read(pending).ToString():""; }
                finally { syncing=false; }
            };
            combined.onValueChanged.AddListener(text=>
            {
                if(syncing||encoding==null)return;
                int value;if(!int.TryParse(text,out value)||value<0||value>encoding.Maximum)return;
                var pending=new MapLine();encoding.Write(pending,value);syncing=true;
                try
                {
                    arguments[encoding.low].text=pending.args[encoding.low].ToString();
                    if(encoding.high>=0)arguments[encoding.high].text=pending.args[encoding.high].ToString();
                    lastSource=source();
                }
                finally { syncing=false; }
            });
            choose.onClick.AddListener(()=>{if(encoding!=null)TagPicker(combined,encoding.type,encoding.Maximum,null,encoding.type==15);});
            foreach(var argument in arguments)argument.onValueChanged.AddListener(_=>refresh());
            // Edit rolls back its snapshot if validation fails; invalid text cannot apply old bytes.
            apply.Add(m=>{if(encoding!=null)Number(combined,0,encoding.Maximum);});
            return refresh;
        }
    }
}
