# Estoque manual — Fase 6A

Incremento na branch `feat/ingredient-stock`, iniciado em 02/10/2026. Implementação, testes automatizados e aceite manual concluídos em 02/10/2026. Integrada pelo PR #8 (`71f7c41`), após aceite e checks aprovados. A [Fase 6B](order-stock.md) acrescenta consumo por pedidos. Acesso em **Ingredientes → Estoque**, somente administrador (`catalog.manage`). Sem novas dependências.

Situação local em 02/10/2026: após a conclusão da instalação PostgreSQL, o acesso ao `made_in_minas` foi confirmado e a migration foi aplicada exclusivamente nesse banco, com backup prévio e preservação das contagens existentes. API em 5080 e frontend em 8101 disponíveis, com readiness saudável. O usuário concluiu o aceite pela interface; o inventário real deve ser registrado conforme a contagem física. Evidências em [validation.md](validation.md).

## Operação

- Entrada soma uma quantidade; saída desconta; contagem física informa o saldo absoluto conferido. Cada operação exige revisão, confirmação e motivo de até 500 caracteres.
- Quantidades na unidade-base fixa do ingrediente (`kg`, `l`, `un`), entre 0 e 999999,999, com até três casas decimais. Entrada/saída devem ser maiores que zero. Zero é permitido na contagem; uma contagem igual ao saldo não gera movimento.
- Não há saldo negativo. A saída não pode ultrapassar o saldo. Ingredientes inativos permitem consulta, mas exigem reativação para novos movimentos.
- Saldo igual ou inferior ao mínimo gera um aviso na tela do ingrediente. Não há ainda painel consolidado, notificações ou bloqueio automático do cardápio.
- Ingredientes existentes começam com saldo de sistema zero e sem movimentos. Isso não representa uma contagem física. Faça o inventário e registre a contagem inicial com motivo “Saldo inicial”. Uma contagem zero sobre saldo zero não gera comprovante de inventário nesta etapa.
- Histórico mostra tipo, data/hora, nome do ingrediente e responsável copiados no lançamento, saldo anterior/novo, diferença e motivo. Horários são armazenados em UTC e exibidos no fuso do dispositivo.
- Movimentos não são editados ou apagados pela API. Para corrigir, registre nova entrada, saída ou contagem com justificativa. Não há vínculo formal de estorno neste incremento.
- O custo unitário continua sendo informado no cadastro. Entradas não recalculam custo médio nem geram despesas financeiras. A Fase 6B consome estoque na confirmação; não duplique esse consumo com saída manual. Consumos manuais anteriores exigem conciliação antes da ativação; nenhum pedido antigo é baixado retroativamente.

## Consistência e API

| Método | Rota | Resultado |
| --- | --- | --- |
| GET | `/api/ingredients/{ingredientId}/stock?page=1&pageSize=20` | Saldo, mínimo, ativo, aviso, versão e histórico paginado |
| POST | `/api/ingredients/{ingredientId}/stock/movements` | Movimento confirmado, HTTP 200, inclusive repetição idempotente |

Todos exigem JWT e `catalog.manage`, com `Cache-Control: no-store`. Page: 1–1000000; pageSize: 1–100. Histórico em ordem decrescente de versão; total e saldo pertencem ao mesmo snapshot RepeatableRead. A navegação de páginas reflete a situação atual em cada consulta.

Exemplo de corpo para entrada (gere um UUID novo para cada intenção):

```json
{
  "requestId": "c2c84775-81ef-45dd-a003-43b55d9f21f0",
  "expectedVersion": 0,
  "type": "Entry",
  "quantity": 10.125,
  "reason": "Compra de carne"
}
```

Tipos: `Entry`, `Exit`, `Count`. Campos desconhecidos, ausência, precisão excedente ou formato inválido retornam 400 antes da escrita. Saldo e autor não são aceitos do cliente.

`StockService` usa a transação administrativa existente: bloqueio compartilhado do usuário, revalidação de ativo/SecurityStamp/perfil e bloqueio exclusivo do ingrediente. Grava saldo, versão e movimento atomicamente. Edição de cadastro usa o mesmo bloqueio e preserva o saldo; movimentar estoque não altera o timestamp comercial do cadastro.

