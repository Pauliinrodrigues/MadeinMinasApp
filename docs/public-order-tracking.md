# Fase 8D — acompanhamento público do pedido

Continuidade autorizada em 05/10/2026, na branch `feat/public-order-tracking`, criada do commit local `c8de7ee` da 8C. Implementação e testes deste incremento não substituem o aceite manual nem autorizam publicação.

## Escopo

Após enviar um pedido para retirada, o cliente acessa **Acompanhar meu pedido** no comprovante. A rota `/pedido/acompanhar` mostra número, total registrado, status atual e histórico com horários de Brasília. Os dados vêm do pedido central e refletem as alterações do atendimento, cozinha e expedição. Não há previsão fictícia de prazo, alteração pública de status, pagamento automático ou conversa neste incremento.

`New` significa recebido para análise; `Ready`, pronto para retirar. `Delivered` é apresentado como **Retirado** neste fluxo. A consulta continua até `Finalized` ou `Cancelled`. Cancelamento orienta procurar o atendimento, sem revelar motivos internos.

## Acesso e contrato

O comprovante de `POST /api/public-checkout/orders` passa a incluir:

```json
{
  "number": 1542,
  "fulfillment": "Pickup",
  "total": 59.8,
  "createdAt": "2026-10-05T14:00:00Z",
  "tracking": {
    "token": "<credencial protegida emitida pela API>",
    "expiresAt": "2026-10-12T14:00:00Z"
  }
}
```

A credencial é protegida com ASP.NET Core Data Protection, finalidade `MadeInMinas.PublicOrderTracking.v1` e aplicação `MadeInMinas.Api`. Contém somente o ID do pedido e o vencimento, cifrados e autenticados. Expira **sete dias após a criação do pedido**, com decisão pelo relógio do servidor. Repetir o checkout idempotente emite outra credencial válida para o mesmo pedido e o mesmo prazo; os bytes do token podem mudar. Após o vencimento, o comprovante continua recuperável, com `tracking: null`. O token não autentica o cliente, não valida seu telefone e não concede acesso administrativo.

`GET /api/public-orders/tracking` exige o cabeçalho `X-Order-Access` com essa credencial. O endpoint não recebe seletor de pedido por número, telefone ou ID. Tokens enviados apenas na URL não são aceitos. Quem possui a credencial consegue consultar aquele pedido durante sua validade; trate-a como segredo.

Resposta 200, com `Cache-Control: no-store`:

```json
{
  "number": 1542,
  "fulfillment": "Pickup",
  "total": 59.8,
  "status": "New",
  "createdAt": "2026-10-05T14:00:00Z",
  "updatedAt": "2026-10-05T14:00:00Z",
  "history": [{ "status": "New", "occurredAt": "2026-10-05T14:00:00Z" }]
}
```

A consulta projeta apenas esses campos de Orders/OrderStatusHistory em uma instrução SQL, com histórico ordenado pela versão. Não retorna cliente, telefone, endereço, observações, identificadores internos, funcionários, motivos de cancelamento, estoque ou dados de pagamentos. Não usa o DTO administrativo. Pedidos manuais são recusados mesmo se receberem credencial emitida pelo servidor.

Acesso ausente, inválido, adulterado, expirado ou para pedido inexistente retorna o mesmo `404/ProblemDetails`, sem indicar qual condição ocorreu. Credencial tem limite de 2.048 caracteres e só um valor de cabeçalho é aceito. Limite separado do checkout: 60 consultas por minuto por endereço remoto; exceder retorna 429. Proxy e restrição de origens devem ser configurados no ambiente de publicação, sem confiar em IP arbitrário enviado pelo visitante.

## Navegador

O acesso fica no comprovante já guardado em `sessionStorage`, na sessão da aba. Não vai na URL, no JWT da equipe ou em armazenamento permanente. Atualizar a página conserva o acompanhamento; fechar a aba pode perdê-lo. **Montar outro pedido remove o comprovante anterior e seu acesso desta aba**. Não existe recuperação pública por telefone/número ou compartilhamento de link neste incremento. Comprovantes anteriores à 8D continuam legíveis, orientando procurar o atendimento.

A página consulta a cada 15 segundos após a conclusão da chamada anterior, sem sobreposição. Ocultar a aba pausa as consultas e cancela a chamada pendente; voltar consulta novamente. Sair da página encerra temporizador e requisição. `Finalized`/`Cancelled` encerram a atualização automática. Há botão de atualização manual durante o acompanhamento.

