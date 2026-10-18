using System.Collections.Generic;
using System;
using MobileUDB.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        void VertexBatchProperties()
        {
            var indices=new List<int>();foreach(var item in Selection)indices.Add(item.index);indices.Sort();
            ShowDialog("Вершины · "+indices.Count,body=>
            {
                var note=Label(body,"Пусто — не менять. Кнопка у числа выбирает = / + / − / × / ÷.",16);note.alignment=TextAnchor.MiddleLeft;note.gameObject.AddComponent<LayoutElement>().minHeight=48;
                var changes=new List<Func<MapDocument,bool>>();
                BatchReal(body,"X",indices,(m,index)=>m.vertices[index].x,(m,index,n)=>m.vertices[index].x=n,-32768,32767,changes);
                BatchReal(body,"Y",indices,(m,index)=>m.vertices[index].y,(m,index,n)=>m.vertices[index].y=n,-32768,32767,changes);
                
#if MOBILEUDB_UDMF
BatchElementFields(body,indices,ElementKind.Vertex,changes);
#endif

                DialogApply(body,()=>Edit(map=>
                {
                    bool changed=false;foreach(var change in changes)changed|=change(map);
                    if(!changed)throw new InvalidOperationException("Нет изменений: заполните нужное поле.");
                },"Свойства вершин обновлены. Один Undo отменяет все изменения."));
            });
        }
    }
}
