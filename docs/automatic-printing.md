# Impressão automática na Diebold

O administrador configura a estação em **Equipe → Gestão → Impressão automática** (`/equipe/impressao`). A estação Windows imprime sem abrir o navegador: usa `PrintDocument`, `StandardPrintController` e o driver instalado, com **Diebold Procomp IM453HU_A**, retrato, margens zero e **IM4X3T/TSP143 76/80x500 mm** em cada trabalho. Não altera a impressora padrão nem preferências de outros aplicativos. A seleção da bobina de 500 mm evita reutilizar o padrão de 3.000 mm encontrado nesta instalação. A saída física deste novo caminho precisa ser conferida no equipamento; o diálogo nativo já foi validado pelo responsável.

## Comportamento

- Ao confirmar um pedido, enfileira a via de produção; ao mudar de Em preparação para Pronto, enfileira a via de expedição. A criação do envio integra a transação da mudança de status. Repetir a mesma transição não cria outra comanda.
- Ativar inclui apenas eventos posteriores à ativação. Não imprime pedidos antigos. Desativar impede novos envios automáticos; os pendentes continuam na fila e podem ser cancelados individualmente.
- **Enviar à impressora**, na prévia, registra uma cópia manual. A estação pode atender esse botão com a automação desativada. Depois do envio, outra cópia exige **Preparar outra cópia**. Após erro de comunicação, tentar o mesmo envio mantém seu identificador, evitando uma segunda inclusão.
- Cozinha recebe produtos, quantidades, observações, modalidade, situação, versão e data. Não recebe cliente, telefone, endereço, preços ou pagamento. Expedição inclui as cópias históricas do cliente/endereço, valores e o pagamento ativo no momento em que o envio foi preparado. Ausência de pagamento é sinalizada para conferência.
- O conteúdo fica registrado na fila; mudanças posteriores no cadastro não reescrevem a comanda. Pedidos curtos começam no topo; textos longos quebram linhas e pedidos acima da área imprimível continuam em outras páginas.
- Cancelar um pedido cancela os envios ainda aguardando impressão. Um trabalho já entregue ao agente/spooler exige conferência e comunicação à cozinha; o sistema não recolhe papel impresso.

## Instalar na estação

Requisitos: Windows, SDK .NET 10 para compilar, driver Diebold instalado e API acessível por HTTPS, ou HTTP apenas em localhost. O agente é um projeto Windows separado da solução/API Linux.

1. Como administrador, baixar a configuração pela tela. Substituir uma estação exige confirmação explícita na própria tela, revoga a credencial anterior e desativa a automação.
2. Na raiz do projeto, executar:

   ```powershell
   powershell -NoProfile -File scripts/Start-PrintAgent.ps1 -ConfigurationFile "$env:USERPROFILE\Downloads\MadeInMinas-printer.json"
   ```

3. O iniciador compila e abre o agente em segundo plano. A credencial é protegida pelo DPAPI para o usuário Windows atual em `.local/print-agent/station/configuration.json`. Remover o arquivo baixado depois de instalar: ele contém a credencial original. Não versionar nem compartilhar esses arquivos.
4. Para um teste físico curto, sem pedido:

   ```powershell
   .\.local\print-agent\app\MadeInMinas.PrintAgent.exe --test-print --store .local/print-agent/station
   ```

5. Confira início, final, acentos e avanço do papel. Quando a tela mostrar agente conectado, ativar a automação. A ativação exige conexão recente no backend. O navegador pode ser fechado; a API e o agente precisam continuar executando.

Para reiniciar o agente depois de encerrar o Windows, executar o iniciador novamente. Para acompanhar no terminal, usar `-Foreground`. Logs e PID ficam em `.local/print-agent`; os logs registram somente identificadores de envios, sem dados do cliente ou credenciais. Para atualizar o executável, encerrar somente esse agente pelo PID registrado antes de recompilar.

O operador local que já dispõe de User Secrets e das credenciais do banco pode fazer o cadastro inicial sem login pelo navegador:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --project backend/MadeInMinas.Api -- --create-print-station
powershell -NoProfile -File scripts/Start-PrintAgent.ps1 -ConfigurationFile .local/print-agent/registration.json
```

O comando aceita exclusivamente `made_in_minas`, usuário `made_in_minas_app`, localhost:5432 e ambiente Development; recusa substituir estação/arquivo existentes. A automação continua desativada. Depois de instalar, remover `registration.json`. Não usar esse procedimento para administração remota.

## Fila e recuperação

`Queued` = aguardando; `Claimed` = em envio; `Submitted` = entregue ao spooler Windows; `Review` = resultado incerto, conferir na impressora; `Cancelled` = envio cancelado antes do processamento. A tela apresenta os últimos 100 envios. **Submitted não comprova impressão física**: falta de papel, impressora desconectada e outras condições são tratadas pelo Windows/driver.

Só um envio fica em andamento na estação. Cada retirada tem um identificador de posse. O agente mantém um pequeno registro local antes de chamar a impressora. Se reiniciar entre a chamada e sua confirmação, informa revisão; não imprime de novo. Depois de enviar ao spooler, pode repetir apenas a confirmação HTTP. Um envio sem confirmação por dois minutos passa a revisão e não retorna automaticamente à fila. Antes de solicitar outra cópia, confira o papel e os trabalhos pendentes no Windows.

O backend guarda apenas SHA-256 da credencial aleatória de 256 bits. O agente usa `X-Print-Key` somente nas rotas de retirada e confirmação; essa chave não permite alterar pedidos, pagamentos ou configurações. Funcionários usam suas permissões existentes de impressão; `printing.manage` fica restrita ao administrador. Rotas do agente têm limite por IP. Não há servidor HTTP local, credenciais em URL, alteração global de políticas do Chrome ou exposição da impressora na internet.

## Migration e validação

`20261008035011_AddPrintQueue` cria `PrintStations`, inicialmente desativada e sem credencial, e `PrintJobs`, com chave única de solicitação, índice da fila e vínculo com o pedido. Não cria pedidos nem movimenta estoque. Revisar SQL, confirmar o banco e criar backup antes de aplicar em outra instalação. O Down recusa eliminar histórico ou estação cadastrada.

Testes: `PrintQueueTests` no PostgreSQL isolado; `printing-automation.spec.ts` para configuração, permissões e idempotência do botão; regressões de pedidos/cozinha/expedição e PDFs do navegador. O agente suporta `--verify caminho.png`, sem imprimir, para conferir texto longo, acentos e paginação. Resultados da instalação local em [validation.md](validation.md).

Referências: [PrintDocument](https://learn.microsoft.com/en-us/dotnet/api/system.drawing.printing.printdocument?view=windowsdesktop-10.0) e [StandardPrintController](https://learn.microsoft.com/en-us/dotnet/api/system.drawing.printing.standardprintcontroller?view=windowsdesktop-10.0).
