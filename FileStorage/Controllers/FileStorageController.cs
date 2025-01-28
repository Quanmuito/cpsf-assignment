using Microsoft.AspNetCore.Mvc;
using Amazon.S3;
using Amazon.S3.Model;
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
            return NoContent();
        }

        var config = new Config();
        if (!config.Validate())
        {
            return StatusCode(500, "Invalid AWS credentials.");
        }

        var awsS3Config = new AmazonS3Config
        {
            ServiceURL = config.awsUrl,
            ForcePathStyle = true,
            UseHttp = false,
            DisableLogging = false,
            Timeout = TimeSpan.FromSeconds(60),
            MaxErrorRetry = 5
        };
        var awsS3Client = new AmazonS3Client(config.awsKey, config.awsSecret, awsS3Config);

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
                var initiateResponse = await awsS3Client.InitiateMultipartUploadAsync(initiateRequest);

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
                        var uploadPartResponse = await awsS3Client.UploadPartAsync(uploadPartRequest);

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
                var completeResponse = await awsS3Client.CompleteMultipartUploadAsync(completeRequest);
                var fileHash = BitConverter.ToString(sha256.Hash).Replace("-", "").ToLowerInvariant();

                // Get the hash as a hexadecimal string
                uploadedFiles.Add(completeResponse.Key);
                hashes.Add(fileHash);
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