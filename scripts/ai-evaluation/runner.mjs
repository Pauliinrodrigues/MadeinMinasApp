import { createHmac, randomUUID, timingSafeEqual } from "node:crypto";
import { verifyRehearsalMenu } from "./catalog.mjs";

export const api = "http://localhost:5082/api";
const pause = (milliseconds) =>
  new Promise((resolve) => setTimeout(resolve, milliseconds));
export class EvaluationError extends Error {
  constructor(code) {
    super(code);
    this.code = code;
  }
}

export function verifyPilotToken(
  token,
  signingKey,
  now = Date.now(),
  profile = "Synthetic",
) {
  try {
    const issuer =
      profile === "Synthetic"
        ? "MadeInMinas.AiPilot"
        : profile === "Rehearsal"
          ? "MadeInMinas.AiRehearsal"
          : null;
    if (!issuer) throw new Error();
    const parts = token.split(".");
    if (parts.length !== 3) throw new Error();
    const header = JSON.parse(Buffer.from(parts[0], "base64url"));
    const claims = JSON.parse(Buffer.from(parts[1], "base64url"));
    const signature = Buffer.from(parts[2], "base64url");
    const key = Buffer.from(signingKey, "base64");
    const expected = createHmac("sha256", key)
      .update(`${parts[0]}.${parts[1]}`)
      .digest();
    if (
      key.length < 32 ||
      header.alg !== "HS256" ||
      signature.length !== expected.length ||
      !timingSafeEqual(signature, expected) ||
      claims.iss !== issuer ||
      claims.aud !== issuer + ".Staff" ||
      !Number.isFinite(claims.exp) ||
      claims.exp * 1000 <= now ||
      claims.nbf * 1000 > now
    )
      throw new Error();
  } catch {
    throw new EvaluationError("PilotIdentityRejected");
  }
}

export function verifyFixtures(menu) {
  const expected = [
    ["Lanche piloto", 25.9, true],
    ["Batata piloto", 12.5, true],
    ["Lanche indisponível piloto", 30, false],
  ];
  if (
    menu.totalCount !== 3 ||
    menu.items?.length !== 3 ||
    menu.categories?.length !== 1 ||
    menu.categories[0].name !== "Piloto (dados fictícios)" ||
    expected.some(
      ([name, price, available]) =>
        !menu.items.some(
          (item) =>
            item.name === name &&
            item.price === price &&
            item.isAvailable === available &&
            item.categoryId === menu.categories[0].id,
        ),
    )
  ) {
    throw new EvaluationError("PilotFixturesRejected");
  }
}

