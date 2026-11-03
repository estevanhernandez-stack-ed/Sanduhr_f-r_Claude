using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DirectComposition;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace UnderIcons;

/// <summary>
/// THROWAWAY SPIKE, 2026-11-02 revisit of 5.0b: the piece drawn through a DirectComposition visual hosted on a
/// child of the WorkerW, the route 5.0b named and did not try. The surface is premultiplied BGRA: if per-pixel
/// alpha survives below SHELLDLL_DefView, the screen over the piece is a blend of the wallpaper and the ink.
/// </summary>
internal sealed class DCompPiece : IDisposable
{
    private ID3D11Device? _d3d;
    private ID3D11DeviceContext? _ctx;
    private IDCompositionDevice? _dev;
    private IDCompositionTarget? _target;
    private IDCompositionVisual? _visual;
    private IDCompositionSurface? _surface;

    /// <summary>Draws <paramref name="bgraPremultiplied"/> (w*h*4 bytes, top-down) into a DComp surface targeted at
    /// <paramref name="hwnd"/>. Returns null on success, or the step that failed.</summary>
    public string? Attach(IntPtr hwnd, int w, int h, byte[] bgraPremultiplied)
    {
        string step = "D3D11CreateDevice";
        try
        {
            D3D11.D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.BgraSupport, null, out _d3d, out _ctx).CheckError();
            step = "QueryInterface IDXGIDevice";
            using var dxgi = _d3d!.QueryInterface<IDXGIDevice>();
            step = "DCompositionCreateDevice";
            _dev = DComp.DCompositionCreateDevice<IDCompositionDevice>(dxgi);
            step = "CreateTargetForHwnd";
            _dev.CreateTargetForHwnd(hwnd, true, out _target).CheckError();
            step = "CreateVisual";
            _dev.CreateVisual(out _visual).CheckError();
            step = "CreateSurface";
            _dev.CreateSurface((uint)w, (uint)h, Format.B8G8R8A8_UNorm, AlphaMode.Premultiplied, out _surface).CheckError();
            step = "BeginDraw";
            var tex = _surface!.BeginDraw<ID3D11Texture2D>(null, out Int2 offset);
            try
            {
                step = "UpdateSubresource";
                var box = new Box(offset.X, offset.Y, 0, offset.X + w, offset.Y + h, 1);
                _ctx!.UpdateSubresource(bgraPremultiplied, tex, 0, (uint)(w * 4), 0, box);
            }
            finally { tex.Dispose(); }
            step = "EndDraw";
            _surface.EndDraw().CheckError();
            step = "SetContent";
            _visual!.SetContent(_surface).CheckError();
            step = "SetRoot";
            _target!.SetRoot(_visual).CheckError();
            step = "Commit";
            _dev.Commit().CheckError();
            return null;
        }
        catch (Exception e) { return $"{step}: {e.GetType().Name}: {e.Message}"; }
    }

    public void Dispose()
    {
        _surface?.Dispose(); _visual?.Dispose(); _target?.Dispose(); _dev?.Dispose(); _ctx?.Dispose(); _d3d?.Dispose();
        _surface = null; _visual = null; _target = null; _dev = null; _ctx = null; _d3d = null;
    }
}
