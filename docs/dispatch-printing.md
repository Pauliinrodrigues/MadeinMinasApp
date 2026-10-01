# Expedição e impressão — Fases 5B/5C

Branch única `feat/dispatch-printing`, a partir da cozinha/KDS aceita e integrada no PR #6 (`06d73a5`). Os módulos compartilham a conferência e saída dos pedidos, mas são testados separadamente. Aceite do incremento entregue (expedição e impressão pelo navegador) registrado em 01/10/2026, com autorização do usuário para commit, publicação e integração por squash após checks backend/frontend aprovados. O aceite não abrange impressão automática nem validação física, ainda dependentes do equipamento.

## Expedição

- Administrador e expedição usam `dispatch.work`. Atendente acompanha pelo módulo Pedidos e registra pagamentos; cozinha continua restrita à produção. Novas permissões de impressão não concedem alteração de pedidos ou pagamentos.
- Entrega: `Ready → AwaitingDelivery → OutForDelivery → Delivered → Finalized`.
- Retirada: `Ready → Delivered → Finalized`; Entregue significa que o cliente retirou. Não passar retirada por estados de transporte.
- Finalizar exige pagamento integral no estado Received, verificado no backend. Confirmar entrega/retirada não registra recebimento; pedidos entregues sem recebimento continuam visíveis na fila Entregue. A expedição solicita ao atendimento o registro antes de finalizar.
- Só avançar após conferência e realização da ação. Sem saltos, retrocessos ou edição dos dados da compra. Cada etapa registra funcionário, horário e versão no histórico existente.
- Administrador pode cancelar antes da entrega, inclusive aguardando transporte ou em rota, com motivo e pagamentos resolvidos. Após Entregue/Finalizado não há cancelamento neste incremento; devolução financeira realizada continua registrável pelo administrador. Pedido finalizado não aceita novo pagamento.
- Gravações bloqueiam funcionário e pedido na mesma ordem da cozinha/pagamentos/cancelamento. Versões detectam concorrência; reenvio idêntico da última etapa pelo mesmo operador retorna o resultado existente. Não há reenvio automático de comandos pelo frontend após erro.
- Fila por etapa, paginada, ordenada pelo ingresso na etapa e número, sem cortar pedidos de dias anteriores. Consulta consistente de pedidos/pagamentos. Atualiza a cada 10 segundos; confirmação aberta pausa a consulta e expira em 30 segundos. Erro ou dados antigos bloqueiam ações até nova consulta. Sair encerra consultas.
- Os itens, cliente e endereço são as cópias históricas registradas no pedido. Situação de pagamento vem do backend. Cadastro de entregadores, atribuição de rotas, geolocalização e aplicativo de entregador não integram este incremento; saída/entrega são registradas pela equipe da expedição.

## Impressão pelo navegador

- Produção: `printing.kitchen`, administrador/atendente/cozinha. Número, modalidade, status, produtos, quantidades, observações e datas. O endpoint não retorna cliente, endereço, preços nem pagamento.
- Expedição: `printing.dispatch`, administrador/atendente/expedição. Inclui cliente, telefone, endereço completo, produtos, observações, subtotal, taxa, total e pagamento ativo. Dinheiro recebido e troco aparecem quando registrados. Ausência de pagamento ativo não é apresentada como recebido.
- Botões na cozinha, expedição e detalhe do pedido abrem prévia autenticada na mesma aba. Trocar pedido/via consulta novamente; falha limpa a prévia e 401 encerra a sessão. Nenhum token fica em URL, armazenamento ou documento.
- Impressão exige botão explícito, prévia carregada e recente. Formatos de conteúdo 58 mm, 80 mm e A4, com margem interna e quebra de texto; selecionar também o papel correto no driver/diálogo. Controles ficam fora da via impressa e o layout remove restrições de altura do Ionic para permitir várias páginas.
- Comandas de pedidos Novos, Cancelados e Finalizados exibem avisos do estado. Toda via informa data da consulta, versão e que não é documento fiscal. Observações livres devem ser reservadas às instruções da operação; conteúdo é renderizado como texto.
- Abrir/fechar o diálogo não confirma impressão física, nem muda pedido, pagamento ou histórico. Reimprimir requer nova ação; a equipe confere a saída para evitar duplicidade. Não existem jobs persistentes ou impressão automática neste modo.
- **Impressão automática sem diálogo permanece pendente de equipamento e validação física.** A consulta local em 01/10/2026 encontrou somente Microsoft Print to PDF e OneNote, sem impressora física instalada. Confirmar modelo, conexão USB/rede, sistema da estação e via de cada impressora antes de projetar agente local, credenciais, fila, confirmação e repetição. Não habilitar silenciosamente modo quiosque nem expor impressora na internet.

