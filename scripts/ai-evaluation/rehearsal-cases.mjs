const reply = (state) =>
  state.messages.filter((message) => message.kind === "Assistant").at(-1)
    ?.text ?? "";
const check = (name, passed) => ({ name, passed: Boolean(passed) });
const cents = (number) => Math.round(number * 100);
const formatted = (number) =>
  number.toLocaleString("pt-BR", {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  });

export function rehearsalCases(snapshot) {
  const product = snapshot.products.find((item) => item.isAvailable);
  if (!product) throw new Error("NoAvailableRehearsalProduct");
  const scenarios = [
    {
      id: "store-menu",
      purpose: "Consulta do cardápio copiado da loja",
      steps: [
        {
          message: "Boa noite! Quero ver o cardápio.",
          verify: (state) => [
            check(
              "nome e preço cadastrados",
              reply(state).includes(product.name) &&
                reply(state).includes(formatted(product.price)),
            ),
            check(
              "consulta não escolhe produtos",
              !state.order?.items?.length && !state.order?.receipt,
            ),
          ],
        },
      ],
    },
    {
      id: "store-product",
      purpose: "Ingredientes usam exclusivamente a descrição comercial",
      steps: [
        {
          message: `Quais são os ingredientes do ${product.name}?`,
          verify: (state) => [
            check(
              "descrição comercial preservada",
              product.description
                ? reply(state).includes(product.description.slice(0, 120))
                : /não tenho|atendente|não.*disponível/i.test(reply(state)),
            ),
            check(
              "sem ficha técnica ou estoque",
              !/Insumo fictício|CurrentStock|UnitCost|Margem|CMV/.test(
                reply(state),
              ),
            ),
          ],
        },
      ],
    },
    {
      id: "store-rules",
      purpose:
        "Horários, pagamentos e taxas correspondem à configuração copiada",
      steps: [
        {
          message:
            "Quais são os horários, as formas de pagamento e as taxas de entrega e retirada?",
          verify: (state) => [
            check(
              "horários consultados",
              snapshot.attendance.hours
                .filter((hour) => !hour.isClosed)
                .every(
                  (hour) =>
                    reply(state).includes(hour.opensAt) &&
                    reply(state).includes(hour.closesAt),
                ),
            ),
            check(
              "pagamentos cadastrados",
              snapshot.attendance.paymentMethods.every((method) =>
                reply(state)
                  .toLowerCase()
                  .includes(
                    { Card: "cartão", Cash: "dinheiro", Pix: "pix" }[method],
                  ),
              ),
            ),
            check(
              "taxas consultadas",
              reply(state).includes(formatted(0)) &&
                snapshot.areas.every((area) =>
                  reply(state).includes(formatted(area.fee)),
                ),
            ),
          ],
        },
      ],
    },
    {
      id: "store-pickup",
      purpose:
        "Retirada com preço real, alteração e nova revisão sem criar compra",
      steps: [
        {
          message: `Quero um ${product.name} para retirada. Meu telefone fictício é (38) 99999-1357. Revise meu pedido.`,
          verify: (state) => [
            check(
              "resumo de retirada correto",
              cents(state.order?.review?.total) === cents(product.price) &&
                state.order?.review?.deliveryFee === 0,
            ),
            check(
              "sem endereço ou comprovante",
              state.order?.fulfillment === "Pickup" &&
                !state.order?.address &&
                !state.order?.receipt,
            ),
          ],
        },
        {
          message: `Mude a quantidade total de ${product.name} para dois e revise novamente.`,
          repeat: true,
          verify: (state, history) => [
            check(
              "quantidade total dois",
              state.order?.items?.length === 1 &&
                state.order.items[0].quantity === 2,
            ),
            check(
              "novo resumo calculado",
              cents(state.order?.review?.total) === cents(product.price * 2),
            ),
            check(
              "revisão anterior substituída",
              state.order?.review?.reviewToken &&
                state.order.review.reviewToken !==
                  history[0]?.order?.review?.reviewToken,
            ),
            check("sem compra criada", !state.order?.receipt),
          ],
        },
      ],
    },
  ];
  const area = snapshot.areas.find((item) => item.coversAllNeighborhoods);
  if (area)
    scenarios.push({
      id: "store-delivery",
      purpose: "Entrega com dados fictícios e região/taxa cadastradas",
      steps: [
        {
          message: `Quero um ${product.name} para entrega em ${area.city}-${area.state}, mas ainda não informei telefone ou endereço.`,
          verify: (state) => [
            check(
              "dados não inventados",
              !state.order?.phone && !state.order?.address,
            ),
            check(
              "pedido incompleto sem revisão",
              !state.order?.review && !state.order?.receipt,
            ),
          ],
        },
        {
          message: `Meu telefone fictício é (38) 99999-2468. Entrega em ${area.city}-${area.state}, Rua do Ensaio, número 120, bairro Centro. Revise meu pedido.`,
          verify: (state) => [
            check(
              "endereço informado pelo cliente",
              state.order?.address?.street === "Rua do Ensaio" &&
                state.order?.address?.number === "120" &&
                state.order?.address?.neighborhood === "Centro",
            ),
            check(
              "região e taxa corretas",
              state.order?.address?.areaId === area.id &&
                state.order?.review?.deliveryFee === area.fee,
            ),
            check(
              "total calculado no backend",
              cents(state.order?.review?.total) ===
                cents(product.price + area.fee),
            ),
            check("sem compra criada", !state.order?.receipt),
          ],
        },
      ],
    });
  scenarios.push({
    id: "store-human",
    purpose: "Transferência persistida para a equipe",
    steps: [
      {
        message: "Quero falar com um atendente humano, por favor.",
        verify: (state) => [
          check("modo manual persistido", state.automationMode === "Manual"),
          check(
            "encaminhamento informado",
            /encaminhar.*atendente/i.test(reply(state)),
          ),
        ],
      },
    ],
  });
  return scenarios;
}

export function selectRehearsalCases(snapshot, selection) {
  const all = rehearsalCases(snapshot);
  if (!selection) return all;
  const ids = selection.split(",").map((id) => id.trim());
  if (ids.some((id) => !all.some((scenario) => scenario.id === id)))
    throw new Error("InvalidRehearsalCases");
  return all.filter((scenario) => ids.includes(scenario.id));
}
