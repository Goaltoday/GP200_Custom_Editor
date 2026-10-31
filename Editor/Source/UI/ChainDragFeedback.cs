using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace Gp200.Replica;

sealed class ChainDragFeedback:Adorner
{
    readonly BitmapSource preview;readonly Size size;Point location;Rect? insertion;bool positioned;
    internal BitmapSource Snapshot=>preview;
    public ChainDragFeedback(UIElement surface,FrameworkElement source):base(surface)
    {
        IsHitTestVisible=false;size=new Size(Math.Max(1,source.ActualWidth),Math.Max(1,source.ActualHeight));
        // Paint into local coordinates. Rendering the translated source visual
        // directly can leave a clipped duplicate at the top-left of the layer.
        var visual=new DrawingVisual();
        using(var dc=visual.RenderOpen())dc.DrawRectangle(new VisualBrush(source){Stretch=Stretch.Fill},null,new Rect(size));
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(size.Width),(int)Math.Ceiling(size.Height),96,96,PixelFormats.Pbgra32);bitmap.Render(visual);bitmap.Freeze();preview=bitmap;
    }
    public void Update(Point point,Rect? destination){positioned=true;location=point;insertion=destination;InvalidateVisual();}
    protected override void OnRender(DrawingContext dc)
    {
        if(!positioned)return;
        if(insertion.HasValue)dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(60,64,206,197)),new Pen(Brushes.Turquoise,2){DashStyle=DashStyles.Dash},insertion.Value,5,5);
        var rect=new Rect(location.X+12,location.Y-size.Height/2,size.Width,size.Height);
        dc.PushOpacity(.8);dc.DrawImage(preview,rect);dc.Pop();
    }
}
internal sealed class LinearChainPreview
{
    readonly FrameworkElement[] tiles;
    readonly Rect[] slots;
    readonly Transform[] transforms;
    readonly double[] opacity;
    readonly int source;
    internal int Destination {get;private set;}
    internal Rect Placeholder {get;private set;}
    internal LinearChainPreview(StackPanel chain,FrameworkElement dragged)
    {
        tiles=chain.Children.OfType<FrameworkElement>().Where(t=>t.Tag is string tag&&tag.StartsWith("chain-module-",StringComparison.Ordinal)).ToArray();
        source=Array.IndexOf(tiles,dragged);if(source<0)throw new InvalidOperationException("Missing chain tile.");
        slots=tiles.Select(t=>new Rect(t.TranslatePoint(new Point(),chain),t.RenderSize)).ToArray();transforms=tiles.Select(t=>t.RenderTransform).ToArray();opacity=tiles.Select(t=>t.Opacity).ToArray();
    }
    internal void Update(Point point)
    {
        int rank=Enumerable.Range(0,tiles.Length).Count(i=>i!=source&&point.X>=slots[i].X+slots[i].Width/2);
        Destination=rank+(rank>source?1:0);Placeholder=slots[rank];
        var order=Enumerable.Range(0,tiles.Length).Where(i=>i!=source).ToList();order.Insert(rank,source);
        for(int i=0;i<order.Count;i++)
        {
            int index=order[i];var transform=new TransformGroup();transform.Children.Add(transforms[index]);transform.Children.Add(new TranslateTransform(slots[i].X-slots[index].X,0));tiles[index].RenderTransform=transform;
            tiles[index].Opacity=index==source?0:opacity[index];
        }
    }
    internal void Restore(){for(int i=0;i<tiles.Length;i++){tiles[i].RenderTransform=transforms[i];tiles[i].Opacity=opacity[i];}}
}
public sealed partial class EditorWindow
{
    ChainDragFeedback? chainFeedback;
    LinearChainPreview? linearChainPreview;
    int? chainDropDestination;
    void StartChainDrag(FrameworkElement source,DataObject data)
    {
        if(polling||sceneStatusPolling||liveBusy||livePump||pendingLive!=null||loadingLibrary||devicePanelOpen)return;
        FrameworkElement previewSource=data.GetData("GP200.Module") is byte&&source.Parent is FrameworkElement parent?parent:source;
        var layer=AdornerLayer.GetAdornerLayer(chain);var feedback=new ChainDragFeedback(chain,previewSource);chainFeedback=feedback;
        feedback.Update(System.Windows.Input.Mouse.GetPosition(chain),null);layer?.Add(feedback);
        if(data.GetData("GP200.Module") is byte&&source.Parent is FrameworkElement block)linearChainPreview=new LinearChainPreview(chain,block);
        double opacity=source.Opacity;source.Opacity=.4;chainGesture=true;chainDropDestination=null;int? destination=null;
        try{if(DragDrop.DoDragDrop(source,data,DragDropEffects.Move)==DragDropEffects.Move)destination=chainDropDestination;}
        finally{source.Opacity=opacity;linearChainPreview?.Restore();linearChainPreview=null;layer?.Remove(feedback);chainFeedback=null;chainGesture=false;chainDropDestination=null;}
        if(destination.HasValue)Guard(()=>ApplyChainDrop(data,destination.Value));
        else if(renderQueued&&!liveBusy&&!livePump)Render();
    }
    void ConfigureChainDrop(FrameworkElement target,Func<DragEventArgs,int> position)
    {
        target.AllowDrop=true;
        target.DragOver+=(_,args)=>
        {
            if(!args.Data.GetDataPresent("GP200.Module")&&!args.Data.GetDataPresent("GP200.Loop")&&!args.Data.GetDataPresent("GP200.Split")){args.Effects=DragDropEffects.None;return;}
            args.Effects=DragDropEffects.Move;args.Handled=true;
            Point point=args.GetPosition(chain);Rect? destination=null;
            if(linearChainPreview!=null){linearChainPreview.Update(point);destination=linearChainPreview.Placeholder;}
            else {var origin=target.TranslatePoint(new Point(),chain);destination=new Rect(origin,new Size(Math.Max(18,target.ActualWidth),Math.Max(24,target.ActualHeight)));}
            chainFeedback?.Update(point,destination);
        };
        target.Drop+=(_,args)=>Guard(()=>
        {
            if(chainFeedback==null){args.Effects=DragDropEffects.None;args.Handled=true;return;}
            if(linearChainPreview!=null)linearChainPreview.Update(args.GetPosition(chain));
            chainDropDestination=linearChainPreview?.Destination??position(args);args.Effects=DragDropEffects.Move;args.Handled=true;
        });
    }
    internal void ApplyChainDrop(IDataObject data,int destination)
    {
            if(data.GetData("GP200.Module") is byte source)
            {
                selectedModule=source;
                Edit(d=>{if(catalog.IsMx42&&ChainRouting.Enabled(d.FxLoopMode)){ChainRouting.MoveBlock(d,source,destination);return;}var order=d.Chain.ToList();int from=order.IndexOf(source);if(from<0)return;order.RemoveAt(from);order.Insert(Math.Clamp(destination-(from<destination?1:0),0,order.Count),source);d.SetChain(order);});
            }
            else if(data.GetData("GP200.Loop") is bool returnNode)Edit(d=>d.SetLoopNode(returnNode,destination));
            else if(data.GetData("GP200.Split") is bool)Edit(d=>d.SetCustomRouting(d.FxLoopMode,d.RoutingRaw[0],Math.Clamp(destination,d.RoutingRaw[0],d.RoutingRaw[1]),d.RoutingRaw[1]));
    }
}
