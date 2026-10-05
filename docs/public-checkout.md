# Checkout público para retirada — Fase 8C

Continuidade autorizada em 05/10/2026. A 8B foi preservada no commit local `15d8107`; `feat/public-checkout` parte dele. Sequência local: 8A (`4bcd978`) → 8B (`15d8107`) → 8C. Aceite manual e publicação continuam separados.

O visitante monta o carrinho, acessa `/pedido/finalizar`, informa nome/telefone, revisa os dados e envia um pedido para **retirada no balcão**. A equipe recebe o pedido **Novo**, confere o contato e confirma o preparo pelo fluxo existente. O site não cobra nem registra recebimento. Pagamento é tratado na retirada pela equipe.

## Escopo e decisões

- Retirada é o escopo deste incremento: o projeto ainda não tem regra pública de frete. Foi solicitada a preferência do responsável; enquanto não definida, não se inventa taxa ou entrega gratuita. Entrega pública exige próximo contrato com cobertura, endereço e taxa calculada no backend.
- Nome e telefone são autodeclarados. Validar o formato brasileiro não autentica a pessoa nem comprova a titularidade. Não há busca pública por telefone, autofill de cadastro, acesso a endereços ou histórico anterior.
- Somente ao enviar, o backend encontra ou cria Customers pelo telefone normalizado. Um cadastro existente não é atualizado; o nome informado fica na cópia histórica do pedido. Cliente inativo impede o envio com mensagem genérica, sem revelar seus dados. A equipe deve conferir o contato antes do preparo. Futura área do cliente exigirá identidade verificada para consultar informações anteriores.
- Revisar não grava clientes ou pedidos. Enviar cria cliente quando necessário, pedido, itens e primeira entrada de histórico na mesma transação. Falha na revisão/indisponibilidade desfaz inclusive o novo cliente.
- Pedido nasce `New`, origem `DirectLink`, `StockStatus=Pending`, `Fulfillment=Pickup`, taxa zero e total igual ao subtotal. Nomes/preços/observações são copiados do conteúdo revalidado. Não há endereço, desconto, pagamento, baixa ou confirmação automática.
- Confirmação, cancelamento, cozinha, expedição e recebimento permanecem nas APIs da equipe. Confirmar utiliza a ficha técnica/estoque existentes; a criação pública não contorna essas verificações.
- Criação pública tem `CreatedById=null` e histórico inicial com `ActorId=null`, `ActorName="Cliente pelo site"`. Não se cria funcionário fictício. Ações operacionais posteriores continuam exigindo funcionário autenticado. O detalhe da equipe distingue origem e contato não verificado.
- Limites do carrinho permanecem: 50 linhas, 99 unidades somadas por produto, 250 caracteres por observação de item e 500 gerais. Nome até 120 caracteres, telefone até 30 antes da normalização. Campos desconhecidos são rejeitados.

## API

Ambos os endpoints são anônimos, com no-store e corpo limitado a 65.536 bytes. Compartilham limite de 20 requisições por minuto por IP remoto, sem fila; excedente retorna 429. Limite em memória por processo; configuração de proxy confiável e proteção de produção dependem da infraestrutura. Não usa telefone como chave do limitador. O frontend não anexa JWT de funcionário, e falhas públicas não revogam a sessão da equipe.

`POST /api/public-checkout/review`:

```text
{ name, phone, cart: { items: [{ productId, quantity, notes }], notes } }
```

Resposta 200:

```text
name, phone, fulfillment: "Pickup"
items: [{ productId, name, quantity, unitPrice, lineTotal, notes }]
notes, subtotal, deliveryFee: 0, total, reviewToken, calculatedAt
```

Nome/telefone na resposta vêm da entrada normalizada, nunca de um cadastro encontrado pelo telefone. Valores vêm de PublicCartService, em decimal. `reviewToken` é o SHA-256 canônico dos dados e preços revisados, excluindo o instante da consulta. Não é credencial, reserva nem autorização de preço; o servidor recalcula tudo ao enviar.

`POST /api/public-checkout/orders`:

```text
{ requestId: UUID-aleatório, reviewToken, checkout: { name, phone, cart } }
```

Resposta 201 na criação e 200 na repetição idêntica:

```text
{ number, fulfillment: "Pickup", total, createdAt }
```

É um comprovante de recebimento, sem IDs internos, cadastro, endereço, dados pessoais ou status de acompanhamento. Não há GET público por número/telefone/ID. A mesma tentativa retorna o comprovante original mesmo após alterações do catálogo ou avanço do pedido.

- 400: ValidationProblemDetails, entrada inválida ou campo desconhecido.
- 409 / OrderReviewChanged: dados/preços mudaram; nova revisão necessária.
- 409 / CartProductUnavailable: produto/categoria inativo, venda pausada ou produto ausente.
- 409 / PublicCheckoutUnavailable: envio impedido pelo estado do cadastro; mensagem genérica para procurar atendimento.
- 409 / OrderRequestConflict: chave já usada com outro conteúdo; manter a recuperação e conferir com atendimento.
- 429: excesso de requisições. Falhas internas seguem ProblemDetails.

## Transação e concorrência

Um advisory lock transacional por tentativa serializa reenvios, e o índice único parcial de RequestId para origem DirectLink garante unicidade no banco. O hash da tentativa inclui contato, itens, observações normalizados e reviewToken. Reenvio já concluído é resolvido antes da disponibilidade atual; conteúdo diferente com a mesma chave é rejeitado. Chaves diferentes representam compras diferentes e não são deduplicadas pelo telefone.

