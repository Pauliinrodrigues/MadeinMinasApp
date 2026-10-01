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
