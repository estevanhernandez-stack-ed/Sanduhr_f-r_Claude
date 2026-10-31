using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class DrawerModelTests
{
    [Theory]
    [InlineData("screenshots", true)]
    [InlineData("trail-notes-2", true)]
    [InlineData("a", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("Screenshots", false)]
    [InlineData("-lead", false)]
    [InlineData("has space", false)]
    [InlineData("has:colon", false)]
    [InlineData("has@at", false)]
    [InlineData("has.dot", false)]
    public void Ids_are_lowercase_slugs_safe_in_a_layout_word(string? id, bool valid) =>
        Assert.Equal(valid, DrawerIds.IsValid(id));

    [Fact]
    public void Ids_longer_than_forty_are_refused() =>
        Assert.False(DrawerIds.IsValid(new string('a', 41)));

    [Theory]
    [InlineData("Screenshots", "screenshots")]
    [InlineData("Older files", "older-files")]
    [InlineData("  Trail  Notes!! ", "trail-notes")]
    [InlineData("Ünïcødé", "n-c-d")]
    [InlineData("!!!", "drawer")]
    public void From_turns_a_name_into_a_valid_id(string name, string id)
    {
        Assert.Equal(id, DrawerIds.From(name));
        Assert.True(DrawerIds.IsValid(DrawerIds.From(name)));
    }

    [Theory]
    [InlineData("Screenshots", true)]
    [InlineData("Older files", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    [InlineData("a/b", false)]
    [InlineData("a\\b", false)]
    [InlineData("..", false)]
    [InlineData(".", false)]
    [InlineData("trailing.", false)]
    [InlineData("trailing ", false)]
    [InlineData("CON", false)]
    [InlineData("nul", false)]
    [InlineData("com1", false)]
    [InlineData("LPT9", false)]
    [InlineData("what?", false)]
    public void Names_are_names_Windows_accepts_as_a_folder(string? name, bool valid) =>
        Assert.Equal(valid, DrawerNames.IsValid(name));

    [Fact]
    public void Names_longer_than_forty_are_refused() =>
        Assert.False(DrawerNames.IsValid(new string('a', 41)));

    [Fact]
    public void Free_keeps_the_name_when_nothing_is_there() =>
        Assert.Equal(@"C:\d\report.docx", DrawerNames.Free(@"C:\d", "report.docx", false, _ => false));

    [Fact]
    public void Free_numbers_a_clash_the_way_Explorer_does()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\d\report.docx", @"C:\d\report (2).docx" };
        Assert.Equal(@"C:\d\report (3).docx", DrawerNames.Free(@"C:\d", "report.docx", false, taken.Contains));
    }

    [Fact]
    public void Free_numbers_a_folder_after_its_whole_name()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\d\my.project" };
        Assert.Equal(@"C:\d\my.project (2)", DrawerNames.Free(@"C:\d", "my.project", true, taken.Contains));
    }

    [Fact]
    public void Free_compares_without_case()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\d\A.TXT" };
        Assert.Equal(@"C:\d\a (2).txt", DrawerNames.Free(@"C:\d", "a.txt", false, taken.Contains));
    }
}
