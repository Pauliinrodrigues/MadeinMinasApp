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

## Pedidos manuais — Fase 4C

A migration `20261001142005_AddManualOrders` adiciona somente `Orders`, `OrderItems` e `OrderStatusHistory`, com chaves estrangeiras, índices e constraints. Não inclui pedidos de demonstração nem tabelas de pagamento/estoque. Revise [o modelo e as regras](../docs/orders.md) antes de aplicar.

Com a conexão de desenvolvimento já conferida para **made_in_minas**, faça backup desse banco, confira o script e aplique a migration:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet ef migrations script 20260930205926_AddCustomersAndAddresses 20261001142005_AddManualOrders --project backend/MadeInMinas.Api
dotnet ef database update 20261001142005_AddManualOrders --project backend/MadeInMinas.Api
```

O banco deste projeto é `made_in_minas`. Não use conexões de outros projetos. Branches não isolam o banco e nenhuma migration é aplicada automaticamente ao iniciar a API.

## Pagamentos manuais — Fase 4D

A migration `20261001180855_AddManualPayments` cria somente Payments e PaymentStatusHistory, com histórico, FKs, constraints e índice parcial de pagamento ativo por pedido. Não cria transações de demonstração nem integração bancária. Regras e testes: [pagamentos](../docs/payments.md).

Depois de conferir a conexão exclusiva para **made_in_minas** e fazer backup:

```powershell
dotnet ef migrations script 20261001142005_AddManualOrders 20261001180855_AddManualPayments --project backend/MadeInMinas.Api
dotnet ef database update 20261001180855_AddManualPayments --project backend/MadeInMinas.Api
```

Reinicie a API e faça novo login no frontend para carregar as permissões de pagamentos. O rollback desta migration remove as duas tabelas novas; depois de registrar pagamentos, preserve os dados e planeje a recuperação antes de qualquer rollback.

## Cozinha/KDS — Fase 5A

A migration `20261001191445_AddKitchenStatuses` amplia somente as constraints de status e histórico para InPreparation/Ready. Depois de conferir a conexão exclusiva para **made_in_minas** e fazer backup:

```powershell
dotnet ef migrations script 20261001180855_AddManualPayments 20261001191445_AddKitchenStatuses --project backend/MadeInMinas.Api
dotnet ef database update 20261001191445_AddKitchenStatuses --project backend/MadeInMinas.Api
```

Reinicie a API atualizada e faça novo login. Para voltar à migration anterior, não pode haver pedidos nem eventos nos novos estados; não apague histórico de produção para satisfazer as constraints antigas. Regras e roteiro: [cozinha/KDS](../docs/kitchen.md).

## Migration de expedição — Fase 5B

20261001201327_AddDispatchStatuses altera duas constraints existentes. Antes de aplicar em outra instalação, confirme exclusivamente made_in_minas, faça backup e revise a migration:

```powershell
dotnet ef migrations script 20261001191445_AddKitchenStatuses 20261001201327_AddDispatchStatuses --project backend/MadeInMinas.Api
dotnet ef database update 20261001201327_AddDispatchStatuses --project backend/MadeInMinas.Api
```

Não é necessário reaplicar ao trocar de branch se já estiver registrada. Rollback falha quando pedidos ou eventos usam estados novos; não apagar/regravar histórico para forçar retorno. Impressão pelo navegador não cria tabelas ou exige credenciais extras. Operação e limites: [expedição e impressão](../docs/dispatch-printing.md).

## Migration de estoque manual — Fase 6A

`20261002130808_AddIngredientStock` adiciona saldo/versão em Ingredients e a tabela StockMovements. Antes de aplicar em outra instalação, confirme a conexão exclusivamente com made_in_minas, faça backup e revise o script:

```powershell
dotnet ef migrations script 20261001201327_AddDispatchStatuses 20261002130808_AddIngredientStock --project backend/MadeInMinas.Api
dotnet ef database update 20261002130808_AddIngredientStock --project backend/MadeInMinas.Api
```

Reinicie a API após a atualização. Ingredientes existentes começam com saldo de sistema zero; registre a contagem física inicial pela interface. Nenhum pedido antigo é baixado retroativamente. O Down elimina os saldos e o histórico de estoque; não executar em base operacional sem planejamento de recuperação e autorização específica. Roteiro: [estoque manual](../docs/stock.md).

## Migration de consumo por pedidos — Fase 6B

`20261002181034_AddOrderStock` cria a composição histórica, o estado do estoque no pedido e o vínculo de movimentos com pedidos. Confira exclusivamente **made_in_minas**, faça backup e confira fichas/inventário físico antes de ativar:

```powershell
dotnet ef migrations script 20261002130808_AddIngredientStock 20261002181034_AddOrderStock --project backend/MadeInMinas.Api
dotnet ef database update 20261002181034_AddOrderStock --project backend/MadeInMinas.Api
```

Reinicie a API com o código desta branch. Pedidos antigos não Novos ficam Legacy e não alteram estoque retroativamente; Novos exigirão ficha e saldo ao confirmar. A migration não insere movimentos, modifica quantidades nem confirma pedidos. O Down apaga a composição e o vínculo dos movimentos sem devolver saldos, perdendo informação de processamento: não executar após operação sem plano de recuperação. Veja [consumo por pedidos](../docs/order-stock.md).

## Migration de pedidos públicos — Fase 8C

`20261005145517_AddPublicOrders` permite autoria pública na criação e adiciona a chave única de tentativa para DirectLink, preservando os pedidos manuais. Sem tabelas novas ou gravação de pedidos pela migration. Antes de aplicar em outra instalação, confirmar exclusivamente **made_in_minas**, fazer backup e revisar:

```powershell
dotnet ef migrations script 20261002181034_AddOrderStock 20261005145517_AddPublicOrders --project backend/MadeInMinas.Api
dotnet ef database update 20261005145517_AddPublicOrders --project backend/MadeInMinas.Api
```

Reinicie a API atualizada. Não reaplique migrations ao trocar de branch quando já estiverem registradas. Down recusa reversão se houver pedidos públicos, pois o esquema antigo exige autoria de funcionário; não apagar histórico nem criar autores fictícios para forçar retorno. Contrato e roteiro: [checkout público para retirada](../docs/public-checkout.md). Resultado da aplicação local: [validação](../docs/validation.md).

## Migration do atendimento humano — Fase 8E

`20261005183152_AddHumanChat` cria `ChatConversations` e `ChatMessages`, com estados, sequência, autoria e chaves de idempotência. Não altera pedidos, pagamentos, clientes ou saldos. Antes de aplicar em outra instalação, confirme exclusivamente **made_in_minas**, faça backup e revise:

```powershell
dotnet ef migrations script 20261005145517_AddPublicOrders 20261005183152_AddHumanChat --project backend/MadeInMinas.Api
dotnet ef database update 20261005183152_AddHumanChat --project backend/MadeInMinas.Api
```

Reinicie a API com o código atualizado e faça novo login na equipe para carregar `chat.manage`. O Down recusa apagar tabelas quando existem conversas; arquivamento e reversão exigem plano que preserve o histórico. Aplicação local e backup estão registrados em [validação](../docs/validation.md). Contratos e roteiro: [atendimento humano](../docs/human-chat.md).
