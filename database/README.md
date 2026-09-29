# PostgreSQL de desenvolvimento

A aplicação usa um banco e um usuário próprios no PostgreSQL local da porta 5432. Use uma conta administrativa apenas para prepará-los; a API conecta como `made_in_minas_app`.

## Assistente local

Na raiz do projeto, em um terminal interativo do seu usuário Windows:

```powershell
powershell -NoProfile -File scripts/Configure-DevelopmentDatabase.ps1
```

Digite a senha de `postgres` no campo oculto do terminal. O assistente verifica o usuário e o banco antes de criá-los. Se `made_in_minas_app` já existir, solicita sua senha atual e não a redefine. Para um usuário novo, gera uma senha aleatória. Após testar a conexão, grava a connection string em User Secrets, fora do repositório. Nenhuma tabela é criada e nenhum banco existente é removido. Um banco existente com outro proprietário exige revisão manual.

Depois, reinicie a API e consulte `http://localhost:5080/health/ready`.

O procedimento manual equivalente está abaixo.

## Criar usuário e banco

Abra o psql em um terminal interativo:

```powershell
psql -h localhost -U postgres -d postgres -W
```

Execute, escolhendo uma senha quando solicitado por `\password`:

```sql
CREATE ROLE made_in_minas_app LOGIN;
\password made_in_minas_app
CREATE DATABASE made_in_minas OWNER made_in_minas_app;
```

Esses comandos são para a primeira criação. Se já existirem, confira proprietário e permissões; não remova bancos existentes.

## Configurar a API sem gravar senha no repositório

Na raiz do projeto, PowerShell:

```powershell
$databaseConnection = Read-Host 'Connection string completa' -AsSecureString
$credential = [System.Net.NetworkCredential]::new('', $databaseConnection)
@{ 'ConnectionStrings:DefaultConnection' = $credential.Password } |
    ConvertTo-Json -Compress |
    dotnet user-secrets set --project backend/MadeInMinas.Api
Remove-Variable databaseConnection, credential
```

Informe no prompt uma conexão no formato:

```text
Host=localhost;Port=5432;Database=made_in_minas;Username=made_in_minas_app;Password=<sua senha>;Timeout=5;Command Timeout=5
```

Substitua o marcador localmente. Senhas com caracteres especiais exigem as regras de escape do Npgsql. User Secrets fica fora do repositório, mas não é um cofre criptografado. Não envie sua senha pelo chat.

Alternativa para servidores: variável de ambiente `ConnectionStrings__DefaultConnection`, provisionada pelo gerenciador de segredos da hospedagem.

## Migrations

O `AppDbContext` está registrado com Npgsql e descoberta de configurações de entidades. A ferramenta local `dotnet-ef` está fixada no manifesto da raiz.

```powershell
dotnet tool restore
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet ef dbcontext info --project backend/MadeInMinas.Api
```

A Fase 2A introduz a migration `InitialAccessControl`: tabelas Users e Roles, índice único para login normalizado e quatro perfis iniciais. Não inclui senhas nem usuário administrador.

Em uma instalação nova, configure a chave JWT e aplique a migration revisada:

```powershell
powershell -NoProfile -File scripts/Initialize-Authentication.ps1
dotnet ef migrations script --project backend/MadeInMinas.Api --configuration Release
dotnet ef database update --project backend/MadeInMinas.Api --configuration Release
```

Nenhum `EnsureCreated` ou `Migrate` é executado automaticamente na inicialização da API. Em produção, scripts de migration serão revisados e aplicados durante implantação.

Depois, crie o administrador pelo comando local descrito em [autenticação](../docs/authentication.md#administrador-inicial). Não há cadastro público nem senha padrão.

## Verificação

`GET http://localhost:5080/health/ready` retorna 200 e `Healthy` somente quando o EF Core consegue abrir uma conexão autenticada. Esse teste não verifica tabelas ou migrations futuras.
