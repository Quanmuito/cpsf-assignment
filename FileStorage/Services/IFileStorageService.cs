namespace FileStorage.Services;

public interface IFileStorageService
{
    public Task<object> StoreFile(IFormFile file);
}