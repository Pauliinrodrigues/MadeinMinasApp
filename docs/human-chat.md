# Fase 8E — atendimento humano no chat próprio

Continuidade autorizada em 05/10/2026. O acompanhamento (8D) foi preservado no commit `687e10c`; a implementação 8E foi registrada em `720f586`, na branch `feat/human-chat`, e integrada na master pelo [PR #17](https://github.com/Pauliinrodrigues/MadeinMinasApp/pull/17). O responsável autorizou expressamente a integração após os antecessores usando os testes locais aprovados, com checks remotos pendentes/cancelados durante o incidente do GitHub Actions. Isso não comprova execução manual integral do roteiro nem aprovação remota dos checks pendentes. Registro da publicação em [validation.md](validation.md).

## Escopo

O cliente pode acessar **Falar com atendente** pelo cardápio, comprovante ou acompanhamento. Em `/pedido/atendimento`, informa nome e primeira mensagem. A solicitação entra na fila **Aguardando atendimento**. Um administrador ou atendente assume a conversa, responde e encerra pelo menu **Atendimentos**, em `/equipe/atendimentos`.

O acesso antes e depois do pedido foi adotado como escopo inicial; a preferência foi solicitada ao responsável, sem resposta durante a implementação. Não vincular automaticamente a conversa a cliente/pedido pelo nome, telefone ou navegador. O atendimento pode orientar o uso das telas existentes, mas mensagens **não criam, confirmam, cancelam ou alteram pedidos, estoque ou pagamentos**.

Este incremento entrega a solicitação e o atendimento humano. Não há IA, bot simulado, anexos, notificações externas, indicador de leitura/digitação, previsão de resposta ou promessa de atendente disponível. IA e coordenação entre bot e humano pertencem à Fase 9. A integração visual mais ampla entre conversa e cardápio e a entrega pública continuam em incrementos próprios.

## Fluxo e regras

- `Waiting`: solicitação na fila; visitante pode complementar a mensagem.
- `InService`: conversa assumida por um funcionário. Só o responsável pode responder/encerrar. Outro atendente recebe conflito; um administrador pode assumir uma conversa de outra pessoa, com registro no histórico. Para encerrar uma solicitação abandonada/expirada, primeiro assumir.
- `Closed`: sem novas mensagens; histórico continua legível até o vencimento do acesso público. Encerramento exige confirmação na interface. Não existe reabertura; o visitante pode iniciar outro atendimento, perdendo o acesso anterior da aba.

Cada mensagem ou ação incrementa `Version` e gera uma sequência na conversa. Ações de assumir/encerrar geram mensagens `System`, com autor e nome do funcionário armazenados para auditoria. API pública e transcript da interface omitem identificadores/nome interno do autor: apresentam Cliente, Atendente ou Atendimento. O texto escrito pelo funcionário é público para o visitante da conversa; não incluir informações internas nele.

Nome do visitante é autodeclarado. A equipe deve verificar a identidade antes de compartilhar dados pessoais de pedidos/clientes; o chat não autentica telefone nem dá acesso a histórico de outras compras.

Solicitação inicial é idempotente por `requestId`, com hash de nome/texto normalizados e advisory lock. Mensagens são idempotentes por conversa + `requestId`; reutilizar a tentativa com outro texto/autor/tipo resulta em conflito. Repetir uma mensagem já aceita pode recuperar o recibo mesmo após encerramento. Nova mensagem em conversa encerrada, expirada, sem responsável correto ou no limite é rejeitada antes de gravar.

Escritas usam transação e `FOR UPDATE` na conversa. Ações exigem `expectedVersion`; a segunda disputa por uma mesma versão falha. Funcionário é revalidado com conta ativa, security stamp e perfil atual sob bloqueio de leitura, antes da conversa. Repetição imediata da mesma ação pelo mesmo funcionário é reconhecida pelo último evento. Histórico não pode ser editado/excluído pelas APIs.

Limites: nome até 120 caracteres, mensagem até 2.000, corpo até 16.384 bytes. Ao alcançar versão 500, não aceitar novas mensagens de texto; ações da equipe continuam permitidas para encerrar o atendimento. Transcript usa cursor `after` e até 50 mensagens por resposta, com `hasMore`. Fila tem 20 conversas por página: aguardando pela criação mais antiga; demais status pela atualização mais recente.

## Acesso público e recuperação

Credencial exclusiva de conversa, protegida por Data Protection com finalidade `MadeInMinas.HumanChat.v1`, distinta da finalidade do acompanhamento de pedidos. Permite ler/escrever **somente naquela conversa**, enquanto aberta e dentro de sete dias desde a criação. Repetição da solicitação emite credencial equivalente sem renovar o prazo; após o vencimento retorna `access: null`. Token de pedido, UUID, nome ou parâmetro na URL não dão acesso ao chat.

O frontend envia a credencial somente no cabeçalho `X-Chat-Access`; omite JWT da equipe nesses endpoints. Não coloca o segredo na URL nem procura conversa por telefone. Credencial ausente, inválida, adulterada, expirada ou de conversa inexistente retorna o mesmo 404. Falha pública não encerra a sessão administrativa. A preservação das chaves no servidor segue [as condições de Data Protection já documentadas na 8D](public-order-tracking.md#banco-chaves-e-execução).

Antes de enviar, o visitante guarda a tentativa em `sessionStorage`; depois de abrir a conversa, conserva credencial e somente eventual mensagem pendente. A primeira solicitação é substituída pelo acesso após sucesso. **Conferir solicitação/mensagem** recupera a mesma tentativa, inclusive após recarregar. Não reenviar como uma nova mensagem enquanto o resultado estiver incerto. Falha ao guardar impede iniciar envio. Fechar a aba pode perder o acesso. O chat tem armazenamento próprio e não limpa carrinho/comprovante ao navegar ou iniciar outra compra.

Na equipe, a mensagem pendente também é guardada na sessão da aba, com chave por funcionário/conversa. Novo login com o mesmo funcionário permite conferir o envio. Não persiste JWT nem todo o histórico no navegador. Recuperação corrompida exige conferir o histórico e marcar a confirmação antes de limpar. Sair da conta não apaga automaticamente essa tentativa incerta; o servidor sempre exige novo login/permissão para recuperá-la.

Conversa consulta novas mensagens a cada cinco segundos enquanto visível; páginas adicionais são carregadas até alcançar o final. Não sobrepõe leituras. Ocultar cancela a leitura pendente; sair destrói requisições/temporizadores. Encerramento com histórico completo para as consultas automáticas. Fila consulta a cada dez segundos quando visível. Falha de leitura conserva histórico com aviso e bloqueia novo envio/ações até atualizar. No público, falhas aguardam pelo menos 15 segundos; 429, 60 segundos. HTTP tem timeout de 15 segundos. Textos usam interpolação, sem HTML executável.

## API

Todos os endpoints usam `no-store`. Nas rotas administrativas, `chat.manage` permite somente Administrator e Attendant. Kitchen/Dispatch não acessam fila, mensagens ou ações.

| Método e rota | Entrada | Resultado |
| --- | --- | --- |
| POST `/api/public-chat` | `{ requestId, name, text }` | 201 criação / 200 repetição; `{ access: { token, expiresAt } ou null, createdAt }` |
| GET `/api/public-chat?after=0` | Cabeçalho X-Chat-Access; cursor não negativo | `{ status, version, createdAt, updatedAt, messages, hasMore }` |
| POST `/api/public-chat/messages` | X-Chat-Access; `{ requestId, text }` | 201 / 200; mensagem aceita |
| GET `/api/chat?status=Waiting&page=1` | JWT + chat.manage | `{ items, page, pageSize, totalCount }` |
| GET `/api/chat/{id}?after=0` | JWT + chat.manage | `{ conversation, transcript }` |
| POST `/api/chat/{id}/messages` | JWT; `{ requestId, text }` | 201 / 200; mensagem aceita |
| PUT `/api/chat/{id}/claim` | JWT; `{ expectedVersion }` | Resumo atualizado |
| PUT `/api/chat/{id}/close` | JWT; `{ expectedVersion }` | Resumo atualizado |

Mensagem pública/transcript: `{ sequence, kind, text, createdAt }`. Resumo administrativo: `{ id, visitorName, status, assignedToId, assignedToName, version, createdAt, updatedAt, expiresAt }`.

Campos desconhecidos, entradas vazias/fora dos limites e UUID vazio: 400. Sem sessão administrativa: 401; perfil sem permissão: 403. `ChatUnavailable`: 404 genérico. `ChatRequestConflict`, `ChatVersionConflict`, `ChatAssignmentRequired`, `ChatClosed`, `ChatExpired`, `ChatLimitReached`: 409. Sem detalhes internos de exceções nas respostas.

Limites públicos independentes por endereço remoto: 5 solicitações iniciais, 20 envios de mensagem e 120 consultas por minuto; exceder retorna 429. As tentativas de recuperação também contam. Não prometem impedir todo abuso distribuído; mecanismos adicionais dependem da exposição/volume real. Na publicação, configurar proxy confiável/CORS/HTTPS e evitar captura de mensagens/credenciais em logs.

## Banco e execução

Migration `20261005183152_AddHumanChat`, posterior a AddPublicOrders:

- `ChatConversations`: identificador, tentativa/hash, nome informado, status, responsável com nome copiado, versão, criação/atualização/encerramento.
- `ChatMessages`: conversa, sequência, tentativa opcional, tipo, texto, autor/nome opcionais e instante. Índices únicos por conversa/sequência e conversa/tentativa. FK para conversa e, nas mensagens da equipe/sistema, User.

Constraints limitam estados/autoria e sequência positiva; exclusão de funcionário referenciado é restrita. Não há FK ou alteração em Orders, Customers, Payments, Ingredients ou StockMovements. Down recusa reversão quando existem conversas, evitando apagar o histórico por engano. Não existe política automática de expurgo neste incremento; definir retenção antes da hospedagem operacional.

Após revisar o script e confirmar conexão exclusivamente ao banco Made in Minas, com backup:

```powershell
dotnet ef migrations script 20261005145517_AddPublicOrders 20261005183152_AddHumanChat --project backend/MadeInMinas.Api
dotnet ef database update 20261005183152_AddHumanChat --project backend/MadeInMinas.Api
dotnet run --project backend/MadeInMinas.Api --launch-profile http
```

Frontend: `npm.cmd start` em `frontend/made-in-minas`. Fazer novo login na equipe para carregar a permissão `chat.manage`. Dados e segredos locais não são transferidos pelo Git.

## Testar e validar

1. Em ambiente de teste, abrir `/pedido`, clicar Falar com atendente, informar nome/mensagem e conferir a fila aguardando.
2. Em outra aba, entrar como atendente/administrador, abrir Atendimentos, assumir e responder. A resposta deve aparecer no cliente.
3. Conferir bloqueio de resposta por outro atendente e possibilidade de tomada pelo administrador. Encerrar exige confirmar; cliente vê encerramento e não envia novas mensagens.
4. Nos testes automatizados, interromper respostas de solicitação/envio e recarregar: conferência deve manter tentativa e não duplicar. Texto com HTML aparece literalmente. Falha na leitura sinaliza histórico desatualizado.
5. Cozinha/expedição não veem Atendimentos e não acessam pela URL. Verificar desktop/celular, acesso expirado, paginação e pausa em aba oculta.

```powershell
# Raiz, PostgreSQL temporário isolado
powershell -NoProfile -File scripts/Test-Authentication.ps1

# frontend/made-in-minas
npm.cmd run format:check
npm.cmd run lint
npm.cmd run build
npm.cmd run test:e2e -- human-chat.spec.ts public-order-tracking.spec.ts public-checkout.spec.ts menu.spec.ts staff.spec.ts
```

Resultados executados, backup e conferência local em [validation.md](validation.md). Testes não devem enviar mensagens fictícias à operação real.

## Arquivos

- Backend novos: `Models/ChatConversation.cs`, `Models/ChatMessage.cs`, respectivas configurações EF, `DTOs/Chat/ChatContracts.cs`, `Security/PublicChatAccess.cs`, `Services/HumanChatService.cs`, `Services/ChatException.cs`, `Infrastructure/ChatExceptionHandler.cs`, `Controllers/ChatController.cs`, `Controllers/PublicChatController.cs`, migration AddHumanChat e designer.
- Backend alterados: `Data/AppDbContext.cs`, snapshot EF, `Program.cs`, `Security/AccessPolicies.cs`.
- Testes backend: novos `HumanChatTests.cs` e `PublicChatAccessTests.cs`; permissões esperadas atualizadas em `AuthenticationTests.cs`.
- Frontend novos: `core/services/chat-api.service.ts`, `public-chat-state.service.ts`, `features/chat` (página pública, fila, conversa da equipe e componente de mensagens), `e2e/human-chat.spec.ts`.
- Frontend alterados: rotas, sessão/guards/interceptor, mensagens de erro, menu da equipe e links no cardápio/comprovante/acompanhamento.
- Documentação: README, roteiro, arquitetura, API, banco, regras, decisões, validação, continuidade do acompanhamento e este documento.