`RequestId` é único por ingrediente. Repetição com mesmo autor, tipo, quantidade, motivo aparado e versão original retorna o movimento já registrado, mesmo após outros movimentos ou inativação. Reuso divergente retorna `StockRequestConflict` (409). Chaves são preservadas no histórico.

Novas intenções exigem a versão atual. Concorrência retorna `StockVersionConflict` (409); é necessário consultar novamente e revisar. Outros conflitos 409: `InactiveIngredient`, `InsufficientStock`, `StockLimitExceeded`, `StockUnchanged`. Ingrediente ausente: 404. Sessão inválida: 401; papel não autorizado: 403.

A tela conserva o comando em memória após falha de rede/servidor e oferece repetir o mesmo lançamento. Enquanto o resultado estiver incerto, formulário, atualização e cancelamento ficam bloqueados. Se fechar a tela ou perder a sessão, confira o histórico antes de lançar novamente. Após conflito ou falha de leitura, atualizar é obrigatório antes de nova revisão.

## Persistência

Migration `20261002130808_AddIngredientStock` adiciona `Ingredients.CurrentStock numeric(9,3)` e `StockVersion bigint`, com zero inicial, além de `StockMovements`. FKs de ingrediente e funcionário usam Restrict. Índices únicos: ingrediente/requestId e ingrediente/versão. Constraints reforçam limites, tipos, unidade, diferença não zero e equação `Balance = PreviousBalance + Delta`.

O saldo é mantido pelo serviço em conjunto com o histórico, sem trigger. Não alterar diretamente saldo/movimentos por SQL operacional. A aplicação não executa migrations automaticamente. Aplicação em outra máquina: conferir conexão para `made_in_minas`, fazer backup e seguir [database/README.md](../database/README.md). O Down elimina o histórico e o saldo: não executar após uso operacional sem plano de recuperação e autorização específica.

## Validar

1. Entre como administrador, abra **Ingredientes → Estoque** de um ingrediente ativo.
2. Confira a unidade. Faça contagem inicial; confira novo saldo, motivo e responsável no histórico.
3. Registre entrada e saída. Tente saída maior que o saldo e quantidade com quatro casas decimais: devem ser bloqueadas.
4. Faça uma contagem menor que o saldo; confira a diferença negativa. Abaixo/no mínimo, confira o aviso.
5. Abra o mesmo ingrediente em duas abas autenticadas. Grave em uma; a revisão antiga na outra deve exigir atualização, sem duplicar movimentos.
6. Inative o ingrediente no cadastro. Histórico continua acessível; novos movimentos ficam bloqueados.
7. Atendente, cozinha e expedição não devem acessar a rota administrativa de estoque.

Testes automatizados: `StockTests` em PostgreSQL isolado e `e2e/stock.spec.ts` em desktop/mobile com API simulada. Resultados da execução e limitações em [validation.md](validation.md).

## Arquivos do incremento

- Backend: `Controllers/StockController.cs`, `Services/StockService.cs`, `Services/StockException.cs`, `Infrastructure/StockExceptionHandler.cs`, `DTOs/Stock/StockContracts.cs`, `Models/StockMovement.cs`, `Data/Configurations/StockMovementConfiguration.cs`.
- Integração EF/API: `Models/Ingredient.cs`, `Data/Configurations/IngredientConfiguration.cs`, `Data/AppDbContext.cs`, `Program.cs`, migration `AddIngredientStock` e snapshot.
- Frontend: `core/services/stock-api.service.ts`, `features/stock/stock.page.ts` e `.html`, `app.routes.ts`, `core/api-error.ts` e link em `features/catalog/ingredients.page.html`.
- Testes: `backend/MadeInMinas.Api.Tests/StockTests.cs` e `frontend/made-in-minas/e2e/stock.spec.ts`.
- Documentação: README, roadmap, arquitetura, banco, API, decisões, regras, ingredientes, validação, este roteiro e `database/README.md`.

O incremento [6B](order-stock.md) define consumo/compensação por pedidos, composição histórica e arredondamento por rendimento. CMV teórico por ficha técnica será o próximo incremento, após seu aceite.
