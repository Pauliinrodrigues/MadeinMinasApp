# Ajustes antes da próxima fase

Plano iniciado em 06/10/2026 na branch `feat/operational-usability`, a partir de `643aa0f`. A análise fornecida pelo responsável orienta os ajustes; dados locais citados nela precisam de conferência operacional e não autorizam inventar contagens, taxas ou fotos.

## Execução e aceite

| Bloco | Entrega | Critério de validação |
| --- | --- | --- |
| A — consistência | Disponibilidade pública por ficha/saldo; revisão conjunta dos ingredientes do carrinho; busca no cardápio; cobertura visível antes do checkout; textos corrigidos | Produto sem capacidade para uma unidade não pode ser adicionado; cesta que excede saldo é rejeitada sem escrita; nenhuma reserva ou alteração de estoque na consulta |
| B — continuidade | Rascunhos público/equipe na aba, recuperação de envio da equipe, retorno após login, destino por perfil, cadastro de cliente sem perder montagem; fila atualizada, resumo de pagamento e prévia de troco | Recarregar, navegar e autenticar novamente conservam a montagem; outro funcionário não recebe dados do anterior; revisão recalcula preços; envio incerto conserva a chave; lista atualiza sem sobreposição e pausa quando oculta |
| C — interface | Navegação agrupada por perfil, pedidos legíveis no celular, resumo do pedido, checkout compacto, controles de quantidade, ajuda contextual e compartilhamento explícito no chat | Conferir desktop/celular, teclado, foco, estados de carregamento/erro e tarefas completas com APIs de teste |
| D — regiões e taxas | Administração da cobertura pública, com revisão, edição, pausa e persistência no banco | Somente administrador; mudanças concorrentes detectadas; taxa alterada exige nova revisão da compra; pedidos anteriores preservados; nenhuma cobertura fictícia |
| E — filtros de pedidos | Origem e situação financeira na lista, combinações de filtros e atalho A receber | Paginação/contador corretos; tentativas anteriores não duplicam pedidos; recebimentos atuais prevalecem; polling não aplica filtros em edição |
| F — reposição | Central de pendências por saldo, mínimo e ausência de movimentações; busca por fornecedor; acesso ao estoque/cadastro | Contadores/paginação consistentes; igualdade no mínimo mantém alerta; unidades/precisão preservadas; somente administrador; nenhuma movimentação pela consulta |
| G — acompanhamentos | Histórico de até 20 pedidos na aba, seleção e remoção; gravação no dispositivo somente por opção explícita | Nova compra preserva acessos; tokens fora da URL; sem dados pessoais; resposta anterior não substitui pedido selecionado; falha de armazenamento não dispara compras |
| H — atendimento e fechamento visual | Fila/conversa lado a lado no computador; navegação por tela no celular; rascunhos por conversa; avisos de catálogo; atalhos de filas; etapas da cozinha; destaque da expedição; resumo lateral e histórico recolhido do pedido | Mensagens e tentativas não se misturam; recuperação mantém a chave; atividade da conversa não aberta permanece indicada; permissões e regras comerciais preservadas |

Os blocos A–C foram implementados na primeira rodada. A continuação acrescenta D, detalhado em [regiões e taxas](delivery-settings.md), E, em [filtros de pedidos](order-filters.md), e F, em [central de reposição](stock-replenishment.md). A validação combina testes reais da API com PostgreSQL isolado, testes de navegador com API simulada e inspeção visual. Nenhuma simulação foi registrada na operação. O aceite com pessoas e equipamentos reais permanece separado dos testes automatizados.

## Dependências da operação

- Responsável informa contagem física; ajustes de saldo continuam auditados pela tela de estoque, sem preenchimento automático.
- Responsável informa bairros/cidades e taxas reais; sem essa configuração, entrega pública permanece indisponível.
- Fotos devem representar os produtos reais; podem ser cadastradas por URL na tela existente. Não usar fotos fictícias como se fossem do produto.
- Criação de funcionários de cozinha/expedição depende de nomes, logins e senhas fornecidos pelo responsável.
- Impressora, pessoas, recebimento e roteiros de entrega exigem ensaio operacional no equipamento real.

Conferência pública local em 06/10/2026, após atualizar a API: readiness `Healthy`; um produto público com URL de imagem cadastrada e disponibilidade de produção positiva; cobertura de entrega retornando `[]`. Isso difere de partes do levantamento anterior. Não foram corrigidos saldos, fotos ou taxas por automação; uma URL cadastrada não comprova que a imagem represente o produto nem que carregue como arquivo de imagem.

