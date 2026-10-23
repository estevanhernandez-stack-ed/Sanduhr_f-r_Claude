namespace Sanduhr.Core;

/// <summary>An edge of the monitor a companion may roam (FORMAT 5.5 <c>roam.edges</c>).</summary>
public enum CompanionEdge { Bottom, Left, Right }

/// <summary>One growth stage of a sprite (FORMAT 5.5): its own sheet and cell size, shown from <see cref="MinGrowth"/>.</summary>
public sealed record CompanionStage(string Id, string Name, double MinGrowth, string Sheet, int FrameWidth, int FrameHeight)
{
    /// <summary>A sprite v2 pixel sheet (<c>.json</c>): drawn from the PNG Sanduhr rasterized at install (<see cref="WidgetStore.RasterPath"/>).</summary>
    public bool IsPixelSheet => Sheet.EndsWith(".json", StringComparison.Ordinal);
}

/// <summary>
/// A sprite pack's <c>sprite.json</c> as the Desk companion draws it (FORMAT 5.5): the art's stages and the animation
/// table, the mood and trigger mapping, the display scale and frame rate, and where it may roam. The pack was validated
/// at install (<see cref="PackValidator"/>); <see cref="Parse"/> is still lenient and returns null rather than throw on a
/// file that is not what the validator passed, so a hand-edited install folder can never take the Desk down.
/// </summary>
public sealed record CompanionSprite(
    int Scale,
    int Fps,
    IReadOnlyList<CompanionStage> Stages,
    IReadOnlyDictionary<string, IReadOnlyList<int>> Animations,
    IReadOnlyDictionary<CompanionMood, string> Moods,
    IReadOnlyDictionary<CompanionTrigger, string> Triggers,
    IReadOnlyList<CompanionEdge> Edges,
    double SpeedPxPerSecond)
{
    /// <summary>The animation drawn while roaming.</summary>
    public const string Walk = "walk";

    /// <summary>The id given to the one stage of a sprite with a single top-level <c>sheet</c> and no <c>stages</c>.</summary>
    public const string SingleStageId = "only";

    /// <summary>The frame indexes of <paramref name="animation"/>; empty when the pack has no such animation.</summary>
    public IReadOnlyList<int> Frames(string animation) =>
        Animations.TryGetValue(animation, out var f) ? f : [];

    /// <summary>The highest frame index any animation uses (a sheet must have cells up to it).</summary>
    public int MaxFrame => Animations.Values.SelectMany(f => f).DefaultIfEmpty(0).Max();

    /// <summary>The stage drawn at <paramref name="growth"/> (<see cref="CompanionGrowth.StageIndex"/>).</summary>
    public CompanionStage StageAt(double growth) => Stages[CompanionGrowth.StageIndex(Stages.Select(s => s.MinGrowth).ToList(), growth)];

    /// <summary>Reads a <c>sprite.json</c>. Null when it is not a usable sprite: no stage, a stage without a sheet or a
    /// cell size in range, a required animation, mood or trigger missing, or a mapping naming an animation that is not there.</summary>
    public static CompanionSprite? Parse(ReadOnlySpan<byte> json)
    {
        if (!PackJson.TryParse(json, out var v, out _) || v is not PackObject o) return null;
        int scale = Int(o["scale"], 1, 4) ?? 1;
        int fps = Int(o["fps"], 1, 24) ?? 8;
        int? fw = Int(o["frame_w"], 8, 64), fh = Int(o["frame_h"], 8, 64);

        var stages = new List<CompanionStage>();
        if (o["stages"] is List<object?> list)
        {
            foreach (var item in list)
            {
                if (item is not PackObject s) return null;
                if (s["id"] is not string id || s["sheet"] is not string sheet || !PackValidator.SafeRelPath(sheet)) return null;
                int? sw = Int(s["frame_w"], 8, 64), sh = Int(s["frame_h"], 8, 64);
                if (sw is null || sh is null) return null;
                double min = s["min_growth"] is PackNumber { IsFinite: true } n ? Math.Clamp(n.Value, 0, 1) : 0;
                if (stages.Count > 0 && min <= stages[^1].MinGrowth) return null;
                stages.Add(new CompanionStage(id, s["name"] as string ?? id, stages.Count == 0 ? 0 : min, sheet, sw.Value, sh.Value));
            }
        }
        else if (o["sheet"] is string single && PackValidator.SafeRelPath(single) && fw is not null && fh is not null)
        {
            stages.Add(new CompanionStage(SingleStageId, SingleStageId, 0, single, fw.Value, fh.Value));
        }
        if (stages.Count == 0) return null;

        var animations = new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal);
        if (o["animations"] is not PackObject anims) return null;
        foreach (var name in anims.Keys)
        {
            if (anims[name] is not List<object?> frames || frames.Count == 0) return null;
            var indexes = new List<int>(frames.Count);
            foreach (var f in frames)
            {
                if (Int(f, 0, int.MaxValue) is not { } i) return null;
                indexes.Add(i);
            }
            animations[name] = indexes;
        }
        foreach (var need in PackValidator.RequiredAnimations)
            if (!animations.ContainsKey(need)) return null;

        var moods = new Dictionary<CompanionMood, string>();
        if (o["moods"] is not PackObject m) return null;
        foreach (var mood in Enum.GetValues<CompanionMood>())
        {
            if (m[CompanionMoods.Key(mood)] is not string anim || !animations.ContainsKey(anim)) return null;
            moods[mood] = anim;
        }
        var triggers = new Dictionary<CompanionTrigger, string>();
        if (o["triggers"] is not PackObject t) return null;
        foreach (var trigger in Enum.GetValues<CompanionTrigger>())
        {
            if (t[CompanionMoods.Key(trigger)] is not string anim || !animations.ContainsKey(anim)) return null;
            triggers[trigger] = anim;
        }

        var edges = new List<CompanionEdge>();
        double speed = 30;
        if (o["roam"] is PackObject roam)
        {
            if (roam["edges"] is List<object?> es)
                foreach (var e in es)
                {
                    CompanionEdge? edge = e switch { "bottom" => CompanionEdge.Bottom, "left" => CompanionEdge.Left, "right" => CompanionEdge.Right, _ => null };
                    if (edge is { } ed && !edges.Contains(ed)) edges.Add(ed);
                }
            if (roam["speed_px_s"] is PackNumber { IsFinite: true } sp) speed = Math.Clamp(sp.Value, 5, 120);
        }
        if (edges.Count == 0) edges.Add(CompanionEdge.Bottom);

        return new CompanionSprite(scale, fps, stages, animations, moods, triggers, edges, speed);
    }

    private static int? Int(object? v, int min, int max) =>
        v is PackNumber { IsInt: true, IsFinite: true } n && n.Value >= min && n.Value <= max ? (int)n.Value : null;
}

