using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
namespace Gp200.Replica;
public sealed class SnapToneImportWindow:Window
{
    public event Action<IReadOnlyList<SnapToneSlot>>? CatalogRead;
    readonly StackPanel panel=new(){Margin=new Thickness(16)};
    readonly TextBox source=new(){IsReadOnly=true},ir=new(){IsReadOnly=true},reference=new(){IsReadOnly=true},library=new(){IsReadOnly=true},name=new(){MaxLength=16};
    readonly CheckBox useIr=new(){Content=UiText.T("Corrective IR (optional)")},useReference=new(){Content=UiText.T("Tone Match reference WAV (optional)")};
    readonly ComboBox target=new(){MinWidth=280};
    readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,15,0,0)};
    readonly ProgressBar bar=new(){Height=4,Margin=new Thickness(0,12,0,0),Visibility=Visibility.Collapsed};
    readonly Button cancel=new(){Content=UiText.T("Cancel conversion"),IsEnabled=false};
    readonly List<Button> actions=[];
    CancellationTokenSource? cancellation;bool busy,uploading;SnapToneJob? job;
    public SnapToneImportWindow(bool readDevice=true)
    {
        EditorWindow.StyleDialog(this);Title="Load SnapTone";Width=680;SizeToContent=SizeToContent.Height;MaxHeight=900;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        panel.Children.Add(new TextBlock{Text="Load SnapTone",FontSize=18,Margin=new Thickness(0,0,0,14)});
        Row("NAM / CLO",source,"Browse…",()=>{var path=Choose("NAM / CLO|*.nam;*.clo");if(path==null)return;source.Text=path;job=null;name.Text=new string(Path.GetFileNameWithoutExtension(path).Where(c=>c>=32&&c<=126).Take(16).ToArray());bool nam=Path.GetExtension(path).Equals(".nam",StringComparison.OrdinalIgnoreCase);useIr.IsEnabled=useReference.IsEnabled=nam;});
        panel.Children.Add(new TextBlock{Text=UiText.T("Tone Match is always enabled for NAM conversion."),Margin=new Thickness(0,8,0,8)});
        panel.Children.Add(useIr);Row("WAV",ir,"Browse…",()=>{var p=Choose("WAV|*.wav");if(p!=null){ir.Text=p;useIr.IsChecked=true;job=null;}});
        panel.Children.Add(useReference);Row("WAV",reference,"Browse…",()=>{var p=Choose("WAV|*.wav");if(p!=null){reference.Text=p;useReference.IsChecked=true;job=null;}});
        useIr.Checked+=(_,_)=>job=null;useIr.Unchecked+=(_,_)=>job=null;useReference.Checked+=(_,_)=>job=null;useReference.Unchecked+=(_,_)=>job=null;
        library.Text=SnapToneLibrary.DefaultRoot;Row("Library folder",library,"Choose…",()=>{var dialog=new OpenFolderDialog{InitialDirectory=Directory.Exists(library.Text)?library.Text:""};if(dialog.ShowDialog(this)==true){library.Text=dialog.FolderName;var p=AppPreferences.Load();(p with{SnapToneLibraryPath=library.Text}).Save();job=null;}});
        panel.Children.Add(new TextBlock{Text=UiText.T("Original NAM, generated CLO, WAV options and logs are kept in the library."),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,12)});
        var slots=new DockPanel();var refresh=Action("Read slots",async()=>await Refresh());DockPanel.SetDock(refresh,Dock.Right);slots.Children.Add(refresh);slots.Children.Add(target);panel.Children.Add(slots);
        panel.Children.Add(new TextBlock{Text=UiText.T("Name (1–16 ASCII characters)"),Margin=new Thickness(0,10,0,4)});panel.Children.Add(name);
        var buttons=new WrapPanel{Margin=new Thickness(0,15,0,0)};buttons.Children.Add(Action("Convert only",async()=>await Run(false)));buttons.Children.Add(Action("Load",async()=>await Run(true)));buttons.Children.Add(Action("Open library",()=>{Directory.CreateDirectory(library.Text);Process.Start(new ProcessStartInfo(library.Text){UseShellExecute=true});return Task.CompletedTask;}));
        cancel.Click+=(_,_)=>cancellation?.Cancel();buttons.Children.Add(cancel);panel.Children.Add(buttons);panel.Children.Add(bar);panel.Children.Add(status);
        if(readDevice)Loaded+=async(_,_)=>{try{if(ReadOnlyMidi.IsDeviceAvailable())await Refresh();else status.Text=UiText.T("Offline: Convert only is available.");}catch(Exception error){status.Text=error.Message;}};
        Closing+=(_,args)=>{if(busy){args.Cancel=true;status.Text=UiText.T(uploading?"Wait for the upload to finish.":"Cancel conversion before closing.");}};
    }
    internal void Preview(string path,string output)
    {
        source.Text=path;library.Text=SnapToneLibrary.DefaultRoot;name.Text="ML MEGA Rec Lead";
        target.ItemsSource=Enumerable.Range(0,10).Select(i=>new SnapToneSlot(i,1,"Empty")).ToArray();target.SelectedIndex=9;
        useIr.IsChecked=true;ir.Text="Corrective IR.wav";
        var root=(FrameworkElement)Content;root.Measure(new Size(660,double.PositiveInfinity));int height=(int)Math.Ceiling(root.DesiredSize.Height);root.Arrange(new Rect(0,0,660,height));root.UpdateLayout();
        var image=new System.Windows.Media.Imaging.RenderTargetBitmap(660,height,96,96,System.Windows.Media.PixelFormats.Pbgra32);var backdrop=new System.Windows.Media.DrawingVisual();using(var dc=backdrop.RenderOpen())dc.DrawRectangle(Background,null,new Rect(0,0,660,height));image.Render(backdrop);image.Render(root);
        var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));using var file=File.Create(output);encoder.Save(file);
    }
    Button Action(string label,Func<Task> action)
    {var button=new Button{Content=UiText.T(label),Padding=new Thickness(12,7,12,7),Margin=new Thickness(0,4,7,4)};actions.Add(button);button.Click+=async(_,_)=>{if(busy)return;try{await action();}catch(Exception error){status.Text=error.Message+(job==null?"":"\n"+job.Directory);}};return button;}
    void Row(string label,TextBox box,string action,Action choose)
    {panel.Children.Add(new TextBlock{Text=UiText.T(label),Margin=new Thickness(0,5,0,4)});var row=new DockPanel();var button=Action(action,()=>{choose();return Task.CompletedTask;});DockPanel.SetDock(button,Dock.Right);row.Children.Add(button);row.Children.Add(box);panel.Children.Add(row);}
    string? Choose(string filter){var dialog=new OpenFileDialog{Filter=filter,CheckFileExists=true};return dialog.ShowDialog(this)==true?dialog.FileName:null;}
    async Task Refresh(){int slot=(target.SelectedItem as SnapToneSlot)?.Index??0;var values=SnapToneSlots.Catalog(await DeviceMidi.ReadCatalogAsync(0x1012));target.ItemsSource=values;target.SelectedItem=values.FirstOrDefault(s=>s.Index==slot);CatalogRead?.Invoke(values);}
    async Task Run(bool load)
    {
        if(!File.Exists(source.Text))throw new FileNotFoundException(UiText.T("Choose a NAM or CLO file."));
        var slot=target.SelectedItem as SnapToneSlot;string requested=name.Text.Trim();
        if(load)
        {
            if(slot==null)throw new InvalidOperationException(UiText.T("Read slots and select a destination first."));
            if(requested.Length is <1 or >16||requested.Any(c=>c<32||c>126))throw new ArgumentException(UiText.T("Name (1–16 ASCII characters)"));
            if(slot.State==0&&UiDialogs.Show(this,UiText.T("Overwrite SnapTone slot?")+"\n"+slot, "Load SnapTone",MessageBoxButton.OKCancel)!=MessageBoxResult.OK)return;
        }
        bool nam=Path.GetExtension(source.Text).Equals(".nam",StringComparison.OrdinalIgnoreCase);
        if(nam&&useIr.IsChecked==true&&!File.Exists(ir.Text))throw new FileNotFoundException(UiText.T("Select a Corrective IR WAV."));
        if(nam&&useReference.IsChecked==true&&!File.Exists(reference.Text))throw new FileNotFoundException(UiText.T("Select a Tone Match reference WAV."));
        busy=true;foreach(var a in actions)a.IsEnabled=false;target.IsEnabled=name.IsEnabled=useIr.IsEnabled=useReference.IsEnabled=false;bar.Visibility=Visibility.Visible;bar.IsIndeterminate=true;cancellation=new();cancel.IsEnabled=true;
        var progress=new Progress<string>(text=>{if(busy)status.Text=UiText.T(text);});
        try
        {
            job??=SnapToneLibrary.Archive(library.Text,source.Text,nam&&useIr.IsChecked==true?ir.Text:null,nam&&useReference.IsChecked==true?reference.Text:null);
            if(job.Clo==null)await SnapToneLibrary.ConvertAsync(job,progress,cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();cancel.IsEnabled=false;
            if(load){uploading=true;await SnapToneImportService.LoadAsync(job,slot!.Index,requested,progress);await Refresh();}
            status.Text=UiText.T(load?"SnapTone catalog confirmed. Files kept in:":"Conversion complete. Files kept in:")+"\n"+job.Directory;
            if(job.Conversion is { } metric&&metric.TryGetProperty("rmseDb",out var rmse))status.Text+=$"\nTone Match RMSE: {rmse.GetDouble():F3} dB";
        }
        catch(OperationCanceledException){status.Text=UiText.T("Conversion cancelled. Original files are kept.");job=null;}
        catch{if(job?.Clo==null)job=null;throw;}
        finally{busy=uploading=false;bar.IsIndeterminate=false;bar.Visibility=Visibility.Collapsed;cancel.IsEnabled=false;foreach(var a in actions)a.IsEnabled=true;target.IsEnabled=name.IsEnabled=true;useIr.IsEnabled=useReference.IsEnabled=nam;cancellation.Dispose();cancellation=null;}
    }
}
