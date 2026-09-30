# Fluxo de trabalho com Git

## Branches por incremento

A `master` recebe incrementos testados e validados. Cada funcionalidade, correção ou manutenção começa em uma branch curta, criada a partir da `master` atualizada. Não usamos uma branch `develop` neste momento.

| Prefixo | Uso | Exemplo |
| --- | --- | --- |
| `feat/` | Funcionalidade | `feat/customers-addresses` |
| `fix/` | Correção | `fix/recipe-validation` |
| `chore/` | Ferramentas e manutenção | `chore/dependency-updates` |
| `docs/` | Documentação | `docs/local-setup` |

Uma branch representa um incremento que pode ser revisado e validado em conjunto. Não é necessário criar uma branch por arquivo ou por ajuste pequeno da mesma atividade.

## Trabalho existente em 30/09/2026

As fichas técnicas e a manutenção já estavam juntas no diretório, sem commit. Foram preservadas na branch `feat/recipes-and-maintenance`, baseada no commit `780df71` da `master`, e registradas no commit `1e151c3`. Esse agrupamento preserva o estado que passou pelos testes; os próximos incrementos começam em branches próprias.

A ficha técnica ainda depende de aceite manual antes da integração. Criar a branch e fazer commits locais não publica o trabalho no GitHub nem altera a `master`.

## Iniciar a próxima atividade

Depois da integração do incremento anterior, confira que não há alterações pendentes com `git status`. Se houver, registre-as na branch da atividade correspondente antes de trocar de branch.

Na raiz do repositório:

```powershell
git switch master
git pull --ff-only origin master
git switch -c feat/customers-addresses
```

Se o pull indicar divergência, examine o histórico antes de continuar; não use reset ou force push para resolver automaticamente.

## Implementar e registrar

Faça commits pequenos, com uma finalidade clara. Revise os arquivos antes de adicioná-los e confira o conteúdo preparado com `git diff --cached`. Evite juntar outra funcionalidade à mesma branch.

Exemplos de mensagens:

- `feat: adiciona cadastro de clientes`
- `fix: valida telefone duplicado`
- `docs: explica configuracao local`

Antes de publicar, confira a identidade local e o destino:

```powershell
git config --local user.name
git config --local user.email
git remote -v
git status
```

A identidade de autoria e a conta autenticada no GitHub são configurações diferentes. Em outra máquina, ambas precisam ser conferidas.

## Publicar e revisar

Na primeira publicação, use o nome da branch em que está trabalhando. Para o incremento atual:

```powershell
git push -u origin feat/recipes-and-maintenance
```

Abra um pull request com destino à `master`. Pode ser um rascunho enquanto a validação manual estiver pendente. Use o modelo do repositório para informar o resultado, os testes e eventuais migrations.

O workflow `Quality` executa os jobs `backend` e `frontend` nos pull requests e nos pushes para `master`. Um push de branch sem pull request não dispara esse workflow. A execução no GitHub depende de os arquivos terem sido publicados e de o Actions estar disponível no repositório.

Execute localmente as verificações apropriadas ao incremento. Para a validação completa, com as dependências instaladas:

```powershell
powershell -NoProfile -File scripts/Test-Quality.ps1
```

Integre somente depois dos testes aplicáveis e do aceite manual do incremento. Confira o diff final e use **Squash and merge** para manter uma entrada por incremento na `master`. Depois da integração, a branch remota pode ser excluída pelo GitHub. A próxima atividade começa novamente pela `master` atualizada.

## Proteção da master

Política desejada no GitHub: exigir pull request e aprovação dos checks `backend` e `frontend`, bloquear force push e exclusão da `master`. O aceite funcional continua sendo registrado no pull request; não depende de existir um segundo desenvolvedor para aprovar a revisão.

Essas regras são configurações do GitHub. Este documento e o workflow não ativam a proteção automaticamente. A configuração remota deve ser conferida separadamente, conforme as permissões e os recursos disponíveis na conta.

## Banco e outra máquina

Branches versionam código e migrations; não isolam bancos nem desfazem migrations já aplicadas. Não execute migrations só porque trocou de branch. Confira o banco de destino e a compatibilidade do esquema primeiro.

Na outra máquina, autentique no GitHub, obtenha a branch publicada e siga o [README](../README.md) e o [guia do banco](../database/README.md). User Secrets, senhas, chave JWT, cadastros e backups não são transferidos pelo Git. O banco `parsmartmanager` não faz parte deste projeto.
