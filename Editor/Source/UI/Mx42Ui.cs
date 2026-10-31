using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Gp200.Replica;

public sealed partial class EditorWindow
{
    internal void SelectMx42Preview(int slot){selectedModule=slot;Render();}
    readonly Dictionary<int,int> mxFamilyFilters=[];
    void AddMx42FamilySelector(PresetDocument doc)
    {
        if(!catalog.IsMx42)return;
        var identity=catalog.Identity(selectedModule,doc.GetEffect(selectedModule).Code);
        int family=mxFamilyFilters.GetValueOrDefault(selectedModule,identity.Known?identity.EffectiveFamily:selectedModule);
        if(selectedModule==5&&current==liveEntry&&live!=null&&!Mx42Integration.Supports(Mx42Capability.UniversalSlotSix))family=identity.Known?identity.EffectiveFamily:5;
        var combo=new ComboBox{Tag="mx42-family",ItemsSource=Catalog.FamilyNames.Concat(new[]{"NONE"}).ToArray(),SelectedIndex=identity.IsNone&&!mxFamilyFilters.ContainsKey(selectedModule)?12:family,IsEnabled=!(selectedModule==5&&current==liveEntry&&live!=null&&!Mx42Integration.Supports(Mx42Capability.UniversalSlotSix)),Margin=new Thickness(0,7,0,3)};algorithmHost.Children.Add(combo);
        int slot=selectedModule;
        combo.SelectionChanged+=(_,_)=>
        {
            if(combo.SelectedIndex<0||selectedModule!=slot||!ReferenceEquals(combo.Parent,algorithmHost))return;
            if(combo.SelectedIndex==12)
            {
                Guard(()=>Edit(d=>{var id=catalog.Identity(slot,d.GetEffect(slot).Code);d.SetAlgorithm(slot,catalog.ForFamily(slot,id.Known?id.EffectiveFamily:slot)[^1]);}));
                mxFamilyFilters.Remove(slot);
            }
            else mxFamilyFilters[slot]=combo.SelectedIndex;
            algorithmFilter="";Render();
        };
    }
    AlgorithmInfo[] InspectorAlgorithms(PresetDocument doc)
    {
        if(!catalog.IsMx42)return catalog.ForModule(selectedModule);
        var id=catalog.Identity(selectedModule,doc.GetEffect(selectedModule).Code);
        if(selectedModule==5&&current==liveEntry&&live!=null&&!Mx42Integration.Supports(Mx42Capability.UniversalSlotSix))
            return id.EffectiveFamily==5?catalog.ForFamily(5,5).Where(a=>!a.IsNone).ToArray():new[]{catalog.Find(5,doc.GetEffect(5).Code)!}.Where(a=>a!=null).ToArray();
        return catalog.ForFamily(selectedModule,mxFamilyFilters.GetValueOrDefault(selectedModule,id.Known?id.EffectiveFamily:selectedModule));
    }
    string EffectLabel(PresetDocument doc,int slot)=>catalog.IsMx42&&catalog.Find(slot,doc.GetEffect(slot).Code)?.IsNone==true?"NONE":catalog.IsMx42?catalog.FamilyLabel(slot,doc.GetEffect(slot).Code):PresetDocument.Modules[slot];
    internal FrameworkElement ChainVisual(PresetDocument doc,int slot,double height)
    {
        var effect=doc.GetEffect(slot);var info=catalog.Find(slot,effect.Code);
        if(info?.IsNone==true)return new TextBlock{Text="+",Tag=$"chain-empty-{slot}",FontSize=height*.7,Height=height,Foreground=Muted,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,TextAlignment=TextAlignment.Center,ToolTip="Empty slot · choose a family and effect"};
        return (FrameworkElement?)EffectArtwork.Load(EffectArtwork.ChainPath(EffectIcon(doc,slot),effect.Enabled,slot==selectedModule),height)
            ??new TextBlock{Text=EffectLabel(doc,slot),FontSize=12,Foreground=effect.Enabled?Accent:Muted,HorizontalAlignment=HorizontalAlignment.Center};
    }
    string EffectIcon(PresetDocument doc,int slot)
    {
        var info=catalog.Find(slot,doc.GetEffect(slot).Code);
        return info?.IsNone==true?"NONE":catalog.FamilyLabel(slot,doc.GetEffect(slot).Code) is "NAM"?"AMP":catalog.FamilyLabel(slot,doc.GetEffect(slot).Code);
    }
}
