using CodebaseExplorer.Application.Analysis;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CodebaseExplorer.Desktop.ViewModels.Pages;

/// <summary>State of one analysis stage in the import progress panel.</summary>
public enum ImportStageState
{
    Pending,
    Active,
    Done,
}

/// <summary>One row of the import progress panel (USER_WORKFLOWS.md WF-01 step 4).</summary>
public sealed partial class ImportStageViewModel(AnalysisStage stage, string name) : ViewModelBase
{
    public AnalysisStage Stage { get; } = stage;

    public string Name { get; } = name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Glyph), nameof(IsActive))]
    public partial ImportStageState State { get; set; }

    public bool IsActive => State == ImportStageState.Active;

    public string Glyph => State switch
    {
        ImportStageState.Done => "●",
        ImportStageState.Active => "◐",
        _ => "○",
    };
}
