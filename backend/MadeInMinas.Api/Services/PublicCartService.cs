using System.Data;
using System.Text;
using MadeInMinas.Api.Data;
using MadeInMinas.Api.DTOs.Cart;
using MadeInMinas.Api.DTOs.PublicCart;
using Microsoft.EntityFrameworkCore;

namespace MadeInMinas.Api.Services;

public sealed class PublicCartService(AppDbContext database, TimeProvider clock)
{
    public async Task<PublicCartQuoteResponse> QuoteAsync(PublicCartQuoteRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = database.Database.CurrentTransaction is null
            ? await database.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken) : null;
        var ids = request.Items.Select(item => item.ProductId!.Value).Distinct().ToArray();
        var products = await database.Products.AsNoTracking()
            .Where(product => ids.Contains(product.Id) && product.IsActive && product.IsAvailable && product.Category.IsActive)
            .Select(product => new { product.Id, product.Name, product.Price })
            .ToDictionaryAsync(product => product.Id, cancellationToken);
        if (products.Count != ids.Length)
            throw new CartException(CartError.CartProductUnavailable, "Um produto do carrinho está indisponível. Confira o cardápio e remova os itens indisponíveis.");
        var availability = await ProductionAvailability.ReadAsync(database, ids, cancellationToken);
        if (!availability.CanProduce(request.Items.Select(item => (item.ProductId!.Value, item.Quantity!.Value))))
            throw new CartException(CartError.CartProductUnavailable, "Não há disponibilidade para produzir esta quantidade. Ajuste o carrinho ou procure o atendimento.");
        var items = request.Items.Select(item =>
        {
            var product = products[item.ProductId!.Value];
            return new CartItemResponse(product.Id, product.Name, item.Quantity!.Value, product.Price,
                product.Price * item.Quantity.Value, CleanOptional(item.Notes));
        }).ToArray();
        var response = new PublicCartQuoteResponse(items, CleanOptional(request.Notes), items.Sum(item => item.LineTotal), clock.GetUtcNow());
        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);
        return response;
    }

    private static string? CleanOptional(string? value) => string.IsNullOrWhiteSpace(value)
        ? null : value.Trim().Normalize(NormalizationForm.FormC);
}
