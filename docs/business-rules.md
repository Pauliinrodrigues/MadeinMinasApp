# Regras de negócio acordadas

Estas regras orientam os módulos futuros; ainda não estão implementadas.
As regras já implementadas por incremento constam em authentication.md, users.md, categories.md, products.md, ingredients.md e recipes.md.

- WhatsApp é canal de entrada por link; o atendimento próprio centraliza o pedido.
- Backend/banco definem produtos, preços, promoções, estoque e totais. A IA não decide regras críticas.
- Preferir interface estruturada para escolher itens, adicionais, quantidades, endereço e pagamento.
- Pedido é a entidade central e preserva valores e composição da compra.
- Origens previstas: chat próprio, WhatsApp, atendimento manual e link direto.
- Status previstos: Novo, Confirmado, Em preparação, Pronto, Aguardando entrega, Saiu para entrega, Entregue, Finalizado e Cancelado.
- Toda transição registra data/hora, responsável e motivo quando necessário; transições permitidas serão definidas antes de implementar pedidos.
- Pagamento possui estado próprio. Confirmar pagamento e confirmar produção são operações distintas.
- Cozinha recebe somente informações necessárias à produção; endereço e cobrança ficam com os perfis autorizados.
- Administrador, atendente, cozinha e expedição têm permissões validadas pelo backend.
- Cliente pode iniciar atendimento sem cadastro complexo. Acesso a dados pessoais e histórico exige verificação adequada.
- Ficha técnica fundamenta custo, consumo de estoque e CMV. Cancelamentos podem exigir estorno conforme o estágio de produção.
- Estoque, financeiro e integrações devem suportar repetição de solicitações sem duplicar efeitos.
