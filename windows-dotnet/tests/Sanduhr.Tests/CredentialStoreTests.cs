using System.Text.Json;
using System.Text.Json.Nodes;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>
/// Parity tests for Core/CredentialStore.cs — ported from the Python build's
/// test_credentials.py. The Python <c>mock_keyring</c> + <c>tmp_appdata</c> +
/// monkeypatched legacy paths map to an injected <see cref="FakeCredentialManager"/>
/// and a <see cref="Paths"/> rooted at temp directories.
/// </summary>
public class CredentialStoreTests
{
    private sealed class Fixture : IDisposable
    {
        public TempDir Temp { get; }
        public string Home { get; }
        public Paths Paths { get; }
        public AccountStore Accounts { get; }
        public CredentialStore Credentials { get; }

        public Fixture()
        {
            Temp = new TempDir();
            Home = Path.Combine(Temp.Path, "home");
            Directory.CreateDirectory(Home);
            Paths = new Paths(Path.Combine(Temp.Path, "appdata"), Home);
            Accounts = new AccountStore(new FakeCredentialManager());
            Credentials = new CredentialStore(Accounts, Paths);
        }

        /// <summary>Write a v1 plaintext config at the legacy location.</summary>
        public void WriteLegacyConfig(string json)
        {
            var path = Paths.LegacyConfigFile;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, json);
        }

        public void WriteLegacyHistory(string json)
        {
            var path = Paths.LegacyHistoryFile;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, json);
        }

        public void Dispose() => Temp.Dispose();
    }

    [Fact]
    public void Load_empty_returns_empty_credentials()
    {
        using var f = new Fixture();
        Assert.Equal(new Credentials(null, null), f.Credentials.Load());
    }

    [Fact]
    public void Save_and_load_roundtrip()
    {
        using var f = new Fixture();
        f.Credentials.Save(sessionKey: "abc", cfClearance: "def");
        Assert.Equal(new Credentials("abc", "def"), f.Credentials.Load());
    }

    [Fact]
    public void Save_session_key_only()
    {
        using var f = new Fixture();
        f.Credentials.Save(sessionKey: "abc");
        Assert.Equal("abc", f.Credentials.Load().SessionKey);
        Assert.Null(f.Credentials.Load().CfClearance);
    }

    [Fact]
    public void Save_auto_creates_personal_account_on_first_save()
    {
        using var f = new Fixture();
        f.Credentials.Save(sessionKey: "abc");
        Assert.Equal(new[] { "Personal" }, f.Accounts.ListAccounts());
        Assert.Equal("Personal", f.Accounts.GetActive());
    }

    [Fact]
    public void Clear_removes_entries()
    {
        using var f = new Fixture();
        f.Credentials.Save(sessionKey: "abc", cfClearance: "def");
        f.Credentials.Clear();
        Assert.Equal(new Credentials(null, null), f.Credentials.Load());
    }

    [Fact]
    public void Service_name_matches_mac_keychain()
    {
        Assert.Equal("com.626labs.sanduhr", CredentialStore.Service);
    }

    [Fact]
    public void Migrate_v1_moves_session_key()
    {
        using var f = new Fixture();
        f.WriteLegacyConfig(JsonSerializer.Serialize(new { session_key = "old-key", theme = "mint" }));

        var result = f.Credentials.MigrateFromV1();

        Assert.True(result.Migrated);
        Assert.Equal("old-key", f.Credentials.Load().SessionKey);
    }

    [Fact]
    public void Migrate_v1_deletes_old_file()
    {
        using var f = new Fixture();
        f.WriteLegacyConfig(JsonSerializer.Serialize(new { session_key = "old-key" }));

        f.Credentials.MigrateFromV1();

        Assert.False(File.Exists(f.Paths.LegacyConfigFile));
    }

    [Fact]
    public void Migrate_v1_preserves_theme()
    {
        using var f = new Fixture();
        f.WriteLegacyConfig(JsonSerializer.Serialize(new { session_key = "k", theme = "aurora" }));

        f.Credentials.MigrateFromV1();

        var settings = JsonNode.Parse(File.ReadAllText(f.Paths.SettingsFile))!.AsObject();
        Assert.Equal("aurora", settings["theme"]!.GetValue<string>());
    }

    [Fact]
    public void Migrate_v1_missing_file_is_noop()
    {
        using var f = new Fixture();
        var result = f.Credentials.MigrateFromV1();
        Assert.False(result.Migrated);
    }

    [Fact]
    public void Migrate_v1_copies_history()
    {
        using var f = new Fixture();
        f.WriteLegacyConfig(JsonSerializer.Serialize(new { session_key = "k" }));
        f.WriteLegacyHistory("{\"five_hour\":[{\"t\":\"t\",\"v\":42}]}");

        f.Credentials.MigrateFromV1();

        var copied = JsonNode.Parse(File.ReadAllText(f.Paths.HistoryFile))!.AsObject();
        Assert.Equal(42, copied["five_hour"]![0]!["v"]!.GetValue<int>());
    }
}
