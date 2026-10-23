using System.Security.Cryptography;
using System.Text;
using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>FORMAT 7 rules 1 to 3: the pinned key, the signature, the strict parse, rollback, sanduhr_min, size and hash.</summary>
public class WidgetCatalogTests
{
    private static byte[] RealCatalog => File.ReadAllBytes(Path.Combine(WidgetTestKit.Fixtures, "widget-zips", "catalog.json"));

    private static byte[] Edit(byte[] catalog, string from, string to)
    {
        var text = Encoding.UTF8.GetString(catalog);
        Assert.Contains(from, text);
        return Encoding.UTF8.GetBytes(text.Replace(from, to));
    }

    // ---- the pinned key -------------------------------------------------------------------------------------------------

    [Fact]
    public void The_pinned_key_imports_as_a_256_bit_P256_key()
    {
        Assert.NotEmpty(WidgetCatalog.PinnedKeys);
        foreach (var spki in WidgetCatalog.PinnedKeys)
        {
            var der = Convert.FromBase64String(spki);
            Assert.Equal(91, der.Length);
            using var key = WidgetCatalog.ImportKey(spki);
            Assert.NotNull(key);
            Assert.Equal(256, key!.KeySize);
            var curve = key.ExportParameters(false).Curve;
            Assert.Equal("1.2.840.10045.3.1.7", curve.Oid.Value);
        }
    }

    [Fact]
    public void The_production_key_is_Estes_key_A()
    {
        Assert.Equal(
            "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEd+Og3mp7woxwUKyFRcqIcFjG2NOFB0jLYme8qLCMYmx7jRCzae6co0SkuQRFN2MoKPAuw5v3kzQLOFFP/nKNkA==",
            WidgetCatalog.PinnedKeys[0]);
    }

