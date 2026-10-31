using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
namespace Gp200.Replica;
public sealed partial class EditorWindow
{
    readonly CheckBox enableChain=new(){Content="Enable CHAIN",Margin=new Thickness(12,0,2,0),VerticalAlignment=VerticalAlignment.Center};
    readonly ContentControl chainOptions=new();
    readonly Dictionary<LibraryEntry,int> nativeChainModes=[];
    readonly Dictionary<LibraryEntry,int> chainSplits=[];
    static string ChainText(string en,string es)=>UiText.Language=="es"?es:en;
    void SetupChainControls()
    {
        enableChain.Style=(Style)FindResource("ChainToggle");enableChain.FontSize=12;
        enableChain.ToolTip=ChainText("Enable independent A/B routing. Save to retain changes.","Activa las ramas A/B. Guardar conserva los cambios.");
        enableChain.Click+=(_,_)=>Guard(()=>
        {
            if(current==null)return;
            var p=current.Document;bool next=!ChainRouting.Enabled(p.FxLoopMode);
            int split=Math.Clamp(chainSplits.GetValueOrDefault(current,ChainRouting.Boundary(p)),p.RoutingRaw[0],p.RoutingRaw[1]);
            Edit(d=>ChainRouting.SetEnabled(d,next,split,nativeChainModes.GetValueOrDefault(current,0)));
        });
    }
    void RenderChainControls()
    {
        bool supported=CatalogRuntime.Current.IsMx42||catalog.IsMx42;
        enableChain.Visibility=catalog.IsMx42?Visibility.Visible:Visibility.Collapsed;
        enableChain.IsEnabled=supported&&!(catalog.IsMx42&&current==liveEntry&&live!=null&&!Mx42ChainUsb.Available)&&current!=null&&(!current.DeviceSlot.HasValue||current==liveEntry&&live!=null)&&!liveBusy&&!livePump&&!loadingLibrary&&!devicePanelOpen;
        chainOptions.Content=null;chainOptions.Visibility=Visibility.Collapsed;
        if(current==null){enableChain.IsChecked=false;return;}
        var doc=current.Document;bool on=ChainRouting.Enabled(doc.FxLoopMode);enableChain.IsChecked=on;
        if(doc.FxLoopMode is 0 or 1)nativeChainModes[current]=doc.FxLoopMode;
        if(ChainRouting.Extended(doc.FxLoopMode))chainSplits[current]=ChainRouting.Boundary(doc);
        if(!supported||!on)return;
        chainOptions.Visibility=Visibility.Visible;
        var panel=new StackPanel{Margin=new Thickness(4,2,4,4),IsEnabled=!(catalog.IsMx42&&current==liveEntry&&live!=null&&!Mx42ChainUsb.Available)};
        if(!panel.IsEnabled)panel.ToolTip="The connected MX42 firmware does not advertise the CHAIN USB extension.";
        var sound=new WrapPanel();panel.Children.Add(sound);
        if(!ChainRouting.Independent(doc))panel.Children.Add(Text(ChainText("Legacy BLEND · the first BLEND edit separates it from VOL.","BLEND heredado · el primer cambio de BLEND lo separa de VOL."),10,brush:Muted));
        void Parameter(string label,double min,double max,double value,Action<PresetDocument,double> set,double reset)
        {
            var group=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,18,4)};var title=Text(label,11);title.Width=48;group.Children.Add(title);
            var slider=new Slider{Minimum=min,Maximum=max,Value=value,TickFrequency=1,IsSnapToTickEnabled=true,Width=170,Tag="chain-"+label};
            var number=Text(((int)value).ToString(),12);number.Width=30;number.Margin=new Thickness(7,0,0,0);slider.ValueChanged+=(_,e)=>number.Text=((int)e.NewValue).ToString();
            slider.PreviewMouseDoubleClick+=(_,e)=>{slider.Value=reset;e.Handled=true;};AddDelayedSlider(slider,v=>Edit(d=>set(d,v)));group.Children.Add(slider);group.Children.Add(number);sound.Children.Add(group);
        }
        Parameter("BLEND",0,100,Math.Clamp(ChainRouting.Blend(doc),0,100),(d,v)=>ChainRouting.SetBlend(d,(float)v),50);
        var alignment=ChainRouting.Alignment(doc);Parameter("ALIGN",-32,32,alignment.frames,(d,v)=>ChainRouting.SetAlignment(d,(int)v,ChainRouting.Alignment(d).inverted),0);
        var phase=new CheckBox{Content="PHASE B · invert",IsChecked=alignment.inverted,Style=(Style)FindResource("ChainToggle"),FontSize=11,Margin=new Thickness(0,0,0,4),Tag="chain-phase"};
        phase.Click+=(_,_)=>Guard(()=>Edit(d=>ChainRouting.SetAlignment(d,ChainRouting.Alignment(d).frames,phase.IsChecked==true)));sound.Children.Add(phase);
        panel.ToolTip=ChainText("BLEND: 0 = A, 50 = centre, 100 = B. ALIGN: + delays A / − delays B (stereo samples). Drag blocks between IN, A, B and OUT to change routing.","BLEND: 0 = A, 50 = centro, 100 = B. ALIGN: + retrasa A / − retrasa B (muestras estéreo). Arrastra bloques entre IN, A, B y OUT para cambiar las rutas.");
        chainOptions.Content=panel;
    }
    void UpdateChainInteraction()
    {
        enableChain.IsEnabled=(CatalogRuntime.Current.IsMx42||catalog.IsMx42)&&!(catalog.IsMx42&&current==liveEntry&&live!=null&&!Mx42ChainUsb.Available)&&current!=null&&(!current.DeviceSlot.HasValue||current==liveEntry&&live!=null)&&!liveBusy&&!livePump&&!loadingLibrary&&!devicePanelOpen;
    }
}
