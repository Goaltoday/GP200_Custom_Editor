using System.Windows;
using System.Windows.Controls;

namespace Gp200.Replica;

internal sealed class MidiPortsPanel:StackPanel
{
    readonly ComboBox input=new(){MinWidth=300,HorizontalAlignment=HorizontalAlignment.Left};
    readonly ComboBox output=new(){MinWidth=300,HorizontalAlignment=HorizontalAlignment.Left};
    readonly TextBlock message=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,0)};
    static string L(string en,string es)=>UiText.Language=="es"?es:en;
    sealed record PortChoice(string Name,string Label){public override string ToString()=>Label;}
    public MidiPortsPanel()
    {
        Children.Add(new TextBlock{Text=L("MIDI ports","Puertos MIDI"),FontSize=16,Margin=new Thickness(0,0,0,12)});
        Children.Add(new TextBlock{Text=L("Input","Entrada")});Children.Add(input);
        Children.Add(new TextBlock{Text=L("Output","Salida"),Margin=new Thickness(0,12,0,0)});Children.Add(output);
        var buttons=new WrapPanel{Margin=new Thickness(0,12,0,0)};Children.Add(buttons);
        var refresh=new Button{Content=L("Refresh ports","Actualizar puertos"),Margin=new Thickness(0,0,8,0)};
        var save=new Button{Content=L("Save selection","Guardar selección")};buttons.Children.Add(refresh);buttons.Children.Add(save);Children.Add(message);
        refresh.Click+=(_,_)=>Refresh();
        save.Click+=(_,_)=>
        {
            try
            {
                if(input.SelectedItem is not PortChoice i||output.SelectedItem is not PortChoice o)throw new InvalidOperationException(L("Select input and output.","Selecciona entrada y salida."));
                if(i.Name.Length>0)ReadOnlyMidi.SelectConfiguredPort(ReadOnlyMidi.InputPorts(),i.Name,"input");
                if(o.Name.Length>0)ReadOnlyMidi.SelectConfiguredPort(ReadOnlyMidi.OutputPorts(),o.Name,"output");
                (AppPreferences.Load() with{MidiInputName=i.Name,MidiOutputName=o.Name}).Save();
                message.Text=L("Saved. Restart the editor to apply the MIDI ports.","Guardado. Reinicia el editor para aplicar los puertos MIDI.");
            }
            catch(Exception e){message.Text=e.Message;}
        };
        Refresh();
    }
    void Refresh()
    {
        try
        {
            var preferences=AppPreferences.Load();
            Fill(input,ReadOnlyMidi.InputPorts(),(input.SelectedItem as PortChoice)?.Name??preferences.MidiInputName);
            Fill(output,ReadOnlyMidi.OutputPorts(),(output.SelectedItem as PortChoice)?.Name??preferences.MidiOutputName);
            message.Text=L("Automatic detects the main GP-200 port. Manual selection allows other names. Select both ports belonging to your GP-200; no MIDI messages are sent from this menu.","Automático detecta el puerto principal GP-200. La selección manual admite otros nombres. Elige los dos puertos de tu GP-200; este menú no envía mensajes MIDI.");
        }
        catch(Exception e){message.Text=e.Message;}
    }
    static void Fill(ComboBox combo,IReadOnlyList<(uint Id,string Name)> ports,string selected)
    {
        var choices=new List<PortChoice>{new("",L("Automatic · GP-200","Automático · GP-200"))};
        choices.AddRange(ports.Where(p=>!string.IsNullOrEmpty(p.Name)).Select(p=>new PortChoice(p.Name,$"{p.Id}: {p.Name}")));
        if(selected.Length>0&&!choices.Any(c=>c.Name==selected))choices.Add(new(selected,selected+L(" · unavailable"," · no disponible")));
        combo.ItemsSource=choices;combo.SelectedItem=choices.First(c=>c.Name==selected);
    }
}
