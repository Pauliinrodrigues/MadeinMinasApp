import { readFile, mkdir, writeFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { fileURLToPath } from 'node:url';
import { parseArgs } from 'node:util';
import { selectCases } from './ai-evaluation/cases.mjs';
import { EvaluationError, readUsage, runEvaluation } from './ai-evaluation/runner.mjs';

async function main() {
  const { values } = parseArgs({
    options: {
      live: { type: 'boolean', default: false },
      list: { type: 'boolean', default: false },
      cases: { type: 'string' },
      'max-turns': { type: 'string', default: '16' },
    },
  });
  const selected = selectCases(values.cases);
  if (!values.live || values.list) {
    for (const item of selected)
      console.log(`${item.id} (${item.steps.length} turno(s)): ${item.purpose}`);
    console.log(
      'Sem chamadas de IA. Execute com --live para avaliar somente o piloto local 5082 com dados fictícios.',
    );
    return;
  }
  const root = new URL('../.local/ai-pilot/', import.meta.url);
  const config = JSON.parse(
    (await readFile(new URL('pilot.local.json', root), 'utf8')).replace(/^\uFEFF/, ''),
  );
  console.log(
    `Avaliando ${selected.length} cenários fictícios. Nenhuma confirmação de pedido será enviada.`,
  );
  const revision = createHash('sha256');
  for (const path of [
    './ai-evaluation/cases.mjs',
    './ai-evaluation/runner.mjs',
    '../backend/MadeInMinas.Api/Services/Ai/AiService.cs',
    '../backend/MadeInMinas.Api/Services/Ai/AiToolCatalog.cs',
  ])
    revision.update(await readFile(new URL(path, import.meta.url)));
  const suiteSha256 = revision.digest('hex');
  const { report, conversationCases } = await runEvaluation({
    selected,
    config,
    maxTurns: Number(values['max-turns']),
    onProgress: ({ id, status }) => console.log(`${status}: ${id}`),
  });
  report.suiteSha256 = suiteSha256;
  try {
    report.usage = readUsage(
      await readFile(new URL('api.log', root), 'utf8'),
      conversationCases,
      report.latency.completedTurns,
    );
  } catch {
    report.usage = { source: 'backend-log', complete: false };
  }
  const directory = new URL('../.local/ai-evaluation/', import.meta.url);
  await mkdir(directory, { recursive: true });
  const target = new URL(`evaluation-${report.startedAt.replace(/[:.]/g, '-')}.json`, directory);
  await writeFile(target, JSON.stringify(report, null, 2) + '\n', 'utf8');
  console.log(JSON.stringify(report.summary));
  console.log(`Relatório: ${fileURLToPath(target)}`);
  if (report.summary.failed || report.summary.blocked || report.summary.skipped)
    process.exitCode = 1;
}

main().catch((error) => {
  // Never echo arbitrary HTTP, filesystem or credential-bearing error messages.
  const code = error instanceof EvaluationError ? error.code : 'EvaluationSetupFailed';
  console.error(
    `Avaliação interrompida: ${code}. Confira o piloto e os parâmetros, sem compartilhar segredos.`,
  );
  process.exitCode = 1;
});
