# Ambientes local teste e produção

O Made in Minas usa três ambientes principais. Uma alteração é desenvolvida localmente, validada em teste e depois publicada em produção. Cada ambiente tem banco, usuários, segredos, arquivos, chaves e fila de impressão próprios. A configuração dos ambientes está preparada no repositório; os endereços externos dependem da hospedagem.

| Ambiente | Uso | Backend | Frontend | Banco sugerido |
| --- | --- | --- | --- | --- |
| Local | Desenvolvimento no computador | `Development` | `development`, API em `localhost:5080` | Banco local atual `made_in_minas` |
| Teste e homologação | Validar a versão com dados fictícios antes da operação | `Staging` | `staging`, API relativa `/api` | `made_in_minas_staging` |
| Produção | Operação real da hamburgueria | `Production` | `production`, API relativa `/api` | `made_in_minas_production` |

O ambiente `Testing` é reservado aos testes automatizados, que criam PostgreSQL temporário na porta 55433. Ele não é a homologação. Os perfis `pilot` e `rehearsal` são laboratórios locais de IA existentes; continuam com bancos isolados e não substituem `Staging`.

## Desenvolvimento local

```powershell
dotnet run --project backend/MadeInMinas.Api --launch-profile http
```

Em outro terminal, na pasta `frontend/made-in-minas`:

```powershell
npm start
```

O frontend abre em `http://localhost:8101`, identificado como **LOCAL · DESENVOLVIMENTO**. O banco atual foi preservado, inclusive os registros existentes. Não executar limpeza ou carga fictícia nesse banco. As credenciais locais continuam no User Secrets de desenvolvimento.

## Homologação

O build de teste usa otimização e limites de tamanho equivalentes aos de produção, sem source maps. A faixa **TESTE · HOMOLOGAÇÃO** fica visível durante a navegação. O endereço deve ser próprio, por exemplo `homologacao.loja.example.invalid`; esse nome é um exemplo a substituir, não um serviço publicado. A API deve estar no mesmo domínio, em `/api`.

Preparação local do pacote, sem implantação:

```powershell
powershell -NoProfile -File scripts/Publish-Release.ps1 -Version 0.1.0-preview.20261028.4 -Environment Staging
```

Saída em `.local/releases/0.1.0-preview.20261028.4-staging`. O manifesto registra `targetEnvironment=Staging`, versão, commit-base, alterações sem commit e hashes. O build isolado do frontend também está disponível em `npm run build:staging`.

Para hospedar, adaptar [variáveis de homologação](../deploy/api.staging.env.example), [serviço](../deploy/made-in-minas-staging.service.example) e [proxy](../deploy/nginx.staging.conf.example). Os modelos usam processo, porta interna, usuário de serviço, banco e diretórios distintos dos de produção. O proxy de teste restringe acesso por IP e adiciona `noindex`; substituir o IP de documentação pelo acesso aprovado da equipe ou usar VPN/controle do host. A identificação visual e `noindex` não substituem a restrição de acesso.

Usar clientes e pedidos fictícios. Não copiar contatos, conversas, endereços ou dump operacional para uma homologação pública. É possível preparar o cardápio com dados comerciais aprovados, mantendo todos os registros de atendimento fictícios. O ensaio local de restauração existente permanece privado e tem outra finalidade.

## Produção

```powershell
powershell -NoProfile -File scripts/Publish-Release.ps1 -Version 0.1.0-preview.20261028.4 -Environment Production
```

Saída em `.local/releases/0.1.0-preview.20261028.4-production`. A aplicação não exibe faixa de teste. Adaptar [variáveis de produção](../deploy/api.env.example), [serviço](../deploy/made-in-minas.service.example) e [proxy](../deploy/nginx.conf.example). Não servir o pacote `staging` aos clientes reais.

Produção começa com IA desligada. Chave, modelo, cota e ativação devem ser definidos por ambiente; homologar o provedor com dados fictícios antes da liberação pública. A validação da V1 e as condições pendentes continuam no [registro de preparação](v1-release.md).

## Isolamento e configuração

