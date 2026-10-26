using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>Settings v2, slice 5 (WF30): the first sign-in asks for a Label with "Personal" filled in; later ones offer "Account N".</summary>
public class AccountLabelsTests
{
    [Fact]
    public void The_first_account_is_offered_Personal()
    {
        Assert.Equal("Personal", AccountLabels.Suggested([]));
        Assert.Equal(AccountLabels.First, AccountLabels.Suggested([]));
    }

    [Fact]
    public void A_later_account_is_offered_the_next_free_number()
    {
        Assert.Equal("Account 2", AccountLabels.Suggested(["Personal"]));
        Assert.Equal("Account 3", AccountLabels.Suggested(["Personal", "account 2"]));
    }

    [Theory]
    [InlineData("Work", true)]
    [InlineData("My_work-2", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("a/b", false)]
    [InlineData("ThirtyThreeCharactersIsTooLongXYZ", false)]
    [InlineData(null, false)]
    public void A_label_is_one_to_32_letters_digits_spaces_underscores_or_hyphens(string? label, bool valid) =>
        Assert.Equal(valid, AccountLabels.IsValid(label));

    [Fact]
    public void A_typed_label_is_trimmed_and_kept_when_valid_and_free() =>
        Assert.Equal("Work", AccountLabels.Accept("  Work ", "Personal", []));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("bad/name")]
    [InlineData("personal")]
    public void Cancelled_invalid_or_taken_keeps_the_suggestion(string? typed) =>
        Assert.Equal("Account 2", AccountLabels.Accept(typed, "Account 2", ["Personal"]));
}
