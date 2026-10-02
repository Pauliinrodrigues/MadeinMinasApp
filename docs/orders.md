# Pedidos manuais — Fase 4C

Incremento na branch `feat/manual-orders`, a partir da Fase 4B validada. Escopo: registrar o carrinho revisado, consultar pedidos e alterar os estados iniciais com histórico. Pagamentos, KDS, estoque, combos e adicionais seguem em etapas próprias.

Aceite manual concluído pelo usuário em 01/10/2026, com autorização para commit, publicação e integração à master pelo fluxo de pull request após os checks de qualidade.

## Regras validadas para este incremento

- Administrador e atendente usam `orders.manage`. Cozinha e expedição terão contratos próprios na fase 5, sem acesso a dados completos neste incremento.
- Registro cria pedido `New` (Novo), origem `Manual`, número sequencial único gerado pelo PostgreSQL e identificador UUID. A numeração não reinicia diariamente e pode ter lacunas.
- Guardar cópias de nome/telefone do cliente, endereço, nomes/preços/quantidades/observações dos itens e valores. Alterações posteriores no cadastro não reescrevem a compra. Itens e dados comerciais do pedido são imutáveis nesta entrega; corrigir exige cancelar e registrar outro pedido.
- A revisão retorna uma identificação do conteúdo revisado. O registro revalida sessão, disponibilidade e dados em transação; divergência exige revisar novamente. Nenhum preço unitário é aceito do navegador.
- `requestId` identifica uma tentativa de registro por funcionário. Repetir o mesmo conteúdo retorna o mesmo pedido, inclusive após perder a resposta. Reutilizar o identificador com outro conteúdo retorna conflito. Registro concorrente não pode duplicar pedido ou número.
- Novo → Confirmado: administrador ou atendente; revalidar disponibilidade atual, preservando os preços já registrados. Confirmar não significa receber pagamento.
- Novo → Cancelado: administrador ou atendente. Confirmado → Cancelado: somente administrador. Cancelamento exige motivo e não apaga o pedido. Cancelado é terminal.
- A partir da Fase 4D, cancelar exige antes cancelar a intenção pendente ou registrar devolução do pagamento recebido. O bloqueio retorna `OrderPaymentUnresolved` (409). Veja [pagamentos](payments.md).
- A Fase 5A amplia a consulta para InPreparation/Ready. A cozinha altera produção por contrato próprio, sem acesso aos dados comerciais. Cancelamento nessas etapas exige administrador, motivo e resolução do pagamento, mantendo o bloqueio/versão do pedido. Veja [KDS](kitchen.md).
- Histórico inclui criação e cada transição, com data UTC, funcionário e motivo. Alterações de status usam versão para detectar decisões concorrentes e são serializadas por pedido.
- Não há descontos neste incremento. Adotada inicialmente taxa de entrega informada pela equipe e validada pelo backend: valor explícito entre R$ 0,00 e R$ 9.999,99, com até duas casas decimais, inclusive zero para entrega gratuita. Retirada tem taxa zero. Essa política é parte do aceite manual; não há cálculo por distância/bairro.

## Modelo e API

Três tabelas novas: `Orders`, `OrderItems` e `OrderStatusHistory`. Endereço e identificação do cliente ficam como colunas de cópia em Orders, sem tabelas extras. Relações com cadastros e funcionários têm exclusão restrita. Itens e histórico pertencem ao pedido. Valores usam decimal/numeric; datas UTC; número único e índice de idempotência garantidos pelo banco.

- `GET /api/orders`: paginação, busca, filtro de status e cliente.
- `POST /api/orders`: carrinho, identificação da revisão e requestId; 201 na criação, 200 na repetição.
- `GET /api/orders/{id}`: detalhes e histórico.
- `PUT /api/orders/{id}/status`: status, versão esperada e motivo.

O frontend possui botão de registro no carrinho revisado, listagem e detalhes de pedidos. Falha de resultado desconhecido preserva a tentativa na página para repetir com a mesma chave e bloqueia a edição. Totais sempre vêm da API. Sair da página ou perder a sessão descarta o carrinho e a chave; nesse caso, consultar Pedidos antes de iniciar outra compra. Idempotência não deduplica tentativas com chaves diferentes.

### Contratos

`POST /api/cart/quote` recebe os campos da Fase 4B e `deliveryFee`: obrigatório na entrega; omitido/nulo/zero na retirada. Retorna também `deliveryFee`, `total` e `reviewToken`. A identificação da revisão é um SHA-256 do conteúdo comercial, sem horário de consulta; não é credencial nem reserva de preço. O backend recalcula e compara os dados ao registrar.

`POST /api/orders` recebe `{ "requestId": "UUID", "reviewToken": "64 caracteres hexadecimais maiúsculos", "cart": { ... } }`. Cart contém somente os campos de entrada da revisão. Preços, totais, origem, número, status e funcionário não podem ser enviados; campos desconhecidos são rejeitados. Limites: 50 linhas e 99 unidades por produto, com observações por linha e gerais.

