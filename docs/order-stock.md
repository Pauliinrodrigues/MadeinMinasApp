# Consumo de estoque por pedidos — Fase 6B

Incremento na branch `feat/order-stock`, após a integração do estoque manual pelo PR #8 (`71f7c41`). A implementação reutiliza a confirmação/cancelamento do pedido e o histórico de estoque. CMV permanece no próximo incremento. Aceite manual desta etapa ainda pendente.

## Operação

- Registrar um pedido Novo não reserva nem baixa ingredientes. Confirmar exige ficha técnica para todos os produtos, ingredientes ativos e saldo suficiente. Produtos de revenda podem usar ficha de rendimento 1 e quantidade 1 do ingrediente correspondente.
- A composição vigente no instante da confirmação é copiada para o pedido. O preço continua sendo o registrado na compra. Alterar uma ficha depois não muda o consumo histórico.
- Linhas do mesmo produto são somadas, incluindo linhas com observações diferentes. Para cada produto/ingrediente, calcular `quantidade da ficha × unidades do produto / rendimento`, arredondando para cima a três casas decimais **uma vez por produto/ingrediente**. Somar depois as contribuições de produtos diferentes que usam o mesmo ingrediente. Exemplo: ficha com 0,500 kg para 3 lanches, pedido de 2 → 0,334 kg.
- A precisão mínima é 0,001 na unidade-base, inclusive `un`. Não arredondar por unidade vendida. Há arredondamento conservador de menos de 0,001 por produto/ingrediente; fichas com quantidades muito pequenas devem considerar essa resolução. Não há conversão automática de unidades.
- Se faltar qualquer saldo, ficha ou ingrediente ativo, a confirmação inteira falha, sem alterar pedido, histórico, componentes ou qualquer ingrediente.
- Administrador e atendente podem confirmar com `orders.manage`. Isso não concede ao atendente acesso ao estoque manual, custos ou saldos gerais. Movimentos automáticos registram o funcionário responsável pela transição.
- Cancelar Novo não movimenta estoque. Cancelar Confirmado, antes do preparo, devolve as quantidades **efetivamente baixadas**, mesmo se a ficha mudou ou o ingrediente foi inativado. Depois de iniciar o preparo, cancelamento mantém o consumo. Essa regra foi adotada como padrão conservador e faz parte do aceite manual desta etapa.
- Cancelamento após confirmação continua exclusivo do administrador e exige resolver pagamentos Pending/Received antes. Se uma devolução ultrapassar 999999,999, o cancelamento inteiro é bloqueado até a conferência do saldo.
- Observações livres, como “sem cebola”, não alteram ingredientes. Ajustes reais exigem conferência e movimento manual justificado. Combos, adicionais, perdas, produção por lote, compensação após devolução física e custo médio têm escopo próprio.
- Repetir a última transição idêntica pelo mesmo funcionário não duplica baixa/devolução. Depois de outra transição, atualizar o pedido; não tentar forçar a versão anterior.

## Histórico e interface

O detalhe do pedido apresenta `stockStatus` e a composição histórica, com nome do produto, quantidade vendida, ingrediente/unidade, quantidade/rendimento da ficha e quantidade baixada. Não mostra custos. O histórico do ingrediente tem `orderId` e link para o pedido, além dos dados de auditoria já existentes.

| Estado do estoque | Significado |
| --- | --- |
| Pending | Pedido Novo, baixa ainda não executada |
| Consumed | Baixa concluída; permanece assim durante produção, entrega e finalização |
| Returned | Cancelamento anterior ao preparo devolveu a baixa |
| Retained | Cancelamento após início do preparo manteve o consumo |
| NotRequired | Cancelado ainda Novo, sem consumo |
| Legacy | Pedido anterior à migração que já não estava Novo; sem baixa/devolução retroativa |

Pedidos antigos que ainda estejam Novos são confirmados pelas novas regras. Os demais mantêm a operação normal de cozinha/expedição e não são movimentados retroativamente. O inventário inicial e eventuais consumos manuais anteriores precisam estar conciliados pela equipe antes da ativação.

## Persistência e concorrência