## Evolução posterior, fora desta rodada

Renovação de sessão/refresh tokens; recuperação automática e primeiro acesso; edição de compra já registrada; pagamento dividido/online; entregadores e rotas; upload de imagens; CMV realizado; conversões de unidades; combos/adicionais com reflexo na ficha; relatórios comparativos/exportação; vínculo autenticado entre conversa e pedido; impressão automática; IA e WhatsApp. Essas funcionalidades exigem incrementos e regras próprios e não serão consideradas entregues por melhorias de tela.

O fechamento acrescenta G e H: múltiplos acompanhamentos e fila/conversa lado a lado. O responsável aceitou executar e publicar todos os ajustes desta rodada em 06/10/2026, incluindo commit, PR e integração à master. Esse aceite autoriza a publicação e não é evidência de ensaio com pessoas ou impressora. O ajuste entrega cobertura administrável, filtros da fila, reposição, resumo financeiro, compartilhamento de número, indicador local de atividade e os detalhes visuais abaixo.

## Conferência com a análise original

| Proposta de ajuste | Entrega nesta rodada |
| --- | --- |
| Produção coerente com vitrine e revisão | A: ficha/saldo no cardápio e revisão conjunta do carrinho, sem reserva antecipada |
| Avisos de pedidos e filas de trabalho | B/E: polling, contador, som opcional, origem/pagamento e atalhos; H: aviso de aumento da fila de confirmados na cozinha |
| Carrinhos preservados e volta após autenticação | B: rascunhos isolados, mesma tentativa no envio incerto, destino por perfil e retorno à tela |
| Entrega antecipada e configuração por tela | A/D: cobertura antes do checkout e administração com revisão/versionamento |
| Pedido, pagamento, chat e troco | B/C: resumo financeiro, prévia de troco e compartilhamento explícito somente do número |
| Cardápio, carrinho, checkout e login | A–C: busca, introdução compacta, barra de carrinho, controles de quantidade, revisão final, erros/foco, senha visível e Caps Lock |
| Equipe, pedido, cozinha e expedição | C/H: navegação agrupada, cartões móveis, resumo lateral, histórico recolhido, seleção de etapas, tempos/observações e destino/pagamento destacados |
| Catálogo, reposição e dashboard | C/F/H: sem foto/ficha/custo, pendências de saldo e atalhos do indicador para a fila filtrada |
| Acompanhamento e atendimento humano | G/H: vários pedidos, retenção opcional no dispositivo, fila/conversa e rascunhos por conversa; indicador local por versão |
| Textos desatualizados | A–C: pagamentos disponíveis, pedidos do site e pagamento conforme retirada/entrega |
| Contagem, fotos, cobertura, contas e equipamento | Ferramentas disponíveis; dados e ensaio reais continuam como dependências operacionais, sem valores inventados |

Gráficos/exportação/comparação e evolução comercial foram apresentados na análise como incrementos futuros; seguem explicitamente na lista de evolução posterior. A rodada não implementa refresh token, gateway, combos/adicionais ou IA.

Disponibilidade consultada é uma fotografia do saldo, não uma reserva. Pedidos concorrentes ainda exigem conferência definitiva na confirmação. Observações livres não alteram ingredientes consumidos.

## Resultados da primeira rodada (A–C)

- **508/508 testes backend aprovados**, incluindo seis novos casos de disponibilidade, ingredientes compartilhados, arredondamento e busca. PostgreSQL temporário em `127.0.0.1:55433`, encerrado e removido pelo helper. Nenhuma migration ou dependência nova.
- **530 cenários distintos de navegador aprovados**, desktop/celular, com API simulada. A execução ampla aprovou 514; oito casos com falha passaram na repetição e os oito interrompidos/não executados passaram em seguida. Duas expectativas antigas de navegação foram corrigidas em ambos os tamanhos; os demais problemas vieram do encerramento do Edge/worker. Não se declara uma execução única integral sem falhas. Detalhes em [validation.md](validation.md).
- **Quatro verificações adicionais aprovadas** após ampliar dois testes de pedidos, em desktop/celular: perda da resposta, reload, novo login, resposta 401 ou 429 e retomada com chave/corpo idênticos. Falhas de autenticação ou limitação não descartam a tentativa anterior.
- Build de produção Angular, lint sem avisos, Prettier e formatação C# aprovados; `git diff --check` sem erros.
- Capturas da navegação, pedidos, cardápio, carrinho e checkout conferidas em desktop/celular com dados simulados. Sem erros JavaScript nos helpers; verificações de largura aprovadas. O campo de busca em foco permanece inteiramente visível abaixo da barra do carrinho. Capturas e helpers ficam em `.local`, fora do Git.
- API atualizada em `http://localhost:5080` e frontend em `http://localhost:8101`. Consulta local de saúde, busca sem resultado e revisão pública responderam corretamente, sem criar pedido. A disponibilidade consultada não reserva ingredientes; a confirmação continua sendo definitiva.
- Alterações locais na branch `feat/operational-usability`, prontas para validação do responsável. Esta rodada não publicou commit, PR ou integração à master.

