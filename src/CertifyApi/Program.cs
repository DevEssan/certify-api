using Azure.Identity;
using Azure.Storage.Blobs;
using CertifyApi.Models;
using CertifyApi.Services;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new() { Title = "Certify API", Version = "v1" });
});

var storageAccountName = builder.Configuration["STORAGE_ACCOUNT_NAME"]
    ?? throw new InvalidOperationException("STORAGE_ACCOUNT_NAME saknas");
var containerName = builder.Configuration["BLOB_CONTAINER_NAME"] ?? "certificates";
var apiKey = builder.Configuration["API_KEY"]
    ?? throw new InvalidOperationException("API_KEY saknas");

var blobServiceUri = new Uri($"https://{storageAccountName}.blob.core.windows.net");
builder.Services.AddSingleton(_ =>
{
    var client = new BlobServiceClient(blobServiceUri, new DefaultAzureCredential());
    var container = client.GetBlobContainerClient(containerName);
    container.CreateIfNotExists();
    return container;
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
    .WithTags("Health")
    .Produces<object>(StatusCodes.Status200OK);

app.MapPost("/certificates", async (CreateCertificateRequest request, BlobContainerClient container) =>
{
    var id = Guid.NewGuid();
    var certificate = CertificateFactory.Create(id, request);

    var blob = container.GetBlobClient($"{id}.json");
    var json = JsonSerializer.Serialize(certificate);
    using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
    await blob.UploadAsync(stream, overwrite: true);

    return Results.Created($"/certificates/{id}", certificate);
})
.WithTags("Certificates")
.Produces<Certificate>(StatusCodes.Status201Created);

app.MapGet("/certificates/{id:guid}", async (Guid id, BlobContainerClient container) =>
{
    var blob = container.GetBlobClient($"{id}.json");
    if (!await blob.ExistsAsync()) return Results.NotFound();

    var download = await blob.DownloadContentAsync();
    var certificate = JsonSerializer.Deserialize<Certificate>(download.Value.Content.ToString());
    return Results.Ok(certificate);
})
.WithTags("Certificates")
.Produces<Certificate>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound);

app.MapGet("/verify/{uuid:guid}", async (Guid uuid, BlobContainerClient container) =>
{
    var blob = container.GetBlobClient($"{uuid}.json");
    if (!await blob.ExistsAsync()) return Results.NotFound(new { valid = false });

    var download = await blob.DownloadContentAsync();
    var certificate = JsonSerializer.Deserialize<Certificate>(download.Value.Content.ToString());
    return Results.Ok(new { valid = true, certificate });
})
.WithTags("Verification")
.Produces<object>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound);

app.MapGet("/certificates", async (HttpRequest request, BlobContainerClient container) =>
{
    if (!request.Headers.TryGetValue("X-Api-Key", out var providedKey) || providedKey != apiKey)
        return Results.Unauthorized();

    var certificates = new List<Certificate>();
    await foreach (var blobItem in container.GetBlobsAsync())
    {
        var blob = container.GetBlobClient(blobItem.Name);
        var download = await blob.DownloadContentAsync();
        var certificate = JsonSerializer.Deserialize<Certificate>(download.Value.Content.ToString());
        if (certificate is not null) certificates.Add(certificate);
    }
    return Results.Ok(certificates);
})
.WithTags("Certificates")
.Produces<List<Certificate>>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status401Unauthorized);

app.Run();

public partial class Program { }