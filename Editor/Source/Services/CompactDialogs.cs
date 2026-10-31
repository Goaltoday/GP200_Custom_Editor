using System.Windows;
using System.Windows.Controls;
namespace Gp200.Replica;
public sealed partial class EditorWindow
{
    static void CompactDialogStyles(Window window)
    {
        foreach(Type type in new[]{typeof(Button),typeof(TextBox),typeof(ComboBox),typeof(TabItem)})
        {
            var style=new Style(type,(Style)window.Resources[type]);
            style.Setters.Add(new Setter(Control.FontSizeProperty,11d));
            style.Setters.Add(new Setter(Control.PaddingProperty,type==typeof(TabItem)?new Thickness(9,6,9,6):new Thickness(7,4,7,4)));
            if(type!=typeof(TabItem))style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty,26d));
            if(type==typeof(TabItem))style.Setters.Add(new Setter(FrameworkElement.MinWidthProperty,105d));
            window.Resources[type]=style;
        }
    }
}
