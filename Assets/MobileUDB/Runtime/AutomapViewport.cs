// GPL-3.0. Original UDB Doom automap color preset and visibility rules.
using System.Collections.Generic;
using MobileUDB.Core;
using UnityEngine;
using UnityEngine.UI;
namespace MobileUDB
{
    public sealed partial class MapViewport
    {
        readonly Dictionary<GameObject,bool> automapHiddenLayers=new Dictionary<GameObject,bool>();
        bool automapLayersHidden;
        void RefreshAutomapLayers()
        {
            if(editor.AutomapActive||editor.SoundPropagationActive||editor.BlockmapActive)
            {
                automapLayersHidden=true;
                foreach(Transform child in transform)
                {
                    if(child==overlay.transform||child.GetComponent<SoundFillGraphic>()!=null)continue;
                    if(!automapHiddenLayers.ContainsKey(child.gameObject))automapHiddenLayers.Add(child.gameObject,child.gameObject.activeSelf);
                    child.gameObject.SetActive(false);
                }
            }
            else if(automapLayersHidden)
            {
                foreach(var pair in automapHiddenLayers)if(pair.Key!=null)pair.Key.SetActive(pair.Value);
                automapHiddenLayers.Clear();automapLayersHidden=false;layerDirty=true;
            }
        }
        internal static Color32 AutomapColor(AutomapLineStyle style)
        {
            switch(style)
            {
                case AutomapLineStyle.Hidden:return new Color32(192,192,192,255);
                case AutomapLineStyle.SingleSided:return new Color32(252,0,0,255);
                case AutomapLineStyle.FloorDifference:return new Color32(188,120,72,255);
                case AutomapLineStyle.CeilingDifference:return new Color32(252,252,0,255);
                default:return new Color32(108,108,108,255);
            }
        }
        void PopulateAutomap(VertexHelper mesh)
        {
            var rect=rectTransform.rect;var map=editor.Map;
            for(int i=0;i<map.lines.Count&&mesh.currentVertCount<60000;i++)
            {
                if(!AutomapLines.Visible(map,i,editor.AutomapShowAll))continue;var line=map.lines[i];var a=Project(map.vertices[line.start].Position);var b=Project(map.vertices[line.end].Position);
                if(!Visible(a,b,rect))continue;
                Line(mesh,a,b,2,AutomapColor(AutomapLines.Style(map,i)));
            }
            foreach(var screen in fingers){var point=Local(screen);Ring(mesh,point,18,2,new Color32(105,225,255,255));}
        }
    }
}
