using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>
/// Parity tests for Core/TokenFormat.cs — the compact token formatter ported from
/// the Python <c>tiers._format_tokens_compact</c> docstring examples plus the
/// boundary values where the ladder switches form.
/// </summary>
public class TokenFormatTests
{
    [Theory]
    [InlineData(0, "0")]
    [InlineData(1, "1")]
    [InlineData(999, "999")]            // below 1k → bare integer
    [InlineData(1000, "1.0k")]          // 1k boundary → one decimal
    [InlineData(1499, "1.5k")]          // docstring example
    [InlineData(1500, "1.5k")]
    [InlineData(9999, "10.0k")]         // still in the decimal-k band
    [InlineData(10000, "10k")]          // ≥10k → integer-thousands
    [InlineData(12345, "12k")]          // docstring example
    [InlineData(999999, "999k")]
    [InlineData(1000000, "1.0M")]       // 1M boundary
    [InlineData(1234567, "1.2M")]       // docstring example
    public void Compact_matches_python_ladder(long n, string expected)
        => Assert.Equal(expected, TokenFormat.Compact(n));
}
