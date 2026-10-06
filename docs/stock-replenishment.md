# Central de reposição

Bloco F dos ajustes operacionais, iniciado em 06/10/2026 na branch `feat/operational-usability`. Consolida os saldos existentes e as pendências dos ingredientes. Usa `catalog.manage`, exclusivo do administrador, sem nova permissão, migration, tabela ou dependência.

## Uso e regras

Abra **Gestão → Reposição**. Também há um atalho na lista de ingredientes e na tela de estoque individual. Inicialmente aparecem ingredientes ativos que estão no mínimo ou abaixo, ou que ainda não possuem movimentação registrada.

- **Saldo zero:** saldo de sistema igual a zero, inclusive quando o mínimo é zero.
- **No mínimo ou abaixo:** `CurrentStock <= MinimumStock`, mantendo a regra do estoque individual. Igualdade continua gerando alerta.
- **Sem movimentação:** versão de estoque zero. Indica ausência de lançamento; não comprova nem substitui inventário físico. Uma entrada anterior também não comprova contagem física. Contagem igual ao saldo continua sem gerar movimento no módulo existente.
- **Precisam de atenção:** união de estoque baixo e ausência de movimentações, sem duplicar o ingrediente.
- **Todos:** inclui também os ingredientes acima do mínimo. Inativos só aparecem quando a opção de inclusão estiver aplicada e continuam impedidos de movimentar até reativação.

Cada cartão mostra saldo, mínimo, fornecedor, unidade e **Falta até o mínimo**, calculada no backend como `max(0, mínimo - saldo)`. No limite essa diferença é zero, mas o alerta permanece. Ela não é previsão de compra, reserva, pedido ao fornecedor ou quantidade garantida para produção. Não são somadas quantidades de unidades diferentes, calculados custos de compra ou geradas despesas.

A busca considera nome ou fornecedor, sem distinguir maiúsculas/minúsculas e com normalização Unicode. Os contadores consideram busca e inclusão de inativos, independentemente da fila selecionada. Um ingrediente pode contribuir para vários contadores; estes não devem ser somados. A lista prioriza saldo zero, estoque baixo e ausência de movimentação, depois nome e identificador.

**Buscar estoque** aplica os campos e volta à primeira página. Atualização manual, paginação e repetição de consulta usam os filtros aplicados, preservando a busca enquanto os campos estão em edição. Os atalhos dos contadores mantêm a busca e a inclusão de inativos aplicadas, descartando alterações ainda não aplicadas. Falha de atualização preserva os últimos dados com aviso e horário da consulta. Ao retornar à central, uma nova consulta abre a fila inicial de atenção; filtros/página não são persistidos entre navegações.

**Conferir estoque** abre a tela existente sem preencher ou confirmar lançamento. **Editar cadastro** abre o ingrediente para corrigir mínimo/fornecedor/status. Toda alteração de saldo continua exigindo quantidade real, motivo, revisão e confirmação no módulo de estoque. A central é somente leitura, não possui polling e não altera a disponibilidade manual dos produtos.

## API e implementação

`GET /api/stock/replenishment`, JWT + `catalog.manage`, `Cache-Control: no-store`.

| Parâmetro | Regra |
| --- | --- |
| `page` | 1 a 1.000.000; padrão 1 |
| `pageSize` | 1 a 100; padrão 20 |
| `search` | Opcional, até 150 caracteres; nome ou fornecedor |
| `status` | `Attention` (padrão), `OutOfStock`, `LowStock`, `Unrecorded` ou `All` |
| `includeInactive` | Booleano, padrão false |

Retorna `summary` com total/atenção/zero/baixo/sem movimentação; `items` com `ingredientId`, `name`, `unit`, `supplier`, `isActive`, `currentStock`, `minimumStock`, `quantityToMinimum`, `isLowStock`, `hasMovements`; e `page`, `pageSize`, `totalCount` da fila. Estoque vazio retorna contadores zerados e lista vazia. Query inválida retorna 400; sem sessão 401, perfil não autorizado 403.

`StockService.ReplenishmentAsync` usa projeções EF no PostgreSQL e lê contadores, total e página na mesma transação `RepeatableRead`. Filtra antes de paginar, sem carregar todos os ingredientes/históricos ou consultar cada ingrediente separadamente. Reutiliza saldo/versão mantidos pelos serviços de estoque existentes. O frontend não calcula a diferença até o mínimo.

## Validação funcional

1. Entre como administrador e abra Reposição. Confira saldo, mínimo, unidade e fornecedor contra o estoque/cadastro.
2. Combine busca por fornecedor, fila e inclusão de inativos. Confira contadores, paginação, consulta vazia e alerta de filtros ainda não aplicados.
3. Confira ingrediente exatamente no mínimo: alerta presente, diferença zero. Ingrediente sem movimentação exige conferência física, não lançamento automático.
4. Em ambiente de teste, abra Conferir estoque, registre uma entrada pelos controles existentes e volte à central: a consulta deve refletir o saldo atual. Se a entrada superar o mínimo, o ingrediente deixa a fila de atenção, desde que não tenha outra pendência.
5. Simule falha de consulta em teste: a lista anterior permanece com aviso; repetir não aplica campos em edição.
6. Atendente, cozinha e expedição não podem acessar o endpoint ou a tela. Repita os fluxos em desktop e celular.

Use somente dados reais nos lançamentos da operação. Resultados automatizados e limitações em [validation.md](validation.md). Compras/fornecedores estruturados, previsão por consumo, estoque máximo, alertas automáticos e registro de inventário sem ajuste de saldo ficam para incrementos próprios.

## Arquivos

- Criados: `Controllers/StockReplenishmentController.cs`, `DTOs/Stock/StockReplenishmentContracts.cs`, `StockReplenishmentTests.cs`, `features/stock/replenishment.page.ts`, `.html`, `.scss`, `e2e/replenishment.spec.ts` e este documento.
- Alterados: `Services/StockService.cs`, `core/services/stock-api.service.ts`, rotas, menu da equipe e links nas telas de ingredientes/estoque.
- Documentação: README, roteiro operacional, roadmap, arquitetura, API, regras de negócio, estoque e validação.