Resolução de cliente usa INSERT parametrizado com ON CONFLICT (Phone) DO NOTHING e leitura com FOR SHARE, sem sobrescrever cadastro concorrente. Depois são bloqueados os produtos e categorias, em ordem determinística, acompanhando a ordem cliente → catálogo do fluxo manual. A revisão usa a transação já aberta. Pedido/itens/histórico e eventual cliente são gravados atomicamente.

## Recuperação no navegador

- Montagem do carrinho e preenchimento inicial continuam em memória. A revisão é descartada ao editar o contato ou sair da página.
- Antes do primeiro HTTP de envio, a tentativa completa e os nomes para reconstrução do carrinho são guardados em **sessionStorage desta aba**, sob `made-in-minas.public-checkout.v1`. Inclui nome, telefone, itens, observações, reviewToken e UUID; nunca JWT. Se não for possível guardar, nenhum HTTP de criação é iniciado.
- Falha, timeout de 15 segundos ou saída durante o envio mantém a tentativa. Recarregar a aba oferece **Conferir envio**, repetindo o mesmo conteúdo/chave. Não dispara criação automática ao carregar a página. Fechar a aba pode perder a recuperação; nesse caso, conferir com o atendimento antes de montar outro pedido.
- Enquanto houver tentativa pendente, cardápio/carrinho impedem alterações e oferecem acesso à conferência. Resposta tardia após sair da página não altera o estado; nova tentativa recupera o resultado no backend.
- Sucesso substitui os dados pessoais armazenados por um comprovante mínimo e limpa o carrinho. Recarregar conserva esse comprovante. **Montar outro pedido** apaga a recuperação e inicia outra seleção.
- Somente rejeições de domínio que provam que a tentativa não foi gravada liberam nova revisão: OrderReviewChanged, CartProductUnavailable e PublicCheckoutUnavailable. Os itens/observações são restaurados, inclusive depois de reload. Outros erros preservam a chave para evitar duplicidade.
- Recuperação inválida exige conferir com atendimento antes de limpá-la. O armazenamento não é uma identidade autenticada; o backend valida toda entrada novamente.

## Banco e migration

`20261005145517_AddPublicOrders` altera somente Orders/OrderStatusHistory:

- CreatedById e ActorId passam a aceitar null para criação pública.
- CK_Orders_Origin exige funcionário em Manual e ausência de funcionário/retirada em DirectLink.
- CK_OrderStatusHistory_Actor exige funcionário nas transições posteriores à criação.
- Índice único parcial de RequestId para DirectLink; a idempotência manual por funcionário permanece.

Não cria tabelas ou dependências. Up preserva os dados existentes. Down recusa execução quando existem pedidos públicos: não apaga pedidos nem inventa autoria para satisfazer o esquema antigo. A aplicação local e os testes estão em [validation.md](validation.md). Antes de aplicar em outra máquina, conferir destino e backup; branches não isolam bancos. Não executar no parsmartmanager.

## Como validar

1. Abrir `http://localhost:8101/pedido`, adicionar produto e acessar **Ver carrinho → Continuar para retirada**.
2. Informar nome/telefone, revisar e conferir itens, observações, modalidade e total. Editar contato deve esconder a revisão e o botão de envio até nova revisão.
3. Em ambiente de testes, enviar e conferir o número no comprovante. Na equipe, abrir Pedidos e conferir Novo, Link direto, contato informado e histórico Cliente pelo site. Não criar pedidos fictícios na operação apenas para testar.
4. Confirmar pela equipe com ficha/saldo adequados e conferir a baixa; recebimento permanece separado. Cancelamento segue permissões e regras de estoque existentes.
5. Simular resposta perdida, recarregar a aba e usar Conferir envio: deve recuperar o mesmo número, sem duplicar pedido. Durante a pendência, tentar adicionar/limpar itens deve orientar a conferência anterior.
6. Em testes, alterar preço/disponibilidade entre revisão e envio: o envio deve ser rejeitado, sem cliente/pedido parcial, e permitir corrigir/revisar.
7. Após sucesso, recarregar conserva comprovante; Montar outro pedido limpa a recuperação. Conferir desktop/celular. Não há acompanhamento de status público neste incremento.

```powershell
# Raiz: regressão backend com PostgreSQL isolado
powershell -NoProfile -File scripts/Test-Authentication.ps1

# frontend/made-in-minas
npm.cmd run format:check
npm.cmd run lint
npm.cmd run build
npm.cmd run test:e2e -- public-checkout.spec.ts public-cart.spec.ts menu.spec.ts orders.spec.ts staff.spec.ts
```

## Arquivos

- Backend novos: Controllers/PublicCheckoutController, DTOs/PublicCheckout/PublicCheckoutContracts, Services/PublicCheckoutService/PublicCheckoutFingerprint, migration AddPublicOrders e teste PublicCheckoutTests.
- Backend alterados: Program, PublicCartService, OrderException, Order/OrderStatusHistory e configurações, contrato OrderHistoryResponse, snapshot EF. A adaptação de assinatura foi feita pela refatoração do Rider.
- Frontend novos: core/services/public-checkout-api.service.ts, public-checkout-state.service.ts, features/public-checkout (TS/HTML/SCSS), e2e/public-checkout.spec.ts.
- Frontend alterados: estado do carrinho, cardápio, página do carrinho, detalhe de pedidos, contrato Order, interceptor, rotas e testes menu/orders.
- Documentação: README, roteiro, arquitetura, API, banco, regras, decisões, validação, continuidade de public-cart e este documento.

Próximo incremento proposto: entrega pública após definir área atendida e taxa; acompanhamento público deverá ter acesso próprio, sem abrir pedidos por telefone ou número. Conversa/transferência e IA continuam em incrementos separados.
