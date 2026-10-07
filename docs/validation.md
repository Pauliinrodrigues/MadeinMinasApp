# Validação da Fase 1

Executada em 29/09/2026, Windows, SDK .NET 10.0.300 e Node 24.19.0.

## Resultados

| Verificação | Resultado |
| --- | --- |
| dotnet restore / build | Sucesso; zero erros e avisos de compilação |
| dotnet tool restore | dotnet-ef 10.0.12 restaurado |
| dotnet ef dbcontext info | AppDbContext reconhecido com provedor Npgsql |
| npm install / auditoria | Dependências instaladas; zero vulnerabilidades reportadas na execução |
| npm run build | Sucesso; bundle inicial de aproximadamente 452 kB |
| npm start | Disponível em localhost:8101 |
| Liveness sem banco | 200 / Healthy |
| Readiness sem configuração | 503 / Unhealthy |
| Readiness com PostgreSQL autenticado | 200 / Healthy |
| Readiness após desligar PostgreSQL | 503 / Unhealthy; liveness continua 200 |
| CORS | Origem 8101 aceita; origem não autorizada sem cabeçalho de permissão |
| Erro de rota inexistente | 404 application/problem+json |
| OpenAPI Development | Documento gerado contendo /api/system/status |
| Navegador Edge headless | Tela renderizada, chamada real da API com CORS |
| Desktop 1280x900 e celular 390x844 | Sem estouro horizontal na largura mobile |
| Falha da API simulada no navegador | Mensagem de indisponibilidade e botão de nova tentativa |
| Recuperação de conexão | Nova tentativa real retorna ao estado conectado |
| Exceções JavaScript | Nenhuma nos cenários verificados |

O comando EF pode emitir aviso de ausência de IEntityTypeConfiguration: ainda não há entidades na Fase 1.

## Isolamento dos testes

O PostgreSQL existente em localhost:5432 exigiu senha não disponível nesta sessão e não foi alterado.

A verificação positiva utilizou uma instância temporária PostgreSQL 17 em 127.0.0.1:55432, banco made_in_minas_validation, senha aleatória e autenticação SCRAM. Uma segunda API em 5081 recebeu a conexão por variável de ambiente. O servidor temporário foi desligado após os testes; sua credencial foi removida. Nenhuma tabela de negócio foi criada.

Os testes de navegador utilizaram Playwright apenas em .local, sem adicionar dependência ao produto. Capturas de desktop e celular ficaram nessa pasta ignorada pelo Git.

## Ajustes confirmados durante a execução

- Ionic 9 usa exports específicos como @ionic/angular/ion-app e @ionic/angular/provide; o caminho /standalone de versões anteriores não existe nesse pacote.
- Porta 8100 ocupada por outro projeto: esta aplicação usa 8101.
- O sandbox bloqueou leitura de diretório pai no servidor Angular. O mesmo comando foi autorizado fora do sandbox e iniciou corretamente; nenhuma alteração de permissões da máquina foi feita.
- O teste HTTP do frontend usa Accept: text/html, conforme esperado pelo servidor de desenvolvimento.

## Reproduzir

Siga o README para iniciar a API e o frontend. Execute:

```powershell
powershell -NoProfile -File scripts/Test-Foundation.ps1
```

Após configurar sua conexão permanente:

```powershell
powershell -NoProfile -File scripts/Test-Foundation.ps1 -ExpectDatabaseReady
```

No navegador, confira a mensagem de conexão, desligue somente a API e recarregue. A página deve mostrar indisponibilidade. Reinicie a API e clique em Tentar novamente; a conexão deve se recuperar.

## Conexão permanente validada

A conexão permanente foi configurada pelo assistente local de [database/README.md](../database/README.md), com a senha digitada pelo responsável no terminal. A connection string foi confirmada em User Secrets sem exibir seu conteúdo.

Após reiniciar a API no perfil Development, `scripts/Test-Foundation.ps1 -ExpectDatabaseReady` passou em todas as verificações: liveness e readiness retornaram 200/Healthy, e contrato HTTP, CORS, ProblemDetails, OpenAPI e frontend responderam conforme esperado. A pendência de conexão permanente está resolvida.

## Limites

Na Fase 1 não foram implementados autenticação, entidades ou migrations de negócio. A evolução está registrada abaixo. Nenhum commit foi criado e o índice Git preexistente foi preservado.

## Fase 2A — Autenticação do backend

Validada em 29/09/2026:

- Build Release da solução: zero erros e zero avisos.
- Restore com --locked-mode: sucesso para API e testes.
- 24 testes de integração aprovados em PostgreSQL temporário com SCRAM, sem utilizar o banco da aplicação.
- Cenários: validação de entrada, login, proteção de endpoints, perfis, tokens expirados/assinatura/emissor/audiência/algoritmo inválidos, inativação e troca de perfil, logout, bloqueio e recuperação da conta, tentativas concorrentes, rate limiting e bootstrap.
- Migration InitialAccessControl revisada e aplicada ao PostgreSQL de desenvolvimento; cria Users, Roles, índices e quatro perfis. Nenhuma conta com senha padrão é inserida.
- EF confirmou ausência de alterações de modelo sem migration.
- API reiniciada; /health/live e /health/ready retornaram 200/Healthy.
- Smoke test da Fase 1 passou integralmente após a atualização.
- /api/auth/me e /api/roles retornaram 401 sem credenciais na API em execução.

A primeira migration em banco vazio pode registrar uma tentativa de consulta a __EFMigrationsHistory antes de criá-la; o comando terminou com sucesso e a migration aplicada foi confirmada por migrations list.

A chave JWT local foi gerada em User Secrets sem exibir seu conteúdo. O cadastro do administrador real exige nome, login e senha no comando interativo descrito em [authentication.md](authentication.md). O bootstrap foi validado automaticamente no banco de testes.

A tentativa de abrir o bootstrap pela ferramenta automatizada do Rider recebeu entrada redirecionada e foi recusada. O comando foi ajustado para retornar uma orientação clara com código 1 nesse caso; esse comportamento e o build foram verificados. O administrador real ainda não foi criado por esta implementação.

A tela de login e a administração de usuários ainda não fazem parte deste incremento.

## Fase 2B — Gestão de funcionários

Validada em 29/09/2026:

- 52 testes de integração aprovados: os 24 de autenticação e 28 novos casos de gestão de funcionários.
- Verificados cadastro/leitura sem campos sensíveis, permissões para os quatro perfis e anônimos, validação, paginação, filtros e unicidade de login.
- Verificados inativação/reativação, mudança de perfil/login, preservação de sessão em edição apenas de nome, troca/reset de senha e revogação dos tokens antigos.
- Testadas duas criações simultâneas com o mesmo login e duas inativações simultâneas de administradores; preservadas unicidade e existência de administrador ativo.
- Testada revalidação do autor dentro da transação após revogação anterior da sessão.
- Testes executados em PostgreSQL temporário autenticado; contas do banco de desenvolvimento não foram alteradas.
- Build Release: zero erros e zero avisos. Restore com --locked-mode passou.
- EF confirmou que não há alteração de modelo pendente: nenhuma migration nova foi criada ou aplicada.
- API atualizada na porta 5080; health checks retornaram 200/Healthy, e o smoke test da Fase 1 passou.
- /api/users respondeu 401 sem token; preflight CORS permitiu PUT somente para a origem configurada; novas rotas confirmadas no OpenAPI.

O comando de testes agora usa .local/test-build, evitando conflito com os binários da API em execução. Os dois grupos de testes compartilham uma coleção sequencial para que a limpeza de usuários do banco isolado não interfira em outra suíte.

Escopo e contratos: [users.md](users.md). A interface de login e administração será o próximo incremento (2C).

## Fase 2C — Interface da equipe

Validada em 29/09/2026:

- Build de produção Angular aprovado, sem erros ou avisos; bundle inicial de aproximadamente 463 kB.
- 28 testes Playwright aprovados: 14 cenários em desktop e os mesmos 14 em viewport móvel, usando Edge/Chromium.
- Cobertura: rotas protegidas, credenciais inválidas, limite de tentativas, perfis de atendente/cozinha/expedição, sessão somente em memória e novo login após recarregar.
- Verificados listagem, filtros, paginação, cadastro com confirmação de senha, login duplicado, edição, confirmação/cancelamento de inativação, reativação, reset e troca da própria senha.
- Verificados erro do último administrador, recuperação após indisponibilidade, 403 sem encerrar sessão, 401 com remoção de acesso, expiração automática, alteração do próprio login e logout com falha de conexão.
- Testes de interface usam respostas simuladas conforme os contratos documentados da API; não criam, editam ou removem contas reais. Não equivalem a um teste completo de login autenticado pelo navegador contra o PostgreSQL real.
- Smoke test da fundação aprovado com PostgreSQL permanente disponível: liveness/readiness Healthy, CORS, contrato público, ProblemDetails, OpenAPI e frontend.
- Página pública verificada no navegador contra a API real em desktop e celular, incluindo falha de conexão e recuperação. Nenhuma exceção JavaScript nesses testes.
- Capturas locais de login desktop/móvel em .local; ajuste da classe ion-page validado após trocar o outlet para destruir páginas entre navegações.
- Nenhuma alteração no backend, migration ou credencial real. Os 52 testes de integração do backend permanecem os validados na Fase 2B e não foram reexecutados neste incremento.

Playwright foi adicionado como dependência de desenvolvimento fixada, com configuração e testes versionáveis. Comandos de reprodução, arquivos alterados e limites da sessão: [staff-frontend.md](staff-frontend.md). O aceite com a conta real do responsável permanece como validação manual antes da Fase 3.

## Fase 3A — Categorias

O usuário validou o acesso à área da equipe e autorizou o próximo incremento.

Validada em 29/09/2026:

- Build Release da solução: zero erros e zero avisos. Build de produção Angular aprovado sem avisos; bundle inicial de aproximadamente 463 kB.
- 73 testes de backend aprovados em PostgreSQL temporário: 52 existentes e 21 casos novos de categorias.
- Casos novos verificam os quatro perfis e acesso anônimo, criação/leitura/edição, inativação/reativação, defaults, limites e campos obrigatórios, paginação, busca, ordenação, inexistência e revalidação da sessão do autor dentro da transação.
- Unicidade validada com caixa diferente, espaços externos, composição Unicode equivalente, categoria inativa, atualização para nome existente e duas criações simultâneas.
- Datas padronizadas em UTC com milissegundos após identificar a diferença de precisão entre .NET e PostgreSQL; respostas de gravação e leitura agora correspondem integralmente nos testes.
- 44 testes Playwright aprovados: 28 existentes e 16 novos (oito cenários de categorias em desktop e os mesmos em viewport móvel).
- Interface validada para nome/ordem obrigatórios com explicação visível, criação, edição e reabertura, ativação/inativação com confirmação/cancelamento, filtros, paginação, estado vazio, nome duplicado, indisponibilidade, categoria inexistente e restrição por perfil.
- Navegador usa API simulada. Os testes de backend usam HTTP e PostgreSQL isolado; nenhuma categoria de exemplo foi inserida no banco real.
- Migration 20260929190440_AddCategories revisada: Up somente cria Categories, chave primária, constraint de ordem e índice único. Aplicada ao banco de desenvolvimento; migrations list confirma InitialAccessControl e AddCategories aplicadas.
- EF confirmou ausência de alterações de modelo pendentes. Nenhuma conta, senha ou perfil persistido foi alterado pelo incremento.
- API Release reiniciada na porta 5080; frontend permanece em 8101. Smoke test completo aprovado com banco Healthy, CORS, ProblemDetails, OpenAPI e frontend.
- /api/categories presente no OpenAPI e retornando 401 sem credenciais na API atualizada.

Arquivos, contratos e comandos: [categories.md](categories.md). O próximo aceite manual é cadastrar e editar uma categoria na área administrativa. Produtos continuam para o próximo incremento, após essa validação.

## Fase 3B — Produtos

Implementada após autorização do usuário para continuar o catálogo.

Validada em 29/09/2026:

- Build Release da solução aprovado, sem erros ou avisos. Build de produção Angular aprovado, bundle inicial de aproximadamente 464 kB.
- 98 casos de backend validados em PostgreSQL temporário: 73 anteriores e 25 novos de produtos. Na execução completa, 97 passaram; o restante identificou uma leitura repetida do mesmo stream no próprio teste. O teste foi corrigido para ler a resposta uma vez e passou na reexecução isolada, sem alteração adicional na implementação.
- Testes novos verificam autorização dos quatro perfis/anônimos em todas as operações, CRUD sem exclusão física, preço exato, rejeição de preços inválidos sem arredondamento, limites/omissões, URL HTTPS, categoria existente/ativa e produto inexistente.
- Verificados disponibilidade derivada, efeito da inativação/reativação de categoria, independência e idempotência dos flags do produto, unicidade por categoria, Unicode, mudança de categoria e criações simultâneas.
- Verificados filtros/paginação, proteção da FK contra exclusão de categoria com produtos e revalidação do autor após revogação da sessão.
- A validação de limite decimal foi corrigida após identificar dependência de configuração regional no RangeAttribute. A implementação compara valores decimal diretamente; preços válidos e inválidos passaram em HTTP no ambiente pt-BR.
- 64 testes Playwright aprovados: 44 existentes e 20 novos (dez cenários de produtos em desktop e em viewport móvel).
- Navegador verificou cadastro/edição, preço com vírgula/ponto, categoria além da primeira página, pausa/liberação/inativação, confirmação/cancelamento, filtros, paginação, erros, campo inválido com explicação, categoria inativa, URL inválida e prévia de imagem indisponível.
- Testes de navegador usam respostas simuladas; os testes de backend usam HTTP e PostgreSQL real isolado. Nenhum produto de exemplo foi inserido no banco de desenvolvimento pelos testes.
- Migration 20260929194633_AddProducts revisada e aplicada ao banco de desenvolvimento. Up somente cria Products, FK restrita para Categories, índice único por categoria/nome e constraint de preço. Migrations anteriores permanecem aplicadas.
- EF confirmou ausência de alterações de modelo pendentes. Nenhum cadastro de categoria, funcionário ou credencial foi alterado pelo incremento.
- API Release reiniciada em 5080; frontend disponível em 8101. Smoke test aprovado com PostgreSQL Healthy, CORS, ProblemDetails, OpenAPI e frontend.
- Rotas de produtos, status e disponibilidade confirmadas no OpenAPI; /api/products retorna 401 sem credenciais.

Arquivos, contratos, limites e comandos: [products.md](products.md). O próximo aceite manual é cadastrar um produto em categoria ativa, editar preço e testar sua disponibilidade. Ingredientes e fichas técnicas ainda não foram implementados.

## Fase 3C — Ingredientes

Implementada após autorização do usuário para continuar o desenvolvimento. Validação em 29/09/2026:

