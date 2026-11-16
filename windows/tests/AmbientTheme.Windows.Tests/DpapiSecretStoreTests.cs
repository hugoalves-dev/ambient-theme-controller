using System.Text;
using AmbientTheme.Windows;

namespace AmbientTheme.Windows.Tests;

public sealed class DpapiSecretStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "atc-secrets-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Secret_roundtrips_for_the_current_user()
    {
        new DpapiSecretStore(_directory).Write("credential-dev-1", "s3cret-value");

        Assert.Equal("s3cret-value", new DpapiSecretStore(_directory).Read("credential-dev-1"));
    }

    [Fact]
    public void Secret_is_not_stored_in_clear_text()
    {
        new DpapiSecretStore(_directory).Write("credential", "s3cret-value");

        var bytes = File.ReadAllBytes(Assert.Single(Directory.GetFiles(_directory)));
        Assert.DoesNotContain("s3cret-value", Encoding.UTF8.GetString(bytes));
        Assert.DoesNotContain("s3cret-value", Encoding.Unicode.GetString(bytes));
    }

    [Fact]
    public void Missing_or_deleted_secret_reads_as_null()
    {
        var store = new DpapiSecretStore(_directory);
        Assert.Null(store.Read("absent"));

        store.Write("credential", "value");
        store.Delete("credential");

        Assert.Null(store.Read("credential"));
    }

    [Fact]
    public void Tampered_secret_reads_as_null()
    {
        var store = new DpapiSecretStore(_directory);
        store.Write("credential", "value");
        File.WriteAllBytes(Directory.GetFiles(_directory)[0], [1, 2, 3]);

        Assert.Null(store.Read("credential"));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("a\\b")]
    [InlineData("")]
    public void Names_that_could_escape_the_directory_are_refused(string name)
    {
        Assert.Throws<ArgumentException>(() => new DpapiSecretStore(_directory).Write(name, "value"));
    }
}
