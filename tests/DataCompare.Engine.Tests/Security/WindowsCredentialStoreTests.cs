using DataCompare.Engine.Security;

namespace DataCompare.Engine.Tests.Security
{

    /// <summary>
    /// tests for the DataCompare.Engine.Security.WindowsCredentialStore class, covering saving,
    /// retrieving, and deleting credentials from the Windows Credential Manager.
    /// </summary>
    public sealed class WindowsCredentialStoreTests : IDisposable
    {
        private readonly string _target = $"DataCompare:Test:{Guid.NewGuid()}";
        private readonly WindowsCredentialStore _store = new();

        [Fact]
        public void SaveAndRetrievePassword_RoundTrips()
        {
            _store.SavePassword(_target, "test-user", "s3cr3t-P@ss");

            var retrieved = _store.TryGetPassword(_target);

            Assert.Equal("s3cr3t-P@ss", retrieved);
        }

        [Fact]
        public void TryGetPassword_ReturnsNull_WhenCredentialDoesNotExist()
        {
            var missingTarget = $"DataCompare:DoesNotExist:{Guid.NewGuid()}";

            var retrieved = _store.TryGetPassword(missingTarget);

            Assert.Null(retrieved);
        }

        [Fact]
        public void DeletePassword_RemovesCredential()
        {
            _store.SavePassword(_target, "test-user", "s3cr3t-P@ss");

            _store.DeletePassword(_target);

            Assert.Null(_store.TryGetPassword(_target));
        }

        [Fact]
        public void DeletePassword_DoesNotThrow_WhenCredentialDoesNotExist()
        {
            var missingTarget = $"DataCompare:DoesNotExist:{Guid.NewGuid()}";

            var exception = Record.Exception(() => _store.DeletePassword(missingTarget));

            Assert.Null(exception);
        }

        /// <summary>
        /// removes the test credential created by this fixture from the Windows Credential Manager
        /// after each test runs.
        /// </summary>
        public void Dispose() => _store.DeletePassword(_target);
    }
}