/// <summary>
/// Cutting frames out of a sheet (FORMAT 5.5): cells of the stage's frame size, row-major, frame <c>i</c> the
/// <c>i</c>-th cell counting left to right, then top to bottom. The same rule for a PNG sheet and for the PNG rasterized
/// from a pixel sheet (<see cref="PixelSheet.SheetColumns"/> cells a row).
/// </summary>
public static class CompanionSheet
{
    /// <summary>Cells across a sheet <paramref name="sheetWidth"/> pixels wide; 0 when the width is not a whole number of cells.</summary>
    public static int Columns(int sheetWidth, int frameWidth) =>
        frameWidth > 0 && sheetWidth >= frameWidth && sheetWidth % frameWidth == 0 ? sheetWidth / frameWidth : 0;

    /// <summary>Frame <paramref name="index"/>'s cell as pixels (x, y, width, height), or null when the sheet has no
    /// such cell (its size is not a whole number of cells, or the index is past its last cell).</summary>
    public static PxRect? Cell(int index, int sheetWidth, int sheetHeight, int frameWidth, int frameHeight)
    {
        if (index < 0 || frameHeight <= 0 || sheetHeight < frameHeight || sheetHeight % frameHeight != 0) return null;
        int columns = Columns(sheetWidth, frameWidth);
        if (columns == 0) return null;
        int rows = sheetHeight / frameHeight;
        if (index >= columns * rows) return null;
        return new PxRect(index % columns * frameWidth, index / columns * frameHeight, frameWidth, frameHeight);
    }

    /// <summary>The file a stage is drawn from: its PNG, or for a pixel sheet the PNG rasterized beside the pack's files.</summary>
    public static string DrawnFile(WidgetStore store, string packId, CompanionStage stage) =>
        stage.IsPixelSheet
            ? store.RasterPath(packId, stage.Sheet)
            : Path.Combine(store.PackDir(packId), stage.Sheet.Replace('/', Path.DirectorySeparatorChar));
}
