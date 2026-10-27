import assert from "node:assert/strict";
import { createHmac } from "node:crypto";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";
import test from "node:test";
import { cases, selectCases } from "./cases.mjs";
import {
  readUsage,
  runEvaluation,
  verifyFixtures,
  verifyPilotToken,
} from "./runner.mjs";

const config = {
  AdminUsername: "piloto.admin",
  AdminPassword: "fictional-admin-password-for-tests",
  SigningKey: Buffer.alloc(48, 7).toString("base64"),
};
const now = Date.UTC(2026, 9, 10);
const claims = {
  iss: "MadeInMinas.AiPilot",
  aud: "MadeInMinas.AiPilot.Staff",
  exp: now / 1000 + 3600,
};
function token(overrides = {}, key = config.SigningKey, algorithm = "HS256") {
  const header = Buffer.from(JSON.stringify({ alg: algorithm })).toString(
    "base64url",
  );
  const body = Buffer.from(
    JSON.stringify({ ...claims, ...overrides }),
  ).toString("base64url");
  return `${header}.${body}.${createHmac("sha256", Buffer.from(key, "base64")).update(`${header}.${body}`).digest("base64url")}`;
}
function menu() {
  return {
    totalCount: 3,
    categories: [{ id: "fake-category", name: "Piloto (dados fictícios)" }],
    items: [
      {
        name: "Lanche piloto",
        price: 25.9,
        isAvailable: true,
        categoryId: "fake-category",
      },
      {
        name: "Batata piloto",
        price: 12.5,
        isAvailable: true,
        categoryId: "fake-category",
      },
      {
        name: "Lanche indisponível piloto",
        price: 30,
        isAvailable: false,
        categoryId: "fake-category",
      },
    ],
  };
}
function transcript(overrides = {}) {
  return {
    automationMode: "Automatic",
    automationState: "Idle",
    version: 2,
    messages: [
      { kind: "Visitor", sequence: 1, text: "fictional visitor" },
      {
        kind: "Assistant",
        sequence: 2,
        text: "Estas são as opções cadastradas:\nBatata piloto: R$ 12,50.\nLanche indisponível piloto: R$ 30,00 (indisponível).\nLanche piloto: R$ 25,90.",
      },
    ],
    ...overrides,
  };
}
const scenario = {
  id: "fake-case",
  steps: [
    {
      message: "fictional input",
      verify: () => [{ name: "observable assertion", passed: true }],
    },
  ],
};
function harness({
  jwt = token(),
  fixtures = menu(),
  state = () => transcript(),
  duplicateSequence = 3,
} = {}) {
  const requests = [];
  let clock = now;
  let visitorName;
  const fetchImpl = async (url, options) => {
    requests.push({ url, options });
    const path = new URL(url).pathname;
    let body;
    if (path === "/api/auth/login") body = { accessToken: jwt };
    else if (path === "/api/menu") body = fixtures;
    else if (path === "/api/public-chat" && options.method === "POST") {
      visitorName = JSON.parse(options.body).name;
      body = { access: { token: "fictional-capability-never-print" } };
    } else if (path === "/api/public-chat/messages") {
      const sends = requests.filter((request) =>
        request.url.endsWith("/messages"),
      );
      body = { sequence: sends.length === 1 ? 3 : duplicateSequence };
    } else if (path === "/api/public-chat") body = state(requests);
    else if (path === "/api/chat")
      body = {
        items: [{ id: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", visitorName }],
        pageSize: 20,
        totalCount: 1,
      };
    else throw new Error("Unexpected request with confidential text");
    return new Response(JSON.stringify(body), { status: 200 });
  };
  return {
    requests,
    options: {
      selected: [scenario],
      config,
      fetchImpl,
      now: () => clock,
      sleep: async (ms) => {
        clock += ms;
      },
    },
  };
}

test("default CLI lists scenarios without reading local credentials or calling a provider", () => {
  const result = spawnSync(
    process.execPath,
    [fileURLToPath(new URL("../Evaluate-AiPilot.mjs", import.meta.url))],
    { encoding: "utf8" },
  );
  assert.equal(result.status, 0);
  assert.match(result.stdout, /Sem chamadas de IA/);
  assert.match(result.stdout, /text-confirmation/);
});

test("case selection is explicit and rejects unknown or duplicate cases", () => {
  assert.equal(selectCases().length, 10);
  assert.deepEqual(
    selectCases("menu,discount").map((item) => item.id),
    ["menu", "discount"],
  );
  assert.throws(() => selectCases("menu,unknown"));
  assert.throws(() => selectCases("menu,menu"));
});

test("rehearsal identity is accepted only when that profile was explicitly selected", () => {
  const jwt = token({
    iss: "MadeInMinas.AiRehearsal",
    aud: "MadeInMinas.AiRehearsal.Staff",
  });
  verifyPilotToken(jwt, config.SigningKey, now, "Rehearsal");
  assert.throws(
    () => verifyPilotToken(jwt, config.SigningKey, now),
    /PilotIdentityRejected/,
  );
  assert.throws(
    () => verifyPilotToken(token(), config.SigningKey, now, "Rehearsal"),
    /PilotIdentityRejected/,
  );
});

test("rehearsal sends requests only to its fixed separate API", async () => {
  const run = harness({
    jwt: token({
      iss: "MadeInMinas.AiRehearsal",
      aud: "MadeInMinas.AiRehearsal.Staff",
    }),
  });
  const fixtures = menu();
  const { report } = await runEvaluation({
    ...run.options,
    profile: "Rehearsal",
    snapshot: { products: fixtures.items, categories: fixtures.categories },
  });
  assert.equal(report.summary.passed, 1);
  assert.ok(
    run.requests.every(({ url }) =>
      url.startsWith("http://localhost:5083/api/"),
    ),
  );
});

test("unknown profiles and missing rehearsal snapshots are rejected before login", async () => {
  for (const override of [
    { profile: "Production" },
    { profile: "Rehearsal" },
  ]) {
    const run = harness();
    await assert.rejects(
      runEvaluation({ ...run.options, ...override }),
      /EvaluationProfileRejected/,
    );
    assert.equal(run.requests.length, 0);
  }
});

for (const [label, jwt] of [
  ["wrong issuer", token({ iss: "MadeInMinas.Operational" })],
  ["wrong audience", token({ aud: "MadeInMinas.Staff" })],
  ["expired token", token({ exp: now / 1000 - 1 })],
  ["future token", token({ nbf: now / 1000 + 10 })],
  ["different signing key", token({}, Buffer.alloc(48, 8).toString("base64"))],
  ["algorithm substitution", token({}, config.SigningKey, "none")],
  ["malformed token", "fictional-not-a-jwt"],
]) {
  test(`rejects ${label} before writing any conversation`, async () => {
    assert.throws(
      () => verifyPilotToken(jwt, config.SigningKey, now),
      /PilotIdentityRejected/,
    );
    const run = harness({ jwt });
    await assert.rejects(runEvaluation(run.options), /PilotIdentityRejected/);
    assert.equal(run.requests.length, 1);
    assert.ok(run.requests[0].url.endsWith("/auth/login"));
  });
}

test("changed fixture catalog is rejected before any conversation is created", async () => {
  const fixtures = menu();
  fixtures.items[0].price = 100;
  const run = harness({ fixtures });
  await assert.rejects(runEvaluation(run.options), /PilotFixturesRejected/);
  assert.equal(run.requests.length, 2);
});

test("fixture availability and category must also match", () => {
  const fixtures = menu();
  fixtures.items[2].isAvailable = true;
  assert.throws(() => verifyFixtures(fixtures), /PilotFixturesRejected/);
  assert.throws(
    () => verifyFixtures({ ...menu(), totalCount: 4 }),
    /PilotFixturesRejected/,
  );
});

test("turn and pacing limits are enforced before network activity", async () => {
  for (const overrides of [
    { maxTurns: 0 },
    { maxTurns: 17 },
    { maxTurns: 1, selected: [scenario, scenario] },
    { delayMs: 1 },
  ]) {
    const run = harness();
    await assert.rejects(
      runEvaluation({ ...run.options, ...overrides }),
      /EvaluationLimitsRejected/,
    );
    assert.equal(run.requests.length, 0);
  }
});

test("report exposes checks and latency while excluding credentials, capabilities and transcripts", async () => {
  const run = harness();
  const { report } = await runEvaluation(run.options);
  assert.equal(report.summary.passed, 1);
  assert.equal(report.latency.completedTurns, 1);
  const json = JSON.stringify(report);
  for (const secret of [
    config.AdminPassword,
    config.SigningKey,
    token(),
    "fictional-capability-never-print",
    "fictional input",
    "fictional visitor",
  ])
    assert.ok(!json.includes(secret));
  assert.ok(
    run.requests.every(
      ({ url, options }) =>
        url.startsWith("http://localhost:5082/api/") &&
        options.redirect === "error",
    ),
  );
  assert.ok(!run.requests.some(({ url }) => url.includes("/confirm")));
});

test("unavailable automation blocks the run and skips later cases without retrying", async () => {
  const run = harness({
    state: () =>
      transcript({
        automationMode: "Manual",
        messages: [
          { kind: "System", sequence: 2, text: "secret provider error" },
        ],
      }),
  });
  const { report } = await runEvaluation({
    ...run.options,
    selected: [scenario, { ...scenario, id: "next" }],
  });
  assert.equal(report.summary.blocked, 1);
  assert.equal(report.summary.skipped, 1);
  assert.equal(report.sentTurns, 1);
  assert.equal(report.cases[0].reason, "AutomaticTurnUnavailable");
  assert.equal(
    run.requests.filter(
      ({ options, url }) =>
        options.method === "POST" && url.endsWith("/public-chat"),
    ).length,
    1,
  );
  assert.ok(!JSON.stringify(report).includes("secret provider error"));
});

test("legitimate model handoff counts as a reply while provider failure does not", async () => {
  const run = harness({
    state: () => transcript({ automationMode: "Manual" }),
  });
  const { report } = await runEvaluation(run.options);
  assert.equal(report.summary.passed, 1);
  assert.equal(report.cases[0].turns[0].automationMode, "Manual");
});

test("pending processing reaches the timeout without additional customer messages", async () => {
  const run = harness({
    state: () => transcript({ automationState: "Processing", messages: [] }),
  });
  const { report } = await runEvaluation({
    ...run.options,
    turnTimeoutMs: 2000,
  });
  assert.equal(report.cases[0].reason, "AutomaticTurnTimedOut");
  assert.equal(report.sentTurns, 1);
});

test("failed state assertions fail a case and stop its dependent steps", async () => {
  const run = harness();
  const selected = [
    {
      ...scenario,
      steps: [
        {
          ...scenario.steps[0],
          verify: () => [{ name: "state mismatch", passed: false }],
        },
        scenario.steps[0],
      ],
    },
  ];
  const { report } = await runEvaluation({ ...run.options, selected });
  assert.equal(report.summary.failed, 1);
  assert.equal(report.sentTurns, 1);
});

test("repeated message uses the identical request ID and detects a different sequence", async () => {
  const run = harness({
    duplicateSequence: 4,
    state: (requests) =>
      transcript({
        messages: [
          {
            kind: "Assistant",
            sequence: requests.some(({ url }) => url.endsWith("/messages"))
              ? 5
              : 2,
            text: "fictional reply",
          },
        ],
      }),
  });
  const selected = [
    {
      ...scenario,
      steps: [scenario.steps[0], { ...scenario.steps[0], repeat: true }],
    },
  ];
  const { report } = await runEvaluation({ ...run.options, selected });
  const sends = run.requests.filter(({ url }) => url.endsWith("/messages"));
  assert.equal(sends.length, 2);
  assert.equal(sends[0].options.body, sends[1].options.body);
  assert.equal(report.summary.failed, 1);
  assert.equal(report.sentTurns, 2);
});

test("correct reply text cannot pass an incorrect backend price", () => {
  const scenario = cases.find((item) => item.id === "text-confirmation");
  const checks = scenario.steps[0].verify(
    transcript({ order: { review: { total: 1 }, fulfillment: "Pickup" } }),
  );
  assert.equal(
    checks.find((item) => item.name === "resumo calculado no servidor").passed,
    false,
  );
});

test("general menu must include accompaniment, not only hamburgers", () => {
  const checks = cases[0].steps[0].verify(
    transcript({
      messages: [
        {
          kind: "Assistant",
          text: "Lanche piloto R$ 25,90; Lanche indisponível piloto indisponível",
        },
      ],
    }),
  );
  assert.equal(
    checks.find((item) => item.name === "acompanhamento e preço cadastrados")
      .passed,
    false,
  );
});

test("usage includes only this run, deduplicates lines and marks incomplete samples", () => {
  const id = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
  const entry = `AI chat turn ${id}/1: input 120, output 8.`;
  const log = `${entry}\n${entry}\nAI chat turn bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb/1: input 999, output 999.`;
  const usage = readUsage(log, new Map([[id, "case"]]), 1);
  assert.deepEqual(usage, {
    source: "backend-log",
    inputTokens: 120,
    outputTokens: 8,
    measuredTurns: 1,
    complete: true,
  });
  assert.equal(readUsage(log, new Map([[id, "case"]]), 2).complete, false);
  assert.equal(readUsage("", new Map(), 0).complete, false);
});
