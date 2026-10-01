# Etapas

Cada fase exige implementação, testes e validação antes da próxima.

| Fase | Escopo | Situação |
| --- | --- | --- |
| 1 | Estrutura, API, frontend, banco/configuração, Git e documentação | Implementada; ver docs/validation.md |
| 2 | Autenticação, usuários e permissões | 2A, 2B e 2C implementadas; acesso validado pelo usuário |
| 3 | Categorias, produtos, ingredientes e fichas técnicas | 3A–3D implementadas e validadas; fichas técnicas integradas pelo PR #1 |
| 4 | Clientes, pedidos, carrinho e pagamentos | 4A validada e integrada pelo PR #2; 4B carrinho/revisão implementados e aprovados pelo usuário em 01/10/2026 no PR #3; gravação de pedidos e pagamentos em incrementos posteriores |
| 5 | Cozinha/KDS, expedição e impressão | Planejada |
| 6 | Estoque e CMV | Planejada |
| 7 | Dashboard e relatórios | Planejada |
| 8 | Chat próprio | Planejada |
| 9 | Integração com IA | Planejada |
| 10 | Integração com WhatsApp | Planejada |
| 11 | Melhorias e automações | Planejada |

Combos e adicionais terão incrementos próprios associados ao catálogo e à montagem do pedido.

Em 30/09/2026 foram concluídas a [manutenção técnica](maintenance.md), as verificações do GitHub e o aceite manual das fichas técnicas. O PR #1 foi integrado na `master` por squash. Clientes e endereços também foram validados e integrados pelo PR #2, commit `511c228`. Em 01/10/2026, a branch `feat/staff-cart` iniciou a [Fase 4B](cart.md): carrinho temporário e revisão, sem persistência de pedidos. Regras, execução e aceite da etapa anterior estão em [clientes e endereços](customers.md).

## Aceite da Fase 1

- Solução e frontend compilam.
- API e página inicial executam localmente.
- Frontend consulta o endpoint de status e trata indisponibilidade.
- Health checks distinguem API ativa de banco acessível.
- Readiness validado com PostgreSQL conectado e indisponível.
- CORS aceita somente origem autorizada.
- OpenAPI disponível no desenvolvimento.
- Repositório ignora dependências, builds e segredos.
- Configuração do PostgreSQL permanente documentada; credenciais fornecidas localmente pelo responsável.

Os incrementos da Fase 2 estão descritos em [authentication.md](authentication.md), [users.md](users.md) e [staff-frontend.md](staff-frontend.md). A [Fase 3A](categories.md) implementa categorias; a [Fase 3B](products.md), produtos; a [Fase 3C](ingredients.md), ingredientes; e a [Fase 3D](recipes.md), fichas técnicas. Ingredientes e fichas técnicas foram validados pelo usuário em 30/09/2026. O usuário também validou clientes e endereços no PR #2. Carrinho, pedidos e pagamentos terão seus próprios incrementos e aceites.