- Build Release da solução aprovado com zero erros e zero avisos. Build de produção Angular aprovado, bundle inicial de aproximadamente 464 kB.
- 119 testes de backend aprovados em uma execução completa no PostgreSQL temporário: 98 existentes e 21 casos novos de ingredientes.
- Casos novos cobrem permissões em todas as rotas, três unidades, criação/leitura/edição, precisão de custo e quantidade, normalização de fornecedor, datas e status idempotente.
- Testados nome duplicado incluindo inativo/Unicode, criação concorrente, busca/paginação/filtros, campos obrigatórios, intervalos e excesso de casas decimais sem arredondar, ausência do registro e inexistência de DELETE.
- Verificadas imutabilidade da unidade, preservação dos dados em conflitos e revalidação de sessão/perfil dentro da transação. Constraints de custo, mínimo e unidade também testadas diretamente no banco isolado.
- A primeira tentativa de build Release encontrou o executável em uso pela instância local da API. Migration gerada em Debug; o build Release passou após encerrar somente a API deste projeto.
- A primeira execução de navegador encontrou o servidor Angular antigo sem o menu novo e foi interrompida. O servidor deste projeto foi reiniciado para carregar o código atual. Nenhum servidor de outro projeto foi encerrado.
- A execução completa de 84 testes Playwright terminou com 83 aprovados e um timeout no cenário existente de categoria inativa de produtos em celular. Os 20 casos novos de ingredientes passaram, com dez cenários em desktop e celular: cadastro/edição, envio correto de decimais, três unidades, bloqueio da unidade na edição, status com confirmação/cancelamento, busca/paginação, duplicidade, API indisponível, registro inexistente, permissões e sessão expirada.
- O cenário de produtos que excedeu cinco segundos já mostrava o elemento esperado no snapshot final da falha. Sua reexecução isolada em celular passou em 4,3 segundos, sem alterar código ou testes. Assim, os 84 casos foram validados entre a execução completa e a reexecução; não se declara uma execução completa de 84/84 sem falhas. Reprodução: `npm.cmd run test:e2e -- products.spec.ts --project=mobile --grep 'categoria inativa pode' --workers=1`.
- Navegador usa API simulada; backend usa HTTP e PostgreSQL real temporário. Nenhum ingrediente de exemplo ou conta real foi criado ou alterado no banco da hamburgueria pelos testes.
- Migration 20260929205733_AddIngredients revisada e aplicada ao banco de desenvolvimento. A listagem do EF confirmou as quatro migrations aplicadas e ausência de alterações de modelo pendentes.
- API Release reiniciada em 5080 e frontend em 8101. A primeira consulta de readiness excedeu o limite de cinco segundos ao abrir conexão; a inspeção confirmou o serviço PostgreSQL ativo, aceitando conexões, e a nova consulta retornou Healthy. A reexecução completa do smoke test passou, incluindo readiness, liveness, CORS, ProblemDetails, OpenAPI e frontend.
- Rotas de ingredientes confirmadas no OpenAPI. GET /api/ingredients retorna 401 sem credenciais. git diff --check sem erros.

Escopo, arquivos e reprodução: [ingredients.md](ingredients.md). O aceite manual é cadastrar ingredientes reais, conferir custo por unidade-base, editar fornecedor/custo e testar ativação/inativação. Fichas técnicas serão o próximo incremento; estoque e CMV permanecem na fase 6.

## Fase 3D — Fichas técnicas

O usuário concluiu a validação de ingredientes em 30/09/2026 e autorizou a próxima etapa.

- Build de produção Angular aprovado, bundle inicial de aproximadamente 465 kB.
- A primeira execução completa do backend teve 135 casos: 134 passaram e um falhou por comparar a serialização textual de decimais (`2` e `2.000`). O teste foi corrigido para comparar campos e valores tipados, sem modificar a regra de negócio.
- Os 16 casos de RecipeTests passaram na reexecução após essa correção. Os 135 casos foram validados entre a execução completa e a reexecução; não se declara uma execução completa sem falhas de 135/135.
- Casos novos verificam autorização dos quatro perfis/anônimo, produto inexistente versus ficha ausente, criação/substituição/remoção de itens, identidade/datas, instruções, unidades e ausência de efeitos sobre produto e ingrediente.
- Verificados ingredientes inativos novos versus vínculos existentes, preservação integral em falhas, campos obrigatórios/nested null, precisão sem arredondar, duplicatas, máximos de itens/rendimento/quantidade, PUT repetido e duas primeiras gravações concorrentes sem misturar composições.
- Revalidação da sessão/perfil dentro da transação e constraints/FKs/cascade foram testadas no banco isolado.
- Build Release da solução aprovado com zero erros e zero avisos.
- A primeira execução dos testes de navegador de fichas foi interrompida ao identificar seletores de teste que não localizavam os campos dinâmicos. Os testes passaram a identificar os seletores por papel e nome acessível. O cenário de criar/editar/remover/reabrir passou na execução isolada antes da regressão completa.
- Migration 20260930174555_AddRecipes revisada: Up adiciona somente Recipes e RecipeItems, com unicidade de produto e ingrediente por ficha, limites, FKs restritas para produto/ingrediente e cascade da receita para seus itens.
- Fixtures de categorias, produtos e ingredientes removem receitas primeiro apenas no banco isolado de testes, respeitando os novos vínculos.
- A execução completa de navegador teve 106 casos: 101 passaram e cinco falharam. Três falhas ocorreram na criação/encerramento do contexto do navegador em testes existentes; duas eram o mesmo cenário novo de ingrediente inativo em desktop/celular. Nesse cenário, o HTML já continha `option disabled`, mas o matcher de estado reportava habilitado; o teste foi ajustado para conferir diretamente a propriedade nativa `disabled` da opção.
- Os cinco casos restantes passaram com `npm.cmd run test:e2e -- --last-failed --workers=1` (25,1 s). Assim, 106 casos foram validados entre execução completa e reexecução, incluindo os 22 novos casos de fichas (11 cenários em desktop e celular); não se declara uma execução completa sem falhas de 106/106. Testes de navegador usam API simulada e não alteram o banco real.
- Casos de interface incluem criar/editar/remover/reabrir, rendimento e quantidades, repetição, lista vazia, ingrediente em página posterior, inativo já vinculado, inativação durante edição, preservação de campos em erro, recuperação de carregamento, produto inexistente e restrição por perfil/sessão.
- Migration aplicada ao banco de desenvolvimento; EF confirmou as cinco migrations aplicadas e nenhuma mudança de modelo pendente. Não foram inseridos dados de demonstração ou alteradas credenciais/cadastros reais.
- API Release reiniciada em 5080, frontend em 8101. A primeira consulta de readiness após iniciar a API excedeu o limite; uma nova consulta retornou Healthy e a reexecução completa de Test-Foundation passou, incluindo liveness/readiness, CORS, ProblemDetails, OpenAPI e frontend.
- GET/PUT /api/products/{productId}/recipe confirmados no OpenAPI; GET sem credenciais retorna 401 na API atualizada.

Contratos, arquivos, comandos e roteiro de aceite: [recipes.md](recipes.md). Estoque e CMV não fazem parte deste incremento. O próximo aceite manual é montar, salvar e reabrir fichas técnicas de produtos reais; clientes e endereços iniciarão a Fase 4 após essa validação.

## Manutenção técnica — 30/09/2026

- Angular migrado oficialmente 20 → 21 → 22.2.1; CLI/build 22.2.0 e TypeScript 6.0.3. Ionic permanece 9.0.5. Build de produção aprovado, com aproximadamente 478 kB no bundle inicial.
- SDK .NET 10.0.401 e runtime .NET/ASP.NET Core 10.0.12 instalados após conferir SHA512 e assinatura Microsoft. O instalador retornou 3010: reinicialização do Windows pendente para arquivos em uso.
- `dotnet restore --locked-mode` e verificação de formatação C# aprovados. Build Release da solução com zero avisos e zero erros. EF confirmou ausência de alterações de modelo desde a última migration; nenhuma migration criada ou aplicada nesta manutenção.
- Backend: **135/135 casos aprovados em uma execução completa no runtime 10.0.12**, após centralizar a autorização/transação do catálogo. Log local: `.local/maintenance-backend-net10.0.12.log`. A execução anterior no runtime 10.0.8 também passou 135/135.
- Frontend: `format:check`, ESLint com zero avisos e build aprovados. Mantida a verificação estrita de templates, sem suprimir os diagnósticos de navegação opcional/nullish.
- Navegador: **106/106 casos aprovados em uma única execução**, sem reexecuções, com um worker, Edge e cenários desktop/celular. Tempo: 7,6 minutos. Log local: `.local/maintenance-browser-tests.log`. O navegador utiliza API simulada; os testes HTTP do backend utilizam PostgreSQL real isolado.
- Instalação das dependências npm informou zero vulnerabilidades conhecidas entre os 344 pacotes auditados naquele momento. Isso não equivale a uma auditoria completa de segurança.
- Sintaxe PowerShell de `Test-Quality.ps1` e sintaxe YAML do workflow verificadas. Os comandos de qualidade foram executados localmente; a execução do GitHub Actions ainda depende de enviar o workflow ao repositório. Linux/Chromium no runner remoto ainda não foi executado.
- O serviço PostgreSQL 17.5 é compartilhado com `parsmartmanager`. Foi criado backup local da Made in Minas em `.local/backups`; não houve atualização ou parada desse serviço. A atualização para 17.11 depende de combinar a janela e obter backups administrativos dos dois bancos e objetos globais.
- API reiniciada em 5080 e frontend em 8101. O servidor Angular encontrou uma restrição de leitura de diretórios no sandbox ao reiniciar; iniciar fora dessa restrição resolveu, sem alterar código. `Test-Foundation.ps1 -ExpectDatabaseReady` passou integralmente, incluindo readiness saudável, CORS, ProblemDetails, OpenAPI e frontend.
- A atualização do Node global 22.16.0 → 22.23.3 possui instalador oficial verificado. A primeira tentativa retornou erro 1925 por falta de privilégio administrativo; a repetição usa a confirmação padrão de elevação do Windows. As validações do frontend acima usaram o Node 24.19.0 do Rider, compatível com Angular 22.

Arquivos, comandos e procedimento de atualização compartilhada: [maintenance.md](maintenance.md). Próximo incremento proposto: [clientes e endereços](customers-plan.md).

## Aceite das fichas técnicas e integração — 30/09/2026

