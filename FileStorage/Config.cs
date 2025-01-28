using System;

namespace FileStorage;

public class Config
{
    public string? awsKey;
    public string? awsSecret;
    public string? awsRegion;
    public string? awsUrl;
    public string? bucketName;
    public int minSize;
    public uint maxSize;

    public Config()
    {
        awsKey = Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID");
        awsSecret = Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY");
        awsRegion = Environment.GetEnvironmentVariable("AWS_REGION");
        awsUrl = Environment.GetEnvironmentVariable("AWS_ENDPOINT_URL");
        bucketName = Environment.GetEnvironmentVariable("AWS_BUCKET_NAME");
        minSize = Convert.ToInt32(Environment.GetEnvironmentVariable("MIN_SIZE"));
        maxSize = Convert.ToUInt32(Environment.GetEnvironmentVariable("MAX_SIZE"));
    }

    public bool Validate()
    {
        return awsKey != null && awsSecret != null && awsRegion != null && awsUrl != null && bucketName != null;
    }
}