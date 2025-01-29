using Microsoft.AspNetCore.Mvc;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Web;
using System.Security.Cryptography;

namespace FileStorage.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FileStorageController : ControllerBase
{
    private readonly ILogger<FileStorageController> logger;
    public FileStorageController(ILogger<FileStorageController> _logger)
    {
        logger = _logger;
    }

    [HttpGet]
    public string[] Get()
    {
        string[] env = [
            Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID") ?? "",
            Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY") ?? "",
            Environment.GetEnvironmentVariable("AWS_REGION") ?? "",
            Environment.GetEnvironmentVariable("AWS_ENDPOINT_URL") ?? "",
            Environment.GetEnvironmentVariable("FileS_AWS__ServiceURL") ?? "",
        ];
        logger.LogError("An internal server error occurred.");
        return env;
    }

    [HttpPost]
    public async Task<IActionResult> Upload()
    {
        IFormFileCollection? files = Request.Form.Files;

        if (!files.Any())
        {
            return BadRequest("No files uploaded.");
        }

        var config = new Config();
        if (!config.Validate())
        {
            return StatusCode(500, "Invalid AWS credentials.");
        }

        var amazonS3Config = new AmazonS3Config
        {
            ServiceURL = config.awsUrl,
            ForcePathStyle = true,
            UseHttp = false,
            DisableLogging = false,
            Timeout = TimeSpan.FromSeconds(60),
            MaxErrorRetry = 5
        };
        var amazonS3Client = new AmazonS3Client(config.awsKey, config.awsSecret, amazonS3Config);

        var dynamoDbConfig = new AmazonDynamoDBConfig
        {
            ServiceURL = config.awsUrl,
            UseHttp = false,
            DisableLogging = false,
            Timeout = TimeSpan.FromSeconds(60),
            MaxErrorRetry = 5
        };
        var dynamoDbClient = new AmazonDynamoDBClient(config.awsKey, config.awsSecret, dynamoDbConfig);

        try
        {
            var bucketName = config.bucketName;
            var uploadedFiles = new List<string>();
            var hashes = new List<string>();

            foreach (var file in files)
            {
                if (file.Length < config.minSize || file.Length > config.maxSize)
                {
                    throw new Exception($"File size should be between 128KB and 2GB. File name: {file.FileName}");
                }

                string fileName = HttpUtility.UrlEncode(file.FileName);
                string uid = Guid.NewGuid().ToString();
                string key = $"{fileName}-{uid}";

                // Initialize a multipart upload
                var initiateRequest = new InitiateMultipartUploadRequest
                {
                    BucketName = bucketName,
                    Key = key
                };
                var initiateResponse = await amazonS3Client.InitiateMultipartUploadAsync(initiateRequest);

                string uploadId = initiateResponse.UploadId;
                int partNumber = 1;
                var partETags = new List<PartETag>();

                // Create the SHA-256 object
                using var sha256 = SHA256.Create();
                using (var fileStream = file.OpenReadStream())
                {
                    byte[] buffer = new byte[5 * 1024 * 1024]; // 5MB buffer
                    int bytesRead;

                    // Upload in chunks
                    while ((bytesRead = await fileStream.ReadAsync(buffer)) > 0)
                    {
                        // Update the SHA-256 hash
                        sha256.TransformBlock(buffer, 0, bytesRead, null, 0);

                         // Upload the chunk
                        using var memoryStream = new MemoryStream(buffer, 0, bytesRead);
                        var uploadPartRequest = new UploadPartRequest
                        {
                            BucketName = bucketName,
                            Key = key,
                            UploadId = uploadId,
                            PartNumber = partNumber,
                            InputStream = memoryStream
                        };
                        var uploadPartResponse = await amazonS3Client.UploadPartAsync(uploadPartRequest);

                        var partETag = new PartETag
                        {
                            PartNumber = partNumber,
                            ETag = uploadPartResponse.ETag
                        };
                        partETags.Add(partETag);

                        partNumber++;
                    }

                    // Finalize the SHA-256 hash
                    sha256.TransformFinalBlock([], 0, 0);
                    if (sha256.Hash == null)
                    {
                        throw new InvalidOperationException($"SHA256 value failed to calculate. File name: {fileName}");
                    }
                }

                // Complete the multipart upload
                var completeRequest = new CompleteMultipartUploadRequest
                {
                    BucketName = bucketName,
                    Key = key,
                    UploadId = uploadId,
                    PartETags = partETags
                };
                var completeResponse = await amazonS3Client.CompleteMultipartUploadAsync(completeRequest);
                uploadedFiles.Add(completeResponse.Key);

                try
                {
                    // Write hash to DynamoDB
                    var fileHash = BitConverter.ToString(sha256.Hash).Replace("-", "").ToLowerInvariant();
                    var putItemRequest = new PutItemRequest
                    {
                        TableName = config.tableName,
                        ReturnValues = "ALL_OLD",
                        Item = new Dictionary<string, AttributeValue>
                        {
                            { "Filename", new AttributeValue { S = fileName } },
                            { "Sha256", new AttributeValue { S = fileHash } },
                            { "UploadedAt", new AttributeValue { S = DateTime.UtcNow.ToString("o") } }
                        }
                    };
                    var putItemResponse = await dynamoDbClient.PutItemAsync(putItemRequest);
                    hashes.Add(fileHash);
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

            object response = new
            {
                message = "File uploaded successfully",
                bucketName,
                uploadedFiles,
                hashes
            };
            return Ok(response);
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"File upload failed: {ex.Message}");
        }
    }
}