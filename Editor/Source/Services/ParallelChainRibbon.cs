using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Gp200.Replica;

// Presentation and drag preview only. The document is committed once on drop by
// EditorWindow.Edit, so preview/cancel never sends MIDI or creates undo entries.
internal sealed class ParallelChainRibbon:Canvas
{
    readonly PresetDocument original;
    readonly Dictionary<byte,FrameworkElement> tiles=[];
    readonly Rect[] areas=new Rect[4];
    byte? dragged;
    PresetDocument? preview;
    (int group,int rank)? destination;
    bool accepted;
    readonly Action<byte,int,int> commit;
    internal Func<bool> CanDrag {get;set;}=()=>true;
    internal Action<bool>? Gesture {get;set;}
    static readonly Brush Teal=Color("#40CEC5"),Purple=Color("#B69AF2"),Muted=Color("#91A1B4");
    static Brush Color(string value)=>new SolidColorBrush((System.Windows.Media.Color)ColorConverter.ConvertFromString(value));
    internal Rect Area(int group)=>areas[group];
    internal Rect TileBounds(byte module){var tile=tiles[module];return new Rect(GetLeft(tile),GetTop(tile),tile.Width,tile.Height);}
    internal PresetDocument PreviewDocument=>preview??original;
    internal (int group,int rank)? Destination=>destination;

