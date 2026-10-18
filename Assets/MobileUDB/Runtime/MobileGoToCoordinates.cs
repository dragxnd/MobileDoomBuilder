// GPL-3.0. UDB CenterOnCoordinatesForm / ClassicMode.centeroncoordinates.
using System;using System.Globalization;using UnityEngine;
namespace MobileUDB
{
    public sealed partial class MobileEditor
    {
        // Desktop form remembers the last accepted pair, initially (0,0).
        Vector2 goToCoordinates;
        static int GoToCoordinate(string text,int previous)
        {
            int value;
            if(!int.TryParse(text,NumberStyles.Integer,CultureInfo.InvariantCulture,out value))value=previous;
            return Math.Max(short.MinValue,Math.Min(short.MaxValue,value));
        }
        void GoToCoordinatesMenu()
        {
            if(VisualActive){Notify("Координаты обзора доступны в 2D.");return;}
            CancelDrag();EnsureClassicView();var context=CaptureOpenContext();
            ShowDialog("Перейти к координатам",body=>
            {
                ToolsCaption(body,"ОБЗОР · X / Y");
                var x=Field(body,"X",((int)goToCoordinates.x).ToString(CultureInfo.InvariantCulture));
                var y=Field(body,"Y",((int)goToCoordinates.y).ToString(CultureInfo.InvariantCulture));
                AddLabel(body,"Центр обзора переместится в эту точку. Масштаб, выделение и карта сохранятся.");
                AddLabel(body,"Doom / Hexen: −32768…32767. Значения за пределами диапазона ограничиваются. Пустое поле сохраняет последнюю принятую координату.");
                DialogApply(body,()=>
                {
                    if(VisualActive||!CurrentOpenContext(context)){Notify("Карта изменилась. Откройте переход снова.");return false;}
                    goToCoordinates=new Vector2(GoToCoordinate(x.text,(int)goToCoordinates.x),GoToCoordinate(y.text,(int)goToCoordinates.y));
                    CancelInvoke(nameof(Fit));View.center=goToCoordinates;View.SetVerticesDirty();
                    Notify("Центр обзора: "+goToCoordinates.x.ToString(CultureInfo.InvariantCulture)+" / "+goToCoordinates.y.ToString(CultureInfo.InvariantCulture));return true;
                },"Перейти",true);
            });
        }
    }
}
