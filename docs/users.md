# Gestão de funcionários — Fase 2B

API de gestão de contas internas, integrada ao JWT e às políticas da Fase 2A. As tabelas Users e Roles existentes foram suficientes; nenhuma migration nova é necessária.

## Endpoints

Todas as rotas /api/users exigem a política users.manage, exclusiva do administrador.

| Método | Rota | Comportamento |
| --- | --- | --- |
| GET | /api/users | Lista paginada de funcionários |
| GET | /api/users/{id} | Dados de um funcionário |
| POST | /api/users | Cria funcionário; retorna 201 e Location |
| PUT | /api/users/{id} | Atualiza nome, login, perfil e status |
| PUT | /api/users/{id}/status | Ativa ou inativa |
| PUT | /api/users/{id}/password | Administrador redefine a senha de outro funcionário |
| PUT | /api/auth/password | Funcionário altera a própria senha, informando a atual |

Não existe exclusão física. A inativação preserva referências para o histórico operacional futuro.

## Contratos

POST /api/users:

```json
{
  "name": "Funcionário",
  "username": "atendente.exemplo",
  "password": "<senha escolhida localmente>",
  "roleId": 2,
  "isActive": true
}
```

Nome: obrigatório, até 120 caracteres; espaços externos são removidos. Login: 3 a 64 letras ASCII, números, ponto, hífen ou sublinhado. Senha: 15 a 128 caracteres, não composta somente por espaços. RoleId deve corresponder a um perfil existente.

O campo isActive é opcional na criação e seu padrão é true. Na edição e na mudança de status, é obrigatório: omiti-lo retorna 400, evitando inativação acidental.

PUT /api/users/{id} recebe name, username, roleId e isActive. Não recebe senha. O campo username é único sem distinguir maiúsculas/minúsculas, inclusive para contas inativas.

PUT /api/users/{id}/status recebe:

```json
{ "isActive": false }
```

PUT /api/users/{id}/password recebe newPassword. Não é permitido usar essa rota para redefinir a própria senha; nesse caso, usar PUT /api/auth/password com currentPassword e newPassword.

As respostas de usuário contêm apenas id, name, username, roleId, role, isActive e createdAt em UTC. Nunca incluem senha, hash, SecurityStamp ou contador de falhas. Respostas do controller de usuários não devem ser armazenadas em cache.

## Listagem

Parâmetros opcionais:

- page: de 1 a 1.000.000; padrão 1.
- pageSize: de 1 a 100; padrão 20.
- search: até 120 caracteres; busca em nome ou login sem distinguir maiúsculas.
- roleId: filtra o perfil.
- isActive: true ou false; ausência inclui ambos.

A resposta contém items, page, pageSize e totalCount. Ordenação por login normalizado e ID torna a paginação determinística.

Exemplo: GET /api/users?search=ana&roleId=2&isActive=true&page=1&pageSize=20.

## Regras de acesso e sessão

- Atendente, cozinha e expedição não acessam os endpoints de gestão.
- Não é possível inativar ou rebaixar o último administrador ativo.
- Administradores inativos não contam para essa proteção.
- Um administrador pode alterar seu próprio perfil/status se outro administrador ativo permanecer.
- Alterar login, perfil ou status revoga sessões. Reativar uma conta não restaura tokens antigos.
- Alterar somente o nome mantém a sessão; a consulta de perfil retorna o nome atualizado.
- Qualquer troca/reset de senha revoga todos os tokens anteriores e exige novo login.
- Reset administrativo limpa o bloqueio temporário da conta. Não ativa automaticamente uma conta inativa.
- Troca da própria senha exige senha atual e compartilha a contagem persistida de falhas do login. Cinco erros bloqueiam novas tentativas por 15 minutos.
- Login e troca da própria senha compartilham o limiter de dez tentativas por minuto por IP.

O cadastro/reset administrativo define uma senha normal neste incremento. Entrega dessa senha, recuperação externa e troca obrigatória no primeiro acesso ainda não foram implementadas.

## Concorrência e integridade

As gravações usam uma transação com o mesmo advisory lock do bootstrap. Isso serializa alterações do conjunto de administradores, impedindo que duas inativações simultâneas removam ambos.

O usuário que executa a alteração é lido novamente dentro da transação, com bloqueio de linha. Sua situação, perfil quando necessário e SecurityStamp são revalidados, evitando aceitar uma gravação autorizada antes da revogação da sessão. O funcionário alterado também é bloqueado durante a gravação.

O índice único de login continua sendo a garantia final de unicidade; conflitos retornam 409. As alterações usam o estado atual do banco. Não há versão de edição otimista/ETag neste incremento; duas edições válidas do mesmo cadastro são aplicadas na ordem em que obtêm o lock.

Logs registram IDs do autor e do funcionário nas operações concluídas, sem senha/hash/token. São logs operacionais; não substituem uma futura trilha de auditoria persistente.

Referências: [locks do PostgreSQL 17](https://www.postgresql.org/docs/17/explicit-locking.html) e [transações do EF Core](https://learn.microsoft.com/en-us/ef/core/saving/transactions).

## Erros

Respostas ProblemDetails incluem code nas falhas de negócio.

| HTTP | Code | Situação |
| --- | --- | --- |
| 400 | InvalidRole | Perfil inexistente |
| 400 | InvalidPassword | Senha atual incorreta ou conta temporariamente bloqueada |
| 400 | UseOwnPasswordEndpoint | Tentativa de resetar a própria senha pela rota administrativa |
| 401 | InvalidSession | Sessão revogada durante a operação |
| 403 | PermissionDenied | Permissão administrativa removida durante a operação |
| 404 | UserNotFound | Funcionário inexistente |
| 409 | DuplicateUsername | Login em uso |
| 409 | LastAdministrator | Operação deixaria o sistema sem administrador ativo |

Erros de DTO usam ValidationProblemDetails. Rejeições anteriores à ação por autenticação/autorização podem retornar 401/403 sem esses códigos específicos.

## Testar

```powershell
powershell -NoProfile -File scripts/Test-Authentication.ps1
```

Executa autenticação e gestão de funcionários em PostgreSQL temporário. O build de testes fica em .local/test-build para não disputar arquivos com a API em execução. Nenhuma conta real é criada, editada ou excluída pelos testes.

Somente a nova suíte:

```powershell
powershell -NoProfile -File scripts/Test-Authentication.ps1 -Filter FullyQualifiedName~UserManagementTests
```

Para uso manual, primeiro crie o administrador conforme [authentication.md](authentication.md#administrador-inicial). Acesse a [interface da equipe](staff-frontend.md) em http://localhost:8101/entrar. Para chamadas diretas à API, faça login e envie o JWT como Authorization: Bearer <token>.
