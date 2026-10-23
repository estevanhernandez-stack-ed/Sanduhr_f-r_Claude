namespace Sanduhr.Core;

/// <summary>A point in the Desk's DIPs.</summary>
public readonly record struct DeskPoint(double X, double Y);

/// <summary>
/// A piece's outline as drawn: its four corners, clockwise from the top left, after its stack's transform (the fold's slant, #314).
/// An upright piece's quad is its rectangle.
/// </summary>
public readonly record struct DeskQuad(DeskPoint TopLeft, DeskPoint TopRight, DeskPoint BottomRight, DeskPoint BottomLeft)
{
    public static DeskQuad Of(DeskRect r) => new(new(r.Left, r.Top), new(r.Right, r.Top), new(r.Right, r.Bottom), new(r.Left, r.Bottom));

    public IReadOnlyList<DeskPoint> Corners => [TopLeft, TopRight, BottomRight, BottomLeft];

    /// <summary>The smallest upright rectangle around it.</summary>
    public DeskRect Bounds
    {
        get
        {
            double l = Corners.Min(p => p.X), t = Corners.Min(p => p.Y), r = Corners.Max(p => p.X), b = Corners.Max(p => p.Y);
            return new DeskRect(l, t, r - l, b - t);
        }
    }

    /// <summary>True when the corners are those of an upright rectangle (no slant worth a polygon).</summary>
    public bool IsUpright =>
        Math.Abs(TopLeft.Y - TopRight.Y) < 0.5 && Math.Abs(BottomLeft.Y - BottomRight.Y) < 0.5
        && Math.Abs(TopLeft.X - BottomLeft.X) < 0.5 && Math.Abs(TopRight.X - BottomRight.X) < 0.5;
}

/// <summary>
/// The fold's geometry (spec 3.8 F, #314): a stack slanted by its tilt is a vertical shear (WPF's <c>SkewTransform</c> with
/// <c>AngleY</c> = tilt), so a piece's frame becomes a parallelogram whose left and right edges stay upright and whose top and
/// bottom rise or fall by <c>width * tan(tilt)</c>. Arrange Desk hit-tests and outlines that parallelogram, not the rectangle
/// around it. Pure.
/// </summary>
public static class DeskFold
{
    /// <summary>The quad <paramref name="r"/> becomes under a vertical shear of <paramref name="tiltDegrees"/> about its own top left
    /// (positive falls to the right, negative rises).</summary>
    public static DeskQuad Shear(DeskRect r, double tiltDegrees)
    {
        double dy = r.Width * Math.Tan(DeskArrangement.ClampTilt(tiltDegrees) * Math.PI / 180);
        return new DeskQuad(new(r.Left, r.Top), new(r.Right, r.Top + dy), new(r.Right, r.Bottom + dy), new(r.Left, r.Bottom));
    }

    /// <summary>The quad grown by <paramref name="pad"/> on every side: each corner moves out along both axes, which keeps the
    /// slant of a sheared quad (its top and bottom move up and down together).</summary>
    public static DeskQuad Grow(DeskQuad q, double pad) => new(
        new(q.TopLeft.X - pad, q.TopLeft.Y - pad),
        new(q.TopRight.X + pad, q.TopRight.Y - pad),
        new(q.BottomRight.X + pad, q.BottomRight.Y + pad),
        new(q.BottomLeft.X - pad, q.BottomLeft.Y + pad));

    /// <summary>True when (<paramref name="x"/>, <paramref name="y"/>) is inside the convex quad or on its edge. A degenerate quad
    /// (no area) holds nothing.</summary>
    public static bool Contains(DeskQuad q, double x, double y)
    {
        var c = q.Corners;
        int sign = 0;
        for (int i = 0; i < 4; i++)
        {
            var a = c[i];
            var b = c[(i + 1) % 4];
            double cross = (b.X - a.X) * (y - a.Y) - (b.Y - a.Y) * (x - a.X);
            if (Math.Abs(cross) < 1e-9) continue;
            int s = cross > 0 ? 1 : -1;
            if (sign == 0) sign = s;
            else if (s != sign) return false;
        }
        return sign != 0 || Area(q) > 0;
    }

    /// <summary>The quad's area (shoelace), always positive.</summary>
    public static double Area(DeskQuad q)
    {
        var c = q.Corners;
        double sum = 0;
        for (int i = 0; i < 4; i++)
            sum += c[i].X * c[(i + 1) % 4].Y - c[(i + 1) % 4].X * c[i].Y;
        return Math.Abs(sum) / 2;
    }
}
