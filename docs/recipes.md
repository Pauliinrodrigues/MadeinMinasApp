# Fichas técnicas — Fase 3D

O usuário validou ingredientes e autorizou este incremento. Cada produto pode ter uma ficha técnica com rendimento, ingredientes, quantidades e instruções de preparo. A administração exige `catalog.manage`, exclusiva do administrador.

## Usar

Entre em http://localhost:8101/entrar e abra **Produtos → Ficha técnica** na linha do produto. Há também um link na edição de um produto já salvo.

1. Informe quantas unidades do produto a ficha produz: rendimento inteiro de 1 a 10000.
2. Selecione de 1 a 100 ingredientes distintos, incluindo embalagens se necessário.
3. Informe a quantidade total de cada ingrediente para aquele rendimento, na unidade-base exibida.
4. Preencha as instruções de preparo opcionais e salve. Reabra a ficha para conferir a persistência.

Exemplo: dois hambúrgueres de 150 g de carne usam rendimento **2**, carne **0,300 kg**, pão **2 un** e molho **0,060 L**. A quantidade da linha corresponde à receita inteira. A interface aceita vírgula ou ponto, sem separador de milhar. Unidades são lidas do ingrediente, nunca escolhidas separadamente na ficha.

Remover uma linha só altera o formulário; é necessário salvar. Não é possível salvar sem ingredientes. Ingredientes inativos aparecem identificados. Podem ser mantidos ou removidos se já estiverem na ficha persistida; novos vínculos exigem ingredientes ativos. Após remover e salvar, um ingrediente inativo não pode ser adicionado novamente até ser reativado.

## Escopo

Este incremento registra a composição. Não movimenta estoque, calcula custo/CMV/margem nem registra produção. A listagem comercial mantém a disponibilidade manual; a Fase 6B passa a exigir ficha completa, ingredientes ativos e saldo suficiente ao confirmar o pedido. O carrinho não reserva estoque. A interface avisa sobre insumos inativos para revisão antes da produção. É possível manter a ficha de um produto inativo.

Não há sub-receitas, fichas de combos, conversão automática de kg/g ou L/mL, perdas, histórico de versões, exclusão da ficha, ETag ou controle de edição simultânea na interface. A última gravação válida aplicada prevalece. O cálculo e arredondamento do consumo por rendimento estão definidos na [Fase 6B](order-stock.md); a ficha é copiada no instante da confirmação.

## Modelo e integridade

- `Recipes`: UUID, ProductId único e obrigatório, YieldQuantity inteiro de 1 a 10000, Instructions opcional até 2000 caracteres, CreatedAt e UpdatedAt UTC com milissegundos.
- `RecipeItems`: chave composta RecipeId + IngredientId, Quantity `numeric(9,3)` entre 0,001 e 999999,999, Position de 0 a 99 atribuída pela API conforme a ordem enviada.
- A API rejeita quantidade ausente, zero, negativa, fora do limite ou com casas significativas além de três, sem arredondar. Ingredientes duplicados, itens null e lista vazia ou acima de 100 são rejeitados.
- FK de Recipes para Products e de RecipeItems para Ingredients restringem exclusão. Exclusão direta de uma Recipe no banco remove somente seus itens por cascade; não há endpoint de exclusão.
- Produto e ingredientes são referenciados, sem copiar nomes/unidades para tabelas de receita. Leituras refletem o cadastro atual; isso não representa um registro histórico de venda.
- Instruções são aparadas; vazias viram null. Rendimento e quantidades são obrigatórios, inclusive nas edições.

Gravações usam uma transação. O autor é revalidado com bloqueio compartilhado do usuário: ativo, SecurityStamp e perfil Administrator. Um bloqueio exclusivo do produto serializa as gravações de sua ficha, inclusive duas primeiras criações concorrentes. Os ingredientes são lidos com bloqueios compartilhados em ordem estável de UUID, impedindo inativação simultânea durante a validação e gravação. Somente após validar todos os ingredientes a composição é substituída. Falhas preservam a ficha anterior inteira.

Repetir o PUT preserva UUID da ficha, data de criação e unicidade dos vínculos, atualizando UpdatedAt. Não duplica ficha ou ingrediente. Não há estoque ou pagamento a repetir nesta operação.

## API

| Método | Rota | Resultado |
| --- | --- | --- |
| GET | /api/products/{productId}/recipe | Ficha atual; 404 se ainda não cadastrada |
| PUT | /api/products/{productId}/recipe | Cria (201 + Location) ou substitui (200) a ficha inteira |

