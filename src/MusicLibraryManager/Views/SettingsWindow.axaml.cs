using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace MusicLibraryManager.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
        KeyDown += OnKeyDown;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        try { FitToWorkingArea(); }
        catch { /* never block opening Settings */ }
    }

    /// <summary>
    /// Keep the whole dialog (title bar + Save/Close) inside the visible working area
    /// when the main window is maximized or fullscreen.
    /// </summary>
    private void FitToWorkingArea()
    {
        var screen = Screens?.ScreenFromVisual(this)
                     ?? Screens?.ScreenFromWindow(Owner as Window ?? this)
                     ?? Screens?.Primary;
        if (screen is null)
            return;

        var wa = screen.WorkingArea; // physical pixels
        var scale = screen.Scaling <= 0 ? 1.0 : screen.Scaling;

        var waX = wa.X / scale;
        var waY = wa.Y / scale;
        var waW = wa.Width / scale;
        var waH = wa.Height / scale;

        const double margin = 16;
        var maxW = Math.Max(480, waW - margin * 2);
        // Leave room for OS chrome; keep dialog comfortably inside the screen.
        var maxH = Math.Max(320, Math.Min(waH - margin * 2, waH * 0.9));

        MaxWidth = maxW;
        MaxHeight = maxH;

        if (Width > maxW) Width = maxW;
        if (Height > maxH) Height = maxH;

        double x;
        double y;
        if (Owner is Window owner && owner.IsVisible)
        {
            var ox = owner.Position.X / scale;
            var oy = owner.Position.Y / scale;
            x = ox + (owner.Bounds.Width - Width) / 2;
            y = oy + (owner.Bounds.Height - Height) / 2;
        }
        else
        {
            x = waX + (waW - Width) / 2;
            y = waY + (waH - Height) / 2;
        }

        // Clamp so title bar and bottom buttons stay on-screen.
        var minX = waX + margin;
        var minY = waY + margin;
        var maxX = waX + waW - Width - margin;
        var maxY = waY + waH - Height - margin;
        if (maxX < minX) maxX = minX;
        if (maxY < minY) maxY = minY;
        x = Math.Clamp(x, minX, maxX);
        y = Math.Clamp(y, minY, maxY);

        Position = new PixelPoint((int)Math.Round(x * scale), (int)Math.Round(y * scale));
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e) => Close();

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
