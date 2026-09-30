# Ingredientes — Fase 3C

Cadastro administrativo de insumos e embalagens, restrito a `catalog.manage` (administrador). Segue o padrão de categorias/produtos: controller, DTOs, service, configuração EF, migration e páginas Angular. Não adiciona dependências ou repositories.

## Usar

Entre em http://localhost:8101/entrar e abra **Ingredientes → Novo ingrediente**. Informe nome, unidade-base, custo por unidade, mínimo desejado, fornecedor opcional e status. Há busca por nome, filtro de status e paginação de 20 registros. Ativação/inativação pela lista exige confirmação.

| Unidade | Custo informado | Exemplo de quantidade |
| --- | --- | --- |
| kg | Preço de 1 kg | 0,150 kg = 150 g |
| l (L na interface) | Preço de 1 litro | 0,030 L = 30 mL |
| un | Preço de 1 unidade | 1 un de pão ou embalagem |

O custo é por unidade-base, não por pacote: se 5 kg custam R$ 150, informe R$ 30 por kg. A interface aceita vírgula ou ponto decimal, sem separador de milhar. Não há conversão automática de embalagens de compra.

A unidade fica fixa após o primeiro cadastro, tanto na interface quanto na API. Alterá-la silenciosamente mudaria o significado de custo, mínimo e futuras fichas técnicas. Para corrigir uma unidade escolhida incorretamente, inative o registro e crie outro com nome distinto. Uma futura operação de conversão precisará tratar todos os vínculos.

## Modelo e limites

- `Ingredients`: UUID, Name, NormalizedName interno, Unit, UnitCost, MinimumStock, Supplier, IsActive, CreatedAt e UpdatedAt em UTC.
- Nome obrigatório até 120 caracteres, aparado, normalizado em NFC e comparado em maiúsculas. Nome único em todo o cadastro, inclusive inativos; acentos e espaços internos são significativos.
- Unidade obrigatória: exatamente `kg`, `l` ou `un`.
- Custo decimal de 0 a 999999,9999, até quatro casas significativas; PostgreSQL `numeric(10,4)`. Zero é um custo informado explicitamente, não um custo desconhecido. A interface pede revisão antes do uso em cálculos.
- Mínimo decimal de 0 a 999999,999, até três casas significativas; PostgreSQL `numeric(9,3)`, na mesma unidade-base. Quantidades fracionárias são permitidas também em `un`.
- Valores que exigiriam arredondamento são rejeitados na API antes da persistência. O banco garante intervalos não negativos, unidade válida e unicidade.
- Fornecedor opcional até 150 caracteres, aparado; vazio vira null. É uma referência textual, sem módulo/tabela de fornecedores neste incremento.
- Repetir o mesmo status preserva UpdatedAt. Não há DELETE, ETag ou histórico de custos; a última edição válida aplicada prevalece.
- Escritas revalidam conta ativa, SecurityStamp e perfil do autor dentro de transação, com bloqueio compartilhado do usuário e exclusivo do ingrediente editado. Conflitos do índice único retornam 409, inclusive sob concorrência.

**Limite desta etapa:** MinimumStock é apenas a configuração de uma quantidade mínima desejada. Não há saldo atual, entradas, baixas, reservas, alertas ou cálculo de CMV. Esses recursos virão na fase 6; nenhum saldo zero fictício é apresentado. As [fichas técnicas da Fase 3D](recipes.md) avisam sobre ingredientes inativos e impedem novos vínculos com eles. Inativar ingrediente não altera automaticamente a disponibilidade dos produtos neste incremento.

## API

Todas as rotas exigem JWT, `catalog.manage` e retornam `Cache-Control: no-store`.

| Método | Rota | Resultado |
| --- | --- | --- |
| GET | /api/ingredients | Lista paginada |
| GET | /api/ingredients/{id} | Ingrediente |
| POST | /api/ingredients | Cria; 201 e Location |
| PUT | /api/ingredients/{id} | Atualiza mantendo a unidade |
| PUT | /api/ingredients/{id}/status | Define status |

