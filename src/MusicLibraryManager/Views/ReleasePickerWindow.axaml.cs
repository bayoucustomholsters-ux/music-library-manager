using Avalonia.Controls;
using Avalonia.Interactivity;
using MusicLibraryManager.Models;

namespace MusicLibraryManager.Views;

public partial class ReleasePickerWindow : Window
{
    public MusicBrainzReleaseCandidate? Selected { get; private set; }

    public ReleasePickerWindow()
    {
        InitializeComponent();
    }

    public ReleasePickerWindow(IReadOnlyList<MusicBrainzReleaseCandidate> candidates) : this()
    {
        CandidateList.ItemsSource = candidates;
        if (candidates.Count > 0)
            CandidateList.SelectedIndex = 0;
    }

    private void OnAcceptClick(object? sender, RoutedEventArgs e)
    {
        Selected = CandidateList.SelectedItem as MusicBrainzReleaseCandidate;
        Close(Selected);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Selected = null;
        Close(null);
    }
}
