using System.Windows.Input;
using EmuDOS.Core.Input;

namespace EmuDOS.Input;

/// <summary>Translates WPF <see cref="Key"/> values to engine-neutral <see cref="DosKey"/>.</summary>
public static class KeyMap
{
    public static DosKey ToDosKey(Key key)
    {
        if (key is >= Key.A and <= Key.Z)
            return (DosKey)((int)DosKey.A + (key - Key.A));
        if (key is >= Key.D0 and <= Key.D9)
            return (DosKey)((int)DosKey.D0 + (key - Key.D0));
        if (key is >= Key.NumPad0 and <= Key.NumPad9)
            return (DosKey)((int)DosKey.Keypad0 + (key - Key.NumPad0));
        if (key is >= Key.F1 and <= Key.F12)
            return (DosKey)((int)DosKey.F1 + (key - Key.F1));

        return key switch
        {
            Key.Space => DosKey.Space,
            Key.Enter => DosKey.Enter,
            Key.Escape => DosKey.Escape,
            Key.Back => DosKey.Backspace,
            Key.Tab => DosKey.Tab,
            Key.Left => DosKey.Left,
            Key.Right => DosKey.Right,
            Key.Up => DosKey.Up,
            Key.Down => DosKey.Down,
            Key.LeftShift => DosKey.LeftShift,
            Key.RightShift => DosKey.RightShift,
            Key.LeftCtrl => DosKey.LeftCtrl,
            Key.RightCtrl => DosKey.RightCtrl,
            Key.LeftAlt => DosKey.LeftAlt,
            Key.RightAlt => DosKey.RightAlt,
            Key.OemMinus => DosKey.Minus,
            Key.OemPlus => DosKey.Equals,
            Key.OemComma => DosKey.Comma,
            Key.OemPeriod => DosKey.Period,
            Key.Oem1 => DosKey.Semicolon,
            Key.Oem2 => DosKey.Slash,
            Key.Oem3 => DosKey.Backquote,
            Key.Oem4 => DosKey.LeftBracket,
            Key.Oem5 => DosKey.Backslash,
            Key.Oem6 => DosKey.RightBracket,
            Key.Oem7 => DosKey.Apostrophe,
            Key.Insert => DosKey.Insert,
            Key.Delete => DosKey.Delete,
            Key.Home => DosKey.Home,
            Key.End => DosKey.End,
            Key.PageUp => DosKey.PageUp,
            Key.PageDown => DosKey.PageDown,
            Key.CapsLock => DosKey.CapsLock,
            Key.NumLock => DosKey.NumLock,
            _ => DosKey.None,
        };
    }
}
