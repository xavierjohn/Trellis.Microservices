namespace E2EHarness;

using Gateway;

/// <summary>
/// Unit tests for the sample gateway's <see cref="DevSigningKey.LoadOrCreate"/> helper: the dev-only
/// opt-in key persistence that lets locally minted tokens survive a gateway restart.
/// </summary>
public sealed class DevSigningKeyTests
{
    [Fact]
    public void LoadOrCreate_NoPath_GeneratesFreshEphemeralKeyEachCall()
    {
        var first = DevSigningKey.LoadOrCreate(null);
        var second = DevSigningKey.LoadOrCreate(null);

        // Zero-config default: a fresh key (and therefore a fresh kid) on every call.
        first.KeyId.Should().NotBe(second.KeyId);
    }

    [Fact]
    public void LoadOrCreate_PathWithNoFile_GeneratesAndPersistsTheKey()
    {
        var path = Path.Combine(Path.GetTempPath(), $"trellis-dev-key-{Guid.NewGuid():N}.pem");
        try
        {
            var key = DevSigningKey.LoadOrCreate(path);

            File.Exists(path).Should().BeTrue("the key is persisted so it survives a restart");
            key.KeyId.Should().StartWith("sample-key-");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadOrCreate_PathInNonexistentDirectory_CreatesDirectoryAndPersists()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"trellis-dev-key-dir-{Guid.NewGuid():N}");
        var path = Path.Combine(dir, "dev.pem");
        try
        {
            var key = DevSigningKey.LoadOrCreate(path);

            Directory.Exists(dir).Should().BeTrue("the opt-in path creates a missing directory");
            File.Exists(path).Should().BeTrue();
            key.KeyId.Should().StartWith("sample-key-");
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void LoadOrCreate_ExistingFile_ReloadsTheSameKeyAcrossRestart()
    {
        var path = Path.Combine(Path.GetTempPath(), $"trellis-dev-key-{Guid.NewGuid():N}.pem");
        try
        {
            var beforeRestart = DevSigningKey.LoadOrCreate(path);   // generates + saves
            var afterRestart = DevSigningKey.LoadOrCreate(path);    // simulates a gateway restart

            // The persisted key is reused, so the kid is stable and the same key material is loaded.
            afterRestart.KeyId.Should().Be(beforeRestart.KeyId);
            afterRestart.Rsa.ExportSubjectPublicKeyInfo()
                .Should().Equal(beforeRestart.Rsa.ExportSubjectPublicKeyInfo());
        }
        finally
        {
            File.Delete(path);
        }
    }
}
