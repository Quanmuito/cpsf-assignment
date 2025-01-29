using Moq;
using Xunit;
using Microsoft.AspNetCore.Http;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using FileStorage.Services;

namespace FileStorage.UnitTests;

public class FileStorageServiceTests
{
    private readonly Config _config;
    private readonly Mock<IAmazonS3> _mockAmazonS3Client;
    private readonly Mock<IAmazonDynamoDB> _mockDynamoDbClient;
    private readonly FileStorageService _fileStorageService;

    public FileStorageServiceTests()
    {
        // Mock config
        var config = new Config();
        config.awsKey = "mock-key";
        config.awsSecret = "mock-secret";
        config.awsRegion = "us-east-1";
        config.awsUrl = "http://mock-aws-url:1234";
        config.bucketName = "storage";
        config.minSize = 131072;
        config.maxSize = 2147483648;
        config.tableName = "Files";
        _config = config;

        // Mock Amazon S3 client
        _mockAmazonS3Client = new Mock<IAmazonS3>();

        // Mock Amazon DynamoDB client
        _mockDynamoDbClient = new Mock<IAmazonDynamoDB>();

        // Initialize FileStorageService with mocked clients
        _fileStorageService = new FileStorageService(_config, _mockAmazonS3Client.Object, _mockDynamoDbClient.Object);
    }

    [Fact]
    public async Task Store_File_Success()
    {
        var expectedResult = new StoreFileResponse
        {
            Message = "File store successfully",
            ObjectKey = "text.txt-3f5b8c96-7d71-4c41-98d4-8762f34729a5",
            Metadata = new Metadata {
                Filename = "text.txt",
                ContentType = "text/plain",
                Size = 153600,
                Sha256 = "8149d065f3817616faae5b86bdaec5c1b6fabdd77ec1a2dad656d65383e8092f",
                BucketName = _config.bucketName,
                UploadedAt = "2025-01-29T16:02:26.7651004Z"
            },
            Error = null,
        };

        // Set up chain of responses for AWS clients
        SetUpAWSMockResponse(expectedResult.ObjectKey);

        // Action
        var response = await _fileStorageService.StoreFile(GetMockFile());

        // Assert main properties
        Assert.NotNull(response);
        Assert.Equal(expectedResult.Message, response.Message);
        Assert.Equal(expectedResult.ObjectKey, response.ObjectKey);
        Assert.Null(response.Error);

        // Assert metadata properties
        Assert.NotNull(response.Metadata);
        Assert.Equal(expectedResult.Metadata.Filename, response.Metadata.Filename);
        Assert.Equal(expectedResult.Metadata.ContentType, response.Metadata.ContentType);
        Assert.Equal(expectedResult.Metadata.Size, response.Metadata.Size);
        Assert.Equal(expectedResult.Metadata.Sha256.Length, response.Metadata.Sha256.Length);
        Assert.Equal(expectedResult.Metadata.BucketName, response.Metadata.BucketName);
        Assert.True(DateTime.TryParse(response.Metadata.UploadedAt, out _));
    }

    [Fact]
    public async Task Store_File_Invalid_File_Size()
    {
        var expectedResult = new StoreFileResponse
        {
            Message = "File store failed",
            ObjectKey = null,
            Metadata = null,
            Error = "File size should be between 128KB and 2GB. File name: text.txt",
        };

        // Action
        var response = await _fileStorageService.StoreFile(GetMockFile(100));

        // Assert main properties
        Assert.NotNull(response);
        Assert.Equal(expectedResult.Message, response.Message);
        Assert.Null(response.ObjectKey);
        Assert.Null(response.Metadata);
        Assert.Equal(expectedResult.Error, response.Error);
    }

    [Fact]
    public async Task Store_File_Upload_Success_Put_Fail()
    {

        SetUpAWSMockResponse("text.txt-3f5b8c96-7d71-4c41-98d4-8762f34729a5", true);

        var expectedResult = new StoreFileResponse
        {
            Message = "File store failed",
            ObjectKey = null,
            Metadata = null,
            Error = "DynamoDB operation failed. File removed from S3. Error: Cannot put to DynamoDB",
        };

        // Action
        var response = await _fileStorageService.StoreFile(GetMockFile());

        // Assert main properties
        Assert.NotNull(response);
        Assert.Equal(expectedResult.Message, response.Message);
        Assert.Null(response.ObjectKey);
        Assert.Null(response.Metadata);
        Assert.Equal(expectedResult.Error, response.Error);
    }

    private void SetUpAWSMockResponse(string key, bool fail = false)
    {
        // Responses for S3 client
        var mockInitiateMultipartUploadResponse = new InitiateMultipartUploadResponse
        {
            UploadId = "VXBsb2FkSWQxMjM0NTY3ODkw",
            BucketName = _config.bucketName,
            Key = key,
        };
        _mockAmazonS3Client
            .Setup(s3 => s3.InitiateMultipartUploadAsync(
                    It.Is<InitiateMultipartUploadRequest>(req => true),
                    It.IsAny<CancellationToken>()
                ))
            .ReturnsAsync(mockInitiateMultipartUploadResponse);

        var uploadPartResponse = new UploadPartResponse
        {
            ETag = "\"d8c2eafd90c266e19ab9dcacc479f8af\"",
            PartNumber = 1
        };
        _mockAmazonS3Client
            .Setup(s3 => s3.UploadPartAsync(
                    It.Is<UploadPartRequest>(req => true),
                    It.IsAny<CancellationToken>()
                ))
            .ReturnsAsync(uploadPartResponse);

        var completeMultipartUploadResponse = new CompleteMultipartUploadResponse
        {
            Location = "https://storage.s3.amazonaws.com/path/to/text.txt",
            BucketName = _config.bucketName,
            Key = key,
            ETag = "\"d8c2eafd90c266e19ab9dcacc479f8af\""
        };
        _mockAmazonS3Client
            .Setup(s3 => s3.CompleteMultipartUploadAsync(
                    It.Is<CompleteMultipartUploadRequest>(req => true),
                    It.IsAny<CancellationToken>()
                ))
            .ReturnsAsync(completeMultipartUploadResponse);

        _mockAmazonS3Client
            .Setup(s3 => s3.DeleteObjectAsync(
                    It.Is<DeleteObjectRequest>(req => true),
                    It.IsAny<CancellationToken>()
                ))
            .ReturnsAsync(new DeleteObjectResponse());

        // Responses for DynamoDb client
        if (fail)
        {
            _mockDynamoDbClient
            .Setup(dynamoDb => dynamoDb.PutItemAsync(
                    It.Is<PutItemRequest>(req => true),
                    It.IsAny<CancellationToken>()
                ))
            .ThrowsAsync(new Exception("Cannot put to DynamoDB"));
        }
        else {
            _mockDynamoDbClient
            .Setup(dynamoDb => dynamoDb.PutItemAsync(
                    It.Is<PutItemRequest>(req => true),
                    It.IsAny<CancellationToken>()
                ))
            .ReturnsAsync(new PutItemResponse());
        }
    }

    private FormFile GetMockFile(uint? size = null)
    {
        var fileSize = size ?? 153600; // Default 150KB
        var fileContent = new byte[fileSize];
        new Random().NextBytes(fileContent);
        var mockFile = new FormFile(new MemoryStream(fileContent), 0, fileSize, "Data", "text.txt")
        {
            Headers = new HeaderDictionary(),
            ContentType = "text/plain"
        };
        return mockFile;
    }
}