Todas as rotas exigem JWT e catalog.manage, com respostas `no-store`. GET não cria uma ficha vazia.

```json
{
  "yieldQuantity": 2,
  "instructions": "Grelhar os blends e montar os lanches.",
  "items": [
    { "ingredientId": "<UUID da carne>", "quantity": 0.300 },
    { "ingredientId": "<UUID do pão>", "quantity": 2 }
  ]
}
```

Na API os números JSON usam ponto. `yieldQuantity` e `items` são obrigatórios; cada item exige ingredientId não vazio e quantity. Instructions pode ser omitido ou null. ProductId vem da rota; o cliente não define IDs de ficha, unidades, status ou posições persistidas.

Resposta: id, productId, yieldQuantity, instructions, createdAt, updatedAt, hasInactiveIngredients e items. Cada item retorna ingredientId, ingredientName, unit, ingredientIsActive e quantity, na ordem salva.

| HTTP | Code | Motivo |
| --- | --- | --- |
| 400 | ValidationProblemDetails | Campos, limites, quantidade inválida ou ingredientes repetidos |
| 400 | InvalidRecipeIngredient | Ingrediente inexistente |
| 400 | InactiveRecipeIngredient | Novo vínculo com ingrediente inativo |
| 401 | InvalidSession ou rejeição JWT | Sessão ausente, inválida ou revogada |
| 403 | PermissionDenied ou rejeição da política | Perfil sem acesso |
| 404 | ProductNotFound | Produto inexistente |
| 404 | RecipeNotFound | Produto existente sem ficha |

O frontend só trata `404/RecipeNotFound` como formulário novo. Falhas ao buscar produto, ficha ou qualquer página de ingredientes impedem editar e oferecem nova tentativa. Os campos são preservados quando a API rejeita uma gravação. O seletor percorre todas as páginas do cadastro de ingredientes.

## Aplicar e testar

Migration `20260930174555_AddRecipes` cria somente Recipes, RecipeItems, índices, constraints e FKs. Não insere exemplos e não altera cadastros de produtos, ingredientes ou funcionários. Migrations não são executadas automaticamente pelo servidor.

Na raiz, em um ambiente de desenvolvimento já configurado e com a API parada antes de recompilar a mesma pasta:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet tool restore
dotnet ef database update --project backend/MadeInMinas.Api
dotnet run --project backend/MadeInMinas.Api --launch-profile http
```

```powershell
powershell -NoProfile -File scripts/Test-Authentication.ps1
# Somente fichas técnicas:
powershell -NoProfile -File scripts/Test-Authentication.ps1 -Filter FullyQualifiedName~RecipeTests

cd frontend/made-in-minas
npm.cmd run build
npm.cmd run test:e2e
```

Backend testado via HTTP em PostgreSQL isolado. Os fixtures apagam receitas antes de produtos/ingredientes apenas nesse banco guardado. Navegador usa respostas de API simuladas em desktop e celular. Resultados e limites da execução em [validation.md](validation.md).

Aceite manual: selecione um produto real, salve sua composição, reabra, altere rendimento/quantidades e remova um item mantendo ao menos um. Confira kg/L/un e as instruções. Inative um ingrediente vinculado para verificar o aviso e reative ao terminar. Fase 4 começa após a validação deste incremento.

## Arquivos criados e modificados

- Backend criado: Models/Recipe.cs e RecipeItem.cs; Data/Configurations/RecipeConfiguration.cs e RecipeItemConfiguration.cs; DTOs/Recipes/RecipeContracts.cs; Services/RecipeService.cs e RecipeException.cs; Infrastructure/RecipeExceptionHandler.cs; Controllers/RecipesController.cs; migration AddRecipes e Designer.
- Backend integrado: Program.cs, Data/AppDbContext.cs e Data/Migrations/AppDbContextModelSnapshot.cs.
- Frontend criado: core/services/recipe-api.service.ts; features/catalog/recipe.page.ts e recipe.page.html.
- Frontend integrado: app.routes.ts, core/api-error.ts, core/services/ingredient-api.service.ts, features/catalog/products.page.html e product-form.page.html, src/staff.scss.
- Testes: RecipeTests.cs e e2e/recipes.spec.ts criados; limpeza dos fixtures CategoryTests.cs, ProductTests.cs e IngredientTests.cs ajustada às FKs.
- Documentação: README.md; docs/recipes.md, roadmap.md, database.md, api.md, architecture.md, business-rules.md, decisions.md, products.md, ingredients.md e validation.md.
