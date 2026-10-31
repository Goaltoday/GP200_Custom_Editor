using System.Windows;using System.Windows.Controls;
namespace Gp200.Replica;
public sealed class DeviceProfilePanel:StackPanel
{
 static string L(string en,string es)=>UiText.Language=="es"?es:en;
 public DeviceProfilePanel(){var p=AppPreferences.Load();Children.Add(new TextBlock{Text=L("Device profile","Perfil del dispositivo"),FontSize=16});
 var mode=new ComboBox{ItemsSource=new[]{"Stock","Custom"},SelectedIndex=p.DeviceProfile=="MX42"?1:0,Width=200,HorizontalAlignment=HorizontalAlignment.Left};Children.Add(mode);
 Children.Add(new TextBlock{Text=L("Select the installed firmware. Restart after changing profile.","Selecciona el firmware instalado y reinicia tras cambiar de perfil."),TextWrapping=TextWrapping.Wrap});
 var save=new Button{Content=L("Save profile","Guardar perfil"),HorizontalAlignment=HorizontalAlignment.Left};Children.Add(save);
 save.Click+=(_,_)=>{try{(AppPreferences.Load() with{DeviceProfile=mode.SelectedIndex==1?"MX42":"Stock",NeedsProfileSelection=false}).Save();UiDialogs.Show(Window.GetWindow(this),L("Profile saved. Restart the editor to connect.","Perfil guardado. Reinicia el editor para conectar."),L("Device profile","Perfil del dispositivo"));}catch(Exception e){UiDialogs.Show(Window.GetWindow(this),e.Message,"Device profile");}};
 }
}
