# Etapas

Cada fase exige implementação, testes e validação antes da próxima.

| Fase | Escopo | Situação |
| --- | --- | --- |
| 1 | Estrutura, API, frontend, banco/configuração, Git e documentação | Implementada; ver docs/validation.md |
| 2 | Autenticação, usuários e permissões | 2A, 2B e 2C implementadas; acesso validado pelo usuário |
| 3 | Categorias, produtos, ingredientes e fichas técnicas | 3A–3D implementadas e validadas; fichas técnicas integradas pelo PR #1 |
| 4 | Clientes, pedidos, carrinho e pagamentos | 4A–4D validadas e integradas pelos PRs #2/#3/#4/#5 |
| 5 | Cozinha/KDS, expedição e impressão | 5A integrada pelo PR #6; 5B/5C aceitas e integradas pelo PR #7, com checks aprovados; impressão automática/validação física dependem do equipamento |
| 6 | Estoque e CMV | Incrementos 6A–6C implementados, testados e integrados pelos PRs #8/#9/#10; migration de consumo aplicada localmente após backup. CMV é teórico por produto |
| 7 | Dashboard e relatórios | Escopo inicial 7A/7B implementado, testado e integrado pelos PRs #11/#12, com checks aprovados; master 5adcd03 |
| 8 | Chat próprio | 8A–8E integradas por squash na master pelos PRs #13/#14/#15/#16/#17. Testes locais aprovados; integração com checks remotos pendentes/cancelados autorizada pelo responsável durante o incidente do Actions, conforme validation.md. 8E entrega atendimento humano, fila, mensagens e encerramento, com AddHumanChat. Entrega pública e integração visual mais ampla terão incrementos próprios |
| 9 | Integração com IA | Planejada |
| 10 | Integração com WhatsApp | Planejada |
| 11 | Melhorias e automações | Planejada |

Combos e adicionais terão incrementos próprios associados ao catálogo e à montagem do pedido.

Incremento mais recente: [8E — atendimento humano](human-chat.md), integrado pelo [PR #17](https://github.com/Pauliinrodrigues/MadeinMinasApp/pull/17). Cliente solicita ajuda em `/pedido/atendimento`; administrador/atendente assumem, respondem e encerram em `/equipe/atendimentos`. Acesso público por credencial própria e tentativas idempotentes. A taxa/cobertura de entrega ainda precisa ser definida; integração visual entre conversa/cardápio e coordenação futura com IA permanecem fora deste incremento. A sequência 8A → 8B → 8C → 8D → 8E preserva uma entrada por incremento na master. Validação técnica e exceção autorizada para os checks remotos em [validation.md](validation.md); a autorização não comprova execução manual integral dos roteiros.

A Fase 5 foi integrada pelos PRs #6 (`06d73a5`) e #7 (`7f32729`), com checks backend/frontend aprovados. A Fase 6A foi integrada pelo PR #8. A publicação autorizada em 02/10/2026 mantém a sequência [6B — consumo de estoque](order-stock.md) → [6C — CMV teórico](cmv.md) → [7A — dashboard diário](daily-dashboard.md) → [7B — relatórios por período](period-reports.md), nos PRs [#9](https://github.com/Pauliinrodrigues/MadeinMinasApp/pull/9), [#10](https://github.com/Pauliinrodrigues/MadeinMinasApp/pull/10), [#11](https://github.com/Pauliinrodrigues/MadeinMinasApp/pull/11) e [#12](https://github.com/Pauliinrodrigues/MadeinMinasApp/pull/12). A Fase 8 foi iniciada pelo cardápio público (8A). Automação da impressora, validação física, CMV realizado e fechamento financeiro continuam fora dessas entregas.

A [Fase 4C](orders.md) registra pedidos manuais com numeração, cópias da compra, histórico, confirmação e cancelamento. Revisão exige taxa explícita para entrega. O aceite manual foi concluído em 01/10/2026 e o PR #4 foi integrado à master (`b98f41d`). A [Fase 4D](payments.md) acrescenta pagamentos manuais integrais, separados do status do pedido, com aceite concluído em 01/10/2026. A Fase 5A sucede essa integração; expedição e impressão dependem de incrementos e aceites próprios.

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
