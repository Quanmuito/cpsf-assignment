using Microsoft.AspNetCore.Http.Features;
using FileStorage.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddHealthChecks();

// Increase the multipart body size limit (default 128MB -> 2GB)
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 2147483648;
});

// Configure Kestrel to allow larger request bodies (default 30000000 bytes to 2GB)
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 2147483648;
});

// Register your custom service
builder.Services.AddScoped<IFileStorageService, FileStorageService>();

// Add logging services (default setup)
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseHttpsRedirection();
app.MapControllers();
app.MapHealthChecks("/health");

app.Run();