using Microsoft.AspNetCore.Mvc;
using FileStorage.Services;

namespace FileStorage.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FileStorageController : ControllerBase
{
    private readonly IFileStorageService fileStorageService;
    public FileStorageController(IFileStorageService _fileStorageService)
    {
        fileStorageService = _fileStorageService;
    }

    // [HttpGet]
    // public string[] Get()
    // {
    //     string[] env = [
    //         Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID") ?? "",
    //         Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY") ?? "",
    //         Environment.GetEnvironmentVariable("AWS_REGION") ?? "",
    //         Environment.GetEnvironmentVariable("AWS_ENDPOINT_URL") ?? "",
    //         Environment.GetEnvironmentVariable("FileS_AWS__ServiceURL") ?? "",
    //     ];
    //     return env;
    // }

    [HttpPost]
    public async Task<IActionResult> Upload()
    {
        IFormFileCollection? files = Request.Form.Files;

        if (!files.Any())
            return BadRequest("No files uploaded.");

        try
        {
            var uploadedFiles = new List<StoreFileResponse>();

            foreach (var file in files)
            {
                var result = await fileStorageService.StoreFile(file);
                uploadedFiles.Add(result);
            }

            return Ok(uploadedFiles);
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Something went wrong: {ex.Message}.");
        }
    }

    [HttpGet("{fileName}")]
    public async Task<IActionResult> Download(string fileName)
    {
        if (String.IsNullOrEmpty(fileName))
        {
            return BadRequest("No file name found.");
        }

        try
        {
            var file = await fileStorageService.DownloadFile(fileName);
            if (file == null)
                return NotFound("File not found.");

            // Content type (MIME type) default for unknown file types
            return File(file, "application/octet-stream", fileName);
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Something went wrong: {ex.Message}.");
        }
    }
}