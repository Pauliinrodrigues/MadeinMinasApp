# Preparação e homologação da primeira versão

Atualização de 28/10/2026. Este registro acompanha a execução do [levantamento de prontidão](v1-readiness.md). O código está preparado para homologação; a publicação externa depende do ambiente de hospedagem e do aceite operacional. O incremento segue na branch `feat/v1-readiness-environments`, com revisão e checks antes da integração na `master`. Não houve implantação em produção.

Os três ambientes principais foram definidos como Local (`Development`), Teste/Homologação (`Staging`) e Produção (`Production`). Builds, configuração, isolamento e promoção estão em [ambientes](environments.md). Os laboratórios de IA existentes continuam separados da homologação.

## Entregas do incremento

| Etapa | Implementação | Dependência de aceite |
| --- | --- | --- |
| Base técnica | Zod 4.6.6, Angular CLI/build 22.2.4, Vite 8.3.3 e fast-uri 3.1.9; auditoria de dependências altas/críticas na CI. Health check verifica migrations e `/api/system/status` identifica o build. | Manter as verificações da release aprovadas. |
| Ambiente local | Backup, restauração e atualização ensaiados em PostgreSQL isolado; schema comparado com uma instalação nova após o mesmo ciclo de exportação/restauração. API principal atualizada, com histórico e dados anteriores preservados. | O PostgreSQL local compartilhado continua na série 17.4; planejar sua atualização sem interromper o outro sistema. A hospedagem deve usar uma versão corrigida da série suportada. |
| Operação comercial | Checkout e montagem no chat recebem preferência de cartão, dinheiro ou Pix e valor para troco. Backend valida, assina a revisão e persiste a escolha; expedição, detalhe e comandas exibem os dados. Escolher pagamento não registra recebimento. | Conferir cardápio, fichas e contagem física com a loja. |
| Horários | Segunda a quinta 18:30–23:30; sexta, sábado e domingo 18:30–23:59. A decisão de manter 23:59 foi aplicada aos padrões e aos testes. | Conferir pausa e abertura com a operação. |
| Fluxo e apresentação | Cabeçalhos menores no celular, links de ajuda separados, aviso sonoro compacto com ajuda expansível, recuperação de pagamento rejeitado, textos de acompanhamento corrigidos e página `/privacidade`. | Aceite em celular físico e revisão dos textos pelo responsável. |
| Continuidade | Configuração de proxy confiável e chaves persistentes protegidas; modelos Nginx/systemd, backup local de banco/fotos/chaves, empacotamento da aplicação e agente Windows com runtime incluído. | Domínio, hospedagem, cópia externa, agendamento/alertas e teste de recuperação no host escolhido. |
| IA | Campos de pagamento e seus limites integrados às ferramentas; só aceita escolha/troco com origem em mensagens do visitante. Avaliadores atualizados com pagamento explícito. A IA principal permanece desligada. | Provedor, público, dados, cotas e atendimento humano precisam atender aos critérios R13–R16 antes da exposição pública. |

Os cartões de pedidos no celular já existiam no código. A observação anterior de tabela móvel estava ligada ao ambiente antigo; essa funcionalidade foi preservada. Renovação da sessão da equipe, navegação móvel completa, avisos globais de intervenção e conciliação de caixa continuam no backlog P1. Combos, cupons, cobrança bancária, WhatsApp e entregadores continuam fora deste incremento.

## Pagamento solicitado e recebido

Uma revisão pública nova exige `paymentPreference`: `Card`, `Cash` ou `Pix`, conforme os métodos aceitos pela loja. `changeFor` é opcional para dinheiro e representa o valor que o cliente pretende entregar. Deve cobrir o total, ser positivo e ter até duas casas decimais. Não é um desconto nem um valor recebido.

```json
{
  "name": "Cliente fictício",
  "phone": "38999992468",
  "paymentPreference": "Cash",
  "changeFor": 100,
  "cart": {
    "items": [{ "productId": "ID_REAL_DO_CARDAPIO", "quantity": 1, "notes": null }],
    "notes": null
  }
}
```

