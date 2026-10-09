# Produtos — Fase 3B

Gestão administrativa de produtos vinculados às categorias da Fase 3A. Usa catalog.manage, exclusiva do administrador. Não cria carrinho, pedidos, estoque ou cardápio público. Ingredientes foram adicionados na [Fase 3C](ingredients.md) e fichas técnicas na [Fase 3D](recipes.md), acessíveis pela listagem e edição de produtos.

## Usar

Entre em http://localhost:8101/entrar e abra **Produtos → Novo produto**. Cadastre antes pelo menos uma categoria ativa.

Informe nome, categoria, preço, descrição e imagem opcionais, status e disponibilidade. O preço aceita vírgula ou ponto decimal (29,90 ou 29.90), sem separador de milhar. Listagem com busca, filtros de categoria/status/venda e paginação de 20 registros.

As categorias do seletor são carregadas percorrendo todas as páginas da API, sem limitar a escolha à primeira página. Categorias inativas aparecem identificadas e não podem ser escolhidas para novos vínculos. Na edição, a categoria original pode ser mantida se tiver sido inativada.

## Modelo e regras

- Products: UUID; CategoryId obrigatório; Name até 120 caracteres; NormalizedName interno; Description opcional até 1000; Price decimal; ImageUrl opcional até 2048; IsActive; IsAvailable; CreatedAt e UpdatedAt em UTC com milissegundos.
- Nome é aparado, normalizado em NFC e comparado em maiúsculas. A combinação CategoryId + NormalizedName é única, incluindo produtos inativos. Acentos e espaços internos são significativos.
- O mesmo nome pode existir em categorias diferentes. Mover para outra categoria também respeita a unicidade.
- Preço entre R$ 0,01 e R$ 999.999,99, com até duas casas decimais significativas. A API rejeita valores que precisariam ser arredondados, antes de gravar numeric(8,2). Comparações usam decimal, sem depender do formato regional do servidor.
- O banco garante faixa do preço, unicidade e FK para Categories com exclusão restrita. Não há DELETE de produto.
- Novos vínculos exigem categoria existente e ativa. A edição pode manter uma categoria original inativa para permitir correções.
- IsActive representa o cadastro ativo; IsAvailable é a disponibilidade manual para pausas temporárias. Alterar um não reescreve o outro.
- IsAvailableForSale é calculado no backend: IsActive && IsAvailable && Category.IsActive.
- Inativar uma categoria torna seus produtos indisponíveis sem alterar os flags ou datas deles. Reativar a categoria restaura a disponibilidade somente daqueles que continuam ativos e com IsAvailable ligado.
- Essa disponibilidade ainda não considera estoque, horário de funcionamento, pedidos ou pagamentos, pois esses módulos não existem neste incremento.
- Repetir o mesmo valor nos endpoints de status/disponibilidade não altera UpdatedAt.

A interface oferece confirmação antes das mudanças de status e disponibilidade pela lista e explica por que um produto está indisponível.

## Imagens

Em **Foto do produto**, selecione JPG/JPEG, PNG ou WebP de até 8 MB e 24 megapixels, sem animação. A seleção mostra uma prévia local; o arquivo só é enviado ao clicar em **Salvar produto**. Para substituir, escolha outra foto. **Remover foto** limpa o formulário e precisa ser confirmado salvando o produto. Falhas de envio preservam os campos e o arquivo selecionado; se o upload terminar e a gravação do produto falhar, a próxima tentativa reutiliza a foto enviada.

Somente administradores enviam arquivos. A API confere o formato pelo conteúdo, decodifica o arquivo completo, corrige a orientação do celular e gera WebP com qualidade 85 e lado maior de até 1600 pixels, preservando a proporção. A nova codificação remove metadados do original. Nomes são UUIDs gerados pelo servidor, sem aproveitar nomes/caminhos recebidos. `ImageUrl` guarda `/api/product-images/<UUID sem hífens>.webp`; o frontend resolve esse endereço na origem da API. A leitura é pública para o cardápio, com cache imutável de um ano e `nosniff`.

As fotos ficam em `backend/MadeInMinas.Api/App_Data/product-images`, fora do Git. O diretório é criado no primeiro envio. Para usar um disco persistente, configure `ProductImages__StoragePath` (caminho absoluto ou relativo à raiz da API). Em hospedagem com múltiplas instâncias, todas precisam acessar o mesmo armazenamento. Inclua esse diretório no backup junto com o banco; apenas buscar o código em outro computador não transfere as fotos já enviadas. Não é necessária migration para este incremento.

Substituir/remover desvincula a foto do produto; arquivos antigos ou enviados antes de um cadastro abandonado permanecem no armazenamento, preservando outras referências e caches. Limpeza de arquivos sem uso e armazenamento externo ficam para uma evolução posterior.

Links HTTPS sem credenciais continuam opcionais como alternativa quando não há foto própria selecionada. O navegador usa `referrerpolicy=no-referrer` e trata falhas de prévia. A API valida o endereço remoto, sem baixar nem conferir o conteúdo remoto.

## API

Todas as rotas exigem catalog.manage e respostas no-store.

| Método | Rota | Resultado |
| --- | --- | --- |
| GET | /api/products | Lista paginada |
| GET | /api/products/{id} | Produto |
| POST | /api/products | Cria; 201 com Location |
| PUT | /api/products/{id} | Atualização completa |
| PUT | /api/products/{id}/status | Define IsActive |
| PUT | /api/products/{id}/availability | Define IsAvailable |

