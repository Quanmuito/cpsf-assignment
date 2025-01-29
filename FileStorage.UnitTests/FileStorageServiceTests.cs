using System;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Moq;
using Xunit;
using FileStorage.Services;
using Microsoft.AspNetCore.Http;
using System.Text.Json;

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
        config.awsKey = "key";
        config.awsSecret = "secret";
        config.awsRegion = "us-east-1";
        config.awsUrl = "http://localstack-cpsf:4566";
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
            }
        };

        // Set up chain of responses
        var mockInitiateMultipartUploadResponse = new InitiateMultipartUploadResponse
        {
            UploadId = "VXBsb2FkSWQxMjM0NTY3ODkw",
            BucketName = expectedResult.Metadata.BucketName,
            Key = expectedResult.ObjectKey
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
            BucketName = expectedResult.Metadata.BucketName,
            Key = expectedResult.ObjectKey,
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

        _mockDynamoDbClient
            .Setup(dynamoDb => dynamoDb.PutItemAsync(
                    It.Is<PutItemRequest>(req => true),
                    It.IsAny<CancellationToken>()
                ))
            .ReturnsAsync(new PutItemResponse());

        var fileContent = new byte[153600];
        new Random().NextBytes(fileContent);

        var mockFile = new FormFile(new MemoryStream(fileContent), 0, 153600, "Data", "text.txt")
        {
            Headers = new HeaderDictionary(),
            ContentType = "text/plain"
        };

        var response = await _fileStorageService.StoreFile(mockFile);
        Assert.NotNull(response);
        Console.WriteLine(JsonSerializer.Serialize(response));

        // Assert main properties
        var result = (StoreFileResponse)response;
        Assert.Equal(expectedResult.Message, result.Message);
        Assert.Equal(expectedResult.ObjectKey, result.ObjectKey);

        // // Assert metadata properties
        Assert.NotNull(result.Metadata);
        var metadata = result.Metadata;
        Assert.Equal(expectedResult.Metadata.Filename, metadata.Filename);
        Assert.Equal(expectedResult.Metadata.ContentType, metadata.ContentType);
        Assert.Equal(expectedResult.Metadata.Size, metadata.Size);
        Assert.Equal(expectedResult.Metadata.Sha256.Length, metadata.Sha256.Length);
        Assert.Equal(expectedResult.Metadata.BucketName, metadata.BucketName);
        Assert.True(DateTime.TryParse(metadata.UploadedAt, out _));
    }
}