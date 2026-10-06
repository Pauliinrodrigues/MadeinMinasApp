# Entrega pelo site — Fase 8F

Incremento autorizado em 05/10/2026, na branch `feat/public-delivery`, a partir da master `0b7cc92`. O responsável autorizou implementação, testes, commit, PR e integração por squash na master. A autorização não significa que realizou o roteiro manual.

## Fluxo

Em `/pedido/finalizar`, o cliente escolhe **Retirada no balcão** ou **Entrega**. Para entrega, seleciona bairro/cidade atendidos e informa rua, número (aceita `s/n`), complemento, CEP e referência. Complemento, CEP e referência são opcionais. A revisão apresenta endereço completo, subtotal, taxa e total antes de enviar.

O backend determina bairro/cidade/UF e taxa a partir da região selecionada; o navegador não informa preço nem escolhe uma cidade fora da cobertura. Não há estimativa de prazo, geocodificação, cálculo por distância ou consulta automática de CEP. A equipe confere contato e endereço antes de confirmar o preparo. O cliente combina a forma de pagamento com o atendimento; não há cobrança online nem escolha automática de um meio de pagamento.

O pedido nasce `New`, origem `DirectLink`, estoque `Pending`. Confirmar pela equipe mantém as validações de cliente, produto, ficha e estoque. Cozinha, expedição, impressão do navegador, entrega e pagamento usam os fluxos existentes. Finalizar exige pagamento recebido. O acompanhamento distingue retirada de entrega nos textos e preserva o histórico.

## Cobertura e valores

O administrador mantém as regiões em **Cadastros → Regiões de entrega**, após aplicar `AddDeliverySettings`. A tela exige bairro, cidade, UF e taxa explícita, com revisão antes de salvar; permite pausar e reativar. Cada entrada tem identificador estável, taxa de 0 a 9999.99 com até duas casas decimais e situação ativa/pausada. Zero representa entrega gratuita quando informado expressamente. IDs e combinações bairro/cidade/UF devem ser únicos, até 500 regiões. Regras e concorrência: [delivery-settings.md](delivery-settings.md).

Por compatibilidade, antes do primeiro salvamento, `PublicDelivery:Areas` fornece a lista inicial por ambiente. GET não a importa automaticamente. Depois de salvar, a lista no banco é a fonte da cobertura; somente regiões ativas aparecem no site. Configuração legada inválida ainda impede iniciar a API.

**Nenhuma cobertura ou taxa operacional foi inventada.** O responsável ainda precisa informar as regiões e valores reais. A configuração versionada começa com `Areas: []`: retirada continua disponível; entrega informa indisponibilidade e orienta atendimento. Os locais e valores dos testes são exclusivamente fictícios.

Para ativar agora, cadastre e confirme os locais/valores aprovados pela tela administrativa, sem reiniciar a API. Em instalações que ainda usam somente a configuração inicial, é possível preencher `PublicDelivery` e reiniciar a API, sem substituir outros segredos. O formato legado por variáveis é:

```text
PublicDelivery__Areas__0__Id=<identificador-estavel>
PublicDelivery__Areas__0__Neighborhood=<bairro-atendido>
PublicDelivery__Areas__0__City=<cidade-atendida>
PublicDelivery__Areas__0__State=<UF>
PublicDelivery__Areas__0__Fee=<valor-decimal-com-ponto>
```

Índices adicionais seguem 1, 2 etc. Taxa única é representada repetindo o mesmo valor em cada bairro autorizado. Se a operação exigir distância ou divisões dentro de um bairro, definir e implementar essa regra antes de ativar a região inteira. Em múltiplas instâncias, manter a configuração inicial consistente; depois do primeiro salvamento, todas consultam o banco compartilhado.

## Contratos

`GET /api/public-checkout/delivery-areas` retorna uma lista de `{ id, neighborhood, city, state, fee }`, anônima e `no-store`. Compartilha o limite de 20 requisições/minuto/IP do checkout. Não consulta clientes nem recebe telefone ou endereço.

`POST /api/public-checkout/review` mantém o contrato de retirada; omitir `fulfillment` equivale a `Pickup`. Para entrega:

```text
{
  name, phone, fulfillment: "Delivery",
  address: { areaId, street, number, complement?, postalCode?, reference? },
  cart: { items: [{ productId, quantity, notes? }], notes? }
}
```