Fotos: `POST /api/product-images` exige `catalog.manage` e recebe `multipart/form-data` com o campo `file`; retorna 201, `Location` e `{ "imageUrl": "/api/product-images/<UUID>.webp" }`. `GET /api/product-images/{fileName}` é público e retorna `image/webp`; arquivos inexistentes ou nomes fora do padrão retornam 404. O cadastro aceita apenas referências próprias que existam no armazenamento. Envio inválido: 400/`InvalidProductImage`; arquivo acima de 8 MB: 413/`ProductImageTooLarge`; arquivo ausente na leitura: 404/`ProductImageNotFound`. O limite do corpo inclui 64 KB adicionais para o formulário; o proxy de hospedagem precisa permitir esse limite.

POST e PUT recebem o mesmo contrato:

```json
{
  "name": "Uai Sô",
  "categoryId": "<UUID de categoria ativa>",
  "price": 29.90,
  "description": "Pão, blend bovino, queijo e bacon.",
  "imageUrl": null,
  "isActive": true,
  "isAvailable": true
}
```

name, categoryId, price, isActive e isAvailable são obrigatórios, inclusive na criação. Descrição/imagem podem ser omitidas ou null. Valores opcionais vazios são normalizados para null. Na API, price é um número JSON com ponto decimal; a interface converte a vírgula antes de enviar.

PUT de status recebe { "isActive": false }; PUT de disponibilidade recebe { "isAvailable": false }. O flag é obrigatório e não é alternado implicitamente.

Resposta: id, categoryId, categoryName, categoryIsActive, name, description, price, imageUrl, isActive, isAvailable, isAvailableForSale, createdAt, updatedAt.

GET lista aceita page (1 a 1.000.000), pageSize (1 a 100), search (até 120), categoryId, isActive e isAvailableForSale. Padrão: página 1, 20 registros. Ordenação por nome normalizado e ID. Retorno: items, page, pageSize e totalCount.

| HTTP | Code | Situação |
| --- | --- | --- |
| 400 | ValidationProblemDetails | Campos obrigatórios, preço, limites ou URL inválidos |
| 400 | InvalidProductCategory | Categoria inexistente |
| 400 | InactiveProductCategory | Novo vínculo com categoria inativa |
| 401 | InvalidSession ou rejeição JWT | Sessão inválida |
| 403 | PermissionDenied ou rejeição de política | Perfil sem acesso |
| 404 | ProductNotFound | Produto inexistente |
| 409 | DuplicateProductName | Nome repetido na mesma categoria |

## Integridade e concorrência

Cada gravação ocorre em transação. O autor é validado novamente com bloqueio compartilhado de sua linha (conta ativa, perfil e SecurityStamp). A categoria também é lida com bloqueio compartilhado, evitando que uma inativação concorra com a validação de um novo vínculo. Alterações no produto bloqueiam sua linha.

O índice único decide criações/edições concorrentes com nomes equivalentes; conflitos retornam 409. Não há ETag ou versionamento de edição neste incremento: a última edição válida aplicada prevalece. IsAvailableForSale é calculado a cada leitura; não é uma reserva nem autorização definitiva para um futuro pedido.

Logs registram IDs do autor e produto e mudanças de flags, sem dados de credenciais.

## Testar e aplicar em outra máquina

Na raiz, com PostgreSQL 17 instalado:

```powershell
powershell -NoProfile -File scripts/Test-Authentication.ps1
```

Inclui autenticação, funcionários, categorias, produtos e fotos, em PostgreSQL isolado. Somente catálogo/fotos: acrescente `-Filter 'FullyQualifiedName~ProductTests|FullyQualifiedName~ProductImageTests|FullyQualifiedName~PublicMenuTests'`. As fotos de teste também usam diretórios isolados e são removidas ao encerrar o fixture.

No frontend:

```powershell
cd frontend/made-in-minas
npm.cmd run build
npm.cmd run test:e2e
```

Testes de navegador usam API simulada; testes HTTP do backend usam PostgreSQL real temporário. Não são inseridos produtos de demonstração no banco da hamburgueria.

Para aplicar a migration em outro desenvolvimento já configurado, na raiz:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet ef database update --project backend/MadeInMinas.Api
dotnet run --project backend/MadeInMinas.Api --launch-profile http
```

AddProducts somente adiciona Products, FK, índice único e constraint de preço. Não altera categorias, contas ou senhas. O servidor não executa migrations automaticamente.

Validação manual: crie um produto em categoria ativa, edite o preço, pause/libere a venda, inative/reative e confira o filtro de disponibilidade. Inative a categoria e confirme que o produto fica indisponível; reative-a ao concluir o teste.

## Arquivos

- Backend: Models/Product.cs, Data/Configurations/ProductConfiguration.cs, DTOs/Products/ProductContracts.cs, Services/ProductService.cs e ProductException.cs, Infrastructure/ProductExceptionHandler.cs, Controllers/ProductsController.cs.
- Integração: AppDbContext, Program, migration AddProducts e snapshot.
- Frontend: core/services/product-api.service.ts, features/catalog/products.page.ts/.html e product-form.page.ts/.html; ajustes no seletor de categorias, erros, rotas, menu e estilos.
- Testes: ProductTests.cs, limpeza de CategoryTests.cs e e2e/products.spec.ts.
- Documentação: README, roteiro, modelo de banco, API, arquitetura, categorias, decisões, interface da equipe e validação.

Referências: [precisão decimal no EF Core](https://learn.microsoft.com/en-us/ef/core/modeling/entity-properties#precision-and-scale) e [validação de modelos no ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/mvc/models/validation?view=aspnetcore-10.0).
