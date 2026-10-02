# Made in Minas

Sistema de gestão e pedidos da hamburgueria Made in Minas, desenvolvido por fases.

**Fases 1 a 5 implementadas nos incrementos descritos no roteiro.** Expedição e impressão pelo navegador foram integradas pelo [PR #7](https://github.com/Pauliinrodrigues/MadeinMinasApp/pull/7), após aceite e checks aprovados; impressão automática e validação física dependem do equipamento. O estoque manual (6A) foi validado e integrado pelo [PR #8](https://github.com/Pauliinrodrigues/MadeinMinasApp/pull/8), commit `71f7c41`. O [consumo de estoque por pedidos — Fase 6B](docs/order-stock.md) está implementado e testado, preservado no commit local `98ecb4a`. O incremento atual é [CMV teórico por produto — Fase 6C](docs/cmv.md), na branch `feat/product-cmv`, derivada da 6B ainda não integrada. O usuário autorizou a continuidade; o aceite manual e a publicação dos incrementos continuam pendentes.

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

**Ingredientes:** abra **Ingredientes → Novo ingrediente**. Cadastre unidade-base (kg, L ou un), custo por unidade, mínimo desejado, fornecedor e status. A unidade fica fixa após salvar. Saldo e movimentos manuais estão em Ingredientes → Estoque; confirmação de pedidos passa a consumir a ficha técnica na Fase 6B. Contratos, migration, testes e arquivos: [Fase 3C](docs/ingredients.md).

**Fichas técnicas:** abra **Produtos → Ficha técnica**. Informe rendimento, ingredientes, quantidades totais na unidade-base e instruções de preparo. A gravação preserva a composição inteira e alerta sobre ingredientes inativos. Contratos, limites, testes e arquivos: [Fase 3D](docs/recipes.md).

**Clientes e endereços:** faça novo login como administrador ou atendente e abra **Clientes**. Cadastre nome e telefone com DDD; depois abra **Gerenciar endereços** para manter os locais de entrega. Busca por nome/telefone, paginação, inativação e reativação estão disponíveis. Contratos, migration e roteiro de aceite: [Fase 4A](docs/customers.md).

**Carrinho e pedidos:** abra **Carrinho** como administrador ou atendente, selecione cliente, retirada/entrega, endereço e taxa quando houver entrega, e produtos disponíveis. Ajuste quantidades/observações e clique em **Revisar carrinho**. Confira os dados e o total calculado pela API; depois use **Registrar pedido**. O registro cria um pedido **Novo**, consultável em **Pedidos**, com itens e valores preservados. Confirmar não registra pagamento. Cancelamento exige motivo; depois da confirmação, somente o administrador pode cancelar. Regras e roteiro de aceite: [Fase 4C](docs/orders.md).

O carrinho ainda é temporário e é descartado ao sair da tela. Se o registro tiver resultado desconhecido, use **Tentar registro novamente**; a mesma tentativa retorna o pedido já salvo ou conclui a criação. Se sair ou perder a sessão, consulte **Pedidos** antes de montar outro carrinho.

**Pagamentos:** faça novo login como administrador ou atendente e abra **Pedidos → Abrir → Pagamentos**. Defina dinheiro, Pix, crédito ou débito para o valor integral. Use **Registrar recebimento** somente após conferir o recebimento; em dinheiro, informe o valor entregue e confira o troco retornado pela API. Uma intenção pendente pode ser cancelada com motivo. Somente administrador registra uma devolução integral já realizada. Resolva pagamentos pendentes/recebidos antes de cancelar o pedido. Não há cobrança ou estorno bancário automático. Contratos, limites e roteiro de aceite: [Fase 4D](docs/payments.md).

## Verificar a fase

**Cozinha/KDS:** faça novo login como administrador ou cozinha e abra **Cozinha**. Pedidos confirmados pelo atendimento entram na fila. Confira quantidades e observações, use **Iniciar preparo**, confirme a etapa e depois **Marcar pronto**. O painel atualiza a cada 10 segundos; falha de conexão bloqueia ações até uma consulta bem-sucedida. Prontos permanecem no painel até a etapa de expedição. O administrador pode cancelar pedidos em preparação/prontos pela tela de Pedidos, após resolver o pagamento. Regras, arquivos e roteiro de aceite: [Fase 5A](docs/kitchen.md).

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

O GitHub Actions executa as verificações em pushes para `master` e pull requests. O PR #7 passou nos checks de backend e frontend antes da integração. Detalhes e limites: [manutenção técnica](docs/maintenance.md). O incremento atual está em [estoque manual](docs/stock.md), com validação local registrada em [validation.md](docs/validation.md).

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
- [Carrinho da equipe](docs/cart.md)
- [Pedidos manuais](docs/orders.md)
- [Pagamentos manuais](docs/payments.md)
- [Cozinha/KDS](docs/kitchen.md)
- [Estoque manual de ingredientes](docs/stock.md)
- [Arquitetura](docs/architecture.md)
- [Autenticação, administrador inicial e testes](docs/authentication.md)
- [Gestão de funcionários e permissões](docs/users.md)
- [Modelo conceitual](docs/database.md)
- [Contrato da API](docs/api.md)
- [Decisões técnicas](docs/decisions.md)
- [Regras de negócio](docs/business-rules.md)
- [Etapas](docs/roadmap.md)

**Expedição e impressão (5B/5C):** faça novo login como administrador ou expedição e abra **Expedição**. Confira destino, produtos e pagamento antes de avançar. Retirada segue direto de Pronto para Entregue; finalize após recebimento integral registrado pelo atendimento. As comandas de produção e expedição abrem prévia com impressão explícita pelo navegador (58/80 mm ou A4). Impressão automática e acerto de margens/corte dependem do equipamento real. Roteiro: [expedição e impressão](docs/dispatch-printing.md).
