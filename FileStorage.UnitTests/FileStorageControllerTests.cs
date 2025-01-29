using Xunit;
using Moq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using FileStorage.Controllers;
using FileStorage.Services;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;

namespace FileStorage.UnitTests;

public class FileStorageControllerTests
{
    private readonly Mock<IFileStorageService> mockFileStorageService;
    private readonly FileStorageController controller;

    public FileStorageControllerTests()
    {
        // Initialize the mock
        mockFileStorageService = new Mock<IFileStorageService>();

        // Inject the mock into the controller
        controller = new FileStorageController(mockFileStorageService.Object);
    }

    [Fact]
    public void Get_Returns_EnvironmentVariables()
    {
        // Act
        var result = controller.Get();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(5, result.Length);
    }

    [Fact]
    public async Task Upload_With_No_File()
    {
        // Set up context for request
        var context = new DefaultHttpContext();
        context.Request.Form = new FormCollection([]); // Empty collection
        controller.ControllerContext = new ControllerContext()
        {
            HttpContext = context
        };

        // Action
        var response = await controller.Upload();

        // Assert
        var result = Assert.IsType<BadRequestObjectResult>(response);
        Assert.Equal(400, result.StatusCode);
        Assert.Equal("No files uploaded.", result.Value);
    }

    [Fact]
    public async Task Upload_With_File()
    {
        // Set up context for request
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
            ObjectKey = "test.txt-3f5b8c96-7d71-4c41-98d4-8762f34729a5",
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

        // Action
        var response = await controller.Upload();

        // Assert
        var result = Assert.IsType<OkObjectResult>(response);
        var objectList = Assert.IsType<List<StoreFileResponse>>(result.Value);

        Assert.Equal(200, result.StatusCode);
        Assert.Single(objectList);

        // Assert main properties
        var firstObject = objectList[0];
        Assert.Equal(mockResponse.Message, firstObject.Message);
        Assert.Equal(mockResponse.ObjectKey, firstObject.ObjectKey);

        // Assert metadata properties
        var metadata = ((dynamic)firstObject).Metadata;
        Assert.Equal(mockResponse.Metadata.Filename, metadata.Filename);
        Assert.Equal(mockResponse.Metadata.ContentType, metadata.ContentType);
        Assert.Equal(mockResponse.Metadata.Size, metadata.Size);
        Assert.Equal(mockResponse.Metadata.Sha256, metadata.Sha256);
        Assert.Equal(mockResponse.Metadata.BucketName, metadata.BucketName);
        Assert.Equal(mockResponse.Metadata.UploadedAt, metadata.UploadedAt);
    }

    [Fact]
    public async Task Upload_Failed()
    {
        // Set up context for request
        var context = new DefaultHttpContext();
        context.Request.Form = GetFormCollection();
        controller.ControllerContext = new ControllerContext()
        {
            HttpContext = context
        };

        // Mock StoreFile failed to upload or save metadata to db
        var mockResponse = new StoreFileResponse
        {
            Message = "File store failed",
            Error = "DynamoDB operation failed. File removed from S3. Error: Error"
        };
        mockFileStorageService.Setup(service => service.StoreFile(It.IsAny<IFormFile>())).ReturnsAsync(mockResponse);

        // Action
        var response = await controller.Upload();

        // Assert
        var result = Assert.IsType<OkObjectResult>(response);
        var objectList = Assert.IsType<List<StoreFileResponse>>(result.Value);

        Assert.Equal(200, result.StatusCode);
        Assert.Single(objectList);

        // Assert main properties
        var firstObject = objectList[0];
        Assert.Equal(mockResponse.Message, firstObject.Message);
        Assert.Equal(mockResponse.Error, firstObject.Error);
    }

    [Fact]
    public async Task Upload_Throw_Exception()
    {
        // Set up context for request
        var context = new DefaultHttpContext();
        context.Request.Form = GetFormCollection();
        controller.ControllerContext = new ControllerContext()
        {
            HttpContext = context
        };

        // Mock service throw error (for example: invalid credentials)
        mockFileStorageService.Setup(service => service.StoreFile(It.IsAny<IFormFile>()))
            .ThrowsAsync(new Exception("Invalid AWS credentials."));

        // Action
        var response = await controller.Upload();

        // Assert
        var result = Assert.IsType<ObjectResult>(response);
        Assert.Equal(500, result.StatusCode);
        Assert.Contains("Something went wrong:", result.Value.ToString());
    }

    private FormCollection GetFormCollection()
    {
        var mockFile = new FormFile(new MemoryStream(new byte[10]), 0, 10, "Data", "test.txt")
        {
            Headers = new HeaderDictionary(),
            ContentType = "text/plain"
        };

        var fileCollection = new FormFileCollection { mockFile };
        return new FormCollection(new Dictionary<string, StringValues>(), fileCollection);
    }
}
