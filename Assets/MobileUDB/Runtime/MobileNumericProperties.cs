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
        // Native NumericTextbox applies relative operations to the form's original values.
        // Keep that baseline here too: a second Apply never repeats the same delta.
        static double BatchNumericResult(double original,double operand,int operation,double min,double max,bool integer)
        {
            if(double.IsNaN(operand)||double.IsInfinity(operand))throw new InvalidOperationException("Введите конечное число.");
            double result;
            switch(operation)
            {
                case 0:result=operand;break;
                case 1:result=original+operand;break;
                case 2:result=original-operand;break;
                case 3:result=original*operand;break;
                case 4:if(operand==0)throw new InvalidOperationException("Делить на ноль нельзя.");result=original/operand;break;
                default:throw new InvalidOperationException("Неизвестная операция.");
            }
            if(double.IsNaN(result)||double.IsInfinity(result))throw new InvalidOperationException("Результат не является конечным числом.");
            if(integer)
            {
                if(operation==0&&result!=Math.Round(result))throw new InvalidOperationException("Абсолютное значение должно быть целым числом.");
                result=Math.Round(result,MidpointRounding.ToEven);
            }
            if(result<min||result>max)throw new InvalidOperationException("Результат должен быть от "+min+" до "+max+".");
            return result;
        }
        InputField BatchNumber(RectTransform body,string title,List<int> indices,Func<MapDocument,int,double> get,Action<MapDocument,int,double> set,double min,double max,bool integer,List<Func<MapDocument,bool>> changes,bool floatStorage=false)
        {
            var original=new Dictionary<int,double>();double first=get(Map,indices[0]);bool common=true;
            foreach(int index in indices){double value=get(Map,index);original[index]=value;if(value!=first)common=false;}
            var input=Field(body,title,common?(floatStorage?((float)first).ToString("R",CultureInfo.InvariantCulture):first.ToString("R",CultureInfo.InvariantCulture)):"",false);BatchPlaceholder(input);
            input.characterLimit=32;input.keyboardType=TouchScreenKeyboardType.NumbersAndPunctuation;
            bool touched=false,syncing=false;int operation=0;input.onValueChanged.AddListener(_=>{if(!syncing)touched=true;});
            string[] symbols={"=","+","−","×","÷"},names={"Задать значение","Прибавить","Вычесть","Умножить","Разделить"};
            var row=(RectTransform)input.transform.parent;var label=row.GetComponentInChildren<Text>();label.resizeTextForBestFit=true;label.resizeTextMinSize=11;label.resizeTextMaxSize=18;
            Button mode=null;mode=Button(row,symbols[0],()=>
            {
                NumericOperationPicker(title,symbols,names,operation,chosen=>
                {
                    if(operation==chosen)return;
                    // The displayed common value is not a delta chosen by the user.
                    if(!touched){syncing=true;try{input.text="";}finally{syncing=false;}}
                    operation=chosen;mode.GetComponentInChildren<Text>().text=symbols[operation];
                    label.text=title+(operation==0?"":" · "+symbols[operation]);
                });
            });
            mode.name="Numeric mode · "+title;var buttonRect=mode.GetComponent<RectTransform>();buttonRect.anchorMin=new Vector2(1,0);buttonRect.anchorMax=Vector2.one;buttonRect.pivot=new Vector2(1,.5f);buttonRect.sizeDelta=new Vector2(52,0);buttonRect.anchoredPosition=Vector2.zero;
            var inputRect=input.GetComponent<RectTransform>();inputRect.offsetMax=new Vector2(-60,inputRect.offsetMax.y);
            var assignment=input.gameObject.AddComponent<NumericFieldAssignment>();assignment.absolute=()=>operation==0;assignment.assign=value=>
            {operation=0;touched=true;mode.GetComponentInChildren<Text>().text=symbols[0];label.text=title;input.text=value.ToString(CultureInfo.InvariantCulture);};
            changes.Add(map=>
            {
                if(!touched||string.IsNullOrWhiteSpace(input.text))return false;
                double operand;
                if((!double.TryParse(input.text,NumberStyles.Float,CultureInfo.InvariantCulture,out operand)&&!double.TryParse(input.text,NumberStyles.Float,CultureInfo.CurrentCulture,out operand))||double.IsNaN(operand)||double.IsInfinity(operand))throw new InvalidOperationException(title+": введите конечное число.");
                bool changed=false;
                foreach(int index in indices)
                {
                    double result;try{result=BatchNumericResult(original[index],operand,operation,min,max,integer);}catch(InvalidOperationException error){throw new InvalidOperationException(title+": "+error.Message);}
                    // Compare the stored precision, avoiding duplicate history for e.g. +0.1.
                    if(floatStorage)result=(float)result;
                    if(get(map,index)==result)continue;set(map,index,result);changed=true;
                }
                return changed;
            });return input;
        }
        void NumericOperationPicker(string title,string[] symbols,string[] names,int current,Action<int> selected)
        {
            if(modal==null)return;CloseTexturePicker();
            texturePicker=Rect("Numeric operation picker",modal);ScaleDialogBounds(texturePicker,1f);texturePicker.gameObject.AddComponent<Image>().color=Background;
            var heading=Label(texturePicker,"Как изменить: "+title,24);var hr=heading.rectTransform;
            hr.anchorMin=new Vector2(0,1);hr.anchorMax=Vector2.one;hr.pivot=new Vector2(.5f,1);hr.anchoredPosition=new Vector2(-60,0);
            heading.resizeTextForBestFit=true;heading.resizeTextMinSize=14;heading.resizeTextMaxSize=24;
            var back=Button(texturePicker,"Назад",CloseTexturePicker);var br=back.GetComponent<RectTransform>();
            br.anchorMin=br.anchorMax=Vector2.one;br.pivot=Vector2.one;br.anchoredPosition=new Vector2(-8,-4);
            var scroll=Rect("Numeric operation scroll",texturePicker);Stretch(scroll);scroll.offsetMin=new Vector2(16,12);
            scroll.gameObject.AddComponent<Image>().color=Background;scroll.gameObject.AddComponent<RectMask2D>();
            var sv=scroll.gameObject.AddComponent<ScrollRect>();sv.horizontal=false;sv.movementType=ScrollRect.MovementType.Clamped;sv.viewport=scroll;
            var body=Rect("Numeric operations",scroll);body.anchorMin=new Vector2(0,1);body.anchorMax=Vector2.one;body.pivot=new Vector2(.5f,1);body.sizeDelta=Vector2.zero;sv.content=body;
            var grid=body.gameObject.AddComponent<GridLayoutGroup>();grid.spacing=new Vector2(6,6);grid.constraint=GridLayoutGroup.Constraint.FixedColumnCount;
            body.gameObject.AddComponent<ContentSizeFitter>().verticalFit=ContentSizeFitter.FitMode.PreferredSize;
            for(int i=0;i<symbols.Length;i++)
            {
                int choice=i;var button=Button(body,(i==current?"✓ ":"")+symbols[i]+" · "+names[i],()=>{selected(choice);CloseTexturePicker();});
                button.name="Numeric operation · "+i;var label=button.GetComponentInChildren<Text>();label.resizeTextForBestFit=true;label.resizeTextMinSize=12;label.resizeTextMaxSize=18;
            }
            // Five fixed operations need no catalog codes, search or pagination.
            // Reflow on rotation; scrolling still supports smaller safe areas.
            refreshTexturePicker=()=>
            {
                texturePickerSize=texturePicker.rect.size;bool landscape=Screen.width>Screen.height;grid.constraintCount=landscape?2:1;
                hr.sizeDelta=new Vector2(-130,62);br.sizeDelta=new Vector2(108,56);
                scroll.offsetMax=new Vector2(-16,-68);
                grid.cellSize=new Vector2(Mathf.Max(1,(texturePicker.rect.width-32-(grid.constraintCount-1)*6)/grid.constraintCount),60);
            };refreshTexturePicker();
        }
        InputField BatchReal(RectTransform body,string title,List<int> indices,Func<MapDocument,int,float> get,Action<MapDocument,int,float> set,float min,float max,List<Func<MapDocument,bool>> changes)
        {
            return BatchNumber(body,title,indices,(m,index)=>get(m,index),(m,index,n)=>set(m,index,(float)n),min,max,!MapFormats.IsUniversal(Map.format),changes,true);
        }
        InputField BatchInteger(RectTransform body,string title,List<int> indices,Func<MapDocument,int,int> get,Action<MapDocument,int,int> set,int min,int max,List<Func<MapDocument,bool>> changes)
        {
            return BatchNumber(body,title,indices,(m,index)=>get(m,index),(m,index,n)=>set(m,index,checked((int)n)),min,max,true,changes);
        }
        InputField SingleNumber(RectTransform body,string title,Func<MapDocument,double> get,Action<MapDocument,double> set,double min,double max,bool integer,List<Action<MapDocument>> apply,bool floatStorage=false)
        {
            // Reuse the grouped row's original-value snapshot, operation menu and
            // validation, including repeat-Apply and stored-precision comparisons.
            var changes=new List<Func<MapDocument,bool>>();
            var field=BatchNumber(body,title,new List<int>{0},(map,index)=>get(map),(map,index,n)=>set(map,n),min,max,integer,changes,floatStorage);
            apply.Add(map=>{changes[0](map);});return field;
        }
    }
    // Transient UI callback: lifetime follows its InputField, without retaining
    // destroyed forms in an editor-level registry. Used by absolute-value pickers.
    public sealed class NumericFieldAssignment : MonoBehaviour {internal Action<int> assign;internal Func<bool> absolute;}
}
