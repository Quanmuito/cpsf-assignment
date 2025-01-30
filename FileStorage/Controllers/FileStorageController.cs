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

    [HttpPost("upload")]
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

    [HttpGet("download/{fileName}")]
    public async Task<IActionResult> Download(string fileName)
    {
        if (String.IsNullOrEmpty(fileName))
            return BadRequest("No file name found.");

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

    [HttpGet("list")]
    public async Task<IActionResult> List()
    {
        try
        {
            var list = await fileStorageService.ListFile();
            return Ok(list);
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Something went wrong: {ex.Message}.");
        }
    }

    [HttpGet("get/{sha256}")]
    public async Task<IActionResult> Get(string sha256)
    {
        if (String.IsNullOrEmpty(sha256))
            return BadRequest("No sha256 found.");

        try
        {
            var record = await fileStorageService.GetFile(sha256);
            return Ok(record);
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Something went wrong: {ex.Message}.");
        }
    }
}
