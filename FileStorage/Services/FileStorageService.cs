using Amazon.S3;
using Amazon.S3.Model;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using System.Web;
using System.Security.Cryptography;

namespace FileStorage.Services;

public class FileStorageService : IFileStorageService
{
    private readonly string? bucketName;
    private readonly string? tableName;
    private readonly int minSize;
    private readonly uint maxSize;
    private readonly AmazonS3Client amazonS3Client;
    private readonly AmazonDynamoDBClient dynamoDbClient;

    public FileStorageService()
    {
        var config = new Config();
        if (!config.Validate())
        {
            throw new Exception("Invalid AWS credentials.");
        }
        bucketName = config.bucketName;
        tableName = config.tableName;

        // Initialize S3 client
        var amazonS3Config = new AmazonS3Config
        {
            ServiceURL = config.awsUrl,
            ForcePathStyle = true,
            UseHttp = false,
            DisableLogging = false,
            Timeout = TimeSpan.FromSeconds(60),
            MaxErrorRetry = 5
        };
        amazonS3Client = new AmazonS3Client(config.awsKey, config.awsSecret, amazonS3Config);

        // Initialize Dynamo client
        var dynamoDbConfig = new AmazonDynamoDBConfig
        {
            ServiceURL = config.awsUrl,
            UseHttp = false,
            DisableLogging = false,
            Timeout = TimeSpan.FromSeconds(60),
            MaxErrorRetry = 5
        };
        dynamoDbClient = new AmazonDynamoDBClient(config.awsKey, config.awsSecret, dynamoDbConfig);
    }

    public async Task<object> StoreFile(IFormFile file)
    {
        try
        {
            if (!ValidateFile(file))
                throw new Exception(
                    $"File size should be between {minSize / 1024.0}KB and {maxSize / Math.Pow(1024.0, 3.0)}GB. File name: {file.FileName}"
                );

            // Create an unique key for the file
            string fileName = HttpUtility.UrlEncode(file.FileName);
            string uid = Guid.NewGuid().ToString();
            string key = $"{fileName}-{uid}";

            // Start uploading using multipart upload. More info at: https://docs.aws.amazon.com/AmazonS3/latest/userguide/mpu-upload-object.html
            string uploadId = await GetUploadId(key);
            int partNumber = 1;
            var partETags = new List<PartETag>();

            using SHA256 sha256 = SHA256.Create(); // Create the SHA-256 object
            using (var fileStream = file.OpenReadStream()) // Start streaming file content
            {
                byte[] buffer = new byte[5 * 1024 * 1024]; // 5MB buffer
                int bytesRead;

                while ((bytesRead = await fileStream.ReadAsync(buffer)) > 0)
                {
                    // Handle upload data stream
                    using var memoryStream = new MemoryStream(buffer, 0, bytesRead);
                    var partETag = await UploadPart(key, uploadId, partNumber, memoryStream);
                    partETags.Add(partETag);

                    // Update the SHA-256 hash
                    sha256.TransformBlock(buffer, 0, bytesRead, null, 0);

                    partNumber++;
                }
            }

            string objectKey = await CompleteUpload(key, uploadId, partETags);
            object metadata = await PutMetadata(key, sha256, file);

            return new {
                Message = "File store successfully",
                objectKey,
                metadata
            };
        }
        catch (Exception ex)
        {
            return new {
                Message = "File store failed",
                Error = ex.Message
            };
        }
    }

    private bool ValidateFile(IFormFile file)
    {
        return file.Length < minSize || file.Length > maxSize;
    }

    /**
     * Initialize a multipart upload
     */
    private async Task<string> GetUploadId(string key)
    {
        var initiateRequest = new InitiateMultipartUploadRequest
        {
            BucketName = bucketName,
            Key = key
        };
        var initiateResponse = await amazonS3Client.InitiateMultipartUploadAsync(initiateRequest);

        return initiateResponse.UploadId;
    }

    /**
     * Upload each part
     */
    private async Task<PartETag> UploadPart(string key, string id, int partNumber, MemoryStream stream)
    {
        var uploadPartRequest = new UploadPartRequest
        {
            BucketName = bucketName,
            Key = key,
            UploadId = id,
            PartNumber = partNumber,
            InputStream = stream
        };
        var uploadPartResponse = await amazonS3Client.UploadPartAsync(uploadPartRequest);

        return new PartETag
        {
            PartNumber = partNumber,
            ETag = uploadPartResponse.ETag
        };
    }

    /**
     * Complete the multipart upload
     */
    private async Task<string> CompleteUpload(string key, string id, List<PartETag> parts)
    {
        var completeRequest = new CompleteMultipartUploadRequest
        {
            BucketName = bucketName,
            Key = key,
            UploadId = id,
            PartETags = parts
        };
        var completeResponse = await amazonS3Client.CompleteMultipartUploadAsync(completeRequest);

        return completeResponse.Key;
    }

    /**
     * Write hash to DynamoDB
     */
    private async Task<object> PutMetadata(string key, SHA256 sha256, IFormFile file)
    {
        try
        {
            var fileHash = GetFileHash(sha256, file.FileName);
            string now = DateTime.UtcNow.ToString("o");
            var putItemRequest = new PutItemRequest
            {
                TableName = tableName,
                Item = new Dictionary<string, AttributeValue>
                {
                    { "Filename", new AttributeValue { S = file.FileName } },
                    { "ContentType", new AttributeValue { S = file.ContentType } },
                    { "Size", new AttributeValue { N = file.Length.ToString() } },
                    { "Sha256", new AttributeValue { S = fileHash } },
                    { "UploadedAt", new AttributeValue { S = now } }
                }
            };
            var putItemResponse = await dynamoDbClient.PutItemAsync(putItemRequest);

            return new {
                Filename = file.FileName,
                ContentType = file.ContentType,
                Size = file.Length.ToString(),
                Sha256 = fileHash,
                UploadedAt = now,
            };
        }
        catch (Exception dbEx)
        {
            // Delete file from S3 if DynamoDB operation fails
            var deleteRequest = new DeleteObjectRequest
            {
                BucketName = bucketName,
                Key = key
            };
            await amazonS3Client.DeleteObjectAsync(deleteRequest);
            throw new Exception($"DynamoDB operation failed. File removed from S3. Error: {dbEx.Message}");
        }
    }

    /**
     * Finalize the SHA-256 hash
     */
    private static string GetFileHash(SHA256 sha256, string fileName)
    {
        sha256.TransformFinalBlock([], 0, 0);
        if (sha256.Hash == null)
        {
            throw new InvalidOperationException($"SHA256 value failed to calculate. File name: {fileName}");
        }

        return BitConverter.ToString(sha256.Hash).Replace("-", "").ToLowerInvariant();
    }
}