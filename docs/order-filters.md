# Pedidos por origem e pagamento

Bloco E dos ajustes operacionais, iniciado em 06/10/2026 na branch `feat/operational-usability`. Usa a listagem existente da equipe, com as mesmas permissões de administrador e atendente. Não cria tabelas, migrations, dependências ou movimentações financeiras.

## Fluxo da equipe

Em **Operação → Pedidos**, combinar busca por número/cliente/telefone, status, origem **Site/Equipe** e situação do pagamento. **Buscar pedidos** aplica os campos e volta à página 1. Alterações ainda não aplicadas são sinalizadas e não mudam a busca durante atualização automática, paginação ou repetição de uma consulta com falha.

Os atalhos **Aguardando confirmação**, **A receber**, **Prontos** e **Todos os pedidos** limpam os demais filtros e abrem a fila correspondente. O contador de novos e o aviso opcional continuam consultando todas as origens, independentemente do filtro da lista. Ocultar a aba pausa o polling; não há reserva de dados nem confirmação automática.

A origem e a situação do pagamento aparecem na tabela do computador e nos cartões do celular. Confirmar produção continua independente do recebimento financeiro. A lista reflete somente o que foi registrado no sistema: não consulta banco, Pix ou operadora de cartão.

## Situação financeira da lista

| Valor | Texto | Regra |
| --- | --- | --- |
| `Received` | Recebido | Há pagamento atualmente recebido |
| `Pending` | Aguardando recebimento | Há uma intenção de pagamento ativa, ainda não recebida |
| `Refunded` | Devolvido | Não há pagamento ativo e existe devolução registrada |
| `NotDue` | Sem cobrança | Sem pagamento ativo/devolvido, e pedido cancelado ou finalizado |
| `NotRegistered` | Não definido | Demais pedidos, incluindo somente intenções canceladas |

Uma nova tentativa ativa prevalece sobre devoluções anteriores. Uma intenção cancelada não é recebimento nem devolução; ela não oculta uma devolução anterior. Pedidos cancelados com devolução permanecem identificados como **Devolvido**, preservando essa informação na busca.

**A receber** (`Unpaid`) é um filtro agregado: pedido diferente de Cancelado/Finalizado, sem pagamento atualmente recebido. Inclui pagamento não definido, intenção pendente e devolução de pedido ainda em andamento. Não é um novo estado de `Payments`, um cálculo de fechamento financeiro ou autorização para cobrar novamente sem conferir a operação. Pedidos encerrados ficam fora dessa fila, inclusive os finalizados com devolução posterior.

## API

`GET /api/orders` mantém `orders.manage`, `no-store`, ordenação por criação/número e paginação. Novos parâmetros opcionais:

- `origin`: `Manual` ou `DirectLink`.
- `paymentStatus`: `Unpaid`, `NotRegistered`, `Pending`, `Received`, `Refunded` ou `NotDue`.

Omitir filtros preserva a consulta anterior. Filtros vazios ou somente com espaços são tratados como ausentes, seguindo a vinculação de parâmetros opcionais do ASP.NET. Valores desconhecidos retornam 400; os filtros combinam com `search`, `status`, `customerId`, `page` e `pageSize`. A resposta mantém os campos existentes e acrescenta `origin` e `paymentStatus` em cada item. `Unpaid` nunca é retornado como situação de um item. Não são expostos telefone, dados de cartão, identificadores de pagamento ou histórico financeiro na listagem.

`OrderService.ListAsync` deriva a situação por consultas de existência traduzidas para SQL, usando os índices existentes de pagamentos por pedido. Filtra antes de contar/paginar e não duplica pedidos com várias tentativas. Total e página são lidos na mesma transação `RepeatableRead`. Não carrega todas as tentativas nem faz uma chamada HTTP adicional por pedido.

## Validação

1. Como administrador ou atendente, abrir Pedidos e combinar Origem/Situação do pagamento com busca/status; confirmar total e paginação.
2. Digitar outro filtro sem clicar em Buscar: a lista, paginação e atualização automática devem manter a busca aplicada e mostrar o aviso de edição.
3. Em ambiente de teste, registrar intenção, recebimento, devolução e nova tentativa; conferir os estados da lista sem alterar o status de produção.
4. Abrir A receber: pedidos sem forma definida também aparecem; recebidos, cancelados e finalizados ficam fora. Um recebimento registrado em outra sessão deve retirar o pedido dessa fila na próxima atualização.
5. Enquanto uma origem estiver filtrada, receber um pedido Novo da outra origem em teste: o contador geral continua informando sua chegada.
6. Repetir em computador e celular, incluindo consulta vazia e falha de conexão. Não registrar pagamentos fictícios na operação.

Resultados em [validation.md](validation.md). Arquivos: `DTOs/Orders/OrderContracts.cs`, `Services/OrderService.cs`, `OrderListTests.cs`, `PaymentTests.cs`, `core/services/order-api.service.ts`, `features/orders/orders.page.*` e `e2e/orders.spec.ts`.
