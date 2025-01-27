using Microsoft.AspNetCore.Mvc;

namespace FileStorage.Controllers;

[ApiController]
[Route("[controller]")]
public class FileStorageController(ILogger<FileStorageController> _logger) : ControllerBase
{
    /*
     * TODO: Place your code here, do not hesitate use the whole solution to implement code for the assignment
     * AWS Resources are placed in us-east-1 region by default
     */
}