- Definir `DOTNET_ENVIRONMENT` e `ASPNETCORE_ENVIRONMENT` com o mesmo valor exato: `Staging` ou `Production`. Usar `dotnet MadeInMinas.Api.dll` no serviço; perfis de `launchSettings.json` são para desenvolvimento.
- `appsettings.Staging.json` e `appsettings.Production.json` contêm apenas padrões sem segredos. User Secrets locais não são carregados nesses ambientes; fornecer conexão, JWT, certificados e chave de IA pelo gerenciador do host.
- `Deployment__DatabaseName` deve coincidir com `Database` na conexão. A API recusa iniciar em `Staging` ou `Production` quando esses nomes divergem. Se o provedor exigir outro nome, ajustar ambos. Essa verificação reduz erros de configuração; permissões de banco e credenciais independentes continuam necessárias.
- Criar roles distintas, sem acesso ao banco do outro ambiente. Usar chaves JWT diferentes e os emissores/audiências próprios fornecidos nos modelos. Não reutilizar senhas de equipe ou de administrador.
- Manter fotos, backup, certificados e key rings em diretórios separados. A aplicação distingue também o escopo Data Protection por ambiente: uma credencial pública de homologação não abre uma conversa ou pedido em produção, mesmo que o armazenamento tenha sido compartilhado por engano. O escopo local existente foi preservado.
- Manter estações, tokens e pastas de estado do agente de impressão independentes. Teste não deve usar a credencial ou a fila operacional. Não habilitar impressão automática na homologação sem reservar explicitamente um equipamento de teste.
- Usar domínios distintos, em vez de caminhos diferentes no mesmo domínio, para separar também o armazenamento do navegador. Backup, migração e restauração sempre precisam identificar o ambiente de destino.

## Caminho até a publicação

1. Desenvolver e executar checks locais com banco de testes isolado.
2. Gerar pacotes de teste e produção da mesma revisão. O backend é o mesmo código; o frontend varia na identificação do ambiente. Os hashes dos dois pacotes não são iguais.
3. Implantar o pacote de teste na hospedagem de homologação, aplicar migrations apenas no banco de homologação e executar o roteiro da V1 com a equipe.
4. Registrar a versão aprovada. Qualquer correção posterior exige novo pacote e nova validação do comportamento afetado.
5. Com publicação autorizada, fazer backup, conferir o banco de destino, aplicar as migrations revisadas e instalar o pacote de produção da revisão aprovada.
6. Verificar `/health/ready`, `/api/system/status`, fotos, acessos e impressão. O status informa a versão e o ambiente da API sem divulgar conexão ou segredos. Manter o artefato anterior e um plano de retorno compatível com o schema.

As configurações nomeadas do frontend seguem o mecanismo de [ambientes do Angular](https://angular.dev/tools/cli/environments); os nomes do backend seguem os [ambientes do ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/environments?view=aspnetcore-10.0). Nenhum template deste incremento foi instalado em hospedagem externa.

## Validação da separação de ambientes

Em 28/10/2026, passaram dez testes direcionados do backend, seis cenários de navegação/impressão e as doze combinações de rota, ambiente e dispositivo verificadas nos builds otimizados. Os testes confirmaram banco incompatível rejeitado antes de abrir conexão, erros sem credenciais, escopo local preservado, acesso público rejeitado entre homologação e produção e restauração do acesso após reiniciar o provedor no mesmo ambiente. A faixa não aparece nas comandas e não altera a altura das bobinas de 58/80 mm. Não houve chamadas de IA, gravação de pedidos operacionais ou impressão física.

O verificador dos builds usa servidores temporários locais e respostas simuladas. Pode ser repetido na pasta `frontend/made-in-minas`:

```powershell
npm run build -- --output-path .local/environment-builds/production
npm run build:staging -- --output-path .local/environment-builds/staging
node ../../scripts/Test-EnvironmentBuilds.mjs .local/environment-builds
```

O mesmo roteiro foi acrescentado à CI. Lint, formato e builds passaram localmente; a execução remota sobre um futuro commit ainda é necessária. Logs locais têm prefixo `.local/environments-`; capturas e resumo ficam em `frontend/made-in-minas/.local/environment-builds`. A hospedagem deverá validar os templates Linux, certificados, restrição de acesso, bancos e restauração no destino escolhido.