Falhas temporárias removem o status antigo e mostram erro; repetição automática após 30 segundos, ou 60 segundos em 429. Timeout de 15 segundos. Acesso indisponível/expirado interrompe consultas e conserva o número no comprovante para atendimento. Falha pública não encerra a sessão da equipe.

## Banco, chaves e execução

Sem nova tabela, migration, escrita no banco ou dependência externa. O esquema continua em `20261005145517_AddPublicOrders`, necessário para o checkout. Não há execução de migration neste incremento.

Data Protection faz parte do ASP.NET Core. No Windows com perfil de usuário disponível, as chaves ficam em `%LOCALAPPDATA%/ASP.NET/DataProtection-Keys`, protegidas por DPAPI. Reiniciar sob o mesmo usuário preserva o acesso. Não versionar, imprimir ou transferir essas chaves como arquivos comuns do projeto. A outra máquina tem suas próprias chaves: um comprovante emitido por uma API não é automaticamente aceito por outra. Referência: [comportamento padrão e persistência das chaves](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/default-settings?view=aspnetcore-10.0).

Antes de hospedar em containers ou múltiplas instâncias, configurar e validar armazenamento durável/compartilhado de chaves, proteção em repouso e permissões da conta de serviço. O provedor padrão pode usar memória quando nenhum armazenamento apropriado está disponível; nesse caso, reiniciar invalida acessos anteriores. Manter nome de aplicação e finalidade estáveis. Remover ou revogar chaves pode invalidar comprovantes ainda vigentes. Este incremento usa a configuração padrão local; não entrega a infraestrutura de produção. Referência: [configuração do Data Protection](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-10.0).

Em produção, usar HTTPS e manter cabeçalho `X-Order-Access`, respostas do checkout e `sessionStorage` fora de logs/telemetria. Não habilitar captura indiscriminada de corpos/cabeçalhos. O aplicativo não registra a credencial. Não há revogação individual de comprovante nesta etapa; o acesso é somente leitura e limitado pelo prazo.

## Testar e validar

1. Em ambiente de testes, abrir `/pedido`, enviar uma retirada e clicar **Acompanhar meu pedido**. Deve mostrar Novo e o horário de recebimento.
2. Em outra aba, entrar na área da equipe e confirmar o mesmo pedido. No KDS, iniciar preparo e marcar pronto. O acompanhamento deve atualizar e orientar retirada apenas quando Pronto.
3. Registrar pagamento e retirada pelo fluxo existente e finalizar; conferir histórico e encerramento das consultas. Cancelar outro pedido e confirmar que o motivo interno não aparece.
4. Recarregar a aba: comprovante e acesso permanecem. Ocultar/voltar deve pausar/retomar consultas. Interromper a API deve remover o status antigo e permitir recuperar ao retornar.
5. Abrir a rota sem comprovante: orientar atendimento, sem pedir telefone ou buscar pedidos. Conferir também comprovante antigo, credencial inválida e expirada nos testes automatizados.
6. Conferir desktop/celular, leitura dos horários e ausência de rolagem horizontal. Nunca criar pedido fictício no banco operacional somente para testar este roteiro.

```powershell
# Raiz: PostgreSQL isolado e regressão backend
powershell -NoProfile -File scripts/Test-Authentication.ps1

# frontend/made-in-minas
npm.cmd run format:check
npm.cmd run lint
npm.cmd run build
npm.cmd run test:e2e -- public-order-tracking.spec.ts public-checkout.spec.ts public-cart.spec.ts menu.spec.ts staff.spec.ts
```

Resultados efetivamente executados ficam em [validation.md](validation.md).

## Arquivos

- Novos no backend: `Controllers/PublicOrderTrackingController.cs`, `DTOs/PublicOrders/PublicOrderTrackingContracts.cs`, `Security/PublicOrderAccess.cs`, `Services/PublicOrderTrackingService.cs`, testes `PublicOrderAccessTests.cs` e `PublicOrderTrackingTests.cs`.
- Alterados no backend: `Program.cs`, contrato/comprovante e serviço do checkout; fábrica de testes usa chaves efêmeras isoladas e testes do checkout passam a verificar prazo e equivalência comercial da repetição.
- Novos no frontend: `core/services/public-order-tracking-api.service.ts`, `features/public-order-tracking` (TS/HTML/SCSS), `e2e/public-order-tracking.spec.ts`.
- Alterados no frontend: rotas, interceptor, contrato do comprovante, página e testes do checkout.
- Documentação: README, roteiro, arquitetura, API, banco, regras, decisões, validação e continuidade do checkout.

Próximos incrementos da Fase 8: conversa e transferência para atendente; entrega pública após definir cobertura e frete. Integração com IA permanece na Fase 9.
