using System.Windows;
using System.Windows.Controls;
namespace Gp200.Replica;
public sealed partial class EditorWindow
{
    bool workspaceRefreshing;
    Slider[]? summarySliders;
    TextBlock[] summaryLabels=[];
    TextBox? workspaceName,workspaceAuthor,workspaceNotes;
    ComboBox? workspaceStyle;
    LibraryEntry? workspaceEntry;
    string? workspaceLanguage;
    void UpdateWorkspaceValues(PresetDocument doc)
    {
        workspaceRefreshing=true;
        try
        {
            if(workspaceLanguage!=UiText.Language){summarySliders=null;workspaceLanguage=UiText.Language;}
            if(summarySliders==null)
            {
                var summary=new StackPanel{Margin=new Thickness(0,3,0,0)};
                summarySliders=new Slider[3];summaryLabels=new TextBlock[3];
                for(int i=0;i<3;i++)
                {
                    int index=i;string label=new[]{"VOL","PAN","BPM"}[i];
                    var group=new StackPanel{Margin=new Thickness(0,0,0,12)};
                    var text=summaryLabels[i]=Text("",12);group.Children.Add(text);
                    var slider=summarySliders[i]=new Slider{Minimum=i==1?-100:i==2?40:0,Maximum=i==2?250:100,IsSnapToTickEnabled=true,TickFrequency=1,Margin=new Thickness(0,4,0,0)};
                    slider.ValueChanged+=(_,e)=>text.Text=$"{label} · {(int)e.NewValue}";
                    AddDelayedSlider(slider,n=>Edit(d=>{if(index==1)d.SetPatchOptions(pan:(int)n);else d.SetGeneral(d.Name,index==0?(int)n:d.Volume,index==2?(int)n:d.Tempo);}),true,()=>workspaceRefreshing);
                    group.Children.Add(slider);summary.Children.Add(group);
                }
                presetSummary.Content=summary;
                var info=new StackPanel();info.Children.Add(Text(UiText.T("EDIT INFO"),12,FontWeights.SemiBold,Muted));
                workspaceName=new TextBox{MaxLength=16,Tag="preset-name"};info.Children.Add(Text(UiText.T("Nombre"),12));info.Children.Add(workspaceName);
                workspaceAuthor=new TextBox{MaxLength=16};workspaceNotes=new TextBox{MaxLength=40};
                info.Children.Add(Text(UiText.T("Autor"),12));info.Children.Add(workspaceAuthor);
                workspaceStyle=new ComboBox();info.Children.Add(Text(UiText.T("Estilo"),12));info.Children.Add(workspaceStyle);info.Children.Add(Text(UiText.T("Notas"),12));info.Children.Add(workspaceNotes);
                commitPresetName=()=>Edit(d=>d.SetGeneral(workspaceName.Text,d.Volume,d.Tempo));
                AutoText(workspaceName,()=>commitPresetName(),true,()=>workspaceRefreshing);
                AutoText(workspaceAuthor,()=>Edit(d=>d.SetMetadata(workspaceAuthor.Text,d.Notes)),true,()=>workspaceRefreshing);
                AutoText(workspaceNotes,()=>Edit(d=>d.SetMetadata(d.Author,workspaceNotes.Text)),true,()=>workspaceRefreshing);
                workspaceStyle.SelectionChanged+=(_,_)=>{if(!workspaceRefreshing&&workspaceStyle.SelectedItem is PresetStyle s)Guard(()=>Edit(d=>d.SetPatchStyle(s.Index)));};
                metadataHost.Content=info;
            }
            double[] values=[doc.Volume,doc.Pan,doc.Tempo];
            for(int i=0;i<3;i++){summarySliders[i].Value=values[i];summaryLabels[i].Text=$"{new[]{"VOL","PAN","BPM"}[i]} · {values[i]}";}
            bool changedEntry=workspaceEntry!=current;workspaceEntry=current;
            if(changedEntry||!workspaceName!.IsKeyboardFocusWithin)workspaceName!.Text=doc.Name;
            if(changedEntry||!workspaceAuthor!.IsKeyboardFocusWithin)workspaceAuthor!.Text=doc.Author;
            if(changedEntry||!workspaceNotes!.IsKeyboardFocusWithin)workspaceNotes!.Text=doc.Notes;
            var styles=presetStyles.Count==20?presetStyles:Enumerable.Range(0,20).Select(i=>new PresetStyle(i,UiText.T($"Tipo {i+1:00}"))).ToArray();
            if(workspaceStyle!.ItemsSource is not IEnumerable<PresetStyle> old||!old.SequenceEqual(styles))workspaceStyle.ItemsSource=styles;
            workspaceStyle.SelectedItem=styles.FirstOrDefault(s=>s.Index==doc.PatchStyle);
        }
        finally{workspaceRefreshing=false;}
    }
}
