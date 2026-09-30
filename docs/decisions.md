# Decisões técnicas

## 001 — Estrutura simples

Uma API e um frontend no mesmo repositório. Reaproveitamos a solução original, convertendo o console sem lógica de negócio para ASP.NET Core Web API.

## 002 — Dependências locais e fixadas

SDK definido em global.json; dotnet-ef no manifesto local; NuGet com packages.lock.json; frontend com package-lock.json. Os CLIs globais antigos não são requisito.

Angular 20 foi escolhido por ser compatível com as duas linhas de Node encontradas; Ionic 9.0.5 declara Angular >=18. Builds e teste em navegador validam a combinação concreta.

Referências:
- [Compatibilidade Angular](https://angular.dev/reference/versions)
- [Ionic Angular](https://ionicframework.com/docs/angular/overview)
- [Npgsql EF Core](https://www.npgsql.org/efcore/)
- [Health checks ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks?view=aspnetcore-10.0)

## 003 — Sem migrations vazias

O DbContext existe para preparar a integração. As tabelas serão criadas conforme cada fase. O processo de inicialização não altera o banco.

## 004 — Conexão opcional durante a fundação

A API pode iniciar sem credenciais de desenvolvimento. Readiness responde 503 nesse caso. Liveness permanece 200. A indisponibilidade do banco não é tratada como sucesso.

## 005 — Segredos fora do Git

User Secrets no desenvolvimento e variáveis de ambiente na hospedagem. O frontend recebe somente a URL pública da API. Connection strings não são registradas nos logs da aplicação.

## 006 — Produção será preparada explicitamente

O build do frontend usa /api no mesmo domínio. Domínio, TLS, proxy, backup/restore, monitoramento e credenciais de implantação dependem do ambiente real. A Fase 1 não publica nenhum serviço nem instala atualizações globais de SDK/runtime.

## 007 — Autenticação incremental de funcionários

A Fase 2A implementa JWT interno de 15 minutos, sem refresh automático. PasswordHasher do ASP.NET Core protege senhas; Users possui SecurityStamp para revogação e estado persistido de bloqueio. Tokens são verificados contra o usuário e perfil atuais no banco.

O primeiro administrador é criado somente por comando local e apenas se não há usuários. Os quatro perfis são dados da migration, sem credenciais. Políticas centralizam a autorização; as permissões de módulos futuros não criam esses módulos.

Microsoft.EntityFrameworkCore.Relational 10.0.12 é uma dependência explícita de runtime. Isso mantém API e projeto de testes na mesma versão; a referência privada a EF Design, sozinha, não fixa o runtime nos consumidores do projeto.

Detalhes e limites: [authentication.md](authentication.md).

## 008 — Catálogo incremental por categorias

A Fase 3A cria somente Categories e seu CRUD administrativo com inativação. Não há exclusão física, produtos antecipados nem dados de demonstração inseridos no banco real. Nomes são únicos após trim, NFC e maiúsculas, inclusive quando inativos. O índice único garante a regra nas gravações concorrentes. Atualizações são transacionais e a última edição aplicada prevalece; ETag não faz parte deste incremento.

catalog.manage é exclusiva de Administrator e independente de users.manage. A sessão e as regras da Fase 2 são reaproveitadas. Datas de categoria usam UTC com milissegundos para manter respostas de escrita e leitura consistentes. Detalhes: [categories.md](categories.md).

## 009 — Produtos e disponibilidade derivada

Produtos têm categoria obrigatória, preço decimal validado antes do numeric(8,2) e nome único dentro da categoria. Ativação e disponibilidade manual são independentes. A API calcula IsAvailableForSale combinando produto ativo, categoria ativa e disponibilidade manual; não considera estoque ou pedidos ainda inexistentes.

Novos vínculos exigem categoria ativa, mas uma edição pode manter a categoria original inativa. Não há exclusão física. Imagens são links HTTPS opcionais, com prévia e tratamento de falha; uploads e armazenamento de mídia ficam para incremento próprio. Detalhes: [products.md](products.md).

## 010 — Ingredientes com unidade-base estável

A Fase 3C usa kg, l e un. Custo é por unidade-base, com quatro casas decimais; mínimo usa três. Não há conversão automática de pacote/preço de compra. A unidade fica fixa após salvar, evitando reinterpretar custos e futuras quantidades de receita. Uma conversão futura deverá ser uma operação explícita que considere todos os vínculos.

Nome único inclui registros inativos. Fornecedor é texto opcional; não se cria tabela própria neste incremento. Custo zero é aceito explicitamente e destacado na interface para revisão. O mínimo é configuração, sem saldo ou alertas fictícios. Estoque transacional e CMV seguem para a fase 6. Regras, limites e testes: [ingredients.md](ingredients.md).

## 011 — Ficha técnica por produto, com composição transacional

A Fase 3D usa uma ficha por produto, rendimento inteiro em unidades do produto e quantidades totais na unidade-base do ingrediente. RecipeItems não aceita ingredientes repetidos. A posição reflete a ordem enviada. O contrato PUT representa a ficha inteira; criação e edição compartilham validação e transação, serializadas por bloqueio do produto. Identidade e CreatedAt são preservados nas edições; a última gravação válida prevalece.

Novos vínculos exigem ingredientes ativos. Um ingrediente inativo já vinculado pode ser mantido, ter sua quantidade ajustada ou ser removido. O backend retorna o status atual e a interface exibe aviso. Isso não altera a disponibilidade comercial do produto nem gera movimentos de estoque. Produto inativo pode ter sua ficha mantida. Não se antecipa cálculo de CMV, perdas, conversão de unidades ou histórico de versões. Detalhes: [recipes.md](recipes.md).
