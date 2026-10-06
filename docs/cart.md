# Carrinho da equipe — Fase 4B

Este documento registra o contrato e o aceite da Fase 4B. A [Fase 4C](orders.md) estende o carrinho com taxa de entrega explícita, total, identificação da revisão e registro de pedidos. Para entrega, os clientes da API devem agora enviar `deliveryFee`; retirada aceita omissão/nulo/zero. As limitações abaixo descrevem a entrega original da Fase 4B.

Aceite manual concluído pelo usuário em 01/10/2026. O [PR #3](https://github.com/Pauliinrodrigues/MadeinMinasApp/pull/3) registra a revisão da branch `feat/staff-cart` para `master`; o usuário também autorizou sua integração, condicionada à aprovação dos checks de qualidade do commit final.

## Escopo

Administrador e atendente montam um carrinho temporário, selecionam cliente ativo, retirada ou entrega, endereço ativo do cliente para entrega, produtos disponíveis, quantidades e observações. A API consulta os cadastros e calcula o subtotal em decimal. A revisão não cria um pedido nem reserva preço ou estoque.

Este incremento usa a permissão existente `orders.manage`, sem conceder ao atendente acesso à administração do catálogo. Não há novas dependências, tabelas ou migrations. Desde os [ajustes operacionais](operational-usability.md), o rascunho fica em `sessionStorage`, separado pelo identificador do funcionário, por até 8 horas. Inclui contato/endereço e itens necessários à recuperação na mesma aba, sem senha ou JWT. Fechar a aba pode apagar o rascunho. Outro funcionário não recupera a montagem pela aplicação. Preços recuperados são apenas referência: registrar uma nova tentativa exige revisão atual na API.

Uma tentativa de registro sem resposta confirmada preserva a chave, o corpo e a revisão originais antes do HTTP; sua recuperação não expira automaticamente. Repetir consulta ou conclui a mesma tentativa no servidor. Falha de armazenamento impede iniciar o envio. Dados de recuperação inválidos bloqueiam a edição até conferência dos pedidos e descarte explícito. Cadastro de cliente/endereço pode ser aberto a partir do carrinho e retornar sem perder a seleção.

Expiração de sessão, falta de permissão e limite de requisições durante uma retomada não comprovam que o primeiro envio falhou. A tentativa continua preservada nesses casos, inclusive após novo login.

## Contratos e regras

- `GET /api/cart/products`: produtos efetivamente disponíveis (produto ativo, disponível e categoria ativa); busca por nome, filtro opcional de categoria e paginação (20 por padrão, até 100).
- `POST /api/cart/quote`: cliente, modalidade `Pickup` ou `Delivery`, endereço para entrega, itens e observações. Retorna dados atuais de cliente/endereço/produtos, preços unitários, totais dos itens, subtotal e instante UTC do cálculo. Respostas sem cache; JWT obrigatório.
- De 1 a 50 linhas; quantidade inteira de 1 a 99, com limite agregado de 99 por produto. Linhas do mesmo produto podem ter observações diferentes. Observações por linha até 250 caracteres e gerais até 500.
- Cliente e endereço devem estar ativos; endereço deve pertencer ao cliente. Retirada não aceita endereço. Entrega exige endereço.
- A requisição aceita apenas os campos declarados: nomes, preços, totais, descontos e taxas enviados pelo navegador são rejeitados. Valores comerciais vêm do banco.
- O cálculo faz leituras em transação com snapshot consistente. Não grava nem bloqueia cadastros para edição; futuras confirmações devem revalidar tudo transacionalmente e preservar cópias históricas.
- Alterar o carrinho invalida a revisão exibida; uma nova consulta usa preços e disponibilidade atuais. Falhas preservam os campos para correção/nova tentativa.
- Não há total final, cobrança, taxa de entrega ou desconto neste incremento. Exibir somente subtotal de produtos, sem assumir entrega gratuita. Combos e adicionais dependem de incrementos de catálogo próprios.

## Próximo incremento

A Fase 4C deverá definir e implementar criação idempotente de pedidos manuais, número atômico, cópias históricas, transições permitidas e histórico. Taxas, descontos e política de cancelamento precisam ter regras explícitas. Pagamentos terão estado independente; KDS e baixa de estoque seguem suas fases.

## Validação automatizada

Testes HTTP com PostgreSQL isolado: autorização, catálogo de venda, cálculos decimais, alterações de preço/disponibilidade, cliente/endereço, limites e rejeição de valores fornecidos pelo cliente. Testes de navegador desktop/celular: seleção, paginação, edição/remoção, revisão, falhas, troca de cliente/modalidade e encerramento da sessão. Rodar formatação, lint e builds. Aceite manual antes do próximo incremento.

Resultado em 01/10/2026: backend 190/190 aprovados, incluindo 29 casos novos; navegador com 24 casos novos aprovados e 152 cenários validados ao todo entre regressão completa e reexecução de um timeout existente de ingredientes. Builds, lint e formatação aprovados. Evidências e limites em [validation.md](validation.md).

## Exemplo de revisão

`POST /api/cart/quote`, com JWT da equipe:

```json
{
  "customerId": "UUID de um cliente ativo",
  "fulfillment": "Delivery",
  "addressId": "UUID de um endereço ativo desse cliente",
  "items": [{ "productId": "UUID de produto disponível", "quantity": 2, "notes": "Sem cebola" }],
  "notes": "Embalar separado"
}
```

Substitua os textos por UUIDs reais. Para retirada, use `Pickup` e `addressId: null`. `200` retorna `customer`, `fulfillment`, `address`, `items` (productId, name, quantity, unitPrice, lineTotal, notes), `notes`, `subtotal` e `calculatedAt`. `400` indica formato/limites inválidos ou campos adicionais; `401/403` indicam sessão/perfil; `409` indica cliente, endereço ou produto indisponível. A revisão não garante que o mesmo preço ou cadastro estará disponível numa solicitação futura.

## Arquivos do incremento

Criados:

- `backend/MadeInMinas.Api/Controllers/CartController.cs`
- `backend/MadeInMinas.Api/DTOs/Cart/CartContracts.cs`
- `backend/MadeInMinas.Api/Services/CartService.cs` e `CartException.cs`
- `backend/MadeInMinas.Api/Infrastructure/CartExceptionHandler.cs`
- `backend/MadeInMinas.Api.Tests/CartTests.cs`
- `frontend/made-in-minas/src/app/core/services/cart-api.service.ts`
- `frontend/made-in-minas/src/app/features/cart/cart.page.ts`, `.html` e `.scss`
- `frontend/made-in-minas/e2e/cart.spec.ts`
- Este documento.

Alterados: `Program.cs` (DI/erros), `app.routes.ts`, `auth-session.service.ts`, `auth.guards.ts`, `staff-layout.page.ts`, `api-error.ts`, README e documentos de arquitetura, API, banco, regras, decisões, roteiro e validação. Não há mudanças em credenciais, dependências ou migrations.

## Testar e validar manualmente

1. Com API em `http://localhost:5080` e frontend em `http://localhost:8101`, entre como administrador ou atendente e abra **Carrinho**.
2. Selecione um cliente ativo; adicione produtos, ajuste quantidades e observações. Revise e confira preços e subtotal.
3. Escolha entrega: a revisão exige endereço desse cliente. Troque o cliente e confira que o endereço anterior foi descartado. Retirada dispensa endereço.
4. Altere uma quantidade/observação: a revisão anterior deve desaparecer. Adicione o mesmo produto em outra linha para observações diferentes; teste o limite agregado de 99 unidades.
5. Em outra sessão de administrador, altere o preço ou a disponibilidade de um produto. Revisar novamente deve refletir o preço atual ou informar indisponibilidade, preservando os itens.
6. Teste buscar, paginar, remover itens e limpar com confirmação. Sair da tela descarta o carrinho. Cozinha e expedição não acessam a rota `/equipe/carrinho`.
7. Confirme que a tela apresenta subtotal de produtos e não informa pedido criado, cobrança realizada ou entrega gratuita.

Automação na raiz: `powershell -NoProfile -File scripts/Test-Authentication.ps1`; no frontend: `npm.cmd run format:check`, `npm.cmd run lint`, `npm.cmd run build` e `npm.cmd run test:e2e`. Para somente o módulo: backend com `-Filter CartTests`; navegador com `npm.cmd run test:e2e -- e2e/cart.spec.ts`. Os testes HTTP usam banco temporário isolado; os de navegador simulam a API. Resultados em [validation.md](validation.md).
