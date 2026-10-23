namespace Sanduhr.Core;

/// <summary>Where the roaming companion stands: its bottom-centre point, which way it faces, and the edge it is on.</summary>
public readonly record struct CompanionSpot(double X, double Y, bool FacingLeft, CompanionEdge Edge);

/// <summary>
/// The route a roaming companion walks (pure geometry, in the screen's DIPs). The edges a pack names make one route:
/// up the left edge's foot, along the bottom, up the right edge, and it walks there and back. The sprite stays upright
/// and hugs the edge it is on (its bottom-centre point is half a sprite in from a side edge). Without the bottom, the
/// two side edges are walked in turn and the companion hops from the foot of one to the foot of the other.
/// </summary>
public sealed class CompanionPath
{
    private readonly List<(double X0, double Y0, double X1, double Y1, CompanionEdge Edge, double Length)> _segments = [];

    /// <summary>The route's length one way, in DIPs.</summary>
    public double Length { get; private set; }

    private CompanionPath() { }

    /// <summary>The route along <paramref name="edges"/> of <paramref name="area"/> (the monitor less its taskbar) for a sprite
    /// <paramref name="spriteWidth"/> by <paramref name="spriteHeight"/>. Null when the area cannot hold the sprite or no edge is named.</summary>
    public static CompanionPath? Build(IReadOnlyCollection<CompanionEdge> edges, DeskRect area, double spriteWidth, double spriteHeight)
    {
        if (edges.Count == 0 || !(spriteWidth > 0) || !(spriteHeight > 0)) return null;
        double xl = area.Left + spriteWidth / 2, xr = area.Right - spriteWidth / 2;
        double yb = area.Bottom, yt = area.Top + spriteHeight;
        if (xr - xl < 1 || yb - yt < 1) return null;
        var path = new CompanionPath();
        if (edges.Contains(CompanionEdge.Left)) path.Add(xl, yt, xl, yb, CompanionEdge.Left);
        if (edges.Contains(CompanionEdge.Bottom)) path.Add(xl, yb, xr, yb, CompanionEdge.Bottom);
        if (edges.Contains(CompanionEdge.Right)) path.Add(xr, yb, xr, yt, CompanionEdge.Right);
        return path.Length > 0 ? path : null;
    }

    private void Add(double x0, double y0, double x1, double y1, CompanionEdge edge)
    {
        double len = Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
        _segments.Add((x0, y0, x1, y1, edge, len));
        Length += len;
    }

    /// <summary>The spot after walking <paramref name="distance"/> DIPs from the route's start: out to the far end, then back,
    /// and so on. Facing is the way it walks along the bottom; on a side edge it faces into the screen.</summary>
    public CompanionSpot At(double distance)
    {
        if (!double.IsFinite(distance) || distance < 0) distance = 0;
        double lap = 2 * Length;
        double d = distance % lap;
        bool back = d > Length;
        double along = back ? lap - d : d;
        for (int i = 0; i < _segments.Count; i++)
        {
            var s = _segments[i];
            if (along <= s.Length || i == _segments.Count - 1)
            {
                double f = s.Length <= 0 ? 0 : Math.Clamp(along / s.Length, 0, 1);
                double x = s.X0 + (s.X1 - s.X0) * f, y = s.Y0 + (s.Y1 - s.Y0) * f;
                bool left = s.Edge switch
                {
                    CompanionEdge.Left => false,
                    CompanionEdge.Right => true,
                    _ => back,
                };
                return new CompanionSpot(x, y, left, s.Edge);
            }
            along -= s.Length;
        }
        throw new InvalidOperationException("a path has at least one segment");
    }

    /// <summary>How far along the route (going out) the point nearest <paramref name="x"/>, <paramref name="y"/> lies: where a
    /// companion placed at an anchor starts walking from.</summary>
    public double Nearest(double x, double y)
    {
        double best = double.MaxValue, at = 0, walked = 0;
        foreach (var s in _segments)
        {
            double dx = s.X1 - s.X0, dy = s.Y1 - s.Y0;
            double t = s.Length <= 0 ? 0 : Math.Clamp(((x - s.X0) * dx + (y - s.Y0) * dy) / (s.Length * s.Length), 0, 1);
            double px = s.X0 + dx * t, py = s.Y0 + dy * t;
            double dist = (px - x) * (px - x) + (py - y) * (py - y);
            if (dist < best)
            {
                best = dist;
                at = walked + t * s.Length;
            }
            walked += s.Length;
        }
        return at;
    }
}

/// <summary>
/// The roaming clock (pure: time comes in as steps). The companion walks for <see cref="WalkFor"/>, then stops for
/// <see cref="RestFor"/> and shows its mood, and so on. The clock only moves while walking is allowed: asleep
/// (exhausted), while a trigger plays, or covered, it holds still, and walks on from the same spot afterwards.
/// </summary>
public sealed class CompanionRoamer
{
    public static readonly TimeSpan WalkFor = TimeSpan.FromSeconds(12);
    public static readonly TimeSpan RestFor = TimeSpan.FromSeconds(8);

    private double _phase;

    /// <summary>Seconds spent walking so far.</summary>
    public double WalkedSeconds { get; private set; }

    /// <summary>The distance walked at <paramref name="speed"/> DIPs a second, plus where it started.</summary>
    public double Distance(double speed) => Start + WalkedSeconds * speed;

    /// <summary>Where along the route it started (<see cref="CompanionPath.Nearest"/> of its anchor).</summary>
    public double Start { get; set; }

    /// <summary>True in the walking part of the cycle.</summary>
    public bool Walking => _phase < WalkFor.TotalSeconds;

    /// <summary>Moves the clock on by <paramref name="step"/> when <paramref name="allowed"/>; holds it otherwise.</summary>
    public void Advance(TimeSpan step, bool allowed)
    {
        if (!allowed || step <= TimeSpan.Zero) return;
        // A long gap (the machine slept, the Desk was covered) is not a sprint across the screen.
        double dt = Math.Min(step.TotalSeconds, 1.0);
        double cycle = WalkFor.TotalSeconds + RestFor.TotalSeconds;
        if (Walking) WalkedSeconds += Math.Min(dt, WalkFor.TotalSeconds - _phase);
        _phase = (_phase + dt) % cycle;
    }
}
