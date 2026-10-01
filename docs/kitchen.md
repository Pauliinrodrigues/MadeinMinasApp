# Cozinha/KDS — Fase 5A

Incremento na branch `feat/kitchen-kds`, após a Fase 4D aceita e integrada pelo PR #5. Escopo: fila de produção e transições Confirmed → InPreparation → Ready. Somente pedidos confirmados pelo atendimento entram na cozinha. Pagamento continua independente; confirmar o pedido representa a autorização operacional para produzir.

## Regras e arquitetura

- Administrador e cozinha usam a política existente kitchen.work. Atendente acompanha o status por Pedidos; expedição terá seu próprio incremento. Cozinha não acessa clientes, endereços, preços ou pagamentos pelas rotas do KDS.
- Reaproveitar Orders, OrderItems e OrderStatusHistory. Não criar tabelas de produção nesta etapa. Horários de confirmação, início e conclusão vêm do histórico; nomes e observações dos itens vêm da compra preservada.
- Cozinha avança uma etapa por vez, sem retroceder, cancelar ou confirmar pedidos novos. O atendimento continua confirmando pela rota existente. Cancelar depois da confirmação, inclusive durante/depois do preparo, exige administrador, motivo e resolução do pagamento.
- Escrita revalida funcionário/perfil, bloqueia o pedido e usa versão. Repetição idêntica da última transição pelo mesmo funcionário retorna o resultado existente; ações concorrentes incompatíveis exigem atualização. Histórico e estado são gravados juntos.
- GET /api/kitchen/orders retorna três colunas paginadas em snapshot consistente, com horário do servidor. confirmedPage, preparingPage e readyPage começam em 1, até 1.000.000; pageSize de 1 a 50 (padrão 20). Ordenação por confirmação e número; página além do fim recua para a última. Não há corte por dia que esconda pedidos atrasados.
- PUT /api/kitchen/orders/{id}/status recebe status (InPreparation ou Ready) e expectedVersion. Não aceita campos comerciais nem observações extras. Respostas reutilizam os erros Order* e no-store.
- Cards exibem número, retirada/entrega, produtos, quantidades, observações, horário e tempos. Observações são texto livre já presente no pedido: a equipe deve reservar esses campos para instruções de produção, sem dados de cobrança/contato.
- Atualização automática a cada 10 segundos e manual. Falha ou dados com mais de 30 segundos bloqueiam novas decisões; versão do backend continua sendo a proteção definitiva. Não há sobreposição de consultas, nem escrita enquanto se consulta. Confirmação visual antes de avançar; após falha de escrita, consultar novamente antes de decidir. Sair da página encerra os temporizadores e requisições.
- Prontos permanecem na coluna até a expedição avançar o pedido; a cozinha não registra saída, entrega ou finalização. A Fase 5C adiciona uma via de produção pelo navegador. Notificações sonoras, estoque e indicadores continuam fora deste módulo.

## Testar manualmente

1. Acesse http://localhost:8101/entrar e faça novo login como administrador. Em Pedidos, confirme um pedido adequado ao teste. Pedido Novo ainda não aparece na cozinha.
2. Abra **Cozinha** no menu. Confira número, retirada/entrega, quantidades, observações e tempo na coluna Confirmado.
3. Use **Iniciar preparo → Confirmar etapa**. O pedido passa para Em preparação. Depois use **Marcar pronto → Confirmar etapa**; só confirme quando todos os itens estiverem prontos.
4. Volte ao detalhe em Pedidos e confira o histórico com funcionário e horários. O status do pagamento permanece independente.
5. Faça login com um usuário ativo de perfil Cozinha em outra sessão. Deve ver o painel e executar apenas essas etapas, sem acesso aos cadastros, clientes ou pagamentos. Atendente não acessa o KDS, mas acompanha o status em Pedidos.
6. Para testar concorrência, deixe o mesmo pedido em duas sessões: a segunda ação com versão antiga deve exigir atualização. Para indisponibilidade, interrompa a conexão no navegador; cards antigos não autorizam novas ações e a consulta deve recuperar após reconexão.
7. Se testar cancelamento durante/depois do preparo, use administrador, motivo e resolva os pagamentos antes. O pedido sai da fila na próxima consulta, em até 10 segundos enquanto a página estiver ativa e sem confirmação aberta.

Os passos alteram pedidos de verdade no ambiente acessado. Use pedidos destinados à validação. Não há botão de expedição/finalização na cozinha; a equipe de expedição usa o painel próprio da Fase 5B. A sessão segue a validade já existente e pede novo login ao expirar.

## Arquivos e validação

- Novos: KitchenController, KitchenService, DTOs/Kitchen/KitchenContracts, migration AddKitchenStatuses, KitchenTests, kitchen-api.service, página kitchen (TS/HTML/SCSS) e e2e/kitchen.spec.ts.
- Ajustados: registro de serviço, constraints/snapshot, filtro e cancelamento de pedidos, tipos e interface de pedidos, testes e2e/orders.spec.ts, permissão/guard, rotas e menu da equipe.
- Documentação: README, database/README e documentos de arquitetura, banco, API, regras, decisões, pedidos, roadmap e validação.
- Testes automatizados: PostgreSQL temporário isolado; navegador com API simulada, sem produzir nem receber pedidos reais. Resultados e limitações ficam em [validation.md](validation.md).

A migration altera somente duas constraints, preservando tabelas e dados. Fazer backup antes de aplicar exclusivamente em made_in_minas. O rollback para os estados antigos só é possível se nenhum pedido ou evento usa InPreparation/Ready; não apagar nem reescrever histórico para forçar retorno. Aceite manual concluído pelo usuário em 01/10/2026, com commit, publicação e integração à master autorizados. Integrar por squash somente após os checks backend/frontend aprovados no commit final.

A Fase 5A foi integrada pelo PR #6 (`06d73a5`) com checks aprovados. Na Fase 5B, expedição movimenta pedidos prontos para saída/retirada; eles deixam a coluna Pronto na próxima consulta. A via de produção passa a ser acessível pelo card, sob printing.kitchen. Ver [expedição e impressão](dispatch-printing.md).
