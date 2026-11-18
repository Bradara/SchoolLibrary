namespace SchoolLibrary.Services.Storage;

/// <summary>
/// Settings for an S3-compatible object storage backend (OCI Object Storage). Bound from the
/// "FileStorage:Oci" configuration section; the secret values come from the environment, never from
/// a file in the repository.
/// </summary>
public class ObjectStorageOptions
{
    /// <summary>
    /// The S3-compatible endpoint, for example
    /// "https://&lt;namespace&gt;.compat.objectstorage.&lt;region&gt;.oraclecloud.com".
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>The region used when signing requests, for example "eu-frankfurt-1".</summary>
    public string Region { get; set; } = string.Empty;

    /// <summary>The bucket that holds the uploaded files.</summary>
    public string Bucket { get; set; } = string.Empty;

    /// <summary>The Customer Secret Key access id.</summary>
    public string AccessKey { get; set; } = string.Empty;

    /// <summary>The Customer Secret Key secret.</summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>Throws when a required value is missing, naming the setting that is not configured.</summary>
    public void Validate()
    {
        Require(Endpoint, nameof(Endpoint));
        Require(Region, nameof(Region));
        Require(Bucket, nameof(Bucket));
        Require(AccessKey, nameof(AccessKey));
        Require(SecretKey, nameof(SecretKey));
    }

    private static void Require(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Object storage is not fully configured: FileStorage:Oci:{name} is missing. " +
                "Set it with the FileStorage__Oci__" + name + " environment variable.");
        }
    }
}
