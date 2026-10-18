using System;
using System.Collections.Generic;
using MobileUDB.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        bool MultipleSelected(ElementKind kind)
        {
            if(Selection.Count<2)return false;
            foreach(var item in Selection)if(item.kind!=kind)return false;
            return true;
        }
        InputField BatchField<TElement,T>(RectTransform body,string title,List<int> indices,Func<MapDocument,List<TElement>> elements,Func<TElement,T> get,Func<T,string> format,Func<InputField,T> parse,Action<TElement,T> set,List<Func<MapDocument,bool>> changes,bool numeric=true)
        {
            return BatchValue(body,title,indices,(map,index)=>get(elements(map)[index]),format,parse,(map,index,value)=>set(elements(map)[index],value),changes,numeric);
        }
        InputField BatchValue<T>(RectTransform body,string title,List<int> indices,Func<MapDocument,int,T> get,Func<T,string> format,Func<InputField,T> parse,Action<MapDocument,int,T> set,List<Func<MapDocument,bool>> changes,bool numeric=true)
        {
            T first=get(Map,indices[0]);bool common=true;
            foreach(int index in indices)if(!EqualityComparer<T>.Default.Equals(first,get(Map,index)))common=false;
            var field=Field(body,title,common?format(first):"",numeric);
            BatchPlaceholder(field);
            bool touched=false;field.onValueChanged.AddListener(_=>touched=true);
            changes.Add(map=>
            {
                if(!touched||string.IsNullOrWhiteSpace(field.text))return false;
                T value=parse(field);bool changed=false;
                foreach(int index in indices)
                {
                    if(EqualityComparer<T>.Default.Equals(get(map,index),value))continue;
                    set(map,index,value);changed=true;
                }
                return changed;
            });
            return field;
        }
        void BatchPlaceholder(InputField field)
        {
            var placeholder=Label(field.transform,"Не менять",16);Stretch(placeholder.rectTransform);
            placeholder.rectTransform.offsetMin=new Vector2(10,0);placeholder.rectTransform.offsetMax=new Vector2(-10,0);
            placeholder.alignment=TextAnchor.MiddleLeft;placeholder.color=new Color(.65f,.7f,.75f);
            // Initialize before layout: InputField's placeholder setter leaves visibility alone.
            placeholder.gameObject.SetActive(string.IsNullOrEmpty(field.text));field.placeholder=placeholder;
        }
        void BatchBoolean(RectTransform body,string title,List<int> indices,Func<MapDocument,int,bool> get,Action<MapDocument,int,bool> set,List<Func<MapDocument,bool>> changes)
        {
            bool first=get(Map,indices[0]),common=true;foreach(int index in indices)if(get(Map,index)!=first)common=false;
            bool? pending=null;int step=0;Button button=null;Action refresh=()=>button.GetComponentInChildren<Text>().text=title+": "+(pending.HasValue?(pending.Value?"Да":"Нет"):common?(first?"Да":"Нет"):"Разные");
            button=AddButton(body,"",()=>
            {
                // Initial common value behaves like a toggle; third click restores untouched state.
                step=(step+1)%3;
                pending=step==0?(bool?)null:step==1?(common?!first:true):(common?first:false);
                refresh();if(!pending.HasValue)button.GetComponentInChildren<Text>().text=title+": Не менять";
            });refresh();
            changes.Add(map=>
            {
                if(!pending.HasValue)return false;bool changed=false;
                foreach(int index in indices)if(get(map,index)!=pending.Value){set(map,index,pending.Value);changed=true;}
                return changed;
            });
        }
    }
}
