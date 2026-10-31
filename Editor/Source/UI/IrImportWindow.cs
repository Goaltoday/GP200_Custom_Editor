using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
namespace Gp200.Replica;
public sealed class IrImportWindow:Window
{
    readonly Func<Task<IReadOnlyList<WireRecord>>> read;
    readonly Func<WireRecord,Task> send;
    readonly ComboBox target=new(){MinWidth=280};
    readonly TextBox name=new(){MaxLength=16,MinWidth=280};
    readonly TextBlock file=new(){Foreground=Brushes.WhiteSmoke,Text=UiText.T("Ningún WAV seleccionado"),TextWrapping=TextWrapping.Wrap};
    readonly TextBlock status=new(){Foreground=Brushes.WhiteSmoke,TextWrapping=TextWrapping.Wrap};
    readonly StackPanel panel=new(){Margin=new Thickness(16)};
    readonly Button load=new(){Content=UiText.T("Load"),Padding=new Thickness(12,4,12,4)};
    string? path;bool busy;
    public IrImportWindow(Func<Task<IReadOnlyList<WireRecord>>> read,Func<WireRecord,Task> send)
    {
        EditorWindow.StyleDialog(this);
        this.read=read;this.send=send;Background=new SolidColorBrush(Color.FromRgb(16,21,28));panel.Background=Background;Title=UiText.T("Load IR");Width=590;Height=460;ResizeMode=ResizeMode.NoResize;Content=panel;
        panel.Children.Add(new TextBlock{Foreground=Brushes.WhiteSmoke,Text=UiText.T("Load IR"),FontSize=18,Margin=new Thickness(0,0,0,18)});
        panel.Children.Add(new TextBlock{Foreground=Brushes.WhiteSmoke,Text=UiText.T("Archivo WAV")});panel.Children.Add(file);var browse=new Button{Content=UiText.T("Seleccionar WAV…"),Padding=new Thickness(12,6,12,6),Margin=new Thickness(0,8,12,12)};browse.Click+=(_,_)=>ChooseFile();
        var fileActions=new WrapPanel();fileActions.Children.Add(browse);var manage=new Button{Content=UiText.T("Gestionar slots…"),Padding=new Thickness(12,6,12,6),Margin=new Thickness(0,8,0,12)};manage.Click+=async(_,_)=>{new IrWindow(read,send){Owner=this}.ShowDialog();await Refresh();};fileActions.Children.Add(manage);panel.Children.Add(fileActions);
        panel.Children.Add(new TextBlock{Foreground=Brushes.WhiteSmoke,Text=UiText.T("IR NO.")});panel.Children.Add(target);panel.Children.Add(new TextBlock{Foreground=Brushes.WhiteSmoke,Text=UiText.T("IR Name · máximo 16 caracteres"),Margin=new Thickness(0,12,0,4)});panel.Children.Add(name);
        var buttons=new WrapPanel{Margin=new Thickness(0,18,0,12)};var cancel=new Button{Content=UiText.T("Cancelar"),Padding=new Thickness(10,4,10,4),Margin=new Thickness(0,0,12,0)};cancel.Click+=(_,_)=>Close();buttons.Children.Add(cancel);load.Click+=async(_,_)=>await Import();buttons.Children.Add(load);
        panel.Children.Add(buttons);panel.Children.Add(status);
        Loaded+=async(_,_)=>await Refresh();Closing+=(_,e)=>{if(busy)e.Cancel=true;};
    }
    async Task Refresh()
    {
        busy=true;load.IsEnabled=false;
        try{var slots=IrService.Catalog(await read());if(slots.Count!=20)throw new IOException(UiText.T("Catálogo IR incompleto."));target.ItemsSource=slots;target.SelectedItem=slots.FirstOrDefault(s=>s.State==1)??slots[0];status.Text=UiText.T("Selecciona un WAV y su posición de destino.");}
        catch(Exception e){status.Text=e.Message;}finally{busy=false;load.IsEnabled=target.Items.Count==20;}
    }
    void ChooseFile()
    {
        var dialog=new OpenFileDialog{Filter=UiText.Filter("Impulso WAV|*.wav"),CheckFileExists=true};if(dialog.ShowDialog(this)!=true)return;
        path=dialog.FileName;file.Text=Path.GetFileName(path);string label=Path.GetFileNameWithoutExtension(path);name.Text=IrService.CanonicalName(new string(label.Where(c=>c>=32&&c<=126).Take(16).ToArray()));
    }
    async Task Import()
    {
        if(busy||path==null||target.SelectedItem is not IrSlot slot){status.Text=UiText.T("Selecciona archivo, slot y nombre.");return;}
        try
        {
            name.Text=IrService.CanonicalName(name.Text);
            var prepared=IrService.ReadWav(path,slot.Index,name.Text,useLeftChannelForStereo24:true);
            if(slot.State!=1&&UiDialogs.Show(this,UiText.T($"Reemplazar USER IR {slot.Index+1} · {slot.Name}?"),UiText.T("Reemplazar IR"),MessageBoxButton.OKCancel)!=MessageBoxResult.OK)return;
            busy=true;panel.IsEnabled=false;status.Text=UiText.T("Importando IR…");await send(prepared.Record);
            var confirmed=await IrService.ConfirmImportAsync(read,slot.Index,name.Text);UpdateSlot(confirmed);
            status.Text=UiText.T($"Importación confirmada · USER IR {slot.Index+1} · {prepared.RetainedFrames} muestras.");UiDialogs.Show(this,UiText.T("IR importado correctamente."),UiText.T("Load IR"));
        }catch(Exception e){status.Text=e.Message;}finally{busy=false;panel.IsEnabled=true;}
    }
    void UpdateSlot(IrSlot actual){target.ItemsSource=target.Items.Cast<IrSlot>().Select(s=>s.Index==actual.Index?actual:s).ToArray();target.SelectedItem=actual;}
    internal void Preview(string capturePath,string wav,string output)
    {
        using var capture=System.Text.Json.JsonDocument.Parse(File.ReadAllText(capturePath));
        var slots=IrService.Catalog(capture.RootElement.GetProperty("ir").EnumerateArray().Select(r=>new WireRecord(r.GetProperty("Type").GetUInt16(),Convert.FromBase64String(r.GetProperty("Value").GetString()!))));
        target.ItemsSource=slots;target.SelectedItem=slots.First(s=>s.State==1);path=wav;file.Text=Path.GetFileName(wav);name.Text=new string(Path.GetFileNameWithoutExtension(wav).Take(16).ToArray());
        _=IrService.ReadWav(wav,((IrSlot)target.SelectedItem).Index,name.Text);var root=(FrameworkElement)Content;root.Measure(new Size(550,420));root.Arrange(new Rect(0,0,550,420));root.UpdateLayout();
        var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap(550,420,96,96,System.Windows.Media.PixelFormats.Pbgra32);bitmap.Render(root);var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using var stream=new FileStream(output,FileMode.CreateNew);encoder.Save(stream);
    }
}
