# Relatórios por período — Fase 7B

Consulta administrativa em **Área da equipe → Relatórios**, rota `/equipe/relatorios`. Apresenta resumo do período, detalhamento diário, recebimentos/estornos por forma de pagamento e até dez produtos mais pedidos. Usa os pedidos, itens e históricos existentes; não cria tabelas, migrations ou lançamentos.

Incremento na branch `feat/period-reports`, publicado no [PR #12](https://github.com/Pauliinrodrigues/MadeinMinasApp/pull/12) após a integração de estoque, CMV e dashboard pelos PRs #9/#10/#11. Publicação e integração autorizadas pelo responsável em 02/10/2026; o merge exige os checks backend/frontend aprovados para o commit final. A atualização da base preservou o conteúdo testado localmente.

## Período e regras

- Data inicial e final inclusivas, em `America/Sao_Paulo`, com 1 a 90 dias por consulta. Sem filtros, retorna os últimos sete dias incluindo hoje, determinados pelo relógio do backend.
- Informar as duas datas ou omitir ambas. Ordem invertida, período acima de 90 dias, data inválida ou data final 9999-12-31 retornam 400. Esse último limite permite calcular o início do dia seguinte sem exceder o calendário.
- A seleção é convertida em limites UTC: início inclusivo e começo do dia seguinte ao final exclusivo. Os agrupamentos também usam Brasília, independentemente do fuso do servidor, conexão PostgreSQL ou navegador.
- Datas futuras são aceitas dentro do limite de 90 dias; não representam previsão e, sem registros, aparecem vazias. Dias sem movimento sempre são incluídos com contagens/somas zero e ticket null.
- Para datas históricas, o primeiro horário local existente define o início do dia; em horário ambíguo, é usado o primeiro instante. Isso contempla o início do horário de verão brasileiro que suprimiu a meia-noite em 04/11/2018.

As regras comerciais são as mesmas do [dashboard](daily-dashboard.md):

| Indicador | Critério |
| --- | --- |
| Criados | Data de criação dentro do período/dia, qualquer status |
| Criados e cancelados | Pedidos criados nesse período/dia cujo status atual é Cancelled; não é uma contagem pela data do cancelamento |
| Confirmados | Histórico de confirmação dentro do período/dia, excluindo pedidos atualmente cancelados |
| Valor confirmado | Total desses pedidos, incluindo taxa de entrega |
| Ticket médio | Total confirmado dividido pela quantidade de confirmações válidas; null sem confirmações |
| Recebido | Eventos Received dentro do período/dia, pelo valor do pagamento, sem troco |
| Estornado | Eventos Refunded dentro do período/dia, independentemente da data do recebimento |
| Recebido menos estornos | Diferença entre os dois valores, podendo ser negativa |

O resumo soma os valores diários e calcula o ticket pela quantidade total de pedidos, sem tirar a média dos tickets de cada dia. Todos os cálculos monetários usam decimal no backend; ticket com duas casas e meio afastado de zero. Alterar filtros não recalcula números comerciais no Angular.

O relatório mostra o estado atual dos registros: um cancelamento posterior remove uma confirmação de um período passado. Não é um fechamento contábil imutável. Recebimentos e estornos preservam a data dos eventos, mesmo quando o status atual do pagamento mudou. Intenções Pending/Cancelled não são recebimentos.

Formas de pagamento: dinheiro, Pix, crédito e débito. Todas aparecem, inclusive sem movimento. Os valores por forma e por dia fecham com o resumo. Não há conciliação com banco/adquirente; são registros manuais conferidos pela equipe.

O ranking agrupa por ID de produto, somando quantidades de todas as linhas e valores históricos dos itens. Desempata por valor decrescente e ID crescente. O nome exibido vem do pedido criado mais recentemente, com desempate por número e posição. Preço/nome/disponibilidade atuais do catálogo não alteram o resultado. Entrega não entra no valor dos produtos. O ranking mostra somente os dez primeiros; sua soma não é o total de todos os itens vendidos quando há mais produtos.

## Contrato e arquitetura

`GET /api/reports/sales?startDate=2026-09-01&endDate=2026-09-30`

Datas em formato ISO `yyyy-MM-dd`. Omitir ambos os parâmetros usa os últimos sete dias.

- Permissão `reports.view`, inicialmente exclusiva de Administrator. JWT e revalidação de sessão pelo mecanismo existente. 401 sem sessão; 403 sem permissão.
- Somente GET, com `Cache-Control: no-store`. Entradas inválidas retornam ValidationProblemDetails, com errors, sem executar o relatório.
- Resposta: startDate, endDate, timeZone, startsAt, endsAt, calculatedAt, summary, days, paymentMethods e topProducts.
- `summary`: created, createdAndCancelled, confirmed, confirmedValue, averageTicket, received, refunded, netReceived.
- `days`: date e metrics com os mesmos campos de summary, em ordem crescente, incluindo dias vazios.
- `paymentMethods`: method, received, refunded, netReceived, para as quatro formas existentes.
- `topProducts`: productId, productName, quantity, itemValue, em ordem do ranking.

SalesReportService executa quatro consultas em RepeatableRead/AsNoTracking: criações por dia, confirmações por dia, eventos de pagamento por dia/forma e ranking limitado a dez. Agrupamentos e somas são feitos no PostgreSQL; somente os agregados são trazidos para montar os dias vazios e o resumo. Nenhum cliente, endereço ou pedido completo é enviado ao navegador.

A conversão explícita de fuso nas consultas usa a tradução de TimeZoneInfo.ConvertTimeBySystemTimeZoneId para AT TIME ZONE, documentada pelo [Npgsql](https://www.npgsql.org/efcore/mapping/translations.html#date-and-time-functions). Os testes conferem agrupamento com uma conexão configurada em Pacific/Auckland e também uma data histórica com horário de verão. Nenhuma dependência nova foi adicionada.

O dashboard mantém seu contrato e suas filas atuais; relatórios não simulam filas históricas. Um teste de integração compara um relatório de um dia com os indicadores comerciais do dashboard. Não foi introduzida uma camada genérica de consultas/repositórios.

O frontend começa com sete dias retornados pelo backend. Ao editar datas, oculta o resultado anterior e solicita gerar novamente; durante a consulta, bloqueia campos e botões. Timeout de 15 segundos, cancelamento ao sair da página e recuperação de falhas sem valores antigos. Tabelas possuem rolagem horizontal acessível em telas pequenas.

## Validar manualmente

1. Inicie API/frontend conforme o README. Faça novo login como administrador e abra **Relatórios** para receber a permissão nova.
2. Confira os últimos sete dias e a indicação do horário de Brasília. Teste também em navegador com outro fuso.
3. Selecione um único dia (inicial = final) e gere. Para hoje, compare resumo e primeiros produtos com o dashboard, sem novas movimentações entre as consultas.
4. Consulte um intervalo com dias de valores/quantidades diferentes. Confira o resumo pela soma diária e o ticket pela quantidade de pedidos; dias vazios devem permanecer.
5. Confira registros existentes de recebimento e devolução, incluindo eventos em datas diferentes. Verifique os totais por forma e por dia; troco não entra no recebido. Use ambiente de teste para simular novas movimentações.
6. Altere datas e confirme que o resultado antigo desaparece. Teste início sem fim, datas invertidas, 90 dias válidos e 91 dias rejeitados.
7. Desligue apenas a API de desenvolvimento, consulte e confira a mensagem de erro; reinicie e gere novamente. Na primeira carga com falha, o botão **Últimos 7 dias** permite repetir o período padrão.
8. Atendente, cozinha e expedição não devem ver Relatórios nem acessar a rota interna/API. Sessão revogada deve remover os resultados.

## Testes e arquivos

```powershell
# Na raiz, banco temporário isolado em 127.0.0.1:55433
powershell -NoProfile -File scripts/Test-Authentication.ps1

# No diretório frontend/made-in-minas
npm.cmd run format:check
npm.cmd run lint
npm.cmd run build
npm.cmd run test:e2e -- reports.spec.ts dashboard.spec.ts staff.spec.ts
```

Os testes de navegador usam API simulada em desktop/mobile; o aceite com dados da operação continua separado. Resultados executados estão em [validation.md](validation.md).

- Novos no backend: `Controllers/ReportsController.cs`, `Services/SalesReportService.cs`, `DTOs/Reports/SalesReportContracts.cs`, sob `backend/MadeInMinas.Api`, e `backend/MadeInMinas.Api.Tests/SalesReportTests.cs`.
- Backend alterado: `Program.cs` e `Security/AccessPolicies.cs`.
- Novos no frontend: `src/app/core/services/reports-api.service.ts`, `src/app/features/reports/sales-report.page.ts`, `.html`, `.scss` e `e2e/reports.spec.ts`, sob `frontend/made-in-minas`.
- Frontend alterado: `app.routes.ts`, `core/auth/auth-session.service.ts`, `core/auth/auth.guards.ts` e `features/staff/staff-layout.page.ts`.
- Documentação: este documento, README, roadmap, arquitetura, API, banco, regras de negócio, decisões, validação e referência de continuidade no dashboard.

## Limites e continuidade

Este incremento entrega os relatórios iniciais de vendas e recebimentos. Exportação, fechamento por turno, despesas, taxas, conciliação e CMV/margem realizados exigem incrementos próprios. O CMV teórico continua disponível por produto e não é aplicado retroativamente às vendas. Após validar este incremento, a próxima fase do roteiro é **8 — chat próprio**.
