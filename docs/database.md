# Modelo conceitual inicial

Modelagem incremental. InitialAccessControl implementa Users e Roles; AddCategories implementa Categories; AddProducts implementa Products com FK restrita para Categories. As demais entidades continuam propostas.

| Entidade | Dados e relações previstos | Fase |
| --- | --- | --- |
| Roles | Código único, nome | 2 |
| Users | Login único, nome, hash de senha, ativo, RoleId | 2 |
| Categories | Nome único normalizado, descrição opcional, ordem, ativo, criação/atualização UTC | 3A implementada |
| Products | Categoria obrigatória, nome único por categoria, descrição, preço numeric(8,2), URL de imagem, ativo, disponibilidade manual, datas UTC | 3B implementada |
| Ingredients | Nome, unidade-base, custo unitário, mínimo, fornecedor, ativo | 3 |
| Recipes | Produto e rendimento | 3 |
| RecipeItems | Receita, ingrediente, quantidade na unidade-base | 3 |
| Combos | Nome, descrição, preço e disponibilidade próprios | Catálogo, incremento específico |
| ComboItems | Combo, produto e quantidade | Catálogo, incremento específico |
| Customers | Nome, telefone normalizado, datas | 4 |
| Addresses | Cliente e dados de entrega | 4 |
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
