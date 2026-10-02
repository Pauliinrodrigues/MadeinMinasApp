# API — Fundação e autenticação

Base local: `http://localhost:5080`.

| Método | Rota | Resposta |
| --- | --- | --- |
| GET | /api/system/status | 200, `{"status":"available"}` |
| GET | /health/live | 200, `{"status":"Healthy","checks":{}}` |
| GET | /health/ready | 200 quando conectado; 503 sem conexão |
| GET | /openapi/v1.json | Documento OpenAPI, somente Development |

Readiness saudável:

```json
{"status":"Healthy","checks":{"database":"Healthy"}}
```

Sem configuração ou com PostgreSQL inacessível:

```json
{"status":"Unhealthy","checks":{"database":"Unhealthy"}}
```

O status da aplicação não informa prontidão do banco. Liveness não depende do banco, evitando reinícios indevidos durante uma indisponibilidade do PostgreSQL.

Respostas públicas de saúde não incluem host, senha, stack trace ou mensagens de exceção. As verificações de saúde têm prazo de cinco segundos.

Rotas desconhecidas retornam 404 em ProblemDetails para requisições JSON. Erros não tratados usam ProblemDetails sem detalhes internos.

Os endpoints técnicos acima são públicos. Os controllers de autenticação e perfis foram adicionados na Fase 2A; consulte [authentication.md](authentication.md) para contratos e permissões. Health checks são middleware e estão documentados aqui.

CORS em Development aceita origem `http://localhost:8101`, métodos GET/POST/PUT e cabeçalhos de requisição, incluindo Authorization.

A Fase 2B adiciona /api/users e PUT /api/auth/password. Contratos, paginação, permissões e erros estão em [users.md](users.md).

A Fase 3A adiciona GET/POST /api/categories, GET/PUT /api/categories/{id} e PUT /api/categories/{id}/status. Todos exigem catalog.manage. Contratos e regras: [categories.md](categories.md).

A Fase 3B adiciona GET/POST /api/products, GET/PUT /api/products/{id}, PUT /api/products/{id}/status e PUT /api/products/{id}/availability. Todos exigem catalog.manage; a API calcula a disponibilidade considerando a categoria. Contratos e regras: [products.md](products.md).

A Fase 3C adiciona GET/POST /api/ingredients, GET/PUT /api/ingredients/{id} e PUT /api/ingredients/{id}/status. Todos exigem catalog.manage. Custos e quantidades são validados sem arredondamento; alterações de unidade são rejeitadas. Contratos: [ingredients.md](ingredients.md).

A Fase 3D adiciona GET/PUT /api/products/{productId}/recipe, com catalog.manage. PUT cria ou substitui integralmente a composição em transação. GET distingue produto inexistente e produto sem ficha. Contratos e exemplos: [recipes.md](recipes.md).

A Fase 4A adiciona GET/POST /api/customers, GET/PUT /api/customers/{id}, PUT /api/customers/{id}/status e operações de endereços sob /api/customers/{customerId}/addresses. Todos exigem customers.manage, para administrador e atendente. Telefone é normalizado e único; vínculo de endereço é conferido na leitura e gravação. Contratos e erros: [customers.md](customers.md).

A Fase 4B adiciona GET /api/cart/products e POST /api/cart/quote, com orders.manage (administrador e atendente). O catálogo de venda retorna somente produtos efetivamente disponíveis, sem conceder acesso ao CRUD administrativo. A revisão usa cliente/endereço/produtos atuais e calcula o subtotal, sem criar pedidos nem reservar estoque. Campos comerciais adicionais na requisição são rejeitados com 400; indisponibilidade retorna 409 com CartCustomerUnavailable, CartAddressUnavailable ou CartProductUnavailable. Contratos e exemplo: [cart.md](cart.md).

A Fase 4C estende a revisão com taxa de entrega validada, total e identificação do conteúdo, e adiciona GET/POST /api/orders, GET /api/orders/{id} e PUT /api/orders/{id}/status. Todos exigem orders.manage. Criação idempotente (201/200), cópias dos dados da compra, número único e histórico. Estados iniciais: New, Confirmed e Cancelled. Confirmar não recebe pagamento. Contratos, permissões de cancelamento e conflitos: [orders.md](orders.md).

A Fase 4D adiciona GET/POST /api/orders/{orderId}/payments, GET /api/orders/{orderId}/payments/{id} e PUT nas terminações /receive, /cancel e /refund. Todos exigem payments.manage; /refund exige também payments.refund (administrador). Somente registro manual, com valor integral derivado do pedido, idempotência e histórico. Cancelar pedido com pagamento Pending/Received retorna OrderPaymentUnresolved (409). Contratos e regras: [payments.md](payments.md).

A Fase 5A adiciona GET /api/kitchen/orders e PUT /api/kitchen/orders/{id}/status, com kitchen.work (administrador/cozinha). A leitura retorna três colunas paginadas em snapshot consistente, somente com dados de produção. A escrita recebe status e expectedVersion, permitindo Confirmed → InPreparation → Ready. Os filtros/listagens de pedidos reconhecem os novos estados; cancelamento de InPreparation/Ready exige administrador e resolução do pagamento. Contratos, paginação e roteiro: [kitchen.md](kitchen.md).

As Fases 5B/5C adicionam GET /api/dispatch/orders e PUT /api/dispatch/orders/{id}/status (dispatch.work), GET /api/print/orders/{id}/kitchen (printing.kitchen) e GET /api/print/orders/{id}/dispatch (printing.dispatch). O filtro comercial de pedidos aceita AwaitingDelivery, OutForDelivery, Delivered e Finalized. Contratos, transições, papéis e erros: [expedição e impressão](dispatch-printing.md).

A Fase 6A acrescenta GET /api/ingredients/{ingredientId}/stock e POST /api/ingredients/{ingredientId}/stock/movements, ambos catalog.manage. GET retorna saldo e histórico paginado consistente. POST recebe RequestId, ExpectedVersion, Type (Entry/Exit/Count), Quantity e Reason; rejeita campos adicionais e retorna 200 inclusive na repetição idempotente. Autor e saldos são derivados pelo servidor. Contratos, erros e limites: [estoque manual](stock.md).
