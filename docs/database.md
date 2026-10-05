# Modelo conceitual inicial

Modelagem incremental. InitialAccessControl implementa Users e Roles; AddCategories implementa Categories; AddProducts implementa Products com FK restrita para Categories; AddIngredients implementa Ingredients; AddRecipes implementa Recipes e RecipeItems; AddCustomersAndAddresses implementa Customers e Addresses. As demais entidades continuam propostas.

A Fase 4B (carrinho/revisão) consulta esse modelo sem criar tabelas ou migrations. Carrinhos não são persistidos. A Fase 4C acrescenta Orders, OrderItems e OrderStatusHistory pela migration `20261001142005_AddManualOrders`. A Fase 4D acrescenta Payments e PaymentStatusHistory por `20261001180855_AddManualPayments`. Regras e tipos: [orders.md](orders.md) e [payments.md](payments.md).

| Entidade | Dados e relações previstos | Fase |
| --- | --- | --- |
| Roles | Código único, nome | 2 |
| Users | Login único, nome, hash de senha, ativo, RoleId | 2 |
| Categories | Nome único normalizado, descrição opcional, ordem, ativo, criação/atualização UTC | 3A implementada |
| Products | Categoria obrigatória, nome único por categoria, descrição, preço numeric(8,2), URL de imagem, ativo, disponibilidade manual, datas UTC | 3B implementada |
| Ingredients | Nome único normalizado, unidade-base fixa (kg/l/un), custo numeric(10,4), mínimo e saldo numeric(9,3), versão de estoque, fornecedor textual opcional, ativo e datas UTC | Cadastro 3C; estoque 6A |
| StockMovements | Ingrediente/autor com FK restrita, cópias de nomes/unidade, RequestId e versão únicos por ingrediente, tipo, quantidade, diferença, saldos, motivo e instante UTC | 6A |
| Recipes | Produto único com FK restrita, rendimento inteiro (1–10000), instruções opcionais, datas UTC | 3D implementada |
| RecipeItems | PK composta receita/ingrediente, quantidade numeric(9,3), posição; FK restrita de ingrediente e cascade de receita para seus itens | 3D implementada |
| Combos | Nome, descrição, preço e disponibilidade próprios | Catálogo, incremento específico |
| ComboItems | Combo, produto e quantidade | Catálogo, incremento específico |
| Customers | Nome, nome normalizado, telefone brasileiro único com +55, ativo e datas UTC | 4A implementada |
| Addresses | Cliente com FK restrita; rua, número textual, bairro, cidade, UF; complemento/CEP/referência opcionais, ativo e datas UTC | 4A implementada |
| Orders | Identity única, cliente/funcionário com FK restrita, chave idempotente por funcionário, origem Manual, status/versão, totais, cópias de cliente/endereço, datas UTC | 4C implementada e validada |
| OrderItems | Pedido, produto com FK restrita, posição única no pedido, quantidade, nome/preço da compra, observações | 4C implementada e validada |
| Payments | Pedido/funcionário com FK restrita, chave de tentativa por funcionário, método, valor numeric(12,2), status/versão, dinheiro entregue e datas UTC; no máximo um Pending/Received por pedido | 4D implementada e validada |
| PaymentStatusHistory | Pagamento, versão única, status anterior/novo, funcionário com FK restrita e nome copiado, motivo, instante UTC | 4D implementada e validada |
| OrderStatusHistory | Pedido, versão única por pedido, status anterior/novo, instante UTC, funcionário com FK restrita e nome copiado, motivo | 4C implementada e validada |

## Diretrizes

A Fase 5A usa a migration `20261001191445_AddKitchenStatuses` para ampliar apenas CK_Orders_Status e CK_OrderStatusHistory_Transition. Não cria tabelas, colunas ou dados de produção. Instantes de confirmação/preparo/conclusão são derivados dos eventos existentes. O retorno às constraints antigas falha se houver estados/eventos novos; não modificar histórico para forçar rollback. Veja [kitchen.md](kitchen.md).

- UUID para identificadores técnicos; número de pedido separado, único e gerado atomicamente.
- Dinheiro em decimal/numeric e quantidades de ingredientes com precisão explícita.
- Instantes em UTC (timestamptz); exibição no fuso America/Sao_Paulo.
- Inicialmente um perfil por usuário; políticas permitem ampliar as permissões.
- Telefone normalizado para busca e deduplicação; conhecer um telefone não autentica um cliente.
- Receita define consumo na unidade-base do ingrediente. Embalagens podem ser ingredientes.
- OrderItems referencia produto nesta fase; vínculo com combo e cópia da composição serão definidos no incremento de combos.
- Adicionais requerem catálogo, associação de opções permitidas e cópia das escolhas no pedido. A modelagem detalhada será feita no incremento de catálogo.
- Alterações do cardápio ou endereço não reescrevem os valores históricos do pedido.
- OrderStatusHistory registra criação e transições. Na Fase 4C, itens, endereço e valores do pedido não são editáveis; correções exigem cancelamento e novo registro.
- Produtos referenciados por pedidos serão inativados.
- Último pedido e contagem do cliente serão derivados inicialmente.
- Fase 6A introduz movimentos manuais de estoque; baixa/compensação por pedido terão incremento próprio, idempotente e transacional.
- Pagamentos manuais têm histórico de tentativas e estados independentes do status do pedido. Não há referência de provedor nem dados sensíveis de cartão. Integração online e conciliação exigirão contratos próprios.

