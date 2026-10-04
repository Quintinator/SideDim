using System.Runtime.InteropServices;

namespace SideDim;

internal sealed class HotkeyWindow : NativeWindow, IDisposable
{
    private const int Id = 1;
    private bool _registered;

    public event Action? Pressed;

    public HotkeyWindow() => CreateHandle(new CreateParams());

    public bool Register(Hotkey hotkey)
    {
        Unregister();
        _registered = Native.RegisterHotKey(Handle, Id, (uint)hotkey.Modifiers | Native.MOD_NOREPEAT, (uint)hotkey.Key);
        if (!_registered) Log.Write($"RegisterHotKey({hotkey}) failed, error {Marshal.GetLastPInvokeError()}");
        return _registered;
    }

    public void Unregister()
    {
        if (_registered) Native.UnregisterHotKey(Handle, Id);
        _registered = false;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_HOTKEY && m.WParam == Id) Pressed?.Invoke();
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        Unregister();
        DestroyHandle();
    }
}

internal static class AppIcon
{
    public static Icon Load(Size size)
    {
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream("SideDim.ico")
            ?? throw new InvalidOperationException("SideDim.ico is missing from the assembly resources");
        return new Icon(stream, size);
    }
}
