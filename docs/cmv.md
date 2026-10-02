# CMV teórico por produto — Fase 6C

Branch `feat/product-cmv`, criada a partir do commit local `98ecb4a` da Fase 6B. O usuário autorizou continuar em uma nova branch após a conclusão técnica da 6B. Esta branch depende da 6B, ainda não integrada à master; a publicação deve respeitar essa ordem. Não foi registrado aceite manual que ainda não tenha ocorrido.

O administrador acessa **Produtos → CMV**, ou **Ficha técnica → Consultar CMV da ficha salva**. A consulta apresenta custo da composição atual, custo por unidade vendida, CMV percentual e margem bruta teórica. Nenhuma tabela, migration, dependência ou configuração adicional.

Implementação e validação técnica concluídas em 02/10/2026: 319 testes de backend e 62 cenários de navegador aprovados, além de build, formatação, lint e verificações locais. API em 5080 e frontend em 8101 disponíveis para aceite manual.

## Cálculo

1. Custo da linha = quantidade total da linha na ficha × custo atual do ingrediente por unidade-base.
2. Custo da ficha = soma dos custos de todas as linhas, incluindo embalagens cadastradas.
3. Custo por unidade do produto = custo da ficha ÷ rendimento.
4. CMV (%) = custo por unidade ÷ preço atual de venda × 100.
5. Margem bruta teórica (R$/unidade) = preço de venda − custo por unidade.
6. Margem bruta teórica (%) = 100 − CMV (%).

Exemplo: ficha para 2 produtos com custo total R$ 12,00, venda por R$ 20,00 → custo unitário R$ 6,00, CMV 30%, margem R$ 14,00 e 70%.

O backend calcula em decimal, sem arredondar as linhas ou as quantidades por rendimento antes dos totais. Custos retornam com a precisão de decimal; apenas CMV percentual é arredondado a duas casas, afastando do zero em empates. A margem percentual complementa esse CMV arredondado para somar 100%. A interface formata moeda com até seis casas e percentuais com duas, sem recalcular indicadores. Valores positivos/negativos inferiores à resolução monetária da tela aparecem como limites, preservando a indicação de que não são zero.

O arredondamento para cima usado na movimentação física da Fase 6B não participa deste cálculo teórico. Não há conversão de kg/g, L/mL ou unidades. O preço comercial existente é obrigatório e maior que zero. Custos acima do preço produzem CMV acima de 100% e margem negativa, com aviso, sem limitar artificialmente os indicadores.

## Pendências e limites

- Sem ficha ou sem linhas: `MissingRecipe`, indicadores nulos e orientação para cadastrar a composição. Consultar não cria uma ficha vazia.
- Qualquer ingrediente com UnitCost zero: `MissingCosts`. A tela identifica os itens e mostra somente o custo conhecido da ficha como **parcial**; custo completo, CMV e margens ficam nulos. Zero é tratado como pendência, mesmo sendo permitido no cadastro. Ingredientes deliberadamente gratuitos exigiriam uma regra explícita em outro incremento.
- Ficha com todos os custos positivos: `Ready`. Ingredientes inativos continuam considerados pelo custo cadastrado, com avisos. Produto/categoria inativos ou venda pausada não impedem a revisão de custos.
- A consulta usa somente a ficha salva. Alterações não salvas no formulário não entram no cálculo. Depois de salvar preço, ficha ou custos, atualizar a consulta.
- Custos são os valores atuais informados manualmente no cadastro. Não são custo médio, custo histórico da compra ou CMV realizado de pedidos já vendidos.
- A margem considera ingredientes/embalagens da ficha. Não inclui impostos, taxas, mão de obra, despesas fixas, desperdício não cadastrado ou outras despesas. Não representa lucro líquido.
- Não há dashboard, consolidado por período, comparativo de produtos, sugestão de preço, metas de CMV ou novos lançamentos financeiros. Esses recursos pertencem a incrementos posteriores.

## API e arquitetura

`GET /api/products/{productId}/costing`, JWT e `catalog.manage` (administrador), `Cache-Control: no-store`.

HTTP 200 retorna ProductCostResponse com produto/disponibilidade/preço, Status, rendimento/data da ficha, composição com quantidade/unidade/custo/estado do ingrediente, KnownRecipeCost, RecipeCost, UnitCost, CmvPercentage, GrossMargin, GrossMarginPercentage e CalculatedAt UTC. Status de pendência também retorna 200, com produto e dados disponíveis. Produto inexistente retorna 404/ProductNotFound; sem sessão 401; atendente/cozinha/expedição 403.

ProductCostService lê produto, ficha e custos em uma transação RepeatableRead, com AsNoTracking. Todas as informações pertencem ao mesmo snapshot. Usa ProductException/handler e política de catálogo já existentes. Não aceita preço/custo do navegador e não grava estoque, pedidos, receitas, produtos ou pagamentos. Não há novos repositories nem calculadora financeira genérica.

O frontend carrega uma página standalone, protegida por catalogGuard. Uma única consulta traz composição e indicadores. Ao atualizar, remove os valores anteriores; falha de rede/servidor exibe erro e nova tentativa, sem manter indicadores antigos como atuais. Requisições têm timeout de 15 segundos e são canceladas ao sair da página. Sessão revogada segue o interceptor existente.

## Validar

1. Entrar como administrador em http://localhost:8101 e abrir **Produtos → CMV**.
2. Conferir quantidades, custos na unidade-base, rendimento, custo por unidade, CMV e margem com uma ficha conhecida.
3. Alterar custo de um ingrediente ou preço/rendimento, salvar e usar **Atualizar cálculo**; os resultados devem refletir os novos valores.
4. Conferir produto sem ficha e ingrediente com custo zero: a página orienta a correção e não apresenta margem completa.
5. Usar um preço inferior ao custo para verificar a margem negativa. Restaurar os dados corretos após a conferência.
6. Confirmar que atender/cozinha/expedição não acessam a consulta e que consultar CMV não cria movimentos de estoque ou pedidos.

Testes automatizados:

```powershell
powershell -NoProfile -File scripts/Test-Authentication.ps1 -Filter FullyQualifiedName~ProductCostTests
cd frontend/made-in-minas
npm.cmd run test:e2e -- e2e/product-cost.spec.ts e2e/products.spec.ts e2e/recipes.spec.ts
```

Backend usa PostgreSQL temporário isolado. Navegador usa API simulada em desktop/mobile. Resultados e disponibilidade local em [validation.md](validation.md). Aceite manual da 6C ainda pendente; nenhum avanço ao dashboard nesta entrega.

## Arquivos

- Criados no backend: Controllers/ProductCostsController.cs, Services/ProductCostService.cs, DTOs/Costing/ProductCostResponse.cs; registro no Program.cs.
- Criados no frontend: core/services/product-cost-api.service.ts, features/catalog/product-cost.page.ts e product-cost.page.html; rota em app.routes.ts, links em products.page.html e recipe.page.html.
- Testes novos: backend/MadeInMinas.Api.Tests/ProductCostTests.cs e frontend/made-in-minas/e2e/product-cost.spec.ts.
- Documentos atualizados: README, roadmap, arquitetura, banco, API, regras, decisões, fichas técnicas e validação.
