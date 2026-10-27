namespace MadeInMinas.Api.Services.Ai;

public static class AiToolCatalog
{
    private static object Text(bool nullable = false) => new { type = nullable ? (object)new[] { "string", "null" } : "string" };
    private static object Integer(int minimum, int maximum) => new { type = "integer", minimum, maximum };
    private static object Schema(Dictionary<string, object> properties) => new
    {
        type = "object",
        properties,
        required = properties.Keys.ToArray(),
        additionalProperties = false
    };
    private static object Empty() => Schema([]);
    private static AiToolDefinition Tool(string name, string description, Dictionary<string, object>? properties = null) =>
        new(name, description, properties is null ? Empty() : Schema(properties));

    public static IReadOnlyList<AiToolDefinition> All { get; } =
    [
        Tool("consultar_cardapio", "Consulta o cardápio público real. Consulta geral: search=null e page=1, incluindo acompanhamentos. Use busca somente para produto ou categoria específica; nunca invente produtos. Paginação de 48 itens.",
            new() { ["search"] = Text(true), ["page"] = Integer(1, 1000000) }),
        Tool("consultar_produto", "Responde perguntas de ingredientes/composição com a descrição comercial real. Execute após localizar o ID com consultar_cardapio; a lista do cardápio sozinha não apresenta a composição ao cliente.",
            new() { ["productId"] = Text() }),
        Tool("consultar_disponibilidade", "Consulta somente a disponibilidade pública do produto, sem dados de estoque.",
            new() { ["productId"] = Text() }),
        Tool("consultar_promocoes", "Verifica se existe suporte a promoções cadastradas. Não autoriza desconto."),
        Tool("consultar_taxa_entrega", "Consulta regiões, taxas reais e endereço de retirada. Argumentos: {}. Para horários e pagamentos execute também consultar_atendimento, em outra rodada."),
        Tool("consultar_atendimento", "Consulta horários, abertura, pausa, formas de pagamento e informação real do prazo. Argumentos: {}. Para taxas execute também consultar_taxa_entrega, em outra rodada."),
        Tool("adicionar_item_pedido", "Seleciona produto real na montagem. Quantity é a quantidade TOTAL desejada desse produto, não incremento. Peça esclarecimento em caso de ambiguidade.",
            new() { ["productId"] = Text(), ["quantity"] = Integer(1, 99), ["notes"] = Text(true) }),
        Tool("alterar_quantidade", "Altera a quantidade TOTAL de produto já selecionado; exige nova revisão antes de confirmar.",
            new() { ["productId"] = Text(), ["quantity"] = Integer(1, 99), ["notes"] = Text(true) }),
        Tool("remover_item_pedido", "Remove da montagem um produto selecionado. Não cancela um pedido já criado.",
            new() { ["productId"] = Text() }),
        Tool("definir_dados_pedido", "Registra somente dados informados pelo cliente. null preserva o campo atual. Retirada: fulfillment=Pickup e address=null, obrigatoriamente. Nunca copie o endereço da loja para o cliente. Entrega: fulfillment=Delivery e areaId obtido de consultar_taxa_entrega. Campos ainda não informados devem ser null. Não invente telefone, modalidade, endereço ou observações. Notas não representam adicionais cobrados ou descontos.",
            new()
            {
                ["name"] = Text(true), ["phone"] = Text(true),
                ["fulfillment"] = new { type = new[] { "string", "null" }, @enum = new string?[] { "Pickup", "Delivery", null } },
                ["notes"] = Text(true),
                ["address"] = new
                {
                    type = new[] { "object", "null" },
                    properties = new Dictionary<string, object>
                    {
                        ["areaId"] = Text(), ["street"] = Text(), ["number"] = Text(),
                        ["neighborhood"] = Text(true), ["complement"] = Text(true),
                        ["postalCode"] = Text(true), ["reference"] = Text(true)
                    },
                    required = new[] { "areaId", "street", "number", "neighborhood", "complement", "postalCode", "reference" },
                    additionalProperties = false
                }
            }),
        Tool("calcular_pedido", "Calcula e persiste o resumo atual com os serviços do backend. Solicite apenas os campos ausentes. Não cria pedido; o cliente deve usar Confirmar pedido no resumo."),
        Tool("consultar_status_pedido", "Consulta somente o pedido criado e vinculado a ESTA conversa. Não aceita IDs, telefones ou tokens de terceiros."),
        Tool("solicitar_atendimento_humano", "Encaminha a conversa para a fila humana e interrompe o modo automático."),
        Tool("responder_cliente", "Conclui a resposta usando uma mensagem autorizada pelo servidor. Use ToolResult depois de uma consulta ou montagem bem-sucedida; nunca produza preços, nomes ou afirmações livres.",
            new() { ["code"] = new { type = "string", @enum = new[] { "ToolResult", "Greeting", "AskProduct", "AskQuantity", "AskName", "AskPhone", "AskFulfillment", "AskAddress", "Clarify", "Unsupported", "DiscountNotAvailable", "PromotionNotAvailable", "Unknown", "AwaitConfirmation" } } })
    ];
}
