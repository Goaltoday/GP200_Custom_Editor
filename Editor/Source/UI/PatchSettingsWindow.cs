using System.Globalization;

using System.Windows;

using System.Windows.Controls;

using System.Windows.Media;

using System.Windows.Media.Imaging;

namespace Gp200.Replica;

public sealed class PatchSettingsWindow:Window

{

    readonly Func<PresetDocument> read;

    readonly Action<Action<PresetDocument>> edit;

    readonly Catalog catalog;

    readonly ContentControl content=new();

    readonly ListBox sections=new(){ItemsSource=new[]{"CTRL Setting","EXP Setting","Quick Access","FX Loop"}.Select(UiText.T).ToArray(),SelectedIndex=0,Width=130};

    readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap,Foreground=Brushes.LightGray};

    int controller,expression;

    readonly Func<ExpressionOperatingState?>? operatingState;

    readonly Func<Task<ExpressionOperatingState?>>? refreshOperatingState;

    readonly System.Windows.Threading.DispatcherTimer stateTimer=new(){Interval=TimeSpan.FromSeconds(1)};

    TextBlock? stateLabel;

    bool readingState;

    static readonly Brush Ink=new SolidColorBrush(Color.FromRgb(234,240,247)),Panel=new SolidColorBrush(Color.FromRgb(31,42,55));

    public PatchSettingsWindow(Func<PresetDocument> read,Action<Action<PresetDocument>> edit,Catalog catalog,Func<bool> busy,Func<ExpressionOperatingState?>? operatingState=null,Func<Task<ExpressionOperatingState?>>? refreshOperatingState=null)

    {

        EditorWindow.StyleDialog(this);

        this.operatingState=operatingState;this.refreshOperatingState=refreshOperatingState;

        this.read=read;this.edit=edit;this.catalog=catalog;Title=UiText.T($"Patch Setting · {BankService.Position(read().Index)} · {read().Name}");Width=830;Height=535;MinWidth=760;MinHeight=470;Background=new SolidColorBrush(Color.FromRgb(16,21,28));Foreground=Ink;

        var root=new DockPanel{Margin=new Thickness(12)};var title=Label(Title,17);title.Margin=new Thickness(0,0,0,10);DockPanel.SetDock(title,Dock.Top);root.Children.Add(title);

        DockPanel.SetDock(status,Dock.Bottom);status.Margin=new Thickness(0,12,0,0);status.Text=UiText.T("Cambios directos al preset activo. Guardar en GP-200 conserva los cambios.");root.Children.Add(status);

        sections.Background=Panel;sections.Foreground=Ink;sections.BorderThickness=new Thickness(0);sections.Margin=new Thickness(0,0,12,0);DockPanel.SetDock(sections,Dock.Left);root.Children.Add(sections);

        root.Children.Add(new ScrollViewer{Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});Content=root;

        sections.SelectionChanged+=(_,_)=>Render();Closing+=(_,e)=>{if(busy()){e.Cancel=true;status.Text=UiText.T("Espera a que termine la edición pendiente.");}};Render();

        stateTimer.Tick+=async(_,_)=>

        {

            if(readingState||sections.SelectedIndex!=1||busy()||refreshOperatingState==null)return;

            readingState=true;try{UpdateStateLabel(await refreshOperatingState());}catch(Exception){if(stateLabel!=null)stateLabel.Text=UiText.T("Current State · lectura no disponible");}finally{readingState=false;}

        };

        Loaded+=(_,_)=>stateTimer.Start();Closed+=(_,_)=>stateTimer.Stop();

    }

    static TextBlock Label(string value,double size=12)=>new(){Text=value,FontSize=size,Foreground=Ink,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,3,6,4),VerticalAlignment=VerticalAlignment.Center};

    void Change(Action<PresetDocument> change,bool refresh=false)

    {

        try{edit(change);if(refresh)Render();status.Text=UiText.T("Cambio aplicado. El guardado permanente se realiza desde la pantalla principal.");}

        catch(Exception e){status.Text=e.Message;}

    }

    void Render()=>content.Content=sections.SelectedIndex switch{0=>Controllers(),1=>Expressions(),2=>Quick(),_=>Loop()};

    StackPanel Controllers()

    {

        var panel=new StackPanel();var tabs=new WrapPanel();panel.Children.Add(tabs);

        foreach(var ctrl in read().Controls)

        {

            var b=new Button{Content=UiText.T($"CTRL {ctrl.Slot+1}"),Padding=new Thickness(7,4,7,4),Margin=new Thickness(0,0,4,8),Background=ctrl.Slot==controller?Brushes.Teal:Panel,Foreground=Ink};

            b.Click+=(_,_)=>{controller=ctrl.Slot;Render();};tabs.Children.Add(b);

        }

        var assignment=read().Controls.First(c=>c.Slot==controller);var blocks=new WrapPanel();panel.Children.Add(blocks);

        for(int module=0;module<11;module++)

        {

            int captured=module;var check=new CheckBox{Content=catalog.IsMx42?$"{module+1:00} · {catalog.FamilyLabel(module,read().GetEffect(module).Code)}":PresetDocument.Modules[module],IsChecked=(assignment.Mask&(1u<<module))!=0,Foreground=Ink,FontSize=12,Width=88,Margin=new Thickness(4,8,4,8)};

            check.Click+=(_,_)=>Change(d=>{var old=d.Controls.Single(c=>c.Slot==controller).Mask;d.SetCtrl((byte)controller,(check.IsChecked==true?old|(1u<<captured):old&~(1u<<captured))&0x7ff);});blocks.Children.Add(check);

        }

        panel.Children.Add(Label(UiText.T("Las casillas eligen los bloques de la asignación CTRL; no son sus interruptores de activación.")));return panel;

    }

    StackPanel Expressions()

    {

        var panel=new StackPanel();var tabs=new WrapPanel();panel.Children.Add(tabs);

        stateLabel=Label(UiText.T(""),12);panel.Children.Add(stateLabel);UpdateStateLabel(operatingState?.Invoke());

        for(int i=0;i<2;i++){int tab=i;var b=new Button{Content=UiText.T($"EXP {i+1}"),Padding=new Thickness(14,4,14,4),Margin=new Thickness(0,0,6,6)};b.Click+=(_,_)=>{expression=tab;Render();};tabs.Children.Add(b);}

        foreach(int bank in expression==0?new[]{0,1}:new[]{2})

        {

            panel.Children.Add(Label(bank switch{0=>UiText.T("EXP 1 · Mode A"),1=>UiText.T("EXP 1 · Mode B"),_=>UiText.T("EXP 2")},14));

            var headings=new Grid();foreach(int w in new[]{44,270,90,90})headings.ColumnDefinitions.Add(new(){Width=new GridLength(w)});

            var low=Label(UiText.T("Min"),11);Grid.SetColumn(low,2);headings.Children.Add(low);var high=Label(UiText.T("Max"),11);Grid.SetColumn(high,3);headings.Children.Add(high);panel.Children.Add(headings);

            foreach(var assignment in read().Expression.Where(a=>a.Slot>>4==bank))

            {

                var row=new Grid{Margin=new Thickness(0,0,0,4),MinHeight=28};foreach(int width in new[]{44,270,90,90})row.ColumnDefinitions.Add(new(){Width=new GridLength(width)});

                row.Children.Add(Label(UiText.T($"Para {(assignment.Slot&15)+1}")));var destination=Destination(false,assignment);Grid.SetColumn(destination,1);row.Children.Add(destination);

                var min=Endpoint(assignment,false);Grid.SetColumn(min,2);row.Children.Add(min);var max=Endpoint(assignment,true);Grid.SetColumn(max,3);row.Children.Add(max);panel.Children.Add(row);

            }

        }

        return panel;

    }

    void UpdateStateLabel(ExpressionOperatingState? state)

    {

        if(stateLabel==null)return;

        stateLabel.Text=state==null?UiText.T("Current State · sin lectura del equipo"):UiText.T($"Current State · EXP {expression+1}: {(expression==0?state.InternalLabel:state.ExternalLabel)}");

        stateLabel.ToolTip=expression==0?UiText.T("Banco operativo del pedal interno; configurar Mode A/B no cambia este estado."):UiText.T("ON cuando EXP2/Footswitch está en modo Pedal; OFF en modo pulsador. No indica detección física del pedal.");

    }

    FrameworkElement Endpoint(Assignment a,bool maximum)

    {

        var panel=new StackPanel{Margin=new Thickness(6,0,0,0),VerticalAlignment=VerticalAlignment.Center};

        if(a.Module==255){panel.Children.Add(new TextBox{Text=UiText.T("OFF"),IsEnabled=false});return panel;}

        var info=a.Module<11?catalog.Find(a.Module,read().GetEffect(a.Module).Code)?.Parameters.FirstOrDefault(p=>p.Slot==a.Parameter):null;

        float value=maximum?a.End1:a.End0;

        void Apply(float changed)=>Change(d=>{var actual=d.Expression.Single(x=>x.Slot==a.Slot);d.SetExpression(a.Slot,maximum?changed:actual.End1,maximum?actual.End0:changed);});

        if(info?.Kind=="Switch"||info?.Menus.Count>0)

        {

            var choices=info.Kind=="Switch"?new[]{new ParameterMenu("OFF",0),new ParameterMenu("ON",1)}:info.Menus;

            var combo=new ComboBox{ItemsSource=choices,DisplayMemberPath="Name",SelectedItem=choices.FirstOrDefault(c=>c.Value==value)};

            combo.SelectionChanged+=(_,_)=>{if(combo.SelectedItem is ParameterMenu selected)Apply(selected.Value);};panel.Children.Add(combo);

        }

        else

        {

            var box=new TextBox{Text=value.ToString("R",CultureInfo.InvariantCulture)};bool dirty=false;box.TextChanged+=(_,_)=>dirty=true;

            void Commit(){if(!dirty)return;dirty=false;try{float val=float.Parse(box.Text.Replace(',','.'),CultureInfo.InvariantCulture);if(info!=null){var bounds=info.Bounds(read().GetEffect(a.Module));if(val<bounds.Minimum||val>bounds.Maximum)throw new ArgumentException(UiText.T($"Rango {bounds.Minimum}…{bounds.Maximum}"));}Apply(val);}catch(Exception e){status.Text=e.Message;}}

            box.KeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Enter){Commit();e.Handled=true;}};box.LostKeyboardFocus+=(_,_)=>Commit();panel.Children.Add(box);

        }

        return panel;

    }

    StackPanel Quick()

    {

        var panel=new StackPanel();panel.Children.Add(Label(UiText.T("Quick Access · tres mandos"),20));

        foreach(var a in read().Quick){panel.Children.Add(Label(UiText.T($"Para {a.Slot+1}")));panel.Children.Add(Destination(true,a));}return panel;

    }

    FrameworkElement Destination(bool quick,Assignment a)

    {

        string title="OFF";

        if(a.Module<11){var algorithm=catalog.Find(a.Module,read().GetEffect(a.Module).Code);title=$"{(catalog.IsMx42?$"{a.Module+1:00} · {catalog.FamilyLabel(a.Module,read().GetEffect(a.Module).Code)}":PresetDocument.Modules[a.Module])} · {algorithm?.Name??"Desconocido"} · {algorithm?.Parameters.FirstOrDefault(p=>p.Slot==a.Parameter)?.Name??UiText.T($"Parámetro {a.Parameter}")}";}

        else if(quick&&a.Module is 11 or 12&&a.Parameter is 0 or 255)title=a.Module==11?"Patch Volume":"Patch Tempo";

        else if(a.Module!=255)title=UiText.T($"Asignación conservada ({a.Module}/{a.Parameter})");

        var button=new Button{Content=new TextBlock{Text=title,TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center},ToolTip=title,Padding=new Thickness(7,4,7,4),HorizontalContentAlignment=HorizontalAlignment.Left,MaxWidth=430,Height=28,Margin=new Thickness(0)};

        var menu=EditorWindow.DarkContextMenu(this);var off=new MenuItem{Header=UiText.T("OFF")};off.Click+=(_,_)=>Change(d=>d.SetDestination(quick,a.Slot,255,0,catalog),true);menu.Items.Add(off);

        for(byte module=0;module<(catalog.IsMx42?11:quick?10:11);module++)

        {

            byte target=module;var algorithm=catalog.Find(module,read().GetEffect(module).Code);if(algorithm==null)continue;

            var group=new MenuItem{Header=catalog.IsMx42?$"{module+1:00} · {catalog.FamilyLabel(module,read().GetEffect(module).Code)}":PresetDocument.Modules[module]};var effect=new MenuItem{Header=algorithm.Name};group.Items.Add(effect);

            foreach(var p in algorithm.Parameters){int parameter=p.Slot;var item=new MenuItem{Header=p.Name,IsCheckable=true,IsChecked=a.Module==module&&a.Parameter==p.Slot};item.Click+=(_,_)=>Change(d=>d.SetDestination(quick,a.Slot,target,(ushort)parameter,catalog),true);effect.Items.Add(item);}menu.Items.Add(group);

        }

        if(quick)foreach(var special in new[]{(Module:(byte)11,Name:"Patch Volume"),(Module:(byte)12,Name:"Patch Tempo")})

        {

            var item=new MenuItem{Header=special.Name,IsCheckable=true,IsChecked=a.Module==special.Module&&a.Parameter is 0 or 255};

            item.Click+=(_,_)=>Change(d=>d.SetDestination(true,a.Slot,special.Module,255,catalog),true);menu.Items.Add(item);

        }

        button.Click+=(_,_)=>{menu.PlacementTarget=button;menu.IsOpen=true;};return button;

    }

    StackPanel Loop()

    {

        var panel=new StackPanel();panel.Children.Add(Label(UiText.T("FX Loop"),22));

        foreach(bool ret in new[]{false,true})

        {

            panel.Children.Add(Label(ret?UiText.T("Return"):UiText.T("Send")));var line=new DockPanel();var value=Label((ret?read().FxReturnLevel:read().FxSendLevel).ToString());DockPanel.SetDock(value,Dock.Right);line.Children.Add(value);

            var slider=new Slider{Minimum=0,Maximum=100,Value=ret?read().FxReturnLevel:read().FxSendLevel,IsSnapToTickEnabled=true,TickFrequency=1,Width=390,HorizontalAlignment=HorizontalAlignment.Left};CancellationTokenSource? pending=null;

            slider.ValueChanged+=async(_,e)=>{value.Text=((int)e.NewValue).ToString();pending?.Cancel();pending=new();var token=pending.Token;int next=(int)e.NewValue;try{await Task.Delay(45,token);if(!token.IsCancellationRequested)Change(d=>d.SetPatchOptions(sendLevel:ret?null:next,returnLevel:ret?next:null));}catch(OperationCanceledException){}};line.Children.Add(slider);panel.Children.Add(line);

        }

        panel.Children.Add(Label(UiText.T("Mode")));var mode=new ComboBox{ItemsSource=new[]{"Parallel","Series"}.Select(UiText.T).ToArray(),SelectedIndex=read().FxLoopMode,Width=240,HorizontalAlignment=HorizontalAlignment.Left};mode.SelectionChanged+=(_,_)=>Change(d=>d.SetPatchOptions(loopMode:mode.SelectedIndex));panel.Children.Add(mode);return panel;

    }

    internal void PreviewPages(string folder)

    {

        Directory.CreateDirectory(folder);var root=(FrameworkElement)Content;

        foreach(var page in new[]{(0,0,"ctrl"),(1,0,"exp1"),(1,1,"exp2"),(2,0,"quick"),(3,0,"fx-loop")})

        {

            expression=page.Item2;sections.SelectedIndex=page.Item1;Render();root.Measure(new Size(790,470));root.Arrange(new Rect(0,0,790,470));root.UpdateLayout();

            var bitmap=new RenderTargetBitmap(790,470,96,96,PixelFormats.Pbgra32);bitmap.Render(root);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(folder,page.Item3+".png"));encoder.Save(file);

        }

    }

}