    internal ParallelChainRibbon(PresetDocument doc,Func<byte,FrameworkElement> makeTile,Action<byte,int,int> move)
    {
        original=new PresetDocument(doc.ToBytes());commit=move;
        Height=286;Background=Color("#141D26");ClipToBounds=true;AllowDrop=true;Tag="parallel-chain";
        foreach(byte module in doc.Chain)
        {
            var tile=makeTile(module);tiles.Add(module,tile);Children.Add(tile);
            Point down=default;
            tile.PreviewMouseLeftButtonDown+=(_,e)=>down=e.GetPosition(tile);
            tile.PreviewMouseMove+=(_,e)=>
            {
                if(e.LeftButton!=MouseButtonState.Pressed||dragged.HasValue||!CanDrag()||IsInteractive(e.OriginalSource as DependencyObject))return;
                if((e.GetPosition(tile)-down).Length>=6)BeginDrag(module,tile);
            };
        }
        DragOver+=(_,e)=>
        {
            if(dragged.HasValue&&e.Data.GetData("GP200.Module") is byte module&&module==dragged)
            {
                e.Effects=DragDropEffects.Move;e.Handled=true;PreviewMove(module,e.GetPosition(this));
                dragAdorner?.Update(e.GetPosition(this),null);
                ScrollAtEdge(e);
            }
            else {e.Effects=DragDropEffects.None;e.Handled=true;}
        };
        Drop+=(_,e)=>
        {
            if(dragged.HasValue&&e.Data.GetData("GP200.Module") is byte module&&module==dragged)
            {PreviewMove(module,e.GetPosition(this));accepted=destination.HasValue;e.Effects=accepted?DragDropEffects.Move:DragDropEffects.None;e.Handled=true;}
        };
        DragLeave+=(_,e)=>{if(!new Rect(RenderSize).Contains(e.GetPosition(this)))ClearPreview();};
        Resize(1000);
    }
    static bool IsInteractive(DependencyObject? node)
    {
        // Leave selectors and bypass switches to their own mouse interactions.
        while(node!=null)
        {
            if(node is ComboBox or CheckBox)return true;
            node=node is Visual?VisualTreeHelper.GetParent(node):LogicalTreeHelper.GetParent(node);
        }
        return false;
    }
    internal void Resize(double available)
    {
        var groups=ChainRouting.Groups(original);
        int[] units=[Math.Max(1,groups[0].Length),Math.Max(1,Math.Max(groups[1].Length,groups[2].Length)),Math.Max(1,groups[3].Length)];
        Width=Math.Max(available,units.Sum()*72+116);
        double unit=(Width-116)/units.Sum(),split=42+units[0]*unit,merge=split+units[1]*unit+16;
        areas[0]=new Rect(30,82,split-38,120);
        areas[1]=new Rect(split+8,8,merge-split-16,120);
        areas[2]=new Rect(split+8,156,merge-split-16,120);
        areas[3]=new Rect(merge+8,82,Width-merge-38,120);
        LayoutTiles();
    }
    void LayoutTiles()
    {
        var groups=ChainRouting.Groups(PreviewDocument);
        for(int group=0;group<4;group++)
        {
            var area=areas[group];int count=Math.Max(1,groups[group].Length);double step=area.Width/count;
            for(int i=0;i<groups[group].Length;i++)
            {
                byte module=groups[group][i];var tile=tiles[module];tile.Width=Math.Max(28,Math.Min(92,step-5));tile.Height=108;
                SetLeft(tile,area.X+i*step+(step-tile.Width)/2);SetTop(tile,area.Y+6);
                tile.Opacity=dragged==module?0:1;
            }
        }
        InvalidateVisual();
    }
    internal void PreviewMove(byte module,Point point)
    {
        if(!new Rect(0,0,Width,Height).Contains(point)){ClearPreview();return;}
        int group=point.X<areas[1].Left?0:point.X>areas[1].Right?3:point.Y<Height/2?1:2;
        var members=ChainRouting.Groups(original)[group].Where(m=>m!=module).ToArray();
        // Original positions stay the hit-test reference throughout this drag;
        // otherwise animated/reflowed tiles would make the target oscillate.
        var baseGroup=ChainRouting.Groups(original)[group];double step=areas[group].Width/Math.Max(1,baseGroup.Length);
        int rank=members.Count(m=>point.X>=areas[group].X+(Array.IndexOf(baseGroup,m)+.5)*step);
        dragged=module;destination=(group,rank);preview=new PresetDocument(original.ToBytes());
        ChainRouting.MoveToGroup(preview,module,group,rank);LayoutTiles();
    }
    internal void ClearPreview(){preview=null;destination=null;LayoutTiles();}
    internal void CancelPreview(){dragged=null;ClearPreview();}
    long lastScroll;
    void ScrollAtEdge(DragEventArgs e)
    {
        if(Environment.TickCount64-lastScroll<40)return;
        DependencyObject? parent=this;
        while(parent!=null&&parent is not ScrollViewer)parent=VisualTreeHelper.GetParent(parent);
        if(parent is not ScrollViewer scroll||scroll.ScrollableWidth<=0)return;
        double x=e.GetPosition(scroll).X;
        if(x<28)scroll.ScrollToHorizontalOffset(Math.Max(0,scroll.HorizontalOffset-16));
        else if(x>scroll.ViewportWidth-28)scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset+16);
        lastScroll=Environment.TickCount64;
    }
    ChainDragFeedback? dragAdorner;
    void BeginDrag(byte module,FrameworkElement tile)
    {
        var layer=AdornerLayer.GetAdornerLayer(this);dragAdorner=new ChainDragFeedback(this,tile);dragAdorner.Update(Mouse.GetPosition(this),null);layer?.Add(dragAdorner);
        dragged=module;accepted=false;tile.Opacity=.35;
        Gesture?.Invoke(true);
        (int group,int rank)? chosen=null;
        try
        {
            var effect=DragDrop.DoDragDrop(tile,new DataObject("GP200.Module",module),DragDropEffects.Move);
            if(effect==DragDropEffects.Move&&accepted)chosen=destination;
        }
        finally{layer?.Remove(dragAdorner);dragAdorner=null;dragged=null;ClearPreview();Gesture?.Invoke(false);}
        if(chosen.HasValue)commit(module,chosen.Value.group,chosen.Value.rank);
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);double split=areas[1].Left-8,merge=areas[1].Right+8,mid=142,top=68,bottom=216;
        var groups=ChainRouting.Groups(PreviewDocument);
        for(int group=0;group<4;group++)
        {
            var area=areas[group];var color=group==1?Teal:group==2?Purple:Muted;
            dc.DrawRoundedRectangle(group==destination?.group?Color("#243B47"):Color("#17212B"),new Pen(group==destination?.group?color:Color("#2C3948"),1),area,6,6);
        }
        var wire=new Pen(Color("#526578"),2);
        dc.DrawLine(wire,new Point(16,mid),new Point(split,mid));dc.DrawLine(wire,new Point(split,top),new Point(split,bottom));
        dc.DrawLine(new Pen(Teal,2),new Point(split,top),new Point(merge,top));dc.DrawLine(new Pen(Purple,2),new Point(split,bottom),new Point(merge,bottom));
        dc.DrawLine(wire,new Point(merge,top),new Point(merge,bottom));dc.DrawLine(wire,new Point(merge,mid),new Point(Width-16,mid));
        dc.DrawEllipse(Teal,null,new Point(split,mid),3,3);dc.DrawEllipse(Purple,null,new Point(merge,mid),3,3);
        Label(dc,"IN",3,mid-24,Muted);Label(dc,"OUT",Width-29,mid-24,Muted);
        Label(dc,"SPLIT",split-15,mid-18,Teal);Label(dc,"MIX",merge-11,mid-18,Purple);
        for(int group=0;group<4;group++)
        {
            var area=areas[group];var color=group==1?Teal:group==2?Purple:Muted;
            if(group is 1 or 2)Label(dc,group==1?"A":"B",area.X-5,area.Y+48,color);
            if(groups[group].Length==0)Label(dc,EditorWindow.ChainDropHint,area.X+7,area.Y+50,Muted,9,Math.Max(10,area.Width-14));
        }
        if(dragged.HasValue&&preview!=null)
        {
            var rect=TileBounds(dragged.Value);dc.DrawRoundedRectangle(Color("#29454B"),new Pen(Teal,2){DashStyle=DashStyles.Dash},rect,5,5);
            Label(dc,PresetDocument.Modules[dragged.Value],rect.X+5,rect.Y+44,Teal);
        }
    }
    static void Label(DrawingContext dc,string text,double x,double y,Brush brush,double size=10,double maxWidth=0)
    {
        var formatted=new FormattedText(text,System.Globalization.CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),size,brush,1);
        if(maxWidth>0){formatted.MaxTextWidth=maxWidth;formatted.Trimming=TextTrimming.CharacterEllipsis;}
        dc.DrawText(formatted,new Point(x,y));
    }
}