O exemplo é ilustrativo: ID, preço, taxa e disponibilidade vêm do backend. Novos envios sem escolha são rejeitados; reenvios idênticos de pedidos antigos já criados continuam recuperáveis sem criar outra compra. Alterar pagamento/troco exige outra revisão e confirmação. A migration `AddRequestedPayment` adiciona campos opcionais aos registros anteriores e impede downgrade que descartaria preferências já armazenadas.

No chat, `paymentPreference=null` preserva a escolha atual. Ao definir o bloco de pagamento em dinheiro, informar também o `changeFor` desejado; nesse bloco, `null` indica que não foi solicitado troco. A equipe registra o dinheiro efetivamente entregue apenas no recebimento. Os testes do agente utilizam provedor simulado e dados fictícios.

## Atualização local do banco

O ensaio usa o cluster isolado já preparado pelo piloto em `127.0.0.1:55435`, sem chamar o modelo. As credenciais permanecem em `.local/ai-pilot/pilot.local.json`; não compartilhar esse arquivo. O banco operacional `made_in_minas` em `5432` é somente lido pelo ensaio.

```powershell
powershell -NoProfile -File scripts/Test-DatabaseUpgrade.ps1
```

O script preserva backup, clone atualizado, projeções de verificação e resultado em `.local/database-upgrade`. Compara contagem e digest das colunas anteriores, além de colunas, constraints e índices. A comparação de schema restaura também a referência: o `pg_dump` pode representar coerções de tipos de outra forma, sem mudar a regra. Nenhuma constraint é ignorada para obter aprovação.

O histórico legado `20261003145937_AddCustomerChat` foi mantido. A atualização local operacional só ocorreu depois do ensaio aprovado, com outro backup e comparação dos dados antes/depois. A API executa `AddRequestedPayment` como última migration deste incremento.

## Backup e recuperação

```powershell
powershell -NoProfile -File scripts/Backup-LocalSystem.ps1
```

O backup inclui `database.dump`, fotos e chaves Data Protection, com hash e manifesto. Só produz o marcador de sucesso depois de copiar todas as partes. Não copia o User Secrets inteiro nem remove backups anteriores. Credenciais e certificado privado devem ter cópia protegida no gerenciador escolhido para a implantação.

Listar o conteúdo de um dump não comprova restauração. O script de ensaio restaura o banco em outro destino; a recuperação de fotos, chaves e serviço também deve ser demonstrada no host. Definir com a loja frequência, retenção, cópia fora do computador, alerta de falha, perda máxima aceitável e tempo de retorno. O agendamento de produção depende desse destino; o backup local feito nesta atividade não substitui essa rotina.

Em Windows, chaves protegidas com DPAPI dependem da conta/máquina. Copiar apenas o XML para Linux não transfere essa capacidade. Planejar a migração antes de trocar de ambiente; na hospedagem nova, usar o certificado e o armazenamento persistente desde o início. [Proteção de chaves em repouso](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/implementation/key-encryption-at-rest?view=aspnetcore-10.0).

## Preparação do artefato

```powershell
powershell -NoProfile -File scripts/Publish-Release.ps1 -Version 0.1.0-preview.20261028.4 -Environment Staging
powershell -NoProfile -File scripts/Publish-Release.ps1 -Version 0.1.0-preview.20261028.4 -Environment Production
powershell -NoProfile -File scripts/Publish-PrintAgent.ps1 -Version 0.1.0-preview.20261028.2
```

Os comandos geram artefatos em `.local/releases`, com versão, hash e logs. Não instalam serviço, executam migration, publicam, criam tag ou enviam Git. O manifesto da aplicação informa o commit-base e se há mudanças ainda sem commit. O projeto exclui configurações locais, segredos e `App_Data` da publicação.

O agente `win-x64` inclui runtime; a verificação gera imagem sem enviar papel. Configurar a estação pelo fluxo existente de impressão, preservando a pasta de estado entre atualizações. Não distribuir configuração contendo token bruto junto ao executável.

Nesta máquina, a tarefa de login já está instalada e habilitada, apontando para o pacote `print-agent-0.1.0-preview.20261028.1` e a pasta de estado existente. O pacote `.2` também foi validado: ele confirma que o empacotamento gera seu próprio lock de dependências sem modificar o lock de desenvolvimento. Não foi reiniciado o Windows nem disparada impressão física nesta validação.

