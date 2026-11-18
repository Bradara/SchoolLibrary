using System.Text;
using SchoolLibrary.Services.Storage;

namespace SchoolLibrary.Tests.Services;

public class LocalFileStorageTests : IDisposable
{
    private readonly string _root;
    private readonly LocalFileStorage _storage;

    public LocalFileStorageTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "sl-storage-tests", Guid.NewGuid().ToString("N"));
        _storage = new LocalFileStorage(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static Stream Bytes(string text) => new MemoryStream(Encoding.UTF8.GetBytes(text));

    private static async Task<string> ReadAllAsync(Stream stream)
    {
        await using (stream)
        using (var reader = new StreamReader(stream))
        {
            return await reader.ReadToEndAsync();
        }
    }

    [Fact]
    public async Task SaveAsync_CreatesTheRootFolder_WhenMissing()
    {
        Assert.False(Directory.Exists(_root));

        await _storage.SaveAsync("a.txt", Bytes("x"));

        Assert.True(Directory.Exists(_root));
    }

    [Fact]
    public async Task SaveAsync_ThenOpenReadAsync_ReturnsTheSameContent()
    {
        await _storage.SaveAsync("resources/a.md", Bytes("# Hello"));

        var stream = await _storage.OpenReadAsync("resources/a.md");

        Assert.NotNull(stream);
        Assert.Equal("# Hello", await ReadAllAsync(stream!));
    }

    [Fact]
    public async Task OpenReadAsync_ReturnsNull_WhenKeyMissing()
    {
        Assert.Null(await _storage.OpenReadAsync("resources/missing.pdf"));
    }

    [Fact]
    public async Task ExistsAsync_IsTrueOnlyAfterSave()
    {
        Assert.False(await _storage.ExistsAsync("resources/x.pdf"));

        await _storage.SaveAsync("resources/x.pdf", Bytes("data"));

        Assert.True(await _storage.ExistsAsync("resources/x.pdf"));
    }

    [Fact]
    public async Task SaveAsync_OverwritesAnExistingFile()
    {
        await _storage.SaveAsync("a.txt", Bytes("old"));
        await _storage.SaveAsync("a.txt", Bytes("new"));

        Assert.Equal("new", await ReadAllAsync((await _storage.OpenReadAsync("a.txt"))!));
    }

    [Fact]
    public async Task DeleteAsync_RemovesTheFile_AndIsSafeWhenMissing()
    {
        await _storage.SaveAsync("a.txt", Bytes("data"));

        await _storage.DeleteAsync("a.txt");
        Assert.False(await _storage.ExistsAsync("a.txt"));

        // A second delete of a missing file does not throw
        await _storage.DeleteAsync("a.txt");
    }

    [Theory]
    [InlineData("")]
    [InlineData("../escape.txt")]
    [InlineData("a/../../escape.txt")]
    [InlineData("/etc/passwd")]
    [InlineData("C:/Windows/x")]
    [InlineData("a\\b.txt")]
    [InlineData("dir/")]
    public async Task SaveAsync_RejectsUnsafeKeys(string key)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _storage.SaveAsync(key, Bytes("x")));
    }
}
