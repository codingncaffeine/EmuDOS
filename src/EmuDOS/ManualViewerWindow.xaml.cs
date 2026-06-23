using System;
using System.Diagnostics;
using System.Windows;

namespace EmuDOS;

/// <summary>
/// Reads a game's manual (PDF) in-app via the Edge WebView2 PDF viewer. Falls back to the system
/// PDF handler if the WebView2 runtime isn't available.
/// </summary>
public partial class ManualViewerWindow : Window
{
    private readonly string _pdfPath;

    public ManualViewerWindow(string pdfPath, string title)
    {
        InitializeComponent();
        DarkChrome.Apply(this);
        WindowIcon.Apply(this);
        _pdfPath = pdfPath;
        Title = $"Manual — {title}";
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await Web.EnsureCoreWebView2Async();
            // Edge renders PDFs with its built-in viewer (zoom, search, page nav).
            Web.CoreWebView2.Navigate(new Uri(_pdfPath).AbsoluteUri);
        }
        catch
        {
            // No WebView2 runtime — open in the system PDF viewer instead and close.
            try { Process.Start(new ProcessStartInfo(_pdfPath) { UseShellExecute = true }); }
            catch { /* nothing else we can do */ }
            Close();
        }
    }
}
