using MadeInMinas.Api.Data;
using MadeInMinas.Api.DTOs.PublicOrders;
using MadeInMinas.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace MadeInMinas.Api.Services;

public sealed class PublicOrderTrackingService(AppDbContext database, PublicOrderAccess access)
{
    public async Task<PublicOrderTrackingResponse?> GetAsync(string? token, CancellationToken cancellationToken)
    {
        var id = access.Read(token);
        if (id is null)
            return null;

        // Uma consulta mantém status e histórico no mesmo snapshot, sem carregar dados pessoais.
        return await database.Orders.AsNoTracking()
            .Where(order => order.Id == id && order.Origin == "DirectLink")
            .Select(order => new PublicOrderTrackingResponse(order.Number, order.Fulfillment, order.Total,
                order.Status, order.CreatedAt, order.UpdatedAt,
                order.History.OrderBy(entry => entry.Version)
                    .Select(entry => new PublicOrderStatusResponse(entry.ToStatus, entry.OccurredAt)).ToArray()))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
