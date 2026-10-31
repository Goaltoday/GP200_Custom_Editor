using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace Gp200.Replica;

public sealed class DeviceWindow:Window
{
    readonly TextBox content=new(){IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Background=new SolidColorBrush(Color.FromRgb(16,21,28)),Foreground=Brushes.White,Padding=new Thickness(18),FontFamily=new FontFamily("Consolas")};
    readonly List<DeviceResponse> responses=[];
    readonly Button read=new(){Content=UiText.T("Leer identidad, estado y preset activo"),Padding=new Thickness(16,10,16,10),Margin=new Thickness(0,0,12,0)};
    readonly Button save=new(){Content=UiText.T("Guardar evidencia…"),Padding=new Thickness(16,10,16,10),IsEnabled=false};
    readonly Button open=new(){Content=UiText.T("Abrir copia editable"),Padding=new Thickness(16,10,16,10),Margin=new Thickness(12,0,0,0),IsEnabled=false};
    PresetDocument? snapshot;
    public event Action<PresetDocument>? SnapshotReady;
    bool reading;
    public DeviceWindow()
    {
        EditorWindow.StyleDialog(this);
        Title=UiText.T("GP-200 · Consulta de lectura");Width=820;Height=650;MinWidth=650;MinHeight=450;
        var grid=new Grid{Margin=new Thickness(20)};grid.RowDefinitions.Add(new(){Height=GridLength.Auto});grid.RowDefinitions.Add(new(){Height=GridLength.Auto});grid.RowDefinitions.Add(new());
        grid.Children.Add(new TextBlock{Text=UiText.T("Cierra el editor oficial antes de consultar. Solo se enviarán peticiones de lectura; los puertos se liberan al terminar."),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,18)});
        var buttons=new WrapPanel{Margin=new Thickness(0,0,0,18)};buttons.Children.Add(read);buttons.Children.Add(save);buttons.Children.Add(open);Grid.SetRow(buttons,1);grid.Children.Add(buttons);
        Grid.SetRow(content,2);grid.Children.Add(content);Content=grid;
        content.Text=UiText.T("No se ha abierto ningún puerto. Pulsa Leer para consultar la GP-200.");
        read.Click+=async(_,_)=>await Read();save.Click+=(_,_)=>Save();
        open.Click+=(_,_)=>{if(snapshot!=null)SnapshotReady?.Invoke(new PresetDocument(snapshot.ToBytes()));};
        Closing+=(_,e)=>{if(reading)e.Cancel=true;};
    }
    async Task Read()
    {
        reading=true;read.IsEnabled=false;save.IsEnabled=false;open.IsEnabled=false;snapshot=null;responses.Clear();content.Text=UiText.T("Consultando…\n");
        try
        {
            foreach(var query in new[]{ReadQuery.Identity,ReadQuery.State,ReadQuery.ActivePreset})
            {
                var response=await DeviceSession.Current.Read(query);responses.Add(response);
                content.AppendText(UiText.T($"\n{query}: {response.Length} bytes, {response.Received.Count} fragmentos\n"));
                var global=response.Records.FirstOrDefault(r=>r.Type==0x1007&&r.Value.Length==72);
                if(global!=null)content.AppendText("\n"+GlobalSnapshot.Parse(global.Value).Summary()+"\n");
                foreach(var record in response.Records)
                {
                    content.AppendText($"  TLV {record.Type:X4} · {record.Value.Length} bytes\n");
                    if(record.Type==2&&record.Value.Length>=88)
                    {
                        ushort index=BinaryPrimitives.ReadUInt16LittleEndian(record.Value);
                        string name=Encoding.Latin1.GetString(record.Value,16,16).Split('\0')[0];
                        content.AppendText(UiText.T($"  Preset activo: {index/4+1:00}-{(char)('A'+index%4)} · {name}\n"));
                    }
                }
                if(response.CleanupErrors.Count>0)throw new IOException(UiText.T("El driver no liberó todos los recursos: ")+string.Join(", ",response.CleanupErrors));
            }
            var state=responses.Single(r=>r.Query==ReadQuery.State).Records.Single(r=>r.Type==0x1007);
            ushort format=BinaryPrimitives.ReadUInt16LittleEndian(state.Value);
            snapshot=PresetDocument.FromSnapshot(responses.Single(r=>r.Query==ReadQuery.ActivePreset).Records,format);open.IsEnabled=true;
            content.AppendText(UiText.T("\nLecturas completadas. Puertos MIDI cerrados. Sin escrituras al equipo.\n"));
        }
        catch(Exception error){content.AppendText("\n"+error.Message+"\n");}
        finally{reading=false;read.IsEnabled=true;save.IsEnabled=responses.Count>0;}
    }
    void Save()
    {
        var dialog=new SaveFileDialog{Filter=UiText.Filter("Evidencia JSON|*.json"),FileName="GP200-lectura-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".json"};
        if(dialog.ShowDialog(this)!=true)return;
        try{using var stream=new FileStream(dialog.FileName,FileMode.CreateNew);JsonSerializer.Serialize(stream,new{time=DateTimeOffset.UtcNow,responses},new JsonSerializerOptions{WriteIndented=true});}
        catch(Exception error){UiDialogs.Show(this,error.Message,UiText.T("Guardar evidencia"));}
    }
}
