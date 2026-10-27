using System.Globalization;
using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;
using MadeInMinas.Api.Data;
using MadeInMinas.Api.DTOs.ChatOrders;
using MadeInMinas.Api.DTOs.Menu;
using MadeInMinas.Api.DTOs.PublicCheckout;
using MadeInMinas.Api.Services.Ai;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MadeInMinas.Api.Validation;

namespace MadeInMinas.Api.Services;

/// <summary>Only explicitly allowed public operations are callable by the model.</summary>
public sealed class AiChatTools(AppDbContext database, PublicMenuService menu, DeliverySettingsService delivery,
    StoreAttendanceService attendance, ChatOrderService orders, TimeProvider clock, IOptions<AiOptions> options)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly CultureInfo Portuguese = CultureInfo.GetCultureInfo("pt-BR");

    public async Task<AiToolResult> ExecuteAsync(Guid conversationId, Guid leaseId, int source,
        AiToolCall call, CancellationToken cancellationToken)
    {
        if (!options.Value.IsConfigured || !await database.ChatConversations.AsNoTracking().AnyAsync(chat =>
            chat.Id == conversationId && chat.AutomationMode == "Automatic" && chat.Status == "Waiting"
            && chat.AiLeaseId == leaseId && chat.AiLeaseSourceSequence == source
            && chat.AiLeaseExpiresAt > clock.GetUtcNow(), cancellationToken))
            throw new AiProviderException("TurnSuperseded");
        try
        {
            using var document = JsonDocument.Parse(call.ArgumentsJson);
            var args = document.RootElement;
            switch (call.Name)
            {
                case "consultar_cardapio":
                    Fields(args, "search", "page");
                    var search = OptionalString(args, "search", 120);
                    var page = Int(args, "page", 1, 1000000);
                    var catalog = await menu.GetAsync(new PublicMenuQuery { Search = search, Page = page, PageSize = 48 }, cancellationToken);
                    var products = catalog.Items.Take(12).Select(ProductLine).ToArray();
                    var list = catalog.Items.Length == 0 ? "Não encontrei produtos cadastrados com essa busca. Você pode consultar o cardápio ou falar com um atendente."
                        : "Estas são as opções cadastradas:\n" + string.Join("\n", products)
                            + (catalog.TotalCount > products.Length ? "\nHá mais opções no cardápio. Qual lanche ou categoria você gostaria de consultar?" : "\nQual opção você prefere?");
                    return Success(new
                    {
                        catalog.Categories,
                        catalog.Page,
                        catalog.PageSize,
                        catalog.TotalCount,
                        items = catalog.Items.Take(12).Select(item => new
                        {
                            item.Id,
                            item.CategoryId,
                            item.Name,
                            item.Price,
                            item.IsAvailable,
                            description = item.Description is { Length: > 300 } description ? description[..300] : item.Description
                        }),
                        notice = catalog.Items.Length > 12 ? "Consulte por nome para ver as demais opções desta página." : null
                    }, Limit(list));
                case "consultar_produto":
                case "consultar_disponibilidade":
                    Fields(args, "productId");
                    var product = await menu.GetProductAsync(ProductId(args), cancellationToken);
                    return product is null
                        ? Failure("ProductNotFound", "Não encontrei esse produto no cardápio. Não tenho um preço cadastrado para ele; posso ajudar a escolher uma opção disponível.")
                        : Success(product, Limit(ProductLine(product) + (call.Name == "consultar_produto" && product.Description is not null ? "\n" + product.Description : "")));
                case "consultar_promocoes":
                    Fields(args);
                    return Success(new { supported = false }, "Não há promoções cadastradas disponíveis para consulta neste sistema. Posso mostrar os produtos e preços do cardápio.");
                case "consultar_taxa_entrega":
                    Fields(args);
                    var areas = await delivery.PublicAreasAsync(cancellationToken);
                    var settings = delivery.PublicOptions();
                    var lines = areas.Take(10).Select(area => $"{area.City}–{area.State}, {(area.CoversAllNeighborhoods ? "todos os bairros" : area.Neighborhood)}: {Money(area.Fee)}.");
                    return Success(new { areas, options = settings }, Limit("Entrega:\n" + string.Join("\n", lines)
                        + (areas.Length == 0 ? "Nenhuma região de entrega disponível." : "")
                        + $"\nRetirada: {Money(settings.PickupFee)}." + (settings.PickupAddress is null ? "" : $" Endereço: {settings.PickupAddress}.")));
                case "consultar_atendimento":
                    Fields(args);
                    var store = await attendance.PublicAsync(cancellationToken);
                    var hours = store.Hours.Select(hour => $"{DayName(hour.Day)}: {(hour.IsClosed ? "fechado" : $"{hour.OpensAt} às {hour.ClosesAt}")}");
                    var payments = store.PaymentMethods.Select(method => method switch { "Card" => "cartão", "Cash" => "dinheiro", "Pix" => "Pix", _ => "" }).Where(method => method.Length > 0);
                    return Success(store, Limit(string.Join("\n", hours) + $"\nPagamento: {string.Join(", ", payments)}.\n{store.DeliveryEstimateMessage}\n"
                        + (store.AcceptingOrders ? "Estamos recebendo pedidos." : "Não estamos recebendo novos pedidos agora. Você pode consultar o cardápio.")));
                case "adicionar_item_pedido":
                case "alterar_quantidade":
                    Fields(args, "productId", "quantity", "notes");
                    var selected = await orders.SetItemAsync(conversationId, leaseId, source, ProductId(args),
                        Int(args, "quantity", 1, 99), OptionalString(args, "notes", 250), cancellationToken,
                        requireExisting: call.Name == "alterar_quantidade");
                    return await UpdatedOrderResultAsync(conversationId, leaseId, source, selected, cancellationToken);
                case "remover_item_pedido":
                    Fields(args, "productId");
                    return await UpdatedOrderResultAsync(conversationId, leaseId, source,
                        await orders.RemoveItemAsync(conversationId, leaseId, source, ProductId(args), cancellationToken), cancellationToken);
                case "definir_dados_pedido":
                    Fields(args, "name", "phone", "fulfillment", "address", "notes");
                    var details = JsonSerializer.Deserialize<ChatOrderDetailsRequest>(call.ArgumentsJson, Json)
                        ?? throw new JsonException();
                    var (missingCustomerInput, validatedDetails) = await ValidateCustomerInputAsync(conversationId, source, details, cancellationToken);
                    if (missingCustomerInput is not null)
                        return Failure("CustomerDataNotProvided", missingCustomerInput switch
                        {
                            "name" => "Qual nome devo colocar no pedido? Preciso do nome informado por você.",
                            "phone" => "Qual é seu telefone com DDD? Não posso preencher um telefone que você ainda não informou.",
                            _ => "Para entrega, informe bairro, rua e número. Não posso preencher um endereço que você ainda não informou."
                        });
                    return await UpdatedOrderResultAsync(conversationId, leaseId, source,
                        await orders.SetDetailsAsync(conversationId, leaseId, source, validatedDetails, cancellationToken), cancellationToken);
                case "calcular_pedido":
                    Fields(args);
                    return OrderResult(await orders.ReviewForToolAsync(conversationId, leaseId, source, cancellationToken));
                case "consultar_status_pedido":
                    Fields(args);
                    var status = await orders.ReadStatusForToolAsync(conversationId, leaseId, source, cancellationToken);
                    return status is null
                        ? Failure("OrderNotLinked", "Esta conversa ainda não possui um pedido recebido. Para acompanhar outra compra, use o link do comprovante; não consigo consultar pedidos de outras pessoas.")
                        : Success(status, $"Pedido #{status.Number}: {StatusName(status.Status)}. Total: {Money(status.Total)}.");
                case "solicitar_atendimento_humano":
                    Fields(args);
                    return new AiToolResult(JsonSerializer.Serialize(new { success = true, handOff = true }, Json),
                        "Vou encaminhar sua conversa para um atendente. Sua mensagem foi preservada; a equipe responderá quando disponível.", true);
                case "responder_cliente":
                    Fields(args, "code");
                    var code = RequiredString(args, "code", 40);
                    var text = code switch
                    {
                        "ToolResult" => null,
                        "Greeting" => "Olá! Sou a assistente virtual do Made in Minas. Posso mostrar o cardápio, ajudar a montar seu pedido ou chamar um atendente. Como posso ajudar?",
                        "AskProduct" => "Qual produto do cardápio você gostaria de escolher?",
                        "AskQuantity" => "Qual é a quantidade total que você deseja desse produto?",
                        "AskName" => "Qual nome devo colocar no pedido?",
                        "AskPhone" => "Qual é seu telefone com DDD para contato sobre o pedido?",
                        "AskFulfillment" => "Você prefere entrega ou retirada?",
                        "AskAddress" => "Para entrega, preciso do bairro, rua e número. Qual é o endereço?",
                        "Clarify" => "Não consegui identificar sua escolha com segurança. Pode explicar qual produto ou alteração deseja? Também posso chamar um atendente.",
                        "Unsupported" => "Essa opção não está cadastrada como uma operação disponível no sistema. Posso mostrar o cardápio ou encaminhar para um atendente.",
                        "DiscountNotAvailable" => "Não tenho autorização para aplicar descontos. Os valores são os cadastrados no cardápio; posso chamar um atendente para conversar com você.",
                        "PromotionNotAvailable" => "Não há promoções cadastradas disponíveis para consulta neste sistema. Posso mostrar o cardápio.",
                        "Unknown" => "Não tenho essa informação disponível. Posso encaminhar sua pergunta para um atendente.",
                        "AwaitConfirmation" => await ConfirmationTextAsync(conversationId, cancellationToken),
                        _ => throw new JsonException()
                    };
                    return Success(new { code }, text);
                default:
                    return Failure("ToolNotAllowed", "Essa operação não está disponível no atendimento automático. Posso chamar um atendente.");
            }
        }
        catch (ChatException exception)
        {
            return Failure(exception.Error.ToString(), exception.Message);
        }
        catch (CartException exception)
        {
            return Failure(exception.Error.ToString(), exception.Message);
        }
        catch (OrderException exception)
        {
            return Failure(exception.Error.ToString(), exception.Message);
        }
        catch (Exception exception) when (exception is JsonException or FormatException or ArgumentException or InvalidOperationException)
        {
            return Failure("InvalidToolArguments", "Não consegui validar esses dados. Confira o produto, a quantidade e as informações do pedido, ou fale com um atendente.");
        }
    }

    private async Task<AiToolResult> UpdatedOrderResultAsync(Guid conversationId, Guid leaseId, int source,
        ChatOrderStateResponse state, CancellationToken cancellationToken)
    {
        // Preparing a review is a server rule, not a model decision. Reuse the
        // checkout validations and lease fencing; creation still requires the
        // visitor's explicit confirmation with the CURRENT review token.
        if (state.Items.Length > 0 && state.MissingFields.Length == 0 && state.Review is null && state.Receipt is null)
            state = await orders.ReviewForToolAsync(conversationId, leaseId, source, cancellationToken);
        return OrderResult(state);
    }

    private async Task<(string? Missing, ChatOrderDetailsRequest Details)> ValidateCustomerInputAsync(Guid id, int sourceSequence, ChatOrderDetailsRequest details, CancellationToken cancellationToken)
    {
        // Model instructions do not prove provenance. Only visitor input from THIS
        // conversation can authorize personal fields, never tool/store/staff data.
        var visitorName = await database.ChatConversations.Where(chat => chat.Id == id)
            .Select(chat => chat.VisitorName).SingleAsync(cancellationToken);
        var messages = await database.ChatMessages.AsNoTracking()
            .Where(message => message.ConversationId == id && message.Kind == "Visitor" && message.Sequence <= sourceSequence)
            .OrderByDescending(message => message.Sequence).Take(50)
            .Select(message => message.Text).ToArrayAsync(cancellationToken);
        var sources = messages.Select(NormalizeCustomerText).ToArray();
        bool Supplied(string? value) => value is null || NormalizeCustomerText(value) is { Length: > 2 } normalized
            && sources.Any(source => source.Contains(normalized, StringComparison.Ordinal));
        if (details.Name is not null && NormalizeCustomerText(details.Name) != NormalizeCustomerText(visitorName) && !Supplied(details.Name))
            return ("name", details);
        if (details.Phone is not null)
        {
            if (!BrazilianPhone.TryNormalize(details.Phone, out var requested))
                return ("phone", details);
            var phones = messages.SelectMany(message => Regex.Matches(message,
                @"(?<!\d)(?:\+?55[\s().-]*)?(?:\(\s*)?[1-9]\d(?:\s*\))?[\s.-]*\d{4,5}[\s.-]*\d{4}(?!\d)", RegexOptions.CultureInvariant).Cast<Match>());
            if (!phones.Any(phone => BrazilianPhone.TryNormalize(phone.Value, out var supplied) && supplied == requested))
                return ("phone", details);
        }
        if (details.Address is not { } address)
            return (null, details);
        if (!Supplied(address.Street) || !Supplied(address.Number) || !Supplied(address.Neighborhood))
            return ("address", details);
        // Optional data inferred by a model is omitted, never saved as customer
        // input. Required street/number/neighborhood still fail the entire change.
        return (null, details with
        {
            Address = address with
            {
                Complement = Supplied(address.Complement) ? address.Complement : null,
                Reference = Supplied(address.Reference) ? address.Reference : null,
                PostalCode = Supplied(address.PostalCode) ? address.PostalCode : null
            }
        });
    }

    private static string NormalizeCustomerText(string value)
    {
        var plain = string.Concat(value.Normalize(NormalizationForm.FormD)
            .Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark));
        return " " + Regex.Replace(plain.ToUpperInvariant(), @"[^\p{L}\p{Nd}]+", " ").Trim() + " ";
    }

    private async Task<string> ConfirmationTextAsync(Guid id, CancellationToken cancellationToken)
    {
        var state = await orders.GetStateAsync(id, cancellationToken);
        return state?.Receipt is not null ? $"O pedido #{state.Receipt.Number} foi recebido e aguarda o fluxo da equipe. Você pode acompanhar pelo comprovante."
            : state?.Review is not null ? "Confira o resumo exibido na conversa e use Confirmar pedido para enviar. Seu pedido ainda não foi criado."
            : "Precisamos revisar o pedido com todos os dados antes da confirmação. Seu pedido ainda não foi criado.";
    }

    private static AiToolResult OrderResult(ChatOrderStateResponse state)
    {
        var data = new
        {
            state.DraftVersion,
            state.Items,
            state.Name,
            hasPhone = state.Phone is not null,
            state.Fulfillment,
            state.MissingFields,
            state.NeedsReview,
            state.Quote,
            review = state.Review is null ? null : new
            {
                state.Review.Items,
                state.Review.Fulfillment,
                state.Review.Subtotal,
                state.Review.DeliveryFee,
                state.Review.Total
            },
            receipt = state.Receipt is null ? null : new { state.Receipt.Number, state.Receipt.Fulfillment, state.Receipt.Total }
        };
        if (state.Receipt is not null)
            return Success(data, $"O pedido #{state.Receipt.Number} já foi recebido. Para alterações, fale com um atendente.");
        var text = state.Items.Length == 0 ? "Seu pedido em montagem está sem itens. Qual produto do cardápio você gostaria de escolher?"
            : "Pedido em montagem:\n" + string.Join("\n", state.Items.Take(10).Select(item => $"{item.Quantity} × {item.Name}: {Money(item.LineTotal)}."));
        if (state.Review is not null)
            text += $"\nSubtotal: {Money(state.Review.Subtotal)}. Entrega: {Money(state.Review.DeliveryFee)}. Total: {Money(state.Review.Total)}.\nConfira todos os dados no resumo e use Confirmar pedido para enviar. Seu pedido ainda não foi criado.";
        else if (state.MissingFields.Length > 0)
            text += "\n" + (state.MissingFields[0] switch
            {
                "phone" => "Qual é seu telefone com DDD?",
                "fulfillment" => "Você prefere entrega ou retirada?",
                "address" => "Qual é o bairro, rua e número para entrega?",
                "name" => "Qual nome devo colocar no pedido?",
                _ => "Qual produto deseja escolher?"
            });
        else
            text += "\nOs dados ainda precisam ser revisados antes de confirmar. Seu pedido ainda não foi criado.";
        return Success(data, Limit(text));
    }

    private static AiToolResult Success(object data, string? text) => new(JsonSerializer.Serialize(new { success = true, data }, Json), text);
    private static AiToolResult Failure(string code, string text) => new(JsonSerializer.Serialize(new { success = false, code }, Json), Limit(text));
    private static string ProductLine(MenuProductResponse product) => $"{product.Name}: {Money(product.Price)}{(product.IsAvailable ? "" : " — indisponível")}.";
    private static string Money(decimal value) => value.ToString("C2", Portuguese);
    private static string Limit(string text) => text.Length <= 1950 ? text : text[..1900] + "\nConsulte os detalhes completos no cardápio ou no resumo.";
    private static string DayName(int day) => day switch { 1 => "Segunda", 2 => "Terça", 3 => "Quarta", 4 => "Quinta", 5 => "Sexta", 6 => "Sábado", 7 => "Domingo", _ => "" };
    private static string StatusName(string status) => status switch
    {
        "New" => "recebido, aguardando aceite da equipe",
        "Confirmed" => "confirmado pela equipe",
        "InPreparation" => "em preparo",
        "Ready" => "pronto",
        "OutForDelivery" => "saiu para entrega",
        "AwaitingDelivery" => "aguardando saída para entrega",
        "Delivered" => "entregue",
        "Finalized" => "finalizado",
        "Cancelled" => "cancelado",
        _ => "aguardando atualização da equipe"
    };
    private static Guid ProductId(JsonElement args) => Guid.TryParse(RequiredString(args, "productId", 36), out var id) && id != Guid.Empty ? id : throw new JsonException();
    private static string RequiredString(JsonElement args, string key, int max) => OptionalString(args, key, max) is { Length: > 0 } value ? value : throw new JsonException();
    private static string? OptionalString(JsonElement args, string key, int max)
    {
        if (!args.TryGetProperty(key, out var value) || value.ValueKind == JsonValueKind.Null)
            return null;
        if (value.ValueKind != JsonValueKind.String || value.GetString()!.Length > max)
            throw new JsonException();
        return value.GetString();
    }
    private static int Int(JsonElement args, string key, int min, int max) => args.TryGetProperty(key, out var value) && value.TryGetInt32(out var number) && number >= min && number <= max ? number : throw new JsonException();
    private static void Fields(JsonElement args, params string[] allowed)
    {
        if (args.ValueKind != JsonValueKind.Object || args.EnumerateObject().Any(property => !allowed.Contains(property.Name, StringComparer.Ordinal))
            || args.EnumerateObject().GroupBy(property => property.Name).Any(group => group.Count() > 1))
            throw new JsonException();
    }
}
