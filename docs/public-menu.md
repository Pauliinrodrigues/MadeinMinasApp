# Cardápio público — Fase 8A

Primeiro incremento da Fase 8, na branch `feat/public-menu`, criada da master `5adcd03`. O cliente acessa `/pedido` sem cadastro ou sessão de funcionário. O link **Ver cardápio** está na página inicial. Esta entrega é de consulta; carrinho público, registro/acompanhamento de pedidos, conversa, transferência para atendente e IA terão incrementos próprios.

## Regras

- Somente produtos ativos em categorias ativas aparecem. Categorias vazias ou que contenham apenas produtos inativos ficam ocultas.
- Produtos ativos com venda pausada continuam visíveis, identificados como **Indisponível**. A disponibilidade vem do backend; não é uma promessa de saldo físico, reserva ou horário de funcionamento.
- Nome, descrição, preço e imagem vêm do cadastro existente. A descrição comercial não é montada a partir da ficha técnica e deve ser revisada pela equipe.
- Categorias seguem DisplayOrder, nome normalizado e ID. Produtos seguem a ordem das categorias, nome normalizado e ID. O filtro conserva todas as categorias públicas para navegação.
- Preços são lidos em decimal e apenas formatados no frontend. Não há preço demonstrativo, cálculo comercial no navegador, custo, saldo, fornecedor ou instrução de preparo na resposta pública.
- As imagens usam a URL HTTPS cadastrada. Enquanto a imagem carrega, quando está ausente ou em falha, aparece uma apresentação tipográfica da marca. Não há upload ou busca de imagens nesta etapa; o navegador carrega a imagem diretamente e não envia Referer.
- Atualização é manual. Durante consulta/falha, os produtos anteriores são removidos. Filtro e atualização ficam bloqueados durante a requisição; timeout de 15 segundos permite tentar novamente.
- O menu funciona sem JWT. O interceptor não anexa o token da equipe à consulta do menu; uma falha pública não encerra a sessão do funcionário. As APIs administrativas mantêm a autorização existente.

## Contrato

`GET /api/menu`, público, somente leitura, `Cache-Control: no-store`.

| Parâmetro | Regra |
| --- | --- |
| categoryId | GUID opcional. Categoria inexistente/inativa retorna página vazia, sem revelar seu cadastro |
| page | 1 a 1.000.000; padrão 1 |
| pageSize | 1 a 48; padrão 24 |

Resposta 200:

```text
categories: [{ id, name }]
items: [{ id, categoryId, name, description, price, imageUrl, isAvailable }]
page, pageSize, totalCount
```

Categorias, contagem e itens são consultados no mesmo snapshot RepeatableRead. Não carrega receitas, usuários ou entidades comerciais completas. O catálogo vazio retorna arrays vazios e totalCount zero. Página além do fim retorna items vazio e totalCount do filtro. GUID inválido e limites de paginação inválidos retornam 400 ValidationProblemDetails. Falhas internas seguem o tratamento ProblemDetails existente.

## Conferência local

1. Iniciar backend/frontend conforme o README e acessar `http://localhost:8101/pedido` em janela anônima.
2. Conferir nome, descrição, imagem e preço com o cadastro administrativo. Testar desktop e celular.
3. Em ambiente de teste, pausar a disponibilidade de um produto ativo e atualizar o menu: deve aparecer Indisponível. Inativar produto/categoria deve removê-lo da consulta seguinte.
4. Selecionar categoria, navegar entre páginas se houver mais de 24 produtos e retornar a todas as categorias. Não inserir produtos fictícios na operação apenas para esta conferência.
5. Conferir a apresentação sem imagem ou com URL que não carrega. Nomes/descrições são texto, sem execução de HTML.
6. Parar apenas a API de desenvolvimento e atualizar: os cards anteriores desaparecem, há mensagem de falha e opção de tentar novamente. Reiniciar a API e repetir.
7. O menu não deve oferecer ações de carrinho/pagamento; atualizar a página não exige login. APIs de produtos, custos e categorias administrativos continuam protegidas.

## Testes e arquivos

```powershell
# Raiz: regressão backend em PostgreSQL temporário isolado
powershell -NoProfile -File scripts/Test-Authentication.ps1

# frontend/made-in-minas
npm.cmd run format:check
npm.cmd run lint
npm.cmd run build
npm.cmd run test:e2e -- menu.spec.ts staff.spec.ts products.spec.ts
```

Backend testa com PostgreSQL real isolado; navegador usa API simulada. A validação manual com o catálogo operacional é separada. Resultados: [validation.md](validation.md).

- Backend novos: `Controllers/MenuController.cs`, `Services/PublicMenuService.cs`, `DTOs/Menu/PublicMenuContracts.cs` sob `backend/MadeInMinas.Api`, e `backend/MadeInMinas.Api.Tests/PublicMenuTests.cs`.
- Backend alterado: `Program.cs`, registro do serviço.
- Frontend novos: `src/app/core/services/menu-api.service.ts`, `src/app/features/menu/menu.page.ts`, `.html`, `.scss` e `e2e/menu.spec.ts`.
- Frontend alterado: `app.routes.ts`, `core/auth/auth.interceptor.ts`, `features/home/home.page.html` e `.scss`.
- Documentação: README, roteiro, arquitetura, API, banco, regras, decisões, validação e este documento.

Não cria entidades, migrations, dependências, cadastros ou configurações sensíveis. A etapa não publica a aplicação na internet. Hospedagem, HTTPS/proxy, limites de requisições adequados ao ambiente e operação em produção exigem preparação própria. A próxima proposta é a Fase 8B: carrinho público, com definição das regras necessárias para a compra.