## Resultado da continuação (D)

Administração de regiões/taxas implementada e validada tecnicamente: 531 testes backend; 20 cenários da nova tela e 70 de sessão/checkout em desktop/celular, em execuções combinadas; build, lint, formatação e inspeção visual aprovados. Detalhes das repetições em [validation.md](validation.md). Migration aplicada com backup somente em `made_in_minas`, preservando contagens/saldos e sem inserir cobertura. API/frontend atualizados localmente. O responsável ainda deve informar os locais e taxas reais e realizar o aceite operacional. Demais pendências listadas acima continuam abertas.

## Resultado da continuação (E)

Filtros por origem e situação do pagamento, resumo na listagem e fila A receber implementados na API e na interface. A busca aplicada permanece estável durante paginação, repetição e atualização automática; o contador de novos mantém todas as origens. Foram validados 55 cenários distintos da API em execuções combinadas, 26 cenários de navegador em desktop/celular, build, lint, formatação e inspeção visual. API/frontend atualizados localmente, com nove checks de fundação aprovados. Não há migration nem escrita comercial neste bloco. Os resultados e limites dos testes estão em [validation.md](validation.md); arquivos, contrato e roteiro em [order-filters.md](order-filters.md). As demais pendências acima permanecem abertas.

## Resultado da continuação (F)

Central de reposição implementada com filas de pendências, busca por ingrediente/fornecedor, contadores, inclusão opcional de inativos e diferença até o mínimo calculada pela API. Atalhos levam ao estoque e ao cadastro existentes. Foram aprovados 44 testes backend e 30 cenários de navegador em desktop/celular; API/frontend atualizados localmente, com nove checks de fundação aprovados. Não houve migration, criação de compras ou movimentação na operação. Resultados e limites em [validation.md](validation.md); regras, arquivos e roteiro em [stock-replenishment.md](stock-replenishment.md). No encerramento de F, múltiplos acompanhamentos e chat lado a lado ainda estavam pendentes; foram implementados em G/H. Dados reais e ensaio operacional continuam separados da validação técnica.

## Roteiro de validação funcional

Fechamento G/H implementado e validado tecnicamente: **551 testes backend** e **304 cenários distintos de interface** nas execuções finais combinadas (284 e 32, com 12 repetições). Build de produção, lint e formatação aprovados; API/frontend locais atualizados, com nove checks de fundação. Os blocos A–H concluem os ajustes desta rodada e têm publicação autorizada pelo responsável. Histórico do acompanhamento e chat foram conferidos visualmente em computador/celular. Detalhes das execuções, incluindo a correção de uma expectativa de teste e a verificação da partida da API, estão em [validation.md](validation.md). Configuração real e ensaio com equipamento permanecem nas dependências da operação; evoluções futuras estão listadas separadamente acima.

