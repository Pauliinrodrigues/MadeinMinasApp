# Ensaio do chat com o catálogo comercial

O perfil `Rehearsal` copia os dados públicos da instalação local para um banco separado e utiliza o Gemini configurado no backend. Reaproveita o chat, as ferramentas, a revisão do pedido e o atendimento humano existentes. O catálogo tem nomes, descrições e preços da loja; clientes, pedidos, estoque e fichas técnicas deste ambiente são fictícios.

## Iniciar e encerrar

Requer o [piloto inicial](ai-chat.md#piloto-local-com-gemini-real) já preparado, SDK .NET 10, PostgreSQL 17, Node/dependências do frontend e Gemini configurado em User Secrets. A API da loja precisa estar acessível em `http://localhost:5080` para a captura. Execute na pasta do repositório:

```powershell
powershell -NoProfile -File scripts/Start-AiRehearsal.ps1
```

O script captura o catálogo primeiro e depois reinicia somente os processos registrados do ensaio. Aplica as migrations no banco de teste e importa os dados públicos numa transação. Uma origem indisponível impede o início antes de encerrar o ensaio anterior. Portas ocupadas por processos desconhecidos impedem o início. O sistema da loja e o piloto sintético podem permanecer ativos.

| Acesso        | Endereço                                                   |
| ------------- | ---------------------------------------------------------- |
| Cliente       | <http://localhost:8103/pedido/atendimento>                 |
| Equipe        | <http://localhost:8103/entrar>                             |
| API do ensaio | `http://localhost:5083`                                    |
| Banco         | `made_in_minas_ai_rehearsal`, PostgreSQL `127.0.0.1:55435` |

O administrador é `piloto.admin`. A senha aleatória está em `.local/ai-rehearsal/pilot.local.json`, ignorado pelo Git. Esse arquivo não contém a chave Gemini. O ensaio possui chave JWT, emissor e audiência exclusivos; o navegador usa outra origem para separar login e conversa dos demais ambientes. A interface pública identifica o ambiente de ensaio e orienta usar dados fictícios.

Para encerrar e preservar os dados:

```powershell
powershell -NoProfile -File scripts/Stop-AiPilot.ps1 -Profile Rehearsal
```

API/Angular só são encerrados se os PIDs e comandos corresponderem ao perfil. O cluster PostgreSQL é compartilhado com o piloto sintético: fica ativo enquanto o outro perfil registrado estiver executando. Credenciais e logs permanecem nas pastas locais ignoradas. Os limites padrão são 30 turnos por conversa, 2.048 tokens por chamada e 300 reservas diárias conservadoras; cada turno reserva até nove chamadas. Alterar `-MaxDailyRequests` não altera a cota do Google nem cria um teto financeiro.

## O que é copiado

A captura faz apenas GETs públicos, com alvo fixo, sem autenticação e sem seguir redirecionamentos: cardápio paginado, atendimento, regiões e opções de entrega. Uma lista explícita permite apenas IDs públicos, categorias, nomes, descrições, preços, disponibilidade, horários, pagamentos, prazo, regiões, taxa e endereço público da loja.

Não são consultados clientes, conversas, pedidos, fichas técnicas, custos, margens ou credenciais da operação. As imagens não são copiadas; o frontend utiliza o comportamento existente para produtos sem foto. Os insumos criados no ensaio são identificados como fictícios, com saldo inicial simulado e sem custo operacional. A disponibilidade copiada é um retrato do catálogo público no momento da captura; não comprova saldo físico nem capacidade real de produção.

O importador recusa conexões fora do host/porta/banco/usuário previstos antes de conectar ou migrar. Também valida a estrutura e as regras do snapshot antes de importar. Importar novamente atualiza preços e descrições e desativa produtos/categorias que saíram da origem, preservando pedidos históricos, insumos, fichas já criadas e saldos do ensaio. Não apaga registros.

O ensaio respeita os horários importados e recebe pedidos somente enquanto aberto. O piloto sintético conserva sua configuração de recebimento fora do horário. Fora do horário, consultas continuam possíveis; revisão/criação obedecem à validação existente do backend. Loja pausada na origem também é importada pausada.

## Conferência comercial pendente

Na captura de 10/10/2026, a API local publicou uma categoria e somente **Uai so, R$ 23,90**. A descrição comercial foi preservada, incluindo pão, blend bovino de 120 g, mussarela, bacon, tomate, picles de cebola roxa, alface americana e maionese de alho assado. O ensaio só oferece o que estiver cadastrado nessa origem.

Entrega para todos os bairros de Corinto–MG: R$ 5,00; retirada: grátis, Rua Esperança, 68, Clarindo de Paiva, Corinto–MG. Pagamentos cadastrados: cartão, dinheiro e Pix; prazo variável conforme a demanda.

O relatório `.local/ai-rehearsal/readiness.json` identifica uma divergência com a orientação do responsável: sexta, sábado e domingo estão cadastrados até **23:59**, enquanto a orientação foi **00:00**. Segunda a quinta: 18:30–23:30. A captura preserva o cadastro; não corrige configurações na operação. A equipe precisa conferir os horários e completar o catálogo antes de usar a IA com clientes.

## Avaliar o Gemini

Para listar cenários sem chamadas ao provedor:

```powershell
node scripts/Evaluate-AiRehearsal.mjs
```

Para executar seis cenários e até oito turnos, com visitantes fictícios:

```powershell
node scripts/Evaluate-AiRehearsal.mjs --live
```

Para repetir somente os cenários afetados por uma correção:

```powershell
node scripts/Evaluate-AiRehearsal.mjs --live --cases store-product,store-rules
```

O avaliador autentica o administrador do ensaio, verifica a assinatura e identidade exclusiva do JWT e compara o catálogo com o snapshot antes de escrever conversas. Não aceita outro alvo de API. Confere cardápio, ingredientes comerciais, horários/pagamentos/taxas, retirada, alteração de quantidade, repetição idempotente, entrega com dados fictícios e encaminhamento humano. Os valores esperados são derivados dos produtos/regiões capturados, e o estado real do backend é examinado para os cálculos.

Os turnos são espaçados por 25 segundos. Falha de provedor ou comunicação interrompe os casos seguintes, sem retry automático. Nenhuma confirmação de pedido é enviada. Os relatórios `.local/ai-rehearsal/results/evaluation-<data>.json` contêm resultados, latência, consumo correlacionado e hashes do catálogo e da revisão do conjunto; não incluem diálogos, contatos, tokens ou chaves. Ausência de registros de uso é indicada explicitamente.

Testes automatizados do importador e do avaliador usam banco/respostas simulados, sem chamadas pagas. Resultados executados e limites de cobertura estão em [validation.md](validation.md).

## Roteiro da equipe

1. Abrir o chat do ensaio e usar um nome fictício. Pedir cardápio e ingredientes; comparar com os cadastros aprovados.
2. Montar retirada com telefone fictício, conferir R$ 23,90 para uma unidade e R$ 47,80 para duas, conforme o catálogo capturado acima. Trocar/remover itens e conferir nova revisão.
3. Montar entrega com rua, número, bairro e telefone fictícios. Conferir a região e taxa de R$ 5,00. Sem esses dados, o pedido deve permanecer incompleto.
4. Pedir atendimento humano, assumir a conversa na equipe e responder. A IA deve permanecer interrompida; retomada depende do controle autorizado existente.
5. Conferir loja fechada/pausada e rejeição de dados/produtos ausentes. Registrar perguntas e divergências antes de liberar clientes.
6. Após revisar, usar **Confirmar pedido** somente no ensaio. Entrar em **Atendimentos**, abrir a conversa e conferir **Pedido #… recebido** e **Abrir pedido #…**. Antes do envio, o resumo informa **Pedido em montagem**, com os campos pendentes; ainda não está na fila de Pedidos. O atalho exige permissão de gestão de pedidos.
7. Abrir o pedido recebido em **Pedidos → Aguardando confirmação**, confirmar o preparo e conferir a entrada na Cozinha. Atualizar a conversa deve mostrar o novo status, mesmo sem novas mensagens. O envio do cliente e a confirmação da equipe são etapas distintas; nenhum deles processa pagamento.

Em 11/10/2026, o responsável relatou sucesso no teste de confirmação/localização do pedido e no acesso da equipe. A conferência somente leitura encontrou um pedido no ensaio, total R$ 28,90. Esse relato não comprova execução integral dos cenários de transferência humana, indisponibilidade, loja fechada ou impressão física.

O botão **Confirmar pedido** cria somente uma compra fictícia neste banco e continua exigindo a revisão atual. A avaliação automática não o aciona. Não há impressora real conectada ao ensaio nem processamento de pagamento. O restante do roteiro da equipe, catálogo completo, configuração operacional, cotas e preparação da hospedagem continuam necessários para uma liberação controlada.
