using Microsoft.AspNetCore.Mvc;
using Amazon.S3;
using Amazon.S3.Model;
using System;
using System.IO;
using System.Threading.Tasks;

namespace FileStorage.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FileStorageController(ILogger<FileStorageController> _logger) : ControllerBase
{
    /*
     * TODO: Place your code here, do not hesitate use the whole solution to implement code for the assignment
     * AWS Resources are placed in us-east-1 region by default
     */

    [HttpPost]
    public async Task<IActionResult> Upload()
    {
        if (!Request.Form.Files.Any())
            return NoContent();

        var awsS3Client = new AmazonS3Client("key", "secret", new AmazonS3Config
            {
                ServiceURL = "http://localstack-cpsf:4566",
                ForcePathStyle = true
            }
        );

        foreach (var file in Request.Form.Files)
        {
            var bucketName = "storage";
            var key = Guid.NewGuid().ToString(); // Generate a unique key for the file

            try
            {
                // Initialize a multipart upload
                var initiateResponse = await awsS3Client.InitiateMultipartUploadAsync(new InitiateMultipartUploadRequest
                {
                    BucketName = bucketName,
                    Key = key
                });

                var uploadId = initiateResponse.UploadId;
                var partNumber = 1;
                var partETags = new List<PartETag>();

                using (var fileStream = file.OpenReadStream())
                {
                    byte[] buffer = new byte[5 * 1024 * 1024]; // 5MB buffer
                    int bytesRead;

                    // Upload in chunks
                    while ((bytesRead = await fileStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        using (var memoryStream = new MemoryStream(buffer, 0, bytesRead))
                        {
                            var uploadPartResponse = await awsS3Client.UploadPartAsync(new UploadPartRequest
                            {
                                BucketName = bucketName,
                                Key = key,
                                UploadId = uploadId,
                                PartNumber = partNumber,
                                InputStream = memoryStream
                            });

                            partETags.Add(new PartETag
                            {
                                PartNumber = partNumber,
                                ETag = uploadPartResponse.ETag
                            });

                            partNumber++;
                        }
                    }
                }

                // Complete the multipart upload
                await awsS3Client.CompleteMultipartUploadAsync(new CompleteMultipartUploadRequest
                {
                    BucketName = bucketName,
                    Key = key,
                    UploadId = uploadId,
                    PartETags = partETags
                });

                return Ok(new { Message = "File uploaded successfully", Key = key });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"File upload failed: {ex.Message}");
            }
        }

        return Ok();
    }
}