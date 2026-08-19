using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace DataCompare.Engine.Security
{

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

        /// <summary>
        /// saves the given password in the Windows Credential Manager under the given target, associated with the given user id.
        /// </summary>
        /// <param name="target">a System.String identifying the credential to save.</param>
        /// <param name="userId">a System.String containing the user id the password belongs to.</param>
        /// <param name="password">a System.String containing the password to save.</param>
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

        /// <summary>
        /// attempts to retrieve the password stored under the given target in the Windows Credential Manager.
        /// </summary>
        /// <param name="target">a System.String identifying the credential to retrieve.</param>
        /// <returns>returns a System.String containing the stored password, or null if no credential exists for <paramref name="target"/>.</returns>
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

        /// <summary>
        /// deletes the password stored under the given target in the Windows Credential Manager, if one exists.
        /// </summary>
        /// <param name="target">a System.String identifying the credential to delete.</param>
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

        /// <summary>
        /// Native Win32 Credential Manager interop declarations used to persist and retrieve generic credentials.
        /// </summary>
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

            /// <summary>
            /// writes the given credential to the Windows Credential Manager.
            /// </summary>
            /// <param name="userCredential">a DataCompare.Engine.Security.WindowsCredentialStore.NativeMethods.CREDENTIAL describing the credential to write.</param>
            /// <param name="flags">a System.UInt32 containing reserved flags for the CredWrite Win32 API; must be 0.</param>
            /// <returns>returns a System.Boolean that is true when the credential was written successfully.</returns>
            [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
            public static extern bool CredWrite(ref CREDENTIAL userCredential, uint flags);

            /// <summary>
            /// reads the credential stored under the given target name from the Windows Credential Manager.
            /// </summary>
            /// <param name="target">a System.String identifying the credential to read.</param>
            /// <param name="type">a System.Int32 containing the Win32 credential type to read; DataCompare.Engine.Security.WindowsCredentialStore.CredTypeGeneric is used throughout this class.</param>
            /// <param name="reservedFlag">a System.Int32 containing reserved flags for the CredRead Win32 API; must be 0.</param>
            /// <param name="credentialPtr">a System.IntPtr that receives a pointer to the retrieved CREDENTIAL structure.</param>
            /// <returns>returns a System.Boolean that is true when a credential was found and read successfully.</returns>
            [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
            public static extern bool CredRead(string target, int type, int reservedFlag, out IntPtr credentialPtr);

            /// <summary>
            /// frees a buffer previously allocated by <see cref="CredRead"/>.
            /// </summary>
            /// <param name="credentialPtr">a System.IntPtr pointing to the buffer to free.</param>
            /// <returns>returns a System.Boolean that is true when the buffer was freed successfully.</returns>
            [DllImport("advapi32.dll", SetLastError = true)]
            public static extern bool CredFree(IntPtr credentialPtr);

            /// <summary>
            /// deletes the credential stored under the given target name from the Windows Credential Manager.
            /// </summary>
            /// <param name="target">a System.String identifying the credential to delete.</param>
            /// <param name="type">a System.Int32 containing the Win32 credential type to delete; DataCompare.Engine.Security.WindowsCredentialStore.CredTypeGeneric is used throughout this class.</param>
            /// <param name="flags">a System.Int32 containing reserved flags for the CredDelete Win32 API; must be 0.</param>
            /// <returns>returns a System.Boolean that is true when the credential was deleted successfully.</returns>
            [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
            public static extern bool CredDelete(string target, int type, int flags);
        }
    }
}
