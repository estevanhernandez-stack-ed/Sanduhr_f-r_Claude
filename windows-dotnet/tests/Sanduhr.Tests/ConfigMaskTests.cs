using System.Text.Json.Nodes;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>Port of the Mac's secret-mask cases (ModsConfigGroundTests).</summary>
public class ConfigMaskTests
{
    private static string[] P(params string[] p) => p;
    private static string Shows(string s) => ConfigMask.MaskString(s);

    [Fact]
    public void Secret_paths_are_masked_and_never_editable()
    {
        Assert.True(ConfigMask.IsSecret(P("env", "PATH")));
        Assert.True(ConfigMask.IsSecret(P("env")));
        Assert.True(ConfigMask.IsSecret(P("apiKeyHelper")));
        Assert.True(ConfigMask.IsSecret(P("mcpServers", "x", "headers", "Authorization")));
        Assert.True(ConfigMask.IsSecret(P("mcpServers", "x", "env", "GITHUB_TOKEN")));
        Assert.True(ConfigMask.IsSecret(P("otelHeadersHelper", "refresh_token")));
        Assert.True(ConfigMask.IsSecret(P("oauthAccount", "emailAddress")));
        Assert.True(ConfigMask.IsSecret(P("someService", "Password")));
        Assert.True(ConfigMask.IsSecret(P("awsCredentialExport")));
        Assert.True(ConfigMask.IsSecret(P("a", "client_secret")));
        Assert.False(ConfigMask.IsSecret(P("cleanupPeriodDays")));
        Assert.False(ConfigMask.IsSecret(P("permissions", "allow", "0")));
        Assert.False(ConfigMask.IsSecret(P("syncClaudeAiSkills")));
        Assert.False(ConfigMask.IsEditable(P("env", "FOO")));
        Assert.True(ConfigMask.IsEditable(P("cleanupPeriodDays")));
    }

    [Fact]
    public void Mask_keeps_names_and_drops_values()
    {
        var settings = JsonNode.Parse("""
        {
          "cleanupPeriodDays": 90,
          "env": {"ANTHROPIC_API_KEY": "sk-ant-secret", "NODE_OPTIONS": "--max-old-space-size=8192"},
          "mcpServers": {"gh": {"command": "npx", "args": ["-y", "server"],
                                "env": {"GITHUB_TOKEN": "ghp_secret"},
                                "headers": {"Authorization": "Bearer secret"}}},
          "apiKeyHelper": "/bin/print-secret",
          "nested": {"deeper": {"password": {"a": "secret-a"}, "list": ["fine"]}}
        }
        """);
        var shown = ConfigMask.Display(settings);
        foreach (var secret in new[] { "sk-ant-secret", "8192", "ghp_secret", "Bearer", "print-secret", "secret-a" })
            Assert.DoesNotContain(secret, shown);
        foreach (var name in new[] { "ANTHROPIC_API_KEY", "NODE_OPTIONS", "GITHUB_TOKEN", "Authorization", "apiKeyHelper", "90", "npx", "fine" })
            Assert.Contains(name, shown);

        var masked = ConfigMask.Mask(settings)!.AsObject();
        Assert.Equal(ConfigMask.Masked, (string?)masked["env"]!["NODE_OPTIONS"]);
        // The input is untouched.
        Assert.Equal("sk-ant-secret", (string?)settings!["env"]!["ANTHROPIC_API_KEY"]);

        var list = ConfigMask.Mask(JsonNode.Parse("""["a","b"]"""), P("env", "LIST"))!.AsArray();
        Assert.Equal(new[] { ConfigMask.Masked, ConfigMask.Masked }, list.Select(n => (string?)n));
        Assert.Equal("plain", ConfigMask.Display(JsonValue.Create("plain"), P("model")));
        Assert.Equal(ConfigMask.Masked, ConfigMask.Display(JsonValue.Create("hunter2"), P("env", "X")));
    }

    [Fact]
    public void Non_string_leaves_keep_their_values_off_secret_paths_and_mask_on_them()
    {
        var node = JsonNode.Parse("""{"n": 5, "b": true, "z": null}""");
        Assert.Equal("""{"b":true,"n":5,"z":null}""", ConfigMask.Display(node));
        Assert.Equal(ConfigMask.Masked, ConfigMask.Display(JsonValue.Create(5), P("env", "N")));
        Assert.Equal(ConfigMask.Masked, ConfigMask.Display(null, P("env", "Z")));
    }

