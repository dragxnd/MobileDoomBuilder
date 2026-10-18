// GPL-3.0. GridSetupForm / GridSetup background controls, adapted for touch.
using System;using System.Collections.Generic;using System.IO;using MobileUDB.Core;using UnityEngine;using UnityEngine.UI;
namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        readonly Dictionary<string,MapBackground> mapBackgrounds=new Dictionary<string,MapBackground>(StringComparer.OrdinalIgnoreCase);
        Texture2D backgroundPreview;
        internal MapBackground CurrentBackground {get {MapBackground state;return mapBackgrounds.TryGetValue(MapNaming.Key(Map),out state)?state:null;}}
        static long BackgroundBytes(IEnumerable<MapBackgroundFrame> entries,bool decode=false)
        {
            long total=0;var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if(entries!=null)foreach(var entry in entries)
            {
                if(entry==null||entry.background==null)throw new InvalidDataException("Пустые настройки фона карты.");
                MapNaming.RequireName(entry.name);if(!names.Add(entry.name))throw new InvalidDataException("Повторяющиеся настройки фона карты.");
                entry.background=entry.background.Validated();string data=entry.background.fileData;
                if(!string.IsNullOrEmpty(data))total+=decode?entry.background.Attachment().LongLength:data.Length*3L/4;
                if(total>MapBackground.MaximumPortableBytes)throw new InvalidDataException("Изображения фона всех карт: не более 32 МиБ.");
            }
            return total;
        }
        List<MapBackgroundFrame> BackgroundFrames()
        {var entries=new List<MapBackgroundFrame>();foreach(var pair in mapBackgrounds)entries.Add(new MapBackgroundFrame{name=pair.Key,background=pair.Value.Copy()});return entries;}
        void SetMapBackground(MapBackground state)
        {
            state=state.Validated();var entries=BackgroundFrames();string key=MapNaming.Key(Map);entries.RemoveAll(entry=>string.Equals(entry.name,key,StringComparison.OrdinalIgnoreCase));entries.Add(new MapBackgroundFrame{name=key,background=state});
            long cost=BackgroundBytes(entries);for(int i=0;i<resourceSources.Count;i++)cost+=ResourceCost(resourceSources[i],(int)(resourceArchives[i].data.Length*3L/4));
            if(cost+(sourceWad==null?0:sourceWad.LongLength)>MaxProjectWadBytes)throw new InvalidDataException("Фон вместе с картами и ресурсами превышает предел памяти проекта.");
            mapBackgrounds[key]=state;viewRecoveryRevision.Changed();saveAt=Time.unscaledTime+2;View.SetVerticesDirty();
        }
        internal ResourceImage DecodeBackground(MapBackground state,out double width,out double height)
        {
            state=state.Validated();ResourceImage image;
            if(state.source==MapBackground.File)
            {var bytes=state.Attachment();if(bytes==null)throw new InvalidDataException("Выберите файл изображения заново: .dbs содержит только путь к нему.");image=BackgroundImageCodec.Decode(bytes,DecodePng);width=image.width;height=image.height;}
            else
            {
                var entry=resources.Find(state.name,state.source==MapBackground.Flat?ResourceKind.Flat:ResourceKind.Wall);
                if(entry==null)throw new InvalidDataException("Изображение отсутствует в подключённых ресурсах: "+state.name);
                image=resources.Decode(entry,DecodePng);width=(entry.width>0?entry.width:image.width)*entry.scaleX;height=(entry.height>0?entry.height:image.height)*entry.scaleY;
            }
            if(double.IsNaN(width)||double.IsInfinity(width)||double.IsNaN(height)||double.IsInfinity(height)||width<=0||height<=0)throw new InvalidDataException("Некорректный размер фоновой текстуры.");return image;
        }
        internal static Texture2D BackgroundTexture(ResourceImage image,string name,int maximumSize=0)
        {
            if(maximumSize>0&&Math.Max(image.width,image.height)>maximumSize)
            {
                float factor=maximumSize/(float)Math.Max(image.width,image.height);int w=Math.Max(1,(int)(image.width*factor)),h=Math.Max(1,(int)(image.height*factor));var pixels=new byte[w*h*4];
                for(int y=0;y<h;y++)for(int x=0;x<w;x++)Buffer.BlockCopy(image.rgba,((int)(y/factor)*image.width+(int)(x/factor))*4,pixels,(y*w+x)*4,4);image=new ResourceImage(w,h,pixels);
            }
            var texture=new Texture2D(image.width,image.height,TextureFormat.RGBA32,false);
            try {texture.name="UDB background · "+name;texture.filterMode=FilterMode.Point;texture.wrapMode=TextureWrapMode.Repeat;texture.LoadRawTextureData(VisualPixelRows(image));texture.Apply(false,true);return texture;}
            catch {Destroy(texture);throw;}
        }
        void CloseBackgroundPreview(){if(backgroundPreview!=null){Destroy(backgroundPreview);backgroundPreview=null;}}
        void BackgroundMenu(){if(VisualActive){Notify("Фон доступен в 2D.");return;}CancelDrag();BackgroundMenu((CurrentBackground??new MapBackground()).Copy(),CaptureOpenContext(),CurrentBackground);}
        void BackgroundMenu(MapBackground draft,OpenContext context,MapBackground expected)
        {
            ShowDialog("Фон карты",body=>
            {
                ToolsCaption(body,"ИСТОЧНИК ИЗОБРАЖЕНИЯ");
                var name=Field(body,draft.source==MapBackground.File?"Файл":"Имя текстуры",draft.name,false);name.characterLimit=1024;
                InputField x=Field(body,"Сдвиг X",draft.offsetX.ToString()),y=Field(body,"Сдвиг Y",draft.offsetY.ToString());
                InputField sx=Field(body,"Масштаб X, %",draft.scaleX.ToString()),sy=Field(body,"Масштаб Y, %",draft.scaleY.ToString());
                var alpha=Field(body,"Видимость, %",draft.opacity.ToString());bool visible=draft.visible;
                Button toggle=null;toggle=AddButton(body,visible?"Фон: включён":"Фон: выключен",()=>{visible=!visible;toggle.GetComponentInChildren<Text>().text=visible?"Фон: включён":"Фон: выключен";});
                Func<MapBackground> read=()=>{var state=draft.Copy();state.name=name.text.Trim();state.offsetX=Number(x,int.MinValue,int.MaxValue);state.offsetY=Number(y,int.MinValue,int.MaxValue);state.scaleX=Number(sx,0,int.MaxValue);state.scaleY=Number(sy,0,int.MaxValue);state.opacity=Number(alpha,0,100);state.visible=visible;return state;};
                Func<bool> current=()=>!VisualActive&&CurrentOpenContext(context)&&ReferenceEquals(expected,CurrentBackground);
                Action refresh=null;
                var sources=ShapeOptionRow(body);
                foreach(int value in new[]{MapBackground.Texture,MapBackground.Flat,MapBackground.File})
                {int source=value;ToolsCommand(sources,source==0?"Текстура стен":source==1?"Текстура пола":"Файл изображения",source==0?"Стена":source==1?"Пол":"Файл",source==0?EditorIcon.Symbol.ViewNormal:source==1?EditorIcon.Symbol.ViewTextureFloor:EditorIcon.Symbol.OpenMap,()=>
                    {try {if(!current())throw new InvalidOperationException("Карта или фон изменились. Откройте форму снова.");var state=read();if(state.source!=source){state.source=source;state.name="";state.fileData="";state.visible=false;}BackgroundMenu(state,context,expected);}catch(Exception error){Notify(error.Message);}});}
                AddButton(body,draft.source==MapBackground.File?"Выбрать изображение…":"Выбрать текстуру…",()=>
                {
                    if(!current()){Notify("Карта или фон изменились. Откройте форму снова.");return;}
                    if(draft.source!=MapBackground.File){TexturePicker(name,draft.source==MapBackground.Flat?ResourceKind.Flat:ResourceKind.Wall,true,()=>{visible=true;toggle.GetComponentInChildren<Text>().text="Фон: включён";refresh();});return;}
                    try
                    {
                        var pending=read();CloseDialog();GetComponent<MobileFileBridge>().ImportImage((bytes,filename)=>
                        {
                            if(!current()){Notify("Карта или фон изменились во время выбора. Результат отменён.");return;}
                            try {BackgroundImageCodec.Decode(bytes,DecodePng);pending.name=Path.GetFileName(filename.Replace('\\','/'));pending.fileData=Convert.ToBase64String(bytes);pending.visible=true;BackgroundMenu(pending,context,expected);}
                            catch(Exception error){Notify("Фон: "+error.Message);BackgroundMenu(pending,context,expected);}
                        });
                    }catch(Exception error){Notify(error.Message);}
                });
                var previewHolder=Rect("Background preview",body);previewHolder.gameObject.AddComponent<LayoutElement>().preferredHeight=140;var previewRect=Rect("Image",previewHolder);Stretch(previewRect);var preview=previewRect.gameObject.AddComponent<RawImage>();preview.raycastTarget=false;preview.color=Color.clear;
                var previewAspect=previewRect.gameObject.AddComponent<AspectRatioFitter>();previewAspect.aspectMode=AspectRatioFitter.AspectMode.FitInParent;
                var info=Label(body,"",16);info.gameObject.AddComponent<LayoutElement>().preferredHeight=72;
                refresh=()=>
                {CloseBackgroundPreview();preview.texture=null;preview.color=Color.clear;try {var state=read();double w,h;var image=DecodeBackground(state,out w,out h);backgroundPreview=BackgroundTexture(image,state.name,256);preview.texture=backgroundPreview;preview.color=Color.white;previewAspect.aspectRatio=image.width/(float)image.height;info.text=image.width+" × "+image.height+" · "+(state.source==2?"сохраняется внутри проекта":"из подключённых ресурсов");}catch(Exception error){info.text=error.Message;}};
                name.onEndEdit.AddListener(_=>refresh());AddButton(body,"Обновить предпросмотр",()=>refresh());refresh();
                AddLabel(body,"Фон повторяется плиткой. Сдвиги задают её начало в координатах карты; масштабы X/Y независимы. Нулевой масштаб скрывает плитку. Он виден под сеткой и геометрией в 2D и сохраняется для каждой карты. Undo геометрии его не меняет.");
                if(draft.source==MapBackground.File)AddLabel(body,"PNG / JPEG / BMP / GIF (первый кадр), до 8 МиБ, 4096 по стороне и 4 млн пикселей. Некоторые сжатые BMP не поддержаны. Путь из .dbs нужно заменить явным выбором файла.");
                DialogApply(body,()=>{if(!current()){Notify("Карта или фон изменились. Откройте форму снова.");return false;}var state=read().Validated();if(state.visible){double w,h;DecodeBackground(state,out w,out h);}SetMapBackground(state);Notify("Фон карты сохранён.");return true;},"Применить",true);
            });
        }
    }
}
