# Made in Minas

Sistema de gestão e pedidos da hamburgueria Made in Minas, desenvolvido por fases.

**Rodada de ajustes:** [ajustes operacionais e de usabilidade](docs/operational-usability.md), desenvolvidos na branch `feat/operational-usability` e publicados para revisão no [PR #19](https://github.com/Pauliinrodrigues/MadeinMinasApp/pull/19). Inclui disponibilidade pública por ficha/estoque, carrinhos recuperáveis, continuidade do login, fila de pedidos atualizada, organização das telas, [administração de regiões e taxas](docs/delivery-settings.md), [filtros por origem e pagamento](docs/order-filters.md), [central de reposição](docs/stock-replenishment.md), histórico de múltiplos acompanhamentos e fila/conversa lado a lado. O fechamento inclui avisos de cadastro incompleto, atalhos de filas e melhorias na cozinha, expedição e detalhe do pedido. A entrega pública 8F já está integrada pelo [PR #18](https://github.com/Pauliinrodrigues/MadeinMinasApp/pull/18), master `643aa0f`. Cobertura, contagens físicas e fotos dependem de dados reais da hamburgueria; a regra atual do pedido público é entrega por R$ 5,00 e retirada grátis.

**Reposição:** como administrador, abra **Gestão → Reposição** em http://localhost:8101/equipe/reposicao. Busque ingrediente/fornecedor e confira saldos, mínimos e falta até o mínimo. A central consulta os dados; contagem e entrada continuam exigindo revisão e confirmação na tela de estoque. Inativos são incluídos apenas quando solicitado. Ao voltar à central, a consulta é refeita na fila inicial de atenção. Regras, arquivos e roteiro: [stock-replenishment.md](docs/stock-replenishment.md).

**Fases 1 a 7 implementadas no escopo inicial descrito no roteiro.** Expedição e impressão pelo navegador foram integradas pelo [PR #7](https://github.com/Pauliinrodrigues/MadeinMinasApp/pull/7); impressão automática e validação física dependem do equipamento. O estoque manual (6A) foi integrado pelo [PR #8](https://github.com/Pauliinrodrigues/MadeinMinasApp/pull/8). Consumo de estoque (6B), CMV teórico (6C) e dashboard diário (7A) foram integrados pelos PRs [#9](https://github.com/Pauliinrodrigues/MadeinMinasApp/pull/9), [#10](https://github.com/Pauliinrodrigues/MadeinMinasApp/pull/10) e [#11](https://github.com/Pauliinrodrigues/MadeinMinasApp/pull/11), após os checks de backend e frontend.

[Relatórios por período — 7B](docs/period-reports.md) completa as consultas iniciais de vendas e recebimentos, integrado pelo [PR #12](https://github.com/Pauliinrodrigues/MadeinMinasApp/pull/12), commit `5adcd03`. Os incrementos públicos 8A–8E foram integrados por squash na master pelos PRs [#13](https://github.com/Pauliinrodrigues/MadeinMinasApp/pull/13), [#14](https://github.com/Pauliinrodrigues/MadeinMinasApp/pull/14), [#15](https://github.com/Pauliinrodrigues/MadeinMinasApp/pull/15), [#16](https://github.com/Pauliinrodrigues/MadeinMinasApp/pull/16) e [#17](https://github.com/Pauliinrodrigues/MadeinMinasApp/pull/17). O responsável autorizou esta integração com os testes locais aprovados e checks remotos ainda pendentes/cancelados durante o incidente do GitHub Actions; a exceção está registrada em [validação](docs/validation.md). A validação final da master 0b7cc92 passou no GitHub (473 testes backend e 488 de navegador). A [entrega pelo site — 8F](docs/public-delivery.md) foi integrada no PR #18, com cobertura e taxas configuráveis; a ativação depende dos valores reais da operação.

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

**Pedido público (8A–8F):** abra http://localhost:8101/pedido, adicione produtos e acesse **Ver carrinho → Continuar pedido**. Entrega começa selecionada, com **Corinto/MG · Todos os bairros** e taxa fixa de **R$ 5,00**. Informe contato, seu bairro, rua e número; revise taxa/total e envie. Retirada é **grátis**, na **Rua Esperança, 68 — Clarindo de Paiva, Corinto–MG**; aguarde confirmação antes de buscar. O pedido chega Novo para a equipe confirmar; não há cobrança online. **Acompanhar meu pedido** mostra status e horários. O administrador mantém a cobertura em **Cadastros → Regiões de entrega**, com a opção **Todos os bairros desta cidade**; a taxa fixa não é editável por região. Sem regiões ativas, entrega permanece indisponível. Aplique as migrations até `AddDeliverySettings` conforme [database/README.md](database/README.md) e faça novo login para carregar a permissão. Configuração inicial é lida até o primeiro salvamento. Regras: [entrega pelo site](docs/public-delivery.md) e [regiões e taxas](docs/delivery-settings.md).

A montagem inicial do carrinho é temporária. Ao enviar, a tentativa fica guardada na sessão da aba para recuperar respostas perdidas: use **Conferir envio**, inclusive após atualizar a página, antes de montar outro pedido. Sucesso substitui os dados pessoais por um comprovante mínimo. Fechar a aba pode perder a recuperação; nesse caso, confira com o atendimento. A migration `AddPublicOrders` é necessária neste fluxo; siga [database/README.md](database/README.md) antes de executar em outra máquina.

O acompanhamento usa uma credencial exclusiva do pedido, válida por sete dias desde a criação, guardada na sessão da aba. Montar outro pedido remove esse acesso; fechar a aba pode perdê-lo. Comprovantes antigos orientam atendimento. A 8D não exige migration. A preservação das chaves do servidor em outra máquina/hospedagem está descrita em [acompanhamento](docs/public-order-tracking.md#banco-chaves-e-execução).

**Atendimento humano (8E):** no site, clique **Falar com atendente** ou abra http://localhost:8101/pedido/atendimento. Informe nome e mensagem; mantenha a aba para acompanhar a resposta. Na área da equipe, administrador/atendente usam **Atendimentos → Assumir atendimento → Enviar resposta → Encerrar atendimento**. Faça novo login para carregar a permissão nova. É necessário aplicar `AddHumanChat` conforme [database/README.md](database/README.md). Mensagens não alteram pedidos/pagamentos, e não há IA nesta etapa. Recuperação de envio, limites e roteiro: [human-chat.md](docs/human-chat.md).

Para entrar, acesse http://localhost:8101/entrar ou clique em **Área da equipe**. Use o administrador criado pelo [comando interativo](docs/authentication.md#administrador-inicial); não existe senha padrão. A sessão dura até 15 minutos e atualizar a página exige novo login. Administradores acessam cadastro, edição e ativação/inativação de funcionários. Todos os perfis podem alterar a própria senha.

Detalhes e arquivos da interface: [Fase 2C](docs/staff-frontend.md). Na pasta do frontend, execute `npm.cmd run build` e `npm.cmd run test:e2e` para validar. Os testes de navegador usam Edge e respostas de API simuladas, sem alterar contas reais.

**Categorias:** entre como administrador e abra **Categorias** no menu. Cadastre nome, descrição opcional, ordem e status. A listagem oferece busca, filtros e ativação/inativação. Entre novamente se a sessão começou antes da atualização. Contratos, migration e arquivos: [Fase 3A](docs/categories.md).

**Produtos:** abra **Produtos → Novo produto**, escolha uma categoria ativa e informe o preço (exemplo: 29,90). Nome, descrição, imagem por link HTTPS, status e disponibilidade podem ser editados. A disponibilidade considera os estados do produto e da categoria. Contratos, migration e arquivos: [Fase 3B](docs/products.md).

**Ingredientes:** abra **Ingredientes → Novo ingrediente**. Cadastre unidade-base (kg, L ou un), custo por unidade, mínimo desejado, fornecedor e status. A unidade fica fixa após salvar. Saldo e movimentos manuais estão em Ingredientes → Estoque; confirmação de pedidos passa a consumir a ficha técnica na Fase 6B. Contratos, migration, testes e arquivos: [Fase 3C](docs/ingredients.md).

**Fichas técnicas:** abra **Produtos → Ficha técnica**. Informe rendimento, ingredientes, quantidades totais na unidade-base e instruções de preparo. A gravação preserva a composição inteira e alerta sobre ingredientes inativos. Contratos, limites, testes e arquivos: [Fase 3D](docs/recipes.md).

**Clientes e endereços:** faça novo login como administrador ou atendente e abra **Clientes**. Cadastre nome e telefone com DDD; depois abra **Gerenciar endereços** para manter os locais de entrega. Busca por nome/telefone, paginação, inativação e reativação estão disponíveis. Contratos, migration e roteiro de aceite: [Fase 4A](docs/customers.md).

**Carrinho e pedidos:** abra **Carrinho** como administrador ou atendente, selecione cliente, retirada/entrega, endereço e taxa quando houver entrega, e produtos disponíveis. Ajuste quantidades/observações e clique em **Revisar carrinho**. Confira os dados e o total calculado pela API; depois use **Registrar pedido**. O registro cria um pedido **Novo**, consultável em **Pedidos**, com itens e valores preservados. Confirmar não registra pagamento. Cancelamento exige motivo; depois da confirmação, somente o administrador pode cancelar. Regras e roteiro de aceite: [Fase 4C](docs/orders.md).

O carrinho da equipe é recuperado na sessão da aba após navegação, reload e novo login do mesmo funcionário. Se o registro tiver resultado desconhecido, use **Tentar registro novamente**; a mesma tentativa retorna o pedido já salvo ou conclui a criação. Fechar a aba pode perder a recuperação; nesse caso, consulte **Pedidos** antes de montar outro carrinho. Em **Pedidos**, combine origem e situação do pagamento com os demais filtros; **A receber** mostra pedidos ainda em andamento sem recebimento atual. Alterações nos campos só são aplicadas ao clicar em **Buscar pedidos**.

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

O GitHub Actions executa as verificações em pushes para `master` e pull requests. Cada integração exige os checks de backend e frontend aprovados para o commit final do PR. Detalhes e limites: [manutenção técnica](docs/maintenance.md). O incremento atual está em [checkout público](docs/public-checkout.md), com os resultados registrados em [validation.md](docs/validation.md).

## Configuração e segurança

- Connection string em User Secrets (Development) ou `ConnectionStrings__DefaultConnection` (ambiente).
- `appsettings.json` não contém senha. Não inclua segredos em nenhum arquivo versionado, inclusive `appsettings.Development.json`.
- Nunca colocar chaves de IA ou credenciais no frontend.
- CORS de desenvolvimento permite apenas `http://localhost:8101`, métodos GET, POST e PUT.
- Em produção, o frontend usa `/api`: hospedar no mesmo domínio e encaminhar essa rota para o backend.
- Fotos próprias: em **Produtos → Editar → Foto do produto**, escolha JPG, PNG ou WebP e salve. Os arquivos ficam em `backend/MadeInMinas.Api/App_Data/product-images`, fora do Git; configure `ProductImages__StoragePath` para um volume persistente e inclua-o no backup junto com o banco. [Regras de imagens](docs/products.md#imagens).
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
- [Consumo de estoque por pedidos](docs/order-stock.md)
- [CMV teórico por produto](docs/cmv.md)
- [Dashboard do dia](docs/daily-dashboard.md)
- [Relatórios por período](docs/period-reports.md)
- [Cardápio público](docs/public-menu.md)
- [Carrinho público](docs/public-cart.md)
- [Checkout público para retirada](docs/public-checkout.md)
- [Arquitetura](docs/architecture.md)
- [Autenticação, administrador inicial e testes](docs/authentication.md)
- [Gestão de funcionários e permissões](docs/users.md)
- [Modelo conceitual](docs/database.md)
- [Contrato da API](docs/api.md)
- [Decisões técnicas](docs/decisions.md)
- [Regras de negócio](docs/business-rules.md)
- [Etapas](docs/roadmap.md)

**Expedição e impressão (5B/5C):** faça novo login como administrador ou expedição e abra **Expedição**. Confira destino, produtos e pagamento antes de avançar. Retirada segue direto de Pronto para Entregue; finalize após recebimento integral registrado pelo atendimento. As comandas de produção e expedição abrem prévia com impressão explícita pelo navegador (58/80 mm ou A4). Impressão automática e acerto de margens/corte dependem do equipamento real. Roteiro: [expedição e impressão](docs/dispatch-printing.md).

**Dashboard (7A):** faça novo login como administrador e abra **Dashboard** no menu. Confira pedidos, valor confirmado, recebimentos/estornos, filas atuais, tempo de produção e produtos mais pedidos. O dia segue o horário de Brasília; use **Atualizar painel** para consultar novamente. Consultas de vários dias estão em **Relatórios (7B)**. Regras e roteiro: [dashboard do dia](docs/daily-dashboard.md).

**Relatórios (7B):** faça novo login como administrador e abra **Relatórios**. A primeira consulta traz os últimos sete dias; selecione início e fim e use **Gerar relatório** para consultar até 90 dias. Confira resumo, detalhamento diário, formas de pagamento e produtos mais pedidos. Datas seguem Brasília e os valores distinguem confirmação, recebimento e estorno. Regras, arquivos e roteiro: [relatórios por período](docs/period-reports.md).