export async function runEvaluation({
  selected,
  config,
  fetchImpl = fetch,
  sleep = pause,
  now = Date.now,
  uuid = randomUUID,
  delayMs = 25000,
  maxTurns = 16,
  turnTimeoutMs = 120000,
  onProgress = () => {},
  profile = "Synthetic",
  snapshot,
}) {
  if (
    !["Synthetic", "Rehearsal"].includes(profile) ||
    (profile === "Rehearsal" && !snapshot?.products?.length)
  )
    throw new EvaluationError("EvaluationProfileRejected");
  const targetApi = profile === "Rehearsal" ? "http://localhost:5083/api" : api;
  const plannedTurns = selected.reduce(
    (count, item) => count + item.steps.length,
    0,
  );
  if (
    !selected.length ||
    !Number.isInteger(maxTurns) ||
    maxTurns < 1 ||
    maxTurns > 16 ||
    plannedTurns > maxTurns ||
    !Number.isInteger(delayMs) ||
    delayMs < 25000 ||
    delayMs > 60000
  )
    throw new EvaluationError("EvaluationLimitsRejected");
  if (
    config?.AdminUsername !== "piloto.admin" ||
    typeof config.AdminPassword !== "string" ||
    config.AdminPassword.length < 24 ||
    typeof config.SigningKey !== "string"
  )
    throw new EvaluationError("PilotConfigRejected");

  async function request(path, body, access, method = body ? "POST" : "GET") {
    let response;
    try {
      response = await fetchImpl(targetApi + path, {
        method,
        redirect: "error",
        signal: AbortSignal.timeout(10000),
        headers: { "Content-Type": "application/json", ...(access ?? {}) },
        ...(body ? { body: JSON.stringify(body) } : {}),
      });
    } catch {
      throw new EvaluationError("PilotConnectionFailed");
    }
    if (!response.ok) throw new EvaluationError(`PilotHttp${response.status}`);
    try {
      const data = await response.text();
      if (data.length > 262144) throw new Error();
      return JSON.parse(data);
    } catch {
      throw new EvaluationError("PilotResponseRejected");
    }
  }

  // Before writing any conversation, authenticate the dedicated pilot signature
  // and verify its exact fake catalog. Redirects and other API targets are refused.
  const login = await request("/auth/login", {
    username: config.AdminUsername,
    password: config.AdminPassword,
  });
  verifyPilotToken(login.accessToken, config.SigningKey, now(), profile);
  if (profile === "Synthetic") verifyFixtures(await request("/menu"));
  else {
    const menu = await request("/menu?page=1&pageSize=48");
    for (
      let page = 2;
      menu.items.length < menu.totalCount && page <= 10;
      page++
    ) {
      const next = await request(`/menu?page=${page}&pageSize=48`);
      if (next.totalCount !== menu.totalCount || !next.items?.length)
        throw new EvaluationError("RehearsalCatalogChanged");
      menu.items.push(...next.items);
    }
    verifyRehearsalMenu(menu, snapshot);
  }
  const report = {
    profile,
    startedAt: new Date(now()).toISOString(),
    plannedCases: selected.length,
    plannedTurns,
    cases: [],
  };
  const visitors = new Map();
  let stop = false;
  let sentTurns = 0;
  for (const scenario of selected) {
    if (stop) {
      report.cases.push({
        id: scenario.id,
        status: "Skipped",
        reason: "PreviousTurnBlocked",
        turns: [],
        checks: [],
      });
      continue;
    }
    const result = { id: scenario.id, status: "Passed", turns: [], checks: [] };
    report.cases.push(result);
    const history = [];
    let access;
    onProgress({ id: scenario.id, status: "Running" });
    try {
      for (const [index, step] of scenario.steps.entries()) {
        // No provider retry. Pace turns, including new conversations, to avoid
        // turning a quota failure into a burst of additional requests.
        if (sentTurns > 0) await sleep(delayMs);
        const started = now();
        const body = { requestId: uuid(), text: step.message };
        let sequence = 0;
        if (index === 0) {
          body.name = `Cliente fictício avaliação ${scenario.id} ${uuid().slice(0, 8)}`;
          const created = await request("/public-chat", body);
          if (!created.access?.token)
            throw new EvaluationError("PilotAccessMissing");
          access = { "X-Chat-Access": created.access.token };
          visitors.set(body.name, scenario.id);
        } else {
          const message = await request("/public-chat/messages", body, access);
          sequence = message.sequence;
          if (step.repeat) {
            const duplicate = await request(
              "/public-chat/messages",
              body,
              access,
            );
            result.checks.push({
              name: "repetição mantém sequência da mensagem",
              passed: duplicate.sequence === sequence,
            });
          }
        }
        sentTurns++;
        let state;
        while (now() - started < turnTimeoutMs) {
          state = await request("/public-chat", null, access);
          const replied = state.messages.some(
            (message) =>
              message.kind === "Assistant" && message.sequence > sequence,
          );
          if (state.automationState === "Idle" && replied) break;
          if (
            state.automationMode !== "Automatic" ||
            state.automationState === "Unavailable"
          ) {
            throw new EvaluationError("AutomaticTurnUnavailable");
          }
          await sleep(1000);
        }
        if (
          !state?.messages.some(
            (message) =>
              message.kind === "Assistant" && message.sequence > sequence,
          ) ||
          state.automationState !== "Idle"
        )
          throw new EvaluationError("AutomaticTurnTimedOut");
        result.turns.push({
          elapsedMs: now() - started,
          automationMode: state.automationMode,
        });
        result.checks.push(...step.verify(state, history));
        history.push(state);
        if (result.checks.some((item) => !item.passed)) {
          result.status = "Failed";
          break;
        }
      }
    } catch (error) {
      result.status = "Blocked";
      result.reason =
        error instanceof EvaluationError ? error.code : "EvaluationFailed";
      stop = true;
    }
    onProgress({ id: scenario.id, status: result.status });
  }
  report.finishedAt = new Date(now()).toISOString();
  report.sentTurns = sentTurns;
  report.summary = Object.fromEntries(
    ["Passed", "Failed", "Blocked", "Skipped"].map((status) => [
      status.toLowerCase(),
      report.cases.filter((item) => item.status === status).length,
    ]),
  );
  const latencies = report.cases
    .flatMap((item) => item.turns.map((turn) => turn.elapsedMs))
    .sort((left, right) => left - right);
  report.latency = {
    completedTurns: latencies.length,
    medianMs: latencies.length
      ? latencies[Math.floor(latencies.length / 2)]
      : null,
    p95Ms: latencies.length
      ? latencies[Math.ceil(latencies.length * 0.95) - 1]
      : null,
  };

  // Resolve only this run's visitor names, never serialize tokens, transcripts,
  // customer data or arbitrary exception/provider bodies into the report.
  const conversationCases = new Map();
  try {
    for (const status of ["Waiting", "InService", "Closed"]) {
      for (let page = 1; page <= 20; page++) {
        const list = await request(
          `/chat?status=${status}&page=${page}`,
          null,
          {
            Authorization: `Bearer ${login.accessToken}`,
          },
        );
        for (const item of list.items) {
          if (visitors.has(item.visitorName))
            conversationCases.set(item.id, visitors.get(item.visitorName));
        }
        if (page * list.pageSize >= list.totalCount) break;
      }
    }
  } catch {
    /* Missing usage correlation does not erase completed checks. */
  }
  return { report, conversationCases };
}

export function readUsage(log, conversationCases, completedTurns) {
  let inputTokens = 0,
    outputTokens = 0,
    measuredTurns = 0;
  const seen = new Set();
  const pattern =
    /AI chat turn ([0-9a-f-]{36})\/(\d+): input (\d+), output (\d+)\./gi;
  for (const match of log.matchAll(pattern)) {
    const key = `${match[1]}/${match[2]}`;
    if (!conversationCases.has(match[1]) || seen.has(key)) continue;
    seen.add(key);
    inputTokens += Number(match[3]);
    outputTokens += Number(match[4]);
    measuredTurns++;
  }
  return {
    source: "backend-log",
    inputTokens,
    outputTokens,
    measuredTurns,
    complete: measuredTurns === completedTurns && completedTurns > 0,
  };
}
