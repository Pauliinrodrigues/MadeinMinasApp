# Pagamentos manuais — Fase 4D

Incremento na branch `feat/manual-payments`, após o aceite e integração da Fase 4C pelo PR #4. Escopo inicial: registrar forma de pagamento integral, recebimento conferido pela equipe, troco em dinheiro e devolução integral já realizada. Não executa cobranças, transferências, estornos bancários, conciliação ou lançamentos financeiros.

## Regras deste incremento

- `payments.manage`: administrador e atendente. `payments.refund`: somente administrador. Cozinha e expedição não acessam pagamentos.
- Formas: Cash (dinheiro), Pix, CreditCard e DebitCard. Uma forma integral por vez; divisão e recebimentos parciais ficam para incremento próprio. Escolhas iniciais validadas pelo usuário em 01/10/2026.
- Criar pagamento gera Pending, com valor copiado do total persistido do pedido. O navegador não informa valor cobrado. Número/itens/status do pedido permanecem independentes.
- Pending → Received exige confirmação explícita de recebimento já conferido. Em dinheiro, informar valor entregue pelo cliente; backend calcula troco. Nas demais formas não se informa dinheiro/troco. Não armazenar número de cartão, CVV, senha ou dados bancários.
- Pending → Cancelled exige motivo. Received → Refunded exige administrador, motivo e confirmação explícita de devolução integral já realizada fora do sistema. Cancelled e Refunded são terminais; não apagam o histórico.
- No máximo um pagamento Pending ou Received por pedido. Depois de cancelar uma intenção ou registrar uma devolução, pode-se criar nova tentativa enquanto o pedido estiver ativo. Novos registros/recebimentos não são permitidos em pedidos cancelados.
- Cancelar pedido exige antes cancelar seu pagamento pendente ou registrar a devolução do recebido. Bloqueio do pedido serializa criação, recebimento, devolução e cancelamento para evitar decisões concorrentes incompatíveis.
- Criação usa requestId por funcionário e conteúdo canônico; reenvio igual retorna a mesma tentativa, inclusive após mudança de status. Reutilização com outro conteúdo é conflito. Alterações usam versão do pagamento; repetição idêntica da última transição não duplica histórico.
- Pagamentos e histórico preservam funcionário, datas UTC e motivo. Não há edição de valor/forma após criação; corrigir exige cancelar a intenção e criar outra. Devolução não confirma cancelamento do pedido automaticamente.

## Modelo e API

Payments referencia Orders e Users com exclusão restrita. PaymentStatusHistory registra criação/transições e referencia funcionário, com nome copiado. Histórico é limitado pelas transições permitidas por tentativa. Índice parcial único impede dois pagamentos ativos no mesmo pedido. Dinheiro usa numeric(12,2), com validação antes de persistir.

- GET /api/orders/{orderId}/payments: lista paginada, total do pedido, valor recebido líquido, saldo a receber e pagamento ativo, em leitura consistente.
- GET /api/orders/{orderId}/payments/{id}: tentativa e histórico.
- POST /api/orders/{orderId}/payments: requestId, forma e versão esperada do pedido; 201/200.
- PUT /api/orders/{orderId}/payments/{id}/receive: versão, confirmação de recebimento e dinheiro entregue quando aplicável.
- PUT /api/orders/{orderId}/payments/{id}/cancel: versão e motivo.
- PUT /api/orders/{orderId}/payments/{id}/refund: versão, motivo e confirmação de devolução; administrador.

O frontend possui página de pagamentos acessível pelo detalhe do pedido. Falhas com resultado desconhecido preservam a tentativa na página para repetição, sem solicitar nova movimentação real de dinheiro. Ao perder a página/sessão, consultar histórico antes de registrar outra tentativa. Falha na consulta posterior a uma gravação confirmada exige apenas atualizar o histórico; não reenvia a gravação.

### Contratos

`POST` recebe `{ "requestId": "UUID", "method": "Pix", "expectedOrderVersion": 1 }`. Não aceita valor, status, funcionário ou outros campos comerciais; dados extras são rejeitados. Retorna 201 com Location na criação ou 200 no reenvio idêntico. A chave é por funcionário, não deduplica chaves diferentes e o navegador a mantém somente enquanto a página existir.

`PUT .../receive` recebe `{ "expectedVersion": 1, "receivedConfirmed": true, "cashTendered": null }`. Para Cash, cashTendered é obrigatório, entre o valor do pedido e 9.999.999.999,99, com até duas casas decimais; demais métodos exigem null. Resposta traz amount, cashTendered e changeAmount calculados no backend. A confirmação é obrigatoriamente true.