```powershell
powershell -NoProfile -File scripts/Install-PrintAgentStartup.ps1 `
  -AgentPath C:\caminho\MadeInMinas.PrintAgent.exe `
  -StorePath C:\caminho\station -WhatIf
```

Retirar `-WhatIf` instala a tarefa para o login do usuário atual, com execução limitada e janela oculta. Deve ser o mesmo usuário que protegeu a credencial com DPAPI. `-Remove` remove só a tarefa; mantém credencial e fila. Reiniciar o computador e verificar reconexão e papel ainda faz parte do aceite físico. O iniciador de desenvolvimento deixou de recompilar a cada execução; `-Rebuild` solicita uma nova publicação.

## Hospedagem e monitoramento

Os arquivos [Nginx](../deploy/nginx.conf.example), [serviço systemd](../deploy/made-in-minas.service.example) e [variáveis](../deploy/api.env.example) são um modelo para Linux, a adaptar depois de definir a hospedagem. Não foram instalados em servidor externo.

1. Definir domínio, TLS, host, conta de serviço e PostgreSQL com permissões próprias. Manter banco e porta interna da API fora do acesso público.
2. Preparar `/var/lib/made-in-minas` para imagens e chaves; dar escrita apenas à conta de serviço. Proteger arquivo de ambiente e PFX com permissões mínimas.
3. Fornecer conexão, JWT, certificado e senha por segredo do host. Em Linux, a API recusa iniciar sem a persistência/proteção das chaves; desenvolvimento e testes mantêm seu fluxo próprio.
4. Publicar frontend e API do mesmo artefato. Substituir o domínio de exemplo, configurar HTTPS e testar recarregamento de `/pedido`, `/privacidade`, acompanhamento e equipe.
5. Fazer backup e ensaiar migrations no banco restaurado. Aplicar o script revisado de migration com conta autorizada, sem apagar o histórico legado.
6. Conferir `/health/ready`, `/api/system/status`, fotos, permissões, pedidos e recuperação. O liveness não substitui o readiness.
7. Configurar reinício e alertas para API, backup atrasado/falho, agente desconectado e filas paradas. Logs não devem conter corpo de mensagem, telefone, endereço, tokens ou conexão.
8. Guardar o artefato anterior e planejar retorno compatível com o schema. Não executar downgrade que descarte novos dados de pedido/IA.

O modelo encaminha somente IP e protocolo, de um proxy explicitamente confiável, antes do redirecionamento HTTPS. Não confiar indiscriminadamente em cabeçalhos enviados pelo cliente. Se houver CDN ou outros proxies, revisar a cadeia conforme o host. [Orientação ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0).

## Pendências para liberação

