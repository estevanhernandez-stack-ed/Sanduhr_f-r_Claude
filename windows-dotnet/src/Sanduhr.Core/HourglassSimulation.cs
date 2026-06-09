namespace Sanduhr.Core;

/// <summary>
/// The falling-sand cellular automaton that drives the focus-timer hourglass —
/// ported <b>1:1</b> from <c>focus.py</c>'s physics (<c>_init_physics</c> +
/// <c>_physics_tick</c>). This is the <b>cert-load-bearing model</b> (MS Store
/// 10.1.4.4 "unique lasting value"): the rendering lives in the App's
/// <c>HourglassControl</c>, but the physics stays here, pure and unit-tested, so
/// the invariant that earns the cert is verifiable independent of WPF.
///
/// <para><b>Do not "improve" the model.</b> Only the view was redesigned (the
/// un-fogging); the grid, bowtie mask, fall rules, and — critically — the
/// wall-clock throttle are a verbatim port.</para>
///
/// <para><b>The load-bearing invariant (the throttle):</b> grains crossing the
/// throat (<c>y == CenterY - 1</c>) are rate-limited so <see cref="SandPassed"/>
/// chases <c>expected = (elapsedMs / durationMs) * TotalSand</c>, compared as a
/// <b>float, not truncated</b> — so sand drains in exact proportion to elapsed
/// time and can never outpace the clock. The throttle gates the center column
/// AND both diagonal entries into the bottom half (the Python fix: diagonals
/// must not bypass the rate limit).</para>
/// </summary>
public sealed class HourglassSimulation
{
    private readonly Random _rng;
    private readonly bool[,] _mask;
    private readonly bool[,] _grid;

    /// <summary>Grid width in cells (31, matching <c>focus.py</c>).</summary>
    public int Width { get; }

    /// <summary>Grid height in cells (31, matching <c>focus.py</c>).</summary>
    public int Height { get; }

    /// <summary>Center column index (<c>Width / 2</c>).</summary>
    public int CenterX { get; }

    /// <summary>Center row index (<c>Height / 2</c>) — the throat.</summary>
    public int CenterY { get; }

    /// <summary>Total grains spawned in the top half at reset — the denominator
    /// the throttle scales against.</summary>
    public int TotalSand { get; private set; }

    /// <summary>Grains that have crossed the throat into the bottom half. Chases
    /// the wall-clock <c>expected</c> value; never outpaces it.</summary>
    public int SandPassed { get; private set; }

    /// <param name="rng">Injectable RNG so the diagonal-bias choice is
    /// deterministic under test. Defaults to a shared instance.</param>
    /// <param name="size">Grid edge length in cells; defaults to 31 (the locked
    /// value). Exposed only so tests can shrink the grid if needed.</param>
    public HourglassSimulation(Random? rng = null, int size = 31)
    {
        _rng = rng ?? Random.Shared;
        Width = size;
        Height = size;
        CenterX = size / 2;
        CenterY = size / 2;
        _mask = new bool[Height, Width];
        _grid = new bool[Height, Width];
        Reset();
    }

    /// <summary>Whether a cell is inside the hourglass mask (the bowtie
    /// <c>dx &lt;= dy + 1</c> region) — the only cells the view draws / fills.</summary>
    public bool IsCell(int x, int y) => InBounds(x, y) && _mask[y, x];

    /// <summary>Whether a masked cell currently holds a grain.</summary>
    public bool HasSand(int x, int y) => InBounds(x, y) && _grid[y, x];

    /// <summary>
    /// Rebuild the mask + grid and respawn the top half — ports
    /// <c>_init_physics</c>. The mask is the bowtie <c>dx &lt;= dy + 1</c>
    /// (<c>dx, dy</c> = distance from center); sand spawns in every masked cell of
    /// the top half (<c>y &lt; CenterY</c>), and <see cref="TotalSand"/> counts them.
    /// </summary>
    public void Reset()
    {
        TotalSand = 0;
        SandPassed = 0;
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                int dy = Math.Abs(y - CenterY);
                int dx = Math.Abs(x - CenterX);
                bool masked = dx <= dy + 1;
                _mask[y, x] = masked;
                bool sand = masked && y < CenterY;
                _grid[y, x] = sand;
                if (sand)
                    TotalSand++;
            }
        }
    }

    /// <summary>
    /// Advance the physics one tick — ports <c>_physics_tick</c>. Bails out (no-op,
    /// no crash) when <paramref name="durationMs"/> is non-positive, mirroring the
    /// Python guard that protects against a 0-minute start.
    ///
    /// <para>Sweeps bottom-up so grains at the bottom fall first, making room.
    /// Each grain tries straight-down, else a random-biased down-diagonal. Any
    /// grain at <c>y == CenterY - 1</c> is entering the bottom half: that move is
    /// throttled against the wall clock — held when
    /// <c>SandPassed &gt;= (elapsedMs / durationMs) * TotalSand</c> (float
    /// comparison, no truncation).</para>
    /// </summary>
    /// <returns><c>true</c> if any grain moved (the view should repaint).</returns>
    public bool Step(long elapsedMs, long durationMs)
    {
        if (durationMs <= 0)
            return false;

        // Float ratio, no truncation — the throttle decides grain-by-grain whether
        // the next fall-through is on schedule, not just once per integer second.
        double expectedPassed = ((double)elapsedMs / durationMs) * TotalSand;

        bool movedAny = false;

        // Sweep bottom up so particles at the bottom fall first, making room.
        for (int y = Height - 2; y >= 0; y--)
        {
            for (int x = 0; x < Width; x++)
            {
                if (!_grid[y, x])
                    continue;

                // A grain at y == CenterY - 1 is about to cross into the bottom
                // half — the bottleneck. Throttle EVERY such move (center column
                // AND the two diagonal neighbours), not just the center cell, so
                // diagonal entries can't bypass the rate limit and outpace the clock.
                bool enteringBottomHalf = y == CenterY - 1;
                if (enteringBottomHalf && SandPassed >= expectedPassed)
                    continue;

                // 1. Try directly below.
                if (_mask[y + 1, x] && !_grid[y + 1, x])
                {
                    _grid[y, x] = false;
                    _grid[y + 1, x] = true;
                    movedAny = true;
                    if (enteringBottomHalf)
                        SandPassed++;
                }
                else
                {
                    // 2. Try falling down-diagonal (random L/R bias, like focus.py).
                    int[] dirs = _rng.NextDouble() > 0.5 ? new[] { -1, 1 } : new[] { 1, -1 };
                    foreach (int dx in dirs)
                    {
                        int nx = x + dx;
                        if (nx >= 0 && nx < Width && _mask[y + 1, nx] && !_grid[y + 1, nx])
                        {
                            _grid[y, x] = false;
                            _grid[y + 1, nx] = true;
                            movedAny = true;
                            if (enteringBottomHalf)
                                SandPassed++;
                            break;
                        }
                    }
                }
            }
        }

        return movedAny;
    }

    private bool InBounds(int x, int y) => x >= 0 && x < Width && y >= 0 && y < Height;
}
