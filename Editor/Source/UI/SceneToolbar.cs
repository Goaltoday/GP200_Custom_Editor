using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
namespace Gp200.Replica;

internal sealed class SceneToolbar:StackPanel
{
    internal readonly CheckBox Toggle=new(){Content="Enable Scenes",Tag="scene-toggle",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(4,0,12,0)};
    internal readonly StackPanel Choices=new(){Orientation=Orientation.Horizontal};
    internal readonly Button[] Buttons=new Button[4];
    internal Func<int,Task<int>> Execute {get;set;}=_=>throw new InvalidOperationException("Disconnected");
    internal Action<Exception>? Failed;
    internal int? Status {get;private set;}
    internal int? Slot {get;private set;}
    bool connected,busy,paused;
    static readonly Brush selectedBackground=new SolidColorBrush(Color.FromRgb(26,95,89)),normalBackground=new SolidColorBrush(Color.FromRgb(36,48,62)),onForeground=new SolidColorBrush(Color.FromRgb(112,224,186));
    static string L(string en,string es)=>UiText.Language=="es"?es:en;
    internal SceneToolbar()
    {
        Orientation=Orientation.Horizontal;HorizontalAlignment=HorizontalAlignment.Center;VerticalAlignment=VerticalAlignment.Center;
        Children.Add(Toggle);Children.Add(Choices);
        Toggle.Click+=async(_,_)=>await Run(Status==4?2:1);
        Toggle.ToolTip=L("Enable/disable Scenes. The switch follows the confirmed GP-200 state.","Activa o desactiva Escenas. El interruptor refleja el estado confirmado de la GP-200.");
        for(int i=0;i<4;i++)
        {
            int scene=i;var button=new Button{Content=((char)('A'+i)).ToString(),Tag=$"scene-{i}",MinWidth=32,Padding=new Thickness(9,7,9,7),Margin=new Thickness(2,0,2,0)};Buttons[i]=button;
            button.ToolTip=L("Left-click: recall. Right-click: capture current sound here. Save to retain Scenes.","Clic izquierdo: recuperar. Clic derecho: capturar aquí el sonido actual. Guardar conserva las escenas.");
            button.Click+=async(_,_)=>await Run(3+scene);
            bool captureGesture=false;
            button.PreviewMouseRightButtonDown+=(_,e)=>{captureGesture=true;button.CaptureMouse();e.Handled=true;};
            button.PreviewMouseRightButtonUp+=async(_,e)=>{e.Handled=true;bool capture=captureGesture&&new Rect(0,0,button.ActualWidth,button.ActualHeight).Contains(e.GetPosition(button));captureGesture=false;if(button.IsMouseCaptured)button.ReleaseMouseCapture();if(capture)await Run(7+scene);};
            button.LostMouseCapture+=(_,_)=>captureGesture=false;
            Choices.Children.Add(button);
        }
        Refresh();
    }
    internal void Session(bool custom,bool ready,int? slot)
    {
        Visibility=custom?Visibility.Visible:Visibility.Collapsed;
        if(!ready||Slot!=slot)Status=null;
        Slot=slot;connected=ready;Refresh();
    }
    internal void Confirm(int? status){if(status is <0 or >5)throw new ArgumentOutOfRangeException();Status=status;Refresh();}
    internal void Pause(bool value){paused=value;Refresh();}
    internal async Task Run(int command)
    {
        if(busy||paused||!connected||Status==null){Refresh();return;}
        if(command>=3&&Status==4)return;
        busy=true;Refresh();
        try{Confirm(await Execute(command));}
        catch(Exception e){Confirm(null);Failed?.Invoke(e);}
        finally{busy=false;Refresh();}
    }
    void Refresh()
    {
        bool enabled=Status.HasValue&&Status!=4;Toggle.IsChecked=enabled;Toggle.IsEnabled=connected&&Status.HasValue&&!busy&&!paused;
        Toggle.Foreground=enabled?onForeground:Brushes.LightGray;
        Choices.Visibility=enabled?Visibility.Visible:Visibility.Collapsed;
        for(int i=0;i<4;i++)
        {
            Buttons[i].IsEnabled=connected&&enabled&&!busy&&!paused;
            Buttons[i].Background=Status==i?selectedBackground:normalBackground;
            Buttons[i].Foreground=Status==i?Brushes.White:Brushes.LightGray;
            Buttons[i].FontWeight=Status==i?FontWeights.Bold:FontWeights.Normal;
        }
    }
}

