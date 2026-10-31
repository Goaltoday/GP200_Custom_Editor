using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Gp200.Replica;

/// <summary>Transport is supplied by the application, and must share its MIDI gate.</summary>
public sealed class IrWindow:Window
{
    readonly Func<Task<IReadOnlyList<WireRecord>>> readState;
    readonly Func<WireRecord,Task> send;
    readonly ListBox list=new(){MinWidth=280,Margin=new Thickness(0,0,16,0)};
    readonly TextBox name=new(){MaxLength=16,MinWidth=230,Margin=new Thickness(0,6,0,14)};
    readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,18,0,0)};
    readonly StackPanel actions=new();
    bool busy;
    public IrWindow(Func<Task<IReadOnlyList<WireRecord>>> readState,Func<WireRecord,Task> send)
    {
        EditorWindow.StyleDialog(this);
        this.readState=readState;this.send=send;Title=UiText.T("GP-200 · Banco User IR");Width=760;Height=520;MinWidth=650;MinHeight=400;
        var grid=new Grid{Margin=new Thickness(20)};grid.ColumnDefinitions.Add(new(){Width=new GridLength(300)});grid.ColumnDefinitions.Add(new());grid.Children.Add(list);Grid.SetColumn(actions,1);grid.Children.Add(actions);Content=grid;
        actions.Children.Add(new TextBlock{Text=UiText.T("20 slots User IR · 1024 muestras"),FontSize=16});
        Add(UiText.T("Leer banco IR"),Refresh);actions.Children.Add(new TextBlock{Text=UiText.T("Nombre (ASCII, máximo 16)"),Margin=new Thickness(0,15,0,0)});actions.Children.Add(name);
        Add(UiText.T("Renombrar slot"),Rename);Add(UiText.T("Importar WAV al slot…"),Import);Add(UiText.T("Eliminar contenido del slot…"),Delete);
        actions.Children.Add(new TextBlock{Text=UiText.T("WAV mono o estéreo · PCM16/24/32 o float32. En estéreo se usa el canal izquierdo. Remuestreo a 44100 Hz y recorte a 1024 muestras. Las operaciones escriben el banco IR del equipo."),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,0)});actions.Children.Add(status);
        list.SelectionChanged+=(_,_)=>{if(list.SelectedItem is IrSlot slot)name.Text=slot.Name;};
        Closing+=(_,e)=>{if(busy)e.Cancel=true;};
    }
    void Add(string title,Func<Task> action)
    {
        var button=new Button{Content=title,Padding=new Thickness(12,8,12,8),Margin=new Thickness(0,8,0,0),HorizontalAlignment=HorizontalAlignment.Left};
        button.Click+=async(_,_)=>{if(busy)return;busy=true;actions.IsEnabled=false;list.IsEnabled=false;try{await action();}catch(Exception e){status.Text=e.Message;}finally{busy=false;actions.IsEnabled=true;list.IsEnabled=true;}};actions.Children.Add(button);
    }
    IrSlot Selected()=>list.SelectedItem as IrSlot??throw new InvalidOperationException(UiText.T("Lee el banco y selecciona un slot IR."));
    async Task Refresh()
    {
        int index=(list.SelectedItem as IrSlot)?.Index??0;var slots=IrService.Catalog(await readState());list.ItemsSource=slots;list.SelectedItem=slots.FirstOrDefault(s=>s.Index==index);
        status.Text=UiText.T($"Recibidos {slots.Count}/20 slots. Estado leído del dispositivo.");
        if(slots.Count!=20)throw new InvalidDataException(UiText.T("Catálogo IR incompleto: no se habilita la selección de slots ausentes."));
    }
    async Task Rename()
    {
        var slot=Selected();string requested=IrService.CanonicalName(name.Text);name.Text=requested;var record=IrService.Rename(slot.Index,requested);await send(record);await Refresh();
        if(list.SelectedItem is not IrSlot actual||IrService.CanonicalName(actual.Name)!=requested)throw new IOException(UiText.T("El nombre leído no coincide; vuelve a consultar el banco IR."));
        status.Text=UiText.T("Nombre verificado mediante lectura del banco IR.");
    }
    async Task Delete()
    {
        var slot=Selected();if(UiDialogs.Show(this,UiText.T($"Eliminar «{slot.Name}» de USER IR {slot.Index+1}?"),UiText.T("Eliminar IR"),MessageBoxButton.OKCancel)!=MessageBoxResult.OK)return;
        await send(IrService.Delete(slot.Index));await Refresh();
        if(list.SelectedItem is not IrSlot actual||actual.State!=1)throw new IOException(UiText.T("El catálogo no confirma que el slot IR esté vacío."));
        status.Text=UiText.T("Slot IR vacío confirmado mediante lectura del catálogo.");
    }
    async Task Import()
    {
        var slot=Selected();var dialog=new OpenFileDialog{Filter=UiText.Filter("Impulso WAV|*.wav"),CheckFileExists=true};if(dialog.ShowDialog(this)!=true)return;
        // Explicit product policy shown above and in the overwrite dialog:
        // select left rather than reproduce native PCM24 stereo interleaving.
        name.Text=IrService.CanonicalName(name.Text);var import=IrService.ReadWav(dialog.FileName,slot.Index,name.Text,useLeftChannelForStereo24:true);
        if(UiDialogs.Show(this,UiText.T($"Importar «{name.Text}» en USER IR {slot.Index+1}?\nSe conservarán {import.RetainedFrames} muestras y se descartarán {import.DiscardedFrames}.\n{(import.SourceChannels==2?UiText.T("Se usará el canal izquierdo.\n"):"")}Se sobrescribirá el contenido del slot."),UiText.T("Importar IR"),MessageBoxButton.OKCancel)!=MessageBoxResult.OK)return;
        string expected=name.Text;await send(import.Record);_=await IrService.ConfirmImportAsync(readState,slot.Index,expected);await Refresh();
        status.Text=UiText.T("El catálogo refleja el IR importado. La comprobación de audio requiere escuchar el equipo; no se dispone de lectura de las muestras IR.");
    }
}
