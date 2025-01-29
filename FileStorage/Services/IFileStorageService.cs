namespace FileStorage.Services;

public interface IFileStorageService
{
    public Task<StoreFileResponse> StoreFile(IFormFile file);
}