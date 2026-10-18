using System;using System.Collections.Generic;using MobileUDB.Core;using UnityEngine;using UnityEngine.UI;
namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        bool elementSearchGeneralized=true;
        readonly Dictionary<ElementSearchKind,int[]> elementSearchArguments=new Dictionary<ElementSearchKind,int[]>();
        InputField[] SearchArgumentControls(RectTransform body,InputField code,ElementSearchKind search,int[] initial,bool replacing)
        {
            if(Map.format!=MapFormat.Hexen||search!=ElementSearchKind.LineAction&&search!=ElementSearchKind.ThingAction)return null;
            ToolsCaption(body,replacing?"АРГУМЕНТЫ ЗАМЕНЫ · ПУСТО = НЕ МЕНЯТЬ":"АРГУМЕНТЫ · ПУСТО = ЛЮБОЕ");
            var fields=new InputField[5];var descriptions=new Text[5];var buttons=new Button[5];
            for(int i=0;i<5;i++)
            {
                int index=i;fields[i]=Field(body,"Аргумент "+(i+1),initial==null||initial[i]<0?"":initial[i].ToString());
                var placeholder=Label(fields[i].transform,replacing?"Не менять":"Любое",16);Stretch(placeholder.rectTransform);placeholder.rectTransform.offsetMin=new Vector2(10,0);placeholder.rectTransform.offsetMax=new Vector2(-10,0);placeholder.color=new Color(1,1,1,.4f);fields[i].placeholder=placeholder;
                descriptions[i]=Label(body,"",16);descriptions[i].gameObject.AddComponent<LayoutElement>().preferredHeight=34;
                buttons[i]=AddButton(body,"Выбрать аргумент "+(i+1)+"…",()=>{int id;if(int.TryParse(code.text,out id)&&id>=0)ArgumentPicker(fields[index],Actions.Find(id).arguments[index],id,index,fields[1],code,true);});
            }
            Action describe=()=>
            {int id;bool valid=int.TryParse(code.text,out id)&&id>=0;var action=valid?Actions.Find(id):null;for(int i=0;i<5;i++){var arg=action==null?null:action.arguments[i];descriptions[i].text=arg!=null&&arg.used?arg.title:"Байт 0–255 · пусто: "+(replacing?"не менять":"любое");buttons[i].interactable=arg!=null&&(arg.choices.Count>0||arg.flags.Count>0||IsScriptArgument(id,i)||IsPolyobjectArgument(arg)||IsTagArgument(arg));}};
            code.onValueChanged.AddListener(_=>describe());describe();return fields;
        }
        static int[] ReadSearchArguments(InputField[] fields)
        {
            if(fields==null)return null;var values=new int[5];
            for(int i=0;i<5;i++){string text=fields[i].text.Trim();if(text.Length==0||text=="*"){values[i]=-1;continue;}int value;if(!int.TryParse(text,out value)||value<0||value>255)throw new InvalidOperationException("Аргумент "+(i+1)+": пусто или целое число 0–255.");values[i]=value;}return values;
        }
        bool CurrentSearchContext(OpenContext context,ActionCatalog actions)=>CurrentOpenContext(context)&&ReferenceEquals(actions,Actions)&&Draft.Count==0&&Tool!=MobileTool.Paste;
        void SearchMatchingControl(RectTransform body,InputField input,ElementSearchKind search)
        {
            if(search==ElementSearchKind.ThingType)return;
            Button any=null;Action anyText=()=>any.GetComponentInChildren<Text>().text=input.text=="-1"?"Любой ненулевой код: да":"Любой ненулевой код: нет";
            int previous=elementSearchCodes[(int)search]>=0?elementSearchCodes[(int)search]:0;
            any=AddButton(body,"",()=>{int code;if(input.text=="-1")input.text=previous.ToString();else{if(int.TryParse(input.text,out code)&&code>=0)previous=code;input.text="-1";}});input.onValueChanged.AddListener(_=>anyText());anyText();
            bool generalized=search==ElementSearchKind.LineAction?Actions.generalizedLines.Count>0:search==ElementSearchKind.SectorEffect&&Actions.generalizedSectors.Count>0;
            if(generalized)
            {
                Button mode=null;Action text=()=>mode.GetComponentInChildren<Text>().text="Совпадение: "+(elementSearchGeneralized?"как UDB":"точный код");mode=AddButton(body,"",()=>{elementSearchGeneralized=!elementSearchGeneralized;text();});text();
                AddLabel(body,search==ElementSearchKind.LineAction?"Как UDB: обобщённые действия совпадают по любому общему ненулевому параметру; тип категории может отличаться. Для одного полного кода выберите «точный код».":"Как UDB: основной эффект и заданные обобщённые параметры должны совпасть; дополнительные параметры допустимы. Для одного полного кода выберите «точный код».");
            }
        }
    }
}
