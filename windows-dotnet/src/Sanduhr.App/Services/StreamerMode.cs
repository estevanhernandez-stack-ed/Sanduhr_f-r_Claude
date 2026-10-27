using Sanduhr.Core;

namespace Sanduhr.App.Services;

/// <summary>
/// The mask every surface reads (Settings v2, slice 7). <see cref="StreamerService"/> is the only writer; it raises its own
/// <c>Changed</c>, so nothing subscribes to a static event. Off until the widget's service loads. Tests reset it (AppWindowFixture).
/// </summary>
public static class StreamerMode
{
    public static StreamerMask Mask { get; private set; } = StreamerMask.Off;

    public static bool On => Mask.On;

    internal static void Use(StreamerMask mask) => Mask = mask;

    internal static void Reset() => Mask = StreamerMask.Off;
}
