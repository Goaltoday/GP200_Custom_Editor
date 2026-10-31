using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
namespace Gp200.Replica;
public sealed partial class EditorWindow
{
    readonly ComboBox librarySource=new(){ItemsSource=new[]{UiText.T("Archivos locales"),UiText.T("GP-200 · 256 posiciones")},SelectedIndex=0};
    readonly ComboBox bankChoice=new(){ItemsSource=new[]{UiText.T("Todos los bancos")}.Concat(Enumerable.Range(1,64).Select(i=>UiText.T($"Banco {i:00}"))).ToArray(),SelectedIndex=0};
    readonly DockPanel algorithmHost=new(){LastChildFill=true};
    readonly ContentControl metadataHost=new();
    IReadOnlyList<PresetStyle> presetStyles=[];
    bool loadingLibrary;
    Action? commitPresetName;
    string algorithmFilter="";
    byte[]? copiedPreset;
    ScenePresetBundle? copiedScenes;
    string copiedPresetLabel="";
    LibraryEntry? menuTarget;
    LibraryEntry? pressedDevicePreset;
    async void CopyWorkspacePreset(LibraryEntry? source)
    {
        if(source==null||liveBusy||livePump||loadingLibrary)return;
        copiedScenes=null;
        copiedPreset=(source==liveEntry?desiredLive??source.Document:source.Document).ToBytes();
        copiedPresetLabel=source.ToString();status.Text=UiText.T("Copiado: ")+copiedPresetLabel+UiText.T(". Selecciona el destino y usa Pegar preset.");
        if(CatalogRuntime.Current.IsMx42&&source.DeviceSlot is int slot)
        {
            liveBusy=true;IsEnabled=false;
            try{status.Text="Copying preset and Scenes…";copiedScenes=ScenePresetBundle.Wrap(await new Mx42PresetTransactions("clipboard").ExportAsync(slot,true));status.Text="Copied preset and Scenes: "+copiedPresetLabel;}
            catch(Exception e){copiedPreset=null;status.Text=e.Message;UiDialogs.Show(this,e.Message,"Copy Scenes");}
            finally{liveBusy=false;IsEnabled=true;}
        }
    }
    async Task PasteWorkspacePreset(LibraryEntry? target)
    {
        if(target?.DeviceSlot is not int slot||copiedPreset==null){status.Text=UiText.T("Copia primero un preset y elige una posición de la GP-200.");return;}
        if(live==null){status.Text=UiText.T("Conecta la GP-200 antes de pegar.");return;}
        if(liveBusy||livePump||loadingLibrary)return;
        var document=new PresetDocument((byte[])copiedPreset.Clone());
        if(UiDialogs.Show(this,UiText.T($"Pegar {copiedPresetLabel} en {BankService.Position(slot)}? Se reemplazará el destino y se conservará una copia previa."),UiText.T("Pegar preset"),MessageBoxButton.OKCancel,MessageBoxImage.Question)!=MessageBoxResult.OK)return;
        liveBusy=true;IsEnabled=false;
        try
        {
            var service=new BankService();var saved=copiedScenes!=null?await service.ImportSceneAsync(slot,copiedScenes):await service.ReplaceAsync(slot,document);
            target.Original=saved.ToBytes();target.EditingBaseline=saved.ToBytes();target.Document=saved;target.Undo.Clear();target.Redo.Clear();
            if(liveEntry?.DeviceSlot==slot)
            {
                var actual=await service.SelectAsync(slot,reload:true);live.AcceptSnapshot(actual);liveEntry.Document=actual;
            }
            RefreshLibrary();Render();status.Text=UiText.T("Preset pegado y verificado en ")+BankService.Position(slot)+UiText.T(". Copia previa: ")+service.LastBackupDirectory;
        }
        catch(Exception error){status.Text=error.Message;UiDialogs.Show(this,error.Message,UiText.T("Pegar preset"));}
        finally{liveBusy=false;IsEnabled=true;}
    }
    IEnumerable<LibraryEntry> VisibleEntries()
    {
        var visible=entries.Where(e=>(librarySource.SelectedIndex==1?e.DeviceSlot.HasValue:!e.DeviceSlot.HasValue)&&(bankChoice.SelectedIndex<=0||(e.DeviceSlot??e.Document.Index)/4==bankChoice.SelectedIndex-1));
        return librarySource.SelectedIndex==1?visible.OrderBy(e=>e.DeviceSlot):visible;
    }
    UIElement BuildWorkspaceSidebar()
    {
        var panel=new DockPanel{Background=PanelBrush,LastChildFill=true};
        var top=new StackPanel{Margin=new Thickness(12)};DockPanel.SetDock(top,Dock.Top);panel.Children.Add(top);
        top.Children.Add(Text(UiText.T("PRESETS"),15,FontWeights.SemiBold));top.Children.Add(librarySource);top.Children.Add(bankChoice);
        librarySource.SelectionChanged+=(_,_)=>{if(!updating)RefreshLibrary();};bankChoice.SelectionChanged+=(_,_)=>RefreshLibrary();
        search.Margin=new Thickness(0,8,0,8);search.ToolTip=UiText.T("Buscar por nombre o posición");search.TextChanged+=(_,_)=>RefreshLibrary();top.Children.Add(search);
        var actions=new WrapPanel();
        actions.Children.Add(Button(UiText.T("←"),()=>NavigatePreset(-1)));actions.Children.Add(Button(UiText.T("→"),()=>NavigatePreset(1)));
        actions.Children.Add(Button(UiText.T("Guardar"),()=>_=SaveDevice()));actions.Children.Add(Button(UiText.Language=="es"?"Guardar como…":"Save As…",()=>_=SaveDeviceAs()));actions.Children.Add(Button(UiText.T("Importar…"),()=>_=ImportWorkspace()));
        actions.Children.Add(Button(UiText.T("Exportar…"),Export));actions.Children.Add(Button(UiText.T("Recargar"),()=>_=ReloadWorkspace()));top.Children.Add(actions);
        actions.Children.Add(Button(UiText.T("Copiar"),()=>CopyWorkspacePreset(library.SelectedItem as LibraryEntry??current)));
        actions.Children.Add(Button(UiText.T("Pegar…"),()=>_=PasteWorkspacePreset(library.SelectedItem as LibraryEntry??current)));
        foreach(var action in actions.Children.OfType<Button>()){action.Padding=new Thickness(8,7,8,7);action.Margin=new Thickness(0,0,5,5);action.FontSize=11;}
        metadataHost.Margin=new Thickness(12);DockPanel.SetDock(metadataHost,Dock.Bottom);panel.Children.Add(metadataHost);
        library.Background=PanelBrush;library.Foreground=TextBrush;library.BorderThickness=new Thickness(0);library.Padding=new Thickness(8);library.SelectionChanged+=(_,_)=>SelectEntry();
        var presetMenu=DarkContextMenu(this);var copy=new MenuItem{Header=UiText.T("Copiar preset")};var paste=new MenuItem{Header=UiText.T("Pegar preset aquí…")};var rename=new MenuItem{Header=UiText.T("Renombrar")};presetMenu.Items.Add(rename);presetMenu.Items.Add(new Separator());presetMenu.Items.Add(copy);presetMenu.Items.Add(paste);library.ContextMenu=presetMenu;
        library.PreviewMouseRightButtonDown+=(_,e)=>
        {
            menuTarget=(ItemsControl.ContainerFromElement(library,e.OriginalSource as DependencyObject) as ListBoxItem)?.Content as LibraryEntry;
            rename.IsEnabled=menuTarget!=null&&!liveBusy&&!loadingLibrary&&(!menuTarget.DeviceSlot.HasValue||live!=null);
            copy.IsEnabled=menuTarget!=null&&!liveBusy&&!loadingLibrary;paste.IsEnabled=menuTarget?.DeviceSlot!=null&&copiedPreset!=null&&live!=null&&!liveBusy&&!loadingLibrary;
        };
        library.ContextMenuOpening+=(_,e)=>
        {
            if(e.CursorLeft<0)menuTarget=library.SelectedItem as LibraryEntry;
            if(menuTarget==null){e.Handled=true;return;}
            rename.IsEnabled=menuTarget!=null&&!liveBusy&&!loadingLibrary&&(!menuTarget.DeviceSlot.HasValue||live!=null);
            copy.IsEnabled=!liveBusy&&!loadingLibrary;paste.IsEnabled=menuTarget!.DeviceSlot!=null&&copiedPreset!=null&&live!=null&&!liveBusy&&!loadingLibrary;
        };
        rename.Click+=async(_,_)=>await RenameWorkspacePreset(menuTarget);
        copy.Click+=(_,_)=>CopyWorkspacePreset(menuTarget);paste.Click+=async(_,_)=>await PasteWorkspacePreset(menuTarget);
        library.PreviewMouseLeftButtonDown+=(_,e)=>
        {
            pressedDevicePreset=null;
            if(ItemsControl.ContainerFromElement(library,e.OriginalSource as DependencyObject) is ListBoxItem item&&item.Content is LibraryEntry selected&&selected.DeviceSlot.HasValue)
            {pressedDevicePreset=selected;library.CaptureMouse();e.Handled=true;}
        };
        library.PreviewMouseLeftButtonUp+=(_,e)=>
        {
            var selected=pressedDevicePreset;pressedDevicePreset=null;
            if(selected!=null){library.ReleaseMouseCapture();e.Handled=true;_=SelectDevicePreset(selected);}
        };
        library.LostMouseCapture+=(_,_)=>pressedDevicePreset=null;
        library.PreviewKeyDown+=(_,e)=>
        {
            if(librarySource.SelectedIndex!=1||e.Key is not (System.Windows.Input.Key.Up or System.Windows.Input.Key.Down or System.Windows.Input.Key.Enter))return;
            e.Handled=true;if(liveBusy||livePump||loadingLibrary)return;
            var rows=library.Items.Cast<LibraryEntry>().ToArray();int at=Array.IndexOf(rows,current);
            if(e.Key!=System.Windows.Input.Key.Enter)at+=e.Key==System.Windows.Input.Key.Down?1:-1;
            if(at>=0&&at<rows.Length)_=SelectDevicePreset(rows[at]);
        };
        panel.Children.Add(library);return panel;
    }
    async Task ReloadWorkspace()
    {
        if(liveBusy||livePump||loadingLibrary)return;
        if(live==null){await ToggleLive();return;}
        try{await LoadDeviceLibrary(true);}catch(Exception e){status.Text=e.Message;}
    }
    async Task LoadDeviceLibrary(bool force=false)
    {
        if(loadingLibrary||!force&&entries.Count(e=>e.DeviceSlot.HasValue)==256)return;
        loadingLibrary=true;var service=new BankService();
        try
        {
            var state=await DeviceSession.Current.Read(ReadQuery.State);presetStyles=PresetStyles.Parse(state.Records);
            for(int slot=0;slot<256;slot++)
            {
                var doc=await service.ReadAsync(slot);var item=entries.FirstOrDefault(e=>e.DeviceSlot==slot);
                if(item==null)entries.Add(new LibraryEntry{Path=$"GP-200 · {BankService.Position(slot)}",Original=doc.ToBytes(),Document=doc,DeviceSnapshot=true,DeviceSlot=slot});
                else {item.Original=doc.ToBytes();if(item!=liveEntry)item.Document=doc;}
                if(slot%16==15){RefreshLibrary();status.Text=UiText.T($"Cargando biblioteca GP-200 · {slot+1}/256");}
            }
            librarySource.SelectedIndex=1;RefreshLibrary();Render();status.Text=UiText.T("Biblioteca GP-200 cargada · 64 bancos / 256 presets.");
        }finally{loadingLibrary=false;}
    }
    async Task SelectDevicePreset(LibraryEntry selected)
    {
        if(liveBusy||livePump||loadingLibrary){RefreshLibrary();return;}
        if(live==null){status.Text=UiText.T("Conecta la GP-200 para seleccionar esta posición.");RefreshLibrary();return;}
        int requestedSlot=selected.DeviceSlot!.Value;
        if(liveEntry?.DeviceSlot==requestedSlot){current=liveEntry;RefreshLibrary();Render();return;}
        var previous=liveEntry;liveBusy=true;IsEnabled=false;
        try
        {
            var service=new BankService();
            if(previous!=null&&previous.Dirty)
            {
                var answer=UiDialogs.Show(this,UiText.T($"Guardar los cambios de {BankService.Position(previous.Document.Index)} antes de cambiar de preset?\nSí: guardar · No: descartar · Cancelar: permanecer."),UiText.T("Cambios sin guardar"),MessageBoxButton.YesNoCancel,MessageBoxImage.Question);
                if(answer==MessageBoxResult.Cancel){current=previous;RefreshLibrary();return;}
                if(answer==MessageBoxResult.Yes){var saved=await service.SaveActiveAsync(previous.Document.Index);previous.ConfirmSceneStore();previous.Original=saved.ToBytes();}
            }
            var actual=await service.SelectAsync(requestedSlot);
            if(previous!=null&&previous!=selected){previous.Document=new PresetDocument(previous.Original);previous.EditingBaseline=null;previous.DiscardSceneChanges();}
            var savedDestination=await service.ReadAsync(selected.DeviceSlot.Value);selected.Original=savedDestination.ToBytes();
            var session=live!.ForSelectedPreset(actual);
            await session.RefreshExpressionStateAsync();
            if(session.Baseline.Index!=requestedSlot)throw new IOException(UiText.T($"Se solicitó {BankService.Position(requestedSlot)}, pero el equipo devolvió {BankService.Position(session.Baseline.Index)}. No se aplicará la respuesta a otra fila."));
            live=session;selected.Document=live.Baseline;selected.EditingBaseline=live.Baseline.ToBytes();selected.Undo.Clear();selected.Redo.Clear();current=liveEntry=selected;RefreshLibrary();Render();status.Text=UiText.T("Preset activo · cambios directos, guardar es una acción separada.");
        }
        catch(Exception e){status.Text=e.Message;RefreshLibrary();}
        finally{liveBusy=false;IsEnabled=true;}
    }
    void NavigatePreset(int delta)
    {
        if(current==null)return;
        if(current.DeviceSlot.HasValue){int slot=BankService.AdjacentSlot(current.DeviceSlot.Value,delta);var target=entries.FirstOrDefault(e=>e.DeviceSlot==slot);if(target!=null)_=SelectDevicePreset(target);}
        else{var local=VisibleEntries().ToList();int index=local.IndexOf(current)+delta;if(index>=0&&index<local.Count)library.SelectedItem=local[index];}
    }
    async Task ImportWorkspace()
    {
        if(liveBusy||livePump||loadingLibrary)return;
        if(current?.DeviceSlot is not int destination){Open();return;}
        var dialog=new OpenFileDialog{Filter=CatalogRuntime.Current.IsMx42?"GP-200 presets and Scenes|*.prst;*.gpscene":UiText.Filter("Presets GP-200|*.prst"),Multiselect=true};if(dialog.ShowDialog(this)!=true)return;
        try
        {
            var bundles=dialog.FileNames.Select(p=>Path.GetExtension(p).Equals(".gpscene",StringComparison.OrdinalIgnoreCase)?ScenePresetBundle.Load(p):null).ToArray();
            var docs=dialog.FileNames.Select((p,i)=>new PresetDocument(bundles[i]?.Preset??File.ReadAllBytes(p))).ToArray();
            if(destination+docs.Length>256)throw new InvalidOperationException(UiText.T("Los presets no caben desde el destino seleccionado."));
            if(UiDialogs.Show(this,UiText.T($"Importar {docs.Length} preset(s) desde {BankService.Position(destination)}? Se reemplazarán esas posiciones. Se conservará copia previa."),UiText.T("Importar presets"),MessageBoxButton.OKCancel)!=MessageBoxResult.OK)return;
            liveBusy=true;IsEnabled=false;var service=new BankService();
            if(bundles.Any(b=>b!=null))
            {for(int i=0;i<docs.Length;i++)if(bundles[i] is ScenePresetBundle bundle)await service.ImportSceneAsync(destination+i,bundle);else await service.ReplaceAsync(destination+i,docs[i]);}
            else await service.ReplaceManyAsync(destination,docs);
            for(int i=0;i<docs.Length;i++){var actual=await service.ReadAsync(destination+i);var item=entries.Single(e=>e.DeviceSlot==destination+i);item.Original=actual.ToBytes();item.EditingBaseline=actual.ToBytes();item.Document=actual;}
            // Import is an explicit replacement of this selected position.
            // Reload it even if firmware retained the prior working buffer.
            if(live!=null&&liveEntry?.DeviceSlot is int activeSlot&&activeSlot>=destination&&activeSlot<destination+docs.Length)
            {
                var actual=await service.SelectAsync(activeSlot,reload:true);live.AcceptSnapshot(actual);liveEntry.Document=actual;
            }
            RefreshLibrary();Render();status.Text=UiText.T("Importación leída y verificada. Copias previas: ")+service.LastBackupDirectory;
        }catch(Exception e){status.Text=e.Message;UiDialogs.Show(this,e.Message,UiText.T("Importar presets"));}
        finally{liveBusy=false;IsEnabled=true;}
    }
    void RenderWorkspacePanels()
    {
        if(current==null)return;var doc=current.Document;
        UpdateWorkspaceValues(doc);
        patchOverview.Children.Clear();patchOverview.Children.Add(Text(live?.ExpressionState is { } state?$"EXP 1: {state.InternalLabel}   EXP 2: {state.ExternalLabel}":"EXP 1 / EXP 2",11,FontWeights.Normal,Muted));
        var quickTitle=Text("Quick Access",12,FontWeights.SemiBold);quickTitle.Margin=new Thickness(0,16,0,10);patchOverview.Children.Add(quickTitle);
        foreach(var assignment in doc.Quick)
        {
            string target=assignment.Module==12?UiText.T("Patch Tempo"):assignment.Module==11?UiText.T("Patch Volume"):assignment.Module==255?"OFF":assignment.Module<11?EffectLabel(doc,assignment.Module)+" · "+(catalog.Find(assignment.Module,doc.GetEffect(assignment.Module).Code)?.Parameters.FirstOrDefault(p=>p.Slot==assignment.Parameter)?.Name??assignment.Parameter.ToString()):"OFF";
            var line=Text($"{assignment.Slot+1}  {target}",11);line.Margin=new Thickness(0,0,0,8);patchOverview.Children.Add(line);
        }
        ScrollViewer? ListScroll(DependencyObject node)
        {
            if(node is ScrollViewer viewer)return viewer;
            for(int i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(node);i++)
                if(ListScroll(System.Windows.Media.VisualTreeHelper.GetChild(node,i)) is { } found)return found;
            return null;
        }
        var previousList=algorithmHost.Children.OfType<ListBox>().FirstOrDefault(l=>Equals(l.Tag,$"algorithm-list-{selectedModule}"));
        double? previousOffset=previousList==null?null:ListScroll(previousList)?.VerticalOffset;
        algorithmHost.Children.Clear();AddMx42FamilySelector(doc);
        var filter=new TextBox{Text=algorithmFilter,Margin=new Thickness(0,10,0,8),ToolTip=UiText.T("Buscar algoritmo")};algorithmHost.Children.Add(filter);
        foreach(UIElement header in algorithmHost.Children)DockPanel.SetDock(header,Dock.Top);
        var list=new ListBox{Tag=$"algorithm-list-{selectedModule}",DisplayMemberPath="Name",Background=PanelBrush,Foreground=TextBrush,BorderThickness=new Thickness(0),MaxHeight=490,VerticalAlignment=VerticalAlignment.Top,FontSize=12};
        void Fill(){list.ItemsSource=InspectorAlgorithms(doc).Where(a=>a.Name.Contains(filter.Text,StringComparison.OrdinalIgnoreCase)).ToList();list.SelectedItem=catalog.Find(selectedModule,doc.GetEffect(selectedModule).Code);}
        int inspectorModule=selectedModule;Fill();filter.TextChanged+=(_,_)=>{algorithmFilter=filter.Text;Fill();};list.SelectionChanged+=(_,_)=>{if(ReferenceEquals(list.Parent,algorithmHost)&&selectedModule==inspectorModule&&list.SelectedItem is AlgorithmInfo choice&&current!=null){var editable=current==liveEntry&&desiredLive!=null?desiredLive:current.Document;if(choice.Code!=editable.GetEffect(inspectorModule).Code)Guard(()=>Edit(d=>d.SetAlgorithm(inspectorModule,choice)));}};algorithmHost.Children.Add(list);
        bool sameChoices=previousList!=null&&previousList.Items.Cast<AlgorithmInfo>().Select(a=>a.Code).SequenceEqual(list.Items.Cast<AlgorithmInfo>().Select(a=>a.Code));
        list.Loaded+=(_,_)=>list.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,new Action(()=>
        {
            if(!list.IsLoaded||!ReferenceEquals(list.Parent,algorithmHost))return;
            if(sameChoices&&previousOffset.HasValue&&ListScroll(list) is { } scroll){scroll.ScrollToVerticalOffset(previousOffset.Value);list.UpdateLayout();}
            if(list.SelectedItem!=null)list.ScrollIntoView(list.SelectedItem);
        }));
    }
    void OpenPatchSettings()
    {
        if(current==null)return;
        var entry=current;
        var session=live;
        var window=new PatchSettingsWindow(()=>desiredLive??entry.Document,action=>{if(current==entry)Edit(action);},catalog,()=>liveBusy||livePump,
            ()=>live==session?session?.ExpressionState:null,
            async()=>live==session&&session!=null?await session.RefreshExpressionStateAsync():null,
            ()=>status.Text){Owner=this};window.ShowDialog();Render();
    }
    async Task OpenPatchAfterSettings(){if(live==null&&ReadOnlyMidi.IsDeviceAvailable())await ToggleLive(true);if(current!=null)OpenPatchSettings();}
}
