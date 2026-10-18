using System;
using System.Collections.Generic;
using MobileUDB.Core;

namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        void EditVisualThings(Func<MapDocument,IEnumerable<int>,int> change,string message)
        {
            var indices=new List<int>(visualView.SelectedThings);if(indices.Count==0)return;
            Edit(m=>{if(change(m,indices)==0)throw new InvalidOperationException("Объекты уже имеют это значение.");},message+" · объектов: "+indices.Count+".");
        }
        void MoveVisualThings(int x,int y,int z)
        {EditVisualThings((m,indices)=>ThingBatchEdits.Move(m,indices,x*visualStep,y*visualStep,z*visualStep,type=>thingCatalog.Find(type).hangs),"Объекты перемещены");}
        void RotateVisualThings(int amount)
        {EditVisualThings((m,indices)=>ThingBatchEdits.Rotate(m,indices,amount),"Объекты повёрнуты");}
        void TypeVisualThings(List<int> indices,int type)
        {Edit(m=>{if(ThingBatchEdits.Type(m,indices,type)==0)throw new InvalidOperationException("Объекты уже имеют этот тип.");},"Тип изменён: "+thingCatalog.Find(type).title+" · объектов: "+indices.Count+".");}
        void DeleteVisualThings()
        {
            var indices=new List<int>(visualView.SelectedThings);if(indices.Count==0)return;
            ShowDialog("Удалить отмеченные объекты · "+indices.Count+"?",body=>
            {
                AddLabel(body,"Одно Undo восстановит всю группу.");
                AddButton(body,"Удалить",()=>
                {
                    CloseDialog();if(Edit(m=>ThingBatchEdits.Delete(m,indices),"Объекты удалены."))visualView.ForgetThingSelection();
                });
            });
        }
    }
}
