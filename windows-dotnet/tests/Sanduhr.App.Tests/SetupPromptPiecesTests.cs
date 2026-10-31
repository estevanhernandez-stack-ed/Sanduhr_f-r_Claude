using Sanduhr.App.Desk;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.App.Tests;

/// <summary>The AI chat prompt teaches the pieces this build's Desk draws. Core cannot see <see cref="DeskView.Drawn"/>, so this pins
/// the prompt's list (the known words less <see cref="SetupPrompt.NotDrawn"/>) to it: a piece drawn from now on, or dropped, fails here.</summary>
public class SetupPromptPiecesTests
{
    [Fact]
    public void The_prompts_pieces_are_the_pieces_the_Desk_draws()
    {
        Assert.Equal(DeskView.Drawn.OrderBy(p => p, StringComparer.Ordinal), SetupPrompt.Pieces.OrderBy(p => p, StringComparer.Ordinal));
        Assert.All(SetupPrompt.NotDrawn, p => Assert.DoesNotContain(p, DeskView.Drawn));
    }
}