`PUT /api/orders/{id}/status` recebe `{ "status": "Confirmed", "expectedVersion": 1, "reason": null }` ou `Cancelled` com motivo de até 500 caracteres. Repetição idêntica da última transição pelo mesmo funcionário não duplica histórico. `expectedVersion` desatualizada exige nova leitura. O cancelamento pode ocorrer mesmo com cadastros atualmente inativos.

`GET /api/orders` aceita `page` (1–1.000.000), `pageSize` (1–100, padrão 20), `search` (até 120 caracteres: nome, telefone ou número, com # opcional), `status` e `customerId`. Ordem decrescente por criação/número. Detalhe retorna cópias da compra, valores, versão e histórico ordenado; não expõe hash nem chave de reenvio.

Erros de domínio: `OrderNotFound` (404); `OrderReviewChanged`, `OrderRequestConflict`, `OrderVersionConflict`, `OrderTransitionDenied` (409); `OrderCancellationDenied`/`PermissionDenied` (403); `InvalidSession` (401). Indisponibilidade reaproveita os códigos Cart* (409). DTO inválido retorna 400. Respostas de pedidos usam `Cache-Control: no-store`.

### Persistência e concorrência

Migration `20261001142005_AddManualOrders` cria somente as três tabelas e seus índices/constraints. `Orders.Number` é uma identity com índice único; `(CreatedById, RequestId)` é único. Totais numeric(12,2), taxa numeric(6,2), preço unitário numeric(8,2), total de linha numeric(10,2). Não há exclusão nem edição comercial pela API.

Criação mantém bloqueio transacional por funcionário/tentativa e bloqueios compartilhados nos cadastros consultados. Reenvio já concluído é resolvido antes da disponibilidade atual. Escrita revalida funcionário e security stamp na transação. Status usa bloqueio de linha e versão; grava pedido e histórico atomicamente. Nenhum efeito financeiro, de estoque ou integração é disparado neste incremento.

## Validação automatizada

HTTP com PostgreSQL isolado: autorização, cópias históricas, revisão desatualizada, idempotência/repetição concorrente, numeração, versões/transições, cancelamento e invariantes do banco. Navegador desktop/celular: registro, recuperação de falha, revisão desatualizada, consulta, filtros e confirmação/cancelamento. Foram aprovados 214 testes de backend e validados 174 cenários de navegador entre execuções; resultados e limites em [validation.md](validation.md). Migration revisada e aplicada exclusivamente no banco local `made_in_minas`, após backup e confirmação do destino. Aceite manual antes de avançar.

## Roteiro de aceite manual

1. Entrar como administrador/atendente em `http://localhost:8101/entrar`, abrir Carrinho e selecionar cliente e produtos ativos.
2. Para entrega, escolher endereço e informar a taxa; revisar e conferir itens, observações e total. Para retirada, conferir taxa zero e ausência de endereço.
3. Registrar e conferir número, status Novo e primeira entrada do histórico. Voltar a Pedidos, buscar o número e reabrir.
4. Confirmar o pedido e conferir histórico. Como atendente, um pedido confirmado não oferece cancelamento; como administrador, cancelar com motivo.
5. Criar outro pedido e cancelar ainda Novo como atendente. Conferir que o pedido continua na consulta e não permite reativação.
6. Em outro atendimento, alterar preço ou endereço depois da revisão e antes de registrar. O registro deve exigir nova revisão. Alterar cadastro depois de salvar não muda o pedido existente.
7. Em duas sessões, abrir o mesmo pedido Novo e realizar ações concorrentes. A segunda ação baseada na versão antiga deve pedir atualização.

Pagamento, estados da cozinha e uso real em produção continuam fora deste aceite. O usuário validou o incremento e autorizou sua publicação e integração; o roteiro acima permanece como referência de verificação.

## Arquivos do incremento

- Backend: Models/Order*, Data/Configurations/Order*, DTOs/Orders, Services/OrderService, OrderFingerprint e OrderException, Controllers/OrdersController, Infrastructure/OrderExceptionHandler; registros no Program e AppDbContext, extensão de CartContracts/CartService e migration/snapshot.
- Testes: OrderTests e CartTests; navegador em e2e/orders.spec.ts e e2e/cart.spec.ts.
- Frontend: core/services/order-api.service.ts, features/orders, extensão do carrinho, rotas, menu e mensagens de erro.
- Documentação: README, orders, cart, API, modelo, decisões, regras, roteiro e validação.

Na Fase 5B, o pedido também acompanha AwaitingDelivery, OutForDelivery, Delivered e Finalized. Atendimento visualiza esses estados e histórico; expedição conduz as transições por rota própria. Cancelamento administrativo é permitido até a saída em rota, com pagamento resolvido; após entrega não há cancelamento. Finalizar exige recebimento integral. Os dois tipos de comanda são acessados pelo detalhe. Ver [expedição e impressão](dispatch-printing.md).

Na Fase 6B, confirmar também exige fichas completas, ingredientes ativos e saldo suficiente, gravando baixa e composição histórica na transação do pedido. Cancelamento antes do preparo devolve o consumo original; após início do preparo mantém o consumo. Pedidos antigos já processados ficam Legacy, sem efeitos retroativos. O detalhe mostra stockStatus e stockComponents; a interface explica os efeitos antes de confirmar. Regras e aceite específicos: [consumo de estoque por pedidos](order-stock.md).
