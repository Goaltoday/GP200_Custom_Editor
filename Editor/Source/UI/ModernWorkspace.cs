using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
namespace Gp200.Replica;
public sealed partial class EditorWindow
{
    readonly TextBlock connectionBadge=new(){FontSize=12,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(20,0,0,0)};
    readonly StackPanel patchOverview=new();
    Button? nativeNamButton;
    Action<double>? applyInterfaceScale;
    static Border Card(UIElement child,int padding=12)=>new(){Child=child,Padding=new Thickness(padding),Background=PanelBrush,BorderBrush=Brush("#2C3744"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(9)};
    Grid BuildModernShell()
    {
        InstallModernStyles();
        var root=new Grid{Margin=new Thickness(16),Background=BackgroundBrush};root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new());root.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var header=new Grid();header.ColumnDefinitions.Add(new(){Width=GridLength.Auto});header.ColumnDefinitions.Add(new());header.ColumnDefinitions.Add(new(){Width=GridLength.Auto});var brand=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};brand.Children.Add(Text("GP-200",27,FontWeights.Bold));brand.Children.Add(connectionBadge);
        var tools=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
        tools.Children.Add(Button(UiText.T("Deshacer"),()=>History(false)));tools.Children.Add(Button(UiText.T("Rehacer"),()=>History(true)));
        tools.Children.Add(Button("Load IR",()=>OpenDevicePanel(()=>new IrImportWindow(()=>DeviceMidi.ReadCatalogAsync(0x1009),async record=>{var response=await DeviceMidi.SendIrAsync(record);if(response.CleanupErrors.Count!=0)throw new IOException(UiText.T("El driver MIDI no se liberó correctamente."));}))));
        tools.Children.Add(Button("Load SnapTone",()=>OpenDevicePanel(()=>new SnapToneImportWindow())));
        nativeNamButton=Button("Load NAM",()=>{OpenDevicePanel(()=>new Mx42NamWindow());});nativeNamButton.Content="Load NAM";nativeNamButton.Visibility=catalog.IsMx42?Visibility.Visible:Visibility.Collapsed;tools.Children.Add(nativeNamButton);
        tools.Children.Add(Button("Settings",()=>OpenDevicePanel(()=>new SettingsWindow())));
        Grid.SetColumn(tools,2);header.Children.Add(tools);header.Children.Add(brand);
        sceneToolbar.Toggle.Style=(Style)FindResource("ChainToggle");sceneToolbar.Toggle.FontSize=12;
        sceneToolbar.Execute=ExecuteToolbarScene;sceneToolbar.Failed=e=>{status.Text=e.Message;UiDialogs.Show(this,e.Message,"Scenes");};
        SetupChainControls();var featureSwitches=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};featureSwitches.Children.Add(sceneToolbar);featureSwitches.Children.Add(enableChain);Grid.SetColumn(featureSwitches,1);header.Children.Add(featureSwitches);sceneToolbar.Session(catalog.IsMx42,false,null);
        var top=Card(header,14);top.Margin=new Thickness(0,0,0,12);root.Children.Add(top);
        var body=new Grid();body.ColumnDefinitions.Add(new(){Width=new GridLength(4,GridUnitType.Star)});body.ColumnDefinitions.Add(new(){Width=new GridLength(12)});body.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star),MinWidth=302});
        details.RowDefinitions.Add(new());details.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var workspace=new Grid();workspace.ColumnDefinitions.Add(new(){Width=new GridLength(2,GridUnitType.Star),MinWidth=300,MaxWidth=510});workspace.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star),MinWidth=180});workspace.ColumnDefinitions.Add(new(){Width=new GridLength(210)});
        // Keep the controls compact; distribute extra width to readable names.
        tabs.Background=PanelBrush;tabs.Foreground=TextBrush;tabs.BorderThickness=new Thickness(0);
        var presenter=new FrameworkElementFactory(typeof(ContentPresenter));presenter.SetBinding(ContentPresenter.ContentProperty,new System.Windows.Data.Binding("SelectedContent"){RelativeSource=System.Windows.Data.RelativeSource.TemplatedParent});tabs.Template=new ControlTemplate(typeof(TabControl)){VisualTree=presenter};
        var effect=Card(tabs,0);effect.Margin=new Thickness(0,0,10,0);workspace.Children.Add(effect);
        var algorithms=Card(algorithmHost,8);algorithms.Margin=new Thickness(0,0,10,0);Grid.SetColumn(algorithms,1);workspace.Children.Add(algorithms);
        var presetColumn=new DockPanel();var currentInfo=new StackPanel();title.FontSize=17;title.FontWeight=FontWeights.SemiBold;title.TextWrapping=TextWrapping.Wrap;subtitle.FontSize=10;subtitle.Foreground=Muted;subtitle.TextWrapping=TextWrapping.Wrap;subtitle.Margin=new Thickness(0,5,0,10);currentInfo.Children.Add(title);currentInfo.Children.Add(subtitle);currentInfo.Children.Add(presetSummary);
        var infoCard=Card(currentInfo,12);infoCard.Margin=new Thickness(0,0,0,10);DockPanel.SetDock(infoCard,Dock.Top);presetColumn.Children.Add(infoCard);
        var patch=new StackPanel();var patchButton=Button("Patch Setting  ⚙",OpenPatchSettings);patchButton.Margin=new Thickness(0,0,0,12);patch.Children.Add(patchButton);patch.Children.Add(patchOverview);presetColumn.Children.Add(Card(patch,12));Grid.SetColumn(presetColumn,2);workspace.Children.Add(presetColumn);details.Children.Add(workspace);
        var chainHost=new ScrollViewer{Content=chain,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled};var chainContents=new StackPanel();chainContents.Children.Add(chainOptions);chainContents.Children.Add(chainHost);var chainCard=Card(chainContents,8);chainCard.Margin=new Thickness(0,12,0,0);Grid.SetRow(chainCard,1);details.Children.Add(chainCard);body.Children.Add(details);
        chainHost.SizeChanged+=(_,e)=>ResizeChain(e.NewSize.Width);
        var sidebar=Card(BuildWorkspaceSidebar(),0);Grid.SetColumn(sidebar,2);body.Children.Add(sidebar);Grid.SetRow(body,1);root.Children.Add(body);
        status.Foreground=Muted;status.FontSize=11;status.Margin=new Thickness(4,10,0,0);status.Text=UiText.T("Abre tus presets para explorar bloques, parámetros y asignaciones.");Grid.SetRow(status,2);root.Children.Add(status);UpdateModernConnection();return root;
    }
    void UpdateModernConnection(){connectionBadge.Text=(live!=null?"●  GP-200 USB":"○  GP-200 USB")+" · "+(catalog.IsMx42?"Custom":"Stock")+(catalog.IsMx42&&live!=null?" · live":"");connectionBadge.Foreground=live!=null?Accent:Muted;connectionBadge.ToolTip=catalog.ProfileStatus;if(nativeNamButton!=null){nativeNamButton.Content="Load NAM";nativeNamButton.Visibility=catalog.IsMx42?Visibility.Visible:Visibility.Collapsed;}UpdateChainInteraction();}
    void ResizeChain(double width)
    {
        foreach(var ribbon in chain.Children.OfType<ParallelChainRibbon>())ribbon.Resize(width);
        double blockWidth=Math.Max(66,(width-12*22-4)/11);
        foreach(var block in chain.Children.OfType<StackPanel>())
        {
            if(!block.Children.OfType<ComboBox>().Any())continue;
            foreach(var selector in block.Children.OfType<ComboBox>())selector.Width=blockWidth;
            foreach(var button in block.Children.OfType<Button>())
            {
                button.Width=blockWidth;
                if(button.Content is StackPanel content)
                    foreach(var icon in content.Children.OfType<Image>())icon.Width=blockWidth-6;
            }
        }
    }
    void ScrollActivePresetToTop()
    {
        var entry=current;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,new Action(()=>
        {
            if(entry==null||current!=entry||!library.Items.Contains(entry))return;
            library.ScrollIntoView(entry);library.UpdateLayout();
            ScrollViewer? FindScroll(DependencyObject node)
            {
                if(node is ScrollViewer scroll)return scroll;
                for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)
                    if(FindScroll(VisualTreeHelper.GetChild(node,i)) is ScrollViewer found)return found;
                return null;
            }
            var viewer=FindScroll(library);if(viewer==null)return;
            if(viewer.CanContentScroll)viewer.ScrollToVerticalOffset(library.Items.IndexOf(entry));
            else if(library.ItemContainerGenerator.ContainerFromItem(entry) is FrameworkElement item)
                viewer.ScrollToVerticalOffset(viewer.VerticalOffset+item.TranslatePoint(new Point(),viewer).Y);
        }));
    }
    void InstallModernStyles()
    {
        StyleDialog(this);
    }
    internal static void StyleDialog(Window window)
    {
        window.Background=BackgroundBrush;window.Foreground=TextBrush;window.FontFamily=new FontFamily("Segoe UI");
        var styles=(ResourceDictionary)XamlReader.Parse(ModernStyles);foreach(object key in styles.Keys)window.Resources[key]=styles[key];
        if(window is not EditorWindow)CompactDialogStyles(window);
    }
    internal static ContextMenu DarkContextMenu(Window owner)
    {
        var menu=new ContextMenu();foreach(object key in owner.Resources.Keys)menu.Resources[key]=owner.Resources[key];return menu;
    }
    const string ModernStyles="""
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
<SolidColorBrush x:Key="{x:Static SystemColors.WindowBrushKey}" Color="#10151C"/>
<SolidColorBrush x:Key="{x:Static SystemColors.WindowTextBrushKey}" Color="#EAF0F7"/>
<SolidColorBrush x:Key="{x:Static SystemColors.ControlBrushKey}" Color="#202D3B"/>
<SolidColorBrush x:Key="{x:Static SystemColors.ControlTextBrushKey}" Color="#EAF0F7"/>
<SolidColorBrush x:Key="{x:Static SystemColors.MenuBrushKey}" Color="#1C2733"/>
<SolidColorBrush x:Key="{x:Static SystemColors.MenuTextBrushKey}" Color="#EAF0F7"/>
<SolidColorBrush x:Key="{x:Static SystemColors.GrayTextBrushKey}" Color="#9BAAB9"/>
<SolidColorBrush x:Key="{x:Static SystemColors.HighlightBrushKey}" Color="#195759"/>
<SolidColorBrush x:Key="{x:Static SystemColors.HighlightTextBrushKey}" Color="#F1FFFF"/>
<Style TargetType="ListBox"><Setter Property="Background" Value="#19212C"/><Setter Property="Foreground" Value="#EAF0F7"/><Setter Property="BorderBrush" Value="#354252"/><Setter Property="BorderThickness" Value="1"/><Setter Property="ScrollViewer.VerticalScrollBarVisibility" Value="Auto"/><Setter Property="ScrollViewer.HorizontalScrollBarVisibility" Value="Disabled"/><Setter Property="ScrollViewer.CanContentScroll" Value="True"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ListBox"><Border Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}"><ScrollViewer Focusable="False" Padding="{TemplateBinding Padding}"><ItemsPresenter/></ScrollViewer></Border></ControlTemplate></Setter.Value></Setter></Style>
<Style TargetType="Button"><Setter Property="Background" Value="#263442"/><Setter Property="Foreground" Value="#EAF0F7"/><Setter Property="Padding" Value="13,9"/><Setter Property="Margin" Value="0,0,7,0"/><Setter Property="Cursor" Value="Hand"/><Setter Property="FontSize" Value="12"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button"><Border x:Name="b" CornerRadius="6" BorderThickness="1" BorderBrush="#3A4858" Background="{TemplateBinding Background}" Padding="{TemplateBinding Padding}"><ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/></Border><ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="b" Property="Background" Value="#344858"/><Setter TargetName="b" Property="BorderBrush" Value="#4A8D91"/></Trigger><Trigger Property="IsPressed" Value="True"><Setter TargetName="b" Property="Background" Value="#1A5357"/></Trigger><Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.4"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
<Style TargetType="TextBox"><Setter Property="Foreground" Value="#EAF0F7"/><Setter Property="Background" Value="#111923"/><Setter Property="CaretBrush" Value="#31D8CA"/><Setter Property="SelectionBrush" Value="#2C666B"/><Setter Property="BorderBrush" Value="#354252"/><Setter Property="BorderThickness" Value="1"/><Setter Property="Padding" Value="8,6"/><Setter Property="FontSize" Value="12"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="TextBox"><Border x:Name="b" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="5" Padding="{TemplateBinding Padding}"><ScrollViewer x:Name="PART_ContentHost"/></Border><ControlTemplate.Triggers><Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="b" Property="BorderBrush" Value="#31D8CA"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
<Style TargetType="ComboBox"><Setter Property="Foreground" Value="#EAF0F7"/><Setter Property="Background" Value="#202D3B"/><Setter Property="FontSize" Value="11"/><Setter Property="Padding" Value="7,5"/><Setter Property="MinHeight" Value="29"/><Setter Property="ScrollViewer.HorizontalScrollBarVisibility" Value="Disabled"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ComboBox"><Grid><ToggleButton Focusable="False" IsChecked="{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}"><ToggleButton.Template><ControlTemplate TargetType="ToggleButton"><Border Background="#202D3B" BorderBrush="#354252" BorderThickness="1" CornerRadius="5"/></ControlTemplate></ToggleButton.Template></ToggleButton><Grid IsHitTestVisible="False" Margin="7,4"><Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition Width="12"/></Grid.ColumnDefinitions><ContentPresenter Content="{TemplateBinding SelectionBoxItem}" ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}" ContentTemplateSelector="{TemplateBinding ItemTemplateSelector}" ContentStringFormat="{TemplateBinding SelectionBoxItemStringFormat}" VerticalAlignment="Center"/><TextBlock Grid.Column="1" Text="⌄" Foreground="#A2B6C7"/></Grid><Popup x:Name="PART_Popup" IsOpen="{TemplateBinding IsDropDownOpen}" Placement="Bottom" AllowsTransparency="True" Focusable="False"><Border Background="#1C2733" BorderBrush="#405568" BorderThickness="1" CornerRadius="5" MinWidth="{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}" MaxHeight="320"><ScrollViewer><ItemsPresenter/></ScrollViewer></Border></Popup></Grid><ControlTemplate.Triggers><Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.4"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
<Style TargetType="ComboBoxItem"><Setter Property="Foreground" Value="#EAF0F7"/><Setter Property="Padding" Value="8,5"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ComboBoxItem"><Border x:Name="b" Background="Transparent" CornerRadius="3" Padding="{TemplateBinding Padding}"><ContentPresenter/></Border><ControlTemplate.Triggers><Trigger Property="IsHighlighted" Value="True"><Setter TargetName="b" Property="Background" Value="#28575D"/></Trigger><Trigger Property="IsSelected" Value="True"><Setter TargetName="b" Property="Background" Value="#28575D"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
<Style TargetType="ListBoxItem"><Setter Property="Foreground" Value="#D5E0ED"/><Setter Property="Padding" Value="7,5"/><Setter Property="Margin" Value="0,1"/><Setter Property="HorizontalContentAlignment" Value="Stretch"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ListBoxItem"><Border x:Name="b" Background="Transparent" CornerRadius="4" BorderThickness="1" BorderBrush="Transparent" Padding="{TemplateBinding Padding}"><ContentPresenter/></Border><ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="b" Property="Background" Value="#283947"/></Trigger><Trigger Property="IsSelected" Value="True"><Setter TargetName="b" Property="Background" Value="#195759"/><Setter TargetName="b" Property="BorderBrush" Value="#30BDB5"/><Setter Property="Foreground" Value="#F1FFFF"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
<Style x:Key="ChainToggle" TargetType="CheckBox"><Setter Property="Foreground" Value="#B7C8D8"/><Setter Property="FontSize" Value="10"/><Setter Property="Cursor" Value="Hand"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="CheckBox"><StackPanel Orientation="Horizontal"><Border x:Name="b" Width="28" Height="15" CornerRadius="7" Background="#39434F"><Ellipse x:Name="dot" Width="9" Height="9" Margin="3" HorizontalAlignment="Left" Fill="#B8C4D0"/></Border><ContentPresenter Margin="5,0,0,0" VerticalAlignment="Center"/></StackPanel><ControlTemplate.Triggers><Trigger Property="IsChecked" Value="True"><Setter TargetName="b" Property="Background" Value="#168F8B"/><Setter TargetName="dot" Property="HorizontalAlignment" Value="Right"/><Setter TargetName="dot" Property="Fill" Value="#DFFFFA"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
<Style TargetType="Slider"><Setter Property="MinHeight" Value="20"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Slider"><Grid Background="Transparent" MinHeight="20"><Track x:Name="PART_Track" Minimum="{TemplateBinding Minimum}" Maximum="{TemplateBinding Maximum}" Value="{TemplateBinding Value}" IsDirectionReversed="{TemplateBinding IsDirectionReversed}"><Track.DecreaseRepeatButton><RepeatButton Command="Slider.DecreaseLarge"><RepeatButton.Template><ControlTemplate TargetType="RepeatButton"><Border Background="#2CBFB7" Height="4" CornerRadius="2"/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton><Track.Thumb><Thumb Width="12" Height="12"><Thumb.Template><ControlTemplate TargetType="Thumb"><Ellipse Fill="#D1E8EB" Stroke="#76BDBD" StrokeThickness="1"/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb><Track.IncreaseRepeatButton><RepeatButton Command="Slider.IncreaseLarge"><RepeatButton.Template><ControlTemplate TargetType="RepeatButton"><Border Background="#344352" Height="4" CornerRadius="2"/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton></Track></Grid></ControlTemplate></Setter.Value></Setter></Style>
<Style TargetType="ContextMenu"><Setter Property="Background" Value="#1C2733"/><Setter Property="Foreground" Value="#EAF0F7"/><Setter Property="BorderBrush" Value="#405568"/></Style>
<Style TargetType="MenuItem"><Setter Property="Foreground" Value="#EAF0F7"/><Setter Property="Background" Value="Transparent"/><Setter Property="Padding" Value="8,6"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="MenuItem"><Border x:Name="item" Background="{TemplateBinding Background}" CornerRadius="3" Padding="{TemplateBinding Padding}"><Grid><Grid.ColumnDefinitions><ColumnDefinition Width="18"/><ColumnDefinition Width="*"/><ColumnDefinition Width="18"/></Grid.ColumnDefinitions><TextBlock x:Name="check" Text="✓" Visibility="Collapsed" Foreground="#70E0BA"/><ContentPresenter Grid.Column="1" ContentSource="Header" RecognizesAccessKey="True" VerticalAlignment="Center" Margin="2,0,12,0"/><TextBlock x:Name="arrow" Grid.Column="2" Text="›" Visibility="Collapsed" Foreground="#B7C8D8"/><Popup x:Name="PART_Popup" IsOpen="{Binding IsSubmenuOpen, RelativeSource={RelativeSource TemplatedParent}}" Placement="Right" AllowsTransparency="True" Focusable="False" PopupAnimation="None"><Border Background="#1C2733" BorderBrush="#405568" BorderThickness="1" CornerRadius="5" Padding="3"><ScrollViewer MaxHeight="600" VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled"><ItemsPresenter KeyboardNavigation.DirectionalNavigation="Cycle"/></ScrollViewer></Border></Popup></Grid></Border><ControlTemplate.Triggers><Trigger Property="HasItems" Value="True"><Setter TargetName="arrow" Property="Visibility" Value="Visible"/></Trigger><Trigger Property="IsHighlighted" Value="True"><Setter TargetName="item" Property="Background" Value="#28575D"/></Trigger><Trigger Property="IsSubmenuOpen" Value="True"><Setter TargetName="item" Property="Background" Value="#28575D"/></Trigger><Trigger Property="IsChecked" Value="True"><Setter TargetName="check" Property="Visibility" Value="Visible"/></Trigger><Trigger Property="IsEnabled" Value="False"><Setter Property="Foreground" Value="#9BAAB9"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
<Style TargetType="Separator"><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Separator"><Border Background="#405568" Height="1" Margin="6,4"/></ControlTemplate></Setter.Value></Setter></Style>
<Style TargetType="TabControl"><Setter Property="Background" Value="#19212C"/><Setter Property="Foreground" Value="#EAF0F7"/><Setter Property="BorderThickness" Value="0"/></Style>
<Style TargetType="TabItem"><Setter Property="Foreground" Value="#A8BBCD"/><Setter Property="Padding" Value="13,10"/><Setter Property="Margin" Value="3,2"/><Setter Property="MinWidth" Value="125"/><Setter Property="FontSize" Value="12"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="TabItem"><Border x:Name="b" Background="#18232F" CornerRadius="5" Padding="{TemplateBinding Padding}"><ContentPresenter ContentSource="Header"/></Border><ControlTemplate.Triggers><Trigger Property="IsSelected" Value="True"><Setter TargetName="b" Property="Background" Value="#1A5559"/><Setter Property="Foreground" Value="#E6FFFC"/></Trigger><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="b" Property="Background" Value="#2B414F"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
</ResourceDictionary>
""";
}
