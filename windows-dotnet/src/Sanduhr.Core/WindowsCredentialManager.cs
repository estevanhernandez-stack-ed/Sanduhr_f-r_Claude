using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Sanduhr.Core;

/// <summary>
/// The keyring seam, ported from the Python build's dependency on
/// <c>keyring</c> (WinVault backend). <c>accounts.py</c> calls
/// <c>keyring.get_password(service, slot)</c> / <c>set_password</c> /
/// <c>delete_password</c>; this interface is the C# equivalent, with the
/// service name bound to the implementation instance. Both <see cref="AccountStore"/>
/// and <see cref="CredentialStore"/> depend on this seam, so tests can swap in
/// an in-memory fake (mirroring the Python <c>_fake_keyring</c> fixture) without
/// touching the real OS credential store.
/// </summary>
public interface ICredentialManager
{
    /// <summary>Service name (e.g. <c>com.626labs.sanduhr</c>).</summary>
    string Service { get; }

    /// <summary>Read the secret stored under <paramref name="slot"/>, or null.</summary>
    string? GetPassword(string slot);

    /// <summary>Store <paramref name="value"/> under <paramref name="slot"/>.</summary>
    void SetPassword(string slot, string value);

    /// <summary>Delete the credential under <paramref name="slot"/>. Returns
    /// false when there was nothing to delete (parity with Python catching
    /// <c>PasswordDeleteError</c>).</summary>
    bool DeletePassword(string slot);
}

/// <summary>
/// Raw fields read straight off a Windows Credential Manager entry. Used by the
/// data-compat self-check to prove the on-disk format matches what Python
/// <c>keyring</c> writes, without going through <see cref="WindowsCredentialManager"/>'s
/// own compound-name logic.
/// </summary>
public readonly record struct RawCredential(string? Blob, string? UserName, uint Persist, uint Type);

/// <summary>
/// Windows Credential Manager backend — the data-compat contract with the
/// shipped Python build. Mirrors <c>keyring</c>'s WinVault backend byte-for-byte
/// (verified empirically against the live install via <c>cmdkey /list</c>):
///
/// <list type="bullet">
/// <item>Each slot is a <b>Generic</b> credential
///   (<c>CRED_TYPE_GENERIC = 1</c>).</item>
/// <item><c>TargetName = "{slot}@{service}"</c> — e.g.
///   <c>sessionKey:Personal@com.626labs.sanduhr</c>.</item>
/// <item><c>UserName = "{slot}"</c>.</item>
/// <item><c>CredentialBlob = UTF-16-LE bytes of the secret</c>
///   (<see cref="Encoding.Unicode"/>), no null terminator.</item>
/// <item><c>Persist = CRED_PERSIST_ENTERPRISE = 3</c>.</item>
/// </list>
///
/// Read fallback: if the compound target is absent, also try the bare
/// <c>service</c> target — keyring's secondary lookup, which is how the legacy
/// single-entry installs resolve.
///
/// Implemented via P/Invoke to <c>advapi32</c> (<c>CredReadW</c> /
/// <c>CredWriteW</c> / <c>CredDeleteW</c>); Windows-only.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsCredentialManager : ICredentialManager
{
    private const uint CRED_TYPE_GENERIC = 1;
    private const uint CRED_PERSIST_ENTERPRISE = 3;
    private const int ERROR_NOT_FOUND = 1168;

    public string Service { get; }

    public WindowsCredentialManager(string service)
    {
        Service = service;
    }

    /// <summary>
    /// Compound credential target — <c>{slot}@{service}</c>. The exact on-disk
    /// TargetName keyring writes. Exposed so the data-compat test can assert the
    /// format directly.
    /// </summary>
    public static string CompoundTarget(string service, string slot) => $"{slot}@{service}";

    public string? GetPassword(string slot)
    {
        var raw = ReadTargetRaw(CompoundTarget(Service, slot));
        // Read fallback for the legacy single-entry install: the bare service
        // target. Mirrors keyring.get_password's secondary lookup.
        raw ??= ReadTargetRaw(Service);
        return raw?.Blob;
    }

    public void SetPassword(string slot, string value)
        => WriteTarget(CompoundTarget(Service, slot), slot, value);

    public bool DeletePassword(string slot)
        => DeleteTarget(CompoundTarget(Service, slot));

    /// <summary>
    /// Read the raw credential stored under an explicit, literal target name
    /// (no compound-name building). Returns null when the target does not exist.
    /// </summary>
    public RawCredential? ReadTargetRaw(string targetName)
    {
        if (!CredRead(targetName, CRED_TYPE_GENERIC, 0, out IntPtr handle))
            return null; // ERROR_NOT_FOUND (or any failure) — treat as absent
        try
        {
            var cred = Marshal.PtrToStructure<CREDENTIAL>(handle);
            string? blob = null;
            if (cred.CredentialBlob != IntPtr.Zero && cred.CredentialBlobSize > 0)
            {
                var bytes = new byte[cred.CredentialBlobSize];
                Marshal.Copy(cred.CredentialBlob, bytes, 0, (int)cred.CredentialBlobSize);
                blob = Encoding.Unicode.GetString(bytes);
            }
            string? userName = cred.UserName == IntPtr.Zero
                ? null
                : Marshal.PtrToStringUni(cred.UserName);
            return new RawCredential(blob, userName, cred.Persist, cred.Type);
        }
        finally
        {
            CredFree(handle);
        }
    }

    private static void WriteTarget(string targetName, string userName, string value)
    {
        var blob = Encoding.Unicode.GetBytes(value);
        IntPtr targetPtr = Marshal.StringToCoTaskMemUni(targetName);
        IntPtr userPtr = Marshal.StringToCoTaskMemUni(userName);
        IntPtr commentPtr = Marshal.StringToCoTaskMemUni("Stored by Sanduhr");
        IntPtr blobPtr = Marshal.AllocHGlobal(blob.Length);
        try
        {
            Marshal.Copy(blob, 0, blobPtr, blob.Length);
            var cred = new CREDENTIAL
            {
                Flags = 0,
                Type = CRED_TYPE_GENERIC,
                TargetName = targetPtr,
                Comment = commentPtr,
                CredentialBlobSize = (uint)blob.Length,
                CredentialBlob = blobPtr,
                Persist = CRED_PERSIST_ENTERPRISE,
                AttributeCount = 0,
                Attributes = IntPtr.Zero,
                TargetAlias = IntPtr.Zero,
                UserName = userPtr,
            };
            if (!CredWrite(ref cred, 0))
            {
                int err = Marshal.GetLastWin32Error();
                throw new InvalidOperationException(
                    $"CredWrite failed for target '{targetName}' (Win32 error {err}).");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(blobPtr);
            Marshal.FreeCoTaskMem(targetPtr);
            Marshal.FreeCoTaskMem(userPtr);
            Marshal.FreeCoTaskMem(commentPtr);
        }
    }

    private static bool DeleteTarget(string targetName)
    {
        if (CredDelete(targetName, CRED_TYPE_GENERIC, 0))
            return true;
        // ERROR_NOT_FOUND is the expected "nothing to delete" case; any other
        // failure is also swallowed (parity with Python's _delete_safely).
        _ = Marshal.GetLastWin32Error();
        return false;
    }

    // -- P/Invoke -------------------------------------------------------------

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public uint Flags;
        public uint Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref CREDENTIAL credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredFree")]
    private static extern void CredFree(IntPtr buffer);
}
