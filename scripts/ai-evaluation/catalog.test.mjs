import assert from "node:assert/strict";
import test from "node:test";
import {
  captureCatalog,
  publicSnapshot,
  catalogDigest,
  verifyRehearsalMenu,
} from "./catalog.mjs";
import { rehearsalCases, selectRehearsalCases } from "./rehearsal-cases.mjs";

const categoryId = "10000000-0000-0000-0000-000000000001";
const productId = "20000000-0000-0000-0000-000000000001";
function fixture() {
  return {
    menu: {
      categories: [{ id: categoryId, name: "Categoria autorizada" }],
      totalCount: 1,
      items: [
        {
          id: productId,
          categoryId,
          name: "Produto do ensaio",
          description: "Descrição pública.",
          price: 20,
          isAvailable: true,
          unitCost: 18.1234,
        },
      ],
    },
    attendance: {
      hours: Array.from({ length: 7 }, (_, index) => ({
        day: index + 1,
        opensAt: "18:30",
        closesAt: "00:00",
        isClosed: false,
      })),
      paymentMethods: ["Card", "Cash", "Pix"],
      deliveryEstimateMessage: "Prazo variável.",
      isPaused: false,
    },
    areas: [
      {
        id: "corinto",
        neighborhood: "Todos os bairros",
        city: "Corinto",
        state: "MG",
        fee: 5,
        coversAllNeighborhoods: true,
      },
    ],
    options: {
      fixedDeliveryFee: 5,
      pickupFee: 0,
      pickupAddress: "Endereço público",
    },
  };
}
function snapshot(data = fixture()) {
  return publicSnapshot(
    data.menu,
    data.attendance,
    data.areas,
    data.options,
    "2026-10-10T23:00:00Z",
  );
}

test("capture performs only GETs on the public source and rejects redirects", async () => {
  const data = fixture(),
    calls = [];
  const source = await captureCatalog({
    fetchImpl: async (url, options) => {
      calls.push({ url, options });
      const body = url.includes("/menu?")
        ? data.menu
        : url.includes("/store-attendance/")
          ? data.attendance
          : url.includes("/delivery-areas")
            ? data.areas
            : data.options;
      return new Response(JSON.stringify(body));
    },
  });
  assert.equal(calls.length, 4);
  assert.ok(
    calls.every(
      ({ url, options }) =>
        url.startsWith("http://localhost:5080/api/") &&
        options.method === "GET" &&
        options.redirect === "error" &&
        !options.headers,
    ),
  );
  assert.equal(source.products[0].price, 20);
});

test("private and customer fields are never copied", () => {
  const data = fixture();
  data.menu.customers = ["private-person-marker"];
  data.menu.items[0].apiKey = "secret-marker";
  const json = JSON.stringify(snapshot(data));
  for (const prohibited of [
    "unitCost",
    "18.1234",
    "customers",
    "private-person-marker",
    "apiKey",
    "secret-marker",
  ])
    assert.ok(!json.includes(prohibited));
});

for (const change of [
  "price",
  "availability",
  "category",
  "duplicate",
  "hours",
  "payment",
  "pickup",
]) {
  test(`capture rejects invalid ${change}`, () => {
    const data = fixture();
    if (change === "price") data.menu.items[0].price = -1;
    if (change === "availability") data.menu.items[0].isAvailable = "yes";
    if (change === "category") data.menu.items[0].categoryId = productId;
    if (change === "duplicate") {
      data.menu.items.push(data.menu.items[0]);
      data.menu.totalCount = 2;
    }
    if (change === "hours") data.attendance.hours[0].day = 2;
    if (change === "payment") data.attendance.paymentMethods = ["Unauthorized"];
    if (change === "pickup") data.options.pickupFee = 5;
    assert.throws(() => snapshot(data));
  });
}

test("source errors do not reveal HTTP response bodies", async () => {
  await assert.rejects(
    captureCatalog({
      fetchImpl: async () =>
        new Response("secret-error-marker", { status: 500 }),
    }),
    { message: "SourceUnavailable" },
  );
});

test("capture bounds pagination and refuses an empty or oversized catalog", async () => {
  for (const totalCount of [0, 481]) {
    const data = fixture();
    data.menu.totalCount = totalCount;
    await assert.rejects(
      captureCatalog({
        fetchImpl: async (url) =>
          new Response(
            JSON.stringify(
              url.includes("/menu?")
                ? data.menu
                : url.includes("/store-attendance/")
                  ? data.attendance
                  : url.includes("/delivery-areas")
                    ? data.areas
                    : data.options,
            ),
          ),
      }),
      /InvalidPublicCatalog/,
    );
  }
});

test("changing page counts refuses an inconsistent snapshot", async () => {
  const data = fixture();
  data.menu.totalCount = 2;
  await assert.rejects(
    captureCatalog({
      fetchImpl: async (url) => {
        const body = url.includes("page=2")
          ? { ...data.menu, totalCount: 3 }
          : url.includes("/menu?")
            ? data.menu
            : url.includes("/store-attendance/")
              ? data.attendance
              : url.includes("/delivery-areas")
                ? data.areas
                : data.options;
        return new Response(JSON.stringify(body));
      },
    }),
    /CatalogChangedDuringCapture/,
  );
});

test("business digest ignores collection time but notices price changes", () => {
  const original = snapshot();
  assert.equal(
    catalogDigest(original),
    catalogDigest({ ...original, capturedAt: "other-time" }),
  );
  assert.notEqual(
    catalogDigest(original),
    catalogDigest({
      ...original,
      products: [{ ...original.products[0], price: 25 }],
    }),
  );
});

test("rehearsal preflight checks descriptions and values against the public snapshot", () => {
  const data = fixture();
  verifyRehearsalMenu(data.menu, snapshot(data));
  data.menu.items[0].description = "Unapproved ingredients";
  assert.throws(
    () => verifyRehearsalMenu(data.menu, snapshot()),
    /RehearsalCatalogMismatch/,
  );
});

test("commercial scenarios use snapshot names and server totals without order creation", () => {
  const scenarios = rehearsalCases(snapshot());
  assert.equal(scenarios.length, 6);
  assert.ok(
    scenarios
      .find((item) => item.id === "store-pickup")
      .steps[0].message.includes("Produto do ensaio"),
  );
  const checks = scenarios
    .find((item) => item.id === "store-pickup")
    .steps[0].verify({
      order: { fulfillment: "Pickup", review: { total: 1, deliveryFee: 0 } },
    });
  assert.equal(checks[0].passed, false);
});

test("rehearsal selection refuses unknown cases before making requests", () => {
  assert.deepEqual(
    selectRehearsalCases(snapshot(), "store-product,store-rules").map(
      (item) => item.id,
    ),
    ["store-product", "store-rules"],
  );
  assert.throws(
    () => selectRehearsalCases(snapshot(), "store-rules,unknown"),
    /InvalidRehearsalCases/,
  );
});
