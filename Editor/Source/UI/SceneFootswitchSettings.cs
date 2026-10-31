using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Gp200.Replica;
internal static class SceneFootswitch
{
    internal static bool Supported=>CatalogRuntime.Current.IsMx42;
    internal static int Template(int stock,int? sceneStatus)=>sceneStatus is >=0 and <=3 or 5?3:stock is >=0 and <=2?stock:-1;
    internal static readonly string[] SceneActions=[..DeviceSettings.Actions,"Scene A","Scene B","Scene C","Scene D","Capture"];
}
public sealed partial class SettingsWindow
{
    static string SceneText(string en,string es)=>UiText.Language=="es"?es:en;
    int? footswitchSceneStatus;
    SceneFsTable? sceneFsTable;
    string? sceneFsError;
    bool HasSceneFootswitchControl=>SceneFootswitch.Supported||sceneFsTable?.Status is 0 or 1;
    async Task ReadSettings()
    {
        state=await DeviceSettings.ReadAsync();footswitchSceneStatus=null;sceneFsTable=null;sceneFsError=null;
        // Probe the device directly, including with a clean Stock/default local
        // profile. A validated SFU1 reply is the capability; no JSON is required.
        try{if(!CatalogRuntime.Current.IsMx42)throw new NotSupportedException("Scenes require MX42.");sceneFsTable=await SceneFootswitchProtocol.ReadAsync();if(sceneFsTable.Status!=0)sceneFsError="SFU1 configuration is not ready. Read again.";}
        catch(Exception e){sceneFsError=e.Message;}
        if(HasSceneFootswitchControl)
        {
            try{var active=await new BankService().ActiveAsync();footswitchSceneStatus=await SceneMailbox.Command(active.Index,active.GetEffect(10).Code,0,null);}
            catch{footswitchSceneStatus=null;}
        }
        editingTemplate=SceneFootswitch.Template(state.Global(24),footswitchSceneStatus);
    }
    void BuildFootswitchPanel()
    {
        var fs=Tab("Footswitch");var controls=new WrapPanel{Margin=new Thickness(0,0,0,10)};fs.Children.Add(controls);
        var mode=new StackPanel{Width=190};Choice(mode,"FS Mode",["Wait","Initial"],state!.Global(35),0,v=>DeviceSettings.SetGlobal(35,v),60);controls.Children.Add(mode);
        controls.Children.Add(new TextBlock{Text=SceneText("Template","Plantilla"),VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(10,0,8,0)});
        var labels=new List<string>{"Patch Mode","Stomp Mode","User Mode"};if(HasSceneFootswitchControl)labels.Add("Scenes");
        var template=new ComboBox{Tag="footswitch-template",ItemsSource=labels.Select(UiText.T).ToArray(),SelectedIndex=Math.Min(editingTemplate,labels.Count-1),Width=150};controls.Children.Add(template);
        template.SelectionChanged+=async(_,_)=>
        {
            int selected=template.SelectedIndex;if(selected<0||selected==editingTemplate)return;
            await Work(async()=>
            {
                if(CatalogRuntime.Current.IsMx42||HasSceneFootswitchControl)
                {
                    if(!HasSceneFootswitchControl)throw new NotSupportedException("Custom Scene mode is not verified for this generation.");
                    var active=await new BankService().ActiveAsync();int actual=await SceneMailbox.Command(active.Index,active.GetEffect(10).Code,selected==3?2:1,null);
                    if(selected==3&&actual==4||selected!=3&&actual!=4)throw new IOException("Scene mode change was not confirmed.");
                }
                if(selected<3)state=await DeviceSettings.ApplyAsync(DeviceSettings.SetGlobal(24,selected));
                await ReadSettings();if(editingTemplate!=selected)throw new IOException("Footswitch template readback differs from the requested mode.");Render();return SceneText("Footswitch mode confirmed.","Modo Footswitch confirmado.");
            });
        };
        if(editingTemplate<0)
        {fs.Children.Add(new TextBlock{Text=SceneText("Unknown footswitch mode preserved. Read the device again before editing assignments.","Modo Footswitch desconocido conservado. Relee el equipo antes de editar asignaciones."),TextWrapping=TextWrapping.Wrap});return;}
        if(editingTemplate==3)
        {
            fs.Children.Add(new TextBlock{Text=sceneFsTable?.Status==0?SceneText("SFU1 · Assignments read from GP-200. Changes use its global settings persistence. Capture: hold this switch while selecting a Scene.","SFU1 · Asignaciones leídas de GP-200. Se usa su persistencia de ajustes globales. Capture: mantén pulsado mientras eliges una escena."):SceneText("Scene assignments require firmware with SFU1 USB support. Use Read GP-200 after installing it. ","Las asignaciones requieren firmware con soporte USB SFU1. Pulsa Leer GP-200 después de instalarlo. ")+sceneFsError,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10),MaxWidth=700});
        }
        else
        {
            var reset=new Button{Content=UiText.T("Reset template…"),Padding=new Thickness(8,4,8,4),Margin=new Thickness(10,0,0,0)};controls.Children.Add(reset);
            reset.Click+=async(_,_)=>
            {
                int selected=editingTemplate;if(UiDialogs.Show(this,UiText.T($"¿Restablecer las asignaciones de {labels[selected]}? Se guardará una copia previa en Backups."),UiText.T("Reset footswitch template"),MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
                await Work(async()=>{state=await DeviceSettings.ResetFootswitchAsync(selected);Render();return UiText.T("Solicitud de reset enviada; plantilla releída. Copia previa en Backups.");});
            };
        }
        var switches=new WrapPanel();fs.Children.Add(switches);
        for(int i=0;i<8;i++)
        {
            int slot=i*2;var card=new StackPanel{Width=162,Margin=new Thickness(0,0,8,10)};
            card.Children.Add(new TextBlock{Text=$"FS {i+1}",FontSize=13,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,5)});
            if(editingTemplate==3)
            {
                foreach(string action in new[]{"TAP","HOLD"})
                {
                    var row=new DockPanel{Margin=new Thickness(0,2,0,2)};row.Children.Add(new TextBlock{Text=action,Width=40,VerticalAlignment=VerticalAlignment.Center});
                    int index=slot+(action=="HOLD"?1:0);bool ready=sceneFsTable?.Status==0;
                    var actionLabels=SceneFootswitch.SceneActions.Select(UiText.T).ToList();int value=ready?sceneFsTable!.Actions[index]:-1;
                    if(value>=actionLabels.Count){actionLabels.Add($"Unknown · {value}");value=actionLabels.Count-1;}
                    var combo=new ComboBox{Tag=$"scene-assignment-{i}-{action}",ItemsSource=actionLabels,SelectedIndex=value,IsEnabled=ready};
                    combo.SelectionChanged+=async(_,_)=>
                    {
                        int selected=combo.SelectedIndex;if(selected<0||selected>=SceneFootswitch.SceneActions.Length||sceneFsTable==null||sceneFsTable.Actions[index]==selected)return;
                        await Work(async()=>{sceneFsTable=await SceneFootswitchProtocol.SetAsync(sceneFsTable,index,selected);Render();return SceneText("Assignment applied and read back. Global persistence follows the GP-200 settings service.","Asignación aplicada y releída. La persistencia global sigue el servicio de ajustes de GP-200.");});
                    };
                    row.Children.Add(combo);card.Children.Add(row);
                }
            }
            else
            {
                Choice(card,"TAP",DeviceSettings.Actions,state!.Footswitch(editingTemplate,slot),0,a=>DeviceSettings.SetFootswitch(editingTemplate,slot,a),40);
                Choice(card,"HOLD",DeviceSettings.Actions,state!.Footswitch(editingTemplate,slot+1),0,a=>DeviceSettings.SetFootswitch(editingTemplate,slot+1,a),40);
            }
            switches.Children.Add(card);
        }
        if(editingTemplate!=3)
        {
            var combinations=new WrapPanel();fs.Children.Add(combinations);string[] names=["FS1 + FS5","FS5 + FS6","FS6 + FS7","FS7 + FS8"];
            for(int i=0;i<4;i++){int slot=16+i;var card=new StackPanel{Width=162,Margin=new Thickness(0,0,8,0)};card.Children.Add(new TextBlock{Text=names[i],Margin=new Thickness(0,0,0,4)});Choice(card,"TAP",DeviceSettings.Actions,state!.Footswitch(editingTemplate,slot),0,a=>DeviceSettings.SetFootswitch(editingTemplate,slot,a),40);combinations.Children.Add(card);}
        }
    }
}
