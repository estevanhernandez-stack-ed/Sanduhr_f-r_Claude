using System.Text;
using System.Text.Json.Nodes;
using Sanduhr.Core;

namespace Sanduhr.Tests;

public class PixelSheetTests
{
    private static byte[] Sheet(string name, string rel) => WidgetPackFixtures.Load("good", name)[rel];

    private static (byte, byte, byte, byte) Px(PixelSheetImage img, int x, int y)
    {
        int at = (y * img.Width + x) * 4;
        return (img.Rgba[at], img.Rgba[at + 1], img.Rgba[at + 2], img.Rgba[at + 3]);
    }

    [Fact]
    public void The_fixture_sheet_rasterizes_into_a_one_row_grid()
    {
        var img = PixelSheet.Rasterize(Sheet("good-sprite-pixel", "sheet.json"), out var error);
        Assert.Null(error);
        Assert.NotNull(img);
        Assert.Equal((8, 8, 4, 4, 1), (img!.FrameWidth, img.FrameHeight, img.FrameCount, img.Columns, img.Rows));
        Assert.Equal((32, 8), (img.Width, img.Height));
        Assert.Equal(32 * 8 * 4, img.Rgba.Length);

        // Frame 0, row 0 is "..0000..": transparent, then palette[0] #22d3ee fully opaque.
        Assert.Equal((0, 0, 0, 0), Px(img, 0, 0));
        Assert.Equal((0x22, 0xd3, 0xee, 255), Px(img, 2, 0));
        // Frame 0, row 2 is "01122110": palette[2] #ec4899 at x 3.
        Assert.Equal((0xec, 0x48, 0x99, 255), Px(img, 3, 2));
        // Frame 1 starts at x 8; its row 0 is ".011110.": palette[1] #a855f7 at x 2.
        Assert.Equal((0, 0, 0, 0), Px(img, 8, 0));
        Assert.Equal((0x22, 0xd3, 0xee, 255), Px(img, 9, 0));
        Assert.Equal((0xa8, 0x55, 0xf7, 255), Px(img, 10, 0));
    }

    [Fact]
    public void Every_pixel_is_fully_opaque_or_fully_transparent()
    {
        var img = PixelSheet.Rasterize(Sheet("good-sprite-pixel", "sheet.json"), out _)!;
        for (int i = 3; i < img.Rgba.Length; i += 4)
            Assert.True(img.Rgba[i] is 0 or 255);
    }

    [Fact]
    public void Frame_copies_one_cell()
    {
        var img = PixelSheet.Rasterize(Sheet("good-sprite-pixel-stages", "sprites/one.json"), out _)!;
        byte[] f1 = img.Frame(1);
        Assert.Equal(8 * 8 * 4, f1.Length);
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
                Assert.Equal(Px(img, 8 + x, y), (f1[(y * 8 + x) * 4], f1[(y * 8 + x) * 4 + 1], f1[(y * 8 + x) * 4 + 2], f1[(y * 8 + x) * 4 + 3]));
        Assert.Throws<ArgumentOutOfRangeException>(() => img.Frame(4));
    }

    [Fact]
    public void Many_frames_wrap_at_sixteen_columns_row_major()
    {
        var frames = new JsonArray();
        for (int i = 0; i < 20; i++)
        {
            var rows = new JsonArray();
            for (int r = 0; r < 8; r++) rows.Add(i == 17 && r == 0 ? "1......." : "........");
            frames.Add(rows);
        }
        var sheet = new JsonObject { ["palette"] = new JsonArray("#101010", "#203040"), ["frame_w"] = 8, ["frame_h"] = 8, ["frames"] = frames };
        var img = PixelSheet.Rasterize(Encoding.UTF8.GetBytes(sheet.ToJsonString()), out var error);
        Assert.Null(error);
        Assert.Equal((16, 2, 128, 16), (img!.Columns, img.Rows, img.Width, img.Height));
        // Frame 17 is cell (1, 1): x 8, y 8.
        Assert.Equal((0x20, 0x30, 0x40, 255), Px(img, 8, 8));
        // Cells past the last frame are transparent.
        Assert.Equal((0, 0, 0, 0), Px(img, 127, 15));
    }

    [Theory]
    [InlineData("{\"palette\": [\"#ffffff\"], \"frame_w\": 8, \"frame_h\": 8, \"frames\": [[\"0.......\",\"........\",\"........\",\"........\",\"........\",\"........\",\"........\",\"........\"]]}", "pure white")]
    [InlineData("{\"palette\": [\"#101010\"], \"frame_w\": 8, \"frame_h\": 8, \"frames\": [[\"1.......\",\"........\",\"........\",\"........\",\"........\",\"........\",\"........\",\"........\"]]}", "palette index '1'")]
    [InlineData("{\"palette\": [\"#101010\"], \"frame_w\": 8, \"frame_h\": 8, \"frames\": [[\"........\"]]}", "has 1 rows, expected 8")]
    [InlineData("{\"palette\": [\"#101010\"], \"frame_w\": 40, \"frame_h\": 40, \"frames\": []}", "integers from 8 to 32")]
    [InlineData("[1, 2]", "must be a JSON object")]
    [InlineData("{\"palette\": [", "invalid JSON")]
    public void A_bad_sheet_is_refused_with_the_reason(string json, string needle)
    {
        var img = PixelSheet.Rasterize(Encoding.UTF8.GetBytes(json), out var error);
        Assert.Null(img);
        Assert.Contains(needle, error);
    }
}
