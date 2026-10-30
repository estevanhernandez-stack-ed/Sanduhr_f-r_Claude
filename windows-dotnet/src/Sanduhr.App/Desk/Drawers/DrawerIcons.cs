using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media;
using System.Windows.Threading;
using Sanduhr.App.Interop;

namespace Sanduhr.App.Desk;

/// <summary>Thumbnails and icons for drawer items, made on one background STA thread (the Shell's image factory can take a while on a big
/// image) and cached by path and write time. A frozen image is handed to the UI thread; nothing blocks a redraw.</summary>
internal sealed class DrawerIcons : IDisposable
{
    public const int Pixels = 64;
    private const int MaxCached = 2000;
    private readonly BlockingCollection<DrawerItem> _queue = new();
    private readonly ConcurrentDictionary<string, ImageSource> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dispatcher _ui;

    public DrawerIcons(Dispatcher ui)
    {
        _ui = ui;
        var worker = new Thread(Work) { IsBackground = true, Name = "Drawer icons" };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
    }

    public void Request(DrawerItem item)
    {
        if (_cache.TryGetValue(Key(item.Path), out var hit)) { item.Icon = hit; return; }
        if (!_queue.IsAddingCompleted) _queue.TryAdd(item);
    }

    private void Work()
    {
        foreach (var item in _queue.GetConsumingEnumerable())
        {
            if (ShellFiles.Thumbnail(item.Path, Pixels) is not { } image) continue;
            if (_cache.Count < MaxCached) _cache[Key(item.Path)] = image;
            _ui.BeginInvoke(() => item.Icon = image);
        }
    }

    private static string Key(string path) => path + "|" + (File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks : 0);

    public void Dispose() => _queue.CompleteAdding();
}
