// GPL-3.0. VerticesMode.DeleteItem, endpoint attach order and AutoRemove cascades.
using System;
using System.Collections.Generic;

namespace MobileUDB.Core
{
    public sealed partial class MapDocument
    {
        void DeleteVerticesBeforeCompact(List<int> selected,HashSet<int> removedVertices,HashSet<int> removedLines)
        {
            removedVertices.Clear();
            var incidence=new LinkedList<int>[vertices.Count];for(int i=0;i<incidence.Length;i++)incidence[i]=new LinkedList<int>();
            var starts=new LinkedListNode<int>[lines.Count];var ends=new LinkedListNode<int>[lines.Count];
            for(int i=0;i<lines.Count;i++){starts[i]=incidence[lines[i].start].AddLast(i);ends[i]=incidence[lines[i].end].AddLast(i);}
            Action<int> disposeVertex=null,disposeLine=null;Action<int,LinkedListNode<int>> detach=null;
            detach=(vertex,node)=>
            {
                // Native DetachLinedefP does not mutate a vertex's incidence
                // collection while that vertex is disposing its own lines.
                if(removedVertices.Contains(vertex)||node==null)return;
                incidence[vertex].Remove(node);if(incidence[vertex].Count==0)disposeVertex(vertex);
            };
            disposeLine=index=>
            {
                if(!removedLines.Add(index))return;var line=lines[index];
                detach(line.start,starts[index]);starts[index]=null;detach(line.end,ends[index]);ends[index]=null;
            };
            disposeVertex=vertex=>
            {
                if(!removedVertices.Add(vertex))return;
                foreach(int index in incidence[vertex])disposeLine(index);incidence[vertex].Clear();
            };
            foreach(int vertex in selected)
            {
                if(removedVertices.Contains(vertex))continue;var attached=incidence[vertex];
                if(attached.Count==2)
                {
                    int first=attached.First.Value,second=attached.First.Next.Value;var kept=lines[first];var discarded=lines[second];
                    int other=discarded.start==vertex?discarded.end:discarded.start;
                    // Keep the first line's exact properties/direction/sides;
                    // appending its new endpoint incidence matches Set*Vertex.
                    if(kept.start==vertex){detach(vertex,starts[first]);kept.start=other;starts[first]=incidence[other].AddLast(first);}
                    else {detach(vertex,ends[first]);kept.end=other;ends[first]=incidence[other].AddLast(first);}
                    disposeLine(second);
                }
                disposeVertex(vertex);
            }
        }
    }
}
