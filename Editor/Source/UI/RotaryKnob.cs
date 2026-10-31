using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
namespace Gp200.Replica;
/// <summary>Presentation-only rotary Slider; existing value and live-edit handlers are reused.</summary>
public sealed class RotaryKnob:RangeBase
{
    public double TickFrequency {get;set;}=1;
    public bool IsSnapToTickEnabled {get;set;}=true;
    Point origin;double initial;bool dragging;
    static readonly Brush Ring=new SolidColorBrush(Color.FromRgb(55,67,79)),Accent=new SolidColorBrush(Color.FromRgb(49,216,202));
    public RotaryKnob()
    {
        Width=76;Height=76;Focusable=true;Cursor=Cursors.SizeNS;Template=new ControlTemplate(typeof(RotaryKnob)){VisualTree=new FrameworkElementFactory(typeof(Border))};
        ValueChanged+=(_,_)=>InvalidateVisual();IsEnabledChanged+=(_,_)=>InvalidateVisual();
    }
    internal double Constrain(double value)
    {
        if(IsSnapToTickEnabled&&TickFrequency>0)value=Minimum+Math.Round((value-Minimum)/TickFrequency)*TickFrequency;
        return Math.Clamp(value,Minimum,Maximum);
    }
    internal void BeginDrag(Point point){origin=point;initial=Value;dragging=true;}
    internal void DragTo(Point point)
    {
        if(dragging)Value=Constrain(initial+(origin.Y-point.Y+(point.X-origin.X)*.25)*(Maximum-Minimum)/180);
    }
    internal void EndDrag()=>dragging=false;
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e){Focus();BeginDrag(e.GetPosition(this));if(!CaptureMouse())EndDrag();e.Handled=true;}
    protected override void OnMouseMove(MouseEventArgs e){if(!dragging)return;DragTo(e.GetPosition(this));e.Handled=true;}
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e){EndDrag();ReleaseMouseCapture();e.Handled=true;}
    protected override void OnLostMouseCapture(MouseEventArgs e){dragging=false;base.OnLostMouseCapture(e);}
    protected override void OnMouseWheel(MouseWheelEventArgs e){Value=Constrain(Value+Math.Sign(e.Delta)*(TickFrequency>0?TickFrequency:1));e.Handled=true;}
    protected override void OnKeyDown(KeyEventArgs e)
    {
        double step=TickFrequency>0?TickFrequency:1;
        if(e.Key==Key.Home)Value=Minimum;else if(e.Key==Key.End)Value=Maximum;
        else if(e.Key is Key.Up or Key.Right)Value=Constrain(Value+step);else if(e.Key is Key.Down or Key.Left)Value=Constrain(Value-step);else{base.OnKeyDown(e);return;}e.Handled=true;
    }
    protected override void OnRender(DrawingContext dc)
    {
        var center=new Point(ActualWidth/2,ActualHeight/2);double r=Math.Min(ActualWidth,ActualHeight)*.34;
        dc.DrawRectangle(Brushes.Transparent,null,new Rect(RenderSize));
        for(int i=0;i<=10;i++){double a=(135+i*27)*Math.PI/180;dc.DrawLine(new Pen(Ring,1.4),At(r+8,a),At(r+12,a));}
        dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(6,10,15)),null,new Point(center.X,center.Y+3),r+4,r+4);
        var gradient=new LinearGradientBrush(Color.FromRgb(65,76,89),Color.FromRgb(14,19,26),new Point(0,0),new Point(.8,1));dc.DrawEllipse(gradient,new Pen(Ring,1),center,r,r);
        dc.DrawEllipse(new LinearGradientBrush(Color.FromRgb(30,39,49),Color.FromRgb(11,16,23),90),new Pen(new SolidColorBrush(Color.FromRgb(19,27,36)),1),center,r-6,r-6);
        double fraction=Maximum>Minimum?Math.Clamp((Value-Minimum)/(Maximum-Minimum),0,1):0;
        var arc=new StreamGeometry();using(var context=arc.Open()){context.BeginFigure(At(r+4,135*Math.PI/180),false,false);context.ArcTo(At(r+4,(135+fraction*270)*Math.PI/180),new Size(r+4,r+4),0,fraction*270>180,SweepDirection.Clockwise,true,false);}
        if(fraction>0)dc.DrawGeometry(null,new Pen(IsEnabled?Accent:Ring,2.3),arc);
        double angle=(135+fraction*270)*Math.PI/180;dc.DrawLine(new Pen(IsEnabled?Accent:Ring,3){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round},At(r*.43,angle),At(r*.79,angle));
        if(IsKeyboardFocused)dc.DrawEllipse(null,new Pen(Accent,1),center,r+16,r+16);
        Point At(double radius,double angle)=>new(center.X+Math.Cos(angle)*radius,center.Y+Math.Sin(angle)*radius);
    }
}
