using System.Globalization;
using System.Numerics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Gp200.Replica;

/// <summary>Editor of verified EQ selectors. Response is an illustrative biquad model,
/// not a measurement or a reconstruction of the device DSP.</summary>
public sealed class EqGraph:FrameworkElement
{
    readonly Dictionary<int,float> values;
    readonly Dictionary<int,float> confirmed;
    readonly bool enabled;
    int dragging=-1;Point origin;
    public event Action<IReadOnlyList<DeviceSettingChange>>? Committed;
    static readonly int[] Frequencies=[1,8,12,16,20,28];
    public EqGraph(DeviceSettings state)
    {
        values=DeviceSettings.Equalizer.ToDictionary(d=>d.Selector,d=>state.Eq(d.Selector));enabled=state.Global(30)!=0;
        confirmed=new(values);
        Height=270;MinWidth=440;Margin=new Thickness(0,8,0,12);Cursor=Cursors.Cross;
        ToolTip=UiText.T("Arrastra las bandas para cambiar frecuencia y ganancia; los extremos cambian Low/High Cut. Se envía al soltar. Curva orientativa: no representa una medición del firmware; no incluye Master Volume.");
    }
    Rect Plot=>new(48,16,Math.Max(1,ActualWidth-68),Math.Max(1,ActualHeight-48));
    public static double Frequency(double fraction)=>20*Math.Pow(1000,Math.Clamp(fraction,0,1));
    double X(double hz)=>Plot.Left+Math.Log10(Math.Clamp(hz,20,20000)/20)/3*Plot.Width;
    double Y(double gain)=>Plot.Top+(20-Math.Clamp(gain,-20,20))/40*Plot.Height;
    Point Handle(int i)=>new(X(values[Frequencies[i]]),Y(i is 0 or 5?0:values[Frequencies[i]+2]));
    void Text(DrawingContext dc,string text,Point at,Brush? color=null)=>dc.DrawText(new FormattedText(text,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),11,color??Brushes.LightGray,VisualTreeHelper.GetDpi(this).PixelsPerDip),at);
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(12,18,25)),null,new Rect(RenderSize));
        foreach(int hz in new[]{20,100,1000,10000,20000}){double x=X(hz);dc.DrawLine(new Pen(Brushes.DimGray,.5),new(x,Plot.Top),new(x,Plot.Bottom));Text(dc,hz>=1000?$"{hz/1000}k":hz.ToString(),new(x-10,Plot.Bottom+5));}
        foreach(int db in new[]{-20,-10,0,10,20}){double y=Y(db);dc.DrawLine(new Pen(Brushes.DimGray,db==0?1:.5),new(Plot.Left,y),new(Plot.Right,y));Text(dc,db.ToString(),new(10,y-8));}
        var curve=new StreamGeometry();using(var g=curve.Open())
        {
            for(int i=0;i<=360;i++){double f=Frequency(i/360.0);var point=new Point(X(f),Y(ResponseDb(values,f)));if(i==0)g.BeginFigure(point,false,false);else g.LineTo(point,true,false);}
        }
        dc.PushClip(new RectangleGeometry(Plot));dc.DrawGeometry(null,new Pen(enabled?Brushes.Turquoise:Brushes.Gray,2),curve);dc.Pop();
        for(int i=0;i<6;i++){var p=Handle(i);dc.DrawEllipse(i==dragging?Brushes.Gold:Brushes.Turquoise,new Pen(Brushes.White,1),p,7,7);Text(dc,i==0?"LC":i==5?"HC":i.ToString(),new(p.X-7,p.Y-24));}
        if(dragging>=0){int selector=Frequencies[dragging];string label=$"{values[selector]:0} Hz"+(dragging is 0 or 5?"":$" · {values[selector+2]:0.0} dB");Text(dc,label,new(Plot.Left+8,Plot.Top+7),Brushes.Gold);}
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);var point=e.GetPosition(this);
        dragging=Enumerable.Range(0,6).OrderBy(i=>(Handle(i)-point).LengthSquared).First();
        if((Handle(dragging)-point).Length>20){dragging=-1;return;}
        origin=point;CaptureMouse();e.Handled=true;InvalidateVisual();
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);if(dragging<0||!IsMouseCaptured)return;
        var p=e.GetPosition(this);values[Frequencies[dragging]]=(float)Frequency((p.X-Plot.Left)/Plot.Width);
        if(dragging is >0 and <5)values[Frequencies[dragging]+2]=(float)Math.Clamp(20-40*(p.Y-Plot.Top)/Plot.Height,-20,20);
        InvalidateVisual();
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);if(dragging<0)return;int handle=dragging;dragging=-1;ReleaseMouseCapture();InvalidateVisual();
        if((e.GetPosition(this)-origin).Length<2){foreach(var pair in confirmed)values[pair.Key]=pair.Value;InvalidateVisual();return;}
        int selector=Frequencies[handle];var changes=new List<DeviceSettingChange>{DeviceSettings.SetEq(selector,values[selector])};
        if(handle is >0 and <5)changes.Add(DeviceSettings.SetEq(selector+2,values[selector+2]));Committed?.Invoke(changes);e.Handled=true;
    }
    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if(dragging>=0)foreach(var pair in confirmed)values[pair.Key]=pair.Value;
        dragging=-1;InvalidateVisual();
    }
    public static double ResponseDb(IReadOnlyDictionary<int,float> v,double frequency)
    {
        // Standard peaking biquads at 44.1 kHz and second-order cut approximations.
        double omega=2*Math.PI*frequency/44100;Complex z=Complex.FromPolarCoordinates(1,-omega),z2=z*z;double db=0;
        foreach(int s in new[]{8,12,16,20})
        {
            double a=Math.Pow(10,v[s+2]/40),w=2*Math.PI*v[s]/44100,alpha=Math.Sin(w)/(2*v[s+1]),c=-2*Math.Cos(w);
            var numerator=(1+alpha*a)+c*z+(1-alpha*a)*z2;var denominator=(1+alpha/a)+c*z+(1-alpha/a)*z2;
            db+=20*Math.Log10(Math.Max(1e-12,(numerator/denominator).Magnitude));
        }
        if(v[1]>19)db-=10*Math.Log10(1+Math.Pow(v[1]/frequency,4));
        if(v[28]<20001)db-=10*Math.Log10(1+Math.Pow(frequency/v[28],4));return db;
    }
}
