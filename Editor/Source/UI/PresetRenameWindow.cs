using System.Windows;
using System.Windows.Controls;
namespace Gp200.Replica;
public sealed class PresetRenameWindow:Window
{
    public string NewName {get;private set;}="";
    public PresetRenameWindow(string position,string current)
    {
        EditorWindow.StyleDialog(this);Title=UiText.T("Renombrar")+" · "+position;Width=380;SizeToContent=SizeToContent.Height;ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var panel=new StackPanel{Margin=new Thickness(20)};Content=panel;
        panel.Children.Add(new TextBlock{Text=UiText.T("Nombre (ASCII, máximo 16)"),Margin=new Thickness(0,0,0,8)});
        var name=new TextBox{Text=current,MaxLength=16};panel.Children.Add(name);
        var error=new TextBlock{Foreground=System.Windows.Media.Brushes.Salmon,Margin=new Thickness(0,5,0,0)};panel.Children.Add(error);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};panel.Children.Add(buttons);
        var cancel=new Button{Content=UiText.T("Cancelar"),IsCancel=true,Padding=new Thickness(12,6,12,6)};buttons.Children.Add(cancel);
        var save=new Button{Content=UiText.T("Renombrar"),IsDefault=true,Padding=new Thickness(12,6,12,6)};buttons.Children.Add(save);
        save.Click+=(_,_)=>{string value=name.Text.Trim();if(value.Length<1||value.Any(c=>c<32||c>126)){error.Text=UiText.T("Nombre: 1–16 caracteres ASCII imprimibles.");return;}NewName=value;DialogResult=true;};
        Loaded+=(_,_)=>{name.Focus();name.SelectAll();};
    }
}
public sealed partial class EditorWindow
{
    async Task RenameWorkspacePreset(LibraryEntry? target)
    {
        if(target==null||liveBusy||livePump||loadingLibrary)return;
        var dialog=new PresetRenameWindow(BankService.Position(target.DeviceSlot??target.Document.Index),target.LibraryName){Owner=this};
        if(dialog.ShowDialog()!=true)return;
        await ApplyWorkspaceRename(target,dialog.NewName);
    }
    internal async Task ApplyWorkspaceRename(LibraryEntry target,string requested)
    {
        if(!target.DeviceSlot.HasValue)
        {
            var renamed=BankService.RenamedCopy(target.Document,requested);if(renamed.Name==target.Document.Name)return;
            target.Undo.Push(target.Document.ToBytes());target.Redo.Clear();target.Document=renamed;RefreshLibrary();if(current==target)Render();return;
        }
        if(live==null)throw new InvalidOperationException(UiText.T("Conecta la GP-200 para seleccionar esta posición."));
        liveBusy=true;IsEnabled=false;
        try
        {
            var service=new BankService();var active=await service.ActiveAsync();var baseline=new PresetDocument(target.EditingBaseline??target.Original);
            var saved=await service.RenameAsync(target.DeviceSlot.Value,requested);var actual=saved;
            if(active.Index==target.DeviceSlot.Value)
            {
                var expected=BankService.RenamedCopy(active,saved.Name);var session=await LiveEditing.ConnectAsync();
                actual=PresetComparison.EquivalentFunctional(expected,session.Baseline,catalog)?session.Baseline:await session.ApplyAsync(expected);
                live.AcceptSnapshot(actual);
            }
            target.Original=saved.ToBytes();target.EditingBaseline=BankService.RenamedCopy(baseline,saved.Name).ToBytes();target.Document=actual;target.Undo.Clear();target.Redo.Clear();
            RefreshLibrary();if(current==target)Render();status.Text=UiText.T("Renombrado y verificado en GP-200.");
        }
        catch(Exception e){status.Text=e.Message;UiDialogs.Show(this,e.Message,UiText.T("Renombrar"));}
        finally{liveBusy=false;IsEnabled=true;}
    }
}