    [Fact]
    public void Values_inside_commands_are_masked()
    {
        // URL userinfo and secret-named query parameters.
        Assert.Equal($"https://{ConfigMask.Masked}@example.com/x", Shows("https://user:hunter2@example.com/x"));
        Assert.Equal($"https://example.com/x?token={ConfigMask.Masked}&page=2", Shows("https://example.com/x?token=abc123&page=2"));
        Assert.Equal($"https://example.com/?api_key={ConfigMask.Masked}", Shows("https://example.com/?api_key=zzz"));
        // Flags: --flag=value and --flag value.
        Assert.Equal($"server --api-key={ConfigMask.Masked} --port=8080", Shows("server --api-key=abc123 --port=8080"));
        Assert.Equal($"server --token {ConfigMask.Masked} --verbose", Shows("server --token abc123 --verbose"));
        Assert.Equal("run --verbose --name me", Shows("run --verbose --name me"));
        // NAME=value in a command, with a secret-named NAME.
        Assert.Equal($"GITHUB_TOKEN={ConfigMask.Masked} PORT=1 python3 x.py", Shows("GITHUB_TOKEN=ghx123 PORT=1 python3 x.py"));
        Assert.Equal($"Bash(export DB_PASSWORD={ConfigMask.Masked})", Shows("Bash(export DB_PASSWORD='p w')"));
        // Headers and bearer tokens.
        Assert.DoesNotContain("dXNlcjpwYXNz", Shows("Bash(curl -H 'Authorization: Basic dXNlcjpwYXNz' x)"));
        Assert.DoesNotContain("s3cr3t", Shows("curl -H \"Cookie: session=s3cr3t\" x"));
        Assert.Equal($"Bearer {ConfigMask.Masked}", Shows("Bearer eyJhbGciOi.payload"));
        // Key-shaped strings anywhere.
        Assert.DoesNotContain("ABCDEFGHIJ", Shows("echo sk-ant-api03-ABCDEFGHIJKLMNOP | x"));
        Assert.DoesNotContain("1234567890", Shows("key sk-proj1234567890abc"));
        Assert.DoesNotContain("abcdefghij", Shows("ghp_abcdefghijklmnopqrstuvwxyz0123"));
        // Plain commands stay readable.
        Assert.Equal("python3 C:/Users/x/statusline.py --compact", Shows("python3 C:/Users/x/statusline.py --compact"));
    }

    [Fact]
    public void Args_lists_and_new_secret_words()
    {
        var server = JsonNode.Parse("""
        {"command": "npx",
         "args": ["-y", "server", "--api-key", "abc123", "--port", "8080", "--auth-token=zzz"],
         "url": "https://u:p@example.com/mcp"}
        """);
        var shown = ConfigMask.Display(server, P("mcpServers", "s"));
        foreach (var leaked in new[] { "abc123", "zzz", "u:p@" }) Assert.DoesNotContain(leaked, shown);
        foreach (var kept in new[] { "--api-key", "8080", "--port", "server" }) Assert.Contains(kept, shown);

        Assert.True(ConfigMask.IsSecret(P("auth")) && ConfigMask.IsSecret(P("oauthClient")) && ConfigMask.IsSecret(P("Authorization")));
        Assert.True(ConfigMask.IsSecret(P("bearerToken")) && ConfigMask.IsSecret(P("cookies")));
        Assert.False(ConfigMask.IsSecret(P("author")) || ConfigMask.IsSecret(P("authors", "0")));

        // statusLine and hook commands are masked by value even though their paths aren't secret.
        var settings = JsonNode.Parse("""
        {"statusLine": {"type": "command", "command": "API_KEY=xyz789 bash s.sh"},
         "hooks": {"Stop": [{"hooks": [{"type": "command", "command": "curl -H 'Authorization: Bearer qqq' x"}]}]},
         "permissions": {"allow": ["Bash(export SECRET_X=val1)"]}}
        """);
        var all = ConfigMask.Display(settings);
        foreach (var leaked in new[] { "xyz789", "qqq", "val1" }) Assert.DoesNotContain(leaked, all);
    }

    [Fact]
    public void Secret_flags_need_a_dash_and_no_equals()
    {
        Assert.True(ConfigMask.IsSecretFlag("--api-key"));
        Assert.True(ConfigMask.IsSecretFlag("-token"));
        Assert.False(ConfigMask.IsSecretFlag("--api-key=x"));
        Assert.False(ConfigMask.IsSecretFlag("token"));
        Assert.False(ConfigMask.IsSecretFlag("--port"));
        Assert.False(ConfigMask.IsSecretFlag("--"));
    }
}
