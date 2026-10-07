# Administração de regiões e taxas

Continuação dos ajustes operacionais em 06/10/2026, na branch `feat/operational-usability`. O administrador passa a manter a cobertura pública em **Cadastros → Regiões de entrega** (`/equipe/regioes-entrega`). Este incremento não ativa bairros ou taxas fictícios.

## Uso e regras

Cadastrar bairro, cidade e UF; revisar e confirmar antes de salvar. Desde 07/10/2026, `PublicDelivery:FixedFee=5.00` estabelece **R$ 5,00 para qualquer região atendida**, conforme informado pelo responsável. O valor aparece preenchido, sem edição por bairro; retirada permanece grátis. Se `FixedFee` for ausente/nulo em outro ambiente, a tela conserva o cadastro de taxa explícita de R$ 0 a R$ 9.999,99 com até duas casas decimais, começando vazio. Bairro/cidade têm até 80 caracteres; UF deve existir. São permitidas até 500 regiões, incluindo pausadas, sem duplicar bairro/cidade/UF. A comparação ignora espaços nas extremidades e maiúsculas/minúsculas.

A lista permite busca, filtro de situação, edição, pausa e reativação. Pausar remove a região das novas compras pelo site. Não há exclusão: o identificador permanece estável. Mudanças afetam revisões e pedidos públicos novos; pedidos registrados mantêm endereço e taxa históricos. O carrinho manual da equipe conserva suas regras de taxa informada pelo funcionário.

Nenhuma região ativa significa retirada disponível e entrega pública indisponível, mesmo com taxa fixa configurada. Não há reinício da API após salvar pela tela. O responsável confirmou atendimento em **todos os bairros de Corinto–MG**. A configuração inicial contém essa cobertura; o endereço de retirada é **Rua Esperança, 68 — Clarindo de Paiva, Corinto–MG**. A ferramenta não estima distância, CEP, prazo ou capacidade de entrega.

Marcar **Todos os bairros desta cidade** define `coversAllNeighborhoods=true` e dispensa um nome de bairro no cadastro. O checkout solicita o bairro real do cliente e o preserva no pedido; o rótulo “Todos os bairros” descreve a cobertura. Editar, pausar e reativar mantêm essa opção. Regiões anteriores conservam a cobertura de um bairro específico.

## Persistência e compatibilidade

A migration `20261006174915_AddDeliverySettings` cria somente `DeliverySettings`, com uma linha de configuração (`Id=1`), versão incremental, lista JSONB limitada a 500 regiões e autor/data da última alteração. A lista pequena e salva como conjunto usa uma única revisão; não exige entidades de entregador, rotas ou relacionamentos com pedidos. Os pedidos já têm suas cópias históricas. O autor é uma cópia do identificador/nome, sem dependência de exclusão do funcionário. Não é um histórico completo de todas as alterações de configuração.

Enquanto não existir configuração salva, `PublicDelivery:Areas` continua sendo lida como cobertura inicial, sem escrita automática no banco. O primeiro salvamento persiste toda a lista revisada. Depois dele, o banco passa a ser a fonte da cobertura, mesmo que todas as regiões estejam pausadas; a configuração antiga não as reativa. A validação inicial de `PublicDelivery` permanece, portanto a configuração legada ainda deve ser válida (pode ser `Areas: []`). Antes do primeiro salvamento, múltiplas instâncias devem usar a mesma configuração inicial.

O Down recusa apagar uma configuração já salva. Exportar e migrar os dados antes de uma reversão planejada; não apagar a linha para contornar a proteção. Aplicação e backup: [database/README.md](../database/README.md).

## API e concorrência

`GET /api/delivery-settings` e `PUT /api/delivery-settings` exigem JWT e `delivery.manage`, exclusiva de `Administrator`. Atendente, cozinha e expedição recebem 403; sem sessão, 401. Respostas usam `no-store` e o corpo de entrada tem limite de 524.288 bytes. A resposta pública de regiões conserva seu contrato e só inclui regiões ativas, sem autor ou revisão administrativa.

GET retorna `{ areas, revision, updatedAt, updatedBy, fixedFee }`. `fixedFee` é nulo quando não existe política de taxa única; quando configurado, prevalece sobre as taxas legadas ou salvas na resposta. Cada região contém `{ id, neighborhood, city, state, fee, isActive, coversAllNeighborhoods }`. A opção de toda a cidade começa falsa nas regiões anteriores e é preservada na lista JSONB existente, sem nova migration. PUT recebe `{ expectedRevision, areas }`, sempre com a lista completa; valores diferentes da taxa fixa retornam `400 FixedDeliveryFeeRequired`. `id` tem até 60 caracteres, em letras minúsculas, números e hífen; a interface gera o ID de novas regiões e preserva os existentes. Campos desconhecidos e listas inválidas retornam 400.

A revisão combina versão, conteúdo normalizado e a política de taxa fixa, inclusive quando a lista estiver vazia. Uma edição desatualizada retorna `409 DeliverySettingsChanged`; remover um ID existente retorna `409 DeliveryAreaRemovalDenied`. A tela conserva os campos e exige recarregar antes de nova edição. Não mescla automaticamente alterações de administradores diferentes.

Se a resposta de salvamento se perder, a tela mantém o mesmo corpo e bloqueia edição enquanto oferece repetição ou nova consulta. Se aquele conteúdo já for o atual, a API responde 200 sem incrementar versão/data. Se outra edição tiver sido salva, a revisão antiga é recusada. Recarregar explicitamente descarta a edição local. Não há rascunho persistente para esse formulário administrativo.

A escrita revalida usuário, sessão e perfil dentro da transação e usa bloqueio exclusivo de cobertura no PostgreSQL. Criar uma nova entrega pública usa o bloqueio compartilhado correspondente até o commit, evitando alteração da taxa entre a conferência final e a gravação. Repetir um pedido já registrado recupera seu comprovante antes de consultar a cobertura atual. Alterações de taxa exigem nova revisão (`OrderReviewChanged`); região pausada é recusada (`PublicDeliveryUnavailable`). Nenhum cliente/pedido parcial permanece nessas recusas.

## Validação

Testes reais da API usam PostgreSQL temporário exclusivo; testes de navegador usam API simulada. Resultados e limitações em [validation.md](validation.md).

1. Entrar novamente como administrador para carregar `delivery.manage`; abrir **Cadastros → Regiões de entrega**.
2. Conferir a lista existente. Cadastrar somente um bairro realmente atendido e sua taxa aprovada; revisar antes de confirmar.
3. Conferir o bairro no cardápio/checkout e o cálculo da taxa na revisão, sem precisar enviar uma compra fictícia na operação.
4. Em ambiente de teste, abrir a tela em duas sessões e tentar salvar alterações diferentes: a segunda deve exigir recarga.
5. Em teste, revisar uma entrega, alterar a taxa e tentar enviar: deve exigir nova revisão. Pausar a região deve bloquear novas compras, preservando pedidos anteriores.
6. Simular falha de conexão em teste: repetir o mesmo salvamento deve recuperar o resultado sem duplicar região; durante o envio, ações ficam bloqueadas.
7. Repetir em computador e celular. Perfis de atendente, cozinha e expedição não devem ver a opção nem acessar sua API.

Arquivos principais: `DeliverySettingsController`, `DeliverySettingsService`, DTOs/Delivery, entidade/configuração/migration `DeliverySettings`, política `delivery.manage`, integração com `PublicCheckoutService`, `features/delivery/delivery-settings.page.*`, `delivery-settings-api.service.ts`, `DeliverySettingsTests.cs` e `e2e/delivery-settings.spec.ts`.
