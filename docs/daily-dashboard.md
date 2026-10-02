# Dashboard do dia — Fase 7A

Primeiro incremento da Fase 7, disponível para administrador em **Área da equipe → Dashboard** (`/equipe/dashboard`). Consulta pedidos, pagamentos e históricos existentes. Não cria tabelas, migrations, dependências ou lançamentos financeiros.

Incremento desenvolvido na branch `feat/daily-dashboard` e integrado pelo [PR #11](https://github.com/Pauliinrodrigues/MadeinMinasApp/pull/11), commit `23b2c38`, após as Fases 6B/6C. Publicação e integração autorizadas pelo responsável em 02/10/2026, com checks backend/frontend aprovados.

## Definições dos indicadores

O backend determina o dia civil em `America/Sao_Paulo`, independentemente do fuso do servidor ou navegador. Intervalo: início inclusivo às 00h, fim exclusivo às 00h do dia seguinte. Não é um fechamento por turno da hamburgueria.

| Indicador | Critério |
| --- | --- |
| Criados hoje | `Orders.CreatedAt` dentro do dia, em qualquer status |
| Criados hoje e cancelados | Subconjunto dos criados hoje cujo status atual é Cancelled |
| Confirmados hoje | Evento Confirmed dentro do dia e status atual diferente de Cancelled; pode incluir pedido criado antes de hoje |
| Valor confirmado hoje | Soma de `Orders.Total` dos confirmados hoje, incluindo taxa de entrega |
| Ticket médio confirmado | Valor confirmado dividido pela quantidade de confirmações válidas; null se não houver pedidos |
| Recebido hoje | Soma de `Payments.Amount` dos eventos Received dentro do dia |
| Estornado hoje | Soma de `Payments.Amount` dos eventos Refunded dentro do dia |
| Recebido menos estornos | Recebido menos estornado; pode ser negativo |
| Filas da operação agora | Contagem atual de New, Confirmed, InPreparation, Ready, AwaitingDelivery, OutForDelivery e Delivered, sem filtro de data |
| Tempo médio de produção | Média de InPreparation → Ready para eventos Ready de hoje com início válido anterior ou igual; null se não houver pares válidos |
| Pedidos na média | Quantidade de pares de horários válidos usados na média |
| Produtos mais pedidos | Até cinco produtos, somando quantidades e `OrderItems.LineTotal` nas confirmações válidas de hoje |

Os recebimentos usam a data de cada evento, sem filtrar pelo status atual do pagamento ou do pedido. Receber e estornar hoje registra as duas parcelas. Receber ontem e estornar hoje registra somente o estorno hoje. Intenções Pending/Cancelled não entram; dinheiro entregue para troco não é receita. Os eventos são registros manuais conferidos pela equipe, sem consulta a gateway ou conciliação bancária.

Valor confirmado não comprova recebimento. Recebido menos estornos não é lucro: despesas, taxas e CMV realizado não são calculados nesta etapa. O [CMV teórico por produto](cmv.md) permanece uma consulta separada; não aplicar custos atuais sobre vendas históricas para inventar margem realizada.

Produção pode atravessar a meia-noite. Trabalho que chegou a Ready hoje continua na média mesmo após cancelamento, pois foi executado. Pedido ainda em preparo não entra na média; histórico sem início válido é excluído e não recebe duração zero fictícia.

O ranking agrupa pelo ID do produto, reunindo linhas com observações ou nomes históricos diferentes. Exibe o nome copiado no pedido criado mais recentemente (desempate por número, depois posição do item); não usa nome, preço ou disponibilidade atuais do catálogo. Ordenação: quantidade decrescente, valor dos itens decrescente e ID crescente. Valores dos itens não incluem entrega. Desativar/renomear o produto não apaga vendas anteriores.

Valores são calculados em decimal no backend. Ticket e média em minutos são arredondados a duas casas, com meio afastado de zero. Médias ausentes são null; contagens e somas vazias são zero.

## API e implementação

`GET /api/dashboard/today`

- Exige JWT e `dashboard.view`, atribuída inicialmente somente a Administrator. Perfil ativo e sessão continuam revalidados pelo mecanismo existente.
- 200: `DailyDashboardResponse`; 401 sem sessão válida; 403 sem permissão. Apenas GET; resposta `Cache-Control: no-store`.
- `date`: data civil ISO; `timeZone`: identificador IANA; `startsAt`/`endsAt`: limites UTC; `calculatedAt`: instante de referência da consulta.
- `orders`: created, createdAndCancelled, confirmed, confirmedValue, averageTicket.
- `receipts`: received, refunded, netReceived.
- `queues`: lista de status/count, incluindo filas vazias.
- `production`: completed, averageMinutes.
- `topProducts`: productId, productName, quantity, itemValue.

As seis consultas compartilham transação RepeatableRead e usam AsNoTracking. Contagens, somas, agrupamentos e limite do ranking são executados no PostgreSQL; para a média, somente os pares de horários do dia são projetados e calculados em memória. Não são carregados clientes, endereços ou pedidos completos para o painel. Nenhum dado pessoal de cliente é enviado.

O frontend carrega ao abrir e permite atualização manual. Exibe data e horário de referência em Brasília. Ao atualizar, remove os valores anteriores, bloqueia repetição e cancela a requisição ao sair da página. Timeout de 15 segundos; falha mostra mensagem e permite nova tentativa, sem apresentar zeros artificiais. O menu e a rota usam a mesma permissão.

## Testar e validar

1. Inicie API e frontend conforme o README. Faça novo login como administrador para receber `dashboard.view`.
2. Abra **Dashboard** e confira o dia e horário de Brasília, inclusive em dispositivo configurado para outro fuso.
3. Compare **Criados hoje** com os pedidos do dia; compare confirmações usando o histórico. Pedidos ainda Novos não compõem valor/ticket/ranking.
4. Em um ambiente de teste, confirme pedidos com taxa e sem taxa. Confira os totais e o ranking por quantidade. Confirmação não deve aumentar os recebimentos antes do registro de pagamento.
5. Registre recebimento conferido e, quando aplicável, devolução integral. Atualize o painel e confira recebido, estornado e líquido separadamente. Em dinheiro, o troco não aumenta o recebido.
6. Avance um pedido pela cozinha até Pronto. Confira a fila e a média pelo histórico. Pedidos pendentes de dias anteriores permanecem nas filas.
7. Teste atualização, indisponibilidade da API e recuperação. Confirme que indicadores antigos desaparecem durante consulta/falha.
8. Entre como atendente, cozinha ou expedição: o menu não deve mostrar Dashboard, a navegação direta deve retornar à conta e a API deve negar acesso.

Testes automatizados do backend usam exclusivamente PostgreSQL temporário em 127.0.0.1:55433. Os testes de navegador usam respostas simuladas em desktop e mobile; não gravam vendas reais.

```powershell
# Na raiz: regressão backend em banco isolado
powershell -NoProfile -File scripts/Test-Authentication.ps1

# No diretório frontend/made-in-minas
npm.cmd run format:check
npm.cmd run lint
npm.cmd run build
npm.cmd run test:e2e -- dashboard.spec.ts staff.spec.ts
```

Resultados executados estão em [validation.md](validation.md). Aceite manual da operação é separado dos testes automatizados.

## Arquivos do incremento

- Novos: `backend/MadeInMinas.Api/Controllers/DashboardController.cs`, `Services/DashboardService.cs`, `DTOs/Dashboard/DailyDashboardResponse.cs` e `backend/MadeInMinas.Api.Tests/DashboardTests.cs`.
- Backend alterado: `Program.cs` (registro do serviço) e `Security/AccessPolicies.cs` (permissão).
- Novos: `frontend/made-in-minas/src/app/core/services/dashboard-api.service.ts`, `features/dashboard/dashboard.page.ts`, `.html` e `.scss`, e `frontend/made-in-minas/e2e/dashboard.spec.ts`.
- Frontend alterado: `app.routes.ts`, `core/auth/auth.guards.ts`, `core/auth/auth-session.service.ts` e `features/staff/staff-layout.page.ts`.
- Documentação: este documento, README, roadmap, arquitetura, API, banco, regras de negócio, decisões, validação e referência de continuidade no CMV.

## Próximo incremento

A [7B — relatórios por período](period-reports.md) acrescenta filtros e regras de datas explícitas no [PR #12](https://github.com/Pauliinrodrigues/MadeinMinasApp/pull/12), com publicação e integração autorizadas. Fechamento por turno, exportação, CMV realizado/margem histórica, despesas e conciliação exigem definições próprias; não são simulados pelo dashboard diário.
