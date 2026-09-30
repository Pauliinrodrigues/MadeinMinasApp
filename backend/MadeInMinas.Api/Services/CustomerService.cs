using System.Text;
using MadeInMinas.Api.Data;
using MadeInMinas.Api.DTOs.Customers;
using MadeInMinas.Api.Models;
using MadeInMinas.Api.Security;
using MadeInMinas.Api.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace MadeInMinas.Api.Services;

public sealed class CustomerService(AppDbContext database, TimeProvider clock, ILogger<CustomerService> logger)
{
    public async Task<CustomerPageResponse> ListAsync(CustomerListQuery request, CancellationToken cancellationToken)
    {
        var query = database.Customers.AsNoTracking();
        if (request.IsActive is not null)
            query = query.Where(customer => customer.IsActive == request.IsActive);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = NormalizeName(request.Search);
            var phone = BrazilianPhone.SearchDigits(request.Search);
            query = string.IsNullOrEmpty(phone)
                ? query.Where(customer => customer.NormalizedName.Contains(search))
                : query.Where(customer => customer.NormalizedName.Contains(search) || customer.Phone.Contains(phone));
        }
        var total = await query.CountAsync(cancellationToken);
        var customers = await query.OrderBy(customer => customer.NormalizedName).ThenBy(customer => customer.Id)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToArrayAsync(cancellationToken);
        return new CustomerPageResponse(customers.Select(ToResponse).ToArray(), request.Page, request.PageSize, total);
    }

    public async Task<CustomerResponse> GetAsync(Guid id, CancellationToken cancellationToken) =>
        ToResponse(await database.Customers.AsNoTracking().SingleOrDefaultAsync(customer => customer.Id == id, cancellationToken)
            ?? throw CustomerNotFound());

    public async Task<CustomerResponse> CreateAsync(Guid actorId, Guid actorStamp, CustomerRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await BeginWriteAsync(actorId, actorStamp, cancellationToken);
        var customer = new Customer { CreatedAt = UtcNow() };
        Apply(customer, request);
        database.Customers.Add(customer);
        await SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Customer {CustomerId} created by {ActorId}.", customer.Id, actorId);
        return ToResponse(customer);
    }

    public async Task<CustomerResponse> UpdateAsync(Guid actorId, Guid actorStamp, Guid id, CustomerRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await BeginWriteAsync(actorId, actorStamp, cancellationToken);
        var customer = await RequireCustomerForUpdateAsync(id, cancellationToken);
        Apply(customer, request);
        await SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Customer {CustomerId} updated by {ActorId}.", id, actorId);
        return ToResponse(customer);
    }

    public async Task<CustomerResponse> SetStatusAsync(Guid actorId, Guid actorStamp, Guid id, bool isActive, CancellationToken cancellationToken)
    {
        await using var transaction = await BeginWriteAsync(actorId, actorStamp, cancellationToken);
        var customer = await RequireCustomerForUpdateAsync(id, cancellationToken);
        if (customer.IsActive != isActive)
        {
            customer.IsActive = isActive;
            customer.UpdatedAt = UtcNow();
            await SaveAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Customer {CustomerId} active status set to {IsActive} by {ActorId}.", id, isActive, actorId);
        return ToResponse(customer);
    }

    public async Task<AddressPageResponse> ListAddressesAsync(Guid customerId, AddressListQuery request, CancellationToken cancellationToken)
    {
        await RequireCustomerAsync(customerId, cancellationToken);
        var query = database.Addresses.AsNoTracking().Where(address => address.CustomerId == customerId);
        if (request.IsActive is not null)
            query = query.Where(address => address.IsActive == request.IsActive);
        var total = await query.CountAsync(cancellationToken);
        var addresses = await query.OrderBy(address => address.CreatedAt).ThenBy(address => address.Id)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToArrayAsync(cancellationToken);
        return new AddressPageResponse(addresses.Select(ToResponse).ToArray(), request.Page, request.PageSize, total);
    }

    public async Task<AddressResponse> GetAddressAsync(Guid customerId, Guid addressId, CancellationToken cancellationToken)
    {
        await RequireCustomerAsync(customerId, cancellationToken);
        var address = await database.Addresses.AsNoTracking().SingleOrDefaultAsync(
            address => address.CustomerId == customerId && address.Id == addressId, cancellationToken) ?? throw AddressNotFound();
        return ToResponse(address);
    }

    public async Task<AddressResponse> CreateAddressAsync(Guid actorId, Guid actorStamp, Guid customerId, AddressRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await BeginWriteAsync(actorId, actorStamp, cancellationToken);
        await RequireCustomerForUpdateAsync(customerId, cancellationToken);
        var address = new Address { CustomerId = customerId, CreatedAt = UtcNow() };
        Apply(address, request);
        database.Addresses.Add(address);
        await SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Address {AddressId} created for customer {CustomerId} by {ActorId}.", address.Id, customerId, actorId);
        return ToResponse(address);
    }

    public async Task<AddressResponse> UpdateAddressAsync(Guid actorId, Guid actorStamp, Guid customerId, Guid addressId, AddressRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await BeginWriteAsync(actorId, actorStamp, cancellationToken);
        await RequireCustomerForUpdateAsync(customerId, cancellationToken);
        var address = await RequireAddressForUpdateAsync(customerId, addressId, cancellationToken);
        Apply(address, request);
        await SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Address {AddressId} updated for customer {CustomerId} by {ActorId}.", addressId, customerId, actorId);
        return ToResponse(address);
    }

    public async Task<AddressResponse> SetAddressStatusAsync(Guid actorId, Guid actorStamp, Guid customerId, Guid addressId, bool isActive, CancellationToken cancellationToken)
    {
        await using var transaction = await BeginWriteAsync(actorId, actorStamp, cancellationToken);
        await RequireCustomerForUpdateAsync(customerId, cancellationToken);
        var address = await RequireAddressForUpdateAsync(customerId, addressId, cancellationToken);
        if (address.IsActive != isActive)
        {
            address.IsActive = isActive;
            address.UpdatedAt = UtcNow();
            await SaveAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Address {AddressId} active status set to {IsActive} by {ActorId}.", addressId, isActive, actorId);
        return ToResponse(address);
    }

    private async Task<IDbContextTransaction> BeginWriteAsync(Guid actorId, Guid actorStamp, CancellationToken cancellationToken)
    {
        var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // A revogação aguarda o commit, como nas gravações do catálogo.
            var actor = await database.Users.FromSqlInterpolated($"SELECT * FROM \"Users\" WHERE \"Id\" = {actorId} FOR SHARE")
                .AsNoTracking().SingleOrDefaultAsync(cancellationToken);
            if (actor is null || !actor.IsActive || actor.SecurityStamp != actorStamp)
                throw new CustomerException(CustomerError.InvalidSession, "Sessão inválida. Faça login novamente.");
            var roles = AccessPolicies.RolesByPermission[AccessPolicies.ManageCustomers];
            if (!await database.Roles.AnyAsync(role => role.Id == actor.RoleId && roles.Contains(role.Code), cancellationToken))
                throw new CustomerException(CustomerError.PermissionDenied, "Acesso restrito ao administrador e atendente.");
            return transaction;
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    private async Task RequireCustomerAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!await database.Customers.AnyAsync(customer => customer.Id == id, cancellationToken))
            throw CustomerNotFound();
    }

    private async Task<Customer> RequireCustomerForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
        await database.Customers.FromSqlInterpolated($"SELECT * FROM \"Customers\" WHERE \"Id\" = {id} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken) ?? throw CustomerNotFound();

    private async Task<Address> RequireAddressForUpdateAsync(Guid customerId, Guid addressId, CancellationToken cancellationToken) =>
        await database.Addresses.FromSqlInterpolated($"SELECT * FROM \"Addresses\" WHERE \"Id\" = {addressId} AND \"CustomerId\" = {customerId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken) ?? throw AddressNotFound();

    private void Apply(Customer customer, CustomerRequest request)
    {
        if (!BrazilianPhone.TryNormalize(request.Phone, out var phone))
            throw new CustomerException(CustomerError.InvalidCustomerPhone, "Informe um telefone brasileiro válido com DDD.");
        customer.Name = Clean(request.Name);
        customer.NormalizedName = NormalizeName(request.Name);
        customer.Phone = phone;
        customer.IsActive = request.IsActive;
        customer.UpdatedAt = UtcNow();
    }

    private void Apply(Address address, AddressRequest request)
    {
        address.Street = Clean(request.Street);
        address.Number = Clean(request.Number);
        address.Neighborhood = Clean(request.Neighborhood);
        address.City = Clean(request.City);
        address.State = request.State.ToUpperInvariant();
        address.Complement = CleanOptional(request.Complement);
        address.PostalCode = CleanOptional(request.PostalCode)?.Replace("-", "", StringComparison.Ordinal);
        address.Reference = CleanOptional(request.Reference);
        address.IsActive = request.IsActive;
        address.UpdatedAt = UtcNow();
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_Customers_Phone" })
        {
            throw new CustomerException(CustomerError.DuplicateCustomerPhone, "Já existe um cliente com esse telefone, inclusive entre os inativos.");
        }
    }

    private static string Clean(string value) => value.Trim().Normalize(NormalizationForm.FormC);
    private static string? CleanOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : Clean(value);
    private static string NormalizeName(string value) => Clean(value).ToUpperInvariant();
    private DateTimeOffset UtcNow() => DateTimeOffset.FromUnixTimeMilliseconds(clock.GetUtcNow().ToUnixTimeMilliseconds());
    private static CustomerException CustomerNotFound() => new(CustomerError.CustomerNotFound, "Cliente não encontrado.");
    private static CustomerException AddressNotFound() => new(CustomerError.AddressNotFound, "Endereço não encontrado para este cliente.");
    private static CustomerResponse ToResponse(Customer customer) => new(customer.Id, customer.Name, customer.Phone,
        customer.IsActive, customer.CreatedAt, customer.UpdatedAt);
    private static AddressResponse ToResponse(Address address) => new(address.Id, address.CustomerId, address.Street,
        address.Number, address.Neighborhood, address.City, address.State, address.IsActive, address.Complement,
        address.PostalCode, address.Reference, address.CreatedAt, address.UpdatedAt);
}