POST e PUT usam o mesmo contrato:

```json
{
  "name": "Blend bovino",
  "unit": "kg",
  "unitCost": 32.4567,
  "minimumStock": 2.500,
  "supplier": "Fornecedor local",
  "isActive": true
}
```

Todos os campos, exceto supplier, são obrigatórios. Números JSON usam ponto decimal. Resposta inclui id, os campos acima e createdAt/updatedAt; não expõe NormalizedName.

Lista aceita page (1–1.000.000), pageSize (1–100), search (até 120) e isActive. Padrão 1/20; ordem por nome normalizado e UUID. Resposta: items, page, pageSize e totalCount. Status recebe `{ "isActive": false }`; o campo é obrigatório.

| HTTP | Código | Motivo |
| --- | --- | --- |
| 400 | ValidationProblemDetails | Campos, precisão ou consulta inválidos |
| 401 | InvalidSession ou rejeição JWT | Sessão ausente, inválida ou revogada |
| 403 | PermissionDenied ou rejeição da política | Sem acesso administrativo |
| 404 | IngredientNotFound | Registro inexistente |
| 409 | DuplicateIngredientName | Nome já cadastrado |
| 409 | IngredientUnitImmutable | Tentativa de mudar unidade |

## Aplicar e testar

Migration `AddIngredients` adiciona somente a tabela Ingredients, constraints e índice único. Não modifica produtos, categorias, funcionários ou senhas. Não há dados de demonstração inseridos no banco da hamburgueria.

Em desenvolvimento já configurado, na raiz:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet tool restore
dotnet ef database update --project backend/MadeInMinas.Api
dotnet run --project backend/MadeInMinas.Api --launch-profile http
```

Pare a instância da API que usa a mesma pasta de build antes de recompilá-la no Windows. A aplicação não aplica migrations automaticamente.

```powershell
powershell -NoProfile -File scripts/Test-Authentication.ps1
# Somente ingredientes:
powershell -NoProfile -File scripts/Test-Authentication.ps1 -Filter FullyQualifiedName~IngredientTests

cd frontend/made-in-minas
npm.cmd run build
npm.cmd run test:e2e
```

O backend é testado via HTTP em PostgreSQL temporário, isolado e protegido por nome/host/porta. Os testes de navegador usam API simulada em desktop e celular. Resultados desta execução em [validation.md](validation.md).

Validação manual: crie um ingrediente em kg com custo 32,4567 e mínimo 1,125; reabra, edite custo/fornecedor, inative e filtre por inativos, depois reative. Confira que a unidade fica bloqueada e que o nome duplicado é rejeitado. Repita para L e un conforme os insumos reais.

## Arquivos criados e modificados

- Criados no backend: Models/Ingredient.cs; Data/Configurations/IngredientConfiguration.cs; DTOs/Ingredients/IngredientContracts.cs; Services/IngredientService.cs e IngredientException.cs; Infrastructure/IngredientExceptionHandler.cs; Controllers/IngredientsController.cs; migration AddIngredients e seu Designer.
- Modificados no backend: Program.cs, Data/AppDbContext.cs e Data/Migrations/AppDbContextModelSnapshot.cs.
- Criados no frontend: core/services/ingredient-api.service.ts; features/catalog/ingredients.page.ts/.html e ingredient-form.page.ts/.html.
- Modificados no frontend: app.routes.ts, core/api-error.ts e features/staff/staff-layout.page.ts.
- Testes criados: backend/MadeInMinas.Api.Tests/IngredientTests.cs e frontend/made-in-minas/e2e/ingredients.spec.ts.
- Documentação: este arquivo; README.md; docs/roadmap.md, database.md, api.md, architecture.md, business-rules.md, decisions.md e validation.md.

Referência: [precisão e escala no EF Core](https://learn.microsoft.com/en-us/ef/core/modeling/entity-properties#precision-and-scale). A configuração do tipo não substitui validação prévia do contrato.
