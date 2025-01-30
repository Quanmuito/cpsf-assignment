using Moq;
using Xunit;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using FileStorage.Controllers;
using FileStorage.Services;

namespace FileStorage.UnitTests;

public class FileStorageControllerTests
{
    private readonly Mock<IFileStorageService> mockFileStorageService;
    private readonly FileStorageController controller;

    public FileStorageControllerTests()
    {
        // Initialize the mock service
        mockFileStorageService = new Mock<IFileStorageService>();

        // Inject the mock service into the controller
        controller = new FileStorageController(mockFileStorageService.Object);
    }

    [Fact]
    public async Task Upload_With_File()
    {
        // Given
        var context = new DefaultHttpContext();
        context.Request.Form = GetFormCollection();
        controller.ControllerContext = new ControllerContext()
        {
            HttpContext = context
        };

        // Mock StoreFile successfully upload
        var mockResponse = new StoreFileResponse
        {
            Message = "File store successfully",
            ObjectKey = "3f5b8c96-7d71-4c41-98d4-8762f34729a5-test.txt",
            Metadata = new Metadata
            {
                Filename = "test.txt",
                ContentType = "text/plain",
                Size = 10,
                Sha256 = "afd4fddfd45c5078d83d6294d1d4df6f179b2ff50288a76d719e3f2194fbf992",
                BucketName = "storage",
                UploadedAt = "2025-01-29T14:59:44.6173079Z"
            },
            Error = null,

        };
        mockFileStorageService.Setup(service => service.StoreFile(It.IsAny<IFormFile>())).ReturnsAsync(mockResponse);

        // When
        var response = await controller.Upload();

        // Assert
        var result = Assert.IsType<OkObjectResult>(response);
        var objectList = Assert.IsType<List<StoreFileResponse>>(result.Value);

        Assert.Equal(200, result.StatusCode);

        // Assert main properties
        var firstObject = objectList[0];
        Assert.Equal(mockResponse.Message, firstObject.Message);
        Assert.Equal(mockResponse.ObjectKey, firstObject.ObjectKey);
        Assert.NotNull(firstObject.Metadata);

        // Assert metadata properties
        var metadata = firstObject.Metadata;
        Assert.Equal(mockResponse.Metadata.Filename, metadata.Filename);
        Assert.Equal(mockResponse.Metadata.ContentType, metadata.ContentType);
        Assert.Equal(mockResponse.Metadata.Size, metadata.Size);
        Assert.Equal(mockResponse.Metadata.Sha256, metadata.Sha256);
        Assert.Equal(mockResponse.Metadata.BucketName, metadata.BucketName);
        Assert.Equal(mockResponse.Metadata.UploadedAt, metadata.UploadedAt);
    }

    [Fact]
    public async Task Upload_With_No_File()
    {
        // Given
        var context = new DefaultHttpContext();
        context.Request.Form = new FormCollection([]); // Empty collection
        controller.ControllerContext = new ControllerContext()
        {
            HttpContext = context
        };

        // When
        var response = await controller.Upload();

        // Assert
        var result = Assert.IsType<BadRequestObjectResult>(response);
        Assert.Equal(400, result.StatusCode);
        Assert.Equal("No files uploaded.", result.Value);
    }

    [Fact]
    public async Task Upload_Failed()
    {
        // Given
        var context = new DefaultHttpContext();
        context.Request.Form = GetFormCollection();
        controller.ControllerContext = new ControllerContext()
        {
            HttpContext = context
        };

        // Mock StoreFile failed to upload or save metadata to db
        var mockResponse = new StoreFileResponse
        {
            Message = "File store failed.",
            Error = "DynamoDB operation failed. File removed from S3. Error: Error."
        };
        mockFileStorageService.Setup(service => service.StoreFile(It.IsAny<IFormFile>())).ReturnsAsync(mockResponse);

        // When
        var response = await controller.Upload();

        // Assert
        var result = Assert.IsType<OkObjectResult>(response);
        var objectList = Assert.IsType<List<StoreFileResponse>>(result.Value);

        Assert.Equal(200, result.StatusCode);

        // Assert main properties
        var firstObject = objectList[0];
        Assert.Equal(mockResponse.Message, firstObject.Message);
        Assert.Null(firstObject.ObjectKey);
        Assert.Null(firstObject.Metadata);
        Assert.Equal(mockResponse.Error, firstObject.Error);
    }

