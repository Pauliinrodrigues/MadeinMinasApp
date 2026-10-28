# Análise de prontidão da primeira versão do Made in Minas

Este é o diagnóstico inicial de 27/10/2026. As correções, validações e pendências da execução estão no [registro da V1](v1-release.md), atualizado em 28/10. A decisão comercial vigente mantém sexta a domingo até 23:59.

O núcleo de pedidos está implementado. A primeira versão precisa de estabilização do ambiente, configuração de produção, dados comerciais conferidos, conclusão do pagamento solicitado pelo cliente e aceite de ponta a ponta. A IA funciona no ensaio, mas sua liberação pública depende também das condições de uso do provedor e do tratamento de dados pessoais.

Análise de 27/10/2026 sobre a `master`, commit `b9965e1`, após a integração do [PR 21](https://github.com/Pauliinrodrigues/MadeinMinasApp/pull/21). O levantamento reúne 40 requisitos e melhorias: 12 bloqueadores gerais propostos para a publicação, quatro condições adicionais para anunciar atendimento com IA, 16 melhorias prioritárias e oito evoluções posteriores. A prioridade expressa uma proposta de escopo para aprovação da loja, não uma declaração de que todas as melhorias são obrigatórias.

## Escopo recomendado para a primeira versão

Uma hamburgueria, com cardápio público, entrega em Corinto e retirada, pedidos por link, atendimento humano, aceite da equipe, cozinha, expedição, impressão e registro manual do recebimento. Entrega de R$ 5,00 e retirada gratuita na Rua Esperança, 68, Clarindo de Paiva, Corinto–MG. Pagamentos aceitos: cartão, dinheiro e Pix; prazo de entrega informado conforme a demanda.

O atendimento por IA pode ser liberado progressivamente após os critérios específicos abaixo. O checkout e o atendimento humano devem permitir operar quando o provedor estiver indisponível. Pagamento online, WhatsApp, combos, fidelidade e conta de cliente são evoluções independentes; passam a ser requisitos da V1 somente se fizerem parte da oferta comercial aprovada.

## Evidências verificadas

- A [CI da master](https://github.com/Pauliinrodrigues/MadeinMinasApp/actions/runs/40552523364) está aprovada nos jobs backend, frontend e impressão. A [execução do PR](https://github.com/Pauliinrodrigues/MadeinMinasApp/actions/runs/40550917279), com o mesmo conteúdo integrado, aprovou 750 testes backend, 692 cenários de interface, 40 testes do avaliador e a verificação nativa de impressão. Esses resultados não equivalem a homologação do equipamento ou da hospedagem.
- Foram visitadas 15 telas em computador e celular, totalizando 30 combinações. Não houve erros JavaScript, respostas de API com erro ou transbordamento horizontal da página nessas visitas. As telas públicas usaram o ambiente principal; a equipe usou o ensaio com dados fictícios. Não foram enviados pedidos, mensagens ou comandos de impressão. Capturas e resumo técnico ficam em `.local/v1-audit`, fora do Git.
- A consulta ao banco principal foi executada em transação `READ ONLY`, restrita a `made_in_minas`. Os resultados abaixo são agregados; não incluem contatos, mensagens ou valores de vendas.
- As consultas NuGet não encontraram pacotes sinalizados como vulneráveis na API, testes ou agente de impressão. O audit npm de dependências de produção encontrou um alerta alto em `zod`. Isso não é uma auditoria de exploração nem uma certificação de segurança.
- O ambiente público de hospedagem, o teste de carga, a restauração operacional e o roteiro completo com a equipe ainda precisam de validação própria. A visita visual não testou todos os formulários, acessibilidade por leitor de tela ou todas as transições reais.

## Situação por funcionalidade

| Área                   | O que já existe                                                                                                                  | Pendência para uso profissional                                                                                                              |
| ---------------------- | -------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------- |
| Acesso da equipe       | Perfis, autorização no servidor, senha com hash, bloqueio de tentativas, revogação, alteração e reset administrativo de senha.   | Melhorar continuidade da sessão e definir recuperação de acesso do administrador.                                                            |
| Catálogo e fotos       | Categorias, produtos, descrições, preços, upload de fotos próprias, disponibilidade e busca.                                     | Conferir todo o cardápio que será oferecido; melhorar a apresentação no celular. Não é necessário refazer o upload.                          |
| Fichas e estoque       | Composição, custo dos insumos, saldo, mínimo, reposição, histórico e baixa no aceite do pedido.                                  | Validar fichas e contagem física com a cozinha. Observações livres não alteram composição ou preço automaticamente.                          |
| Carrinho e checkout    | Seleção, quantidades, observações, entrega/retirada, endereço, revisão no servidor e criação idempotente.                        | Coletar preferência de pagamento e necessidade de troco; simplificar o formulário e seus avisos.                                             |
| Horários e entrega     | Pausa da loja, horários, região, taxa, retirada e mensagem de prazo, com validação do backend.                                   | Manter sexta a domingo até 23:59, conforme decisão de 27/10; conferir o cadastro antes da publicação.                      |
| Pedidos                | Origem site/equipe, filas, filtros, histórico, confirmação, cancelamento e controle de concorrência.                             | Melhorar operação no celular e informações financeiras solicitadas pelo cliente. Pedido público aguardar aceite é comportamento intencional. |
| Pagamentos             | Registro manual de dinheiro, Pix, crédito/débito, conferência, dinheiro entregue, troco e devolução.                             | O troco já existe no recebimento da equipe; falta pedi-lo ao cliente no checkout/chat. Não existe integração bancária automática.            |
| Cozinha                | Confirmados, em preparo e prontos, com tempos e observações.                                                                     | Homologar em um turno real, com tela adequada e rotina de contingência. Não enviar pedidos Novos diretamente ao preparo.                     |
| Expedição              | Endereço, modalidades, etapas de entrega, pagamento e finalização condicionada ao recebimento.                                   | Conferir o recebimento na entrega e tornar a preferência de pagamento visível ao preparar a saída.                                           |
| Impressão              | Comandas, fila persistente, agente Windows, credencial protegida, tratamento de envio incerto e reimpressão controlada.          | Inicialização após reinício, distribuição do executável, configuração clara e aceite físico completo. O heartbeat atual está recente.        |
| Avisos                 | Alerta digital a cada 30 segundos e contador, compartilhados pelas páginas da equipe autorizada.                                 | Reduzir o painel explicativo e notificar intervenção humana no chat. O som depende da ativação e da aba visível.                             |
| Acompanhamento         | Credencial por pedido, status, múltiplos comprovantes e opção de lembrar no dispositivo.                                         | Alinhar textos sobre fechamento da aba, prazo e recuperação; validar reinício com chaves persistentes.                                       |
| Atendimento humano     | Fila, responsável, mensagens, histórico, rascunhos, recuperação e encerramento.                                                  | Rotina de resposta, avisos globais e comunicação clara de disponibilidade. Token expirado não apaga o histórico.                             |
| IA                     | Gemini, provedor substituível, contexto limitado, ferramentas autorizadas, cálculo, confirmação, vínculo e transferência humana. | Atualizar o ambiente principal, controlar a ativação, dimensionar cotas, homologar com catálogo final e resolver adequação contratual/dados. |
| Gestão                 | Dashboard, relatórios por período, recebimentos/estornos e CMV teórico.                                                          | Conciliação de caixa, exportação e, em evolução própria, custos históricos e resultado financeiro.                                           |
| Qualidade e publicação | CI com testes, formato, lint e builds; configuração frontend de produção com `/api`.                                             | Processo de implantação, domínio, persistência, recuperação, monitoramento e verificações de segurança contínuas.                            |

## Bloqueios confirmados no ambiente atual

### Versão da API e esquema do banco

O frontend principal usa `localhost:8101`, conectado à API `5080`. A API responde como saudável, mas seu OpenAPI não apresenta os endpoints de revisão/confirmação da IA nem transferência. A integração do código no Git não substituiu o processo em execução.

O banco principal ainda não tem `ChatOrderDrafts` nem a migration `20261010152627_AddAiChatAgent`. Além disso, registra `20261003145937_AddCustomerChat`, que não consta na cadeia de migrations do código atual. A mesma contagem de migrations entre ambientes não comprova equivalência de esquema. Antes de atualizar a operação, é necessário revisar o histórico e ensaiar a atualização numa restauração isolada, preservando os dados legados.

A migration nova é necessária ao executar a versão atual do chat, mesmo com IA desligada: as consultas de atendimento passaram a utilizar o estado da montagem. Não basta habilitar a chave ou reiniciar com o banco antigo. O [health check atual](../backend/MadeInMinas.Api/Infrastructure/DatabaseHealthCheck.cs) verifica conexão, sem verificar compatibilidade de esquema; [o status do sistema](../backend/MadeInMinas.Api/Controllers/SystemController.cs) não identifica a versão do build.

### Dados e condições comerciais

Há um produto ativo e um insumo ativo. O produto tem foto, descrição e ficha; o insumo tem custo positivo e saldo positivo. Não há evidência de uma falha de cadastro nesses indicadores, mas eles não comprovam que o cardápio, a composição e o inventário físico da hamburgueria estejam completos. Se a oferta inicial tiver só esse produto, esse escopo precisa ser conferido e aprovado pela loja.

A API pública confirma entrega de R$ 5,00 para todos os bairros de Corinto/MG, retirada gratuita e cartão/dinheiro/Pix. Segunda a quinta estão em 18:30–23:30; sexta, sábado e domingo estão em 18:30–23:59. Em 27/10, a loja confirmou manter 23:59; não há ajuste para 00:00 a executar. A loja estava fora do horário durante a análise, e a recusa de novos envios nesse período é esperada.

O [contrato do checkout](../backend/MadeInMinas.Api/DTOs/PublicCheckout/PublicCheckoutContracts.cs) e a [entidade do pedido](../backend/MadeInMinas.Api/Models/Order.cs) não armazenam preferência de pagamento ou troco solicitado. O [recebimento administrativo](../backend/MadeInMinas.Api/DTOs/Payments/PaymentContracts.cs) já suporta dinheiro entregue e troco. A nova coleta precisa chegar ao resumo, pedido, expedição e comanda sem marcar a compra como paga.

### Segurança de dependências e banco

`npm audit --omit=dev --json` sinalizou `zod` 4.6.5 com severidade alta, por possível esgotamento de memória na validação de coleções grandes. A versão corrigida indicada é 4.6.6. `npm explain` mostra o pacote também ligado a `@angular/forms`, além das ferramentas de desenvolvimento. Não há importação direta de Zod no código da aplicação e não foi demonstrada exploração do fluxo atual. É necessário resolver a dependência de forma compatível e revalidar o frontend, sem atualização forçada indiscriminada. [Advisory GHSA-mrhx-c7xc-m3w5](https://github.com/advisories/GHSA-mrhx-c7xc-m3w5).

O PostgreSQL consultado informou versão **17.4**. Na consulta de 27/10/2026, a versão menor corrente da série 17 era **17.11**. A atualização deve ter janela e restauração testada, especialmente porque o serviço local é compartilhado com outro projeto. A conta da aplicação consultada não é superusuária nem pode criar bancos; essa proteção deve ser mantida na hospedagem. [Política de versões PostgreSQL](https://www.postgresql.org/support/versioning/).

### Implantação e continuidade

O build de produção já usa [API relativa `/api`](../frontend/made-in-minas/src/environments/environment.production.ts), o que permite frontend e API na mesma origem. Falta confirmar e preparar a hospedagem que servirá o frontend, encaminhará `/api`, manterá HTTPS e devolverá a página Angular ao recarregar uma rota interna. Docker não é obrigatório; o processo escolhido precisa ser reproduzível.

O [Program.cs](../backend/MadeInMinas.Api/Program.cs) habilita HSTS/HTTPS fora de Development, mas não configura explicitamente cabeçalhos de proxy confiável. Se a hospedagem encerrar TLS antes da API, esquema e IP precisam ser encaminhados corretamente para evitar redirecionamentos inadequados e limitadores agrupando todos os clientes. A configuração depende do host; a integração IIS tem particularidades próprias. [Microsoft sobre proxies](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0).

O Data Protection usa o padrão local. Na hospedagem, chaves, imagens e banco precisam sobreviver à troca de processo/instância; as chaves também precisam de controle de acesso e proteção em repouso. Perder as chaves pode invalidar credenciais de chat e acompanhamento. [Microsoft sobre persistência das chaves](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/default-settings?view=aspnetcore-10.0).

O backup mais recente encontrado em `.local/backups` é de **10/10/2026**. Isso não comprova ausência de backup externo; a rotina externa não foi validada. O requisito de publicação é ter backup programado, cópia protegida e restauração demonstrada, cobrindo também imagens e os segredos/chaves necessários à recuperação. A fila de impressão está ativa, o agente tem conexão recente e não há trabalhos em revisão na consulta atual. O [iniciador existente](../scripts/Start-PrintAgent.ps1) ainda exige execução após reiniciar o Windows.

## Condições específicas para a IA pública

O principal mantém `Ai:Enabled=false`; chave e modelo existem em User Secrets, sem serem expostos no frontend. Nos perfis de ensaio e piloto, o backend informa `automationAvailable=true`. As conversas antigas consultadas já ultrapassaram sete dias de acesso. Uma nova conversa é necessária para testar; essas credenciais não devem ser estendidas ou compartilhadas para contornar a expiração.

O [contexto do agente](../backend/MadeInMinas.Api/Services/AiChatCoordinator.cs) reduz os campos estruturados enviados ao modelo e não inclui a credencial do pedido. Entretanto, o histórico de mensagens pode conter informações pessoais digitadas pelo cliente, e o estado inclui nome. A [abstração de provedor](../backend/MadeInMinas.Api/Services/Ai/AiContracts.cs) permite adaptar a integração.

Os termos atuais do Gemini distinguem serviços gratuitos e pagos, orientam a não enviar informações pessoais aos gratuitos e incluem restrições sobre público menor de 18 anos. Isso exige avaliar o plano, o público da hamburgueria e a minimização de dados antes de expor o agente. Trocar apenas o plano não resolve automaticamente todas as condições. Para os testes gratuitos, continuar com dados fictícios. Alternativas técnicas incluem coletar contato/endereço em formulário separado, mantê-los fora do contexto do modelo e escolher serviço/configuração compatível com o público e os dados tratados. [Termos Gemini](https://ai.google.dev/gemini-api/terms).

Os limites do Google variam por projeto, modelo e modalidade de uso. O limite local de 300 reservas diárias não significa 300 clientes: com `MaxToolCalls=8`, o [coordenador](../backend/MadeInMinas.Api/Services/AiChatCoordinator.cs) reserva nove chamadas por turno, admitindo no máximo 33 turnos completos nesse orçamento padrão. A reserva é conservadora e não equivale ao número efetivo de chamadas ou a um teto financeiro. É necessário dimensionar com a demanda e mostrar uso, falhas e transferência para a equipe. [Limites Gemini](https://ai.google.dev/gemini-api/docs/rate-limits).

## Requisitos priorizados

P0 bloqueia a publicação do escopo recomendado até ser resolvido e aceito. P0 IA se aplica quando a V1 anunciar o atendimento automático. P1 melhora adoção, operação e manutenção e deve ser priorizado na estabilização. P2 pode entrar após validar a primeira versão. Um item de configuração pode ser concluído sem desenvolver um módulo novo.

### P0 antes da publicação

| ID  | Requisito                                    | Critério para concluir                                                                                                                                                                                                                              | Responsabilidade                               |
| --- | -------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------- |
| R01 | Alinhar API, frontend e migrations.          | Atualização ensaiada em restauração do banco real; legado revisado sem apagar histórico; API executando o build esperado e atendimento manual sem falhas por esquema ausente.                                                                       | Desenvolvimento e infraestrutura.              |
| R02 | Preparar domínio e implantação reproduzível. | Acesso externo por HTTPS; `/api`, imagens e rotas Angular funcionam inclusive após recarregar; serviço reinicia e permite voltar ao artefato anterior compatível.                                                                                   | Infraestrutura e desenvolvimento.              |
| R03 | Persistir dados, imagens e chaves.           | Reiniciar/substituir a instância preserva fotos e acessos ainda válidos ao chat/pedido; armazenamento e proteção das chaves documentados.                                                                                                           | Infraestrutura.                                |
| R04 | Automatizar backup e comprovar recuperação.  | Execução programada com alerta de falha; restauração isolada conferida; prazo aceitável de perda de dados e tempo de retorno definidos com a loja.                                                                                                  | Infraestrutura e responsável da loja.          |
| R05 | Resolver o alerta de dependência.            | Árvore de Zod corrigida de forma compatível, lock atualizado, audit de produção sem esse alerta e checks do frontend aprovados.                                                                                                                     | Desenvolvimento.                               |
| R06 | Conferir o catálogo que será vendido.        | Loja aprova produtos, fotos, descrições, preços, composição e saldo inicial; indisponibilidade corresponde à capacidade de produção.                                                                                                                | Loja e cozinha.                                |
| R07 | Validar horários e condições comerciais.     | Sexta a domingo encerram às 23:59 conforme decisão de 27/10; testes nos limites do horário, pausa, Corinto, R$ 5,00 e retirada gratuita aprovados.                                                                                                          | Loja e desenvolvimento.                        |
| R08 | Coletar pagamento pretendido e troco.        | Checkout e compra pelo chat registram a preferência; dinheiro/troco validado pelo backend; equipe e comanda recebem os dados; recebimento continua exigindo conferência.                                                                            | Desenvolvimento e loja.                        |
| R09 | Configurar segurança da instalação.          | Segredos exclusivos, conta de banco restrita, ambiente Production e proxy/HTTPS verificados; acessos de equipe e estação separados; credenciais de teste não reutilizadas.                                                                          | Infraestrutura e desenvolvimento.              |
| R10 | Definir privacidade e contato da loja.       | Aviso acessível descreve coleta e provedores; canal para suporte/dados e período de retenção definidos; descarte/anonimização preserva obrigações e histórico necessário.                                                                           | Responsável da loja, com apoio de privacidade. |
| R11 | Homologar o turno completo.                  | Compra por link, atendimento humano, aceite, cozinha, expedição, recebimento, impressão física e finalização testados com a equipe; recuperação, concorrência e reinícios conferidos. Compra pelo chat também homologada quando a IA for anunciada. | Loja, desenvolvimento e operação.              |
| R12 | Implantar monitoramento mínimo.              | Build e compatibilidade de esquema identificáveis; alertas de API, backup, agente desconectado e fila parada; erros rastreáveis sem registrar contatos, tokens ou corpos completos.                                                                 | Desenvolvimento e infraestrutura.              |

### P0 IA quando houver atendimento automático público

| ID  | Requisito                                     | Critério para concluir                                                                                                                                                                            | Responsabilidade                              |
| --- | --------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------- |
| R13 | Configurar a IA no ambiente de destino.       | Provedor/modelo/segredo e limites conferidos; nova conversa recebe resposta; modos manuais são preservados e retomada é explícita.                                                                | Desenvolvimento e operação.                   |
| R14 | Disponibilizar controle operacional da IA.    | Responsável consegue ver disponibilidade e interromper o automático globalmente, sem editar frontend; assumir conversa impede resposta concorrente; retorno humano está operacional.              | Desenvolvimento e atendimento.                |
| R15 | Dimensionar cotas e acompanhar consumo.       | Demanda estimada cabe no orçamento e na cota real; uso e falhas visíveis; indisponibilidade/429 gera encaminhamento compreensível; alertas e limite de gasto compatíveis com o serviço escolhido. | Loja e desenvolvimento.                       |
| R16 | Aprovar uso do provedor e homologar diálogos. | Público e dados atendem às condições do serviço; dados mínimos enviados; testes com catálogo final, ambiguidades, inexistentes, descontos, confirmação, falha e tomada humana aceitos.            | Loja, desenvolvimento e apoio de privacidade. |

### P1 para estabilização e acabamento

| ID  | Melhoria                                         | Resultado esperado                                                                                                                                                | Responsabilidade            |
| --- | ------------------------------------------------ | ----------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------- |
| R17 | Compactar a vitrine no celular.                  | Primeiro produto alcançável sem atravessar vários avisos; informações comerciais continuam acessíveis.                                                            | Design e frontend.          |
| R18 | Dar continuidade ao carrinho.                    | Resumo/ação de continuar visíveis em posições úteis; quantidades e subtotal claros; taxa e total identificados no momento correto da revisão.                     | Frontend.                   |
| R19 | Simplificar a finalização.                       | Etapas indicam progresso real; menos instruções antes dos campos; DDD local coerente, mensagens junto aos campos e preenchimento preservado.                      | Frontend.                   |
| R20 | Tornar o acompanhamento compreensível.           | Textos distinguem aba, opção de lembrar no dispositivo e prazo de sete dias; recuperação não promete busca por telefone ou acesso sem credencial.                 | Frontend e atendimento.     |
| R21 | Guiar a montagem no chat existente.              | Produtos com imagem/valor consultado, ações de quantidade, pendências claras e caminho para o carrinho sem perda/duplicação; layout do chat preservado.           | Frontend e backend.         |
| R22 | Melhorar a navegação da equipe no celular.       | Menu de operações e acesso claro a cadastros/gestão; destino não depende de descobrir uma longa faixa horizontal.                                                 | Design e frontend.          |
| R23 | Adaptar a fila de pedidos ao uso móvel.          | Cartões ou resumo compacto com número, modalidade, status, pagamento e ação principal; detalhes disponíveis sem percorrer nove colunas.                           | Frontend e atendimento.     |
| R24 | Reduzir avisos repetidos e destacar intervenção. | Alerta de pedidos compacto mantém a cadência aprovada; fila humana e falhas operacionais têm contador/ação visível; restrição de aba oculta é comunicada.         | Frontend e atendimento.     |
| R25 | Melhorar a continuidade da sessão.               | Política de renovação segura e expiração definida; rascunhos e tentativas recuperáveis; evitar simplesmente persistir JWT em localStorage.                        | Backend e frontend.         |
| R26 | Iniciar e recuperar o agente Windows.            | Agendamento por usuário compatível com DPAPI ou rotina equivalente testada; reinício do computador volta a conectar sem duplicar papel.                           | Infraestrutura e operação.  |
| R27 | Distribuir o agente como versão instalável.      | Executável/configuração versionados, teste de conexão/impressão e instrução simples; operação não depende de recompilar diariamente no ambiente do desenvolvedor. | Desenvolvimento.            |
| R28 | Acrescentar conciliação de caixa.                | Recebimentos por método conferidos com dinheiro/Pix/cartão, divergências registradas e fechamento identificado; vendas não são tratadas como lucro.               | Loja e backend.             |
| R29 | Exportar e organizar relatórios.                 | Exportação autorizada dos filtros, totais claros e data/fuso consistentes; dashboard destaca filas e recebimentos úteis ao turno.                                 | Backend e frontend.         |
| R30 | Preparar suporte e recuperação de acesso.        | Manual curto por perfil, responsável por incidente e recuperação segura do administrador; dados de exemplo não são confundidos com a operação.                    | Loja e desenvolvimento.     |
| R31 | Automatizar manutenção de segurança.             | Consultas de dependências e política para alertas adicionadas à rotina/CI; atualizações compatíveis com lock e testes, sem correção forçada indiscriminada.       | Desenvolvimento.            |
| R32 | Organizar release e documentação vigente.        | Tag/artefato identificável, notas da versão, proteção/checks do Git conferidos e instruções atuais separadas dos registros históricos.                            | Desenvolvimento e operação. |

### P2 para versões posteriores

| ID  | Evolução                                | Dependência antes de oferecer                                                                                                                 |
| --- | --------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------- |
| R33 | Combos, tamanhos e adicionais cobrados. | Catálogo estruturado, regras de preço, revisão, baixa de estoque e comanda; observação textual não substitui esses dados.                     |
| R34 | Promoções, cupons e fidelidade.         | Regras, vigência, autorização e auditoria no backend; IA consulta somente o que estiver cadastrado.                                           |
| R35 | Pix/cartão com confirmação automática.  | Integração apropriada, confirmação autenticada, idempotência e devolução; não inferir recebimento de uma mensagem ou comprovante.             |
| R36 | WhatsApp.                               | Canal oficial, regras de acesso e tratamento de dados, recuperação e reaproveitamento dos serviços atuais.                                    |
| R37 | Gestão de entregadores.                 | Responsável pela entrega, atribuição e confirmação operacional; geolocalização somente se houver necessidade e tratamento adequado.           |
| R38 | CMV histórico e resultado financeiro.   | Custos preservados por venda, despesas e conciliação; o CMV teórico atual não representa lucro realizado.                                     |
| R39 | PWA e notificações fora da aba.         | Permissões e limites reais dos dispositivos; retorno ao aplicativo e testes em aparelhos da operação. Não prometer som com navegador fechado. |
| R40 | Conta do cliente e recompra.            | Identidade verificada, recuperação segura e autorização do histórico. Manter pedido como visitante na V1 evita exigir cadastro para comprar.  |

## Ajustes de fluxo e layout observados

A identidade visual já utiliza cores, tipografia e hierarquia consistentes. Os ajustes de maior retorno estão na densidade da informação e no acesso às ações.

| Tela                      | Observação da revisão                                                                                                                                    | Ajuste proposto                                                                                                                                |
| ------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------- |
| Cardápio no celular       | Cabeçalho, título e aviso de funcionamento ocupam a primeira tela; produtos ficam abaixo. Links “Falar com atendente” e “Meus pedidos” aparecem colados. | Reduzir o topo, resumir abertura/prazo numa faixa, manter horários em expansão e separar as ações secundárias.                                 |
| Checkout no celular       | Título grande e o mesmo aviso vêm antes dos campos; o formulário começa muito abaixo.                                                                    | Priorizar recebimento/contato/resumo, com cabeçalho compacto e detalhes comerciais acessíveis. Loja fechada deve continuar bloqueando o envio. |
| Equipe                    | O painel explicativo do som se repete acima do conteúdo. No celular, o título de Pedidos apareceu por volta de 493 px do topo.                           | Usar controle compacto de som, contador e ajuda expansível; priorizar a fila e a próxima ação.                                                 |
| Navegação móvel da equipe | Operação, cadastros e gestão compartilham uma faixa horizontal longa.                                                                                    | Operações frequentes visíveis e menu “Mais” para os demais destinos, com foco e indicação do local atual.                                      |
| Pedidos                   | A tabela é adequada no computador, mas exige rolagem interna no celular.                                                                                 | Cartões móveis com dados essenciais e acesso ao detalhe, preservando a tabela em telas maiores.                                                |
| Chat                      | Entrada simples e histórico existentes; a montagem depende muito de digitação e leitura de texto.                                                        | Resumo acessível, opções claras para dados faltantes e produtos, identificação de atendimento e recuperação de acesso expirado.                |
| Impressão                 | O estado de conexão/fila já existe, mas fica abaixo de textos e aviso sonoro global.                                                                     | Destacar conexão, última comunicação e pendências; instrução de instalação/configuração em área própria.                                       |
| Início e textos públicos  | A descrição HTML ainda anuncia atendimento “em preparação”; faltam informações de confiança reunidas numa área comercial.                                | Revisar textos de lançamento, endereço, contato, horários, retirada/entrega e privacidade; preservar a ação principal “Ver cardápio”.          |

Antes do aceite visual, conferir também teclado, foco, leitor de tela, zoom, legibilidade e áreas de toque. A revisão de 30 telas não substitui esses testes de acessibilidade nem um ensaio no celular físico da equipe.

## Ordem de execução

| Etapa | Entrega                                                                                        | Saída para avançar                                                                                                   |
| ----- | ---------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------- |
| 1     | Baseline técnica e segurança: R05, R01 e preparação de R03/R04/R09.                            | Dependência corrigida; atualização ensaiada no banco restaurado; dados preservados; build e esquema identificáveis.  |
| 2     | Operação comercial: R06, R07, R08 e R10.                                                       | Cardápio e regras aprovados; cliente informa pagamento/troco; informações de suporte e dados definidas.              |
| 3     | Acabamento dos fluxos: R17–R25, priorizando cardápio, checkout, pedidos e intervenção no chat. | Cliente conclui a compra com clareza e equipe opera no celular sem perder tentativas ou ações importantes.           |
| 4     | Publicação e continuidade: R02, R03, R04, R09, R11, R12 e rotina do agente.                    | Ambiente de homologação acessível, restauração/reinício testados, impressão física aceita e responsáveis preparados. |
| 5     | Liberação controlada da IA: R13–R16.                                                           | Uso do provedor adequado aos dados/público, cotas dimensionadas e diálogos aprovados com o catálogo final.           |

As etapas podem avançar em tarefas independentes; a ordem expressa dependências de aceite. A implementação de funcionalidades novas deve ocorrer em incrementos pequenos, com teste do comportamento afetado e os checks existentes preservados.

## Critérios de liberação da primeira versão

1. Não há P0 pendente no escopo que será anunciado; as condições de IA também estão atendidas se ela for pública.
2. O endereço externo funciona no celular e computador, inclusive acesso direto e recarregamento das rotas.
3. Pedido de entrega e retirada percorre revisão, envio único, aceite, cozinha, impressão, expedição, recebimento e finalização.
4. Alterações de quantidade/preço/disponibilidade exigem nova revisão; repetição de mensagem/envio não duplica compra ou papel.
5. Fechamento/pausa/virada de horário, falta de estoque, perda de conexão, retorno após login e concorrência têm tratamento compreensível.
6. Contas de atendimento, cozinha e expedição só acessam suas funções; número/telefone não dão acesso público ao histórico de terceiros.
7. Agente desconectado, impressora sem papel e envio incerto têm procedimento testado, sem repetição automática indevida.
8. Backup, restauração, troca de processo e retorno a versão compatível preservam registros e acessos necessários.
9. Capacidade e limites são testados para a demanda inicial definida pela loja; cotas e indisponibilidade do provedor não interrompem o caminho manual.
10. CI da release aprovada, equipe treinada, canal de suporte definido, artefato versionado e período inicial acompanhado.

## Referências de implementação e operação

- [Rotas e módulos existentes](../frontend/made-in-minas/src/app/app.routes.ts), [checkout](../frontend/made-in-minas/src/app/features/public-checkout/public-checkout.page.html), [menu](../frontend/made-in-minas/src/app/features/menu/menu.page.html) e [layout da equipe](../frontend/made-in-minas/src/app/features/staff/staff-layout.page.ts).
- [Serviços e middleware da API](../backend/MadeInMinas.Api/Program.cs), [autenticação](../backend/MadeInMinas.Api/Security/AuthenticationRegistration.cs), [recebimento](../backend/MadeInMinas.Api/Services/PaymentService.cs) e [expedição](../backend/MadeInMinas.Api/Services/DispatchService.cs).
- [Agente de IA](ai-chat.md), [avaliação](ai-evaluation.md), [ensaio](ai-rehearsal.md), [impressão automática](automatic-printing.md), [dados e migrations](../database/README.md) e [validações históricas](validation.md).
- Para política de dados, considerar o contexto da empresa e as orientações oficiais da [ANPD para agentes de pequeno porte](https://www.gov.br/anpd/pt-br/centrais-de-conteudo/materiais-educativos-e-publicacoes/anonimizado___guia_orientat-_seg_da_inf_p_atpp.pdf). O levantamento não equivale a aprovação jurídica ou homologação de produção.
