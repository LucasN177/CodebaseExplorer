using CommunityToolkit.Mvvm.ComponentModel;

namespace CodebaseExplorer.Desktop.ViewModels;

/// <summary>Base class for all ViewModels. ViewModels must not reference Avalonia controls.</summary>
public abstract class ViewModelBase : ObservableObject;