- O usuário aprovou as fichas técnicas. O [PR #1](https://github.com/Pauliinrodrigues/MadeinMinasApp/pull/1) passou no GitHub Actions com 135 testes HTTP e 106 testes de navegador em Linux/Chromium, além dos builds, lint e formatação.
- O PR foi integrado por squash na `master`, commit `b35ebcc`. A branch `feat/customers-addresses` foi criada dessa base para a Fase 4A.
- A confirmação administrativa da atualização global do Node foi cancelada; essa atualização é opcional. O Node 24.19.0 do Rider é compatível e foi usado nas verificações locais.

## Clientes e endereços — Fase 4A

- Backend: **161/161 testes aprovados em uma execução completa**, incluindo 26 novos casos de clientes/endereços. PostgreSQL temporário em 127.0.0.1:55433, sem usar os bancos permanentes. Log: `.local/customers-backend-tests.log`.
- Casos novos incluem permissões em todas as operações, telefone formatado/duplicado/inválido, conflito concorrente, rollback de edição, paginação/filtros, vínculo de endereço, validações e rechecagem de sessão/perfil nas seis gravações. O teste existente do atendente foi atualizado para exigir também customers.manage, mantendo a restrição à administração de funcionários.
- Lint aprovado, formatação frontend/C# verificada e build de produção do frontend aprovado, com aproximadamente 479 kB iniciais. Nenhuma dependência adicionada.
- A primeira execução de navegador foi interrompida após identificar seletores ambíguos dos novos testes: duas mensagens role=status coexistiam ao criar cliente, e getByLabel com exact não localizava selects cujo label inclui as opções. Os testes passaram a selecionar a mensagem específica e usar o papel combobox. Não se mascarou falha com retries nem se alterou regra do produto para satisfazer o teste. Log inicial: `.local/customers-browser-initial.log`.
- A migration `20260930205926_AddCustomersAndAddresses` foi revisada e validada no PostgreSQL isolado: cria somente Customers, Addresses, índices, constraints e FK restrita.
- Migration aplicada ao `made_in_minas` após confirmar banco, host, porta e usuário próprios. Backup anterior em `.local/backups/made_in_minas-before-customers-20260930-181856.dump`, com índice conferido por pg_restore; não foi realizado ensaio de restauração desse backup. A primeira aplicação usou um binário anterior à geração da migration e não a encontrou; recompilar antes de aplicar resolveu. Verificação posterior confirmou a migration no histórico e zero clientes/endereços, sem inserção de dados de demonstração.
- Solução Release compilada com zero avisos e zero erros. API reiniciada em 5080 e frontend disponível em 8101. Test-Foundation com ExpectDatabaseReady passou, incluindo conexão autenticada saudável, CORS, ProblemDetails e frontend. OpenAPI contém as seis rotas de clientes/endereços; GET /api/customers sem credenciais retorna 401.
- O PostgreSQL compartilhado não foi parado nem atualizado. Nenhum comando de migration ou backup foi dirigido ao `parsmartmanager`.
- EF confirmou ausência de alterações de modelo pendentes após a migration.
- Navegador: a execução completa terminou com **127 aprovados e um caso de produtos interrompido ao criar a página**, com `browserContext.newPage: Target page, context or browser has been closed`. Os 22 novos casos de clientes/endereços passaram em desktop e celular. O único caso restante passou isoladamente com `--last-failed --workers=1` (8,9 s), sem alteração no produto nem aumento de timeout. Assim, os 128 casos foram validados entre a execução completa e a reexecução; não se declara uma execução local única de 128/128. Logs: `.local/customers-browser-full.log` e `.local/customers-browser-recheck.log`; trace original preservado em `.local/customers-browser-product-context-failure.zip`.

O usuário concluiu o aceite manual e autorizou a publicação/integração da Fase 4A. O [PR #2](https://github.com/Pauliinrodrigues/MadeinMinasApp/pull/2) registra esse aceite e as verificações remotas. O commit de implementação `de07c7e` passou no GitHub Actions com **161/161 testes de backend e 128/128 de navegador em uma execução completa**, além de builds, lint e formatação; [execução Quality](https://github.com/Pauliinrodrigues/MadeinMinasApp/actions/runs/36780008053). O roteiro em [customers.md](customers.md) permanece como referência.

## Carrinho da equipe — Fase 4B — 01/10/2026

- Branch `feat/staff-cart`, criada da `master` atualizada no commit `511c228`, após o aceite e integração de clientes/endereços.
- Backend: **190/190 testes aprovados em execução completa**, incluindo 29 novos casos do carrinho. PostgreSQL temporário isolado em 127.0.0.1:55433. Log: `.local/cart-backend-tests.log`.
- Casos cobrem permissões, revogação, catálogo disponível, busca/paginação, cliente/endereço, preço atualizado, decimais, quantidades, linhas nulas/duplicadas, limites, ausência de efeitos nos cadastros e rejeição de valores comerciais enviados pelo navegador.
- Solução Release compilada com zero avisos e zero erros após reiniciar somente o executável da MadeInMinas identificado pelo caminho completo. Formatação C# aprovada. Nenhuma migration, dependência ou gravação comercial no banco de uso.
- Frontend: formatação e lint aprovados; build de produção aprovado, com aproximadamente 491 kB iniciais e carrinho em carregamento por rota. O primeiro build Angular falhou por restrição de leitura de diretórios no sandbox; executar com acesso autorizado resolveu sem alterar o código.
- Uma tentativa inicial de navegador foi interrompida após timeout de cinco segundos ao carregar a nova rota. O cenário seguinte passou; o trace não apontou falha de regra comercial. Evidências preservadas em `.local/cart-browser-initial.log` e `.local/cart-browser-initial-trace.zip`.
- A execução focada teve 22/24 aprovados. Os dois casos restantes eram o mesmo cenário de erro em desktop/celular: o mock 503 enviava indevidamente o código de produto indisponível e o teste esperava mensagem genérica. Corrigida a resposta simulada para representar indisponibilidade do servidor, sem mudar o tratamento de erro da aplicação. Log: `.local/cart-browser-focused.log`.
- API disponível em 5080 e frontend existente reutilizado em 8101. Test-Foundation com ExpectDatabaseReady passou integralmente após o timeout da primeira abertura de conexão; o serviço PostgreSQL permaneceu ativo. OpenAPI inclui as duas rotas do carrinho, ambas retornando 401 sem credenciais. Nenhuma operação foi dirigida ao banco `parsmartmanager`.

- A regressão completa de navegador terminou com **151 aprovados e um timeout no cenário existente de ingrediente duplicado** (30 segundos), em 16,3 minutos. Os **24 casos novos do carrinho passaram** em desktop/celular. O trace foi preservado e revisado; o formulário ainda aguardava a resposta quando o orçamento total do teste se esgotou. O mesmo caso passou isoladamente em 7,1 segundos com `--last-failed --workers=1`, sem alterar código ou timeout. Assim, **152 cenários foram validados entre execução completa e reexecução**; não se declara uma execução única de 152/152. Logs: `.local/cart-browser-full.log` e `.local/cart-browser-recheck.log`; trace: `.local/cart-browser-ingredient-timeout.zip`.
- A revisão final de diff não apontou erros de whitespace. Formatação e lint do arquivo de testes foram novamente verificados após corrigir a simulação 503.

O usuário aprovou a Fase 4B em 01/10/2026, autorizou o commit e a abertura do [PR #3](https://github.com/Pauliinrodrigues/MadeinMinasApp/pull/3) e, posteriormente, sua integração à `master`. A integração exige os checks `backend` e `frontend` aprovados no commit final; os resultados remotos ficam registrados no PR. Roteiro e arquivos em [cart.md](cart.md). Ainda não há gravação de pedido, taxa final ou cobrança.

## Pedidos manuais — Fase 4C — 01/10/2026

- Branch `feat/manual-orders`, a partir de `c101b5f` (Fase 4B integrada). Aceite manual concluído pelo usuário em 01/10/2026; commit, publicação e integração à master autorizados.
- Backend: **214/214 testes aprovados em execução completa**, incluindo 24 casos novos de pedidos e extensão dos 29 casos de carrinho. PostgreSQL temporário exclusivo em 127.0.0.1:55433; log `.local/orders-backend-full.log`.
- Cobertura: permissões HTTP e revalidação transacional, cópias históricas, criação e reenvios concorrentes, chave por funcionário, replay após cancelamento, numeração única, revisão alterada, disponibilidade, versões/transições, motivo de cancelamento, constraints/FKs e valor monetário máximo sem arredondamento.
- Na primeira execução, a preparação do teste criava produto inativo; corrigido o cenário. Depois, os testes detectaram que a inclusão de histórico com UUID já atribuído era interpretada como atualização pelo EF; corrigida a inserção explícita no serviço. Todos os casos passaram antes da regressão completa.
- Formatação C# e frontend, lint e build Angular de produção aprovados. Bundle inicial aproximado de 492 kB; páginas de pedidos carregadas por rota. Logs `.local/orders-dotnet-format-check.log`, `.local/orders-format-check.log`, `.local/orders-lint.log` e `.local/orders-frontend-build.log`.
- Navegador focado: **44/46 aprovados**, incluindo os **22/22 casos novos de pedidos** em desktop/celular. Um teste antigo do carrinho lia o mock antes da resposta; corrigido para aguardar a revisão na interface. Outro esgotou 30 segundos no segundo login; o cenário móvel passou. Ambos passaram na execução completa subsequente. Evidências: `.local/orders-browser-focused.log`, `.local/orders-cart-race-trace.zip`, `.local/orders-cart-login-timeout.zip`.
- Migration revisada: `20261001142005_AddManualOrders` cria somente Orders, OrderItems e OrderStatusHistory. Não adiciona dados de demonstração, pagamentos nem estoque. Backup local anterior à migration: `.local/backups/made_in_minas-before-orders-20261001-115444.dump`, com índice verificado por pg_restore; restauração integral desse arquivo não foi ensaiada nesta etapa.
- Migration aplicada exclusivamente em `made_in_minas`, após conferir host/porta, usuário, banco e migration anterior. Verificação posterior retornou `made_in_minas|20261001142005_AddManualOrders` e contagem zero nas três tabelas novas. Nenhuma operação foi dirigida ao `parsmartmanager`; o serviço PostgreSQL compartilhado permaneceu ativo. Log `.local/orders-local-migration.log`.
- Solução Release compilada com zero avisos/erros. `dotnet ef migrations has-pending-model-changes` confirmou que o modelo corresponde à migration. Logs `.local/orders-release-build.log` e `.local/orders-model-check.log`.
- Reiniciada somente a API identificada pelo caminho do executável deste projeto, em 5080. Frontend existente em 8101. A primeira prontidão expirou ao abrir a conexão; a segunda execução de Test-Foundation com ExpectDatabaseReady passou integralmente. OpenAPI contém as rotas novas; GET /api/orders sem JWT retornou 401. Logs `.local/orders-api.log`, `.local/orders-foundation.log`, `.local/orders-foundation-recheck.log`.
- A regressão geral de navegador registrou **86 aprovações e um timeout em desktop**, no encerramento do contexto do cenário existente de categoria inativa em produtos. A execução ficou sem progresso na transição entre projetos e foi interrompida; os processos dessa execução foram conferidos antes de retomar. Log `.local/orders-browser-full.log` e trace preservado `.local/orders-products-timeout.zip`.
- A execução separada de celular concluiu **87/87 testes aprovados**, em 6,9 minutos. O único cenário pendente de desktop passou isoladamente em 8,2 segundos (14,1 segundos no total), sem modificar código nem timeout. Assim, **174 cenários de navegador foram validados entre execuções**, sem afirmar uma execução única de 174/174. Logs `.local/orders-browser-mobile.log` e `.local/orders-browser-desktop-recheck.log`.
- Revisão final de diff sem erros de whitespace. Nenhuma dependência nova ou credencial versionada. Publicação e integração autorizadas pelo usuário após o aceite; os checks remotos devem passar antes do merge.

Roteiro de aceite e arquivos alterados estão em [orders.md](orders.md). O aceite concluído inclui a política inicial de taxa manual e as regras de cancelamento. Pagamentos serão o próximo incremento.

## Pagamentos manuais — Fase 4D — 01/10/2026

- Branch `feat/manual-payments`, criada de `b98f41d` após o aceite e integração da Fase 4C pelo PR #4. Incremento manual integral aceito pelo usuário em 01/10/2026; commit, publicação e integração autorizados, com checks remotos aprovados antes do merge.
- Backend: **235/235 testes aprovados em execução completa**, incluindo **21 casos novos de pagamentos**. PostgreSQL temporário em 127.0.0.1:55433, sem usar bancos permanentes. Logs `.local/payments-backend-focused.log` e `.local/payments-backend-full.log`.
- Cobertura nova: acesso por perfil, sessão/perfil revalidados na transação, valor do pedido preservado, quatro formas, dinheiro/troco, confirmação explícita, devolução por administrador, motivos, campos comerciais rejeitados, versões, vínculo ao pedido, paginação, reenvios idênticos e concorrentes, criação versus cancelamento de pedido, recebimento versus cancelamento de pagamento, constraints e índice único parcial.
- Build Angular de produção aprovado, bundle inicial de 492,60 kB; página de pagamentos carregada por rota. Nenhuma dependência nova. Log `.local/payments-frontend-build.log`.
- Formatação C#/frontend e lint aprovados. A verificação inicial apontou apenas formatação no teste após ajustar o seletor da forma de pagamento; Prettier aplicado e verificação repetida com sucesso. Logs `.local/payments-dotnet-format-check.log`, `.local/payments-format-check-final.log` e `.local/payments-lint.log`.
- Primeira execução de navegador interrompida: o servidor Angular existente continuava exibindo a versão anterior do detalhe do pedido, sem link de pagamentos. Capturas e traces preservados em `.local/payments-browser-initial`; reiniciado somente o servidor identificado deste projeto na porta 8101. Os testes usam API simulada, sem pagamentos reais.
- Pagamentos em desktop: **12 cenários validados entre execuções**. A execução focada passou 11/12; débito excedeu a espera pela atualização, mas a captura já mostrava o registro Pendente. A repetição isolada esgotou os 30 segundos após navegação lenta enquanto builds estavam ativos (trace: abertura 5,76 s, navegação inicial 8,03 s, abertura da lista 5,25 s). Após terminar as compilações, o mesmo caso passou em 10,2 s, sem modificar código ou timeout. Logs `.local/payments-e2e-focused-recheck.log`, `.local/payments-debit-recheck.log` e `.local/payments-debit-final.log`; traces preservados em `.local/payments-debit-timeout.zip` e `.local/payments-debit-recheck-timeout.zip`.
- Celular: **49/49 cenários aprovados em uma execução**, em 6,0 minutos, cobrindo pagamentos (12), pedidos (11), carrinho (12) e sessão/equipe (14). Log `.local/payments-browser-mobile.log`. Somados ao desktop, **61 cenários de navegador validados entre execuções**. A regressão local desta etapa concentrou-se nos fluxos afetados; não se declara execução completa de todos os testes de catálogo no navegador. A suíte completa permanece no workflow de qualidade para a futura publicação.
- Migration `20261001180855_AddManualPayments` revisada e aplicada exclusivamente em `made_in_minas`, após conferir host, porta, banco, usuário e migration anterior. Backup `.local/backups/made_in_minas-before-payments-20261001-152933.dump`, índice legível por pg_restore; restauração integral desse arquivo não foi ensaiada.
- A conferência de contagens posterior à migration detectou atividade concorrente: pedido #2 criado às 15:31:10 e confirmado às 15:31:36, após o backup. Comparação de todas as linhas de Orders, OrderItems e OrderStatusHistory do backup com as atuais confirmou **preservação literal de todas as linhas anteriores**; apenas registros novos apareceram. Estado verificado: dois pedidos, três itens e cinco eventos, Payments/PaymentStatusHistory vazios. Logs `.local/payments-local-migration.log` e `.local/payments-migration-verification.log`. Não houve rollback ou regravação dos pedidos.
- Nenhuma operação foi dirigida ao banco `parsmartmanager`; o serviço PostgreSQL compartilhado não foi parado ou atualizado. O script temporário de comparação precisou da opção `--file -` de pg_restore para ler o backup sem restaurar dados; esse ajuste não altera o banco.
- Solução Release compilada com zero avisos e zero erros. Reiniciada somente a API identificada pelo caminho completo deste projeto, em 5080; frontend em 8101. Log `.local/payments-release-build.log`.
- EF confirmou ausência de alterações de modelo pendentes após a migration; log `.local/payments-model-check.log`. Revisão de diff sem erros de whitespace; backups, logs e helpers locais permanecem ignorados pelo Git.
- Test-Foundation com ExpectDatabaseReady aprovado integralmente após timeout de cinco segundos na primeira abertura da conexão, observado no log da API; a repetição não alterou o timeout. OpenAPI contém as cinco rotas de pagamentos (seis operações); GET sem JWT retorna 401. Logs `.local/payments-api.log`, `.local/payments-foundation.log` e `.local/payments-foundation-recheck.log`.

Roteiro de aceite, contratos e arquivos alterados: [payments.md](payments.md). O aceite de recebimentos, troco, cancelamento e devolução manual foi concluído. Etapa integrada pelo PR #5, commit `5f84680`, com checks backend/frontend aprovados.

## Cozinha/KDS — Fase 5A — 01/10/2026

- Branch `feat/kitchen-kds`, criada de `5f84680` após a integração dos pagamentos. Aceite manual concluído pelo usuário em 01/10/2026; commit, publicação e integração à master autorizados, com checks remotos aprovados antes do merge.
- Backend: **252/252 testes aprovados em execução completa**, incluindo **17 casos novos de cozinha**. PostgreSQL temporário isolado em 127.0.0.1:55433. Logs `.local/kitchen-backend-focused.log` e `.local/kitchen-backend-full.log`.
- Cobertura: permissões, campos exclusivos de produção, paginação e ordem da fila, horários, cópias da compra, histórico, transições/versões, replay, concorrência entre operadores e cancelamento, revogação de sessão/perfil, cancelamento administrativo sujeito à resolução de pagamentos e constraints.
- Frontend: formatação, lint e build de produção aprovados. Logs `.local/kitchen-format-final.log`, `.local/kitchen-lint-final.log` e `.local/kitchen-frontend-build.log`. Nenhuma dependência adicionada.
- Navegador desktop: **12/12 cenários novos aprovados**, cobrindo produção, paginação, fila antiga, atualização, falhas, recuperação, confirmação expirada, consultas sem sobreposição, conflitos, resposta perdida, permissões e expiração da sessão. Log `.local/kitchen-browser-desktop.log`. O processo de servidor iniciado pelo Playwright encontrou a porta 8101 já ocupada pelo servidor deste projeto, que serviu os testes com sucesso.
- Celular: **53/53 cenários aprovados**, incluindo cozinha (12), pedidos (15, com quatro casos de cancelamento durante/depois da produção), pagamentos (12) e sessão/equipe (14). Log `.local/kitchen-browser-mobile.log`. Junto do desktop, **65 cenários de navegador validados em duas execuções**. Esta regressão local cobre os fluxos afetados; não houve execução local de toda a suíte de catálogo. Os testes do navegador usam API simulada.
- Migration `20261001191445_AddKitchenStatuses` revisada: altera somente duas constraints, sem novas tabelas nem alteração dos dados comerciais. Backup `.local/backups/made_in_minas-before-kitchen-20261001-163520.dump`, com índice legível por pg_restore; restauração integral desse arquivo não foi ensaiada.
- Migration aplicada exclusivamente em `made_in_minas`, após conferir host, porta, usuário, banco e migration anterior. API interrompida por caminho de executável verificado antes da aplicação. Contagens preservadas: **4 pedidos, 5 itens, 8 eventos de pedido, 2 pagamentos e 3 eventos de pagamento**. Log `.local/kitchen-local-migration.log`. Nenhuma operação foi dirigida ao `parsmartmanager`; o serviço PostgreSQL compartilhado permaneceu ativo.

- Solução Release compilada com zero avisos/erros e verificação C# aprovada. EF confirmou ausência de alterações de modelo pendentes. Logs `.local/kitchen-release-build.log`, `.local/kitchen-dotnet-format-check.log` e `.local/kitchen-model-check.log`.
- API atualizada em 5080 e frontend em 8101. A primeira checagem de prontidão expirou durante a abertura da conexão, conforme log da API. A segunda execução de Test-Foundation com ExpectDatabaseReady passou integralmente, sem mudar timeout/configuração. OpenAPI contém as duas rotas KDS; consulta sem JWT retorna 401. Logs `.local/kitchen-api.log`, `.local/kitchen-foundation.log` e `.local/kitchen-foundation-recheck.log`.
- Revisão final do diff sem erros de whitespace. Logs, helpers e backup permanecem ignorados pelo Git.

Roteiro de aceite e arquivos alterados: [kitchen.md](kitchen.md). A fila começa após confirmação pelo atendimento. Expedição e impressão ficam para os próximos incrementos, após validar a cozinha.

## Expedição e impressão — Fases 5B/5C — 01/10/2026

- Branch única feat/dispatch-printing, criada de 06d73a5 após o PR #6, conforme solicitação do usuário. Aceite conjunto da expedição e impressão pelo navegador registrado em 01/10/2026, com commit, publicação e integração autorizados após checks remotos. Impressão automática e validação física dependem do equipamento; não são declaradas concluídas pela impressão de navegador.
- Backend focado: 25/25 casos aprovados em PostgreSQL temporário isolado, incluindo entrega/retirada, finalização condicionada a recebimento, cancelamento e devolução, permissões, histórico, concorrência/replay, paginação, snapshot de compra/endereço e contratos distintos das comandas. Log .local/dispatch-backend-focused.log.
- Testes existentes ajustados ao contrato novo: Delivered passa a ser filtro/status válido, e os perfis recebem permissões explícitas de impressão. Estados desconhecidos continuam rejeitados. Nenhuma expectativa de segurança foi removida.
- Navegador desktop: 15/15 cenários novos aprovados, com fluxos completos, falhas, dados expirados, conflitos, paginação, sessão/permissões, saída da tela, troca de pedido/via, três larguras de papel e texto tratado sem HTML ativo. Log .local/dispatch-browser-desktop.log. O diálogo é simulado nos testes; não comprova saída em impressora física.
- Build Angular de produção, lint e formatação aprovados. Logs .local/dispatch-build-final.log, .local/dispatch-lint-final.log e .local/dispatch-format-check.log. Sem dependências novas.
- Migration 20261001201327_AddDispatchStatuses revisada: altera somente duas constraints de estado/transição existentes, sem novas tabelas ou registros comerciais. Aplicada exclusivamente em made_in_minas após identificar e parar somente a API deste projeto. Backup .local/backups/made_in_minas-before-dispatch-20261001-173335.dump com índice legível por pg_restore (restauração integral não ensaiada). Preservadas as contagens de 4 pedidos, 5 itens, 12 eventos de pedido, 2 pagamentos e 3 eventos de pagamento. Log .local/dispatch-local-migration.log. Nenhuma operação no parsmartmanager ou interrupção do serviço PostgreSQL compartilhado.

Roteiro de aceite, regras, arquivos e limitações: [dispatch-printing.md](dispatch-printing.md).

- Primeira execução mobile: os 15/15 cenários novos de expedição/impressão passaram. Na regressão de cozinha, o cenário Kitchen excedeu 30 segundos antes do fluxo: trace registra criação da página em 20,1 s e navegação ao login em 19,3 s; cleanup do navegador levou 78 s. A execução seguinte estava em Salvando quando o runner foi interrompido após identificação por caminho/argumentos, para concluir as compilações e retomar sem essa carga concorrente. Nenhum timeout da aplicação/teste foi aumentado. Evidências em .local/dispatch-browser-mobile-initial e log .local/dispatch-browser-mobile.log.
- Solução Release compilada com zero avisos/erros, EF sem alterações de modelo pendentes; logs .local/dispatch-release-build.log e .local/dispatch-model-check.log.
- API atualizada em 5080 e frontend em 8101. Test-Foundation com ExpectDatabaseReady aprovado, incluindo banco, CORS, ProblemDetails e frontend. OpenAPI contém as quatro rotas de expedição/impressão; consultas sem JWT retornam 401. Logs .local/dispatch-api.log e .local/dispatch-foundation.log.

- Regressão completa do backend: 276/277 aprovados em 12,2 minutos. O caso existente OrderCancellationRequiresResolvingPaymentAndRefundRequiresAdministrator falhou antes da regra de pagamento, no login: Npgsql reportou timeout ao abrir conexão em 127.0.0.1:55433 durante carga concorrente. Log .local/dispatch-backend-full.log.
- O único caso pendente passou isoladamente, sem alterar código nem timeout, após encerrar compilações e o runner mobile lento. Log .local/dispatch-backend-recheck.log. Assim, 277 cenários de backend foram validados entre execuções; não se declara uma execução única de 277/277. Formatação C# verificada com sucesso em .local/dispatch-dotnet-format-check.log.

- Retomada mobile sem compilações concorrentes: 53/53 cenários aprovados em 5,8 minutos, cobrindo cozinha (12), pedidos (15), pagamentos (12) e equipe/sessão (14). Os dois cenários da cozinha que estavam pendentes passaram. Log .local/dispatch-browser-mobile-regression.log.
- Total de navegador validado neste incremento: 83 cenários distintos entre execuções (15 novos desktop, 15 novos mobile e 53 regressões mobile). Não se declara uma execução única completa de toda a suíte de catálogo. A suíte completa continua no workflow de qualidade para futura publicação.
- Consulta local de impressoras encontrou somente Microsoft Print to PDF e OneNote. Não houve validação em impressora física ou implementação de impressão automática. A prévia e o diálogo são a entrega de 5C nesta branch; automação depende de definição e aceite do equipamento.
- Diff final sem erros de whitespace; helpers, logs, traces e backup permanecem ignorados pelo Git. Branch feat/dispatch-printing; aceite e publicação/integração autorizados pelo usuário em 01/10/2026, com aprovação dos checks backend/frontend antes do merge.

## Fase 6A — estoque manual (02/10/2026)

Branch `feat/ingredient-stock`. Implementados saldo por ingrediente, entradas, saídas, contagem física, histórico com cópias de nomes/unidade, versão e chave idempotente, com acesso administrativo e revisão na interface. Nenhuma dependência nova. Roteiro e arquivos: [stock.md](stock.md).

- Compilação Release da API e dos testes aprovada durante a execução isolada. Os 14 novos cenários de StockTests passaram, incluindo concorrência, repetição após outros movimentos, saldo insuficiente, limites/precisão, permissões, sessão revogada, auditoria e preservação do custo/cadastro.
- Formatação C# verificada sem alterações pendentes. Frontend: build de produção, lint e format:check aprovados.
- Primeira regressão completa: 291 executados, 284 aprovados e 7 falhas durante a indisponibilidade dos arquivos PostgreSQL. Essa execução não foi aprovada; a retomada está registrada abaixo.
- Durante a regressão, arquivos da instalação PostgreSQL 17 deixaram de estar disponíveis. O log do cluster isolado registra ausência de share/timezone e share/timezonesets; ao encerrar, pg_ctl.exe não foi encontrado. As portas 5432 e 55433 ficaram sem listener. Evidência preservada em `.local/stock-postgres-regression.log`.
- Antes disso, a primeira inicialização do banco temporário falhou por falta de espaço no disco. A limpeza executada limitou-se aos três instaladores já usados em `.local/installers` e ao build antigo `.local/auth-build`, dentro deste repositório. Foram liberados cerca de 300 MB, preservando backups. Uma execução com redirecionamento ficou presa após iniciar o cluster; somente seu cluster identificado em 55433 foi encerrado. A execução seguinte, sem esse redirecionamento, aprovou os 14 testes novos.
- Ao encerrar a primeira sessão, a migration `20261002130808_AddIngredientStock` estava gerada e revisada, mas ainda não aplicada ao banco operacional. A atualização local ficou pendente da recuperação do ambiente; a retomada está registrada abaixo.
- Na conclusão da implementação, commit, push e integração aguardavam o aceite do usuário, registrado na retomada abaixo.
- Navegador: os 20 cenários existentes de ingredientes passaram (10 desktop + 10 mobile). A primeira execução dos novos cenários revelou seletor de teste incorreto para o combobox Tipo; traces preservados em `.local/stock-browser-initial`. Corrigido o seletor para papel/nome acessível, sem alterar timeouts ou código de negócio, os 12 cenários de estoque passaram (6 desktop + 6 mobile) em 2,1 minutos. Total validado: 32 cenários distintos entre execuções.
- Foi observado um instalador PostgreSQL em execução no Windows após a indisponibilidade. Nenhum instalador PostgreSQL foi iniciado por este trabalho. Após confirmar as ferramentas executando e os arquivos de fuso presentes, a regressão foi retomada em outro cluster isolado, sem depender do serviço operacional.
- Retomada completa aprovada: **291/291 testes**, sem falhas, em 7 min 48 s. Relatório `.local/stock-test-results/stock-backend.trx`. O cluster temporário foi encerrado e removido pelo helper. O cluster da execução anterior também foi removido após `pg_ctl status` confirmar ausência de servidor ativo; seu log foi preservado.
- Solução Release compilada com zero avisos/erros. EF confirmou ausência de alterações de modelo pendentes. O lint do arquivo de teste corrigido também passou. Naquele momento, a aplicação da migration local e o aceite manual ainda dependiam do serviço PostgreSQL operacional.
- API reiniciada com o build Release atualizado em 5080; frontend permanece em 8101. Os nove checks de Test-Foundation passaram no modo de banco indisponível: liveness 200, readiness 503 sem detalhes sensíveis, contrato de status, CORS, ProblemDetails, OpenAPI e frontend. OpenAPI inclui as duas rotas de estoque. Não houve validação com ExpectDatabaseReady nem aplicação da migration: o serviço em 5432 permanecia ausente. Diff sem erros de whitespace.

### Retomada após conclusão da instalação PostgreSQL

Em 02/10/2026, o usuário informou a conclusão da instalação. O serviço `postgresql-x64-17` estava ativo e as credenciais existentes permitiram confirmar `current_database() = made_in_minas`, usuário `made_in_minas_app` e migration anterior `20261001201327_AddDispatchStatuses`.

- Backup `.local/backups/made_in_minas-before-stock-20261002-113347.dump` criado antes da alteração; índice legível com `pg_restore --list`. Não foi realizado ensaio de restauração integral.
- Migration `20261002130808_AddIngredientStock` aplicada exclusivamente ao `made_in_minas`, usando o build Release já validado. Foi interrompida apenas a API identificada pelo caminho do executável deste projeto; nenhum serviço PostgreSQL compartilhado foi reiniciado e nenhuma operação foi direcionada ao `parsmartmanager`.
- Contagens preservadas antes/depois: 4 pedidos, 5 itens, 17 eventos de pedido, 3 pagamentos, 5 eventos de pagamento, 1 ingrediente, 1 ficha técnica e 1 item de ficha. Ao concluir, StockMovements estava vazio e nenhum ingrediente tinha saldo/versão de estoque diferente de zero. Nenhuma movimentação comercial fictícia foi criada.
- Registro da aplicação em `.local/stock-local-migration.log`. API reiniciada em 5080; logs `.local/stock-api-activated.log` e `.local/stock-api-activated-error.log`. Frontend disponível em 8101.
- A primeira consulta de readiness após o reinício foi cancelada durante a abertura da conexão Npgsql. Após investigar o log e repetir a verificação com a inicialização concluída, os **nove checks de Test-Foundation com -ExpectDatabaseReady passaram**, sem alterar timeouts: liveness/readiness 200 Healthy, ausência de detalhes sensíveis, contrato de status, CORS permitido/negado, ProblemDetails, OpenAPI e frontend.
- As duas rotas de estoque estão presentes no OpenAPI; consulta de estoque sem JWT retornou 401. Não houve mudança no código de negócio nesta retomada; permanecem válidos os 291 testes de backend e os 32 cenários de navegador já aprovados. Atualizados roadmap, roteiro de estoque e este registro.
- Aceite manual de **Ingredientes → Estoque** concluído pelo usuário em 02/10/2026, com autorização para commit, publicação e integração seguindo o padrão: PR para master, checks backend/frontend aprovados e squash merge.

## Fase 6B — consumo por pedidos (02/10/2026)

Branch `feat/order-stock`, criada da master `71f7c41`, após a integração da Fase 6A pelo PR #8. Implementação concluída; aceite manual pendente. Roteiro e arquivos em [order-stock.md](order-stock.md).

- **305/305 testes de backend aprovados**, sem falhas, em PostgreSQL temporário na porta 55433. Incluem os 14 novos cenários de consumo: agregação/arredondamento de linhas, composição histórica, falta de ficha/ingrediente ativo/saldo, limite, confirmação e cancelamento concorrentes/idempotentes, disputa entre pedidos/saída manual, cancelamento versus KDS, consumo mantido após preparo, devolução original após editar a ficha e migração de pedidos antigos sem baixa retroativa. Relatório preservado em `.local/order-stock-test-results/backend.trx`. Cluster temporário encerrado e removido pelo helper.
- **78/78 cenários de navegador aprovados** (pedidos, pagamentos e estoque; desktop/mobile), incluindo 12 execuções novas de avisos de estoque, composição, legado e conflitos. API simulada; não comprova por si só a integração com o banco local. Comando: `npm.cmd run test:e2e -- e2e/orders.spec.ts e2e/payments.spec.ts e2e/stock.spec.ts`.
- Build Release da solução com zero avisos/erros; verificação de whitespace .NET aprovada; EF confirmou ausência de mudanças de modelo pendentes. Frontend com format:check, lint e build de produção aprovados. Não houve atualização de dependências.
- Migration `20261002181034_AddOrderStock` aplicada exclusivamente ao `made_in_minas`, após confirmar destino/usuário e criar `.local/backups/made_in_minas-before-order-stock-20261002-153750.dump`. Índice verificado com `pg_restore --list`; restauração integral não ensaiada.
- Contagens preservadas: 4 pedidos, 5 itens, 17 eventos de pedido, 3 pagamentos, 5 eventos de pagamento, 1 ingrediente, 1 ficha e 1 item de ficha, 2 movimentos de estoque. Soma dos saldos continuou 0,000 e soma das versões, 2. Resultado: 3 pedidos Legacy e 1 Pending; nenhuma composição ou baixa fictícia criada. Logs em `.local/order-stock-local-migration.log`. Nenhuma operação foi direcionada ao parsmartmanager.
- API iniciada com Release em 5080 e frontend em 8101, em segundo plano. Logs `.local/order-stock-api.log` e `.local/order-stock-frontend.log`. O inventário real precisa estar conferido antes de confirmar pedidos, pois saldo zero bloqueia consumo.
- Alterações permanecem locais na branch para aceite manual; publicação/PR/integração serão feitos após a validação do incremento. CMV não foi iniciado.
- Os nove checks de Test-Foundation com -ExpectDatabaseReady passaram: API/banco saudáveis, frontend acessível, CORS, ProblemDetails e OpenAPI. Os contratos locais incluem stockStatus/stockComponents no pedido e orderId no movimento.

## Fase 6C — CMV teórico por produto (02/10/2026)

O usuário autorizou continuar em nova branch após a conclusão técnica da 6B. O incremento anterior foi preservado no commit local `98ecb4a`, em `feat/order-stock`. A branch `feat/product-cmv` parte dele; as duas ainda não foram publicadas/integradas, e a sequência de integração deve preservar essa dependência. Não foi inferido aceite manual do pedido de continuidade.

- **319/319 testes de backend aprovados**, sem falhas, em 3 min 23 s, no PostgreSQL temporário de 127.0.0.1:55433. Incluem 14 novos cenários de CMV: autorização, ficha ausente, custos zero/parciais, embalagem/rendimento/precisão, atualização de preço/custos/composição, cadastros inativos, margem negativa, valores mínimos/máximos e ausência de gravações. Relatório `.local/cmv-test-results/cmv-backend.trx`; cluster temporário encerrado e removido pelo helper.
- A primeira execução do lint apontou seis blocos if novos sem chaves. Corrigidos somente os arquivos indicados, conforme a regra curly; formatação, lint e build de produção passaram depois. Nenhuma alteração de dependência.
- O CMV usa apenas consultas; nenhuma migration, atualização de saldo ou escrita operacional foi executada neste incremento. O banco parsmartmanager não foi acessado. A migration herdada da 6B permanece como base.
- **62/62 cenários de navegador aprovados** em 3 min 42 s, em desktop/mobile: 20 execuções novas de CMV e 42 de regressão de produtos/fichas técnicas. API simulada, sem escrita comercial real. Comando: `npm.cmd run test:e2e -- e2e/product-cost.spec.ts e2e/products.spec.ts e2e/recipes.spec.ts`.
- Build Release da solução sem avisos/erros; whitespace .NET verificado e EF sem alterações de modelo pendentes. Apenas a API deste projeto foi reiniciada para usar o novo build, com logs `.local/cmv-api.log` e `.local/cmv-api-error.log`.
- Os nove checks de Test-Foundation com -ExpectDatabaseReady passaram; API em 5080, PostgreSQL conectado e frontend em 8101 disponíveis. A rota de CMV está no OpenAPI local e uma consulta sem login retornou 401.
- Arquivos novos/alterados e roteiro de aceite em [cmv.md](cmv.md). A 6C permanece em alterações locais na branch própria, sem publicação. Aceite manual de 6B/6C e integração seguem pendentes; dashboard não foi iniciado.

## Fase 7A — dashboard do dia (02/10/2026)

A continuidade foi autorizada após a conclusão técnica da 6C. O incremento de CMV foi preservado no commit local `5ce0ba8` e criada `feat/daily-dashboard` a partir dele. A branch depende de 6B/6C, ainda não publicadas/integradas. Não foi inferido aceite manual.

- Os **15 novos testes de backend** passaram no PostgreSQL isolado de 127.0.0.1:55433: permissões, dia civil/virada UTC, limites inclusivo/exclusivo, confirmação/cancelamento/taxa/ticket, recebimentos e estornos em dias distintos, troco, líquido negativo, filas antigas, produção atravessando meia-noite, dados históricos incompletos, ranking com nomes/linhas diferentes, limite/desempate, ausência de gravações/dados pessoais e revogação de sessão.
- A primeira regressão completa encontrou 77 falhas de chave estrangeira na preparação de testes antigos: os testes de dashboard deixavam pedidos vinculados a clientes/produtos/usuários no banco compartilhado de testes. Todos os 15 cenários novos passaram. Corrigida a limpeza em DisposeAsync da nova suíte; a nova regressão completa passou com **334/334 testes**, sem falhas ou ignorados, em 3 min 23 s. Relatório `.local/dashboard-test-results/dashboard-backend.trx`. Cluster temporário encerrado e removido pelo helper.
- **48/48 cenários de navegador aprovados** em 3,4 min: 20 execuções de dashboard e 28 de regressão de acesso/usuários, em desktop/mobile. Incluem formato monetário, timezone de navegador diferente de Brasília, médias ausentes, líquido negativo, nomes extensos/escape, erro inicial, atualização/recuperação, sessão revogada e acesso por perfil. API simulada, sem escrita comercial real. Comando: `npm.cmd run test:e2e -- dashboard.spec.ts staff.spec.ts`.
- Formatação, lint e build de produção do frontend aprovados. O build inicial no sandbox falhou por acesso negado a diretórios ancestrais; repetido com a permissão do ambiente, sem mudar código para contornar o erro. Build Release da solução sem avisos/erros; whitespace .NET verificado; EF sem mudanças de modelo pendentes. Sem novas dependências ou migrations.
- API atualizada iniciada em segundo plano na porta 5080; frontend existente em 8101. Logs `.local/dashboard-api.log` e `.local/dashboard-api-error.log`. Os nove checks de Test-Foundation com -ExpectDatabaseReady passaram. Rota do dashboard presente no OpenAPI e consulta sem sessão retorna 401.
- Nenhuma escrita comercial, atualização de saldo ou alteração de esquema foi executada no banco local neste incremento. Não houve operação sobre o parsmartmanager. Aceite manual e publicação/integração continuam pendentes; a 7B de relatórios por período ainda não foi iniciada.
- Arquivos e roteiro para conferir a tela estão em [daily-dashboard.md](daily-dashboard.md).

## Fase 7B — relatórios por período (02/10/2026)

Dashboard preservado no commit local `15e4fa0`, em feat/daily-dashboard. A branch `feat/period-reports` foi criada dele após autorização para a próxima etapa, mantendo a dependência dos incrementos locais 6B/6C/7A. Aceite manual e publicação/integração continuam pendentes.

- **354/354 testes backend aprovados**, sem falhas ou ignorados, em 6 min 25 s, no PostgreSQL temporário isolado de 127.0.0.1:55433. Incluem 20 novos cenários de relatórios: autorização/revogação, parâmetros inválidos, limite de 90 dias, padrão de sete dias, preenchimento de dias vazios, meia-noite UTC/Brasília, histórico de confirmação/cancelamento, taxa de entrega, ticket ponderado, recebimentos/estornos por data e forma, troco, ranking/limite/desempate/nomes preservados, consistência com dashboard e ausência de gravações/dados pessoais. Verificados também início histórico de horário de verão e conexão PostgreSQL em Pacific/Auckland. Relatório `.local/reports-test-results/reports-backend.trx`; cluster temporário encerrado e removido pelo helper.
- Antes da regressão completa, os 19 primeiros testes novos passaram em execução focada. O teste adicional de fuso da conexão foi incluído na execução completa, também aprovada. Não houve falha funcional nesta suíte.
- **78/78 cenários de navegador aprovados**, em 6,3 min, desktop/mobile: 30 execuções de relatórios e 48 de regressão de dashboard/acesso/usuários. Fuso do navegador em Asia/Tokyo nos cenários novos; filtros, datas inválidas, consulta exata, descarte de resultado ao editar, bloqueio durante consulta, falha/recuperação, médias ausentes, líquido negativo, permissões, revogação, nomes extensos/escape e ausência de rolagem horizontal da página. As tabelas mantêm rolagem própria. API simulada, sem vendas reais. Comando: `npm.cmd run test:e2e -- reports.spec.ts dashboard.spec.ts staff.spec.ts`.
- Formatação, lint e build de produção do frontend aprovados. Build Release da solução com zero avisos/erros; whitespace .NET verificado; EF confirmou ausência de mudanças de modelo pendentes. Sem dependências ou migrations novas.
- API atualizada em 5080, iniciada em segundo plano com logs `.local/reports-api.log` e `.local/reports-api-error.log`; frontend existente em 8101. Os nove checks de Test-Foundation com -ExpectDatabaseReady passaram. `/api/reports/sales` consta no OpenAPI local e retorna 401 sem login.
- Nenhuma escrita comercial, alteração de saldo ou atualização de esquema no banco operacional; nenhuma operação sobre parsmartmanager. O incremento permanece local para aceite manual, sem publicação. A próxima fase é o chat próprio, ainda não iniciado.
- Contratos, lista de arquivos novos/alterados e roteiro de aceite: [period-reports.md](period-reports.md).

## Publicação dos incrementos 6B a 7B — 02/10/2026

O responsável autorizou publicar todas as alterações seguindo o fluxo de branches, pull requests, checks e squash na master. As menções a publicação pendente nas seções anteriores registram a situação ao concluir cada implementação. A autorização atual não comprova execução manual dos roteiros.

| Incremento | Pull request | Validação no GitHub antes da integração |
| --- | --- | --- |
| 6B — estoque por pedido | [#9](https://github.com/Pauliinrodrigues/MadeinMinasApp/pull/9), integrado em `ed94d80` | 305 testes backend e 284 de navegador aprovados; ambos os jobs concluídos com sucesso |
| 6C — CMV teórico | [#10](https://github.com/Pauliinrodrigues/MadeinMinasApp/pull/10), integrado em `e4f71b2` | 319 testes backend e 304 de navegador aprovados; ambos os jobs concluídos com sucesso |
| 7A — dashboard | [#11](https://github.com/Pauliinrodrigues/MadeinMinasApp/pull/11), integrado em `23b2c38` | 334 testes backend e 324 de navegador aprovados; ambos os jobs concluídos com sucesso |
| 7B — relatórios | [#12](https://github.com/Pauliinrodrigues/MadeinMinasApp/pull/12) | Execução e resultado do commit final disponíveis nos checks do PR; merge condicionado ao sucesso de backend e frontend |

- As atualizações das bases de CMV, dashboard e relatórios preservaram os arquivos dos commits locais originais; comparação integral sem diferenças. A documentação desta publicação foi atualizada no PR de relatórios.
- Autoria e destino conferidos: Pauliinrodrigues, repositório Pauliinrodrigues/MadeinMinasApp. Nenhum arquivo ignorado estava versionado.
- A publicação não executou migration, movimentação comercial ou operação sobre o PostgreSQL compartilhado. Segredos, logs, resultados locais e backups permanecem fora do Git.

## Fase 8A — cardápio público (05/10/2026)

Implementação autorizada na branch `feat/public-menu`, criada da master `5adcd03` após atualização por fast-forward. Acesso público em `/pedido` e `GET /api/menu`, com filtro, paginação, disponibilidade manual e DTOs comerciais. Escopo e arquivos: [public-menu.md](public-menu.md).

- **367/367 testes backend aprovados**, sem falhas ou ignorados, em 3 min 9 s. Incluem 13 casos novos: leitura anônima, catálogo ativo, venda pausada, contrato sem dados administrativos, ordem/filtro/paginação, categorias ocultas/inexistentes, validação dos parâmetros, atualização de preço/disponibilidade, inativação de categoria, catálogo vazio, ausência de gravações e manutenção da proteção administrativa mesmo com JWT inválido. PostgreSQL temporário isolado em 127.0.0.1:55433; cluster encerrado e removido pelo helper. Relatório `.local/public-menu-test-results/public-menu-backend.trx`.
- **68/68 cenários de navegador aprovados**, em 3,4 min, desktop/mobile: 20 do menu, 20 de produtos e 28 de acesso/usuários. Cobrem consulta sem login, filtros/paginação, atualização, falha/retomada, timeout, descarte de produtos antigos, imagens, texto escapado, layout e consulta pública sem enviar/revogar a sessão da equipe. API simulada. Comando: `npm.cmd run test:e2e -- menu.spec.ts staff.spec.ts products.spec.ts`.
- Após a conferência visual, a apresentação da marca passou a aparecer também durante o carregamento da imagem, evitando área vazia. Lint e build foram repetidos com sucesso; os **20/20 cenários do menu foram revalidados** em 47,2 s. Nenhuma mudança nas regras/API nessa revisão visual.
- Build Release da solução com zero avisos/erros, formatação .NET verificada, formatação/lint/build Angular aprovados e EF sem mudanças de modelo pendentes. Sem novas migrations ou dependências.
- A primeira compilação Release encontrou o executável em uso. Identificada e encerrada somente a API deste projeto; nova compilação aprovada. O build Angular inicial foi bloqueado pelo sandbox na leitura de diretórios; passou com a permissão do ambiente. A ferramenta EF fixada no manifesto foi restaurada para executar a verificação de modelo, sem atualizar versões.
- API reiniciada em 5080 com o build atual; frontend existente em 8101. Logs `.local/public-menu-api.log` e `.local/public-menu-api-error.log`. Os nove checks de Test-Foundation com ExpectDatabaseReady passaram. Consulta real sem JWT ao menu retornou 200, no-store/no-cache e o catálogo existente; rota presente no OpenAPI.
- Conferência adicional com navegador, frontend e API reais em desktop/celular: resposta 200, cards correspondentes aos itens da API, sem JWT na consulta e sem erros JavaScript. Capturas conferidas em `.local/public-menu-desktop.png`, `.local/public-menu-mobile.png` e `.local/public-menu-mobile-product.png`. Somente leitura, sem cadastrar produtos de demonstração.
- Nenhuma escrita comercial, aplicação de migration ou operação sobre parsmartmanager. Esta entrega não inclui carrinho público, compra, conversa ou IA. Alterações locais para aceite manual; commit/publicação desta etapa ainda não realizados.

## Fase 8B — carrinho público (05/10/2026)

Continuidade autorizada pelo responsável. A 8A foi preservada no commit local `4bcd978` (`feat/public-menu`) e a branch `feat/public-cart` partiu dele. A autorização para continuar não é evidência de execução manual do roteiro nem de publicação. Escopo e arquivos: [public-cart.md](public-cart.md).

- **395/395 testes backend aprovados**, sem falhas ou ignorados, em 3 min 6 s. Os 28 casos novos cobrem revisão anônima, cálculo decimal com preços atuais, normalização de observações, resposta sem dados privados, linhas separadas e limite agregado de 99 unidades, 50 linhas, notas máximas, produto inexistente/inativo/pausado/categoria inativa, entradas nulas/fracionárias/fora dos limites e adulteração de campos comerciais. Também verificam ausência de gravações, proteção das rotas administrativas e consistência com a revisão da equipe. PostgreSQL temporário em 127.0.0.1:55433, banco exclusivo de testes; cluster encerrado e removido. Relatório `.local/public-cart-test-results/public-cart-backend.trx`.
- **96/96 cenários de navegador aprovados**, em 10 min, desktop/mobile: 24 do carrinho público, 20 do cardápio, 24 do carrinho da equipe e 28 de acesso/usuários. Cobrem adição/quantidade/observações, produtos pausados, limites, preço atual vindo da API, invalidação de revisão, remoção/limpeza, memória entre rotas e descarte ao recarregar, falhas/timeout, cancelamento de consulta ao sair, separação da sessão da equipe, texto escapado e largura no celular. API simulada. Comando: `npm.cmd run test:e2e -- public-cart.spec.ts menu.spec.ts staff.spec.ts cart.spec.ts`.
- Build Release da solução aprovado, zero avisos/erros. Formatação C# aprovada com o comando padrão do repositório: `dotnet format whitespace MadeinMinasApp.sln --no-restore --verify-no-changes --exclude backend/MadeInMinas.Api/Data/Migrations`. A primeira verificação ampla incluiu migrations antigas e apontou seus finais de linha/codificação; o fluxo de qualidade já as exclui, e nenhuma migration foi alterada. Prettier, lint e build Angular aprovados, incluindo o ajuste para apagar observações gerais ao remover o último item.
- `dotnet ef migrations has-pending-model-changes --project backend/MadeInMinas.Api --configuration Release --no-build` confirmou ausência de alterações pendentes. Não foram adicionadas/aplicadas migrations, entidades ou dependências.
- Identificada e reiniciada somente a API deste projeto em 5080; frontend em 8101. Logs `.local/public-cart-api.log` e `.local/public-cart-api-error.log`. A primeira readiness expirou ao abrir a conexão (OperationCanceledException no Npgsql, com limite de cinco segundos no health check); nova consulta retornou Healthy sem modificar conexão, credenciais ou serviço PostgreSQL. Após investigar o log, os **nove checks** de `Test-Foundation.ps1 -ExpectDatabaseReady` passaram.
- Conferência com **frontend/API reais**, em desktop e celular: adicionar produto existente, quantidade 2 e observação, revisar e conferir valor por linha/subtotal. Respostas 200/no-store, sem JWT, sem erros JavaScript, sem requisições de escrita comercial e sem transbordamento horizontal. Capturas inspecionadas: `.local/public-cart-desktop.png`, `.local/public-cart-mobile.png` e `.local/public-cart-mobile-review.png`. Nenhum produto de demonstração foi cadastrado ou preço alterado.
- Sem operação sobre parsmartmanager ou reinício do PostgreSQL compartilhado. A revisão consulta somente o catálogo de made_in_minas. A 8B permanece local, sem commit/push/PR; a 8A está no commit local mencionado acima, também sem publicação. Aceite manual e integração continuam pendentes.

## Fase 8C — checkout público para retirada (05/10/2026)

Continuidade autorizada. A 8B foi preservada no commit local `15d8107` (`feat/public-cart`); `feat/public-checkout` foi criada a partir dele. Escopo: contato autodeclarado, revisão e criação idempotente de pedido para retirada, com confirmação pela equipe e pagamento tratado no balcão. A preferência de entrega/frete foi solicitada; enquanto não definida, esta etapa não libera entrega pública. Contrato, arquivos e aceite: [public-checkout.md](public-checkout.md).

- **428/428 testes backend aprovados**, sem falhas ou ignorados, em 3 min 3 s. Incluem 33 casos novos: revisão anônima sem gravação/consulta de dados privados, normalização, criação com cópias e autoria pública, preservação de cadastro existente, alterações de preço/nome/contato/itens/notas, rejeição atômica de indisponibilidade, cliente inativo, entradas adulteradas, concorrência/idempotência, unicidade de cliente/número, confirmação/cancelamento pela equipe com baixa/devolução, constraints de autoria, limite por IP e proteção de rollback da migration. PostgreSQL temporário isolado em 127.0.0.1:55433; cluster encerrado/removido. Relatório `.local/public-checkout-test-results/public-checkout-backend.trx`.
- Primeira execução: 426 aprovados e uma falha no teste de concorrência ao desserializar novamente um stream consumido. As respostas já indicavam uma criação e sete repetições. O teste passou a ler cada resposta uma única vez; também foi corrigido o aviso xUnit2031. A execução final acima inclui o teste adicional de rollback. Nenhuma alteração aleatória em regras de pedido para contornar a falha.
- **144/144 cenários de navegador aprovados**, desktop/mobile, em 6,5 min: 28 do checkout, 24 do carrinho público, 20 do cardápio, 44 de pedidos e 28 de acesso/usuários. API simulada. Incluem recuperação da mesma tentativa após reload/resposta perdida, timeout/saída durante envio, bloqueio de edição pendente, restauração de itens após rejeição, falha/corrupção de armazenamento, remoção dos dados pessoais após sucesso, separação de JWT, origem pública no detalhe, texto escapado e layout. Comando: `npm.cmd run test:e2e -- public-checkout.spec.ts public-cart.spec.ts menu.spec.ts orders.spec.ts staff.spec.ts`.
- Build Release da solução aprovado com zero avisos/erros; formatação C# pelo comando padrão (migrations excluídas), Prettier, lint e build Angular aprovados. O lint inicial dos testes novos apontou cinco condicionais sem chaves; foram formatadas conforme o padrão e a nova execução passou. Sem dependências novas. A assinatura nullable de OrderHistoryResponse foi adaptada pela ferramenta semântica do Rider.
- Migration `20261005145517_AddPublicOrders` revisada e aplicada **somente a made_in_minas**, com validação de host/porta/usuário, current_database e versão anterior. Backup `.local/backups/made_in_minas-before-public-orders-20261005-121258.dump`, criado com pg_dump e índice conferido com pg_restore. Contagens de pedidos/itens/históricos/pagamentos/clientes/endereços/movimentos e somas de saldos/versões de estoque ficaram iguais. Sem criação de pedido operacional de teste, sem operação em parsmartmanager e sem reiniciar o PostgreSQL compartilhado.
- Down recebeu proteção explícita para recusar reversão com pedidos públicos; removido o preenchimento automático de autor com GUID vazio gerado inicialmente pelo EF. O teste executa o script reverso dentro de transação isolada, verifica a recusa e a preservação da autoria. `has-pending-model-changes` confirmou o modelo compatível com a migration após aplicação local.
- API reiniciada em 5080 com a versão atual; frontend em 8101. Logs `.local/public-checkout-api.log` e `.local/public-checkout-api-error.log`. Os **nove checks** de `Test-Foundation.ps1 -ExpectDatabaseReady` passaram, incluindo banco Healthy e CORS.
- Conferência visual com frontend/API **reais** em desktop/celular: produto existente, contato de conferência sem persistência, revisão 200/no-store, taxa zero/retirada, preços/total/reviewToken vindos da API, sem JWT, sem erros JavaScript ou transbordamento horizontal. O helper bloqueia qualquer endpoint de criação; não enviou pedido nem gravou dados no sessionStorage. Capturas inspecionadas em `.local/public-checkout-desktop.png` e `.local/public-checkout-mobile-review.png`. Envio completo e integração com a equipe foram verificados no banco isolado e nos testes de navegador; aceite manual do responsável continua separado.
- A 8C permanece local, sem commit, push ou PR. As bases 8A/8B estão preservadas nos commits locais mencionados; a continuidade não publicou alterações nem integrou master. README, roteiro, documentação do banco e contrato foram atualizados.

## Fase 8D — acompanhamento público do pedido — 05/10/2026

Continuidade autorizada após a 8C. A entrega anterior foi preservada no commit local `c8de7ee`; `feat/public-order-tracking` foi criada a partir dele. Escopo, arquivos e roteiro manual em [public-order-tracking.md](public-order-tracking.md). A Fase 8 ainda precisa de conversa/transferência; entrega pública depende da definição de cobertura/frete.

- **451/451 testes na regressão completa do backend**, sem falhas ou ignorados, em 3 min 58 s. Incluem 23 casos novos de credencial e integração pública: isolamento por pedido, adulteração, vencimento, prazo fixo na recuperação, finalidade criptográfica, ausência de dados privados, recusa de pedidos manuais, histórico real por atendimento/KDS/expedição, cancelamento e limite de consultas. PostgreSQL temporário exclusivo em 127.0.0.1:55433; cluster encerrado e removido ao terminar. Relatório `.local/public-tracking-test-results/public-tracking-backend.trx`.
- Após acrescentar a verificação de persistência de chaves, **9/9 testes de PublicOrderAccessTests** passaram em 445 ms: oito já cobertos e um novo que recria o provedor, lê a credencial com as mesmas chaves e a recusa sob outro nome de aplicação. São **452 casos distintos aprovados**, em duas execuções, sem repetir toda a regressão. Chaves efêmeras isoladas na fábrica HTTP; o teste de persistência cria/remove somente seu diretório de chaves nos artefatos de teste. Relatório `.local/public-tracking-test-results/public-access-tests.trx`.
- **128/128 cenários de navegador aprovados**, desktop/mobile, em 7,4 min: 28 de acompanhamento, 28 do checkout, 24 do carrinho, 20 do cardápio e 28 de acesso/usuários. API simulada, sem pedidos operacionais. Cobrem acesso pelo comprovante/reload, credencial somente no cabeçalho, atualização/histórico, cancelamento/finalização, retirada, falhas/timeouts, pausa/retomada por visibilidade, ausência de consultas após sair, intervalo maior em 429, comprovante antigo/corrompido e separação do JWT da equipe. Comando: `npm.cmd run test:e2e -- public-order-tracking.spec.ts public-checkout.spec.ts public-cart.spec.ts menu.spec.ts staff.spec.ts`.
- Compilação Release da solução com **zero avisos e erros**; formatação C# verificada pelo comando padrão, excluindo migrations. Prettier, lint e build Angular finais aprovados. Sem dependências, migrations ou alterações no modelo. A assinatura do construtor do checkout foi ajustada pela refatoração semântica do Rider; nenhuma chamada direta exigiu alteração.
- API reiniciada em 5080 somente após identificar o executável deste projeto; frontend permanece em 8101. Logs `.local/public-tracking-api.log` e `.local/public-tracking-api-error.log`. **Nove checks de Test-Foundation.ps1 -ExpectDatabaseReady aprovados**, incluindo banco Healthy. Consulta real sem credencial confirmou 404 genérico/no-store; preflight CORS real confirmou origem local e cabeçalho X-Order-Access.
- Conferência visual com frontend real e dados de acompanhamento **simulados** em desktop/celular, com todas as chamadas de API interceptadas: sem erros JavaScript ou transbordamento horizontal. Capturas inspecionadas em `.local/public-tracking-desktop.png` e `.local/public-tracking-mobile-history.png`; helper `.local/Preview-PublicTracking.cjs`. O fluxo completo com gravações foi validado somente no banco isolado. Nenhuma migration, gravação operacional ou operação em parsmartmanager foi executada; PostgreSQL compartilhado não foi reiniciado.
- README, roteiro, arquitetura, API, banco, regras, decisões e continuidade do checkout atualizados. A 8D permanece com alterações locais não commitadas; não houve push, PR ou integração à master. Aceite manual do responsável e publicação continuam pendentes. Preservação/compartilhamento de chaves para hospedagem é requisito documentado, não infraestrutura entregue nesta etapa.

## Fase 8E — atendimento humano — 05/10/2026

Continuidade autorizada. A 8D foi preservada no commit local `687e10c`; `feat/human-chat` foi criada a partir dele. Escopo: solicitação pública, fila da equipe, atribuição, mensagens e encerramento. A preferência de permitir ajuda antes/depois do pedido foi solicitada, sem resposta durante a implementação; adotado esse acesso sem vínculo automático com cliente/pedido. Contratos, arquivos e aceite em [human-chat.md](human-chat.md).

- **473/473 testes backend aprovados**, sem falhas ou ignorados, em 3 min 30 s. São 21 casos novos sobre os 452 anteriores: credencial específica com prazo fixo, privacidade, separação do acompanhamento, rejeição de acesso adulterado/expirado, idempotência e concorrência, atribuição, tomada pelo administrador, encerramento, permissões, entradas inválidas, paginação, limite de mensagens, limitação por IP e proteção do Down contra perda de conversas. PostgreSQL temporário exclusivo em 127.0.0.1:55433, encerrado/removido pelo helper ao terminar. Relatório `.local/human-chat-test-results/human-chat-backend.trx`.
- **136 cenários distintos de navegador aprovados**, em execuções combinadas, desktop/mobile: 32 do chat, 28 do acompanhamento, 28 do checkout, 20 do cardápio e 28 de acesso/usuários. A primeira execução de `npm.cmd run test:e2e -- human-chat.spec.ts public-order-tracking.spec.ts public-checkout.spec.ts menu.spec.ts staff.spec.ts` teve 130 aprovados e seis falhas (três casos por viewport). Traces apontaram seletor de label incorreto no filtro de status e alteração prematura da resposta simulada antes da primeira leitura da conversa. Ajustados somente os testes para usar o combobox pelo nome acessível e aguardar a tela inicial antes de simular concorrência. Nova execução de `npm.cmd run test:e2e -- human-chat.spec.ts`: **32/32 aprovados em 2,2 min**. Os outros 104 cenários já tinham passado e não foram repetidos.
- Navegador cobre solicitação/resposta, recuperação após falha e reload sem duplicação, retomada da mensagem da equipe após novo login, histórico paginado, encerramento, falha de leitura, armazenamento indisponível, texto HTML escapado, pausa ao ocultar/sair, permissões, disputa entre atendentes e tomada pelo administrador. API simulada, sem mensagens fictícias na operação.
- Build Release da solução aprovado com zero avisos/erros. Formatação C# verificada com `dotnet format whitespace MadeinMinasApp.sln --no-restore --verify-no-changes --exclude backend/MadeInMinas.Api/Data/Migrations`. Prettier, lint e build de produção Angular finais aprovados. `has-pending-model-changes` confirmou que o modelo corresponde à migration. Nenhuma dependência nova.
- Migration `20261005183152_AddHumanChat` revisada e aplicada **somente em made_in_minas**, após verificar host/porta/usuário/banco e criar `.local/backups/made_in_minas-before-human-chat-20261005-154749.dump`, com catálogo conferido pelo pg_restore. Cria apenas ChatConversations/ChatMessages, índices e constraints; não insere conversas. Contagens de pedidos, itens, históricos, pagamentos, clientes, endereços e movimentos, além de somas de saldos/versões do estoque, ficaram iguais antes/depois. Nenhuma operação em parsmartmanager ou reinício do PostgreSQL compartilhado.
- API atualizada em 5080, identificando e reiniciando somente o executável deste projeto; logs `.local/human-chat-api.log` e `.local/human-chat-api-error.log`. Frontend em 8101. **Nove checks de Test-Foundation.ps1 -ExpectDatabaseReady aprovados**. Verificações HTTP adicionais, somente leitura: chat público sem credencial retorna 404 genérico/no-store; chat da equipe sem JWT retorna 401; preflight CORS aceita a origem local e X-Chat-Access.
- Conferência visual com frontend real e API totalmente interceptada por dados simulados: cliente, fila e conversa da equipe em desktop/celular, sem erros JavaScript nem transbordamento horizontal. Capturas inspecionadas `.local/human-chat-desktop-customer.png`, `.local/human-chat-mobile-customer-reply.png`, `.local/human-chat-desktop-queue.png` e `.local/human-chat-mobile-staff-reply.png`; helper `.local/Preview-HumanChat.cjs`. O fluxo com gravações foi exercitado no banco isolado dos testes.
- README, banco, roteiro, arquitetura, API, regras, decisões e guia do chat atualizados. A 8E permanece local e não commitada; 8A–8D estão preservadas em commits locais. Sem push, PR ou integração à master nesta continuidade. Aceite manual e publicação pendentes. A Fase 8 ainda terá a integração visual mais ampla entre conversa/cardápio; entrega pública depende de cobertura/frete. IA pertence à Fase 9.

## Publicação dos incrementos 8A a 8E — 05/10/2026

O responsável solicitou commitar e publicar todo o trabalho seguindo o fluxo habitual de branches, PRs, checks e squash na master. As menções a publicação pendente nas seções anteriores registram o estado ao concluir cada implementação. A autorização atual não comprova execução manual integral dos roteiros.

| Incremento | Commit original da implementação | Pull request | Squash na master |
| --- | --- | --- | --- |
| 8A — cardápio público | `4bcd978` | [#13](https://github.com/Pauliinrodrigues/MadeinMinasApp/pull/13) | `50dda01` |
| 8B — carrinho público | `15d8107` | [#14](https://github.com/Pauliinrodrigues/MadeinMinasApp/pull/14) | `9c3074d` |
| 8C — checkout para retirada | `c8de7ee` | [#15](https://github.com/Pauliinrodrigues/MadeinMinasApp/pull/15) | `e9183cc` |
| 8D — acompanhamento do pedido | `687e10c` | [#16](https://github.com/Pauliinrodrigues/MadeinMinasApp/pull/16) | `735d63a` |
| 8E — atendimento humano | `720f586` | [#17](https://github.com/Pauliinrodrigues/MadeinMinasApp/pull/17) | Commit final de merge registrado no PR |

- As cinco branches foram publicadas no repositório **Pauliinrodrigues/MadeinMinasApp**, com autoria Pauliinrodrigues. Os PRs foram direcionados à master em sequência. Antes de atualizar cada base, foi conferido que a árvore da master era idêntica à do antecessor original; a reconciliação de ancestralidade preservou os arquivos da funcionalidade, sem force push. O resultado de cada squash foi comparado ao conteúdo esperado.
- **Exceção autorizada para esta publicação:** em 05/10/2026, o responsável confirmou expressamente integrar tudo à master usando os testes locais aprovados, mesmo com checks remotos pendentes/cancelados durante o [incidente do GitHub Actions](https://stspg.io/c11dc9nb1zdq). A política geral de aguardar os checks permanece; esta autorização é específica para os PRs #13–#17. Não se declara sucesso de um check pendente ou cancelado.
- Antes das atualizações de base, backend e frontend haviam passado nos commits originais dos PRs #14 e #16. Backend também aprovado nos PRs #13 e #17; frontend aprovado no PR #15. Os três jobs restantes ficaram sem runner ou foram cancelados durante a espera, e foram retomados individualmente. As atualizações de base/documentação disparam novos workflows, cujo resultado deve ser consultado nos PRs e na master. Nenhuma falha de teste foi reportada na conferência anterior aos merges.
- O código final corresponde ao incremento 8E validado localmente: **473 testes backend e 136 cenários distintos de navegador aprovados**, além de builds, lint, formatação e verificações HTTP. As mudanças desta publicação ajustam ancestralidade e documentação; não mudam regras de negócio ou testes. A autorização para integrar não comprova execução manual integral dos roteiros.
- Nenhuma migration é reaplicada pela publicação; não há movimentação comercial, alteração de saldo ou operação no PostgreSQL compartilhado. Segredos, backups, logs e artefatos locais permaneceram fora do Git. As instruções para outra máquina continuam no README e em database/README.md.

## Fase 8F — entrega pelo site — 05/10/2026

Implementação, testes e publicação por branch/PR/squash na master autorizados pelo responsável. A branch `feat/public-delivery` parte de `0b7cc92`; a autorização não comprova aceite manual integral pelo responsável. Contratos, configuração e roteiro em [public-delivery.md](public-delivery.md).

- **502 cenários distintos de backend aprovados:** regressão inicial 499/499 (14 min), seguida por quatro verificações finais aprovadas (36 s): três casos adicionais de confirmação/compatibilidade e repetição do fluxo completo acrescentando a impressão. São 29 casos novos sobre os 473 anteriores. Testes incluem região/taxa/endereço, entradas adulteradas, mudança após revisão, ausência de efeitos parciais, idempotência concorrente, privacidade do cadastro, fluxo completo com estoque/pagamento/expedição, histórico, constraints e proteção da reversão. Logs ignorados `.local/public-delivery-backend-tests.log` e `.local/public-delivery-backend-extra.log`.
- Compilação Release da solução com zero avisos/erros; modelo EF correspondente à migration; C# formatado. Prettier, lint e build Angular aprovados. Nenhuma dependência nova. A primeira verificação de lint apontou três condicionais sem chaves, corrigidas antes da execução do navegador.
- Conferência visual com API simulada, desktop/celular, sem erros JavaScript ou transbordamento horizontal. Capturas `.local/public-delivery-desktop-address.png`, `.local/public-delivery-mobile-review.png` e demais variantes, inspecionadas. Helper ignorado `.local/Preview-PublicDelivery.cjs`; não cria pedidos reais.
- Migration `20261005210534_AddPublicDelivery` aplicada exclusivamente em `made_in_minas`, após verificação de host/porta/usuário/banco e backup legível `.local/backups/made_in_minas-before-public-delivery-20261005-182530.dump`. Contagens de pedidos, itens, históricos, pagamentos, clientes, endereços/movimentos e somas de saldos/versões do estoque ficaram iguais antes/depois. Nenhuma operação em parsmartmanager ou reinício do PostgreSQL compartilhado.
- Cobertura/taxas reais ainda aguardam resposta do responsável. Configuração operacional permanece vazia; o código permite entrega após configuração, sem presumir taxa nem região atendida. Valores dos testes são fictícios. Pagamento online, cálculo por distância e administração visual de frete ficam fora deste incremento.
- **164/164 cenários de navegador aprovados** em 15,5 min, desktop/mobile: cardápio, carrinho, checkout, acompanhamento e pedidos. Casos novos cobrem endereço/taxa, alternância para retirada, validação, cobertura vazia, falha de consulta, perda de resposta, restauração após rejeição e textos de entrega. API simulada; log `.local/public-delivery-browser-tests.log`.
- API reiniciada em 5080 com a versão atualizada, frontend em 8101. Nove checks de fundação aprovados com readiness Healthy. GET real de regiões retornou 200/no-store e lista vazia, confirmando que nenhuma cobertura/taxa fictícia foi habilitada. Logs `.local/public-delivery-api.log` e `.local/public-delivery-api-error.log`.

## Ajustes operacionais e de usabilidade — 06/10/2026

Rodada autorizada antes de avançar à próxima fase, na branch `feat/operational-usability`, a partir da master `643aa0f` (PR #18). Plano, arquivos, limitações e roteiro de aceite em [operational-usability.md](operational-usability.md).

- **508/508 testes backend aprovados**, em aproximadamente cinco minutos, por `scripts/Test-Authentication.ps1`. Seis casos novos verificam disponibilidade por ficha/ingrediente/saldo, consumo conjunto, arredondamento e busca. Banco isolado em `127.0.0.1:55433`, encerrado/removido ao terminar. Uma tentativa anterior redirecionando a saída do helper ficou presa na inicialização do PostgreSQL por herança de handles; foi interrompida apenas a instância temporária verificada, e a execução sem redirecionamento passou. Nenhuma alteração no PostgreSQL compartilhado.
- **530 cenários distintos de navegador aprovados em execuções combinadas**, com API simulada. A regressão ampla (`npm run test:e2e -- --workers=2 --timeout=60000 --max-failures=8`) aprovou 514 e encerrou com oito falhas, um interrompido e sete não executados. Quatro falhas eram as expectativas antigas de destino após login/expiração em desktop/celular; foram ajustadas ao destino por perfil e ao `returnUrl`. Três ocorreram no encerramento do contexto do Edge; um worker preso foi encerrado após conferir PID, comando e processo pai, afetando somente o navegador automatizado. Não houve alteração de regra comercial para ocultar esses problemas.
- A repetição `npm run test:e2e -- --last-failed --workers=1 --timeout=60000` aprovou os oito casos. A execução móvel de `e2e/staff.spec.ts:194`, `e2e/staff.spec.ts:361` e `e2e/stock.spec.ts` aprovou os oito restantes. As execuções terminaram com código zero. O registro da primeira seleção de falhas está em `.local/operational-full-last-run.json`, ignorado pelo Git.
- Após a revisão final de persistência, dois testes de pedidos foram ampliados: resposta perdida seguida por reload/login/401, e resposta perdida seguida por 429. Os **quatro cenários desktop/celular passaram** com `npm run test:e2e -- e2e/orders.spec.ts --grep "tentativa incerta sobrevive|falha incerta conserva" --workers=1 --timeout=60000`. Comprovam corpo e chave idênticos em todas as retomadas, edição bloqueada e limpeza após sucesso; não representam quatro casos distintos adicionais aos 530.
- Compilação do backend e formatação C# aprovadas. Build de produção Angular, `npm run lint` e `npm run format:check` aprovados. As condicionais novas foram ajustadas à regra `curly` antes da verificação final. Sem dependências, migrations ou alteração do modelo do banco.
- Inspeção visual de navegação, pedidos, cardápio, carrinho e checkout em desktop/celular, com dados simulados e chamadas de API interceptadas. Helpers `.local/Preview-OperationalUsability.cjs` e `.local/Preview-PublicDelivery.cjs` terminaram com código zero; sem erros JavaScript ou transbordamento nas verificações de largura. Conferida também a posição do campo de busca em foco: inteiro abaixo da barra do carrinho e dentro da tela. Capturas `.local/usability-*.png` e `.local/public-delivery-*.png`, inspecionadas; nenhuma compra real criada.
- API em 5080 executando uma cópia do artefato Release testado em `.local/operational-api-runtime-20261006`, com diretório de trabalho e configuração Development deste projeto. Frontend em 8101. Readiness `Healthy`; busca pública sem correspondência retorna zero itens; revisão pública devolve o preço atual. Logs `.local/operational-runtime.log` e `.local/operational-runtime-error.log`.
- Consulta pública local encontrou um produto com URL de imagem e capacidade de produção positiva, além de zero regiões de entrega. Esses dados diferem do levantamento anterior; não foram inventados saldos, fotos, taxas, clientes ou contas da equipe. Nenhuma operação em `parsmartmanager` e nenhum reinício do PostgreSQL compartilhado.
- README, roteiro, arquitetura, API e documentação dos fluxos atualizados. Trabalho local na branch, sem commit, push, PR ou integração à master nesta rodada. O aceite com equipe, impressora e dados operacionais reais permanece pendente.

## Continuação — administração de regiões e taxas — 06/10/2026

Incremento D na mesma branch `feat/operational-usability`, autorizado pelo pedido de continuidade. Escopo, contratos, arquivos e roteiro de aceite em [delivery-settings.md](delivery-settings.md).

- **531/531 testes backend aprovados** pela execução completa de `scripts/Test-Authentication.ps1`, em aproximadamente sete minutos, com PostgreSQL temporário em `127.0.0.1:55433`, encerrado/removido ao terminar. Inclui 23 novos casos de permissões, compatibilidade com configuração legada, validação, edições simultâneas, repetição de resposta, revisão/taxa e preservação de pedidos, além de proteção do Down. A seleção focada de regiões/checkout/entrega também aprovou **85/85**.
- O primeiro teste isolou um erro de cultura na leitura do limite decimal pelo `RangeAttribute`. `ParseLimitsInInvariantCulture` corrigiu a causa; entrada inválida retorna 400 e a taxa válida funciona no ambiente brasileiro. O teste de rollback da entrega pública passou a citar a migration que realmente verifica, em vez de assumir que ela é sempre a última.
- Build Release sem avisos/erros, formatação C# e `has-pending-model-changes` aprovados. SQL gerado/revisado em `.local/delivery-settings.sql`: cria somente `DeliverySettings` e registra a migration. Não altera tabelas comerciais nem insere regiões.
- Migration `20261006174915_AddDeliverySettings` aplicada exclusivamente em `made_in_minas`, após verificar host/porta/usuário/banco e criar `.local/backups/made_in_minas-before-delivery-settings-20261006-151357.dump`, cujo índice foi conferido com `pg_restore`. Contagens de pedidos/itens/históricos/pagamentos/clientes/endereços/movimentos e somas de saldos/versões de estoque permaneceram iguais. A nova tabela contém **zero linhas**. Nenhuma operação em `parsmartmanager` ou reinício do PostgreSQL compartilhado.
- API atualizada em 5080 a partir do artefato Release testado, com cópia em `.local/delivery-settings-api-runtime-20261006`; frontend em 8101. Os **nove checks** de `Test-Foundation.ps1 -ExpectDatabaseReady` passaram. Readiness Healthy, cobertura pública `[]`, acesso administrativo sem JWT rejeitado com 401. Logs `.local/delivery-settings-api.log` e `.local/delivery-settings-api-error.log`.
- **20/20 cenários da nova tela aprovados** em desktop/celular, com API simulada: cadastro/revisão/edição/pausa/reativação, taxa explícita/precisão/duplicidade, conflito com recarga, envio pendente e incerto, falha de consulta, três perfis sem acesso, 403 e 401. Os seletores de UF/situação dos testes foram corrigidos para o papel acessível `combobox`. Durante a conferência visual, o foco antecipado foi substituído por `afterNextRender`; recarga do servidor de desenvolvimento durante os ajustes interrompeu uma sessão de teste, repetida sem editar arquivos da aplicação.
- Regressão de sessão e checkout: **35 cenários desktop e 35 móveis aprovados**, com API simulada. A seleção inicial de 90 casos terminou com 47 aprovados, três falhas dos novos testes e 40 não executados; as causas estão descritas acima. A execução final aprovou os 20 casos de regiões em 1,8 minuto, seguida dos 35 de sessão/checkout no celular. Com os 35 desktop já aprovados, são **90 cenários distintos validados em execuções combinadas**, sem declarar uma execução única integral sem falhas. Não foi repetida a suíte inteira de navegador da rodada A–C.
- Build de produção Angular, lint sem avisos e Prettier aprovados após os ajustes finais de foco e apresentação. Inspeção visual da lista, formulário e revisão em desktop/celular com `.local/Preview-DeliverySettings.cjs`: sem erros JavaScript ou transbordamento horizontal. Checkbox usa o padrão existente da equipe. Capturas `.local/delivery-settings-*.png` conferidas; todos os dados da prévia foram interceptados e não gravados na operação.
- Trabalho local, sem commit/push/PR nesta continuação. O administrador deve entrar novamente para carregar `delivery.manage` e informar somente regiões/taxas reais. Nenhum aceite operacional do responsável foi presumido.

## Continuação — filtros de pedidos — 06/10/2026

Incremento E na branch `feat/operational-usability`, autorizado pelo pedido de continuidade. Escopo, contratos, arquivos e roteiro em [order-filters.md](order-filters.md). Não cria migration, tabela, dependência nem registros na operação.

- **55 cenários distintos de backend aprovados em execuções combinadas**, selecionando `OrderListTests`, `OrderTests` e `PaymentTests` pelo helper isolado. A primeira execução aprovou 55 de 57; duas expectativas novas supunham 400 para parâmetros contendo somente espaços. O ASP.NET converte esses parâmetros opcionais em ausência, como os demais filtros. As expectativas foram corrigidas para verificar consulta sem filtro no caso combinado, preservando rejeição de valores desconhecidos. A repetição final aprovou **10/10** casos de filtros e ciclo de pagamentos. Não se declara execução única integral sem falhas nem repetição de toda a suíte backend.
- Cobertura nova: origem, situação financeira atual, ausência de pagamento, intenção cancelada, devolução, substituição por nova tentativa, pedidos encerrados fora de A receber, combinações com busca/status/cliente, contador/paginação, ausência de duplicidade e leitura sem escrita. O fluxo real de criação, recebimento, devolução e substituição também verifica a lista sem mudar o estado de produção.
- PostgreSQL temporário em `127.0.0.1:55433`, encerrado/removido pelo helper ao terminar. API local atualizada a partir do artefato Release testado em `.local/order-filters-api-runtime-20261006`, porta 5080; frontend em 8101. Os **nove checks de fundação passaram** na repetição. A primeira consulta de readiness excedeu o tempo de abertura da conexão, com `OperationCanceledException` registrado; nenhuma configuração de timeout ou do banco foi alterada para contornar isso. A máquina estava com menos de 500 MB de memória física livre durante as verificações.
- Nenhuma operação em `parsmartmanager`, reinício do PostgreSQL compartilhado ou lançamento fictício na operação. Logs da API em `.local/order-filters-api.log` e `.local/order-filters-api-error.log`. Trabalho local, sem commit, push ou PR nesta continuação.
- **26/26 cenários de navegador aprovados**, desktop/celular, com API simulada, em 2,4 minutos e código de saída zero. Seleção: quatro cenários novos de filtros em ambos os tamanhos (oito casos), paginação/falha de consulta, avisos/pausa com aba oculta, bloqueio de cozinha/expedição e retorno/destino após login. Comando: `npm run test:e2e -- e2e/orders.spec.ts e2e/operational-usability.spec.ts --grep "fila detecta|lista pagina|origem e pagamento|atalho a receber|atualização usa filtros|falha automática|não acessa lista|login abre a área|login retorna" --workers=1 --timeout=60000 --max-failures=3`. Não foi repetida a suíte completa de navegador.
- Formatação C# conferida após o ajuste final dos testes, com `dotnet format whitespace --verify-no-changes --no-restore` nos quatro arquivos deste incremento; código zero. `git diff --check` sem erros.
- Build de produção Angular, lint sem avisos e Prettier aprovados após os ajustes finais. Inspeção visual com `.local/Preview-OrderFilters.cjs`, em desktop/celular, sem erros JavaScript ou transbordamento horizontal; capturas `.local/order-filters-*.png` conferidas. Corrigida quebra do número na tabela e acrescentado destaque do filtro rápido ativo. A primeira prévia móvel parou por seletor que não considerava os rótulos dos cartões; a árvore acessível confirmou os rótulos, e o helper/testes passaram a localizar a coluna pelo `data-label`. A execução final do helper terminou com código zero. Todos os dados da prévia foram interceptados.

## Continuação — central de reposição — 06/10/2026

Incremento F na branch `feat/operational-usability`, autorizado pelo pedido de continuidade. Contrato, arquivos, regras e roteiro em [stock-replenishment.md](stock-replenishment.md). Sem migration, dependência nova ou escrita comercial pela central.

- **44/44 testes backend aprovados**, em 52 segundos, por `scripts/Test-Authentication.ps1 -Filter 'FullyQualifiedName~StockReplenishmentTests|FullyQualifiedName~MadeInMinas.Api.Tests.StockTests|FullyQualifiedName~IngredientTests'`. Inclui nove novos casos de autorização, mínimos/precisão/unidades, ausência de movimentação, fila de atenção, inclusão de inativos, busca Unicode por nome/fornecedor, contadores/paginação, consulta vazia, validação e atualização após entrada/edição/inativação. A consulta preserva saldos, versões, custos e histórico. Regressão dos lançamentos e cadastro incluída; não foi repetida toda a suíte backend.
- Compilação Release e formatação C# aprovadas. Banco PostgreSQL temporário em `127.0.0.1:55433`, encerrado e removido pelo helper. Nenhuma operação em `parsmartmanager`, reinício do PostgreSQL compartilhado ou lançamento fictício na operação.
- API local em 5080 atualizada com o artefato Release testado em `.local/replenishment-api-runtime-20261006`; frontend em 8101. **Nove checks de fundação aprovados**, incluindo readiness Healthy. Logs `.local/replenishment-api.log` e `.local/replenishment-api-error.log`, fora do Git.
- Trabalho local, sem commit, push ou PR nesta continuação. Contagem física, fornecedores e mínimos reais continuam dependendo da operação; o painel não cria inventário nem compra automática. O aceite do responsável não foi presumido.
- **30/30 cenários de navegador aprovados**, com API simulada, por `npm run test:e2e -- e2e/replenishment.spec.ts e2e/stock.spec.ts --workers=1 --timeout=60000 --max-failures=3`. São 18 casos da central e 12 do estoque existente, em desktop/celular. Incluem autorização, sessão expirada, filtros/paginação, contadores, ausência de escrita pela central, atalhos, retorno com saldo atualizado e regressão das confirmações/reenvios. Execução encerrada com código zero em 12,3 minutos; o encerramento final do Edge foi lento, mas concluiu sem intervenção ou falha de teste. Não foi repetida toda a suíte frontend.
- Lint sem avisos, Prettier e build de produção Angular aprovados. A inspeção visual levou a dois ajustes de CSS: números dos contadores acima dos rótulos e largura suficiente para a situação selecionada. Após esses ajustes, build e Prettier passaram novamente; **os dois cenários de apresentação desktop/celular foram repetidos e passaram**, em 48 segundos, com código zero. Não são dois casos distintos adicionais aos 30. Capturas `.local/replenishment-*-overview.png` e `.local/replenishment-*-cards.png` conferidas; sem erros JavaScript ou transbordamento horizontal da página. Dados simulados, sem lançamentos na operação. `git diff --check` sem erros.

## Fechamento dos ajustes operacionais — 06/10/2026

O responsável aceitou concluir todos os ajustes da rodada e publicar commit/PR/master. A matriz de reconciliação com a análise original está em [operational-usability.md](operational-usability.md). Esse aceite não substitui ensaio com funcionários e impressora reais.

- Backend: **551/551 testes aprovados** em uma execução integral com `scripts/Test-Authentication.ps1`, build Release e PostgreSQL isolado em `127.0.0.1:55433`. O helper encerrou o cluster ao terminar, saída 0. Inclui permissões, concorrência, estoque, pagamentos, entrega, reposição e indicadores de ficha/custo no catálogo. Os testes não usaram o banco operacional.
- Frontend: lint sem avisos, Prettier e build de produção aprovados. Nenhuma nova dependência em G/H.
- Primeira execução dos 20 cenários novos de histórico/chat: 18 aprovados; duas expectativas do teste de armazenamento bloqueado foram corrigidas. `check()` exigia checkbox marcado, mas a tela corretamente reverte quando a gravação é recusada; o teste agora clica e verifica aviso/opção desmarcada. Não era falha do fluxo. A regressão integrada posterior está registrada abaixo.
- Inspeção visual do chat em desktop/celular: fila/conversa lado a lado no desktop; navegação por tela e ausência de overflow horizontal no celular. Capturas com dados fictícios e APIs simuladas em `.local`, fora do Git.
- API local atualizada com o build Release validado em `.local/operational-usability-api-runtime-20261006`, porta 5080. A primeira readiness excedeu o timeout na partida (`OperationCanceledException`, aproximadamente 6,8 s), durante pressão de memória na máquina. `pg_isready` confirmou que o PostgreSQL aceitava conexões; a repetição completa aprovou **9/9 checks de fundação**, sem alteração de configuração ou reinício do banco. Nova consulta retornou `Healthy`, e o OpenAPI contém ambos os indicadores de catálogo. Frontend continua em 8101; não houve acesso ao banco `parsmartmanager`.
- Regressão integrada da interface: **284/284 cenários aprovados em 20,9 minutos**, uma execução sem falhas, desktop/celular, abrangendo histórico, chat, checkout, pedidos, dashboard, cozinha, expedição/impressão e produtos. Comando: `npm run test:e2e -- e2e/order-history.spec.ts e2e/chat-workspace.spec.ts e2e/public-order-tracking.spec.ts e2e/human-chat.spec.ts e2e/public-checkout.spec.ts e2e/orders.spec.ts e2e/dashboard.spec.ts e2e/kitchen.spec.ts e2e/dispatch-printing.spec.ts e2e/products.spec.ts --workers=1 --timeout=60000 --max-failures=3`.
- Após essa execução, a revisão isolou a leitura das cópias de acompanhamento da aba e do dispositivo: uma cópia corrompida não impede recuperar a outra. Também explicitou o efeito da inativação de categorias sobre o catálogo. Validação complementar abaixo; não se atribui esses últimos ajustes à execução anterior.
- Complemento final: **32/32 cenários aprovados em 1,8 minuto**, por `npm run test:e2e -- e2e/order-history.spec.ts e2e/categories.spec.ts --workers=1 --timeout=60000 --max-failures=2`. Inclui as quatro verificações novas de corrupção parcial (aba/dispositivo × desktop/celular), além da repetição do histórico e da regressão de categorias. São 304 cenários distintos nas duas execuções finais (284 + 32, descontadas 12 repetições), sem representar uma execução única de toda a suíte frontend. Lint novamente aprovado. Capturas do acompanhamento desktop/celular conferidas.
- Publicação autorizada: commit `27db79c` na branch `feat/operational-usability`, autoria `Pauliinrodrigues`, publicado no repositório próprio e apresentado no [PR #19](https://github.com/Pauliinrodrigues/MadeinMinasApp/pull/19). O primeiro push ficou aguardando seleção de conta no GCM; foi encerrado apenas esse processo e repetido com a identidade explícita, concluindo normalmente.
- Revisão do teste de acompanhamento: a segunda aba também usa relógio fixo, para que a expiração da credencial de teste não dependa da data da máquina. **2/2 verificações desktop/celular aprovadas**, sem alteração na aplicação. A publicação desse ajuste renova os checks do PR; o job backend da primeira execução já havia passado. A aprovação final dos checks e integração ficam registradas no próprio PR, sem presumir sucesso de uma execução em andamento.
- A suíte completa do frontend no GitHub aprovou 600 de 602 cenários. As duas falhas eram a mesma expectativa antiga do carrinho em desktop/celular: contava uma única chave em `sessionStorage`, mas o histórico acrescenta sua própria chave. O teste passou a verificar o conteúdo exato do rascunho, sem preços persistidos, e a ausência de gravação permanente. A aplicação não precisou de alteração. Regressão local: **24/24 cenários do carrinho público aprovados em 1,4 minuto**, com lint e Prettier aprovados; a correção segue no PR para nova execução integral do CI.
- A execução seguinte aprovou 601 de 602 cenários e revelou ambiguidade no seletor do teste móvel de reposição: durante a consulta, tanto o aviso de carregamento quanto o contador possuem papel `status`. Os testes agora identificam especificamente o contador. A recuperação após falha também mantém uma resposta pendente para verificar os dados anteriores durante a consulta e os dados novos após a liberação. **18/18 cenários de reposição aprovados localmente em 1,1 minuto**, além de lint e formatação, sem alteração na aplicação ou aumento de timeouts/retries para contornar a falha.
- A terceira execução integral aprovou 601 cenários e isolou uma corrida no teste de atualização da fila: após detectar o início das requisições, avançava outro intervalo sem aguardar as duas respostas; a proteção da aplicação contra consultas sobrepostas podia ignorar esse tick. O teste controla a liberação das respostas e espera o término do carregamento antes de avançar novamente. **18/18 execuções locais aprovadas em 1,6 minuto**: três cenários de polling em desktop/celular, repetidos três vezes. Lint e formatação aprovados; nenhuma regra da aplicação foi alterada.
# Fotos próprias dos produtos — 07/10/2026

Implementação na branch `feat/product-image-uploads`, baseada em `bc030f2`. O cadastro permite selecionar, visualizar, enviar, substituir e remover fotos; o cardápio resolve as imagens próprias na origem da API. JPG, PNG e WebP são validados pelo conteúdo e convertidos para WebP, com orientação corrigida e dimensões limitadas. Armazenamento e backup: [produtos](products.md#imagens).

- Backend: **63/63 cenários** de produtos, fotos e cardápio público aprovados em PostgreSQL isolado. Após o ajuste final de cores/limpeza de arquivos, **23/23 cenários de fotos** aprovados novamente. Cobertura inclui permissões, sessão revogada, formatos reais, dados inválidos, limites, caminhos proibidos, vínculo ao produto, substituição/remoção, leitura anônima, cache, redimensionamento e oito orientações EXIF.
- Frontend: **50/50 cenários** de produtos/cardápio aprovados em desktop e celular com API simulada. Inclui prévia antes do envio, multipart autenticado apenas ao salvar, leitura pública, substituição/remoção, erros de envio, preservação dos campos e repetição sem duplicar upload após falha no cadastro. Capturas locais foram inspecionadas para conferir os controles e a largura no celular.
- Builds da API e frontend de produção, lint e formatação verificados. A API atualizada e o frontend local respondem em 5080 e 8101, respectivamente; readiness retorna `Healthy` e as rotas de fotos constam no OpenAPI.
- Os testes usam banco e diretório de imagens temporários. Para o aceite operacional, selecione uma foto real no cadastro do lanche, salve e confira o resultado no cardápio.

## Taxa fixa, região no checkout e local de retirada — 07/10/2026

Implementação local na branch `feat/public-delivery-flow`, preservando as fotos do commit `c86d7d8`. Regra informada pelo responsável: entrega R$ 5,00; retirada grátis na Rua Esperança, 68 — Clarindo de Paiva. Cidade/UF e bairros efetivamente atendidos ainda precisam ser informados; nenhuma cobertura fictícia foi cadastrada.

- A consulta local de regiões falhava com `42P01` porque faltava `DeliverySettings`. Aplicada a migration existente `20261006174915_AddDeliverySettings`, após conferir o destino `made_in_minas` e criar `.local/backups/made_in_minas-before-delivery-settings-20261007-081409.dump` (76.478 bytes), com índice verificado por `pg_restore`. SQL revisado em `.local/backups/add-delivery-settings-reviewed.sql`. A conferência posterior confirmou os **dois pedidos existentes**, a migration registrada e **zero linhas** de cobertura. Não houve nova migration, alteração de pedidos, criação de compra ou reinício do PostgreSQL compartilhado.
- Backend: **90/90 testes aprovados** em uma execução de `scripts/Test-Authentication.ps1 -Filter 'FullyQualifiedName~DeliverySettingsTests|FullyQualifiedName~PublicDeliveryTests|FullyQualifiedName~PublicCheckoutTests'`, com PostgreSQL isolado em `127.0.0.1:55433`, encerrado pelo helper. Os cinco casos novos verificam informações públicas sem escrita, rejeição de taxas diferentes da fixa, prevalência sobre taxas anteriores, retirada gratuita, preservação/repetição de pedidos antigos e exigência de nova revisão após mudança de taxa.
- Frontend: **86 cenários distintos aprovados em execuções combinadas**, em computador e celular, selecionando `public-checkout.spec.ts`, `delivery-settings.spec.ts` e `order-history.spec.ts`. A primeira execução aprovou 84 e teve duas falhas no mesmo teste de sessão: ele navegava diretamente e supunha retirada inicial. Ajustado para escolher retirada explicitamente, de acordo com o novo padrão de entrega. A repetição dos dois casos passou; nenhuma regra da aplicação foi alterada para contornar a expectativa antiga.
- Os testes novos conferem região visível desde o início, R$ 5,00 compondo o total, taxa administrativa sem edição por bairro, endereço de retirada antes do envio e após reload, recuperação da consulta de informações e ausência de taxa enviada pelo navegador. Todos os pedidos desses testes usam API simulada. Capturas `.local/pickup-checkout-desktop.png` e `.local/pickup-checkout-mobile.png` inspecionadas.
- Build da API sem avisos/erros, build de produção Angular, lint, Prettier e formatação C# aprovados. `git diff --check` sem erros. Não foi repetida toda a suíte do projeto.
- API atualizada em 5080 e frontend em 8101. Consulta real de `/api/public-checkout/options` confirmou `fixedDeliveryFee=5.00`, `pickupFee=0` e o endereço informado; `/api/public-checkout/delivery-areas` retornou 200 com lista vazia. Readiness `Healthy`, rotas de fotos preservadas no OpenAPI e **9/9 checks de fundação aprovados**. Entrega permanece aguardando cadastro dos locais reais; retirada está disponível.

## Cobertura confirmada — todos os bairros de Corinto–MG — 07/10/2026

O responsável confirmou **Corinto–MG** e atendimento em **todos os bairros da cidade**. A configuração inicial passa a conter `corinto-todos-bairros`, com `CoversAllNeighborhoods=true`, taxa R$ 5,00 e retirada na Rua Esperança, 68 — Clarindo de Paiva, Corinto–MG. A cobertura da cidade é selecionada automaticamente quando for a única disponível; o cliente deve informar seu bairro real no endereço.

- Backend: **97/97 testes aprovados** na seleção de `DeliverySettingsTests`, `PublicDeliveryTests` e `PublicCheckoutTests`, por `scripts/Test-Authentication.ps1`, com PostgreSQL temporário exclusivo em `127.0.0.1:55433`, encerrado pelo helper. Os sete casos adicionais conferem bairros diferentes na mesma cidade, obrigatoriedade do bairro, rejeição de bairro livre em região específica, preservação da cópia histórica, repetição sem duplicar, conflito de conteúdo, mudança de cobertura após revisão e preservação do alcance ao salvar/pausar/reativar.
- Frontend: **76/76 cenários aprovados em uma execução**, computador e celular, por `npm run test:e2e -- public-checkout.spec.ts delivery-settings.spec.ts`. Inclui bairro obrigatório, cidade/UF da cobertura, total com R$ 5,00, edição invalidando a revisão, recuperação do mesmo envio após reload, ausência de bairro livre em região específica, retirada sem endereço de entrega e manutenção da cobertura no cadastro administrativo. API simulada, sem pedidos criados na operação.
- Builds da API e Angular aprovados; lint e Prettier aprovados. Capturas `.local/corinto-checkout-desktop.png` e `.local/corinto-checkout-mobile.png` inspecionadas; sem transbordamento horizontal nos testes. Não foi repetida a suíte inteira do projeto.
- API local em 5080 usa `.local/public-delivery-city-build/bin/MadeInMinas.Api/debug/MadeInMinas.Api.dll`, com configuração Development deste projeto; frontend em 8101. Consulta real confirmou uma região ativa: Corinto/MG, todos os bairros, `fee=5.00`, `coversAllNeighborhoods=true`; informações públicas confirmaram retirada gratuita e endereço completo. **9/9 checks de fundação aprovados**.
- Sem nova migration ou alteração de pedidos. A opção de toda a cidade usa a lista JSONB existente, com valor falso para regiões anteriores. A configuração inicial é lida enquanto não houver cobertura salva; depois, a lista administrada no banco continua prevalecendo. Hashes de recuperação anteriores sem bairro livre foram preservados.
