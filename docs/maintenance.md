# Manutenção técnica antes da Fase 4

Incremento autorizado em 30/09/2026: atualizar ferramentas compatíveis, melhorar a leitura do código e automatizar as verificações existentes. Não altera preços, permissões, composição de receitas ou dados comerciais.

## Ferramentas

- .NET SDK 10.0.401, fixado em `global.json`, e runtimes .NET/ASP.NET Core 10.0.12 instalados. Instalador obtido da Microsoft, com SHA512 e assinatura digital conferidos. Resultado 3010: instalação concluída, com reinicialização do Windows pendente por arquivos em uso.
- Angular atualizado sequencialmente 20 → 21 → 22, usando as migrations oficiais. Destino: framework 22.2.1, CLI/build 22.2.0 e TypeScript 6.0.3. Preservado o `provideZoneChangeDetection` já utilizado pelo aplicativo; `withXhr()` mantém o transporte HTTP anterior. A verificação estrita de templates permanece habilitada, sem as supressões de diagnósticos opcionais adicionadas pela migration.
- Node recomendado/CI: 24.21.0. Node do Rider usado localmente: 24.19.0, compatível com a faixa exigida. Nenhum runtime gerenciado pelo Rider foi sobrescrito. O antigo Node global 22.16.0 não atende à nova versão do Angular.
- O instalador oficial de atualização do Node global para 22.23.3 está em `.local/installers/node-v22.23.3-x64.msi`, com SHA256 e assinatura OpenJS verificados. A tentativa sem elevação retornou erro 1925 (privilégio administrativo necessário). A nova tentativa solicita a confirmação padrão do Windows; enquanto não for concluída, use o Node compatível do Rider. Não reiniciar a máquina durante uma instalação em andamento.
- Ionic mantido em 9.0.5, com dependências pares compatíveis. PostgreSQL compartilhado segue 17.5 até combinar a parada descrita abaixo.
- Ferramentas adicionadas: ESLint 10.11.0, `@eslint/js` 10.0.1, typescript-eslint 8.71.0, angular-eslint 22.5.0 e Prettier 3.9.9. São dependências de desenvolvimento, fixadas no manifest/lock. `.npmrc` exige Node compatível e salva versões exatas.

## Código

- `Services/CatalogWriteTransaction.cs` concentra a abertura da transação, bloqueio compartilhado do funcionário e revalidação de sessão/perfil das quatro operações de catálogo. Cada serviço conserva suas exceções e contratos. A transação continua aberta até a persistência e o commit; falhas na autorização a descartam imediatamente.
- `.editorconfig` define a formatação C#, separando instruções e preservando propriedades automáticas compactas. Migrations geradas ficam fora da verificação de formatação.
- Prettier formata TypeScript, templates, estilos e configurações do frontend. ESLint verifica TypeScript e templates Angular, incluindo regras de acessibilidade. Chaves são obrigatórias nos condicionais e laços TypeScript.
- A ficha técnica atualiza os dados exibidos dos ingredientes sem alterar os objetos do catálogo por referência.
- `.gitattributes` mantém finais de linha LF entre Windows e Linux.

Não foram introduzidos repositórios genéricos ou camadas de arquitetura adicionais. Formulários continuam usando o padrão existente; uma troca geral de estratégia de formulários exigiria um incremento próprio.

## Verificações locais

Após instalar as dependências com `npm.cmd ci` na pasta do frontend, execute na raiz:

```powershell
powershell -NoProfile -File scripts/Test-Quality.ps1
```

O comando restaura o backend em modo bloqueado, verifica a formatação C#, compila e executa testes HTTP em PostgreSQL isolado, verifica formatação/lint do frontend, compila e executa os testes de navegador em desktop e celular. Encerra na primeira falha. O banco real não é utilizado pelos testes.

Para aplicar a formatação:

```powershell
dotnet format whitespace MadeinMinasApp.sln --no-restore --exclude backend/MadeInMinas.Api/Data/Migrations
cd frontend/made-in-minas
npm.cmd run format
```

