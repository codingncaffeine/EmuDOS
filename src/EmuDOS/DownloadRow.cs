using System;
using System.Threading.Tasks;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using EmuDOS.Core.Downloads;

namespace EmuDOS;

/// <summary>One row in the Downloads tab: an on-demand asset, or a custom download (its own action).</summary>
public sealed partial class DownloadRow : ObservableObject
{
    private static readonly Brush Green = Frozen(0x9F, 0xE0, 0xA0);
    private static readonly Brush Tan = Frozen(0xA8, 0x9A, 0x86);
    private static readonly Brush Red = Frozen(0xE0, 0x85, 0x85);

    private readonly string _name = "";
    private readonly string _description = "";

    public DownloadRow(DownloadAsset asset, bool installed)
    {
        Asset = asset;
        _status = installed ? "Installed" : "Not installed";
        _statusBrush = installed ? Green : Tan;
        _actionText = installed ? "Update" : "Download";
    }

    /// <summary>A row backed by a custom download action rather than a single-file asset.</summary>
    public DownloadRow(string name, string description, bool installed, Func<Action<string>, Task> customDownload)
    {
        _name = name;
        _description = description;
        CustomDownload = customDownload;
        _status = installed ? "Installed" : "Not installed";
        _statusBrush = installed ? Green : Tan;
        _actionText = installed ? "Re-download" : "Download";
    }

    public DownloadAsset? Asset { get; }

    /// <summary>For custom rows: runs the download, given a progress-text reporter. Null for assets.</summary>
    public Func<Action<string>, Task>? CustomDownload { get; }

    public string Name => Asset?.DisplayName ?? _name;

    public string Description => Asset?.Description ?? _description;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDownload))]
    private bool _isBusy;

    [ObservableProperty]
    private string _status;

    [ObservableProperty]
    private Brush _statusBrush;

    [ObservableProperty]
    private string _actionText;

    public bool CanDownload => !IsBusy;

    public void SetProgress(string text)
    {
        Status = text;
        StatusBrush = Tan;
    }

    public void SetResult(bool ok, string? error)
    {
        Status = ok ? "Installed" : $"Failed: {error}";
        StatusBrush = ok ? Green : Red;
        ActionText = "Update";
    }

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
