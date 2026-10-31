
using System.Windows;

using System.Windows.Controls;

using Microsoft.Win32;

namespace Gp200.Replica;

public sealed class Mx42NamWindow:Window

{

    readonly TextBox file=new(){IsReadOnly=true},name=new(){MaxLength=16};readonly ComboBox destination=new(){MinWidth=300};

    readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,0)};

    readonly List<Button> buttons=[];Button? load;bool busy;

    public Mx42NamWindow(bool readOnOpen=true)

    {

        EditorWindow.StyleDialog(this);Title="Load NAM";Width=600;SizeToContent=SizeToContent.Height;MaxHeight=690;WindowStartupLocation=WindowStartupLocation.CenterOwner;

        var panel=new StackPanel{Margin=new Thickness(16)};Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};

        panel.Children.Add(new TextBlock{Text="Load NAM · 32 slots",FontSize=18,Margin=new Thickness(0,0,0,8)});

        panel.Children.Add(new TextBlock{Text=T("Native A2 Lite · 48 kHz. Original Input/Output controls and audio engine are retained. No CLO conversion.","A2 Lite nativo · 48 kHz. Conserva Input/Output y el motor de audio original. Sin conversión CLO."),TextWrapping=TextWrapping.Wrap});

        var row=new DockPanel{Margin=new Thickness(0,12,0,10)};var browse=Add(T("Browse…","Examinar…"),()=>{var dialog=new OpenFileDialog{Filter="Neural Amp Modeler|*.nam",CheckFileExists=true};if(dialog.ShowDialog(this)==true){file.Text=dialog.FileName;name.Text=new string(Path.GetFileNameWithoutExtension(file.Text).Select(c=>char.IsAsciiLetterOrDigit(c)||c is ' ' or '_' or '-'?c:'_').Take(16).ToArray()).Trim();}return Task.CompletedTask;});DockPanel.SetDock(browse,Dock.Right);row.Children.Add(browse);row.Children.Add(file);panel.Children.Add(row);

        panel.Children.Add(new TextBlock{Text=T("Destination NAM slot","Posición NAM destino")});panel.Children.Add(destination);destination.SelectionChanged+=(_,_)=>{UpdateLoad();ShowAvailability();};

        panel.Children.Add(new TextBlock{Text=T("Name · 1–16 letters, numbers, spaces, _ or -","Nombre · 1–16 letras, números, espacios, _ o -"),Margin=new Thickness(0,10,0,4)});panel.Children.Add(name);

        var actions=new WrapPanel{Margin=new Thickness(0,12,0,0)};



        load=Add("Load",async()=>

        {

            if(destination.SelectedItem is not Mx42NamSlot slot)throw new InvalidOperationException(T("Read and choose a destination.","Lee y elige un destino."));

            if(!slot.CanLoad)throw new InvalidOperationException(T("The destination is in use or busy.","El destino está en uso u ocupado."));

            _=A2LiteModel.ReadWeights(file.Text);Mx42NamCodec.Name(name.Text);

            if(UiDialogs.Show(this,T($"Replace NAM {slot.Index+1:00} · {slot.Name}? Every preset using this global slot will use the new model. A recovery backup of the previous slot will be retained. No preset will be selected or saved.",$"¿Sustituir NAM {slot.Index+1:00} · {slot.Name}? Los presets que usen esta posición global usarán el nuevo modelo. Se conservará una copia de recuperación del registro anterior. No se seleccionará ni guardará ningún preset."),"Load NAM",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;

            await Mx42NamService.Upload(file.Text,slot.Index,name.Text,new Progress<string>(x=>status.Text=x));await Read();status.Text=T("NAM loaded and full physical readback verified. No preset was selected or saved.","NAM cargado y lectura física íntegra verificada. No se seleccionó ni guardó ningún preset.");

        });actions.Children.Add(load);


        panel.Children.Add(actions);panel.Children.Add(status);
        panel.Children.Add(new TextBlock{Text=T("Maximum one selected NAM block per preset, including bypassed blocks. Choose among 32 stored models. A selected model cannot be replaced.","Máximo un bloque NAM seleccionado por preset, también apagado. Puedes elegir entre 32 modelos almacenados. No se puede reemplazar el modelo seleccionado."),TextWrapping=TextWrapping.Wrap,FontSize=11,Margin=new Thickness(0,10,0,0)});

        destination.ItemsSource=Enumerable.Range(0,32).Select(i=>new Mx42NamSlot(i,CatalogRuntime.Current.Mx42NamLabels[i].Split('·',2).Last().Trim(),0,7,false)).ToArray();destination.SelectedIndex=0;UpdateLoad();

        if(!Mx42Integration.Supports(Mx42Capability.NamUpload))status.Text=T("This firmware has no direct NAM32 loader. Installed names are read from the device. NH04 is not used; install the matching firmware extension to load models.","Este firmware no tiene cargador NAM32 directo. Los nombres se leen del equipo. No se usa NH04; instala la ampliación de firmware correspondiente para cargar modelos.");else ShowAvailability();
        file.TextChanged+=(_,_)=>UpdateLoad();name.TextChanged+=(_,_)=>UpdateLoad();
        if(readOnOpen)Loaded+=async(_,_)=>await RunBusy(Read);

        Closing+=(_,e)=>{if(busy)e.Cancel=true;};

    }

    async Task Read()

    {

        int selected=(destination.SelectedItem as Mx42NamSlot)?.Index??0;

        if(!Mx42Integration.Supports(Mx42Capability.NamUpload)){await Mx42Integration.ReadNamNames(CatalogRuntime.Current);destination.ItemsSource=Enumerable.Range(0,32).Select(i=>new Mx42NamSlot(i,CatalogRuntime.Current.Mx42NamLabels[i].Split('·',2).Last().Trim(),0,7,false)).ToArray();destination.SelectedIndex=selected;status.Text=T("Names refreshed. Direct loading is unavailable on this firmware.","Nombres actualizados. La carga directa no está disponible en este firmware.");return;}

        destination.ItemsSource=await Mx42NamService.Slots();destination.SelectedIndex=selected;UpdateLoad();ShowAvailability();

    }

    void UpdateLoad(){if(load!=null)load.IsEnabled=!busy&&Mx42Integration.Supports(Mx42Capability.NamUpload|Mx42Capability.NamRecovery)&&destination.SelectedItem is Mx42NamSlot slot&&slot.CanLoad&&File.Exists(file.Text)&&name.Text.Trim().Length>0;}
    void ShowAvailability()
    {
        if(!Mx42Integration.Supports(Mx42Capability.NamUpload))status.Text=T("Installed firmware has no native NAM USB loader.","El firmware instalado no tiene cargador NAM USB nativo.");
        else if(!Mx42Integration.Supports(Mx42Capability.NamRecovery))status.Text=T("Install the matching R2 firmware with boot-recovery protection to enable Load. Slot status can still be read.","Instala el firmware R2 correspondiente con protección de recuperación al arrancar para habilitar Load. Puedes consultar el estado de las posiciones.");
        else if(destination.SelectedItem is Mx42NamSlot slot)status.Text=slot.CanLoad?T("Destination available. Choose a NAM file and name to load.","Destino disponible. Elige archivo NAM y nombre para cargar."):T("Cannot load this destination: ","No se puede cargar este destino: ")+slot.Reason;
    }
    async Task RunBusy(Func<Task> action)
    {
        if(busy)return;busy=true;foreach(var b in buttons)b.IsEnabled=false;file.IsEnabled=name.IsEnabled=destination.IsEnabled=false;
        try{await action();}catch(Exception error){status.Text=error.Message;}
        finally{busy=false;foreach(var b in buttons)b.IsEnabled=true;file.IsEnabled=name.IsEnabled=destination.IsEnabled=true;UpdateLoad();}
    }

    static string T(string en,string es)=>UiText.Language=="es"?es:en;

    Button Add(string text,Func<Task> action)

    {

        var button=new Button{Content=text,Margin=new Thickness(0,0,6,4)};buttons.Add(button);button.Click+=async(_,_)=>

        {await RunBusy(action);};return button;

    }

}

