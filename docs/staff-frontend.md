# Interface da equipe — Fase 2C

Login e gestão de funcionários em Ionic/Angular, conectados aos contratos das Fases 2A e 2B. Não adiciona tabelas, migrations ou módulos de pedidos.

## Acesso e rotas

Com frontend e API iniciados, acesse http://localhost:8101/entrar ou clique em **Área da equipe** na página pública.

| Rota | Acesso |
| --- | --- |
| / | Página pública |
| /entrar | Login de funcionário |
| /equipe | Conta do funcionário autenticado |
| /equipe/senha | Troca da própria senha |
| /equipe/funcionarios | Gestão, somente users.manage |
| /equipe/funcionarios/novo | Cadastro, somente users.manage |
| /equipe/funcionarios/:id | Edição e reset de senha, somente users.manage |
| /equipe/categorias | Gestão de categorias, somente catalog.manage (Fase 3A) |
| /equipe/categorias/nova | Cadastro de categoria, somente catalog.manage |
| /equipe/categorias/:id | Edição de categoria, somente catalog.manage |
| /equipe/produtos | Gestão de produtos, somente catalog.manage (Fase 3B) |
| /equipe/produtos/novo | Cadastro de produto, somente catalog.manage |
| /equipe/produtos/:id | Edição de produto, somente catalog.manage |

Use a conta administrativa criada pelo comando interativo em [authentication.md](authentication.md#administrador-inicial). Não existe usuário ou senha padrão. Se ainda não houver administrador, execute o bootstrap no Terminal do Rider; nunca informe a senha no chat.

A tela de conta apresenta apenas dados do funcionário. Indicadores operacionais e atalhos para módulos não implementados não são exibidos.

## Sessão e permissões

- JWT e perfil permanecem somente na memória. Não são gravados em localStorage, sessionStorage, cookies ou URL.
- Atualizar/fechar a página exige novo login. Não há refresh token nem renovação automática.
- A validade vem de expiresAt retornado pelo backend; o padrão é 15 minutos. Um temporizador encerra a sessão local ao expirar.
- O interceptor envia Bearer somente para o caminho /api/ na origem configurada, excluindo o login. Aplica timeout de 15 segundos às requisições da API.
- Respostas 401 autenticadas encerram a sessão; 403 mostra falta de permissão. Uma resposta atrasada de outro token não encerra uma sessão nova.
- Guards e menu restringem a navegação. A API permanece responsável por autorizar cada operação.
- Logout solicita revogação de todas as sessões ao backend e limpa a sessão local. Se a chamada falhar, a tela informa que a revogação no servidor não foi confirmada.
- As páginas usam RouterOutlet Angular e a classe ion-page nos contêineres Ionic. A navegação destrói componentes, sem cache de páginas administrativas. Requisições das páginas são canceladas na destruição.
- Troca da própria senha e alteração do próprio login/perfil/status encerram a sessão após confirmação da API.

## Funcionários

Busca por nome/login, filtro por perfil e status, paginação de 20 registros, cadastro, edição, ativação/inativação e reset de senha de outro funcionário.

Perfis são carregados da API. O formulário valida limites dos campos, formato do login e confirmação da senha; o backend revalida os dados. Inativação/ativação pela listagem exige confirmação. A edição informa que login, perfil e status revogam sessões. O último administrador permanece protegido pela API.

Tratamento de login duplicado, último administrador, conta inexistente, credenciais inválidas, limite de tentativas, indisponibilidade e sessão revogada. Falhas técnicas não exibem detalhes internos do servidor.

## Testar

Na pasta frontend/made-in-minas:

```powershell
npm.cmd ci
npm.cmd run build
npm.cmd run test:e2e
```

Os testes Playwright usam Microsoft Edge instalado, com dois projetos: desktop e viewport móvel em Chromium (não é teste em Safari/iPhone físico). O servidor Angular é iniciado automaticamente quando necessário; um servidor existente na porta 8101 é reutilizado fora de CI.

Para usar Chromium do Playwright em outra máquina:

```powershell
npx.cmd playwright install chromium
$env:PLAYWRIGHT_CHANNEL = 'chromium'
npm.cmd run test:e2e
```

A suíte de navegador simula respostas da API e verifica os fluxos da interface sem modificar contas reais. Os testes HTTP do backend com PostgreSQL isolado continuam em scripts/Test-Authentication.ps1. A verificação da integração pública com a API em execução fica em scripts/Test-Foundation.ps1.

Validação manual: entre com seu administrador, crie um funcionário de teste autorizado, altere seu nome/perfil, teste a entrada com essa conta e sua inativação. Verifique também a troca da própria senha. Essas ações alteram o banco configurado; os testes automatizados da interface não realizam essas operações nele.

## Arquivos deste incremento

- src/app/core/auth: contratos, sessão, interceptor e guards.
- src/app/core/services/staff-api.service.ts: chamadas REST de autenticação e funcionários.
- src/app/core/api-error.ts: mensagens de erro.
- src/app/features/auth: login.
- src/app/features/staff: layout, conta, senha, listagem e formulário.
- src/staff.scss: aparência responsiva da área interna.
- app.config.ts, app.component.ts e app.routes.ts: integração HTTP e roteamento.
- features/home: link para a equipe e contêiner Ionic.
- package.json, package-lock.json, playwright.config.ts e e2e/staff.spec.ts: execução e testes.
- README.md e docs: escopo, arquitetura, operação e validação.

Os caminhos src são relativos a frontend/made-in-minas. AuthenticationService.cs e o banco não precisaram de alterações.
