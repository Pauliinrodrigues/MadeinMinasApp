using System.Data;
using System.Text;
using MadeInMinas.Api.Data;
using MadeInMinas.Api.DTOs.Cart;
using Microsoft.EntityFrameworkCore;

namespace MadeInMinas.Api.Services;

public sealed class CartService(AppDbContext database, TimeProvider clock)
{
    public async Task<CartProductPageResponse> ListProductsAsync(CartProductQuery request, CancellationToken cancellationToken)
    {
        var query = database.Products.AsNoTracking()
            .Where(product => product.IsActive && product.IsAvailable && product.Category.IsActive);
        if (request.CategoryId is not null)
            query = query.Where(product => product.CategoryId == request.CategoryId);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().Normalize(NormalizationForm.FormC).ToUpperInvariant();
            query = query.Where(product => product.NormalizedName.Contains(search));
        }
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(product => product.NormalizedName).ThenBy(product => product.Id)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .Select(product => new CartProductResponse(product.Id, product.CategoryId, product.Category.Name,
                product.Name, product.Description, product.Price)).ToArrayAsync(cancellationToken);
        return new CartProductPageResponse(items, request.Page, request.PageSize, total);
    }

    public async Task<CartQuoteResponse> QuoteAsync(CartQuoteRequest request, CancellationToken cancellationToken)
    {
        // As leituras compartilham um snapshot; a revisão não reserva preço ou disponibilidade.
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var customer = await database.Customers.AsNoTracking()
            .Where(customer => customer.Id == request.CustomerId && customer.IsActive)
            .Select(customer => new CartCustomerResponse(customer.Id, customer.Name, customer.Phone))
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new CartException(CartError.CartCustomerUnavailable, "O cliente não está disponível. Selecione um cliente ativo.");
        CartAddressResponse? address = null;
        if (request.Fulfillment == "Delivery")
        {
            address = await database.Addresses.AsNoTracking()
                .Where(address => address.Id == request.AddressId && address.CustomerId == customer.Id && address.IsActive)
                .Select(address => new CartAddressResponse(address.Id, address.Street, address.Number, address.Neighborhood,
                    address.City, address.State, address.Complement, address.PostalCode, address.Reference))
                .SingleOrDefaultAsync(cancellationToken)
                ?? throw new CartException(CartError.CartAddressUnavailable, "O endereço não está disponível para este cliente. Selecione um endereço ativo.");
        }
        var ids = request.Items.Select(item => item.ProductId!.Value).Distinct().ToArray();
        var products = await database.Products.AsNoTracking()
            .Where(product => ids.Contains(product.Id) && product.IsActive && product.IsAvailable && product.Category.IsActive)
            .Select(product => new { product.Id, product.Name, product.Price })
            .ToDictionaryAsync(product => product.Id, cancellationToken);
        if (products.Count != ids.Length)
            throw new CartException(CartError.CartProductUnavailable, "Um produto do carrinho está indisponível. Confira o catálogo e remova os itens indisponíveis.");
        var items = request.Items.Select(item =>
        {
            var product = products[item.ProductId!.Value];
            return new CartItemResponse(product.Id, product.Name, item.Quantity!.Value, product.Price,
                product.Price * item.Quantity.Value, CleanOptional(item.Notes));
        }).ToArray();
        var response = new CartQuoteResponse(customer, request.Fulfillment, address, items, CleanOptional(request.Notes),
            items.Sum(item => item.LineTotal), clock.GetUtcNow());
        await transaction.CommitAsync(cancellationToken);
        return response;
    }

    private static string? CleanOptional(string? value) => string.IsNullOrWhiteSpace(value)
        ? null : value.Trim().Normalize(NormalizationForm.FormC);
}