    [Fact]
    public void A_key_on_another_curve_or_not_a_key_is_refused()
    {
        using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        Assert.Null(WidgetCatalog.ImportKey(Convert.ToBase64String(p384.ExportSubjectPublicKeyInfo())));
        using var rsa = RSA.Create(2048);
        Assert.Null(WidgetCatalog.ImportKey(Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo())));
        Assert.Null(WidgetCatalog.ImportKey("not base64!"));
        using var p256 = WidgetTestKit.NewKey();
        var der = p256.ExportSubjectPublicKeyInfo().Concat(new byte[] { 0 }).ToArray();
        Assert.Null(WidgetCatalog.ImportKey(Convert.ToBase64String(der)));   // trailing bytes
    }

    // ---- rule 1: the signature ------------------------------------------------------------------------------------------

    [Fact]
    public void A_valid_signature_verifies()
    {
        using var key = WidgetTestKit.NewKey();
        var cat = RealCatalog;
        Assert.True(WidgetCatalog.VerifySignature(cat, [WidgetTestKit.SigFile(key, cat), null], [WidgetTestKit.Spki(key)]));
    }

    [Fact]
    public void A_tampered_catalog_fails()
    {
        using var key = WidgetTestKit.NewKey();
        var cat = RealCatalog;
        var sig = WidgetTestKit.SigFile(key, cat);
        var tampered = (byte[])cat.Clone();
        tampered[^2] ^= 1;
        Assert.False(WidgetCatalog.VerifySignature(tampered, [sig], [WidgetTestKit.Spki(key)]));
        // One more byte at the end is a different file too.
        Assert.False(WidgetCatalog.VerifySignature([.. cat, (byte)'\n'], [sig], [WidgetTestKit.Spki(key)]));
    }

    [Fact]
    public void A_signature_by_another_key_fails()
    {
        using var signer = WidgetTestKit.NewKey();
        using var pinned = WidgetTestKit.NewKey();
        var cat = RealCatalog;
        Assert.False(WidgetCatalog.VerifySignature(cat, [WidgetTestKit.SigFile(signer, cat)], [WidgetTestKit.Spki(pinned)]));
        // And the production key does not verify a test key's signature.
        Assert.False(WidgetCatalog.VerifySignature(cat, [WidgetTestKit.SigFile(signer, cat)], WidgetCatalog.PinnedKeys));
    }

    [Theory]
    [InlineData(63)]
    [InlineData(65)]
    [InlineData(0)]
    public void A_signature_of_the_wrong_length_is_invalid(int length)
    {
        var bytes = new byte[length];
        RandomNumberGenerator.Fill(bytes);
        Assert.Null(WidgetCatalog.DecodeSignature(Encoding.ASCII.GetBytes(Convert.ToBase64String(bytes) + "\n")));
    }

    [Fact]
    public void A_DER_signature_is_not_a_P1363_signature()
    {
        using var key = WidgetTestKit.NewKey();
        var cat = RealCatalog;
        var der = key.SignData(cat, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        Assert.False(WidgetCatalog.VerifySignature(cat, [Encoding.ASCII.GetBytes(Convert.ToBase64String(der) + "\n")], [WidgetTestKit.Spki(key)]));
    }

    [Fact]
    public void Signature_file_whitespace_is_trimmed_but_a_second_line_or_odd_characters_are_not()
    {
        using var key = WidgetTestKit.NewKey();
        var cat = RealCatalog;
        string b64 = Encoding.ASCII.GetString(WidgetTestKit.SigFile(key, cat)).TrimEnd('\n');
        string spki = WidgetTestKit.Spki(key);
        Assert.True(WidgetCatalog.VerifySignature(cat, [Encoding.ASCII.GetBytes(b64)], [spki]));
        Assert.True(WidgetCatalog.VerifySignature(cat, [Encoding.ASCII.GetBytes("  " + b64 + " \r\n\n")], [spki]));
        Assert.False(WidgetCatalog.VerifySignature(cat, [Encoding.ASCII.GetBytes(b64[..40] + "\n" + b64[40..])], [spki]));
        Assert.Null(WidgetCatalog.DecodeSignature(Encoding.ASCII.GetBytes("-" + b64[1..])));   // URL-safe alphabet
        Assert.Null(WidgetCatalog.DecodeSignature(Encoding.UTF8.GetBytes("é" + b64)));
    }

    [Fact]
    public void Sig2_alone_verifies_and_a_bad_sig_beside_a_good_sig2_still_verifies()
    {
        using var oldKey = WidgetTestKit.NewKey();
        using var newKey = WidgetTestKit.NewKey();
        var cat = RealCatalog;
        var pinned = new[] { WidgetTestKit.Spki(oldKey), WidgetTestKit.Spki(newKey) };
        Assert.True(WidgetCatalog.VerifySignature(cat, [null, WidgetTestKit.SigFile(newKey, cat)], pinned));
        Assert.True(WidgetCatalog.VerifySignature(cat, [Encoding.ASCII.GetBytes("garbage\n"), WidgetTestKit.SigFile(newKey, cat)], pinned));
        // A client that pins only the old key reads only .sig (rotation step 2).
        Assert.True(WidgetCatalog.VerifySignature(cat, [WidgetTestKit.SigFile(oldKey, cat), WidgetTestKit.SigFile(newKey, cat)], [WidgetTestKit.Spki(oldKey)]));
        Assert.False(WidgetCatalog.VerifySignature(cat, [null, null], pinned));
        Assert.False(WidgetCatalog.VerifySignature(cat, [WidgetTestKit.SigFile(newKey, cat)], []));
    }

    // ---- the strict parse -----------------------------------------------------------------------------------------------

    [Fact]
    public void The_real_catalog_parses()
    {
        var doc = WidgetCatalog.Parse(RealCatalog, out var error);
        Assert.True(doc is not null, error);
        Assert.Equal("2026-10-22T02:23:13Z", doc!.GeneratedAtText);
        Assert.Equal(new DateTimeOffset(2026, 10, 22, 2, 23, 13, TimeSpan.Zero), doc.GeneratedAt);
        Assert.Equal(["hourglass", "oled-neon", "oled-neon-effects", "oled-neon-palettes"], doc.Packs.Select(p => p.Id));
        var h = doc.Packs[0];
        Assert.Equal(("Hourglass", "sprite", "1.0.0", "3.8.0", 34521L), (h.Name, h.Kind, h.Version, h.SanduhrMin, h.Size));
        Assert.Equal("551c75b56192b045a02b307aafffdac24b5965d4626c1e5b37fb14182c48fd67", h.Sha256);
    }

    [Theory]
    [InlineData("\"schema_version\": 1", "\"schema_version\": 2")]
    [InlineData("\"schema_version\": 1", "\"schema_version\": 1.0")]
    [InlineData("\"schema_version\": 1\n", "\"schema_version\": 1,\n  \"extra\": true\n")]
    [InlineData("\"generated_at\": \"2026-10-22T02:23:13Z\"", "\"generated_at\": \"2026-10-22 02:23:13\"")]
    [InlineData("\"generated_at\": \"2026-10-22T02:23:13Z\"", "\"generated_at\": \"2026-13-22T02:23:13Z\"")]
    [InlineData("\"generated_at\": \"2026-10-22T02:23:13Z\"", "\"generated_at\": \"2026-10-22T02:23:13+00:00\"")]
    [InlineData("\"kind\": \"sprite\"", "\"kind\": \"script\"")]
    [InlineData("\"id\": \"hourglass\"", "\"id\": \"Hourglass\"")]
    [InlineData("\"version\": \"1.0.0\"\n    },\n    {\n      \"id\": \"oled-neon\"", "\"version\": \"1.0\"\n    },\n    {\n      \"id\": \"oled-neon\"")]
    [InlineData("\"sanduhr_min\": \"3.8.0\"", "\"sanduhr_min\": \"3.8\"")]
    [InlineData("\"size\": 34521", "\"size\": 0")]
    [InlineData("\"size\": 34521", "\"size\": 34521.0")]
    [InlineData("\"size\": 34521", "\"size\": \"34521\"")]
    [InlineData("\"size\": 34521", "\"size\": 99999999999")]
    [InlineData("551c75b56192b045a02b307aafffdac24b5965d4626c1e5b37fb14182c48fd67", "551C75B56192B045A02B307AAFFFDAC24B5965D4626C1E5B37FB14182C48FD67")]
    [InlineData("551c75b56192b045a02b307aafffdac24b5965d4626c1e5b37fb14182c48fd67", "551c75b5")]
    [InlineData("https://github.com/626Labs-LLC/sanduhr-widgets/releases/download/v1.0.0/hourglass-1.0.0.zip", "https://evil.example/hourglass-1.0.0.zip")]
    [InlineData("https://github.com/626Labs-LLC/sanduhr-widgets/releases/download/v1.0.0/hourglass-1.0.0.zip", "http://github.com/626Labs-LLC/sanduhr-widgets/releases/download/v1.0.0/hourglass-1.0.0.zip")]
    [InlineData("https://github.com/626Labs-LLC/sanduhr-widgets/releases/download/v1.0.0/hourglass-1.0.0.zip", "https://github.com/626Labs-LLC/sanduhr-widgets/releases/download/v1.0.0/oled-neon-1.0.0.zip")]
    [InlineData("https://github.com/626Labs-LLC/sanduhr-widgets/releases/download/v1.0.0/hourglass-1.0.0.zip", "https://github.com/626Labs-LLC/sanduhr-widgets/releases/download/../../x/hourglass-1.0.0.zip")]
    [InlineData("https://github.com/626Labs-LLC/sanduhr-widgets/releases/download/v1.0.0/hourglass-1.0.0.zip", "https://github.com/626Labs-LLC/other-repo/releases/download/v1.0.0/hourglass-1.0.0.zip")]
    [InlineData("\"name\": \"Hourglass\"", "\"name\": \"Hourglass\",\n      \"name\": \"Twice\"")]
    [InlineData("\"name\": \"Hourglass\"", "\"name\": \"Hour\\u0007glass\"")]
    [InlineData("\"name\": \"Hourglass\"", "\"name\": \"\"")]
    [InlineData("\"id\": \"oled-neon\"", "\"id\": \"aaa-first\"")]   // out of order
    [InlineData("\"id\": \"oled-neon-effects\"", "\"id\": \"oled-neon\"")]   // twice
    public void A_malformed_catalog_is_refused(string from, string to)
    {
        var bytes = Edit(RealCatalog, from, to);
        Assert.Null(WidgetCatalog.Parse(bytes, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void A_byte_order_mark_bad_utf8_or_non_object_is_refused()
    {
        Assert.Null(WidgetCatalog.Parse([0xEF, 0xBB, 0xBF, .. RealCatalog], out _));
        Assert.Null(WidgetCatalog.Parse([.. RealCatalog[..^2], 0xC3, (byte)'\n'], out _));
        Assert.Null(WidgetCatalog.Parse(Encoding.UTF8.GetBytes("[]"), out _));
        Assert.Null(WidgetCatalog.Parse([], out _));
        Assert.Null(WidgetCatalog.Parse(new byte[WidgetCatalog.MaxCatalogBytes + 1], out _));
    }

    [Fact]
    public void An_empty_pack_list_is_a_catalog()
    {
        var doc = WidgetCatalog.Parse(WidgetTestKit.Catalog("2026-10-22T00:00:00Z"), out var error);
        Assert.True(doc is not null, error);
        Assert.Empty(doc!.Packs);
    }

    // ---- rule 2: rollback -------------------------------------------------------------------------------------------------

    [Fact]
    public void Older_than_the_newest_seen_is_a_rollback_and_the_same_one_is_not()
    {
        var seen = new DateTimeOffset(2026, 10, 22, 2, 23, 13, TimeSpan.Zero);
        Assert.True(WidgetCatalog.IsRollback(seen.AddSeconds(-1), seen));
        Assert.False(WidgetCatalog.IsRollback(seen, seen));
        Assert.False(WidgetCatalog.IsRollback(seen.AddDays(1), seen));
        Assert.False(WidgetCatalog.IsRollback(seen, null));
    }

    // ---- rule 3: sanduhr_min, size, sha256 --------------------------------------------------------------------------------

    [Theory]
    [InlineData("3.8.0", 3, 8, 0, true)]
    [InlineData("3.8.0", 3, 7, 2, false)]
    [InlineData("3.8.0", 3, 10, 0, true)]
    [InlineData("3.8.1", 3, 8, 0, false)]
    [InlineData("4.0.0", 3, 99, 99, false)]
    [InlineData("3.10.0", 3, 9, 0, false)]
    [InlineData("3.8", 9, 9, 9, false)]
    [InlineData("v3.8.0", 9, 9, 9, false)]
    public void Sanduhr_min_compares_numerically(string min, int major, int minor, int build, bool met) =>
        Assert.Equal(met, WidgetCatalog.MeetsMinimum(min, new Version(major, minor, build, 0)));

    [Fact]
    public void Size_and_hash_are_checked_before_the_zip_is_opened()
    {
        var zip = Encoding.ASCII.GetBytes("pretend zip");
        var entry = WidgetTestKit.EntryFor("good-palette", "P", "palette", "1.0.0", zip);
        Assert.Null(WidgetCatalog.CheckDownload(entry, zip));
        Assert.Contains("bytes", WidgetCatalog.CheckDownload(entry, [.. zip, 0]));
        var flipped = (byte[])zip.Clone();
        flipped[0] ^= 1;
        Assert.Contains("SHA-256", WidgetCatalog.CheckDownload(entry, flipped));
    }

    [Theory]
    [InlineData("https://github.com/626Labs-LLC/sanduhr-widgets/releases/latest/download/catalog.json", true)]
    [InlineData("https://objects.githubusercontent.com/github-production-release-asset/x", true)]
    [InlineData("https://release-assets.githubusercontent.com/github-production-release-asset/x", true)]
    [InlineData("http://github.com/x", false)]
    [InlineData("https://github.com:8443/x", false)]
    [InlineData("https://user:pw@github.com/x", false)]
    [InlineData("https://github.com.evil.example/x", false)]
    [InlineData("https://raw.githubusercontent.com/x", false)]
    public void Redirects_may_land_only_on_GitHub_over_https(string url, bool allowed) =>
        Assert.Equal(allowed, WidgetCatalog.IsAllowedUrl(new Uri(url)));
}
