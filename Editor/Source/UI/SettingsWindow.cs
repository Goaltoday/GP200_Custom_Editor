using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Gp200.Replica;

/// <summary>Explicit, verified writes for corroborated GP-200 settings; no reset or firmware operation.</summary>
public sealed partial class SettingsWindow:Window
{
    readonly TabControl tabs=new(){TabStripPlacement=Dock.Left};
    int editingTemplate;
    public event Action? PatchSettingsRequested;
    readonly TextBlock status=new(){Text=UiText.T("Pulsa Leer GP-200 para cargar los ajustes actuales."),TextWrapping=TextWrapping.Wrap,Margin=new(12)};
    readonly Button read=new(){Content=UiText.T("Leer GP-200"),Margin=new(8),Padding=new(8,4,8,4),HorizontalAlignment=HorizontalAlignment.Left};
    DeviceSettings? state;bool busy;
    public SettingsWindow()
    {
        EditorWindow.StyleDialog(this);
        Title=UiText.T("GP200 · Ajustes, EQ, footswitch y ritmos");Width=940;Height=680;MinWidth=820;MinHeight=550;Background=new SolidColorBrush(Color.FromRgb(16,21,28));Foreground=Brushes.WhiteSmoke;
        tabs.Background=Background;tabs.Foreground=Foreground;
        var layout=new DockPanel();DockPanel.SetDock(read,Dock.Top);layout.Children.Add(read);DockPanel.SetDock(status,Dock.Bottom);layout.Children.Add(status);layout.Children.Add(tabs);Content=layout;
        AddProfileAndLanguageTabs();
        read.Click+=async(_,_)=>await Work(async()=>{await ReadSettings();Render();return UiText.T("Estado leído; los cambios se envían al aplicar cada control.");});
        Loaded+=async(_,_)=>await Work(async()=>{await ReadSettings();Render();return UiText.T("Ajustes leídos de GP-200.");});
        Activated+=async(_,_)=>{if(state!=null&&!busy)await Work(async()=>{await ReadSettings();Render();return UiText.T("Ajustes leídos de GP-200.");});};
        Closing+=(_,e)=>{if(busy){e.Cancel=true;status.Text=UiText.T("Espera a que termine la lectura o verificación en curso.");}};
    }
    async Task Work(Func<Task<string>> action)
    {
        if(busy)return;busy=true;read.IsEnabled=false;tabs.IsEnabled=false;status.Text=UiText.T("Comunicando con GP-200…");
        try{status.Text=UiText.T(await action());}catch(Exception ex){status.Text=ex.Message;state=null;tabs.Items.Clear();AddProfileAndLanguageTabs();}
        finally{busy=false;read.IsEnabled=true;tabs.IsEnabled=true;}
    }
    Task Apply(DeviceSettingChange change)=>Work(async()=>{state=await DeviceSettings.ApplyAsync(change);Render();return UiText.T("Verificado: ")+change.Description;});
    StackPanel Tab(string title)
    {
        var panel=new StackPanel{Margin=new(10)};tabs.Items.Add(new TabItem{Header=UiText.T(title),Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}});return panel;
    }
    void Render()
    {
        int selected=tabs.SelectedIndex;tabs.Items.Clear();if(state==null)return;
        var general=Tab(UiText.T("General"));
        Heading(general,UiText.T("Input / Output Settings"));Globals(general,[2,1,3,4]);
        Heading(general,UiText.T("USB Audio Settings"));Globals(general,[5,6,7,8,9]);
        Heading(general,UiText.T("Auto Cab Match"));Globals(general,[36]);Heading(general,UiText.T("Device Display"));Globals(general,[40]);
        BuildFootswitchPanel();
        var external=Tab(UiText.T("EXP / Footswitch"));Globals(external,[12]);
        var patch=new Button{Content=UiText.T("Setting › EXP del preset"),Padding=new(8,4,8,4),Margin=new(0,12,0,12),HorizontalAlignment=HorizontalAlignment.Left};patch.Click+=(_,_)=>{PatchSettingsRequested?.Invoke();Close();};external.Children.Add(patch);
        if(state.Global(12)>0){Globals(external,[31]);if(state.Global(12)>1)Globals(external,[33]);}
        var eq=Tab(UiText.T("Global EQ"));Globals(eq,[30]);
        var graph=new EqGraph(state);graph.Committed+=async changes=>await Work(async()=>
        {
            foreach(var change in changes)state=await DeviceSettings.ApplyAsync(change);
            Render();return UiText.T("EQ verificado: frecuencia y ganancia actualizadas.");
        });eq.Children.Add(graph);
        eq.Children.Add(new TextBlock{Text=UiText.T("Curva orientativa · arrastra los puntos; Q y Master Volume se ajustan debajo."),Foreground=Brushes.LightGray,TextWrapping=TextWrapping.Wrap});
        foreach(var item in DeviceSettings.Equalizer)Number(eq,item.Name,state.Eq(item.Selector),item.Minimum,item.Maximum,v=>DeviceSettings.SetEq(item.Selector,v));
        var midiSettings=Tab(UiText.T("MIDI"));Heading(midiSettings,UiText.T("Entrada MIDI"));Globals(midiSettings,[14,15,16]);
        Heading(midiSettings,UiText.T("Salida MIDI"));Globals(midiSettings,[17,18]);
        Heading(midiSettings,UiText.T("Reloj MIDI"));Globals(midiSettings,[19,20,21]);
        midiSettings.Children.Add(new TextBlock{Foreground=Brushes.LightGray,Text=UiText.T("Omni recibe en todos los canales. DIN Only, USB Only y External usan reloj externo; en esos modos TAP Tempo no controla el tempo. Clock Out habilita la salida de reloj del equipo."),TextWrapping=TextWrapping.Wrap,Margin=new(0,15,0,0)});
        var about=Tab(UiText.T("About"));Heading(about,UiText.T("GP200 Replica"));about.Children.Add(new TextBlock{Foreground=Brushes.WhiteSmoke,Text=UiText.T("Editor independiente · interfaz basada en los flujos del editor oficial."),TextWrapping=TextWrapping.Wrap});
        var help=Tab(UiText.T("Help"));var support=new Button{Content=UiText.T("Manual y preferencias…"),Padding=new(8,4,8,4),HorizontalAlignment=HorizontalAlignment.Left};support.Click+=(_,_)=>new HelpWindow{Owner=this}.ShowDialog();help.Children.Add(support);
        var notes=Tab(UiText.T("Release Notes"));notes.Children.Add(new TextBlock{Foreground=Brushes.WhiteSmoke,Text=UiText.T("Distribución integrada de presets y cadena. Patch Setting con CTRL, EXP, Quick Access y FX Loop. Settings organizado por función y carga de IR con selección de slot. Firmware y factory reset excluidos."),TextWrapping=TextWrapping.Wrap});
        var drum=Tab(UiText.T("Ritmos"));Choice(drum,UiText.T("Patrón"),DeviceSettings.DrumPatterns,state.Drum(0),0,v=>DeviceSettings.SetDrum(0,v));Number(drum,UiText.T("Volumen"),state.Drum(1),0,100,v=>DeviceSettings.SetDrum(1,(int)v),true);Number(drum,UiText.T("Tempo BPM"),state.Drum(2),40,250,v=>DeviceSettings.SetDrum(2,(int)v),true);
        var play=new Button{Content=state.DrumPlaying?UiText.T("Parar ritmo"):UiText.T("Reproducir ritmo"),Padding=new(8,4,8,4),HorizontalAlignment=HorizontalAlignment.Left,Margin=new(0,12,0,0)};play.Click+=async(_,_)=>await Apply(DeviceSettings.SetDrumPlaying(!state!.DrumPlaying));drum.Children.Add(play);
        drum.Children.Add(new TextBlock{Foreground=Brushes.WhiteSmoke,Text=UiText.T("Afinador y looper: asignables a footswitch. Una pantalla de control equivalente en el editor oficial aún no está acreditada."),TextWrapping=TextWrapping.Wrap,Margin=new(0,18,0,0)});
        var midi=Tab(UiText.T("Lista MIDI"));midi.Children.Add(new TextBlock{Foreground=Brushes.WhiteSmoke,Text=UiText.T("Referencia del manual suministrado, páginas PDF 76–78. Consulta; no envía mensajes MIDI."),TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,12)});
        var rows=System.Text.Json.JsonSerializer.Deserialize<List<MidiControlReference>>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Data","midi-control-list.json")))??[];
        var table=new DataGrid{ItemsSource=rows.Select(r=>r with{Meaning=UiText.T(r.Meaning)}).ToArray(),IsReadOnly=true,AutoGenerateColumns=false,CanUserAddRows=false,HeadersVisibility=DataGridHeadersVisibility.Column,MinHeight=350};
        table.Columns.Add(new DataGridTextColumn{Header=UiText.T("CC"),Binding=new System.Windows.Data.Binding("Controller"),Width=50});table.Columns.Add(new DataGridTextColumn{Header=UiText.T("Rango"),Binding=new System.Windows.Data.Binding("Range"),Width=70});table.Columns.Add(new DataGridTextColumn{Header=UiText.T("Función"),Binding=new System.Windows.Data.Binding("Meaning"),Width=new DataGridLength(1,DataGridLengthUnitType.Star)});midi.Children.Add(table);
        AddProfileAndLanguageTabs();
        tabs.SelectedIndex=Math.Clamp(selected,0,tabs.Items.Count-1);
    }
    void AddProfileAndLanguageTabs()
    {
        var profile=Tab("Device profile");profile.Children.Add(new DeviceProfilePanel());
        var languagePanel=Tab("Language");
        var language=new ComboBox{ItemsSource=new[]{"English","Español"},SelectedIndex=UiText.Language=="en"?0:1,Width=180,HorizontalAlignment=HorizontalAlignment.Left};
        languagePanel.Children.Add(language);
        languagePanel.Children.Add(new TextBlock{Text=UiText.T("Idioma guardado. Reinicia la aplicación para aplicarlo."),Margin=new Thickness(0,12,0,0),TextWrapping=TextWrapping.Wrap});
        language.SelectionChanged+=(_,_)=>{var preferences=AppPreferences.Load();(preferences with{Language=language.SelectedIndex==0?"en":"es"}).Save();};
    }
    void Heading(StackPanel panel,string title)=>panel.Children.Add(new TextBlock{Foreground=Brushes.WhiteSmoke,Text=UiText.T(title),FontSize=14,FontWeight=FontWeights.SemiBold,Margin=new(0,10,0,6)});
    void Globals(StackPanel panel,int[] selectors)
    {
        foreach(int selector in selectors){var item=DeviceSettings.Globals.Single(g=>g.Selector==selector);int value=state!.Global(selector);
            if(item.Choices!=null)Choice(panel,item.Name,item.Choices,value,item.Minimum,i=>DeviceSettings.SetGlobal(selector,i));
            else Number(panel,item.Name,value,item.Minimum,item.Maximum,v=>DeviceSettings.SetGlobal(selector,checked((int)v)),true);}
    }
    internal int PreviewPages(string capturePath,string folder)
    {
        using var capture=System.Text.Json.JsonDocument.Parse(File.ReadAllText(capturePath));
        var response=capture.RootElement.GetProperty("responses").EnumerateArray().First(r=>r.GetProperty("Records").EnumerateArray().Any(t=>t.GetProperty("Type").GetInt32()==0x1007));
        state=new DeviceSettings(response.GetProperty("Records").EnumerateArray().Select(r=>new WireRecord(r.GetProperty("Type").GetUInt16(),Convert.FromBase64String(r.GetProperty("Value").GetString()!))).ToArray());Render();Directory.CreateDirectory(folder);
        var root=(FrameworkElement)Content;int count=tabs.Items.Count;
        for(int i=0;i<count;i++)
        {
            tabs.SelectedIndex=i;root.Measure(new Size(900,620));root.Arrange(new Rect(0,0,900,620));root.UpdateLayout();
            var image=new System.Windows.Media.Imaging.RenderTargetBitmap(900,620,96,96,PixelFormats.Pbgra32);image.Render(root);var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));using var file=File.Create(Path.Combine(folder,$"settings-{i}.png"));encoder.Save(file);
        }
        return count;
    }
    DockPanel Row(StackPanel parent,string title)
    {
        var row=new DockPanel{Margin=new(0,4,0,4)};var label=new TextBlock{Foreground=Brushes.WhiteSmoke,Text=UiText.T(title),Width=185,VerticalAlignment=VerticalAlignment.Center};DockPanel.SetDock(label,Dock.Left);row.Children.Add(label);parent.Children.Add(row);return row;
    }
    void Choice(StackPanel parent,string title,string[] options,int current,int minimum,Func<int,DeviceSettingChange> make,int labelWidth=185)
    {
        var row=Row(parent,title);((TextBlock)row.Children[0]).Width=labelWidth;var choice=new ComboBox{ItemsSource=options.Select(UiText.T).ToArray(),SelectedIndex=current-minimum,MinWidth=100,MaxWidth=300,HorizontalAlignment=HorizontalAlignment.Left,Width=labelWidth<80?110:250};row.Children.Add(choice);
        if(current-minimum<0||current-minimum>=options.Length){choice.IsEnabled=false;choice.ToolTip="Custom/unknown value preserved: "+current;row.Children.Add(new TextBlock{Text="  "+current,VerticalAlignment=VerticalAlignment.Center});}
        choice.SelectionChanged+=async(_,_)=>{if(choice.SelectedIndex<0)return;try{await Apply(make(choice.SelectedIndex+minimum));}catch(Exception ex){status.Text=ex.Message;}};
    }
    void Number(StackPanel parent,string title,float current,float min,float max,Func<float,DeviceSettingChange> make,bool integer=false)
    {
        var row=Row(parent,title);var group=new StackPanel();row.Children.Add(group);
        var input=new TextBox{Text=current.ToString("G9",CultureInfo.CurrentCulture),Width=130,HorizontalAlignment=HorizontalAlignment.Left,ToolTip=UiText.T($"{min} … {max}")};
        var slider=new Slider{Minimum=min,Maximum=max,Value=Math.Clamp(current,min,max),Width=260,HorizontalAlignment=HorizontalAlignment.Left,TickFrequency=integer?1:.1,IsSnapToTickEnabled=integer};group.Children.Add(slider);group.Children.Add(input);bool changed=false;
        input.TextChanged+=(_,_)=>changed=true;
        async Task Commit()
        {
            if(!changed||busy)return;changed=false;
            if(!float.TryParse(input.Text.Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out float value)||!float.IsFinite(value)||value<min||value>max||(integer&&value!=MathF.Truncate(value))){status.Text=UiText.T($"Introduce un valor entre {min} y {max}.");return;}
            await Apply(make(value));
        }
        input.KeyDown+=async(_,e)=>{if(e.Key==System.Windows.Input.Key.Enter){e.Handled=true;await Commit();}};input.LostKeyboardFocus+=async(_,_)=>await Commit();
        slider.ValueChanged+=(_,e)=>input.Text=((float)e.NewValue).ToString("G9",CultureInfo.CurrentCulture);
        slider.PreviewMouseLeftButtonUp+=async(_,_)=>await Commit();slider.KeyUp+=async(_,_)=>await Commit();

    }
}
public sealed record MidiControlReference(int Controller,string Range,string Meaning);


