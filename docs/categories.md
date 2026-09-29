# Categorias — Fase 3A

Primeiro incremento do catálogo: gestão de categorias pela equipe administrativa. Produtos foram adicionados na [Fase 3B](products.md); ingredientes e fichas técnicas continuam para os próximos incrementos.

## Uso

Entre em http://localhost:8101/entrar com seu administrador e abra **Categorias** no menu. É possível buscar, filtrar por status, paginar, cadastrar, editar e ativar/inativar.

Se a sessão foi iniciada antes desta atualização, entre novamente para carregar a nova permissão catalog.manage. A página pública permanece sem cardápio neste incremento.

## Regras e dados

- ID UUID gerado no backend.
- Nome obrigatório, até 80 caracteres; espaços externos são removidos e a composição Unicode é normalizada (NFC).
- Nome único sem distinguir maiúsculas/minúsculas, inclusive em categorias inativas. Acentos e espaços internos continuam significativos.
- Descrição opcional, até 500 caracteres; espaços externos são removidos e texto vazio é salvo como null.
- DisplayOrder inteiro de 0 a 9999; menores primeiro, depois nome normalizado e ID para desempate estável.
- IsActive controla o estado do cadastro. Não há exclusão física nem exclusão em cascata.
- CreatedAt e UpdatedAt são gerados no servidor em UTC, com precisão de milissegundos, preservada nas leituras do banco.
- A repetição de uma operação de status para o mesmo valor não altera UpdatedAt.
- A Fase 3B vincula produtos às categorias. Inativar uma categoria torna seus produtos indisponíveis para venda sem reescrever os flags deles. Novos vínculos exigem categoria ativa; produtos existentes podem manter a categoria original inativa durante uma edição. Detalhes em [products.md](products.md).

O PostgreSQL garante unicidade por índice em NormalizedName e a faixa de DisplayOrder por constraint. A migration AddCategories adiciona apenas Categories e seu índice; Users e Roles permanecem intactos.

## API

Todas as rotas exigem a política catalog.manage, atualmente exclusiva de Administrator. As respostas não devem ser armazenadas em cache.

| Método | Rota | Resultado |
| --- | --- | --- |
| GET | /api/categories | Lista paginada |
| GET | /api/categories/{id} | Categoria |
| POST | /api/categories | 201 e Location |
| PUT | /api/categories/{id} | Atualização completa |
| PUT | /api/categories/{id}/status | Ativação/inativação |

POST:

```json
{
  "name": "Hambúrgueres",
  "description": "Feitos na hora",
  "displayOrder": 0,
  "isActive": true
}
```

Na criação, description é opcional, displayOrder tem padrão 0 e isActive tem padrão true. Na edição, displayOrder e isActive são obrigatórios para evitar alterações acidentais por omissão. PUT de status recebe somente isActive obrigatório.

Resposta: id, name, description, displayOrder, isActive, createdAt e updatedAt. NormalizedName permanece interno.

GET de lista aceita page (1 a 1.000.000, padrão 1), pageSize (1 a 100, padrão 20), search (até 80 caracteres) e isActive opcional. Busca por trecho do nome normalizado. Retorna items, page, pageSize e totalCount.

| HTTP | Code | Situação |
| --- | --- | --- |
| 400 | ValidationProblemDetails | Entrada inválida |
| 401 | InvalidSession ou rejeição JWT | Sessão ausente, expirada ou revogada |
| 403 | PermissionDenied ou rejeição de política | Sem permissão |
| 404 | CategoryNotFound | Categoria não existe |
| 409 | DuplicateCategoryName | Nome já utilizado |

## Concorrência

Criações simultâneas com o mesmo nome são resolvidas pelo índice único; a operação rejeitada retorna 409. Atualizações bloqueiam a linha da categoria e ocorrem em transação. Não há ETag ou versão de edição neste incremento: a última edição válida aplicada prevalece.

O autor é consultado novamente dentro da transação, validando conta ativa, perfil administrativo e SecurityStamp. Um bloqueio compartilhado mantém essa autorização válida até o término da gravação; alterações/revogação do autor aguardam o fim da operação.

Logs registram IDs da categoria e do autor. Não constituem uma trilha de auditoria persistente.

## Testar e executar

Na raiz:

```powershell
powershell -NoProfile -File scripts/Test-Authentication.ps1
```

O script agora executa também CategoryTests, sempre no PostgreSQL temporário guardado pelo fixture. Para executar somente categorias, acrescente -Filter FullyQualifiedName~CategoryTests.

No frontend:

```powershell
cd frontend/made-in-minas
npm.cmd run build
npm.cmd run test:e2e
```

Os testes de navegador usam respostas simuladas da API. Os testes de backend usam a API HTTP e PostgreSQL real isolado. Nenhuma categoria de exemplo é inserida no banco de desenvolvimento pelos testes.

Para aplicar em outro ambiente local já configurado, na raiz:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet ef database update --project backend/MadeInMinas.Api
dotnet run --project backend/MadeInMinas.Api --launch-profile http
```

O servidor não aplica migrations automaticamente. Revise migrations e prepare backup antes de mudanças em produção. Neste ambiente de desenvolvimento, a aplicação e a verificação da migration são registradas em [validation.md](validation.md).

Validação manual: crie sua primeira categoria, altere descrição e ordem, inative/reative, busque pelo nome e confira a rejeição ao cadastrar outro nome equivalente.

## Arquivos

Backend: Models/Category.cs, Data/Configurations/CategoryConfiguration.cs, DTOs/Categories/CategoryContracts.cs, Services/CategoryService.cs e CategoryException.cs, Infrastructure/CategoryExceptionHandler.cs, Controllers/CategoriesController.cs; ajustes em AppDbContext, AccessPolicies e Program. Migration AddCategories, designer e snapshot.

Frontend: core/services/category-api.service.ts, features/catalog/categories.page.ts e .html, category-form.page.ts e .html; ajustes em sessão, guards, mensagens, rotas, menu e staff.scss.

Testes: backend/MadeInMinas.Api.Tests/CategoryTests.cs e frontend/made-in-minas/e2e/categories.spec.ts.

Documentação: README, roteiro, banco, API, arquitetura, permissões, decisões, regras, instruções da equipe e validação.

Referências técnicas: [migrations do EF Core](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/managing), [transações do EF Core](https://learn.microsoft.com/en-us/ef/core/saving/transactions) e [precisão de datas no Npgsql](https://www.npgsql.org/doc/types/datetime.html).