O Playwright usa Edge localmente, com um worker para reduzir disputa por recursos. No CI utiliza Chromium e dois workers. Não há repetição automática para ocultar falhas. Screenshots e traces de falhas ficam nos artefatos ignorados pelo Git.

## GitHub Actions

`.github/workflows/quality.yml` roda em pushes para `master`, pull requests e execução manual. Dois jobs independentes verificam backend e frontend. O backend usa um PostgreSQL 17 efêmero, restrito ao loopback do runner e ao nome/porta aceitos pelo fixture. Não usa credenciais da aplicação. O frontend usa API simulada nos testes de navegador.

O workflow só poderá ser confirmado no GitHub depois de enviado ao repositório. Sua existência não configura automaticamente proteção de branches; isso depende das configurações do repositório. Os testes de navegador simulados não substituem a validação manual com a API real.

## PostgreSQL compartilhado

O serviço local `postgresql-x64-17`, na porta 5432, hospeda `made_in_minas` e `parsmartmanager`. Uma atualização do serviço alcança ambos. Antes de executá-la é necessário combinar a parada e obter backup dos dois bancos e dos objetos globais com uma conta administrativa. O backup da Made in Minas foi criado em `.local/backups`, ignorado pelo Git; não cobre o outro projeto nem os objetos globais.

Procedimento para a atualização 17.5 → 17.11:

1. Confirmar a janela de parada e o acesso administrativo local.
2. Fazer backup de ambos os bancos e dos objetos globais; testar a restauração em instância isolada.
3. Verificar as notas da versão e baixar o instalador Windows do fornecedor oficial, conferindo a assinatura digital.
4. Parar os consumidores e aplicar a atualização da mesma versão principal, preservando diretório de dados, serviço, porta e contas.
5. Conferir versão, conexões dos dois projetos, migrations e `/health/ready`; reiniciar os consumidores.

Não trocar o diretório de dados nem executar `initdb` sobre o cluster existente. A atualização do serviço compartilhado permanece separada das alterações da aplicação.

Referências: [suporte .NET](https://dotnet.microsoft.com/en-us/platform/support/policy), [compatibilidade Angular](https://angular.dev/reference/versions), [atualizações PostgreSQL](https://www.postgresql.org/docs/17/upgrading.html), [instalador PostgreSQL](https://www.enterprisedb.com/downloads/postgres-postgresql-downloads).

## Arquivos deste incremento

Criados:

- `.gitattributes` e `.github/workflows/quality.yml`.
- `backend/MadeInMinas.Api/Services/CatalogWriteTransaction.cs`.
- `frontend/made-in-minas/.npmrc`, `.prettierrc.json`, `.prettierignore` e `eslint.config.mjs`.
- `scripts/Test-Quality.ps1`.
- `docs/maintenance.md` e `docs/customers-plan.md`.

Modificados:

- `global.json`, `.editorconfig`, `.gitignore`, `README.md`, `docs/roadmap.md` e `docs/validation.md`.
- `Services/CategoryService.cs`, `ProductService.cs`, `IngredientService.cs` e `RecipeService.cs`: uso da transação compartilhada.
- C# da API e dos testes: somente formatação nos demais arquivos; migrations foram excluídas dessa operação.
- `frontend/made-in-minas/package.json`, `package-lock.json`, `.nvmrc`, `playwright.config.ts` e configurações TypeScript: ferramentas, versões e comandos de qualidade.
- `src/app/app.config.ts`: migração HTTP oficial; `features/catalog/recipe.page.ts`: atualização imutável dos ingredientes da ficha.
- Demais arquivos de `src`, `e2e` e configurações do frontend: formatação e chaves explícitas nos condicionais/laços.

As alterações e arquivos da Fase 3D que já estavam no diretório foram preservados. Este incremento não cria migration nem modifica a estrutura do banco.
