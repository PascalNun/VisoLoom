using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace VisoLoom.Providers;

/// <summary>Per-user OS credentials. No plaintext fallback, shell arguments or credential files.</summary>
public sealed class OsCredentialStore : ICredentialStore
{
    public string? Read(string origin)
    {
        string target = Target(origin);
        if (OperatingSystem.IsMacOS())
            return Mac.Read(target);
        if (!OperatingSystem.IsWindows())
            throw Unavailable();
        if (!CredRead(target, 1, 0, out var pointer))
        {
            if (Marshal.GetLastWin32Error() == 1168)
                return null;
            throw Unavailable();
        }
        try
        {
            var credential = Marshal.PtrToStructure<Credential>(pointer);
            return ReadSecret(credential.CredentialBlob, credential.CredentialBlobSize);
        }
        finally { CredFree(pointer); }
    }

    public void Set(string origin, string token)
    {
        string target = Target(origin);
        if (string.IsNullOrWhiteSpace(token) || token.Length > 4096 || token.Any(char.IsControl))
            throw new ArgumentException("Invalid credential.");
        byte[] bytes = Encoding.UTF8.GetBytes(token);
        try
        {
            if (OperatingSystem.IsMacOS())
                Mac.Set(target, bytes);
            else if (OperatingSystem.IsWindows())
            {
                if (bytes.Length > 2560)
                    throw Unavailable();
                var memory = Marshal.AllocHGlobal(bytes.Length);
                try
                {
                    Marshal.Copy(bytes, 0, memory, bytes.Length);
                    var credential = new Credential
                    {
                        Type = 1,
                        TargetName = target,
                        CredentialBlobSize = (uint)bytes.Length,
                        CredentialBlob = memory,
                        Persist = 2,
                        UserName = "VisoLoom"
                    };
                    if (!CredWrite(ref credential, 0))
                        throw Unavailable();
                }
                finally
                {
                    Marshal.Copy(new byte[bytes.Length], 0, memory, bytes.Length);
                    Marshal.FreeHGlobal(memory);
                }
            }
            else
                throw Unavailable();
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public void Forget(string origin)
    {
        string target = Target(origin);
        if (OperatingSystem.IsMacOS())
            Mac.Forget(target);
        else if (!OperatingSystem.IsWindows() || !CredDelete(target, 1, 0) && Marshal.GetLastWin32Error() != 1168)
            throw Unavailable();
    }

    private static string Target(string origin)
    {
        if (origin.Length is < 1 or > 200 || origin.Any(char.IsControl))
            throw new ArgumentException("Invalid credential origin.");
        return "VisoLoom/" + origin;
    }

    private static string ReadSecret(IntPtr pointer, uint length)
    {
        if (length is 0 or > 4096 || pointer == IntPtr.Zero)
            throw Unavailable();
        byte[] bytes = new byte[length];
        try
        {
            Marshal.Copy(pointer, bytes, 0, bytes.Length);
            return Encoding.UTF8.GetString(bytes);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private static InvalidOperationException Unavailable() => new("The OS credential store is unavailable. Use this Rhino session only.");

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags;
        public uint Type;
        public string? TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);
    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref Credential credential, uint flags);
    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, uint type, uint flags);
    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);

    private static class Mac
    {
        private const string Security = "/System/Library/Frameworks/Security.framework/Security";
        private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        private static readonly IntPtr SecurityLibrary = NativeLibrary.Load(Security);
        private static readonly IntPtr FoundationLibrary = NativeLibrary.Load(CoreFoundation);
        private static IntPtr Constant(string name) => Marshal.ReadIntPtr(NativeLibrary.GetExport(SecurityLibrary, name));

        public static string? Read(string target)
        {
            using var query = new Query(target);
            CFDictionarySetValue(query.Handle, Constant("kSecReturnData"), Marshal.ReadIntPtr(NativeLibrary.GetExport(FoundationLibrary, "kCFBooleanTrue")));
            CFDictionarySetValue(query.Handle, Constant("kSecMatchLimit"), Constant("kSecMatchLimitOne"));
            int status = SecItemCopyMatching(query.Handle, out var data);
            if (status == -25300)
                return null;
            if (status != 0)
                throw Unavailable();
            try
            { return ReadSecret(CFDataGetBytePtr(data), checked((uint)CFDataGetLength(data))); }
            finally { CFRelease(data); }
        }

        public static void Set(string target, byte[] secret)
        {
            using var query = new Query(target);
            using var attributes = new Query();
            var data = CFDataCreate(IntPtr.Zero, secret, secret.Length);
            if (data == IntPtr.Zero)
                throw Unavailable();
            try
            {
                CFDictionarySetValue(attributes.Handle, Constant("kSecValueData"), data);
                int status = SecItemUpdate(query.Handle, attributes.Handle);
                if (status == -25300)
                {
                    CFDictionarySetValue(query.Handle, Constant("kSecValueData"), data);
                    status = SecItemAdd(query.Handle, IntPtr.Zero);
                }
                if (status != 0)
                    throw Unavailable();
            }
            finally { CFRelease(data); }
        }

        public static void Forget(string target)
        {
            using var query = new Query(target);
            int status = SecItemDelete(query.Handle);
            if (status is not (0 or -25300))
                throw Unavailable();
        }

        // Dictionaries borrow their values; this owner keeps strings alive until the API call ends.
        private sealed class Query : IDisposable
        {
            private readonly List<IntPtr> strings = [];
            public IntPtr Handle { get; } = CFDictionaryCreateMutable(IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero);
            public Query(string? target = null)
            {
                if (Handle == IntPtr.Zero)
                    throw Unavailable();
                if (target is null)
                    return;
                try
                {
                    CFDictionarySetValue(Handle, Constant("kSecClass"), Constant("kSecClassGenericPassword"));
                    SetString("kSecAttrService", target);
                    SetString("kSecAttrAccount", "api-key");
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }
            private void SetString(string key, string value)
            {
                var text = CFStringCreateWithCString(IntPtr.Zero, value, 0x08000100);
                if (text == IntPtr.Zero)
                    throw Unavailable();
                strings.Add(text);
                CFDictionarySetValue(Handle, Constant(key), text);
            }
            public void Dispose()
            {
                CFRelease(Handle);
                foreach (var text in strings)
                    CFRelease(text);
            }
        }

        [DllImport(Security)] private static extern int SecItemCopyMatching(IntPtr query, out IntPtr result);
        [DllImport(Security)] private static extern int SecItemUpdate(IntPtr query, IntPtr attributes);
        [DllImport(Security)] private static extern int SecItemAdd(IntPtr attributes, IntPtr result);
        [DllImport(Security)] private static extern int SecItemDelete(IntPtr query);
        [DllImport(CoreFoundation)] private static extern IntPtr CFDictionaryCreateMutable(IntPtr allocator, nint capacity, IntPtr keys, IntPtr values);
        [DllImport(CoreFoundation)] private static extern void CFDictionarySetValue(IntPtr dictionary, IntPtr key, IntPtr value);
        [DllImport(CoreFoundation, CharSet = CharSet.Ansi)] private static extern IntPtr CFStringCreateWithCString(IntPtr allocator, string value, uint encoding);
        [DllImport(CoreFoundation)] private static extern IntPtr CFDataCreate(IntPtr allocator, byte[] bytes, nint length);
        [DllImport(CoreFoundation)] private static extern nint CFDataGetLength(IntPtr data);
        [DllImport(CoreFoundation)] private static extern IntPtr CFDataGetBytePtr(IntPtr data);
        [DllImport(CoreFoundation)] private static extern void CFRelease(IntPtr value);
    }
}
