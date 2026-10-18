using System.Collections.Generic;using MobileUDB.Core;
namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        MapDocument associationMap;long associationRevision=-1;ActionCatalog associationActions;
        ElementRef? associationFocus;MapAssociationPlan associationPlan=new MapAssociationPlan();
        internal MapAssociationPlan Associations
        {
            get
            {
                ElementRef? focus=null;if(!VisualActive&&Tool==MobileTool.Select)foreach(var item in Selection)if(item.kind==ElementKind.Line||item.kind==ElementKind.Sector||item.kind==ElementKind.Thing)focus=item;
                var actions=focus.HasValue?Actions:null;
                if(!ReferenceEquals(associationMap,Map)||associationRevision!=MapContentRevision||!System.Nullable.Equals(associationFocus,focus)||!ReferenceEquals(associationActions,actions))
                {associationMap=Map;associationRevision=MapContentRevision;associationFocus=focus;associationActions=actions;associationPlan=focus.HasValue?MapAssociationPlan.Build(Map,actions,focus.Value):new MapAssociationPlan();}
                return associationPlan;
            }
        }
    }
}