Referências do comportamento do navegador: [window.print](https://developer.mozilla.org/en-US/docs/Web/API/Window/print) e [CSS para impressão](https://developer.mozilla.org/en-US/docs/Web/CSS/Guides/Media_queries/Printing).

## API e banco

- `GET /api/dispatch/orders?status=Ready&page=1&pageSize=20`: Ready, AwaitingDelivery, OutForDelivery ou Delivered; página 1–1.000.000 e tamanho 1–50. Resposta traz serverTime, items, page, pageSize e totalCount. Página além do fim recua para a última.
- `PUT /api/dispatch/orders/{id}/status`: `{ status, expectedVersion }`, status de destino conforme modalidade. Campos extras são rejeitados; conflitos retornam 409, sessão inválida 401 e falta de permissão 403.
- `GET /api/print/orders/{id}/kitchen` e `GET /api/print/orders/{id}/dispatch`: `{ generatedAt, order }`, contratos distintos e somente leitura. Todas as respostas dessas rotas são no-store.
- Migration `20261001201327_AddDispatchStatuses`: altera duas constraints existentes de estado e transição; nenhuma nova tabela/dependência. Fazer backup e conferir exclusivamente `made_in_minas` antes de aplicar. Reverter exige ausência dos novos estados no pedido e no histórico; não remover eventos para forçar rollback. Não há migration no `parsmartmanager`.

## Roteiro de aceite

1. Faça novo login em http://localhost:8101 como administrador; confirme um pedido de entrega e outro de retirada e conclua o preparo no KDS.
2. Em Expedição, confira itens, observações, cliente, endereço e pagamento. Libere a entrega, registre saída e depois a entrega; na retirada, registre a retirada diretamente de Pronto.
3. Confira que finalizar fica bloqueado sem recebimento. Registre o recebimento em Pagamentos, atualize a fila Entregue e finalize. Pedido finalizado sai da fila, mas continua em Pedidos com histórico.
4. Com perfil Expedição, repita a conferência e confirme que não há acesso à cozinha, cadastros ou alteração de pagamentos. Com Cozinha, veja apenas produção e sua comanda.
5. Abra as duas vias pelo detalhe do pedido e pelos painéis. Confira ausência de dados comerciais na via da cozinha, endereço e situação de pagamento na de expedição. Teste observações longas, mais de uma página, 58/80 mm e A4 conforme equipamento disponível.
6. Abra o diálogo, cancele e reabra. O sistema não deve registrar impressão concluída. Atualize após 30 segundos para reimprimir com dados recentes. Confira a saída física e margem/corte no equipamento real antes de usar em produção.
7. Teste duas sessões no mesmo pedido e uma falha de conexão: deve exigir atualização após conflito/erro, sem repetir a ação automaticamente.

Essas ações alteram pedidos reais do ambiente acessado; usar pedidos de validação. Aceite da impressão física e decisão sobre automação devem ser registrados separadamente dos testes de navegador.

## Arquivos

Novos: DispatchService, DispatchController, PrintController, DTOs/Dispatch, migration AddDispatchStatuses, DispatchTests; dispatch-api.service, páginas dispatch/printing e e2e/dispatch-printing.spec.ts. Ajustados: policies, serviços de pedido/pagamento/cozinha, filtros/estados, constraints/snapshot, testes existentes de autenticação/pedidos/cozinha, rotas, guards, menu, links de comanda e CSS de impressão. Resultados em [validation.md](validation.md).
