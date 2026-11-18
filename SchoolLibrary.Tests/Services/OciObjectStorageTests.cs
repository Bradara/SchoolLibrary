using System.Net;
using System.Text;
using Amazon.S3;
using Amazon.S3.Model;
using Moq;
using SchoolLibrary.Services.Storage;

namespace SchoolLibrary.Tests.Services;

public class OciObjectStorageTests
{
    private const string Bucket = "schoollibrary-uploads";

    private readonly Mock<IAmazonS3> _client = new(MockBehavior.Strict);
    private readonly OciObjectStorage _storage;

    public OciObjectStorageTests()
    {
        _storage = new OciObjectStorage(_client.Object, Bucket);
    }

    private static AmazonS3Exception NotFound() =>
        new("Not found") { StatusCode = HttpStatusCode.NotFound };

    [Fact]
    public void Constructor_RejectsEmptyBucket()
    {
        Assert.Throws<ArgumentException>(() => new OciObjectStorage(_client.Object, " "));
    }

    [Fact]
    public async Task SaveAsync_PutsTheObjectInTheBucket_WithoutClosingTheStream()
    {
        PutObjectRequest? sent = null;
        _client
            .Setup(c => c.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PutObjectRequest, CancellationToken>((r, _) => sent = r)
            .ReturnsAsync(new PutObjectResponse());

        var content = new MemoryStream(Encoding.UTF8.GetBytes("data"));
        await _storage.SaveAsync("resources/a.pdf", content);

        Assert.NotNull(sent);
        Assert.Equal(Bucket, sent!.BucketName);
        Assert.Equal("resources/a.pdf", sent.Key);
        Assert.False(sent.AutoCloseStream);
        // The caller's stream stays open for reuse (e.g. hashing or a retry)
        Assert.True(content.CanRead);
    }

    [Fact]
    public async Task OpenReadAsync_ReturnsTheObjectStream()
    {
        var payload = new MemoryStream(Encoding.UTF8.GetBytes("hello"));
        _client
            .Setup(c => c.GetObjectAsync(Bucket, "resources/a.md", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetObjectResponse { ResponseStream = payload });

        var stream = await _storage.OpenReadAsync("resources/a.md");

        Assert.NotNull(stream);
        using var reader = new StreamReader(stream!);
        Assert.Equal("hello", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task OpenReadAsync_ReturnsNull_WhenMissing()
    {
        _client
            .Setup(c => c.GetObjectAsync(Bucket, "missing", It.IsAny<CancellationToken>()))
            .ThrowsAsync(NotFound());

        Assert.Null(await _storage.OpenReadAsync("missing"));
    }

    [Fact]
    public async Task DeleteAsync_DeletesTheObject()
    {
        _client
            .Setup(c => c.DeleteObjectAsync(Bucket, "resources/a.pdf", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteObjectResponse());

        await _storage.DeleteAsync("resources/a.pdf");

        _client.Verify(c => c.DeleteObjectAsync(Bucket, "resources/a.pdf", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_IsSafe_WhenMissing()
    {
        _client
            .Setup(c => c.DeleteObjectAsync(Bucket, "gone", It.IsAny<CancellationToken>()))
            .ThrowsAsync(NotFound());

        // Does not throw
        await _storage.DeleteAsync("gone");
    }

    [Fact]
    public async Task ExistsAsync_IsTrue_WhenMetadataIsReturned()
    {
        _client
            .Setup(c => c.GetObjectMetadataAsync(Bucket, "resources/a.pdf", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetObjectMetadataResponse());

        Assert.True(await _storage.ExistsAsync("resources/a.pdf"));
    }

    [Fact]
    public async Task ExistsAsync_IsFalse_WhenMissing()
    {
        _client
            .Setup(c => c.GetObjectMetadataAsync(Bucket, "missing", It.IsAny<CancellationToken>()))
            .ThrowsAsync(NotFound());

        Assert.False(await _storage.ExistsAsync("missing"));
    }
}