Índices, limites de campos, constraints, transações e estratégias de concorrência serão implementados com cada módulo, acompanhados de migrations e testes.

Migration 20261001201327_AddDispatchStatuses (5B/5C): estende somente CK_Orders_Status e CK_OrderStatusHistory_Transition com AwaitingDelivery, OutForDelivery, Delivered e Finalized. Modalidade e recebimento integral são validados transacionalmente pelo serviço. Sem nova tabela ou dados de demonstração. Rollback não pode apagar histórico para eliminar estados novos; ver [expedição e impressão](dispatch-printing.md).

## Estoque manual — 6A

A migration `20261002130808_AddIngredientStock` acrescenta `CurrentStock numeric(9,3)` e `StockVersion bigint` a Ingredients, ambos inicialmente zero. `StockMovements` registra autor, ingrediente, cópias de nomes/unidade, RequestId, versão, tipo, quantidade, diferença, saldos anterior/novo, motivo e instante UTC. FKs restritas preservam referências; índices únicos por ingrediente/requestId e ingrediente/versão. Constraints de intervalo, tipo e equação do saldo complementam a transação do serviço. Não há consumo automático, custos históricos ou custo médio nesta etapa. Regras: [stock.md](stock.md).

## Estoque por pedidos — 6B

Migration `20261002181034_AddOrderStock` acrescenta Orders.StockStatus, OrderStockComponents (cópias de composição e consumo) e StockMovements.OrderId opcional. O índice único filtrado OrderId/IngredientId/Type impede duas baixas ou devoluções do mesmo ingrediente/pedido. FKs preservam os vínculos de auditoria. Pedidos anteriores não Novos ficam Legacy; Novos ficam Pending. Não há baixa retroativa nem alteração de saldo na migration. Rendimento e composição da confirmação são preservados, sem depender de versões posteriores da ficha. Ver [order-stock.md](order-stock.md).


## CMV teórico — 6C

Sem migration ou tabela nova. ProductCostService consulta Products, Categories, Recipes, RecipeItems e Ingredients em RepeatableRead, sem gravações. Resultados não são persistidos nem usados para reescrever custos de pedidos passados. A migration mais recente permanece AddOrderStock, herdada da 6B. Regras: [cmv.md](cmv.md).

## Dashboard diário — 7A

Sem migration, atualização de dados ou tabela de indicadores. DashboardService consulta Orders, OrderItems, OrderStatusHistory, Payments e PaymentStatusHistory em RepeatableRead. Confirmações e produção usam histórico do pedido; recebimentos e estornos usam histórico do pagamento, preservando a data efetiva dos eventos. Agregados não são persistidos. O modelo e a migration mais recente continuam os da 6B. Regras: [daily-dashboard.md](daily-dashboard.md).

## Relatórios por período — 7B

Sem mudanças no esquema ou nos dados. SalesReportService agrega Orders, OrderItems, OrderStatusHistory, Payments e PaymentStatusHistory em quatro consultas sob o mesmo snapshot. Datas civis de Brasília são convertidas em limites UTC e agrupadas explicitamente no PostgreSQL. Resultados não são persistidos; não há tabela de fechamento ou reconstrução fictícia de custos históricos. Ver [period-reports.md](period-reports.md).

## Fase 8A — leitura pública do catálogo

Utiliza Categories e Products existentes, com projeções de campos comerciais, ordenação e paginação. Categorias, total e itens são lidos em RepeatableRead. Sem migration, alteração de saldo ou persistência de dados públicos novos. Ver [public-menu.md](public-menu.md).

## Fase 8B — revisão do carrinho público

PublicCartService consulta Products e o estado de Categories com AsNoTracking/RepeatableRead, projetando apenas ID, nome e preço. Calcula subtotal em decimal no backend. A seleção existe somente em memória no frontend; não há tabela de carrinho, reserva, cliente anônimo ou escrita em pedidos/estoque/pagamentos. Nenhuma migration; o esquema permanece o da 6B. Ver [public-cart.md](public-cart.md).
