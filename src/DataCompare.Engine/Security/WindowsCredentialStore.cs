using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace DataCompare.Engine.Security;

/// <summary>
/// Persists passwords in the Windows Credential Manager (generic credentials, local-machine
/// persistence) so profile JSON files never contain plaintext secrets.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsCredentialStore : ICredentialStore
{
    private const int CredTypeGeneric = 1;
    private const int CredPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;

    public void SavePassword(string target, string userId, string password)
    {
        var passwordBytes = Encoding.Unicode.GetBytes(password);
        var blobPtr = Marshal.AllocHGlobal(passwordBytes.Length);
        var targetPtr = Marshal.StringToCoTaskMemUni(target);
        var userPtr = Marshal.StringToCoTaskMemUni(userId);
        try
        {
            Marshal.Copy(passwordBytes, 0, blobPtr, passwordBytes.Length);

            var credential = new NativeMethods.CREDENTIAL
            {
                Type = CredTypeGeneric,
                TargetName = targetPtr,
                CredentialBlobSize = passwordBytes.Length,
                CredentialBlob = blobPtr,
                Persist = CredPersistLocalMachine,
                UserName = userPtr,
            };

            if (!NativeMethods.CredWrite(ref credential, 0))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"Failed to save credential '{target}'.");
            }
        }
        finally
        {
            Array.Clear(passwordBytes);
            Marshal.FreeHGlobal(blobPtr);
            Marshal.FreeCoTaskMem(targetPtr);
            Marshal.FreeCoTaskMem(userPtr);
        }
    }

    public string? TryGetPassword(string target)
    {
        if (!NativeMethods.CredRead(target, CredTypeGeneric, 0, out var credentialPtr))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == ErrorNotFound)
            {
                return null;
            }

            throw new Win32Exception(error, $"Failed to read credential '{target}'.");
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeMethods.CREDENTIAL>(credentialPtr);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
            {
                return string.Empty;
            }

            var passwordBytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, passwordBytes, 0, credential.CredentialBlobSize);
            return Encoding.Unicode.GetString(passwordBytes);
        }
        finally
        {
            NativeMethods.CredFree(credentialPtr);
        }
    }

    public void DeletePassword(string target)
    {
        if (!NativeMethods.CredDelete(target, CredTypeGeneric, 0))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != ErrorNotFound)
            {
                throw new Win32Exception(error, $"Failed to delete credential '{target}'.");
            }
        }
    }

    private static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct CREDENTIAL
        {
            public int Flags;
            public int Type;
            public IntPtr TargetName;
            public IntPtr Comment;
            public long LastWritten;
            public int CredentialBlobSize;
            public IntPtr CredentialBlob;
            public int Persist;
            public int AttributeCount;
            public IntPtr Attributes;
            public IntPtr TargetAlias;
            public IntPtr UserName;
        }

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool CredWrite(ref CREDENTIAL userCredential, uint flags);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool CredRead(string target, int type, int reservedFlag, out IntPtr credentialPtr);

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool CredFree(IntPtr credentialPtr);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool CredDelete(string target, int type, int flags);
    }
}
