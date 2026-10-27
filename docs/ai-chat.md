# Agente de IA no chat — protótipo incremental

Implementação de 10/10/2026. O chat público e o atendimento humano existentes foram ampliados, preservando histórico, credenciais, permissões, envio idempotente e atualização por polling. A integração permanece **desativada por padrão**. Não houve chamada paga para validar um modelo real, publicação em produção ou ativação automática de uma chave.

## Arquitetura encontrada e reaproveitada

O repositório utilizado é `C:\Users\pauli\IdeaProjects\MadeinMinasApp`, com a solução `MadeinMinasApp.sln`, projetos em `backend/`, frontend em `frontend/made-in-minas/`, scripts, `database/` e documentação em `docs/`. O caminho e o nome da solução diferem das referências iniciais do pedido.

O frontend utiliza Ionic 9, Angular 22 e TypeScript 6. A API utiliza ASP.NET Core/.NET 10, Entity Framework Core 10 e Npgsql/PostgreSQL. A implementação usa os mesmos projetos e não acrescenta um serviço separado de IA, SDK obrigatório, SignalR ou banco vetorial.

O chat já contava com `HumanChatService`, `ChatConversation`, `ChatMessage`, controllers público/equipe, credencial própria emitida por Data Protection e páginas públicas/administrativas. A credencial pública permite acessar somente a conversa correspondente durante sete dias desde a criação. Nome e telefone informados pelo visitante não constituem identidade autenticada.

Os serviços reaproveitados são:

| Serviço existente            | Uso no agente                                                                                |
| ---------------------------- | -------------------------------------------------------------------------------------------- |
| `PublicMenuService`          | Categorias e produtos comerciais ativos, descrição, preço, imagem e disponibilidade pública. |
| `PublicCartService`          | Quantidades, disponibilidade para produção e valores dos itens calculados no servidor.       |
| `PublicCheckoutService`      | Revisão, frete, resumo, revalidação e criação transacional/idempotente do pedido.            |
| `DeliverySettingsService`    | Regiões atendidas, taxas e endereço de retirada cadastrados.                                 |
| `StoreAttendanceService`     | Horários, pausa, recebimento de pedidos, formas de pagamento e mensagem de prazo.            |
| `PublicOrderTrackingService` | Consulta do pedido vinculado à própria conversa, usando o comprovante emitido pelo backend.  |

O fluxo é cliente → chat existente → API .NET → `IAiService`/`IAiProvider` → ferramentas permitidas → serviços de domínio → PostgreSQL. O provedor não recebe acesso ao banco, SQL, credenciais administrativas ou métodos internos arbitrários.

## Interpretação e respostas ao cliente

O modelo interpreta linguagem natural e escolhe ferramentas com argumentos estruturados. Os dados retornados vêm do backend. **O texto factual exibido ao cliente é montado pelo servidor**, com mensagens autorizadas para cardápio, valores, resumo, campos ausentes, erros e encaminhamento. Texto livre produzido pelo modelo não é publicado como resposta do atendimento.

Essa escolha limita a liberdade de redação do protótipo e protege preços, informações comerciais e afirmações de sucesso. Os nomes dos produtos e a descrição comercial vêm do cadastro. A ficha técnica interna não é fornecida ao modelo ou ao cliente para responder sobre ingredientes. Dúvidas não cobertas pelo cadastro devem ser encaminhadas à equipe.

Há duas abstrações: `IAiService.ProcessMessageAsync` controla o turno, ferramentas e limites; `IAiProvider.RespondAsync` adapta o provedor. `AiProviderRegistration` seleciona somente o adaptador configurado: `GeminiAiProvider` usa a API REST `generateContent` do Google; `OpenAiProvider` conserva a Responses API da OpenAI. A escolha do Gemini para o piloto não altera chat, ferramentas, persistência ou checkout. Ollama ainda não possui adaptador.