Migration `20261002181034_AddOrderStock`:

- `Orders.StockStatus varchar(12)`; backfill Pending para Novos e Legacy para os demais, sem alterar saldos ou status comerciais.
- `OrderStockComponents`, chave composta OrderId/ProductId/IngredientId, guarda as cópias históricas e quantidades numeric(9,3). FKs de produto/ingrediente restritas; composição pertence ao pedido.
- `StockMovements.OrderId` opcional, FK restrita, e índice único filtrado OrderId/IngredientId/Type. Cada pedido tem no máximo uma saída e uma entrada por ingrediente. Movimentos manuais continuam com OrderId nulo e não podem enviar esse campo pela API.

`OrderStockService` participa da transação de `OrderService`, sem salvar ou confirmar isoladamente. A ordem de bloqueios é funcionário, pedido, clientes/endereço/produtos/categorias na confirmação e ingredientes ordenados por UUID. O bloqueio compartilhado do produto impede edição da ficha durante o cálculo; o exclusivo do ingrediente coordena baixas, ajustes manuais e devoluções. O bloqueio do pedido coordena cozinha, pagamentos e cancelamento.

Detalhes são lidos com RepeatableRead e consultas separadas para as coleções, evitando multiplicação de linhas entre itens, composição e histórico. Reenvio de criação bloqueia o pedido existente antes de ler essas coleções. Não há nova dependência, repository, endpoint de baixa ou configuração de IA.

## API

Os contratos de entrada existentes permanecem iguais. `OrderResponse` ganha `stockStatus` e `stockComponents`; `StockMovementResponse` ganha `orderId` opcional. KDS, expedição e impressão mantêm suas projeções restritas.

Novos conflitos HTTP 409: `OrderRecipeRequired`, `OrderIngredientInactive`, `OrderInsufficientStock`, `OrderStockQuantityExceeded` e `OrderStockReturnOverflow`. O frontend explica a correção e exige atualizar o pedido antes de nova ação.

## Aplicar e validar

Confira a conexão para `made_in_minas`, faça backup e aplique a migration conforme [database/README.md](../database/README.md). A API não migra automaticamente. Não use Down após baixas operacionais: ele remove o vínculo/composição e a proteção contra processamento retroativo, sem desfazer os saldos. Reverter exigiria restauração/recuperação planejada.

1. Como administrador, confira fichas e inventário físico; escolha um produto e anote os saldos dos ingredientes.
2. Registre um pedido Novo. Confira que os saldos não mudaram. Confirme e compare consumo, composição no detalhe e movimentos vinculados no ingrediente.
3. Com dois pedidos disputando saldo insuficiente para ambos, confirme um e tente o outro: o segundo deve continuar Novo, sem baixa parcial.
4. Cancele um pedido Confirmado antes do preparo, com pagamentos resolvidos. Os ingredientes devem voltar exatamente ao saldo anterior, preservando o histórico.
5. Confirme outro pedido, inicie o preparo no KDS e cancele como administrador. O consumo deve permanecer, com o aviso correspondente.
6. Altere a ficha após confirmar e confira que a composição histórica não muda. Um pedido antigo já processado deve aparecer como Legacy e não devolver ingredientes ao cancelar.
7. Como atendente, confirme com ficha/saldo válidos; confira que o ajuste manual e o cancelamento após confirmação continuam restritos.

Resultados automatizados e situação do banco local: [validation.md](validation.md). Os testes de navegador simulam a API; o aceite acima valida a integração real.

## Arquivos

- Backend: OrderStockService, OrderStockComponent/configuração, migration/Designer/snapshot; extensões em OrderService, Order/StockMovement/configurações, AppDbContext, DTOs, OrderException e Program.
- Frontend: detalhe de pedido, histórico de estoque, contratos e mensagens de erro.
- Testes: OrderStockTests/OrderStockFixture; preparação e limpeza de OrderTests, PaymentTests, KitchenTests e DispatchTests; cenários de navegador de pedidos/estoque/pagamentos.
- Documentação: este roteiro, README, roadmap, estoque, fichas, pedidos, arquitetura, banco, API, decisões, regras e validação.
