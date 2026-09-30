# Clientes e endereços — Fase 4A

Incremento na branch `feat/customers-addresses`, iniciado após o aceite das fichas técnicas e integração do PR #1. Administrador e atendente mantêm clientes e seus endereços na área da equipe. Pedidos, carrinho, pagamentos e identificação pública do cliente continuam fora deste incremento.

## Regras

- Cliente exige nome (até 120 caracteres) e telefone. `isActive` assume `true` quando omitido no cadastro. Não há senha de cliente.
- Telefone aceita DDD brasileiro, fixo com oito dígitos iniciado em 2–5 ou celular com nove dígitos iniciado em 9. Aceita espaços, parênteses, pontos, hífens e prefixo 55 ou +55. Persiste como `+55` seguido de DDD/número, sem completar dígitos ausentes.
- A validação é de formato; não verifica se o número existe ou pertence ao cliente. Telefone não autentica ninguém. A interface não oferece consulta pública de dados pessoais.
- Telefone é único, inclusive entre inativos. Conflitos simultâneos são resolvidos pelo índice único do PostgreSQL e retornam 409. Nomes podem se repetir.
- Nome é aparado e normalizado em Unicode NFC. Busca por nome ignora maiúsculas/minúsculas, mas não remove acentos. Busca por telefone aceita fragmentos numéricos e formatação comum.
- Endereço pertence a um cliente e exige rua/avenida (120), número textual (20, aceita S/N), bairro (80), cidade (80) e UF brasileira. Complemento (120), CEP e referência (250) são opcionais. CEP aceita oito dígitos com ou sem hífen e persiste sem hífen; não há consulta externa de CEP nem definição de área/taxa de entrega.
- Clientes e endereços são inativados, sem exclusão física. Inativar cliente não muda o status dos endereços. A equipe pode manter endereços de clientes inativos; a elegibilidade para um pedido será definida no módulo de pedidos.
- IDs e datas de criação são preservados nas edições. Datas UTC têm precisão de milissegundos. Repetir a mesma mudança de status não altera `UpdatedAt`. A última gravação válida prevalece; não há ETag neste incremento.
- Quantidade de compras, histórico e último pedido serão derivados quando existir o módulo de pedidos. Um futuro pedido manterá uma cópia do endereço usado.

## Autorização e persistência

Todos os endpoints exigem JWT e `customers.manage`, atribuída a `Administrator` e `Attendant`. `Kitchen` e `Dispatch` não recebem essa permissão. O frontend usa a mesma permissão para menu e guard; a API aplica a autorização independentemente da interface. Faça novo login após atualizar a API para receber a permissão na sessão do navegador.

As gravações abrem transação e revalidam o funcionário, SecurityStamp e perfil sob bloqueio compartilhado do usuário até o commit. Alterações de um cliente e de seus endereços são serializadas pelo bloqueio da linha do cliente; endereço é localizado sempre pelo par cliente/ID. Não é possível transferir um endereço alterando o corpo da requisição ou usar seu ID na rota de outro cliente.

`CustomerService` concentra esse incremento e utiliza diretamente o EF Core. O helper de catálogo permanece exclusivo do administrador; não foi ampliado para permitir atendentes no catálogo. Logs de negócio registram IDs e ação, sem nome, telefone ou conteúdo do endereço. Dados sensíveis do EF permanecem desabilitados. Respostas do controller não devem ser armazenadas em cache.

## API

| Método | Rota | Resultado |
| --- | --- | --- |
| GET | /api/customers | Página de clientes |
| POST | /api/customers | 201 e Location do cliente |
| GET / PUT | /api/customers/{id} | Consultar / editar cliente |
| PUT | /api/customers/{id}/status | Ativar ou inativar |
| GET | /api/customers/{customerId}/addresses | Página de endereços daquele cliente |
| POST | /api/customers/{customerId}/addresses | 201 e Location do endereço |
| GET / PUT | /api/customers/{customerId}/addresses/{addressId} | Consultar / editar endereço vinculado |
| PUT | /api/customers/{customerId}/addresses/{addressId}/status | Ativar ou inativar endereço vinculado |

