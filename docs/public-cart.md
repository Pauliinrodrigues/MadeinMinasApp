# Carrinho público — Fase 8B

Continuação autorizada em 05/10/2026. A branch `feat/public-cart` parte do commit local `4bcd978`, que preserva a Fase 8A em `feat/public-menu`. Os dois incrementos ainda dependem de publicação; a autorização para continuar não substitui o aceite manual.

O visitante adiciona produtos em `/pedido` e abre **Ver carrinho** em `/pedido/carrinho`, sem login. Pode ajustar quantidades, escrever observações, remover itens, limpar a seleção com confirmação e revisar o subtotal calculado pela API. Este incremento não registra pedidos nem identifica clientes. Endereço, entrega, pagamento, acompanhamento, conversa, adicionais e combos continuam para etapas próprias.

Continuidade: a 8B foi preservada no commit local `15d8107`. A [Fase 8C](public-checkout.md) acrescenta o link Continuar para retirada e a finalização em página própria. As regras abaixo descrevem a montagem/revisão da 8B; somente a tentativa de envio da 8C usa recuperação em sessionStorage. Enquanto houver envio pendente ou comprovante, o carrinho encaminha à conferência anterior.

## Comportamento e limites

- Seleção recuperável em `sessionStorage` nesta aba por até 8 horas: produtos, quantidades e observações, sem tokens de funcionário nem preços persistidos. Navegação e recarga conservam a montagem. Fechar a aba pode removê-la; não há sincronização entre dispositivos. A tentativa pendente do checkout tem precedência sobre o rascunho.
- Adicionar novamente um produto soma a quantidade de sua linha sem observações. Se todas as linhas desse produto têm observações, cria outra linha para permitir preparos diferentes.
- De 1 a 50 linhas, com quantidades inteiras de 1 a 99. O limite de 99 também se aplica à soma das linhas do mesmo produto. Até 250 caracteres de observação por linha e 500 nas observações gerais.
- A remoção do último item e a limpeza completa apagam também as observações gerais. Limpar todos os itens exige confirmação na própria tela.
- O frontend guarda IDs, nomes para apresentação, quantidades e observações. Não guarda preços na seleção nem calcula valores comerciais.
- A revisão busca nomes e preços atuais e exige produto ativo, disponível e em categoria ativa, ficha não vazia, ingredientes ativos e saldo para toda a cesta. Agrupa linhas do mesmo produto antes de arredondar o consumo e soma ingredientes compartilhados, seguindo a regra de confirmação. Não reserva nem baixa estoque. Um produto inválido ou quantidade sem capacidade impede a revisão inteira.
- Cada edição, remoção, nova revisão ou saída da página descarta os valores anteriores. A seleção permanece após falhas; uma consulta tem timeout de 15 segundos. Durante a consulta, edição e revisão ficam bloqueadas; navegar para fora cancela a assinatura e ignora a resposta antiga.
- A resposta apresenta **subtotal dos produtos**, sem taxa de entrega, desconto ou total final fictício. Observações são texto, não alteram preços e não substituem a futura escolha de adicionais.
- A revisão não reserva estoque/preço, não emite token de compra e não cria cliente, endereço, pagamento, pedido ou movimento de estoque. A futura confirmação deverá revalidar todos os dados.
- O interceptor omite o JWT da equipe neste endpoint público. Falhas públicas não encerram a sessão administrativa; as APIs da equipe continuam protegidas.

## Contrato

`POST /api/public-cart/quote`, anônimo, somente leitura, `Cache-Control: no-store`. Corpo limitado a 65.536 bytes. Entrada reutiliza as validações de itens do carrinho administrativo e rejeita campos desconhecidos, incluindo preços, taxa e identificação de cliente.

```json
{
  "items": [
    { "productId": "GUID-do-produto", "quantity": 2, "notes": "Sem cebola" }
  ],
  "notes": "Embalar separado"
}
```

Resposta 200:

```text
items: [{ productId, name, quantity, unitPrice, lineTotal, notes }]
notes, subtotal, calculatedAt
```

O serviço projeta apenas ID, nome e preço de Products, filtrando pelo estado de Categories. Usa AsNoTracking e RepeatableRead, calcula em decimal e preserva a ordem das linhas. Observações são normalizadas em Unicode FormC e aparadas; espaços vazios tornam-se null.

- 400 / ValidationProblemDetails: entrada inválida, limites excedidos ou campos desconhecidos.
- 409 / CartProductUnavailable: produto inexistente, inativo, pausado ou em categoria inativa; não revela dados do produto oculto.
- Falhas internas seguem o tratamento ProblemDetails existente.

Sem entidades, migrations, dependências ou configurações novas. `POST /api/cart/quote` e o fluxo de pedidos da equipe mantêm seus contratos e permissões. Um teste verifica que as revisões pública e administrativa calculam os mesmos preços/subtotal para os mesmos itens.

## Como validar

1. Iniciar API e frontend conforme o README e abrir `http://localhost:8101/pedido` em janela anônima.
2. Adicionar um produto disponível, abrir **Ver carrinho**, alterar quantidade e observações e clicar em **Revisar carrinho**. Conferir preço unitário, valor por linha e subtotal com o cadastro.
3. Alterar um item: os valores revisados devem desaparecer até nova consulta. Voltar ao cardápio, adicionar outro produto e retornar: a seleção permanece e exige nova revisão.
4. Para observações diferentes do mesmo produto, preencher as observações da primeira linha antes de adicioná-lo novamente. Quantidade vazia, fracionária, zero, acima de 99 ou soma acima de 99 deve impedir a revisão.
5. Remover itens e testar **Limpar carrinho**, primeiro mantendo a seleção e depois confirmando. Atualizar a página deve mostrar carrinho vazio.
6. Em ambiente de teste, pausar/inativar um produto selecionado ou mudar seu preço no cadastro: nova revisão deve rejeitar a indisponibilidade ou refletir o preço atual. Não alterar o catálogo operacional apenas para produzir evidência.
7. Conferir desktop/celular, falha de conexão e nova tentativa. Não deve aparecer confirmação de pedido ou cobrança; a revisão não altera o banco.

```powershell
# Raiz: backend com PostgreSQL temporário isolado
powershell -NoProfile -File scripts/Test-Authentication.ps1

# frontend/made-in-minas
npm.cmd run format:check
npm.cmd run lint
npm.cmd run build
npm.cmd run test:e2e -- public-cart.spec.ts menu.spec.ts staff.spec.ts cart.spec.ts
```

Testes backend usam PostgreSQL real isolado; testes de navegador simulam respostas HTTP. Conferência adicional com a API local e resultados estão em [validation.md](validation.md). Aceite manual do responsável permanece separado.

## Arquivos

- Backend novos: `Controllers/PublicCartController.cs`, `Services/PublicCartService.cs`, `DTOs/PublicCart/PublicCartContracts.cs` e `MadeInMinas.Api.Tests/PublicCartTests.cs`.
- Backend alterado: `Program.cs`, registro do serviço.
- Frontend novos: `core/services/public-cart-api.service.ts`, `core/services/public-cart-state.service.ts`, `features/public-cart/public-cart.page.ts`, `.html`, `.scss` e `e2e/public-cart.spec.ts`.
- Frontend alterado: rotas, interceptor, página do cardápio e testes do cardápio.
- Documentação: README, roteiro, arquitetura, API, banco, regras, decisões, validação, cardápio e este documento.

A continuidade foi definida na [Fase 8C — checkout público para retirada](public-checkout.md). Entrega pública aguarda regra de cobertura/taxa; acompanhamento terá controle de acesso próprio.