- Responsável conferir catálogo, receitas, saldos, pausa da loja e impressão de entrega/retirada em 80 mm.
- Informar domínio/hospedagem e contato comercial. Por enquanto o contato público usa o chat existente; não foi inventado telefone ou WhatsApp.
- Aprovar rotina de backup, monitoramento, recuperação, retenção de dados e responsáveis pelo atendimento. A página pública explica o funcionamento atual; não promete exclusão automática nem substitui a definição dessa política.
- Homologar equipamentos, reinício do Windows, perda de rede, fila em revisão, duas pessoas operando e cancelamento/devolução.
- Para IA pública, resolver R13–R16 do levantamento. O plano gratuito não deve receber dados pessoais reais sem adequação às condições do serviço. O piloto deve continuar com dados fictícios. [Termos Gemini](https://ai.google.dev/gemini-api/terms).

## Registro de validação

Validações executadas em 27 e 28/10/2026. Artefatos de teste, dumps, capturas, credenciais e logs ficam em `.local`, fora do Git. Um teste automatizado ou imagem de comanda não equivale a teste físico da impressora ou aceite da hospedagem.

| Verificação | Resultado e evidência local |
| --- | --- |
| Backend completo e correções | A execução completa terminou com 765 aprovações e duas falhas em dados de testes novos (`Int32` em parâmetro `double?`). Corrigidos os literais, os cinco casos desse grupo passaram. Quatro casos adicionais de origem do pagamento no chat também passaram. São 771 cenários únicos cobertos pelas execuções combinadas; não foi realizada outra execução completa dos 771 após as correções. Logs: `v1-final-backend-tests.log`, `v1-payment-corrected-tests.log`, `v1-ai-payment-tests.log`. |
| Interface completa e correções | Primeira execução: 626 aprovações em 694 cenários. Após corrigir fixtures do contrato/horário e repetir os casos afetados, passaram 66 dos 68; os dois restantes identificaram formato monetário incorreto no componente novo. Corrigido para o formatador brasileiro já usado pelo sistema, esses dois e quatro cenários novos passaram. São 698 cenários únicos cobertos em conjunto, sem nova execução completa dos 698. Logs: `v1-final-e2e.log`, `v1-e2e-corrected.log`, `v1-new-ui-tests.log`. |
| Avaliador da IA | 40/40 testes aprovados, sem chamadas pagas ao provedor. Log: `v1-evaluator-tests.log`. |
| Dependências e qualidade | Auditoria npm sem vulnerabilidades sinalizadas, incluindo desenvolvimento; consulta NuGet da API sem pacotes sinalizados. Lint, Prettier, verificações de formato .NET usadas pela CI e builds de produção aprovados. A CI exclui migrations geradas da verificação de formato, como já ocorria antes deste incremento; a checagem mais ampla ainda aponta codificação/finais de linha em migrations históricas. O arquivo novo foi normalizado sem mudar o SQL. A ausência de alertas não comprova ausência de vulnerabilidades. |
| Restauração e schema | Clone atualizado, comparação dos dados anteriores aprovada e nenhuma diferença nas definições de schema comparadas. Resultado: `database-upgrade/result.json`. A atualização operacional preservou os dados e o histórico legado: `v1-runtime/20261028-093829/result.json`. |
| Fluxo integrado real em banco fictício | Revisão, criação, repetição sem duplicação, aceite/estoque, cozinha, expedição, bloqueio da finalização antes de receber, recebimento manual idempotente e acompanhamento final aprovados. Não criou pedidos operacionais, chamou IA ou enviou papel. Resultado: `v1-live/result.json`. |
| Revisão das telas | 30 combinações de tela/dispositivo visitadas; nenhum erro JavaScript, falha de API ou transbordamento horizontal encontrado. Escritas operacionais bloqueadas durante a visita. Resumo: `v1-audit-final/summary.json`. Não substitui avaliação por leitor de tela nem teste físico. |
| Artefatos | Aplicação `0.1.0-preview.20261028.1` e agente `0.1.0-preview.20261028.2` gerados com versão e hash. API publicada localmente sem configurações Development/Local, User Secrets ou fotos operacionais. Agente com runtime e verificação de imagem aprovados, sem papel. Manifestos em `releases`. |
| Estado local final | APIs principal, piloto e ensaio saudáveis; agente com heartbeat recente e nenhum trabalho em revisão na consulta final. Tarefa de login habilitada, em estado `Ready`. O frontend principal continua em `http://localhost:8101`; a IA principal continua desativada. |

A execução interrompida pela suspensão do computador foi descartada. Os totais acima usam a execução posterior e suas repetições direcionadas. Antes de criar uma release definitiva, executar novamente os checks completos na CI sobre o commit que será publicado.

## Próximo aceite

1. Conferir cardápio, ficha técnica e estoque físico; hoje há um produto ativo no banco operacional. Nenhum cadastro comercial foi inventado para preencher o catálogo.
2. Fazer um pedido de retirada e outro de entrega com a equipe, testando pagamento, troco, cancelamento, impressão de 80 mm e retomada após reiniciar o computador.
3. Definir hospedagem/domínio, responsável operacional e destino externo dos backups; adaptar os modelos e ensaiar recuperação nesse destino antes da publicação.
4. Concluir os acabamentos P1 ainda abertos: acesso aos produtos no primeiro trecho do cardápio móvel, navegação da equipe, continuidade da sessão e avisos globais de atendimento humano. As reduções de espaçamento entregues são parciais para esses critérios.
5. Liberar IA somente depois do aceite específico R13–R16, mantendo testes com dados fictícios até essa decisão.