`PUT .../cancel` recebe `{ "expectedVersion": 1, "reason": "Cliente mudou a forma de pagamento" }`. `PUT .../refund` recebe `{ "expectedVersion": 2, "reason": "Valor devolvido pelo meio de origem", "refundedConfirmed": true }`. Motivo é obrigatório, até 500 caracteres, com trim/NFC. Devolução envolve apenas o valor do pedido, preservando o histórico do dinheiro entregue/troco original.

`GET` da coleção recebe page (1–1.000.000) e pageSize (1–100, padrão 20), ordena tentativas por criação/UUID decrescentes e retorna orderId, orderNumber, orderStatus, orderVersion, orderTotal, receivedAmount, balance, activePayment, items e metadados de paginação. activePayment independe da página consultada. receivedAmount considera somente o pagamento atualmente Received; tentativas Refunded não compõem o valor líquido. Pedido cancelado tem saldo zero. Cada item possui histórico crescente por versão; chaves/hashes internos não são expostos.

Erros: PaymentNotFound/PaymentOrderNotFound (404), InvalidSession (401), PermissionDenied (403), PaymentInvalidCash e DTO inválido (400); PaymentOrderCancelled, PaymentOrderChanged, PaymentAlreadyActive, PaymentRequestConflict, PaymentVersionConflict e PaymentTransitionDenied (409). Pedido usa OrderPaymentUnresolved (409). As respostas exigem sessão e usam no-store.

### Persistência

Migration `20261001180855_AddManualPayments`: somente Payments, PaymentStatusHistory, índices, constraints e FKs. Índice único (CreatedById, RequestId); índice parcial único OrderId onde Status é Pending/Received. FK de pagamento para pedido/funcionário e de histórico para funcionário tem exclusão restrita. API não oferece exclusão física. Status/valor/dinheiro e transições de histórico possuem constraints, além das regras do serviço.

## Validação

Backend: **235/235 testes aprovados**, incluindo 21 novos casos de pagamentos, em PostgreSQL isolado. Navegador: **24 cenários novos de pagamentos** em desktop/celular e **37 cenários de regressão** de carrinho, pedidos e sessão no celular, validados entre execuções. Formatação, lint, builds, correspondência do modelo/migration e Test-Foundation aprovados. Migration revisada e aplicada exclusivamente em `made_in_minas` após backup; dados anteriores dos pedidos conferidos contra o backup e preservados. Resultados, repetições e limites em [validation.md](validation.md). Aceite manual antes de publicação ou próxima fase.

## Roteiro de aceite manual

1. Faça novo login em `http://localhost:8101/entrar` como administrador ou atendente. Abra um pedido Novo/Confirmado e clique em Pagamentos.
2. Defina Pix, crédito ou débito. Confira que fica Pendente e que não há registro automático de recebimento. Registre somente após conferir o recebimento real (para teste, use pedidos destinados à validação).
3. Em outro pedido, defina dinheiro. Em Registrar recebimento, informe o dinheiro entregue; valor menor que o pedido não libera confirmação. Após confirmar, confira valor do pedido, troco e histórico.
4. Cancele uma intenção Pendente com motivo e escolha outra forma. A tentativa cancelada permanece no histórico.
5. Tente cancelar um pedido com pagamento Pendente/Recebido: deve exigir resolver o pagamento. Para valor já recebido, entre como administrador e registre uma devolução integral já realizada, com motivo e confirmação explícita. Depois cancele o pedido, respeitando as permissões existentes.
6. Confira que atendente não oferece registro de devolução e cozinha/expedição não acessam pagamentos. Receber não altera o status do pedido nem estoque.
7. Em duas sessões, abra o mesmo pagamento pendente e tente decisões diferentes. A segunda ação deve solicitar atualização. Se houver falha incerta, use Repetir registro, sem movimentar o dinheiro novamente; ao perder a página/sessão, consulte o histórico antes de agir.

Pagamento dividido/parcial, cobrança online, conciliação, taxas, comprovantes, caixa e financeiro não fazem parte deste aceite. O usuário concluiu o aceite manual em 01/10/2026 e autorizou commit, publicação e integração à master pelo fluxo padrão de pull request, com checks aprovados antes do merge. O roteiro acima permanece como referência de verificação.

## Arquivos do incremento

- Backend: Models/Payment*, Data/Configurations/Payment*, DTOs/Payments, Services/PaymentService e PaymentException, Controllers/PaymentsController, Infrastructure/PaymentExceptionHandler, migration/snapshot; registros no Program/AppDbContext, políticas e bloqueio no OrderService.
- Testes: PaymentTests e atualização das permissões esperadas em AuthenticationTests; navegador em e2e/payments.spec.ts.
- Frontend: core/services/payment-api.service.ts, features/payments, rotas/guard/permissões, mensagens e acesso pelo detalhe do pedido.
- Documentação: README, payments, orders, modelo, API, decisões, regras, roteiro e validação.
