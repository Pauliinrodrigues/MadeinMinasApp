using System.Text.RegularExpressions;
using MadeInMinas.Api.Data;
using SkiaSharp;

namespace MadeInMinas.Api.Services;

public sealed partial class ProductImageStorage(
    IWebHostEnvironment environment, IConfiguration configuration, AppDbContext database)
{
    public const long MaxFileBytes = 8 * 1024 * 1024;
    public const long MaxRequestBytes = MaxFileBytes + 64 * 1024;
    public const int MaxPixels = 24_000_000;
    public const int MaxEdge = 1600;
    private const string UrlPrefix = "/api/product-images/";
    private readonly string directory = Path.GetFullPath(
        configuration["ProductImages:StoragePath"] ?? Path.Combine("App_Data", "product-images"),
        environment.ContentRootPath);

    [GeneratedRegex("\\A[a-f0-9]{32}\\.webp\\z", RegexOptions.CultureInvariant)]
    private static partial Regex FileNamePattern();

    public static bool IsManagedUrl(string value) =>
        value.StartsWith(UrlPrefix, StringComparison.Ordinal) && FileNamePattern().IsMatch(value[UrlPrefix.Length..]);

    public void ValidateReference(string? imageUrl)
    {
        var value = imageUrl?.Trim();
        if (value is not null && IsManagedUrl(value) && !File.Exists(Path.Combine(directory, value[UrlPrefix.Length..])))
            throw new ProductException(ProductError.InvalidProductImage, "A foto não está disponível. Selecione e envie o arquivo novamente.");
    }

    public Stream OpenRead(string fileName)
    {
        if (!FileNamePattern().IsMatch(fileName))
            throw NotFound();
        try
        {
            return new FileStream(Path.Combine(directory, fileName), FileMode.Open, FileAccess.Read, FileShare.Read,
                64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        }
        catch (FileNotFoundException) { throw NotFound(); }
        catch (DirectoryNotFoundException) { throw NotFound(); }
    }

    public async Task<string> SaveAsync(Guid actorId, Guid actorStamp, IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length > MaxFileBytes)
            throw new ProductException(ProductError.ProductImageTooLarge, "Escolha uma foto de até 8 MB.");
        if (file.Length == 0)
            throw Invalid();

        using var input = new MemoryStream((int)file.Length);
        await file.CopyToAsync(input, cancellationToken);
        using var encoded = SKData.CreateCopy(input.GetBuffer().AsSpan(0, (int)input.Length));
        using var codec = SKCodec.Create(encoded);
        if (codec is null || codec.EncodedFormat is not (SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Png or SKEncodedImageFormat.Webp) ||
            codec.Info.Width <= 0 || codec.Info.Height <= 0 || (long)codec.Info.Width * codec.Info.Height > MaxPixels || codec.FrameCount > 1)
            throw Invalid();

        using var colorSpace = SKColorSpace.CreateSrgb();
        using var bitmap = new SKBitmap(new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Premul, colorSpace));
        if (codec.GetPixels(bitmap.Info, bitmap.GetPixels()) != SKCodecResult.Success)
            throw Invalid();
        using var optimized = Optimize(bitmap, codec.EncodedOrigin);
        cancellationToken.ThrowIfCancellationRequested();

        // Revalidate the actor while holding the same user lock as other catalog writes.
        await using var transaction = await CatalogWriteTransaction.BeginAsync(database, actorId, actorStamp,
            () => new ProductException(ProductError.InvalidSession, "Sessão inválida. Faça login novamente."),
            () => new ProductException(ProductError.PermissionDenied, "Acesso restrito ao administrador."), cancellationToken);
        Directory.CreateDirectory(directory);
        var fileName = Guid.NewGuid().ToString("N") + ".webp";
        var path = Path.Combine(directory, fileName);
        var created = false;
        try
        {
            await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                64 * 1024, FileOptions.Asynchronous))
            {
                created = true;
                await output.WriteAsync(optimized.AsSpan().ToArray(), cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            if (created)
                File.Delete(path);
            throw;
        }
        return UrlPrefix + fileName;
    }

    private static SKData Optimize(SKBitmap bitmap, SKEncodedOrigin origin)
    {
        var swapped = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var width = swapped ? bitmap.Height : bitmap.Width;
        var height = swapped ? bitmap.Width : bitmap.Height;
        var scale = Math.Min(1d, (double)MaxEdge / Math.Max(width, height));
        var outputWidth = Math.Max(1, (int)Math.Round(width * scale));
        var outputHeight = Math.Max(1, (int)Math.Round(height * scale));
        using var colorSpace = SKColorSpace.CreateSrgb();
        using var surface = SKSurface.Create(new SKImageInfo(outputWidth, outputHeight, SKColorType.Rgba8888, SKAlphaType.Premul, colorSpace)) ?? throw Invalid();
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.Scale((float)outputWidth / width, (float)outputHeight / height);
        switch (origin)
        {
            case SKEncodedOrigin.TopRight:
                canvas.Translate(width, 0);
                canvas.Scale(-1, 1);
                break;
            case SKEncodedOrigin.BottomRight:
                canvas.Translate(width, height);
                canvas.RotateDegrees(180);
                break;
            case SKEncodedOrigin.BottomLeft:
                canvas.Translate(0, height);
                canvas.Scale(1, -1);
                break;
            case SKEncodedOrigin.LeftTop:
                canvas.RotateDegrees(90);
                canvas.Scale(1, -1);
                break;
            case SKEncodedOrigin.RightTop:
                canvas.Translate(width, 0);
                canvas.RotateDegrees(90);
                break;
            case SKEncodedOrigin.RightBottom:
                canvas.Translate(width, height);
                canvas.RotateDegrees(90);
                canvas.Scale(-1, 1);
                break;
            case SKEncodedOrigin.LeftBottom:
                canvas.Translate(0, height);
                canvas.RotateDegrees(270);
                break;
        }
        using var image = SKImage.FromBitmap(bitmap);
        canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Linear));
        using var snapshot = surface.Snapshot();
        return snapshot.Encode(SKEncodedImageFormat.Webp, 85) ?? throw Invalid();
    }

    private static ProductException Invalid() => new(ProductError.InvalidProductImage,
        "Escolha uma foto JPG, PNG ou WebP válida, sem animação e com até 24 megapixels.");
    private static ProductException NotFound() => new(ProductError.ProductImageNotFound, "Foto não encontrada.");
}
