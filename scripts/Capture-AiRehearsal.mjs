import { mkdir, writeFile } from "node:fs/promises";
import {
  captureCatalog,
  CatalogError,
  catalogDigest,
} from "./ai-evaluation/catalog.mjs";

async function main() {
  const snapshot = await captureCatalog();
  const directory = new URL("../.local/ai-rehearsal/", import.meta.url);
  await mkdir(directory, { recursive: true });
  await writeFile(
    new URL("catalog.snapshot.json", directory),
    JSON.stringify(snapshot, null, 2) + "\n",
  );
  const discrepancies = [];
  for (const hour of snapshot.attendance.hours) {
    const expected = hour.day <= 4 ? "23:30" : "23:59";
    if (hour.opensAt !== "18:30" || hour.closesAt !== expected || hour.isClosed)
      discrepancies.push({
        type: "HoursDifferFromOwnerInstructions",
        day: hour.day,
        opensAt: hour.opensAt,
        closesAt: hour.closesAt,
        expectedClosesAt: expected,
      });
  }
  if (
    snapshot.options.deliveryFee !== 5 ||
    snapshot.areas.some((area) => area.fee !== 5)
  )
    discrepancies.push({ type: "DeliveryFeeDiffersFromOwnerInstructions" });
  const report = {
    capturedAt: snapshot.capturedAt,
    source: "Local public API 5080",
    catalogSha256: catalogDigest(snapshot),
    products: snapshot.products.length,
    categories: snapshot.categories.length,
    availableProducts: snapshot.products.filter((item) => item.isAvailable)
      .length,
    discrepancies,
    limitations: [
      "Only public active products are copied.",
      "Inventory and recipes in the rehearsal are fictional.",
      "No customer, order, cost, margin or credentials copied.",
      "Images are not copied.",
      "Rehearsal accepts orders only within copied opening hours.",
    ],
  };
  await writeFile(
    new URL("readiness.json", directory),
    JSON.stringify(report, null, 2) + "\n",
  );
  console.log(
    `Catálogo público capturado: ${report.products} produto(s), ${report.categories} categoria(s).`,
  );
  console.log(
    `Divergências com as regras informadas: ${discrepancies.length}. Relatório .local/ai-rehearsal/readiness.json.`,
  );
}
main().catch((error) => {
  console.error(
    error instanceof CatalogError ? error.code : "CatalogCaptureFailed",
  );
  process.exitCode = 1;
});
