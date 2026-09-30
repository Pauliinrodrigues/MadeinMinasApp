# Etapas

Cada fase exige implementação, testes e validação antes da próxima.

| Fase | Escopo | Situação |
| --- | --- | --- |
| 1 | Estrutura, API, frontend, banco/configuração, Git e documentação | Implementada; ver docs/validation.md |
| 2 | Autenticação, usuários e permissões | 2A, 2B e 2C implementadas; acesso validado pelo usuário |
| 3 | Categorias, produtos, ingredientes e fichas técnicas | 3A–3D implementadas; ingredientes validados pelo usuário; fichas técnicas aguardam aceite manual |
| 4 | Clientes, pedidos, carrinho e pagamentos | Planejada |
| 5 | Cozinha/KDS, expedição e impressão | Planejada |
| 6 | Estoque e CMV | Planejada |
| 7 | Dashboard e relatórios | Planejada |
| 8 | Chat próprio | Planejada |
| 9 | Integração com IA | Planejada |
| 10 | Integração com WhatsApp | Planejada |
| 11 | Melhorias e automações | Planejada |

Combos e adicionais terão incrementos próprios associados ao catálogo e à montagem do pedido.

Em 30/09/2026 foi autorizada a [manutenção técnica](maintenance.md), antes do próximo módulo: atualização compatível das ferramentas, legibilidade e verificações automáticas. A [proposta da Fase 4A](customers-plan.md) detalha clientes e endereços; ainda depende da conclusão da manutenção e do aceite manual das fichas técnicas.

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

Os incrementos da Fase 2 estão descritos em [authentication.md](authentication.md), [users.md](users.md) e [staff-frontend.md](staff-frontend.md). A [Fase 3A](categories.md) implementa categorias; a [Fase 3B](products.md), produtos; a [Fase 3C](ingredients.md), ingredientes; e a [Fase 3D](recipes.md), fichas técnicas. O usuário validou ingredientes em 30/09/2026. Após o aceite das fichas técnicas, o próximo incremento será clientes e endereços na Fase 4, antes de carrinho, pedidos e pagamentos.
