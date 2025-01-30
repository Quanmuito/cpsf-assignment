namespace FileStorage.Services;

public interface IFileStorageService
{
    public Task<StoreFileResponse> StoreFile(IFormFile file);
    public Task<Stream> DownloadFile(string key);
    public Task<List<Dictionary<string, string>>> ListFile();
}

public class StoreFileResponse
{
    public string? Message { get; set; }
    public string? ObjectKey { get; set; }
    public Metadata? Metadata { get; set; }
    public string? Error {get; set;}
}

public class Metadata
{
    public string? Filename { get; set; }
    public string? ContentType { get; set; }
    public long? Size { get; set; }
    public string? Sha256 { get; set; }
    public string? BucketName { get; set; }
    public string? UploadedAt { get; set; }
}