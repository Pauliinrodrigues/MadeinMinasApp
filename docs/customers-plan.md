# Próximo incremento: clientes e endereços — Fase 4A

Proposta preparada durante a manutenção técnica e aceita após a validação das fichas técnicas em 30/09/2026. A implementação na branch `feat/customers-addresses` está descrita em [customers.md](customers.md), incluindo o GET de endereço individual acrescentado para consulta e Location do cadastro. Este documento preserva o escopo planejado.

## Entrega

Administrador e atendente poderão cadastrar, buscar e editar clientes e seus endereços na área da equipe. O cadastro inicial exige somente nome e telefone. Endereços serão cadastrados quando necessários para entrega.

Não incluir pedidos, carrinho, cobrança ou acesso público neste incremento. Histórico de pedidos, quantidade de compras e último pedido serão derivados quando o módulo de pedidos existir.

## Modelagem proposta

- `Customers`: UUID, Name, NormalizedName para busca, Phone normalizado e único, IsActive, CreatedAt e UpdatedAt em UTC.
- `Addresses`: UUID, CustomerId obrigatório, Street, Number textual (aceitando sem número), Complement opcional, Neighborhood, City, State, PostalCode opcional, Reference opcional, IsActive, CreatedAt e UpdatedAt.
- Um cliente pode ter vários endereços. Não excluir fisicamente registros por enquanto; usar inativação.
- A futura criação do pedido copiará os dados do endereço escolhido, preservando o histórico quando o cadastro mudar.

Telefone: nesta primeira entrega, números brasileiros com DDD, aceitando formatação comum e o prefixo +55. Normalizar na API antes de buscar ou verificar duplicidade. Rejeitar letras e números incompletos; não inserir dígitos ausentes automaticamente. Conhecer o telefone não autentica o cliente e não permite consultar seus dados em uma rota pública.

## Contratos propostos

Permissão `customers.manage`, atribuída a Administrator e Attendant. Kitchen e Dispatch não terão acesso ao cadastro completo.

| Método | Rota | Operação |
| --- | --- | --- |
| GET / POST | /api/customers | Listar com paginação e filtros / cadastrar |
| GET / PUT | /api/customers/{id} | Consultar / editar |
| PUT | /api/customers/{id}/status | Ativar ou inativar |
| GET / POST | /api/customers/{customerId}/addresses | Listar endereços / cadastrar |
| PUT | /api/customers/{customerId}/addresses/{addressId} | Editar endereço do cliente indicado |
| PUT | /api/customers/{customerId}/addresses/{addressId}/status | Ativar ou inativar endereço |

Todos os endpoints exigirão JWT, autorização no backend e respostas sem cache. A API verificará o vínculo entre endereço e cliente, inclusive nas alterações. Conflitos de telefone retornarão 409 com código de erro próprio. Não registrar telefone ou endereço completo nos logs.

## Validação prevista

1. Testes HTTP com PostgreSQL isolado: permissões, normalização, telefone duplicado inclusive em concorrência, validações, paginação, vínculo de endereço e sessão revogada.
2. Testes de navegador em desktop/celular: cadastro, edição, busca, inativação, endereços e mensagens de falha.
3. Verificações de formatação, lint e build no mesmo fluxo de qualidade da manutenção.
4. Aceite manual de cadastro e reabertura dos dados com API e banco reais antes de iniciar carrinho e pedidos.