O bloco F tem [roteiro próprio de reposição](stock-replenishment.md#validação-funcional), incluindo mínimo exato, unidades, filtros, conferência física e navegação para estoque/cadastro. Dados reais continuam sendo responsabilidade da operação.

O bloco D tem [roteiro próprio de regiões e taxas](delivery-settings.md#validação), incluindo comparação de duas sessões, pausa e mudança de taxa entre revisão e envio. A validação técnica está em [validation.md](validation.md).

1. Abra `/pedido`, confira a cobertura e busque um produto. Uma indisponibilidade de produção deve bloquear a adição; consultar cardápio/revisão não altera saldos.
2. Adicione itens, use os controles de quantidade e escreva uma observação. Recarregue o carrinho: seleção e observações permanecem; preços revisados precisam ser consultados novamente. O subtotal estimado usa somente preços recebidos da API nesta sessão, não preços salvos no rascunho.
3. Na equipe, monte um carrinho, abra cadastro de cliente/endereço pelo atalho e volte. Atualize a aba, autentique novamente e confira o rascunho recuperado. Uma conta diferente não deve recuperar os dados da anterior.
4. Em ambiente de teste, simule perda da resposta ao registrar. Recarregue, autentique e use **Tentar registro novamente**. A chave/corpo devem permanecer iguais, com um único pedido no servidor. Não simule falhas no banco operacional.
5. Deixe a fila de pedidos aberta e registre um pedido em outra sessão de teste. A fila e o contador devem atualizar em até 10 segundos. Ative som se desejado. Ocultar a aba pausa novas consultas; voltar retoma a atualização.
6. Confira resumo financeiro no detalhe e a prévia do troco antes do recebimento. O registro definitivo continua dependendo do servidor e da confirmação explícita do operador.
7. No checkout, tente revisar sem nome/telefone ou com endereço incompleto: o primeiro campo inválido recebe foco. Confira produtos, modalidade, endereço, taxa e total antes de enviar.
8. No chat, use **Incluir pedido #... na mensagem**: apenas o número é inserido no texto, sem envio automático ou token de acompanhamento. Na fila da equipe, **Nova atividade** indica versão ainda não lida nesta aba por esse funcionário.
9. Repita os fluxos em computador e celular. Para aceite operacional, use pessoas da equipe e a impressora real em um ensaio controlado.
10. Em uma base de teste, envie dois pedidos e use **Montar outro pedido** entre eles. Abra **Meus pedidos**, selecione cada um, recarregue e confira o status recebido da API. Ative **Lembrar meus pedidos neste dispositivo** somente em navegador pessoal e reabra em outra aba. Desmarque para retirar a cópia permanente; remova um acompanhamento com confirmação e verifique que o pedido continua na equipe.
11. Na equipe, abra duas conversas e alterne entre elas. Cada rascunho deve voltar à conversa correta. No computador, fila e conversa permanecem juntas; no celular, **Voltar à fila** mantém o filtro. Em teste, perca a resposta de um envio, alterne a conversa e retome **Conferir mensagem**: deve haver uma única mensagem no servidor.
12. Abra o indicador **Em preparação** no dashboard: a lista deve iniciar com esse status aplicado. Na cozinha, selecione uma etapa e confira os cartões. No detalhe, abra o histórico recolhido; na expedição, confira os destaques de destino/pagamento. Na lista de produtos, confira ausência de foto/ficha ou custo incompleto conforme os cadastros.

## Arquivos por responsabilidade

- API: `Services/ProductionAvailability.cs`, `PublicMenuService.cs`, `PublicCartService.cs` e `DTOs/Menu/PublicMenuContracts.cs`; testes `PublicMenuTests` e `PublicCartTests`.
- Regiões/taxas: `DeliverySettingsController`, `DeliverySettingsService`, DTOs/Delivery, entidade/configuração/migration `DeliverySettings`, checkout público e `features/delivery`; testes `DeliverySettingsTests` e `e2e/delivery-settings.spec.ts`. O bloco D adiciona uma tabela e migration, sem novas dependências.
- Filtros de pedidos: `DTOs/Orders/OrderContracts.cs`, `OrderService`, `order-api.service.ts`, `orders.page.*`, `OrderListTests`, `PaymentTests` e `e2e/orders.spec.ts`. O bloco E amplia a listagem existente, sem migration.
- Reposição: `StockReplenishmentController`, `StockReplenishmentContracts`, `StockService`, `stock-api.service.ts`, `replenishment.page.*`, rotas/menu e atalhos nas telas de ingredientes/estoque; testes `StockReplenishmentTests` e `e2e/replenishment.spec.ts`. O bloco F consulta os saldos existentes, sem migration.
- Continuidade: `core/services/public-cart-state.service.ts`, `features/cart/cart.page.*`, `core/auth/auth-session.service.ts`, `auth.guards.ts`, login e telas de clientes/endereços.
- Fechamento G/H: `public-order-history.service.ts`, `public-checkout-state.service.ts`, `public-order-tracking.page.*`, `chat-list.page.ts`, `staff-chat-route.page.ts` e páginas de chat; `ProductService`, `ProductContracts`, produtos/dashboard/cozinha/expedição/detalhe; testes `ProductCostTests`, `order-history.spec.ts`, `chat-workspace.spec.ts` e regressões ampliadas.
- Operação/interface: páginas de pedidos, pagamentos, menu, carrinho/checkout públicos, layout da equipe, produtos, cozinha e chat; `chat-read-state.service.ts` registra apenas a versão vista localmente.
- Testes: cenários novos em `e2e/operational-usability.spec.ts`, ampliações em pedidos, pagamentos, checkout/chat e ajustes nos helpers de login para solicitar explicitamente `/equipe` nos testes antigos.
- Documentação: README, roteiro, arquitetura, carrinhos, cardápio, acesso da equipe e validação.
