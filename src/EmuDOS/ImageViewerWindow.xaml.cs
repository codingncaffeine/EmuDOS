using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace EmuDOS;

/// <summary>A simple full-image viewer that flips through a set of images (extras gallery): arrow
/// keys or the on-screen ‹ › buttons, Esc to close.</summary>
public partial class ImageViewerWindow : Window
{
    private readonly IReadOnlyList<string> _paths;
    private int _index;

    public ImageViewerWindow(IReadOnlyList<string> paths, int index)
    {
        InitializeComponent();
        DarkChrome.Apply(this);
        WindowIcon.Apply(this);
        _paths = paths;
        ShowImage(index);
    }

    private void ShowImage(int i)
    {
        if (_paths.Count == 0)
            return;
        _index = (i % _paths.Count + _paths.Count) % _paths.Count;
        var path = _paths[_index];
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            Pic.Source = bmp;
        }
        catch { Pic.Source = null; }
        Caption.Text = $"{Path.GetFileNameWithoutExtension(path)}   ({_index + 1}/{_paths.Count})";
    }

    private void OnPrev(object sender, RoutedEventArgs e) => ShowImage(_index - 1);
    private void OnNext(object sender, RoutedEventArgs e) => ShowImage(_index + 1);

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Left: ShowImage(_index - 1); break;
            case Key.Right: ShowImage(_index + 1); break;
            case Key.Escape: Close(); break;
        }
    }
}
