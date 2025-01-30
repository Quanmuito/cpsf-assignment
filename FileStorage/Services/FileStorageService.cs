using Amazon.S3;
using Amazon.S3.Model;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using System.Web;
using System.Security.Cryptography;
using System.Net;

namespace FileStorage.Services;

public class FileStorageService : IFileStorageService
{
    private readonly string? bucketName;
    private readonly string? tableName;
    private readonly int minSize;
    private readonly uint maxSize;
    private readonly IAmazonS3 amazonS3Client;
    private readonly IAmazonDynamoDB dynamoDbClient;

    public FileStorageService(Config? _config = null, IAmazonS3? _amazonS3Client = null, IAmazonDynamoDB? _dynamoDbClient = null)
    {
        var config = _config ?? new Config();
        if (!config.Validate())
        {
            throw new Exception("Invalid AWS credentials.");
        }
        bucketName = config.bucketName;
        tableName = config.tableName;
        minSize = config.minSize;
        maxSize = config.maxSize;

        if (_amazonS3Client != null)
        {
            amazonS3Client = _amazonS3Client;
        }
        else
        {
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
        }

        if (_dynamoDbClient != null)
        {
            dynamoDbClient = _dynamoDbClient;
        }
        else
        {
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
    }

    public async Task<StoreFileResponse> StoreFile(IFormFile file)
    {
        var response = new StoreFileResponse();
        try
        {
            ValidateFile(file);

            // Create an unique key for the file
            string fileName = HttpUtility.UrlEncode(file.FileName);
            string uid = Guid.NewGuid().ToString();
            string key = $"{uid}-{fileName}";

            // Start uploading using multipart upload.
            string uploadId = await GetUploadId(key);
            int partNumber = 1;
            var partETags = new List<PartETag>();

            using SHA256 sha256 = SHA256.Create(); // Create the SHA-256 object
            using (var fileStream = file.OpenReadStream()) // Start streaming file content
            {
                int bytesRead;
                byte[] buffer = new byte[5 * 1024 * 1024]; // 5MB buffer
                // Read data by chunks and write to buffer
                while ((bytesRead = await fileStream.ReadAsync(buffer)) > 0)
                {
                    // Read chunk of data from buffer to upload
                    using var stream = new MemoryStream(buffer, 0, bytesRead);
                    var partETag = await UploadPart(key, uploadId, partNumber, stream);
                    partETags.Add(partETag);

                    // Update the SHA-256 hash
                    sha256.TransformBlock(buffer, 0, bytesRead, null, 0);

                    partNumber++;
                }
            }

            // Complete upload and put record to DynamoDB
            string objectKey = await CompleteUpload(key, uploadId, partETags);
            var metadata = await PutMetadata(key, sha256, file);

            response.Message = "File store successfully";
            response.ObjectKey = objectKey;
            response.Metadata = metadata;
            return response;
        }
        catch (Exception ex)
        {
            response.Message = "File store failed";
            response.Error = ex.Message;
            return response;
        }
    }

    public async Task<Stream> DownloadFile(string key)
    {
        int attempt = 0;

        while (attempt < 3)
        {
            try
            {
                int chunkSize = 5 * 1024 * 1024; // 5MB chunks
                long start = 0;
                var outputStream = new MemoryStream();

                while (true)
                {
                    // Request a specific byte range from S3
                    using (var responseStream = await GetObject(key, start, chunkSize))
                    {
                        await responseStream.CopyToAsync(outputStream);
                    }

                    start += chunkSize; // Move to the next chunk

                    // Stop if the last chunk was smaller than chunkSize (end of file)
                    if (outputStream.Length < chunkSize)
                        break;
                }
                outputStream.Position = 0; // Reset stream position before returning
                return outputStream;
            }
            catch (AmazonS3Exception s3Ex) when (s3Ex.StatusCode == HttpStatusCode.ServiceUnavailable)
            {
                attempt++;
                await Task.Delay(TimeSpan.FromSeconds(3));
            }
            catch (Exception ex)
            {
                throw new Exception($"Error downloading file: {ex.Message}", ex);
            }
        }

        throw new Exception("Max retry attempts exceeded.");
    }

    private void ValidateFile(IFormFile file)
    {
        if (file.Length < minSize || file.Length > maxSize)
            throw new Exception(
                $"File size should be between {minSize / 1024.0}KB and {maxSize / Math.Pow(1024.0, 3.0)}GB. File name: {file.FileName}"
            );
    }

    /**
     * Initialize a multipart upload
     * https://docs.aws.amazon.com/sdkfornet/v3/apidocs/items/S3/MS3InitiateMultipartUploadAsyncInitiateMultipartUploadRequestCancellationToken.html
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
     * https://docs.aws.amazon.com/sdkfornet/v3/apidocs/items/S3/MS3UploadPartAsyncUploadPartRequestCancellationToken.html
     */
    private async Task<PartETag> UploadPart(string key, string id, int partNumber, Stream stream)
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
            PartNumber = uploadPartResponse.PartNumber,
            ETag = uploadPartResponse.ETag
        };
    }

    /**
     * Complete the multipart upload
     * https://docs.aws.amazon.com/sdkfornet/v3/apidocs/items/S3/MS3CompleteMultipartUploadAsyncCompleteMultipartUploadRequestCancellationToken.html
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
     * Delete an object from S3
     * https://docs.aws.amazon.com/sdkfornet/v3/apidocs/items/S3/MS3DeleteObjectAsyncDeleteObjectRequestCancellationToken.html
     */
    private async void DeleteObject(string key)
    {
        // Delete file from S3 if DynamoDB operation fails
        var deleteRequest = new DeleteObjectRequest
        {
            BucketName = bucketName,
            Key = key
        };
        await amazonS3Client.DeleteObjectAsync(deleteRequest);
    }

    /**
     * Get object from bucket
     * https://docs.aws.amazon.com/sdkfornet/v3/apidocs/items/S3/MS3GetObjectAsyncGetObjectRequestCancellationToken.html
     */
    private async Task<Stream> GetObject(string key, long start, int chunkSize)
    {
        var getObjectRequest = new GetObjectRequest
        {
            BucketName = bucketName,
            Key = key,
            ByteRange = new ByteRange(start, start + chunkSize - 1) // 0-based indexing
        };

        GetObjectResponse getObjectResponse = await amazonS3Client.GetObjectAsync(getObjectRequest);
        return getObjectResponse.ResponseStream;
    }

    /**
     * Write hash to DynamoDB
     * https://docs.aws.amazon.com/sdkfornet/v3/apidocs/items/DynamoDBv2/MDynamoDBPutItemAsyncPutItemRequestCancellationToken.html
     */
    private async Task<Metadata> PutMetadata(string key, SHA256 sha256, IFormFile file)
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
                    { "BucketName", new AttributeValue { S = bucketName } },
                    { "UploadedAt", new AttributeValue { S = now } }
                }
            };
            await dynamoDbClient.PutItemAsync(putItemRequest);

            return new Metadata
            {
                Filename = file.FileName,
                ContentType = file.ContentType,
                Size = file.Length,
                Sha256 = fileHash,
                BucketName = bucketName,
                UploadedAt = now
            };
        }
        catch (AmazonDynamoDBException dbEx)
        {
            DeleteObject(key);
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