public sealed partial class EditorWindow
{
    readonly SceneToolbar sceneToolbar=new();
    bool sceneStatusPolling;
    DateTime nextSceneStatus;
    DateTime lastPolledSnapshotUtc;
    DateTime lastSceneCommand;
    async Task RefreshSceneToolbar()
    {
        int? slot=liveEntry?.DeviceSlot;
        bool supported=CatalogRuntime.Current.IsMx42||catalog.IsMx42;
        bool changed=sceneToolbar.Slot!=slot;
        sceneToolbar.Session(catalog.IsMx42,supported&&live!=null,slot);
        sceneToolbar.Pause(liveBusy||livePump||pendingLive!=null||loadingLibrary||sliderGesture||chainGesture||devicePanelOpen||current!=liveEntry);
        if(!supported||live==null||slot==null||sceneStatusPolling||polling||liveBusy||livePump||pendingLive!=null||loadingLibrary||sliderGesture||chainGesture||devicePanelOpen||!changed&&DateTime.UtcNow<nextSceneStatus)return;
        sceneStatusPolling=true;var session=live;int? previous=sceneToolbar.Status;
        try
        {
            lastSceneCommand=DateTime.UtcNow;
            int actual;
            if(catalog.IsMx42)
            {
                await DeviceMidi.OperationGate.WaitAsync();
                try
                {
                    // This tick already read the preset. Reuse only that fresh context
                    // for the non-mutating status query, never for recall/capture/STORE.
                    var fresh=DateTime.UtcNow-lastPolledSnapshotUtc<TimeSpan.FromMilliseconds(100)&&session.Baseline.Index==slot.Value;
                    actual=await SceneMailbox.Command(slot.Value,session.Baseline.GetEffect(10).Code,0,null,true,fresh?session.Baseline:null);
                }
                finally{DeviceMidi.OperationGate.Release();}
            }
            else actual=await SceneMailbox.Command(slot.Value,session.Baseline.GetEffect(10).Code,0,null);
            if(live!=session||liveEntry?.DeviceSlot!=slot||liveBusy||livePump)return;
            sceneToolbar.Confirm(actual);
            if(previous.HasValue&&previous!=actual&&actual!=4)
            {
                var snapshot=await session.ReadCurrentAsync();
                if(live!=session||liveBusy||livePump||snapshot.Index!=slot)return;
                session.AcceptSnapshot(snapshot);liveEntry!.Document=snapshot;liveEntry.EditingBaseline=snapshot.ToBytes();liveEntry.Undo.Clear();liveEntry.Redo.Clear();desiredLive=null;pendingLive=null;
                RefreshLibrary();if(current==liveEntry)Render();
            }
        }
        catch{sceneToolbar.Confirm(null);}
        finally{sceneStatusPolling=false;nextSceneStatus=DateTime.UtcNow.AddSeconds(2);}
    }
    async Task<int> ExecuteToolbarScene(int command)
    {
        Mx42Policy.RequireScenes();
        if(live==null||liveEntry==null||current!=liveEntry||liveBusy||livePump||pendingLive!=null||loadingLibrary||chainGesture||devicePanelOpen)throw new InvalidOperationException(UiText.T("Espera a que termine la edición pendiente."));
        liveBusy=true;IsEnabled=false;
        try
        {
            while(polling||sceneStatusPolling)await Task.Delay(15);
            var wait=lastSceneCommand.AddMilliseconds(500)-DateTime.UtcNow;if(wait>TimeSpan.Zero)await Task.Delay(wait);
            var session=live;var entry=liveEntry;var before=await session.ReadCurrentAsync();
            string folder=Path.Combine(AppPaths.Evidence,$"scene-toolbar-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}");Directory.CreateDirectory(folder);File.WriteAllBytes(Path.Combine(folder,"before.prst"),before.ToBytes());
            lastSceneCommand=DateTime.UtcNow;
            int actual=await SceneMailbox.Command(before.Index,before.GetEffect(10).Code,command,Path.Combine(folder,"response.json"));
            var after=await session.ReadCurrentAsync();if(after.Index!=before.Index)throw new IOException("Active preset changed during Scene command. No command was repeated.");
            if(command>=3&&actual!=(command-3)%4||command==1&&actual!=4||command==2&&actual==4)throw new IOException("The GP-200 did not confirm the requested Scene operation. No command was repeated.");
            session.AcceptSnapshot(after);entry.Document=after;if(command>=7)entry.MarkSceneCaptured();desiredLive=null;pendingLive=null;
            if(command<7){entry.EditingBaseline=after.ToBytes();entry.Undo.Clear();entry.Redo.Clear();}
            EditorAuditLog.SupplementBytes(Path.Combine(folder,"after.prst"),after.ToBytes());
            RefreshLibrary();Render();nextSceneStatus=DateTime.UtcNow.AddSeconds(2);
            status.Text=command>=7?(UiText.Language=="es"?$"Escena {(char)('A'+command-7)} capturada. Guardar conserva las escenas.":$"Scene {(char)('A'+command-7)} captured. Save to retain Scenes."):(actual<4?$"Scene {(char)('A'+actual)}":actual==4?"Scenes OFF":"Scenes ON");
            return actual;
        }
        finally{liveBusy=false;IsEnabled=true;}
    }
}
