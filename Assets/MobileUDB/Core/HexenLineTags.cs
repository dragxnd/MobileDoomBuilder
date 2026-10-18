using System;

namespace MobileUDB.Core
{
    // Hexen encodings from UDB Linedef.TranslateToUDMF and GZDoom's udmf_zdoom spec.
    // These are map-load definitions, not guesses based on an argument's caption.
    public sealed class HexenLineTag
    {
        public readonly int type, low, high;
        public int Maximum { get { return high < 0 ? 255 : 65535; } }
        public int ArgumentMask { get { return (1 << low) | (high < 0 ? 0 : 1 << high); } }
        public HexenLineTag(int type,int low,int high=-1) { this.type=type;this.low=low;this.high=high; }
        public int Read(MapLine line) { return line.args[low]+(high<0?0:line.args[high]*256); }
        public void Write(MapLine line,int value)
        {
            if(value<0||value>Maximum)throw new ArgumentOutOfRangeException("value");
            line.args[low]=value&255;if(high>=0)line.args[high]=value>>8;
        }
    }
    public static class HexenLineTags
    {
        // At most one special encoding per action. Other typed arguments remain independent.
        public static HexenLineTag Encoding(MapLine line,ActionCatalog catalog)
        {
            if(catalog==null||!catalog.hasArguments)return null;
            string id=(catalog.Find(line.action).identifier??"").ToLowerInvariant();
            if(id=="line_setidentification")return new HexenLineTag(15,0,catalog.extended?4:-1);
            if(!catalog.extended)return null;
            switch(id)
            {
                case "translucentline":return new HexenLineTag(15,0);
                case "polyobj_startline":return new HexenLineTag(15,3);
                case "polyobj_explicitline":return new HexenLineTag(15,4);
                case "plane_align":return new HexenLineTag(15,2);
                case "teleport_line":return new HexenLineTag(15,0);
                case "scroll_texture_model":return new HexenLineTag(15,0);
                case "sector_set3dfloor":return (line.args[1]&8)!=0?new HexenLineTag(15,4):new HexenLineTag(13,0,4);
                default:return null;
            }
        }
    }
}
