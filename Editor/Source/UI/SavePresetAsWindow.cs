using System.Windows;
using System.Windows.Controls;
namespace Gp200.Replica;

internal sealed class SavePresetAsWindow:Window
{
    internal ComboBox Destination {get;}=new(){Tag="save-as-destination",MinWidth=330};
    internal int SelectedSlot=>Destination.SelectedIndex;
    public SavePresetAsWindow(int active,IReadOnlyDictionary<int,string> names)
    {
        EditorWindow.StyleDialog(this);Title=UiText.Language=="es"?"Guardar como":"Save As";
        SizeToContent=SizeToContent.WidthAndHeight;ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var panel=new StackPanel{Margin=new Thickness(16)};Content=panel;
        panel.Children.Add(new TextBlock{Text=UiText.Language=="es"?"Preset de destino":"Destination preset",Margin=new Thickness(0,0,0,8)});
        Destination.ItemsSource=Enumerable.Range(0,256).Select(i=>BankService.Position(i)+" · "+names.GetValueOrDefault(i,"")).ToArray();
        Destination.SelectedIndex=active;panel.Children.Add(Destination);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,14,0,0)};
        var cancel=new Button{Content=UiText.Language=="es"?"Cancelar":"Cancel",IsCancel=true,Margin=new Thickness(0,0,8,0)};
        var save=new Button{Content=UiText.Language=="es"?"Guardar":"Save",Tag="save-as-confirm",IsDefault=true};
        save.Click+=(_,_)=>{if(SelectedSlot>=0)DialogResult=true;};
        buttons.Children.Add(cancel);buttons.Children.Add(save);panel.Children.Add(buttons);
    }
}

public sealed partial class EditorWindow
{
    async Task SaveDeviceAs()
    {
        if(liveBusy||livePump||loadingLibrary)return;
        if(live==null||current==null||current!=liveEntry){status.Text=UiText.Language=="es"?"Conecta primero la GP-200.":"Connect the GP-200 first.";return;}
        var source=current;
        var names=entries.Where(e=>e.DeviceSlot.HasValue).GroupBy(e=>e.DeviceSlot!.Value).ToDictionary(g=>g.Key,g=>g.First().Document.Name);
        var dialog=new SavePresetAsWindow(source.Document.Index,names){Owner=this};
        if(dialog.ShowDialog()!=true)return;
        int target=dialog.SelectedSlot;
        if(target!=source.Document.Index&&UiDialogs.Show(this,
            UiText.Language=="es"?$"¿Guardar «{source.Document.Name}» en {BankService.Position(target)}? Se reemplazará el destino y se conservará una copia previa.":$"Save «{source.Document.Name}» to {BankService.Position(target)}? The destination will be replaced and backed up.",
            dialog.Title,MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
        if(current!=source||liveBusy||livePump||loadingLibrary)return;
        await SaveDevice(target);
    }
}
