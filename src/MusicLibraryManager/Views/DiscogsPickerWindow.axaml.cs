using Avalonia.Controls;
using Avalonia.Interactivity;
using MusicLibraryManager.Models;

namespace MusicLibraryManager.Views;

public partial class DiscogsPickerWindow : Window
{
    public DiscogsReleaseCandidate? Selected { get; private set; }

    public DiscogsPickerWindow()
    {
        InitializeComponent();
    }

    public DiscogsPickerWindow(IReadOnlyList<DiscogsReleaseCandidate> candidates) : this()
    {
        CandidateList.ItemsSource = candidates;
        if (candidates.Count > 0)
            CandidateList.SelectedIndex = 0;
    }

    private void OnAcceptClick(object? sender, RoutedEventArgs e)
    {
        Selected = CandidateList.SelectedItem as DiscogsReleaseCandidate;
        Close(Selected);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Selected = null;
        Close(null);
    }
}
