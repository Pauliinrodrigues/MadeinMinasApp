using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using MadeInMinas.Api.Security;
using MadeInMinas.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MadeInMinas.Api.Controllers;

[ApiController]
[Route("api/product-images")]
public sealed class ProductImagesController(ProductImageStorage images) : ControllerBase
{
    [HttpPost]
    [Authorize(Policy = AccessPolicies.ManageCatalog)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(ProductImageStorage.MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = ProductImageStorage.MaxRequestBytes)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<ProductImageResponse>> Upload([Required] IFormFile file, CancellationToken cancellationToken)
    {
        var imageUrl = await images.SaveAsync(Guid.Parse(User.FindFirstValue("sub")!),
            Guid.Parse(User.FindFirstValue("auth_stamp")!), file, cancellationToken);
        return Created(imageUrl, new ProductImageResponse(imageUrl));
    }

    [HttpGet("{fileName}")]
    [AllowAnonymous]
    public IActionResult Get(string fileName)
    {
        var stream = images.OpenRead(fileName);
        Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(stream, "image/webp", enableRangeProcessing: true);
    }
}

public sealed record ProductImageResponse(string ImageUrl);
