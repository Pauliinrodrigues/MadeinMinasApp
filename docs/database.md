# Modelo conceitual inicial

Modelagem incremental. InitialAccessControl implementa Users e Roles; AddCategories implementa Categories; AddProducts implementa Products com FK restrita para Categories; AddIngredients implementa Ingredients; AddRecipes implementa Recipes e RecipeItems; AddCustomersAndAddresses implementa Customers e Addresses. As demais entidades continuam propostas.

A Fase 4B (carrinho/revisão) consulta esse modelo sem criar tabelas ou migrations. Carrinhos não são persistidos. A Fase 4C acrescenta Orders, OrderItems e OrderStatusHistory pela migration `20261001142005_AddManualOrders`. A Fase 4D acrescenta Payments e PaymentStatusHistory por `20261001180855_AddManualPayments`. Regras e tipos: [orders.md](orders.md) e [payments.md](payments.md).

| Entidade | Dados e relações previstos | Fase |
| --- | --- | --- |
| Roles | Código único, nome | 2 |
| Users | Login único, nome, hash de senha, ativo, RoleId | 2 |
| Categories | Nome único normalizado, descrição opcional, ordem, ativo, criação/atualização UTC | 3A implementada |
| Products | Categoria obrigatória, nome único por categoria, descrição, preço numeric(8,2), URL de imagem, ativo, disponibilidade manual, datas UTC | 3B implementada |
| Ingredients | Nome único normalizado, unidade-base fixa (kg/l/un), custo numeric(10,4), mínimo numeric(9,3), fornecedor textual opcional, ativo, datas UTC; sem saldo nesta etapa | 3C implementada |
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
- Fase 6 introduzirá movimentos de estoque e estornos; a regra de baixa será idempotente e transacional.
- Pagamentos manuais têm histórico de tentativas e estados independentes do status do pedido. Não há referência de provedor nem dados sensíveis de cartão. Integração online e conciliação exigirão contratos próprios.

Índices, limites de campos, constraints, transações e estratégias de concorrência serão implementados com cada módulo, acompanhados de migrations e testes.
