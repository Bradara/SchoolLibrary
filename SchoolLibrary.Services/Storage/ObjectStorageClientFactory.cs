using Amazon.Runtime;
using Amazon.S3;

namespace SchoolLibrary.Services.Storage;

/// <summary>
/// Builds an S3 client configured for OCI Object Storage's S3-compatible API. Kept separate from
/// <see cref="OciObjectStorage"/> so the storage itself depends only on <see cref="IAmazonS3"/> and
/// stays unit-testable with a mock.
/// </summary>
public static class ObjectStorageClientFactory
{
    public static IAmazonS3 Create(ObjectStorageOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        var config = new AmazonS3Config
        {
            ServiceURL = options.Endpoint,
            AuthenticationRegion = options.Region,

            // OCI addresses buckets as endpoint/bucket/key, not bucket.endpoint.
            ForcePathStyle = true,

            // OCI's S3 compatibility rejects the default integrity checksums the AWS SDK v4 adds to
            // every request/response, so only send them when an operation truly needs one.
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
        };

        var credentials = new BasicAWSCredentials(options.AccessKey, options.SecretKey);
        return new AmazonS3Client(credentials, config);
    }
}
