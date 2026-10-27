import { readFile, mkdir, writeFile } from "node:fs/promises";
import { parseArgs } from "node:util";
import { createHash } from "node:crypto";
import { catalogDigest } from "./ai-evaluation/catalog.mjs";
import { selectRehearsalCases } from "./ai-evaluation/rehearsal-cases.mjs";
import {
  EvaluationError,
  runEvaluation,
  readUsage,
} from "./ai-evaluation/runner.mjs";

async function main() {
  const { values } = parseArgs({
    options: {
      live: { type: "boolean", default: false },
      cases: { type: "string" },
    },
  });
  const directory = new URL("../.local/ai-rehearsal/", import.meta.url);
  const snapshot = JSON.parse(
    await readFile(new URL("catalog.snapshot.json", directory), "utf8"),
  );
  const selected = selectRehearsalCases(snapshot, values.cases);
  if (!values.live) {
    for (const item of selected) console.log(`${item.id}: ${item.purpose}`);
    console.log("Sem chamadas de IA; use --live para o ensaio isolado 5083.");
    return;
  }
  const config = JSON.parse(
    (await readFile(new URL("pilot.local.json", directory), "utf8")).replace(
      /^\uFEFF/,
      "",
    ),
  );
  const revision = createHash("sha256");
  for (const path of [
    "./ai-evaluation/rehearsal-cases.mjs",
    "./ai-evaluation/runner.mjs",
    "../backend/MadeInMinas.Api/Services/Ai/AiService.cs",
    "../backend/MadeInMinas.Api/Services/Ai/AiToolCatalog.cs",
  ])
    revision.update(await readFile(new URL(path, import.meta.url)));
  const suiteSha256 = revision.digest("hex");
  const { report, conversationCases } = await runEvaluation({
    selected,
    config,
    profile: "Rehearsal",
    snapshot,
    onProgress: ({ id, status }) => console.log(`${status}: ${id}`),
  });
  report.catalogSha256 = catalogDigest(snapshot);
  report.catalogCapturedAt = snapshot.capturedAt;
  report.suiteSha256 = suiteSha256;
  try {
    report.usage = readUsage(
      await readFile(new URL("api.log", directory), "utf8"),
      conversationCases,
      report.latency.completedTurns,
    );
  } catch {
    report.usage = { complete: false, source: "backend-log" };
  }
  const results = new URL("results/", directory);
  await mkdir(results, { recursive: true });
  await writeFile(
    new URL(
      `evaluation-${report.startedAt.replace(/[:.]/g, "-")}.json`,
      results,
    ),
    JSON.stringify(report, null, 2) + "\n",
  );
  console.log(JSON.stringify(report.summary));
  console.log(
    "Relatório em .local/ai-rehearsal/results. Nenhuma confirmação de pedido enviada.",
  );
  if (report.summary.failed || report.summary.blocked || report.summary.skipped)
    process.exitCode = 1;
}
main().catch((error) => {
  console.error(
    error instanceof EvaluationError
      ? error.code
      : "RehearsalEvaluationSetupFailed",
  );
  process.exitCode = 1;
});
