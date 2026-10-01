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
