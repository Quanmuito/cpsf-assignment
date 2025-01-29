using Microsoft.AspNetCore.Mvc;
using FileStorage.Services;

namespace FileStorage.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FileStorageController : ControllerBase
{
    private readonly ILogger<FileStorageController> logger;
    private readonly IFileStorageService fileStorageService;
    public FileStorageController(ILogger<FileStorageController> _logger, IFileStorageService _fileStorageService)
    {
        logger = _logger;
        fileStorageService = _fileStorageService;
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

        try
        {
            var uploadedFiles = new List<object>();

            foreach (var file in files)
            {
                var result = await fileStorageService.StoreFile(file);
                uploadedFiles.Add(result);
            }

            return Ok(uploadedFiles);
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Something went wrong: {ex.Message}");
        }
    }
}