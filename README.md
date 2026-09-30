# Made in Minas

Sistema de gestão e pedidos da hamburgueria Made in Minas, desenvolvido por fases.

**Fases 1, 2 e 3 implementadas e fichas técnicas validadas.** A Fase 4A adiciona clientes e endereços na API e no frontend, em branch própria e aguardando aceite manual. Pedidos, pagamentos, estoque/CMV e atendimento por IA seguem para os próximos incrementos.

## Estrutura

- `backend/MadeInMinas.Api`: ASP.NET Core Web API e EF Core.
- `frontend/made-in-minas`: Ionic/Angular.
- `database`: preparação do PostgreSQL.
- `docs`: arquitetura, regras, API e etapas.
- `scripts`: verificação HTTP da fundação.

O projeto de console original foi reorganizado na API; abra `MadeinMinasApp.sln` no Rider.

## Pré-requisitos

- .NET SDK 10.0.401 (ou patch da mesma faixa, conforme `global.json`), com runtime 10.0.12.
- PostgreSQL 17 disponível localmente.
- Node 24.21.0 recomendado (`.nvmrc` no frontend). Também são aceitos Node 22.22.3+ na linha 22 e Node 24.15+ na linha 24.
- npm 10+ e Git.

Use `node --version` antes de instalar. O Node 24.19.0 fornecido pelo Rider é compatível; a instalação global antiga 22.16.0 não atende ao Angular 22. Se o terminal mostrar 22.16.0, selecione um Node compatível nas configurações do Rider/terminal. Os comandos npm abaixo usam o Angular CLI local; não precisam de Ionic CLI global.

Dependências diretas fixadas: Angular 22.2.1, Angular CLI/build 22.2.0, TypeScript 6.0.3, Ionic 9.0.5, EF Core Design 10.0.12 e Npgsql EF 10.0.3. Preserve `package-lock.json` e `packages.lock.json`.

## Iniciar o backend

Execute na raiz:

```powershell
dotnet restore MadeinMinasApp.sln --locked-mode
dotnet tool restore
powershell -NoProfile -File scripts/Initialize-Authentication.ps1
dotnet build MadeinMinasApp.sln --no-restore
dotnet run --project backend/MadeInMinas.Api --launch-profile http
```

API em http://localhost:5080. O perfil `http` usa Development.
OpenAPI JSON: http://localhost:5080/openapi/v1.json (somente Development).

A API inicia sem banco configurado. `/health/live` retorna 200; `/health/ready` retorna 503 até a conexão funcionar. Isso permite preparar o frontend sem ocultar a pendência do banco.

Siga [database/README.md](database/README.md) para criar o banco e configurar a conexão. Reinicie a API depois de alterar os segredos.

## Iniciar o frontend

Em outro terminal:

```powershell
cd frontend/made-in-minas
npm.cmd ci
npm.cmd start
```

Abra http://localhost:8101. A página consulta `http://localhost:5080/api/system/status`. Com a API desligada, mostra a falha de conexão e permite tentar novamente. A porta 8101 evita conflito com outro projeto local que já utiliza 8100.

Para entrar, acesse http://localhost:8101/entrar ou clique em **Área da equipe**. Use o administrador criado pelo [comando interativo](docs/authentication.md#administrador-inicial); não existe senha padrão. A sessão dura até 15 minutos e atualizar a página exige novo login. Administradores acessam cadastro, edição e ativação/inativação de funcionários. Todos os perfis podem alterar a própria senha.

Detalhes e arquivos da interface: [Fase 2C](docs/staff-frontend.md). Na pasta do frontend, execute `npm.cmd run build` e `npm.cmd run test:e2e` para validar. Os testes de navegador usam Edge e respostas de API simuladas, sem alterar contas reais.

**Categorias:** entre como administrador e abra **Categorias** no menu. Cadastre nome, descrição opcional, ordem e status. A listagem oferece busca, filtros e ativação/inativação. Entre novamente se a sessão começou antes da atualização. Contratos, migration e arquivos: [Fase 3A](docs/categories.md).

**Produtos:** abra **Produtos → Novo produto**, escolha uma categoria ativa e informe o preço (exemplo: 29,90). Nome, descrição, imagem por link HTTPS, status e disponibilidade podem ser editados. A disponibilidade considera os estados do produto e da categoria. Contratos, migration e arquivos: [Fase 3B](docs/products.md).

