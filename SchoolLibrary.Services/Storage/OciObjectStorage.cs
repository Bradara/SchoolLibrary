using System.Net;
using Amazon.S3;
using Amazon.S3.Model;

namespace SchoolLibrary.Services.Storage;

/// <summary>
/// Keeps uploaded files in an S3-compatible bucket (OCI Object Storage), so they live off the server's
/// disk and survive without a mounted volume. Only the key and metadata stay in the database.
/// </summary>
public class OciObjectStorage : IFileStorage
{
    private readonly IAmazonS3 _client;
    private readonly string _bucket;

    public OciObjectStorage(IAmazonS3 client, string bucket)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        if (string.IsNullOrWhiteSpace(bucket))
        {
            throw new ArgumentException("The bucket name is required.", nameof(bucket));
        }

        _bucket = bucket;
    }

    public async Task SaveAsync(string key, Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var request = new PutObjectRequest
        {
            BucketName = _bucket,
            Key = key,
            InputStream = content,
            AutoCloseStream = false,       // the caller owns the stream
            DisablePayloadSigning = true,  // send UNSIGNED-PAYLOAD: avoids aws-chunked bodies OCI does not accept
        };

        await _client.PutObjectAsync(request, cancellationToken);
    }

    public async Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.GetObjectAsync(_bucket, key, cancellationToken);
            return response.ResponseStream;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.DeleteObjectAsync(_bucket, key, cancellationToken);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // Deleting a missing object is not an error
        }
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.GetObjectMetadataAsync(_bucket, key, cancellationToken);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }
}
