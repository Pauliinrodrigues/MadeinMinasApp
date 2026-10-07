using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MadeInMinas.Api.Controllers;
using MadeInMinas.Api.DTOs.Auth;
using MadeInMinas.Api.DTOs.Categories;
using MadeInMinas.Api.DTOs.Menu;
using MadeInMinas.Api.DTOs.Products;
using MadeInMinas.Api.Services;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;

namespace MadeInMinas.Api.Tests;

[Collection("Access control")]
public sealed class ProductImageTests(AuthenticationFactory factory) : IClassFixture<AuthenticationFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.WithDatabaseAsync(async database =>
    {
        await database.StockMovements.ExecuteDeleteAsync();
        await database.Payments.ExecuteDeleteAsync();
        await database.Orders.ExecuteDeleteAsync();
        await database.Recipes.ExecuteDeleteAsync();
        await database.Products.ExecuteDeleteAsync();
        await database.Categories.ExecuteDeleteAsync();
        await database.Users.ExecuteDeleteAsync();
    });
    public Task DisposeAsync() => Task.CompletedTask;
    private int PhotoCount() => Directory.Exists(factory.ProductImagesPath) ? Directory.GetFiles(factory.ProductImagesPath).Length : 0;

    private async Task<HttpClient> SignInAsync(int role = 1)
    {
        var user = await factory.CreateUserAsync(role);
        var client = factory.CreateStaffClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Username, factory.Password));
        response.EnsureSuccessStatusCode();
        var login = (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        return client;
    }

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, byte[] bytes, string fileName = "lanche.png")
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", fileName);
        return await client.PostAsync("/api/product-images", form);
    }

    private static byte[] Image(SKEncodedImageFormat format = SKEncodedImageFormat.Png, int width = 120, int height = 80)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        using var paint = new SKPaint();
        paint.Color = SKColors.Red;
        canvas.DrawRect(0, 0, width / 2f, height / 2f, paint);
        paint.Color = SKColors.Green;
        canvas.DrawRect(width / 2f, 0, width / 2f, height / 2f, paint);
        paint.Color = SKColors.Blue;
        canvas.DrawRect(0, height / 2f, width / 2f, height / 2f, paint);
        paint.Color = SKColors.Yellow;
        canvas.DrawRect(width / 2f, height / 2f, width / 2f, height / 2f, paint);
        using var encoded = bitmap.Encode(format, 95);
        return encoded.ToArray();
    }

    private static async Task<string> UploadedUrlAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var url = (await response.Content.ReadFromJsonAsync<ProductImageResponse>())!.ImageUrl;
        Assert.True(ProductImageStorage.IsManagedUrl(url));
        Assert.Equal(url, response.Headers.Location!.OriginalString);
        return url;
    }

    private static async Task ProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, body.RootElement.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task OnlyAdministratorsCanUpload(int role)
    {
        var count = PhotoCount();
        using var client = role == 0 ? factory.CreateStaffClient() : await SignInAsync(role);
        using var response = await UploadAsync(client, Image());
        Assert.Equal(role == 0 ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(count, PhotoCount());
    }

    [Theory]
    [InlineData(SKEncodedImageFormat.Png)]
    [InlineData(SKEncodedImageFormat.Jpeg)]
    [InlineData(SKEncodedImageFormat.Webp)]
    public async Task RealImagesAreStoredAsWebpAndReadableWithoutLogin(SKEncodedImageFormat format)
    {
        using var client = await SignInAsync();
        // The caller's name and declared MIME type never determine the stored path or actual format.
        using var upload = await UploadAsync(client, Image(format), "../../foto.exe");
        var url = await UploadedUrlAsync(upload);
        var fileName = url.Split('/').Last();
        Assert.True(File.Exists(Path.Combine(factory.ProductImagesPath, fileName)));
        using var visitor = factory.CreateStaffClient();
        using var response = await visitor.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/webp", response.Content.Headers.ContentType!.MediaType);
        Assert.Contains("immutable", response.Headers.CacheControl!.ToString());
        Assert.Contains("nosniff", response.Headers.GetValues("X-Content-Type-Options"));
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(factory.ProductImagesPath, fileName)), bytes);
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        Assert.Equal(SKEncodedImageFormat.Webp, codec.EncodedFormat);
        Assert.Equal(120, codec.Info.Width);
        Assert.Equal(80, codec.Info.Height);
    }

    [Fact]
    public async Task PhotoCanBeSavedReplacedAndRemovedFromProductAndPublicMenu()
    {
        using var client = await SignInAsync();
        using var first = await UploadAsync(client, Image());
        var firstUrl = await UploadedUrlAsync(first);
        var categoryResponse = await client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest("Lanches"));
        var category = (await categoryResponse.Content.ReadFromJsonAsync<CategoryResponse>())!;
        var input = new ProductRequest("Uai Sô", category.Id, 23.90m, true, true, ImageUrl: firstUrl);
        using var created = await client.PostAsJsonAsync("/api/products", input);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var product = (await created.Content.ReadFromJsonAsync<ProductResponse>())!;
        using var visitor = factory.CreateStaffClient();
        var menu = await visitor.GetFromJsonAsync<PublicMenuResponse>("/api/menu");
        Assert.Equal(firstUrl, menu!.Items.Single(item => item.Id == product.Id).ImageUrl);
        using var second = await UploadAsync(client, Image(SKEncodedImageFormat.Jpeg));
        var secondUrl = await UploadedUrlAsync(second);
        Assert.NotEqual(firstUrl, secondUrl);
        using var replaced = await client.PutAsJsonAsync($"/api/products/{product.Id}", input with { ImageUrl = secondUrl });
        Assert.Equal(secondUrl, (await replaced.Content.ReadFromJsonAsync<ProductResponse>())!.ImageUrl);
        using var removed = await client.PutAsJsonAsync($"/api/products/{product.Id}", input with { ImageUrl = null });
        Assert.Null((await removed.Content.ReadFromJsonAsync<ProductResponse>())!.ImageUrl);
        menu = await visitor.GetFromJsonAsync<PublicMenuResponse>("/api/menu");
        Assert.Null(menu!.Items.Single(item => item.Id == product.Id).ImageUrl);
        // Existing references/caches are safe when a product changes its photo.
        Assert.Equal(HttpStatusCode.OK, (await visitor.GetAsync(firstUrl)).StatusCode);
    }

    [Fact]
    public async Task InvalidFilesAndMissingReferencesDoNotWritePhotosOrProducts()
    {
        var count = PhotoCount();
        using var client = await SignInAsync();
        foreach (var invalid in new[] { Array.Empty<byte>(), "<svg xmlns=\"http://www.w3.org/2000/svg\"/>"u8.ToArray(),
            "not a photo"u8.ToArray(), Image()[..40], Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7") })
        {
            using var response = await UploadAsync(client, invalid);
            await ProblemAsync(response, HttpStatusCode.BadRequest, "InvalidProductImage");
        }
        using var tooLarge = await UploadAsync(client, new byte[ProductImageStorage.MaxFileBytes + 1]);
        await ProblemAsync(tooLarge, HttpStatusCode.RequestEntityTooLarge, "ProductImageTooLarge");
        var huge = Image(SKEncodedImageFormat.Jpeg);
        for (var index = 0; index < huge.Length - 9; index++)
        {
            if (huge[index] != 0xff || huge[index + 1] is not (0xc0 or 0xc2))
                continue;
            huge[index + 5] = huge[index + 7] = 0x13; // 5000 × 5000, rejected before allocation/decode.
            huge[index + 6] = huge[index + 8] = 0x88;
            break;
        }
        using var hugeResponse = await UploadAsync(client, huge);
        await ProblemAsync(hugeResponse, HttpStatusCode.BadRequest, "InvalidProductImage");
        Assert.Equal(count, PhotoCount());
        var categoryResponse = await client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest("Lanches"));
        var category = (await categoryResponse.Content.ReadFromJsonAsync<CategoryResponse>())!;
        using var missing = await client.PostAsJsonAsync("/api/products",
            new ProductRequest("Sem foto", category.Id, 10m, true, true, ImageUrl: $"/api/product-images/{Guid.NewGuid():N}.webp"));
        await ProblemAsync(missing, HttpStatusCode.BadRequest, "InvalidProductImage");
        await factory.WithDatabaseAsync(async database => Assert.False(await database.Products.AnyAsync()));
    }

    [Theory]
    [InlineData("secrets.json")]
    [InlineData("../appsettings.json")]
    [InlineData("%2e%2e%2fappsettings.json")]
    [InlineData("00000000000000000000000000000000.webp")]
    public async Task PublicImageReadsCannotExposeOtherFiles(string name)
    {
        using var visitor = factory.CreateStaffClient();
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync("/api/product-images/" + name)).StatusCode);
    }

    [Fact]
    public async Task LargePhotoIsResizedWithItsAspectRatio()
    {
        using var client = await SignInAsync();
        using var response = await UploadAsync(client, Image(width: 2000, height: 1000));
        var url = await UploadedUrlAsync(response);
        using var data = SKData.CreateCopy(await client.GetByteArrayAsync(url));
        using var codec = SKCodec.Create(data);
        Assert.Equal(1600, codec.Info.Width);
        Assert.Equal(800, codec.Info.Height);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public async Task PhoneOrientationIsAppliedAndMetadataIsRemoved(int orientation)
    {
        var jpeg = Image(SKEncodedImageFormat.Jpeg);
        var exif = Convert.FromHexString("45786966000049492A0008000000010012010300010000000100000000000000");
        exif[24] = (byte)orientation;
        var photo = new byte[jpeg.Length + exif.Length + 4];
        jpeg.AsSpan(0, 2).CopyTo(photo);
        photo[2] = 0xff;
        photo[3] = 0xe1;
        photo[4] = 0;
        photo[5] = (byte)(exif.Length + 2);
        exif.CopyTo(photo, 6);
        jpeg.AsSpan(2).CopyTo(photo.AsSpan(exif.Length + 6));
        using var client = await SignInAsync();
        using var response = await UploadAsync(client, photo);
        var url = await UploadedUrlAsync(response);
        var bytes = await client.GetByteArrayAsync(url);
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        Assert.Equal(SKEncodedOrigin.TopLeft, codec.EncodedOrigin);
        Assert.Equal(orientation >= 5 ? 80 : 120, codec.Info.Width);
        Assert.Equal(orientation >= 5 ? 120 : 80, codec.Info.Height);
        using var bitmap = SKBitmap.Decode(bytes);
        var expected = orientation switch
        {
            2 or 8 => SKColors.Green,
            3 or 7 => SKColors.Yellow,
            4 or 6 => SKColors.Blue,
            _ => SKColors.Red
        };
        var actual = bitmap.GetPixel(10, 10);
        Assert.InRange(Math.Abs(expected.Red - actual.Red), 0, 30);
        Assert.InRange(Math.Abs(expected.Green - actual.Green), 0, 30);
        Assert.InRange(Math.Abs(expected.Blue - actual.Blue), 0, 30);
    }

    [Fact]
    public async Task RevokedSessionCannotStorePhotos()
    {
        var count = PhotoCount();
        using var client = await SignInAsync();
        await factory.WithDatabaseAsync(database => database.Users.ExecuteUpdateAsync(update => update.SetProperty(user => user.SecurityStamp, Guid.NewGuid())));
        using var response = await UploadAsync(client, Image());
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(count, PhotoCount());
    }
}