**Ingredientes:** abra **Ingredientes → Novo ingrediente**. Cadastre unidade-base (kg, L ou un), custo por unidade, mínimo desejado, fornecedor e status. A unidade fica fixa após salvar. Saldo e movimentações de estoque virão na fase 6. Contratos, migration, testes e arquivos: [Fase 3C](docs/ingredients.md).

**Fichas técnicas:** abra **Produtos → Ficha técnica**. Informe rendimento, ingredientes, quantidades totais na unidade-base e instruções de preparo. A gravação preserva a composição inteira e alerta sobre ingredientes inativos. Contratos, limites, testes e arquivos: [Fase 3D](docs/recipes.md).

**Clientes e endereços:** faça novo login como administrador ou atendente e abra **Clientes**. Cadastre nome e telefone com DDD; depois abra **Gerenciar endereços** para manter os locais de entrega. Busca por nome/telefone, paginação, inativação e reativação estão disponíveis. Contratos, migration e roteiro de aceite: [Fase 4A](docs/customers.md).

## Verificar a fase

Com os dois processos iniciados:

```powershell
powershell -NoProfile -File scripts/Test-Foundation.ps1
```

Depois de configurar o banco:

```powershell
powershell -NoProfile -File scripts/Test-Foundation.ps1 -ExpectDatabaseReady
```

Build de produção do frontend:

```powershell
cd frontend/made-in-minas
npm.cmd run build
```

Saída em `frontend/made-in-minas/www/browser`. Consulte [validação](docs/validation.md) para os cenários e resultados da implementação.

## Qualidade e manutenção

Trabalhamos com uma branch por incremento e integração na `master` por pull request, após testes e validação manual. Consulte o [fluxo de Git](docs/git-workflow.md) para criar branches, publicar alterações e continuar em outra máquina.

O fluxo local completo está em `scripts/Test-Quality.ps1`: formatação, testes do backend em banco isolado, lint, build e testes de navegador. Na primeira execução, instale as dependências do frontend com `npm.cmd ci`.

```powershell
powershell -NoProfile -File scripts/Test-Quality.ps1
```

O GitHub Actions executa as verificações em pushes para `master` e pull requests. O PR #1 passou com 135 testes do backend e 106 do frontend antes da integração. Detalhes e limites: [manutenção técnica](docs/maintenance.md). O incremento atual está em [clientes e endereços](docs/customers.md).

## Configuração e segurança

- Connection string em User Secrets (Development) ou `ConnectionStrings__DefaultConnection` (ambiente).
- `appsettings.json` não contém senha. Não inclua segredos em nenhum arquivo versionado, inclusive `appsettings.Development.json`.
- Nunca colocar chaves de IA ou credenciais no frontend.
- CORS de desenvolvimento permite apenas `http://localhost:8101`, métodos GET, POST e PUT.
- Em produção, o frontend usa `/api`: hospedar no mesmo domínio e encaminhar essa rota para o backend.
- Configurar `AllowedHosts`, HTTPS e, se necessário, `Cors__AllowedOrigins__0` para o domínio real.
- Há redirecionamento HTTPS/HSTS fora de Development. A hospedagem deve fornecer HTTPS; reverse proxy e forwarded headers precisarão ser configurados conforme a infraestrutura escolhida.
- A base ainda não é uma entrega de produção. A autenticação do backend está descrita em [docs/authentication.md](docs/authentication.md).
- A manutenção instalou o SDK 10.0.401 e o runtime 10.0.12. O instalador Microsoft retornou 3010 (sucesso com reinicialização pendente): salve seu trabalho e reinicie o Windows quando puder para concluir as substituições de arquivos em uso.

## Documentação

- [Fluxo de Git e branches](docs/git-workflow.md)
- [Clientes e endereços](docs/customers.md)
- [Arquitetura](docs/architecture.md)
- [Autenticação, administrador inicial e testes](docs/authentication.md)
- [Gestão de funcionários e permissões](docs/users.md)
- [Modelo conceitual](docs/database.md)
- [Contrato da API](docs/api.md)
- [Decisões técnicas](docs/decisions.md)
- [Regras de negócio](docs/business-rules.md)
- [Etapas](docs/roadmap.md)
