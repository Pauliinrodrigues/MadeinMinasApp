# Regras de negócio acordadas

Estas regras orientam os módulos futuros; ainda não estão implementadas.
As regras já implementadas por incremento constam em authentication.md, users.md, categories.md, products.md, ingredients.md, recipes.md, [customers.md](customers.md), [cart.md](cart.md), [orders.md](orders.md) e [payments.md](payments.md). As regras de pedidos manuais da Fase 4C foram validadas pelo usuário em 01/10/2026. Pagamentos manuais integrais (4D) foram aceitos pelo usuário em 01/10/2026.

- WhatsApp é canal de entrada por link; o atendimento próprio centraliza o pedido.
- Backend/banco definem produtos, preços, promoções, estoque e totais. A IA não decide regras críticas.
- Preferir interface estruturada para escolher itens, adicionais, quantidades, endereço e pagamento.
- Pedido é a entidade central e preserva valores e composição da compra.
- Origens previstas: chat próprio, WhatsApp, atendimento manual e link direto.
- Status previstos: Novo, Confirmado, Em preparação, Pronto, Aguardando entrega, Saiu para entrega, Entregue, Finalizado e Cancelado.
- Toda transição registra data/hora, responsável e motivo quando necessário. Na Fase 4C: Novo → Confirmado/Cancelado; Confirmado → Cancelado somente por administrador, sempre com motivo. Estados de produção serão definidos na fase 5.
- Pagamento possui estado próprio. Confirmar pagamento e confirmar produção são operações distintas.
- Na Fase 4D, valor integral em uma única forma por tentativa, sem cobrança online. Equipe confirma recebimento conferido; somente administrador registra devolução integral já realizada. Não se cancela pedido com pagamento pendente/recebido: primeiro resolver o pagamento, preservando histórico.
- Cozinha recebe somente informações necessárias à produção; endereço e cobrança ficam com os perfis autorizados.
- Na Fase 5A, só Confirmed entra na fila; kitchen.work avança Confirmed → InPreparation → Ready. Pagamento não libera produção automaticamente. Horários e autor ficam no histórico; administrador pode cancelar durante/depois do preparo com motivo e pagamento resolvido. Observações livres devem conter instruções de produção, sem dados de contato/cobrança. Aceite do KDS concluído em 01/10/2026; [regras e validação](kitchen.md).
- Administrador, atendente, cozinha e expedição têm permissões validadas pelo backend.
- Cliente pode iniciar atendimento sem cadastro complexo. Acesso a dados pessoais e histórico exige verificação adequada.
- Ficha técnica fundamenta custo, consumo de estoque e CMV. Cancelamentos podem exigir estorno conforme o estágio de produção.
- Estoque, financeiro e integrações devem suportar repetição de solicitações sem duplicar efeitos.

- Fases 5B/5C: entrega passa por aguardando entrega, saída, entregue e finalizado; retirada vai de pronto a entregue e finalizado. Finalizar exige Received integral; não registra recebimento. Só administrador cancela antes da entrega, com motivo e pagamentos resolvidos. Após entrega não cancelar; após finalização não criar novo pagamento, mantendo devolução administrativa auditada. Comandas são consultas atuais com via de produção restrita; diálogo do navegador não comprova saída física. Regras e aceite do incremento entregue, registrado em 01/10/2026: [dispatch-printing.md](dispatch-printing.md).

- Fase 6A: administrador movimenta estoque manual por ingrediente. Entrada/saída alteram o saldo pela quantidade; contagem define o saldo físico conferido. Motivo, autor, instante e saldos ficam no histórico. Saldo negativo, precisão excedente e alterações com versão desatualizada são rejeitados. Não há efeito sobre preço, custo ou pedidos. Inventário inicial deve ser conferido; mínimo é um aviso no detalhe. Regras: [stock.md](stock.md).

- Fase 6B: confirmar baixa ingredientes pela composição atual, copiada no pedido. Quantidades somam linhas do mesmo produto e arredondam para cima a três casas por produto/ingrediente antes de agrupar ingredientes compartilhados. Sem ficha, ingrediente ativo ou saldo, nada é confirmado. Cancelar antes do preparo devolve a baixa original; após preparo mantém consumo. Pedidos antigos já processados não são movimentados retroativamente. Regra adotada para este incremento, com aceite manual pendente: [order-stock.md](order-stock.md).


- Fase 6C: CMV teórico usa custos e preço atuais; custo por produto deriva da ficha/rendimento, sem arredondamento físico da baixa. Custo zero é pendência e impede apresentar CMV/margem completos. Margem pode ser negativa e exclui despesas fora da ficha. A consulta é administrativa e não altera dados operacionais; não representa CMV realizado das vendas. Ver [cmv.md](cmv.md).

- Fase 7A: dashboard administrativo usa dia civil de Brasília. Confirmações válidas excluem cancelados e incluem entrega; recebimentos e estornos pertencem ao dia do evento, independentemente da data/status do pedido. Filas incluem dias anteriores. Produção mede preparo até Pronto com horários válidos, inclusive quando há cancelamento posterior. Ranking soma itens por produto nas confirmações de hoje, usando cópias históricas; médias sem amostra são nulas. Não há cálculo de lucro ou CMV realizado no painel. Ver [daily-dashboard.md](daily-dashboard.md).
