# Autenticação de funcionários — Fase 2A

Primeiro incremento da Fase 2. Implementa backend de autenticação e os perfis iniciais. A gestão de funcionários está implementada na [Fase 2B](users.md), e a interface de login e administração na [Fase 2C](staff-frontend.md).

## Contrato

| Método | Rota | Acesso |
| --- | --- | --- |
| POST | /api/auth/login | Público, com limite de tentativas |
| GET | /api/auth/me | Funcionário autenticado e ativo |
| POST | /api/auth/logout | Funcionário autenticado; invalida todas as suas sessões |
| PUT | /api/auth/password | Funcionário autenticado; exige senha atual e nova |
| GET | /api/roles | Administrador |

Login recebe JSON com username e password. Retorna accessToken, tokenType (Bearer), expiresAt em UTC e user (id, name, username, role, permissions). Nunca retorna hash, senha ou SecurityStamp.

Enviar o token no cabeçalho Authorization: Bearer <token>. Não enviar o token em query string. Respostas de autenticação usam Cache-Control: no-store.

- 400: entrada inválida.
- 401: credencial inválida, conta bloqueada/inativa ou sessão inválida/expirada.
- 403: usuário autenticado sem a permissão necessária.
- 429: limite de requisições de login por origem.

## Regras de credencial e sessão

- Login: 3 a 64 letras ASCII, números, ponto, sublinhado ou hífen; comparação sem distinguir maiúsculas.
- Nome: 1 a 120 caracteres.
- Senha nova: 15 a 128 caracteres, sem composição artificial obrigatória; espaços são permitidos, mas uma senha somente de espaços é recusada.
- Hash com PasswordHasher do ASP.NET Core Identity, salt aleatório e 210.000 iterações; atualização de hashes antigos após autenticação válida.
- JWT assinado com HS256 e chave aleatória de pelo menos 32 bytes, codificada em Base64.
- Validação de assinatura, algoritmo, emissor, audiência, validade, identidade, perfil atual e SecurityStamp.
- Expiração padrão de 15 minutos, sem tolerância adicional e sem renovação automática neste incremento. Ao expirar, fazer login novamente.
- Logout troca o SecurityStamp e invalida todos os tokens anteriores daquele funcionário.
- Inativação, mudança de perfil e mudança de SecurityStamp invalidam o acesso existente na próxima requisição.
- Não existe cadastro público de funcionários.
- Clientes não usam estas contas de funcionário.

O JWT local atende ao sistema interno de uma única aplicação. Não é um servidor OAuth/OIDC. Caso existam aplicativos externos ou federação de identidade, adotar um provedor de identidade com fluxo padronizado será uma decisão específica de arquitetura.

## Proteção do login

Cinco erros consecutivos bloqueiam a conta por 15 minutos. Login e verificação da senha atual durante troca de senha compartilham essa contagem. O bloqueio expirado permite nova tentativa; login válido zera a contagem. Contas desconhecidas, inativas, bloqueadas e senhas incorretas recebem a mesma mensagem no login.

Uma transação com bloqueio da linha do usuário serializa tentativas simultâneas e evita perda de incrementos.

O limiter permite dez tentativas por minuto por IP, sem fila. Seu estado é local ao processo; o bloqueio por conta persiste no PostgreSQL. Atrás de proxy, configurar forwarded headers somente para proxies confiáveis antes de disponibilizar a operação. Em múltiplas instâncias, avaliar limitação compartilhada.

## Perfis e políticas

| Perfil | Políticas |
| --- | --- |
| Administrator | users.manage, catalog.manage, orders.manage, kitchen.work, dispatch.work |
| Attendant | orders.manage |
| Kitchen | kitchen.work |
| Dispatch | dispatch.work |

As políticas de pedidos, cozinha e expedição estão declaradas para uso futuro; os módulos ainda não existem. GET /api/roles já exige users.manage.

A Fase 3A adiciona catalog.manage, exigida pelos endpoints de [categorias](categories.md). Perfis existentes foram preservados; não há migration de permissões, pois as políticas são configuradas no backend.

Controllers exigem autenticação por padrão via filtro MVC. O status técnico e o login são explicitamente anônimos. Health checks continuam públicos e não revelam credenciais.

## Preparação local

Na raiz do projeto:

```powershell
powershell -NoProfile -File scripts/Initialize-Authentication.ps1
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet ef database update --project backend/MadeInMinas.Api --configuration Release
```

O primeiro comando preserva uma chave existente ou gera uma nova em User Secrets. A connection string configurada anteriormente permanece intacta. Em outra hospedagem, usar Jwt__SigningKey, Jwt__Issuer e Jwt__Audience por configuração segura.

A API recusa iniciar com chave ausente, Base64 inválida ou menor que 32 bytes. Nenhum segredo pertence ao appsettings versionado.

## Administrador inicial

Em um terminal interativo do usuário Windows que configurou o banco:

```powershell
dotnet run --project backend/MadeInMinas.Api --configuration Release --no-build --launch-profile http -- --create-admin
```

Informe nome, login e senha duas vezes. A senha não aparece no terminal. Esse comando não abre um servidor HTTP e não contém senha padrão.

Execute o comando manualmente na aba Terminal do Rider. A execução automatizada da IDE pode redirecionar a entrada; nesse caso, o comando recusa continuar sem criar usuário.

O bootstrap exige migrations aplicadas e tabela Users vazia. É protegido contra execução concorrente por lock transacional no PostgreSQL. Quando já existem usuários, recusa criar outro administrador ou redefinir credenciais.

## Testes

```powershell
powershell -NoProfile -File scripts/Test-Authentication.ps1
```

O script inicia PostgreSQL temporário com autenticação SCRAM em 127.0.0.1:55433, aplica a migration e executa os testes HTTP com WebApplicationFactory. Depois encerra a instância e remove seus arquivos e credenciais. A porta deve estar livre.

O projeto de testes recusa connection strings que não apontem ao banco made_in_minas_auth_tests nesse host e porta. Não usa o banco da aplicação. Caminho alternativo das ferramentas: parâmetro -PostgresBin.

Após iniciar a API e o frontend:

```powershell
powershell -NoProfile -File scripts/Test-Foundation.ps1 -ExpectDatabaseReady
```

## Próximos incrementos

Login, sessão e gestão de funcionários no Ionic/Angular estão implementados. O frontend mantém JWT somente na memória e exige novo login ao atualizar a página ou ao expirar. Persistência e renovação de sessão exigirão uma decisão própria em incremento futuro.

Cadastro, edição, inativação, troca de senha e proteção do último administrador foram implementados na [Fase 2B](users.md).

Referências: [validação JWT no ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-jwt-bearer-authentication?view=aspnetcore-10.0), [configuração Identity](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-configuration?view=aspnetcore-10.0) e [autorização por políticas](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/policies?view=aspnetcore-10.0).