    [Fact]
    public async Task Upload_Throw_Exception()
    {
        // Given
        var context = new DefaultHttpContext();
        context.Request.Form = GetFormCollection();
        controller.ControllerContext = new ControllerContext()
        {
            HttpContext = context
        };

        // Mock service throw error (for example: invalid credentials)
        mockFileStorageService.Setup(service => service.StoreFile(It.IsAny<IFormFile>()))
            .ThrowsAsync(new Exception("Invalid AWS credentials."));

        // When
        var response = await controller.Upload();

        // Assert
        var result = Assert.IsType<ObjectResult>(response);
        Assert.Equal(500, result.StatusCode);
        Assert.Contains("Something went wrong:", result.Value.ToString());
    }

    [Fact]
    public async Task Download_File()
    {
        // Given
        string fileName = "3f5b8c96-7d71-4c41-98d4-8762f34729a5-test.txt";
        string fileContent = "This is a test file";
        var fileStream = new MemoryStream(Encoding.UTF8.GetBytes(fileContent));

        mockFileStorageService
            .Setup(service => service.DownloadFile(fileName))
            .ReturnsAsync(fileStream);

        // When
        var response = await controller.Download(fileName);

        // Assert
        var result = Assert.IsType<FileStreamResult>(response);

        // Validate content type and file name
        Assert.NotNull(result);
        Assert.Equal("application/octet-stream", result.ContentType);
        Assert.Equal(fileName, result.FileDownloadName);

        // Ensure the response contains a valid Stream object
        Assert.NotNull(result.FileStream);
        Assert.IsType<MemoryStream>(result.FileStream);
        using (var reader = new StreamReader(result.FileStream, Encoding.UTF8))
        {
            string resultContent = await reader.ReadToEndAsync();
            Assert.Equal(fileContent, resultContent);
        }
    }

    [Fact]
    public async Task Download_File_Without_Name()
    {
        // When
        var response = await controller.Download("");

        // Assert
        var result = Assert.IsType<BadRequestObjectResult>(response);
        Assert.Equal(400, result.StatusCode);
        Assert.Equal("No file name found.", result.Value);
    }

    [Fact]
    public async Task Download_File_Not_Exist()
    {
        // Given
        string fileName = "3f5b8c96-7d71-4c41-98d4-8762f34729a5-test.txt";
        mockFileStorageService.Setup(service => service.DownloadFile(fileName)).ReturnsAsync((Stream)null!);

        // When
        var response = await controller.Download(fileName);

        // Assert
        var result = Assert.IsType<NotFoundObjectResult>(response);
        Assert.Equal("File not found.", result.Value);
    }

    [Fact]
    public async Task Download_File_Fail()
    {
        // Given
        string fileName = "3f5b8c96-7d71-4c41-98d4-8762f34729a5-test.txt";
        mockFileStorageService
            .Setup(service => service.DownloadFile(fileName))
            .ThrowsAsync(new Exception("Unexpected error"));

        // When
        var response = await controller.Download(fileName);

        // Assert
        var result = Assert.IsType<ObjectResult>(response);
        Assert.Equal(500, result.StatusCode);
        Assert.Contains("Something went wrong", result.Value.ToString());
    }

    [Fact]
    public async Task List_Success()
    {
        // Given
        var mockData = new List<Dictionary<string, string>>
        {
            new Dictionary<string, string> {
                { "Filename", "test.txt" },
                { "BucketName", "storage" },
                { "ContentType", "text/plain" },
                { "Size", "161450" },
                { "Sha256", "005853fe24980ef2d96eda4ec99d122bc7b2d621ae2f812f499912d4f184a7ef" },
                { "UploadedAt", "2025-01-30T17:40:09.6798488Z" },
            }
        };
        mockFileStorageService.Setup(service => service.ListFile()).ReturnsAsync(mockData);

        // When
        var response = await controller.List();

        // Assert
        var result = Assert.IsType<OkObjectResult>(response);
        var objectList = Assert.IsType<List<Dictionary<string, string>>>(result.Value);
    }

    [Fact]
    public async Task List_Fail()
    {
        // Given
        mockFileStorageService.Setup(service => service.ListFile()).ThrowsAsync(new Exception("Unexpected error"));;

        // When
        var response = await controller.List();

        // Assert
        var result = Assert.IsType<ObjectResult>(response);
        Assert.Equal(500, result.StatusCode);
        Assert.Contains("Something went wrong", result.Value.ToString());
    }

    private FormFile GetMockFile()
    {
        var fileSize = 153600; // Default 150KB
        var fileContent = new byte[fileSize];
        new Random().NextBytes(fileContent);
        var mockFile = new FormFile(new MemoryStream(fileContent), 0, fileSize, "Data", "test.txt")
        {
            Headers = new HeaderDictionary(),
            ContentType = "text/plain"
        };
        return mockFile;
    }

    private FormCollection GetFormCollection()
    {
        var mockFile = GetMockFile();
        var fileCollection = new FormFileCollection { mockFile };
        return new FormCollection(new Dictionary<string, StringValues>(), fileCollection);
    }
}
