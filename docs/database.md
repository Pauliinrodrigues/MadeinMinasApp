# Modelo conceitual inicial

Modelagem incremental. InitialAccessControl implementa Users e Roles; AddCategories implementa Categories; AddProducts implementa Products com FK restrita para Categories; AddIngredients implementa Ingredients; AddRecipes implementa Recipes e RecipeItems; AddCustomersAndAddresses implementa Customers e Addresses. As demais entidades continuam propostas.

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
| Orders | Número único, cliente, origem, status, totais, observações, cópia do endereço, datas | 4 |
| OrderItems | Pedido, produto ou combo, quantidade, nome/preço da compra, observações | 4 |
| Payments | Pedido, método, valor, status, referência externa, datas | 4 |
| OrderStatusHistory | Pedido, status anterior/novo, instante, responsável, motivo | 4 |

## Diretrizes

- UUID para identificadores técnicos; número de pedido separado, único e gerado atomicamente.
- Dinheiro em decimal/numeric e quantidades de ingredientes com precisão explícita.
- Instantes em UTC (timestamptz); exibição no fuso America/Sao_Paulo.
- Inicialmente um perfil por usuário; políticas permitem ampliar as permissões.
- Telefone normalizado para busca e deduplicação; conhecer um telefone não autentica um cliente.
- Receita define consumo na unidade-base do ingrediente. Embalagens podem ser ingredientes.
- OrderItems referencia produto OU combo, com restrição de integridade; composição escolhida fica registrada na compra.
- Adicionais requerem catálogo, associação de opções permitidas e cópia das escolhas no pedido. A modelagem detalhada será feita no incremento de catálogo.
- Alterações do cardápio ou endereço não reescrevem os valores históricos do pedido.
- OrderStatusHistory registra transições. A Fase 4 detalhará também a auditoria de mudanças em itens, endereço e valores.
- Produtos referenciados por pedidos serão inativados.
- Último pedido e contagem do cliente serão derivados inicialmente.
- Fase 6 introduzirá movimentos de estoque e estornos; a regra de baixa será idempotente e transacional.
- Pagamentos terão histórico de tentativas e estados independentes do status do pedido.

Índices, limites de campos, constraints, transações e estratégias de concorrência serão implementados com cada módulo, acompanhados de migrations e testes.