O Gemini usa `functionDeclarations` com `parametersJsonSchema`, `functionCallingConfig.mode="ANY"` e respostas de ferramentas em `functionResponse`, conforme a [referência oficial de generateContent](https://ai.google.dev/api/generate-content). A chave segue no header `x-goog-api-key`, nunca na URL. O adaptador preserva os conteúdos originais de retorno, incluindo `thoughtSignature`, nas rodadas do mesmo turno; esses itens não são persistidos no histórico comercial. IDs do provedor, quando presentes, acompanham a resposta; se ausentes, a aplicação gera apenas seu identificador interno. Chamadas paralelas são rejeitadas antes de executar ferramentas, e as instruções exigem uma ferramenta por rodada. Respostas incompletas, bloqueadas ou sem ferramenta válida não são publicadas como sucesso.

Na OpenAI, o uso de chamadas de função segue a [documentação oficial de function calling](https://developers.openai.com/api/docs/guides/function-calling). A requisição usa schemas estritos, ferramentas sequenciais, `parallel_tool_calls=false`, `tool_choice="required"` e `store=false`. Em ambos, o histórico necessário é enviado pela aplicação; não se depende de uma conversa remota para manter o estado comercial.

Para modelos com raciocínio, o adapter solicita `reasoning.encrypted_content` e devolve os itens opacos nas rodadas de ferramentas do mesmo turno, conforme o [exemplo oficial de contexto com raciocínio](https://developers.openai.com/cookbook/examples/responses_api/reasoning_items). Esses itens não são publicados ao cliente nem persistidos no histórico comercial.

## Ferramentas disponíveis

| Ferramenta                                        | Comportamento                                                                                                      |
| ------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------ |
| `consultar_cardapio`                              | Busca/paginação do cardápio público.                                                                               |
| `consultar_produto` / `consultar_disponibilidade` | Consulta comercial de um ID real, sem dados internos de estoque.                                                   |
| `consultar_promocoes`                             | Informa que não existe cadastro estruturado de promoções disponível. Não inventa uma promoção.                     |
| `consultar_taxa_entrega`                          | Regiões, taxas e retirada do sistema.                                                                              |
| `consultar_atendimento`                           | Horários, pagamentos, pausa e mensagem comercial de prazo.                                                         |
| `adicionar_item_pedido`                           | Seleciona um produto real com sua quantidade **total desejada**. Repetir o mesmo alvo não incrementa a quantidade. |
| `alterar_quantidade`                              | Altera somente um produto já selecionado; mantém observações quando não forem substituídas.                        |
| `remover_item_pedido`                             | Remove o produto da montagem. Não cancela um pedido recebido.                                                      |
| `definir_dados_pedido`                            | Guarda nome, telefone, modalidade, endereço e observações informados pelo cliente.                                 |
| `calcular_pedido`                                 | Revisa com os serviços do backend e persiste o resumo atual. Não cria pedido.                                      |
| `consultar_status_pedido`                         | Consulta apenas o pedido criado por esta conversa. Não aceita identificação de outro pedido.                       |
| `solicitar_atendimento_humano`                    | Encerra o modo automático e encaminha à fila humana.                                                               |
| `responder_cliente`                               | Seleciona resultado ou pergunta autorizada pelo servidor.                                                          |

Não existe ferramenta `criar_pedido` acessível ao modelo. Também não existem ferramentas para cobrar pagamentos, aplicar descontos, cancelar pedidos, aceitar comandas ou executar SQL.

Itens respeitam os limites existentes: 50 produtos/linhas no contrato público, até 99 unidades por produto, 250 caracteres nas observações de um item e 500 nas observações gerais. A montagem do chat mantém uma linha por produto. Quantidade zero exige remoção; valores de preço ou desconto não são argumentos aceitos. A aplicação valida entradas e objetos aninhados também quando a chamada é interna, sem depender exclusivamente da validação MVC.

## Montagem, revisão e confirmação

`ChatOrderDraft` é uma extensão de uma conversa existente, com chave `ConversationId`. Guarda itens, contato autodeclarado, modalidade, endereço, cotação, revisão, versão, tentativa de criação, vínculo e comprovante. O rascunho é criado somente ao montar o pedido, sem duplicar tabelas de produtos, clientes ou pedidos.

O nome inicial vem do visitante; telefone e modalidade precisam ser informados. O sistema não presume retirada e não consulta cadastro anterior para preencher endereço ou dados privados. Entrega exige região atendida e os campos previstos pelo checkout. Quando a região atende todos os bairros, o bairro real informado pelo cliente é obrigatório.

Uma alteração de itens/dados invalida a revisão. Após uma ferramenta alterar o rascunho, `AiChatTools` prepara novamente o resumo se houver itens e todos os dados obrigatórios, reutilizando `ChatOrderService.ReviewForToolAsync`, as regras de checkout e a validação da lease. A revisão deixa de depender de o modelo lembrar de chamar outra ferramenta; isso não cria o pedido. Loja pausada/fechada ou outra regra rejeitada preserva a montagem sem revisão válida. Uma nova mensagem do visitante também invalida a revisão ainda não enviada, para impedir confirmação de um resumo anterior enquanto a IA interpreta uma mudança. Revisão ou confirmação pública aguardam a conclusão do turno automático corrente. O frontend bloqueia essas ações durante processamento, envio de mensagem ou recuperação pendente.

O cliente confere produtos, quantidades, observações, contato, entrega/retirada, frete e total no resumo estruturado e usa **Confirmar pedido**. Um texto como “sim” não cria um pedido. A confirmação exige a versão atual e o token da revisão persistida; o checkout recalcula os dados novamente antes de gravar.

A criação participa da transação da conversa: bloqueia a conversa, usa uma chave de criação gerada pelo servidor e reaproveita os locks e a idempotência do checkout. Pedido, cliente eventualmente novo, itens, histórico e vínculo com a conversa são gravados atomicamente. Uma repetição idêntica da confirmação recupera o comprovante, inclusive depois de pausa ou encerramento posterior. Conteúdo diferente com a mesma tentativa é rejeitado.

O navegador guarda a confirmação pendente em `sessionStorage` da aba antes de enviar. Falha de rede, timeout ou resposta perdida preserva a mesma tentativa para **Conferir envio**; o polling também pode recuperar o comprovante. Uma rejeição definitiva libera revisão; falhas incertas não iniciam automaticamente outro pedido.

O pedido nasce `New`, origem `DirectLink`, estoque `Pending`. A equipe continua aceitando o preparo pelo fluxo existente, com validação/baixa de estoque e impressão nos estados já definidos. Confirmar pelo chat não aceita a comanda em nome do comerciante. Cada conversa vincula um pedido; alterações depois do recebimento exigem atendimento humano. Para outra compra, o cliente pode usar o cardápio/carrinho existente ou uma nova conversa.

Combos, adicionais com preço, substituições estruturadas, descontos, promoções e pagamentos online continuam fora deste incremento. Observações livres não alteram composição ou valor. As opções de pagamento exibidas vêm da configuração da loja; o agente não registra cobrança ou recebimento real.

## Atendimento humano e concorrência

`AutomationMode` é persistido no backend como `Manual` ou `Automatic`, independentemente dos estados existentes `Waiting`, `InService` e `Closed`. Conversas anteriores permanecem manuais. Novas conversas usam modo automático somente quando a integração está configurada e habilitada.

O cliente pode solicitar uma pessoa; a equipe pode assumir a conversa com `chat.manage`, responder, encerrar ou retomar o modo automático quando permitido e configurado. Assumir a conversa interrompe a IA. Falha, indisponibilidade ou ausência da configuração necessária preservam as mensagens e encaminham ao atendimento manual. Excesso de contexto encerra o turno; se já houver uma resposta segura, ela pode ser apresentada sem transferência, caso contrário o atendimento passa ao modo manual.

`AiChatWorker` procura trabalho pendente a cada dois segundos. `AiChatCoordinator` reserva o turno com lock e lease no PostgreSQL, sem manter uma transação aberta durante a chamada externa. Cada ferramenta de alteração bloqueia e revalida a conversa, lease, expiração, modo e última mensagem do visitante. Nova mensagem, transferência, aceite humano e encerramento revogam a lease. Uma resposta atrasada é descartada se o turno tiver sido substituído.

O frontend mantém o polling existente; não foi necessário SignalR. Mensagens identificam cliente, assistente virtual, atendente e eventos. A configuração global `Ai:Enabled=false` desativa novos turnos; mensagens e atendimento manual continuam disponíveis.

Desde 11/10/2026, a conversa da equipe também mostra **Pedido em montagem**, itens, contato, endereço, valores e campos faltantes. A montagem ainda não entra na fila de Pedidos. Após o cliente usar **Confirmar pedido**, a equipe vê **Pedido #… recebido**, o status atual e **Abrir pedido #…**, quando possui `orders.manage`. O pedido nasce `New`, aguardando confirmação da equipe; confirmar preparo continua sendo uma ação na tela de Pedidos. O resumo não cobra, aceita ou altera pedidos.

Quando a confirmação pública fica sem resposta, a tela informa que o envio ainda não foi confirmado e conserva **Conferir envio** com a mesma tentativa. Nesse estado, não afirma que o pedido deixou de ser enviado. Recebido/enviado descreve o registro do pedido, sem indicar pagamento.

## Contratos HTTP

As rotas existentes de conversas/mensagens permanecem disponíveis. `GET /api/public-chat` inclui `automationMode`, `automationState`, `automationAvailable` e `order`. `order` é `null` antes de existir montagem. O contrato de estado é:

```text
draftVersion
items: [{ productId, name, quantity, unitPrice, lineTotal, notes }]
name, phone, fulfillment, address, notes
quote: { items, notes, subtotal, calculatedAt } | null
review: { name, phone, fulfillment, items, notes, subtotal,
          deliveryFee, total, reviewToken, calculatedAt,
          address, deliveryAreaId } | null
receipt: { number, fulfillment, total, createdAt, tracking } | null
missingFields: ["items" | "name" | "phone" | "fulfillment" | "address"]
needsReview
```

`fulfillment` é `null`, `Pickup` ou `Delivery`. `address` contém `areaId`, `street`, `number`, `neighborhood`, `complement`, `postalCode` e `reference`, conforme o contrato de entrega pública. `receipt.tracking` contém a credencial e expiração do próprio pedido, destinadas ao navegador, sem envio dessa credencial ao modelo.

`GET /api/chat/{id}`, protegido por `chat.manage`, acrescenta `linkedOrder: { id, number, status, fulfillment, subtotal, deliveryFee, total } | null` à resposta administrativa, ao lado de `conversation` e `transcript`. O vínculo vem de `ChatOrderDrafts.OrderId`, exige a mesma tentativa de checkout (`OrderRequestId`) e origem `DirectLink`. Número mencionado em mensagem, nome ou telefone não cria vínculo. A leitura usa a transação existente e consulta o status atual mesmo sem novas mensagens; não precisa assumir a conversa. O identificador interno não é acrescentado ao transcript público nem ao contexto do modelo. Abrir o detalhe continua exigindo `orders.manage` no servidor.

| Rota                                  | Corpo                                              | Resposta                                                 |
| ------------------------------------- | -------------------------------------------------- | -------------------------------------------------------- |
| `POST /api/public-chat/order/review`  | `{ expectedDraftVersion }`                         | `ChatOrderStateResponse` com revisão atual.              |
| `POST /api/public-chat/order/confirm` | `{ requestId, expectedDraftVersion, reviewToken }` | `ChatOrderStateResponse` com comprovante.                |
| `PUT /api/public-chat/handoff`        | `{ expectedVersion }`                              | Histórico/estado atual da conversa.                      |
| `PUT /api/chat/{id}/automation`       | `{ expectedVersion }`                              | Resumo da conversa, com retomada autorizada pela equipe. |

Operações públicas usam um único header `X-Chat-Access`, no-store, corpo até 16.384 bytes e os limitadores de chat existentes: início 5/minuto/IP, escrita/revisão/confirmação 20/minuto/IP e leitura 120/minuto/IP. Limites HTTP são por processo. A reserva diária do provedor, descrita abaixo, é persistida e coordenada no PostgreSQL.

Credencial inválida/expirada retorna erro genérico `ChatUnavailable`/404. Conflitos incluem `ChatDraftChanged`, `ChatOrderReviewRequired`, `ChatOrderIncomplete`, `ChatAutomationPending`, `ChatRequestConflict` e os erros do checkout sobre preço, produto, região ou funcionamento. Campos desconhecidos e entradas inválidas são rejeitados. Não há busca pública de pedidos por telefone, número ou identificador interno.

## Configuração do provedor

`appsettings.json` seleciona `Ai:Provider="Gemini"`, conserva `Ai:Enabled=false` e modelo vazio; não contém chave real. Modelo e chave devem ser definidos no backend com os mecanismos já existentes. Os provedores aceitos são `Gemini` e `OpenAI`; a seleção não ativa a integração sem modelo/chave e habilitação explícita.

Para testar o Gemini, crie uma chave de um projeto próprio no [Google AI Studio](https://aistudio.google.com/apikey) e confira o nível **Free** e as cotas do projeto antes de ativar. O Gemini gratuito no aplicativo/site não fornece automaticamente credenciais para a API do projeto. A [documentação de modelos](https://ai.google.dev/gemini-api/docs/models/gemini-3.5-flash-lite) lista `gemini-3.5-flash-lite` com chamadas de função; a [tabela de preços](https://ai.google.dev/gemini-api/docs/pricing) lista uma camada gratuita para entrada/saída Standard. Esse modelo foi exercitado com a chave local no piloto isolado descrito ao final deste guia. Para outra conta, confirme a disponibilidade; se necessário, escolha outro modelo de texto com `generateContent` e chamadas de função. A documentação atual restringe modelos 2.5 a contas que já os utilizavam, portanto eles não são o exemplo para um projeto novo.

A camada gratuita possui limites e o Google informa uso de conteúdo para melhoria de produtos. **Use nomes, telefones, endereços e conversas fictícios no piloto gratuito**. Não há garantia de uso ilimitado nem de custo zero em um projeto com faturamento ativo. O código não ativa faturamento, não escolhe automaticamente um modelo pago e não faz fallback para outro provedor. Consulte as [cotas reais do projeto](https://ai.google.dev/gemini-api/docs/rate-limits). Um erro HTTP 429 encerra o turno e preserva a conversa para a equipe; não há retries automáticos contra a cota.

Preparação recomendada com User Secrets em desenvolvimento, a partir da raiz, no seu próprio terminal PowerShell:

```powershell
powershell -NoProfile -File scripts/Configure-Gemini.ps1
```

O script solicita a chave com entrada oculta, envia a configuração ao CLI por stdin e mantém `Ai:Enabled=false`. Preserva os demais segredos existentes e não faz chamadas ao Google nem altera o banco. Para outro modelo disponível na conta, use `-Model <ID_DO_MODELO>`. User Secrets ficam fora do repositório e são destinados ao desenvolvimento local; não são um cofre criptografado de produção. Se uma chave foi compartilhada em conversa ou arquivo, substitua-a no AI Studio e cadastre a nova localmente, conforme a [orientação do Google para chaves expostas](https://ai.google.dev/gemini-api/docs/api-key#security-and-secret-management).

Alternativa manual — **os valores são placeholders e estes comandos não foram executados para ativar a integração**. Digitar uma chave real como argumento pode deixá-la no histórico do terminal; prefira o script acima:

```powershell
dotnet user-secrets set "Ai:Provider" "Gemini" --project backend/MadeInMinas.Api
dotnet user-secrets set "Ai:Model" "gemini-3.5-flash-lite" --project backend/MadeInMinas.Api
dotnet user-secrets set "Ai:ApiKey" "<CHAVE_LOCAL>" --project backend/MadeInMinas.Api
dotnet user-secrets set "Ai:Enabled" "false" --project backend/MadeInMinas.Api
```

Alternativa para a sessão do processo:

```powershell
$env:Ai__Provider = 'Gemini'
$env:Ai__Model = 'gemini-3.5-flash-lite'
$env:Ai__ApiKey = '<CHAVE_LOCAL>'
$env:Ai__Enabled = 'false'
```

Depois da migration local conferida e da preparação do piloto, o responsável pode trocar `Ai:Enabled` para `true` e reiniciar a API. User Secrets são carregados no ambiente Development; variáveis devem estar disponíveis ao processo real da API. Chaves não pertencem ao frontend, arquivos versionados, mensagens, logs ou screenshots. Remover/alterar configurações locais não exige commit de segredos.

Esses exemplos mantêm a IA desligada durante a preparação. Substitua a chave somente no ambiente local, não envie seu valor pelo chat. Se o modelo atingir o limite de saída antes de terminar uma ferramenta, ajuste `Ai:MaxOutputTokens` dentro dos limites validados (até 4.000; por exemplo 2.048 para um piloto), considerando que tokens de raciocínio também contam. Essa alteração não substitui a conferência das cotas. Para voltar à OpenAI, configure juntos `Ai:Provider="OpenAI"`, modelo da conta compatível com Responses/function calling e a chave correspondente; não reaproveite a chave do outro fornecedor.

Os limites padrão lidos por `AiOptions` são:

| Configuração              | Padrão | Aplicação                                                                                            |
| ------------------------- | ------ | ---------------------------------------------------------------------------------------------------- |
| `MaxHistoryMessages`      | 20     | Últimas mensagens relevantes por turno.                                                              |
| `MaxHistoryCharacters`    | 16.000 | Histórico enviado, limitado também em caracteres.                                                    |
| `MaxContextCharacters`    | 48.000 | Instruções, schemas, histórico, estado e resultados acumulados; excedente encerra/encaminha o turno. |
| `MaxOutputTokens`         | 600    | Saída solicitada ao provedor por chamada.                                                            |
| `MaxToolCalls`            | 8      | Ferramentas por turno, executadas sequencialmente.                                                   |
| `TimeoutSeconds`          | 30     | Timeout de cada requisição externa.                                                                  |
| `TurnTimeoutSeconds`      | 90     | Prazo total do turno.                                                                                |
| `MaxTurnsPerConversation` | 100    | Reservas de turnos por conversa.                                                                     |
| `MaxDailyRequests`        | 300    | Orçamento diário de reservas de requisições externas.                                                |

A lease padrão dura 120 segundos (`TurnTimeoutSeconds + 30`). Se a instância parar, outra pode recuperar um turno após expiração, respeitando orçamento e idempotência das ferramentas. A reserva diária usa a data UTC e um lock compartilhado entre instâncias: cada turno reserva antecipadamente `MaxToolCalls + 1`, ou seja, **9 chamadas**. O padrão de 300 permite **33 reservas completas por dia** (297 unidades), mesmo quando alguns turnos precisarem de menos chamadas. Reservas não são devolvidas em falhas; retentativas/reinícios também podem consumir reservas.

Esse controle limita requisições, contexto e saída; **não é um teto em reais** nem garantia de consumo exato de tokens. Avaliar orçamento do provedor/conta no piloto. Métricas de tokens, quando retornadas, são registradas sem prompts, dados de cliente, corpo de erro do provedor ou credenciais.

## Migration e aplicação local

`20261010152627_AddAiChatAgent` adiciona campos de modo/lease/contadores nas conversas, permite mensagens `Assistant` e eventos sem funcionário fictício, e cria `ChatOrderDrafts` com constraints, FK e unicidade. `AutomationMode` tem default `Manual`; conversas e histórico existentes são preservados. Não se executa migration automaticamente no startup.

O `Down` recusa a reversão quando há montagem, mensagens de IA/eventos automáticos ou uso do atendimento automático. Essa proteção impede descartar histórico para adaptar dados ao esquema antigo. Não se deve apagar registros para forçar a reversão.

A migration está preparada para testes em PostgreSQL isolado e não foi aplicada à base operacional neste incremento. Antes de aplicá-la ao ambiente do piloto, conferir a conexão efetiva, o banco Made in Minas, a versão anterior `20261010142620_AddStoreAttendanceSettings`, backup verificável e SQL do incremento. A configuração de IA não substitui essa conferência do destino.

Para inspecionar o SQL sem aplicá-lo:

```powershell
dotnet tool restore
dotnet ef migrations script 20261010142620_AddStoreAttendanceSettings 20261010152627_AddAiChatAgent --project backend/MadeInMinas.Api --output .local/ai-chat-migration.sql
```

## Arquivos e etapas

| Etapa                       | Implementação e principais arquivos                                                                                                                                                          | Validação                                                                                                                  |
| --------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------- |
| 1 — Diagnóstico             | Solução, chat, autorização pública/equipe, checkout, banco, testes e polling confirmados; [arquitetura](architecture.md).                                                                    | Inspeção antes das alterações; reaproveitamento dos contratos e serviços existentes.                                       |
| 2 — Provedor                | `Services/Ai/AiContracts.cs`, `AiOptions.cs`, `AiService.cs`, `OpenAiProvider.cs`, `GeminiAiProvider.cs`, `AiProviderRegistration.cs`, `AiToolCatalog.cs`, `Program.cs`, `appsettings.json`. | `AiProviderTests`: 19 casos; `GeminiAiProviderTests`: 25 casos. Configuração ausente/desativada não chama o provedor.      |
| 3 — Contexto e persistência | `AiChatCoordinator.cs`, `AiChatWorker.cs`, `AppDbContext.cs`, modelos/configurações/DTOs do chat, migration e snapshot.                                                                      | `AiChatAutomationTests` e os seis casos de `AiChatMigrationTests`; histórico preservado e resposta concorrente descartada. |
| 4 — Cardápio real           | `AiChatTools.cs`, `PublicMenuService.cs`; DTOs públicos existentes e consulta de produto.                                                                                                    | `AiChatToolsTests`: oito casos compartilhados entre consulta, segurança e montagem.                                        |
| 5 — Montagem e cálculo      | `ChatOrderDraft.cs`, sua configuração, `ChatOrderContracts.cs`, `ChatOrderService.cs`; serviços de orçamento e atendimento existentes.                                                       | `ChatOrderTests`: quantidades, alterações, remoção, dados obrigatórios, taxa e valores oficiais.                           |
| 6 — Confirmação e criação   | `PublicChatOrderController.cs`, `ChatOrderService.cs`, `PublicCheckoutService.cs`; resumo/botão e recuperação em `public-chat.page.*` e estado público.                                      | `ChatOrderTests`: revisão, confirmação concorrente, mudanças comerciais, isolamento e recibo; E2E do resumo.               |
| 7 — Controle humano         | `HumanChatService.cs`, controllers existentes, `chat-api.service.ts`, interceptor, lista, páginas da equipe e componente de mensagens.                                                       | `AiChatAutomationTests`: 14 casos; E2E da transferência/retomada e regressões do atendimento manual.                       |
| 8 — Testes e documentação   | Seis arquivos de testes backend acima, `e2e/ai-chat.spec.ts`, este guia e documentação existente.                                                                                            | 72 testes backend iniciais + 25 do Gemini e 58 E2E do protótipo aprovados; execuções e limites descritos abaixo.           |

Os grupos de testes abrangem mais de uma etapa. `ChatOrderTests` tem 25 casos no total. Nenhuma etapa depende de novo cadastro artificial: cardápio, estoque, entrega e atendimento vêm dos serviços existentes. Para executar o piloto faltam a aplicação da migration no ambiente escolhido e a configuração de modelo/chave; não houve chamada ao provedor real.

Os 20 cenários obrigatórios estão distribuídos assim:

| Cenários                                              | Cobertura                                                                                                                                                                                                             |
| ----------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 1–3: saudação, cardápio e produto inexistente         | `AiChatToolsTests.NaturalLanguageScenariosUseRealBackendToolsAndOnlyAuthorizedPublicData` e teste de saudação da automação.                                                                                           |
| 4–6: adicionar, alterar quantidade e remover          | `ChatOrderTests.AddChangeAndRemoveUseServerAmountsAndIdempotentTargetQuantities` e validação de quantidades inválidas.                                                                                                |
| 7–8: promoção inexistente e desconto não autorizado   | Teoria de linguagem natural em `AiChatToolsTests`, cenários `promotion` e `discount`.                                                                                                                                 |
| 9–10: dados ausentes e confirmação válida             | `MissingDataPreventsReviewAndOrderCreation` e `ReviewDoesNotCreateAnOrderAndConfirmationUsesExistingCheckoutRules`.                                                                                                   |
| 11: alteração após revisão/confirmação                | `DraftChangeInvalidatesReviewAndRejectsPreviousConfirmation` exige nova revisão; `ReceivedOrderRejectsLaterDraftChangesAndKeepsItsOriginalReceipt` protege o pedido já enviado.                                       |
| 12: repetição/idempotência                            | `ConcurrentConfirmationRetriesProduceOnlyOneOrder`, retries de mensagens na automação e `DuplicateToolCallIsExecutedOnce`.                                                                                            |
| 13–15: indisponibilidade, desativação e tomada humana | `AiChatAutomationTests` e `AiProviderTests`, incluindo descarte da resposta ainda em processamento.                                                                                                                   |
| 16: acesso a outro cliente/pedido                     | `OtherConversationAndForgedCredentialCannotConfirmThisDraft` e regressão existente `PublicOrderTrackingTests.CredentialReadsOnlyItsOrderAndDoesNotGrantStaffAccess`; a ferramenta de status não aceita um ID externo. |
| 17–18: valores manipulados e indisponibilidade        | `PriceAndDiscountFieldsAreRejectedByThePublicConfirmationContract`, `CommercialChangeAfterReviewRejectsCreationAtomically` e ferramenta inválida.                                                                     |
| 19–20: falha de ferramenta e dados administrativos    | `AFailedToolEndsTheTurnWithNoSuccessClaim`, `FailedToolCannotChangeDraftOrReturnAdministrativeInformation`, cenário `private-data` e bloqueio de ferramentas irrestritas.                                             |

## Validação e pendências

Validação executada em 10/10/2026: **174/174 testes backend selecionados**, mais o teste final dedicado a mudanças após pedido recebido, **1/1 aprovado**. São 175 casos distintos nessas duas execuções, incluindo **72 casos novos de IA**. A execução ampla anterior aprovou os 612 testes preexistentes, mas encontrou uma falha na validação de quantidade zero; essa falha foi corrigida e os casos afetados passaram na regressão de 174. A execução ampla não foi repetida nem declarada integralmente aprovada. **58/58 testes E2E** passaram em desktop/mobile, incluindo 16 novos casos da integração.

Build Angular, compilação Release da API/testes, lint, formatação e revisão visual do resumo concluídos. O SQL da migration foi gerado e os testes verificaram aplicação preservando mensagens antigas e rejeição de reversão com dados de IA. Relatórios e comandos estão em [validation.md](validation.md). Testes usam provedores simulados/HTTP simulado e PostgreSQL isolado, sem chamadas pagas, pedidos operacionais ou impressão física.

Comandos de reprodução:

```powershell
# Raiz: banco temporário isolado, nunca a base operacional
powershell -NoProfile -File scripts/Test-Authentication.ps1

# frontend/made-in-minas
npm.cmd run lint
npm.cmd run build
npm.cmd run test:e2e -- ai-chat.spec.ts human-chat.spec.ts chat-workspace.spec.ts
```

### Validação incremental do Gemini

A adaptação posterior passou em **66/66 testes backend selecionados**, em 42,34 segundos: 25 novos de `GeminiAiProviderTests`, 19 de `AiProviderTests`, 14 de automação e oito de ferramentas. Cobrem seleção por configuração, chave somente no header, schemas, ciclo completo de ferramenta/resposta, preservação de assinatura, IDs presentes/ausentes, chamadas sem argumentos, quota/indisponibilidade, rejeição de chamadas paralelas e bloqueio de funções arbitrárias. HTTP e respostas dos modelos são simulados; nenhum Google/OpenAI real foi acionado. Relatórios `.local/gemini-regression-results.trx` e `.local/gemini-regression-tests.log`.

Os 72 casos iniciais e 58 E2E acima pertencem à validação original; a troca de provedor acrescenta 25 testes ao projeto. Frontend, banco e migration não foram modificados para essa troca, portanto não houve nova execução E2E nem aplicação de schema nesta adaptação. A API/testes foram compilados em Release durante a regressão; formato C#/Prettier e `git diff --check` conferidos.

O piloto real ainda depende de modelo/chave fornecidos pelo responsável, conferência do nível gratuito/cotas e aplicação da migration no ambiente escolhido. Deve exercitar linguagem natural, ambiguidades, quantidade total, endereço, revisão alterada, produto sem saldo, fechamento/pausa, retomada humana e resposta perdida, usando um ambiente e dados fictícios definidos. O fornecedor simulado verifica a integração e suas regras; não comprova a qualidade de interpretação de um modelo real.

Publicação, funcionamento contínuo, HTTPS/proxy, persistência das chaves de Data Protection, monitoramento e retenção de dados seguem os critérios de hospedagem e do [levantamento para IA](ai-readiness.md). Este incremento não publica o sistema em produção. Commits e pushes continuam dependentes de autorização explícita do responsável para estas alterações.

## Piloto local com Gemini real

O piloto usa outra instância PostgreSQL em `127.0.0.1:55435`, banco `made_in_minas_ai_pilot`, API em `http://localhost:5082` e Angular em `http://localhost:8102`. Todo produto, insumo, cliente, conversa e pedido nesse banco é de teste. Não representa o cardápio operacional nem os dados dos clientes da loja.

Após cadastrar modelo/chave com `Configure-Gemini.ps1`, execute no PowerShell da pasta do repositório:

```powershell
powershell -NoProfile -File scripts/Start-AiPilot.ps1
```

Requer SDK .NET 10, PostgreSQL 17 e dependências Angular instaladas. Para outro diretório do PostgreSQL, informe `-PostgresBin`. O script inicia processos ocultos, aplica as migrations apenas ao banco isolado, cadastra três produtos fictícios e um administrador. O utilitário `scripts/AiPilot/Program.cs` verifica host, porta, nome do banco, usuário e SSL antes de abrir a conexão. Reiniciar preserva estoque, pedidos, histórico e usuário; não há limpeza automática dos dados.

- Cliente: `http://localhost:8102/pedido/atendimento`.
- Equipe: `http://localhost:8102/entrar`, usuário `piloto.admin`.
- Senha aleatória do administrador e credenciais desse banco: `.local/ai-pilot/pilot.local.json`, ignorado pelo Git. Esse arquivo não contém a chave Gemini. Não compartilhe nem publique a pasta `.local`.
- IA habilitada somente no processo dessa API. User Secrets e configuração versionada continuam com IA desligada; o frontend recebe apenas a URL pública da API.
- Limites do piloto: 2.048 tokens de saída por chamada, 30 turnos por conversa e 300 reservas diárias de chamadas por padrão. Cada turno reserva até nove chamadas, mesmo que utilize menos; a cota conservadora não representa uma contagem exata de chamadas efetivas nem um teto financeiro. `-MaxDailyRequests` permite ajustar esse limite local; depois da avaliação repetível, a sessão foi iniciada com 600 reservas para permitir experimentar o chat após os testes. Isso não altera a cota nem o faturamento no Google. Para conservar esse limite ao reiniciar, use `powershell -NoProfile -File scripts/Start-AiPilot.ps1 -MaxDailyRequests 600`.
- Recebimento fora do horário habilitado somente no processo do piloto para testar checkout. As regras e configurações operacionais não são alteradas.
- Impressoras e estações reais não são cadastradas nesse banco. Pedidos confirmados nele são fictícios.

Para encerrar os processos, mantendo o banco e o histórico:

```powershell
powershell -NoProfile -File scripts/Stop-AiPilot.ps1
```

O encerramento confere os PIDs e os comandos dos processos antes de parar API/Angular e usa apenas o diretório do cluster do piloto para parar PostgreSQL. Logs de preparo, build e execução ficam em `.local/ai-pilot`. A porta ocupada impede iniciar outra instância, sem encerrar processos desconhecidos.

A API Gemini respondeu em chamadas reais neste piloto, incluindo cardápio, quantidades, remoção e revisão de retirada. Uma chamada recebeu HTTP 429 e a conversa passou para atendimento manual, sem retry automático; a retomada/testes devem respeitar as cotas do projeto. O modelo está disponível para a chave cadastrada, mas o nível de faturamento da conta não foi verificado pelo sistema. A camada gratuita e suas condições permanecem aquelas do Google AI Studio. Não use dados reais de clientes nesse piloto.

A composição das respostas conserva fatos das consultas, substitui os estados intermediários do pedido e limita o texto persistido a 2.000 caracteres. Falhas exibem o erro autorizado pelo backend, sem alegar sucesso anterior. As instruções esclarecem que retirada usa `address=null` e entrega exige a região obtida da ferramenta de taxas. Isso melhora a integração observada; não garante que qualquer mensagem ambígua será interpretada corretamente.

O teste de entrega revelou que instruções sozinhas não impedem um modelo de preencher dados pessoais não informados. `AiChatTools` agora verifica nome e telefone nas entradas do visitante dessa conversa; nome também pode vir do formulário inicial. Rua, número e bairro precisam constar nas mensagens do próprio cliente, com normalização de acentos/maiúsculas e limites de palavras. São examinadas até 50 mensagens do visitante, limitadas à sequência do turno; mensagens da equipe, resultados de ferramentas e outras conversas não autorizam esses dados. CEP, complemento e referência propostos sem correspondência no texto do cliente são omitidos. Dados obrigatórios sem correspondência rejeitam a alteração antes de persistir. A correspondência textual não resolve toda ambiguidade de intenção; a revisão explícita do cliente continua obrigatória.

Depois desse reforço, a entrega real no piloto rejeitou o endereço ausente e aceitou o endereço fictício informado, com subtotal R$ 25,90, taxa R$ 5,00 e total R$ 30,90. O pedido permaneceu em montagem, sem confirmação. A retirada teve criação idempotente de um pedido fictício de R$ 51,80, consulta de status e continuidade por atendente humano.

O conjunto repetível de dez cenários, as verificações sem chamadas pagas e os relatórios de latência/consumo estão em [ai-evaluation.md](ai-evaluation.md). Consultas gerais ao cardápio devem usar busca vazia para conservar acompanhamentos; pedidos de desconto recebem a recusa específica, em vez de apenas informar ausência de promoções.

Resultados, falhas encontradas e validação final: [validation.md](validation.md).

## Ensaio com catálogo comercial

O perfil separado `Rehearsal` captura somente os dados públicos da API local e mantém banco, identidade de login e origem do navegador próprios. O chat apresenta um aviso de ensaio; pedidos, insumos e clientes continuam fictícios. Inicialização, reimportação, avaliação com Gemini e roteiro da equipe estão em [ai-rehearsal.md](ai-rehearsal.md).

As instruções distinguem a consulta de ingredientes, que exige `consultar_produto`, das listas de cardápio. Horários/pagamentos e taxas usam ferramentas separadas sem argumentos; dados já presentes na última mensagem devem ser registrados antes de perguntar novamente. O estado persistido continua sendo a fonte dos cálculos. Diagnósticos em nível Debug registram somente nomes autorizados de ferramentas, sucesso e contagens de campos ausentes/extras, nunca argumentos, resultados ou mensagens pessoais.
