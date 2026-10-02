# Decisões técnicas

## 001 — Estrutura simples

Uma API e um frontend no mesmo repositório. Reaproveitamos a solução original, convertendo o console sem lógica de negócio para ASP.NET Core Web API.

## 002 — Dependências locais e fixadas

SDK definido em global.json; dotnet-ef no manifesto local; NuGet com packages.lock.json; frontend com package-lock.json. Os CLIs globais antigos não são requisito.

Angular 20 foi escolhido por ser compatível com as duas linhas de Node encontradas; Ionic 9.0.5 declara Angular >=18. Builds e teste em navegador validam a combinação concreta.

Referências:
- [Compatibilidade Angular](https://angular.dev/reference/versions)
- [Ionic Angular](https://ionicframework.com/docs/angular/overview)
- [Npgsql EF Core](https://www.npgsql.org/efcore/)
- [Health checks ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks?view=aspnetcore-10.0)

## 003 — Sem migrations vazias

O DbContext existe para preparar a integração. As tabelas serão criadas conforme cada fase. O processo de inicialização não altera o banco.

## 004 — Conexão opcional durante a fundação

A API pode iniciar sem credenciais de desenvolvimento. Readiness responde 503 nesse caso. Liveness permanece 200. A indisponibilidade do banco não é tratada como sucesso.

## 005 — Segredos fora do Git

User Secrets no desenvolvimento e variáveis de ambiente na hospedagem. O frontend recebe somente a URL pública da API. Connection strings não são registradas nos logs da aplicação.

## 006 — Produção será preparada explicitamente

O build do frontend usa /api no mesmo domínio. Domínio, TLS, proxy, backup/restore, monitoramento e credenciais de implantação dependem do ambiente real. A Fase 1 não publica nenhum serviço nem instala atualizações globais de SDK/runtime.

## 007 — Autenticação incremental de funcionários

A Fase 2A implementa JWT interno de 15 minutos, sem refresh automático. PasswordHasher do ASP.NET Core protege senhas; Users possui SecurityStamp para revogação e estado persistido de bloqueio. Tokens são verificados contra o usuário e perfil atuais no banco.

O primeiro administrador é criado somente por comando local e apenas se não há usuários. Os quatro perfis são dados da migration, sem credenciais. Políticas centralizam a autorização; as permissões de módulos futuros não criam esses módulos.

Microsoft.EntityFrameworkCore.Relational 10.0.12 é uma dependência explícita de runtime. Isso mantém API e projeto de testes na mesma versão; a referência privada a EF Design, sozinha, não fixa o runtime nos consumidores do projeto.

Detalhes e limites: [authentication.md](authentication.md).

## 008 — Catálogo incremental por categorias

A Fase 3A cria somente Categories e seu CRUD administrativo com inativação. Não há exclusão física, produtos antecipados nem dados de demonstração inseridos no banco real. Nomes são únicos após trim, NFC e maiúsculas, inclusive quando inativos. O índice único garante a regra nas gravações concorrentes. Atualizações são transacionais e a última edição aplicada prevalece; ETag não faz parte deste incremento.

catalog.manage é exclusiva de Administrator e independente de users.manage. A sessão e as regras da Fase 2 são reaproveitadas. Datas de categoria usam UTC com milissegundos para manter respostas de escrita e leitura consistentes. Detalhes: [categories.md](categories.md).

## 009 — Produtos e disponibilidade derivada

Produtos têm categoria obrigatória, preço decimal validado antes do numeric(8,2) e nome único dentro da categoria. Ativação e disponibilidade manual são independentes. A API calcula IsAvailableForSale combinando produto ativo, categoria ativa e disponibilidade manual; não considera estoque ou pedidos ainda inexistentes.

Novos vínculos exigem categoria ativa, mas uma edição pode manter a categoria original inativa. Não há exclusão física. Imagens são links HTTPS opcionais, com prévia e tratamento de falha; uploads e armazenamento de mídia ficam para incremento próprio. Detalhes: [products.md](products.md).

## 010 — Ingredientes com unidade-base estável

A Fase 3C usa kg, l e un. Custo é por unidade-base, com quatro casas decimais; mínimo usa três. Não há conversão automática de pacote/preço de compra. A unidade fica fixa após salvar, evitando reinterpretar custos e futuras quantidades de receita. Uma conversão futura deverá ser uma operação explícita que considere todos os vínculos.

Nome único inclui registros inativos. Fornecedor é texto opcional; não se cria tabela própria neste incremento. Custo zero é aceito explicitamente e destacado na interface para revisão. O mínimo é configuração, sem saldo ou alertas fictícios. Estoque transacional e CMV seguem para a fase 6. Regras, limites e testes: [ingredients.md](ingredients.md).

## 011 — Ficha técnica por produto, com composição transacional

A Fase 3D usa uma ficha por produto, rendimento inteiro em unidades do produto e quantidades totais na unidade-base do ingrediente. RecipeItems não aceita ingredientes repetidos. A posição reflete a ordem enviada. O contrato PUT representa a ficha inteira; criação e edição compartilham validação e transação, serializadas por bloqueio do produto. Identidade e CreatedAt são preservados nas edições; a última gravação válida prevalece.

Novos vínculos exigem ingredientes ativos. Um ingrediente inativo já vinculado pode ser mantido, ter sua quantidade ajustada ou ser removido. O backend retorna o status atual e a interface exibe aviso. Isso não altera a disponibilidade comercial do produto nem gera movimentos de estoque. Produto inativo pode ter sua ficha mantida. Não se antecipa cálculo de CMV, perdas, conversão de unidades ou histórico de versões. Detalhes: [recipes.md](recipes.md).

## 012 — Branch por incremento e integração validada

A `master` recebe trabalho testado e validado por pull request. Funcionalidades, correções e manutenção usam branches curtas, sem uma branch `develop` permanente. O incremento atual de fichas técnicas e manutenção foi preservado em `feat/recipes-and-maintenance`; os próximos começam na `master` atualizada após a integração anterior.

Branches não isolam o banco de dados. Migrations exigem conferir o destino e o esquema independentemente da branch. O fluxo, a publicação e a política desejada de proteção remota estão em [git-workflow.md](git-workflow.md).

## 013 — Clientes e endereços sem cadastro público

Clientes exigem somente nome e telefone brasileiro normalizado; o índice único cobre também inativos e concorrência. Identificação por telefone não autentica nem expõe o cadastro publicamente. Administrador e atendente recebem customers.manage, enquanto o catálogo mantém sua política própria.

Endereços pertencem a um cliente e só podem ser acessados pelo vínculo correto. Cliente e endereço têm inativação independente; uma futura compra copiará os dados de entrega. Gravações revalidam sessão e perfil na transação, e serializam alterações pelo cliente. Não há exclusão física, pedido antecipado, consulta externa de CEP ou abstração de repositório. Contratos e limites: [customers.md](customers.md).

## 014 — Carrinho temporário e revisão antes da persistência de pedidos

A Fase 4B separa montagem/revisão da futura criação do pedido. O carrinho vive na página Angular e é descartado na navegação/saída da sessão; não há tabela de carrinho nem armazenamento de dados pessoais no navegador. A permissão orders.manage permite consultar o catálogo de venda sem abrir o CRUD administrativo ao atendente.

A API aceita somente IDs, quantidades, modalidade e observações, e rejeita campos comerciais extras. A revisão valida os cadastros em um snapshot consistente e calcula os valores em decimal, sem gravar ou reservar preço/estoque. Exibe subtotal dos produtos; taxa de entrega, descontos, confirmação e pagamento exigem incrementos próprios. O próximo módulo deve revalidar e registrar cópias históricas, com criação idempotente, numeração atômica e transições explícitas. Regras e aceite: [cart.md](cart.md).

## 015 — Pedidos manuais com dados preservados e criação idempotente

A Fase 4C cria somente Orders, OrderItems e OrderStatusHistory. Número identity único separado do UUID, cópias dos dados comerciais e índice único por funcionário/tentativa. Um bloqueio transacional serializa reenvios; dados comerciais são bloqueados para leitura durante a criação. O SHA-256 da revisão detecta conteúdo alterado, sem substituir autorização nem permitir preços enviados pelo navegador.

Itens e valores são imutáveis nesta etapa. Status usa versão e bloqueio de linha, com gravação atômica do histórico. Novo pode ser confirmado ou cancelado pela equipe de atendimento; cancelamento após confirmação exige administrador. Cancelado é terminal e exige motivo. A confirmação preserva preços e revalida disponibilidade, sem efeito financeiro/estoque.

Adotada taxa manual validada e registrada pela API, a validar no aceite do usuário. Não há padrão silencioso de entrega gratuita. O navegador preserva a mesma tentativa enquanto a página existir e bloqueia edição após resultado desconhecido; ao perder a página/sessão, a equipe deve consultar os pedidos antes de registrar outra compra. Pagamentos seguem para incremento próprio. Detalhes: [orders.md](orders.md).

## 016 — Pagamento manual integral, independente da produção

A Fase 4D registra uma forma e o valor integral copiado do pedido. Pending → Received exige confirmação explícita, com dinheiro entregue validado e troco calculado no backend. Cancelled e Refunded são terminais. Refunded registra devolução integral já realizada externamente, por administrador; não chama banco, adquirente ou gateway. Divisão de pagamentos, recebimento parcial, taxas e conciliação ficam para incrementos próprios.

Todas as gravações bloqueiam primeiro o pedido, depois o pagamento quando existir. Cancelamento do pedido consulta pagamentos ativos sob o mesmo bloqueio, evitando corrida com criação/recebimento. Índice parcial único reforça a exclusividade Pending/Received. Histórico é gravado na mesma transação, com autor revalidado, versão, data e motivo. Criação tem chave por funcionário; transições idênticas da última versão podem ser repetidas sem novo histórico.

A leitura do resumo e da página usa snapshot consistente. O frontend preserva o comando exato durante falhas incertas e bloqueia novas decisões até resolver a tentativa; falha na consulta posterior à gravação exige consultar novamente, sem repetir a movimentação. Escolha inicial de pagamento manual integral aceita pelo usuário em 01/10/2026. Detalhes e contratos: [payments.md](payments.md).

## 017 — KDS com contrato de produção e histórico do pedido

A Fase 5A reutiliza pedido e histórico, sem tabelas de cozinha. Apenas pedidos confirmados entram na fila; início e conclusão são transições separadas da confirmação comercial e do pagamento. KitchenService projeta número, modalidade, produtos, quantidades, observações e horários; não envia cliente, endereço ou valores. Administrador e cozinha usam kitchen.work; atendente acompanha pela consulta comercial existente.

O painel consulta três colunas em um snapshot consistente, com paginação independente e ordenação por confirmação/número, sem corte de data que esconda atrasos. Polling de 10 segundos atende ao primeiro incremento; SignalR, sons e notificações exigirão avaliação própria. Falha ou dados desatualizados bloqueiam ações, e o relógio visual usa horário retornado pelo servidor mais tempo monotônico do navegador. Confirmação aberta pausa consultas automáticas; passados 30 segundos, atualizar antes de agir. A versão e o bloqueio do pedido continuam protegendo gravações concorrentes. Cancelamento administrativo após iniciar/concluir produção preserva histórico e exige resolução do pagamento; perdas de estoque serão tratadas na fase correspondente.

## 018 — Expedição e comandas na mesma branch

Por solicitação do usuário, 5B e 5C compartilham feat/dispatch-printing, com validação técnica por módulo e aceite conjunto. Expedição utiliza estados e histórico existentes, distinguindo entrega de retirada e exigindo recebimento integral para finalizar. Impressão pelo navegador usa contratos/permissions específicos e nenhuma escrita comercial; não manter flag de impresso com base em abertura/fechamento de diálogo. Impressão automática depende de equipamento/agente e teste físico. Não criar tabelas de entregador, jobs ou integrações fictícias. Detalhes: [dispatch-printing.md](dispatch-printing.md).

## 019 — Estoque manual antes de consumo automático e CMV

A Fase 6 começa pela base auditável por ingrediente: saldo e versão no cadastro, movimentos anexados em transação e sem edição/exclusão pela API. A política administrativa catalog.manage já limita o público necessário; uma permissão própria será introduzida quando houver necessidade de delegar estoque. Saldo negativo é rejeitado, contagens são valores absolutos com versão esperada, e entrada/saída exigem quantidade positiva. Custo não é recalculado por movimentação.

A chave idempotente é por ingrediente; o bloqueio desse ingrediente serializa verificação da chave, versão, saldo e inserção do histórico. A mesma transação revalida o autor. Leituras usam RepeatableRead. Não há trigger nem integração com pedidos neste incremento. Inventário inicial é conferido pelo operador, sem inferir saldo físico a partir das fichas técnicas. Consumo automático exigirá decidir composição histórica, arredondamento e compensação por estágio do pedido. Regras: [stock.md](stock.md).

## 020 — Baixa atômica na confirmação e devolução anterior ao preparo

A Fase 6B exige ficha para todos os produtos confirmados, inclusive revenda com ficha unitária. A listagem comercial e o carrinho não reservam estoque. OrderStockService participa da transação já aberta de OrderService: pedido e ingredientes bloqueados, validação integral antes da escrita, composição copiada e uma saída por ingrediente. Repetição usa versão/histórico existentes, reforçada por índice único por pedido/ingrediente/tipo.

Quantidades são arredondadas para cima a três casas por produto/ingrediente depois de agregar linhas do produto; depois são somadas entre produtos. Devolução usa a quantidade efetivamente baixada e nunca recalcula a ficha. Cancelamento antes do preparo devolve; após o início mantém consumo. Observações livres não alteram a composição. Essa política foi adotada e explicitada ao usuário durante a implementação; exige aceite manual antes da integração.

Pedidos existentes que não estejam Novos ficam Legacy, evitando baixa ou devolução fictícia após a atualização. Os Novos passam pelas regras atuais ao confirmar. Migração sem ajuste retroativo de saldo. Não introduzir custos históricos/CMV nesta etapa. Leituras das coleções de pedido usam snapshot e consultas separadas para evitar produto cartesiano. Detalhes: [order-stock.md](order-stock.md).

## 021 — CMV teórico consultado por produto

A 6C começa com consulta administrativa somente leitura, usando preço e custos atuais e rendimento da ficha. Não persistir um cálculo que ficaria desatualizado após editar ingredientes/preço. Usar uma leitura RepeatableRead e decimal no backend; nenhum cálculo comercial no Angular. Percentuais com duas casas, margem percentual complementar e valores negativos preservados.

Custo zero é tratado como pendência: o cadastro não distingue ingrediente gratuito de custo ainda não informado. Exibir composição e total conhecido como parcial, com indicadores completos nulos. Ingrediente inativo permanece no cálculo com aviso. Sem ficha, orientar cadastro; consultar não cria composição. CMV realizado e custo histórico exigirão outro incremento.

O usuário autorizou a continuidade em nova branch após a conclusão técnica da 6B. O commit local 98ecb4a preserva a 6B; feat/product-cmv parte dele e permanece dependente até integração. Esta organização não registra aceite manual nem publicação que ainda não tenham ocorrido. Regras e escopo: [cmv.md](cmv.md).

## 022 — Dashboard diário antes dos relatórios por período

A Fase 7A usa o dia civil em America/Sao_Paulo, determinado pelo servidor, com início inclusivo e fim exclusivo. Diferenciar criação, confirmação comercial e recebimento: pedidos cancelados saem do valor confirmado, mas eventos de recebimento/estorno conservam suas datas. Total confirmado inclui entrega; ranking considera apenas os valores dos itens. Filas incluem dias anteriores. A média de produção considera trabalho que chegou a Pronto hoje com início válido, incluindo posterior cancelamento.

Permissão dashboard.view inicialmente exclusiva de administrador, sem expor dados pessoais. Consultas em snapshot consistente, somas em decimal, médias ausentes como null e atualização manual no frontend. Não criar tabelas de agregados nem transformar o CMV teórico atual em custo histórico. Relatórios por período ficam para a 7B.

Continuidade autorizada após concluir implementação/testes da 6C, preservada em 5ce0ba8. feat/daily-dashboard parte desse commit e mantém a dependência da 6B; o pedido de continuidade não substitui aceite manual nem publicação. Contratos e validação: [daily-dashboard.md](daily-dashboard.md).

## 023 — Relatórios limitados por período e consistentes com o dashboard

A 7B acrescenta consulta administrativa de vendas/recebimentos, protegida por reports.view, com resumo, dias, formas de pagamento e ranking de dez produtos. Períodos de 1 a 90 dias inclusivos evitam respostas sem limite; sete dias é o padrão definido pelo backend. Conversão e agrupamento explícitos em America/Sao_Paulo evitam depender dos fusos do navegador/servidor/conexão. Datas históricas com horário de verão usam o primeiro instante local válido.

Reutilizar as definições comerciais da 7A, com teste de consistência para um único dia. O ticket do período usa a quantidade total de confirmações válidas, e todos os dias vazios permanecem na resposta. Quatro consultas agregadas em snapshot, sem carregar pedidos completos nem introduzir tabelas de indicadores. Não inventar fechamento contábil, CMV realizado ou filas históricas. A interface descarta o resultado ao alterar filtros e permite repetir consultas após falha.

O dashboard foi preservado no commit local 15e4fa0, e feat/period-reports parte dele após autorização para continuar. Integração deve preservar a sequência dos incrementos ainda locais, com aceites próprios. Regras e limites: [period-reports.md](period-reports.md).