public sealed partial class EditorWindow
{
    internal static string ChainDropHint=>ChainText("Drop here","Arrastra aquí");
    ParallelChainRibbon BuildParallelChain(PresetDocument doc,double available)
    {
        ParallelChainRibbon? ribbon=null;var entry=current;var session=live;
        FrameworkElement Tile(byte module)
        {
            var effect=doc.GetEffect(module);var algorithm=catalog.Find(module,effect.Code);
            var block=new StackPanel{Tag=$"parallel-module-{module}"};
            var bypass=new CheckBox{Style=(Style)Resources["ChainToggle"],Content=EffectLabel(doc,module),FontSize=10,IsChecked=algorithm?.IsNone==true?false:effect.Enabled,IsEnabled=algorithm?.IsNone!=true,Margin=new Thickness(2,0,0,3)};
            bypass.Click+=(_,_)=>Guard(()=>Edit(d=>{var e=d.GetEffect(module);d.SetEffect(module,bypass.IsChecked==true,e.Parameters);}));block.Children.Add(bypass);
            var icon=ChainVisual(doc,module,50);
            if(icon!=null){icon.HorizontalAlignment=HorizontalAlignment.Center;icon.Margin=new Thickness(0);}
            var button=new Button{Content=icon,Height=57,Margin=new Thickness(0),Padding=new Thickness(2),Background=module==selectedModule?Brush("#1A4B4E"):Brush("#151E28"),ToolTip=$"{EffectLabel(doc,module)} · {algorithm?.Name}"};
            button.Click+=(_,_)=>{selectedModule=module;algorithmFilter="";Render();tabs.SelectedIndex=0;};block.Children.Add(button);
            var model=new FrameworkElementFactory(typeof(TextBlock));model.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding("Name"));model.SetValue(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis);
            var choice=new ComboBox{Tag=$"chain-model-{module}",ItemsSource=catalog.ForEffect(module,effect.Code),ItemTemplate=new DataTemplate{VisualTree=model},SelectedItem=algorithm,Margin=new Thickness(0,3,0,0),MinHeight=26,ToolTip=algorithm?.Name};
            choice.SelectionChanged+=(_,_)=>
            {
                if(ribbon?.Parent!=chain||choice.SelectedItem is not AlgorithmInfo next||current==null)return;
                selectedModule=module;algorithmFilter="";var editable=current==liveEntry&&desiredLive!=null?desiredLive:current.Document;
                if(editable.GetEffect(module).Code!=next.Code)Guard(()=>Edit(d=>d.SetAlgorithm(module,next)));
            };
            block.Children.Add(choice);return block;
        }
        ribbon=new ParallelChainRibbon(doc,Tile,(m,g,r)=>{if(current==entry&&live==session)MoveParallelBlock(m,g,r);});
        ribbon.CanDrag=()=>!polling&&!sceneStatusPolling&&!liveBusy&&!livePump&&pendingLive==null&&!loadingLibrary&&!devicePanelOpen&&current==entry&&(!entry!.DeviceSlot.HasValue||entry==liveEntry&&live!=null);
        ribbon.Gesture=active=>chainGesture=active;ribbon.Resize(available-16);return ribbon;
    }
    void MoveParallelBlock(byte module,int group,int rank)=>Guard(()=>
    {
        if(current==null||!ChainRouting.Enabled(current.Document.FxLoopMode))return;
        selectedModule=module;Edit(d=>ChainRouting.MoveToGroup(d,module,group,rank));
    });
}
