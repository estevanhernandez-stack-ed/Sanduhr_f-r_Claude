using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class DrawerRuleTextTests
{
    [Theory]
    [InlineData(DrawerRuleKind.Screenshots, "Screenshots go to Screenshots")]
    [InlineData(DrawerRuleKind.Images, "Pictures go to Screenshots")]
    [InlineData(DrawerRuleKind.Documents, "Documents go to Screenshots")]
    [InlineData(DrawerRuleKind.Installers, "Installers go to Screenshots")]
    [InlineData(DrawerRuleKind.Archives, "Zip files go to Screenshots")]
    [InlineData(DrawerRuleKind.Shortcuts, "Shortcuts go to Screenshots")]
    public void Kind_rules_read_as_sentences(DrawerRuleKind kind, string sentence) =>
        Assert.Equal(sentence, DrawerRuleText.Sentence(new DrawerRule(kind, "x"), "Screenshots"));

    [Fact]
    public void The_age_rule_follows_its_days()
    {
        Assert.Equal("Files not opened in 30 days go to Older files",
            DrawerRuleText.Sentence(new DrawerRule(DrawerRuleKind.OlderThan, "older-files", Days: 30), "Older files"));
        Assert.Equal("Files not opened in 1 day go to Older files",
            DrawerRuleText.Sentence(new DrawerRule(DrawerRuleKind.OlderThan, "older-files", Days: 1), "Older files"));
    }

    [Fact]
    public void Name_rules_quote_the_users_text_and_use_the_drawer_name_given()
    {
        Assert.Equal("Names with \"invoice\" go to Receipts",
            DrawerRuleText.Sentence(new DrawerRule(DrawerRuleKind.NameContains, "receipts", Text: "invoice"), "Receipts"));
        Assert.Equal("Names starting with \"scan\" go to Folder 3",
            DrawerRuleText.Sentence(new DrawerRule(DrawerRuleKind.NameStartsWith, "scans", Text: "scan"), "Folder 3"));
    }

    [Fact]
    public void Every_default_rule_reads_without_the_words_to_to_or_a_stray_dash()
    {
        foreach (var rule in DrawerRules.Defaults)
        {
            var line = DrawerRuleText.Sentence(rule, "Somewhere");
            Assert.EndsWith(" go to Somewhere", line);
            Assert.DoesNotContain("—", line);
        }
    }
}