Listagens aceitam `page` (1–1000000, padrão 1), `pageSize` (1–100, padrão 20), `isActive` opcional. Clientes também aceitam `search` de até 120 caracteres. Retorno: `{ items, page, pageSize, totalCount }`. Clientes ordenam por nome normalizado/ID; endereços por criação/ID. Cliente inexistente retorna 404 ao listar endereços, não uma página vazia.

Exemplo de cadastro (dados fictícios):

```json
{ "name": "Cliente exemplo", "phone": "(31) 99999-1234" }
```

Exemplo de endereço:

```json
{
  "street": "Rua de exemplo",
  "number": "S/N",
  "neighborhood": "Centro",
  "city": "Belo Horizonte",
  "state": "MG",
  "complement": null,
  "postalCode": "30110-000",
  "reference": null,
  "isActive": true
}
```

Status exige `{ "isActive": false }` ou `true`. Campos inválidos retornam 400 em ProblemDetails; códigos de domínio: `DuplicateCustomerPhone` (409), `CustomerNotFound`/`AddressNotFound` (404), `InvalidCustomerPhone` (400), `InvalidSession` (401) e `PermissionDenied` (403). Uma gravação rejeitada preserva o registro anterior e os campos preenchidos na tela.

## Banco e execução

Migration `20260930205926_AddCustomersAndAddresses`: cria somente `Customers`, `Addresses`, índices, constraints de telefone/UF/CEP e FK restrita de endereço para cliente. Não insere cadastros nem altera dados comerciais existentes. Reverter a migration remove essas duas tabelas e seus dados; não fazer rollback em banco com cadastros sem plano de recuperação.

Em uma instalação de desenvolvimento configurada, confira se a conexão aponta para `made_in_minas`, faça backup e aplique a migration revisada explicitamente. A API não aplica migrations ao iniciar. Na raiz:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet tool restore
dotnet ef database update --project backend/MadeInMinas.Api
dotnet run --project backend/MadeInMinas.Api --launch-profile http
```

Ao recompilar a mesma pasta de saída, pare antes a API. O frontend continua em `http://localhost:8101` com `npm.cmd start` na pasta `frontend/made-in-minas`. Este incremento não atualiza nem para o serviço PostgreSQL compartilhado com `parsmartmanager`.

## Testes e aceite

```powershell
# Raiz: regressão HTTP em PostgreSQL isolado
powershell -NoProfile -File scripts/Test-Authentication.ps1
# Ou somente o novo módulo
powershell -NoProfile -File scripts/Test-Authentication.ps1 -Filter FullyQualifiedName~CustomerTests

cd frontend/made-in-minas
npm.cmd run format:check
npm.cmd run lint
npm.cmd run build
npm.cmd run test:e2e
```

Os testes de navegador usam API simulada e cobrem desktop/celular; os testes HTTP validam persistência, concorrência e permissões em PostgreSQL isolado. Resultados efetivamente executados são registrados em [validation.md](validation.md).

Aceite manual antes do próximo incremento:

1. Entre novamente como administrador ou atendente e abra **Clientes**.
2. Cadastre nome e telefone, reabra e edite. Busque por nome e telefone formatado.
3. Tente repetir o telefone e confira a mensagem, inclusive com o cliente original inativo.
4. Abra **Endereços**, cadastre, edite, reabra e confira CEP, UF, número e campos opcionais.
5. Inative e reative cliente/endereço; confira que os cadastros são preservados.
6. Confira que cozinha/expedição não acessam essas telas.

## Arquivos

Criados: `Models/Customer.cs`, `Models/Address.cs`, respectivas configurações EF, migration e Designer, `DTOs/Customers/CustomerContracts.cs`, `Validation/BrazilianPhone.cs`, `Services/CustomerService.cs`, `CustomerException.cs`, `Infrastructure/CustomerExceptionHandler.cs`, `Controllers/CustomersController.cs`, `CustomerTests.cs`, serviço HTTP `customer-api.service.ts`, páginas em `features/customers`, `e2e/customers.spec.ts` e este guia.

Integrados: `AppDbContext` e snapshot, `Program.cs`, `AccessPolicies`, teste de permissões da autenticação, sessão/guard/rotas/menu e mensagens de erro do frontend. README, modelo do banco, regras, decisões, API, arquitetura, roadmap e validação acompanham o incremento. Nenhuma dependência foi adicionada.