Retirada rejeita `address`; entrega exige endereço válido e região cadastrada. Campos desconhecidos, inclusive taxa, endereço de cadastro, bairro ou cidade enviados pelo cliente, são rejeitados. Rua até 120, número 20, complemento 120, referência 250; CEP opcional com oito dígitos, com ou sem hífen. Campos obrigatórios não aceitam espaços vazios.

A resposta acrescenta `address` normalizado e `deliveryAreaId`, mais `fulfillment`, `subtotal`, `deliveryFee` e `total`. `address.id` é nulo: trata-se da cópia informada para essa compra, não de um endereço do cadastro. Nome/telefone continuam autodeclarados.

`POST /api/public-checkout/orders` recebe o mesmo envelope `{ requestId, reviewToken, checkout }`. Endereço/modalidade/região/taxa participam da revisão. Mudanças antes do envio exigem revisar novamente (`409 OrderReviewChanged`); região pausada/desconhecida produz `409 PublicDeliveryUnavailable`. A transação desfaz também eventual novo cliente. A criação mantém um bloqueio compartilhado de cobertura até o commit; uma edição administrativa usa o bloqueio exclusivo correspondente.

Tentativa já gravada retorna o comprovante original antes de consultar a cobertura atual. Mesmo identificador com outro endereço/conteúdo retorna `409 OrderRequestConflict`. Hashes de retirada preservam o formato anterior para recuperar envios da 8C/8D/8E. O comprovante e o acompanhamento não expõem endereço ou cadastro; a modalidade e o total identificam a entrega.

## Privacidade e persistência

O telefone não comprova identidade. A entrega não consulta, cadastra nem altera `Addresses` do cliente encontrado pelo telefone. Os dados informados ficam somente nas colunas históricas `Orders.Address*`, com `AddressId=null`, e aparecem para a equipe autorizada no detalhe e na expedição/impressão.

Durante envio incerto, a tentativa incluindo endereço permanece em `sessionStorage` da aba. Recuperar repete conteúdo e chave. Rejeição definitiva restaura contato, modalidade, endereço e carrinho para nova revisão; sucesso substitui os dados pessoais pelo comprovante mínimo. Edição de modalidade/endereço invalida a revisão. Retirada nunca envia os campos de endereço preenchidos anteriormente.

## Banco

`20261005210534_AddPublicDelivery` ajusta somente constraints de `Orders`: permite `DirectLink` com entrega e cópia de endereço sem referência ao cadastro. Pedidos manuais de entrega continuam exigindo `AddressId`; retirada continua exigindo endereço vazio e taxa zero. Nenhuma tabela, coluna ou dependência nova. O Down recusa reversão quando existem entregas públicas, preservando seus dados.

Aplicar após backup e conferência do destino, conforme [database/README.md](../database/README.md). A migration não ativa cobertura. Detalhe/expedição passam a retornar endereço mesmo com `id=null`; o contrato compartilhado de resposta foi ajustado pela refatoração do Rider.

## Validação

1. Sem regiões configuradas, abrir entrega e conferir orientação para atendimento; retirada deve continuar funcionando.
2. Em testes, configurar uma região, montar carrinho e escolher entrega. Campos obrigatórios e CEP inválido devem impedir revisão.
3. Conferir endereço, complemento, referência, taxa e total; editar endereço ou modalidade deve remover a revisão.
4. Enviar e conferir pedido Novo, modalidade Entrega, endereço e valores no atendimento. Confirmar, preparar, expedir, entregar, registrar pagamento e finalizar.
5. Alterar/remover a região entre revisão e envio: exigir nova revisão, sem pedido ou cliente parcial. Alterações posteriores ao pedido não reescrevem sua compra.
6. Simular resposta perdida e recarregar: **Conferir envio** recupera o mesmo número e endereço, sem duplicação. Após sucesso, dados pessoais não permanecem na recuperação.
7. Validar desktop/celular, textos de acompanhamento e impressão da expedição. Não gerar pedidos fictícios na operação.

Testes backend usam PostgreSQL exclusivo em `127.0.0.1:55433`; testes de navegador interceptam a API. Resultados em [validation.md](validation.md).

Arquivos principais: `PublicCheckoutContracts`, `PublicCheckoutService`, `PublicCheckoutFingerprint`, `PublicDeliveryOptions`, `PublicCheckoutController`, `OrderService`, `DispatchService`, configuração/migration EF; checkout, carrinho, cardápio, acompanhamento e contratos/interceptor do frontend; `PublicDeliveryTests`, testes Playwright e documentação.
