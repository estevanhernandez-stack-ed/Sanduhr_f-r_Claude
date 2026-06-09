using System.Runtime.Versioning;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>
/// Data-compat self-check for the Windows Credential Manager interop. Proves
/// that <see cref="WindowsCredentialManager"/> writes the SAME on-disk format
/// Python <c>keyring</c> (WinVault) writes — so an existing user's accounts
/// carry over untouched — WITHOUT ever reading, writing, or deleting the live
/// <c>com.626labs.sanduhr</c> slots. Every test uses a throwaway service name
/// (<c>com.626labs.sanduhr.test-{guid}</c>) and cleans up after itself.
///
/// Format compat is confirmed by writing through the public API and then reading
/// the credential back by its LITERAL target name <c>{slot}@{service}</c> — if
/// that exact target resolves and the blob decodes to the original secret, the
/// on-disk TargetName format genuinely matches keyring's compound-name scheme.
/// The raw read also asserts UserName == slot, Persist == ENTERPRISE (3), and
/// Type == GENERIC (1), the three other fields the WinVault backend pins.
/// </summary>
[SupportedOSPlatform("windows")]
public class CredentialManagerInteropTests
{
    private static string ThrowawayService() => $"com.626labs.sanduhr.test-{Guid.NewGuid():N}";

    [Fact]
    public void Write_read_roundtrip_and_target_format_match_keyring()
    {
        if (!OperatingSystem.IsWindows())
            return; // interop is Windows-only; skip elsewhere

        var service = ThrowawayService();
        var mgr = new WindowsCredentialManager(service);
        const string slot = "sessionKey:Personal";
        // Non-ASCII + emoji proves the UTF-16-LE blob round-trips exactly.
        const string secret = "sk-ant-test-äöü-\U0001F511";

        try
        {
            mgr.SetPassword(slot, secret);

            // 1. Round-trip through the public API.
            Assert.Equal(secret, mgr.GetPassword(slot));

            // 2. The on-disk TargetName is literally "{slot}@{service}".
            var fullTarget = WindowsCredentialManager.CompoundTarget(service, slot);
            Assert.Equal($"{slot}@{service}", fullTarget);

            // 3. Read that literal target raw and confirm the keyring contract.
            var raw = mgr.ReadTargetRaw(fullTarget);
            Assert.NotNull(raw);
            Assert.Equal(secret, raw!.Value.Blob);   // UTF-16-LE blob decodes identically
            Assert.Equal(slot, raw.Value.UserName);  // UserName == slot
            Assert.Equal(3u, raw.Value.Persist);     // CRED_PERSIST_ENTERPRISE
            Assert.Equal(1u, raw.Value.Type);        // CRED_TYPE_GENERIC
        }
        finally
        {
            mgr.DeletePassword(slot);
        }

        // Cleanup proven — the throwaway slot is gone.
        Assert.Null(mgr.GetPassword(slot));
    }

    [Fact]
    public void AccountStore_roundtrips_through_real_credential_manager()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var service = ThrowawayService();
        var mgr = new WindowsCredentialManager(service);
        var accounts = new AccountStore(mgr);

        try
        {
            accounts.AddAccount("Personal", "sk-real", "cf-real");
            accounts.AddAccount("Marcus", "sk-marcus");

            Assert.Equal(new[] { "Personal", "Marcus" }, accounts.ListAccounts());
            Assert.Equal("Personal", accounts.GetActive());

            var personal = accounts.LoadCredentials("Personal");
            Assert.Equal("sk-real", personal.SessionKey);
            Assert.Equal("cf-real", personal.CfClearance);

            accounts.SetActive("Marcus");
            Assert.Equal("Marcus", accounts.GetActive());
            Assert.Equal("sk-marcus", accounts.LoadCredentials("Marcus").SessionKey);
            Assert.Null(accounts.LoadCredentials("Marcus").CfClearance);
        }
        finally
        {
            // Remove every slot this test may have written under the throwaway
            // service. Never touches the real com.626labs.sanduhr entries.
            mgr.DeletePassword("sessionKey:Personal");
            mgr.DeletePassword("cf_clearance:Personal");
            mgr.DeletePassword("sessionKey:Marcus");
            mgr.DeletePassword("cf_clearance:Marcus");
            mgr.DeletePassword("accounts:list");
            mgr.DeletePassword("accounts:active");
        }

        Assert.Null(mgr.GetPassword("accounts:list"));
    }
}
