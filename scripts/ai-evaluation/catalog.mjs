import { createHash } from "node:crypto";

export class CatalogError extends Error {
  constructor(code) {
    super(code);
    this.code = code;
  }
}
const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
function bounded(value, maximum, nullable = false) {
  if (nullable && value === null) return null;
  if (typeof value !== "string" || !value.trim() || value.length > maximum)
    throw new CatalogError("InvalidPublicData");
  return value;
}
function money(value, nullable = false) {
  if (nullable && value === null) return null;
  if (
    !Number.isFinite(value) ||
    value < 0 ||
    value > 999999.99 ||
    Math.abs(value * 100 - Math.round(value * 100)) > 0.000001
  )
    throw new CatalogError("InvalidPublicData");
  return value;
}
const boolean = (value) => {
  if (typeof value !== "boolean") throw new CatalogError("InvalidPublicData");
  return value;
};
function identifier(value) {
  if (!guid.test(value) || /^0{8}-0{4}-0{4}-0{4}-0{12}$/.test(value))
    throw new CatalogError("InvalidPublicData");
  return value;
}

export function publicSnapshot(menu, attendance, areas, options, capturedAt) {
  if (
    !menu.items?.length ||
    menu.items.length !== menu.totalCount ||
    menu.totalCount > 480 ||
    !menu.categories?.length
  )
    throw new CatalogError("InvalidPublicCatalog");
  const categories = menu.categories.map((item) => ({
    id: identifier(item.id),
    name: bounded(item.name, 120),
  }));
  const products = menu.items.map((item) => ({
    id: identifier(item.id),
    categoryId: identifier(item.categoryId),
    name: bounded(item.name, 160),
    description: bounded(item.description, 1000, true),
    price: money(item.price),
    isAvailable: boolean(item.isAvailable),
  }));
  if (
    new Set(categories.map((item) => item.id)).size !== categories.length ||
    new Set(products.map((item) => item.id)).size !== products.length ||
    products.some(
      (item) => !categories.some((category) => category.id === item.categoryId),
    )
  )
    throw new CatalogError("InvalidPublicCatalog");
  const hours = attendance.hours?.map((hour) => ({
    day: hour.day,
    opensAt: bounded(hour.opensAt, 5),
    closesAt: bounded(hour.closesAt, 5),
    isClosed: boolean(hour.isClosed),
  }));
  if (
    hours?.length !== 7 ||
    new Set(hours.map((hour) => hour.day)).size !== 7 ||
    hours.some(
      (hour) =>
        !Number.isInteger(hour.day) ||
        hour.day < 1 ||
        hour.day > 7 ||
        !/^([01]\d|2[0-3]):[0-5]\d$/.test(hour.opensAt) ||
        !/^([01]\d|2[0-3]):[0-5]\d$/.test(hour.closesAt),
    )
  )
    throw new CatalogError("InvalidAttendance");
  if (
    !attendance.paymentMethods?.length ||
    attendance.paymentMethods.some(
      (item) => !["Card", "Cash", "Pix"].includes(item),
    ) ||
    new Set(attendance.paymentMethods).size !==
      attendance.paymentMethods.length ||
    areas.length > 100
  )
    throw new CatalogError("InvalidAttendance");
  const deliveryAreas = areas.map((area) => ({
    id: bounded(area.id, 100),
    neighborhood: bounded(area.neighborhood, 120, true),
    city: bounded(area.city, 120),
    state: bounded(area.state, 2),
    fee: money(area.fee),
    coversAllNeighborhoods: boolean(area.coversAllNeighborhoods),
  }));
  if (
    new Set(deliveryAreas.map((area) => area.id)).size !== deliveryAreas.length
  )
    throw new CatalogError("InvalidPublicData");
  if (money(options.pickupFee) !== 0)
    throw new CatalogError("UnsupportedPickupFee");
  return {
    schemaVersion: 1,
    capturedAt,
    categories,
    products,
    attendance: {
      hours,
      paymentMethods: [...attendance.paymentMethods],
      deliveryEstimateMessage: bounded(attendance.deliveryEstimateMessage, 500),
      isPaused: boolean(attendance.isPaused),
      receiveOrdersOutsideHours: false,
    },
    areas: deliveryAreas,
    options: {
      deliveryFee: money(options.fixedDeliveryFee, true),
      pickupFee: 0,
      pickupAddress: bounded(options.pickupAddress, 500, true),
    },
  };
}

export async function captureCatalog({
  fetchImpl = fetch,
  now = () => new Date().toISOString(),
} = {}) {
  async function get(path) {
    let response;
    try {
      response = await fetchImpl("http://localhost:5080/api/" + path, {
        method: "GET",
        redirect: "error",
        signal: AbortSignal.timeout(10000),
      });
    } catch {
      throw new CatalogError("SourceUnavailable");
    }
    if (!response.ok) throw new CatalogError("SourceUnavailable");
    try {
      const body = await response.text();
      if (body.length > 262144) throw new Error();
      return JSON.parse(body);
    } catch {
      throw new CatalogError("InvalidSourceResponse");
    }
  }
  const [first, attendance, areas, options] = await Promise.all([
    get("menu?page=1&pageSize=48"),
    get("store-attendance/public"),
    get("public-checkout/delivery-areas"),
    get("public-checkout/options"),
  ]);
  if (
    !Number.isInteger(first.totalCount) ||
    first.totalCount < 1 ||
    first.totalCount > 480
  )
    throw new CatalogError("InvalidPublicCatalog");
  const items = [...first.items];
  for (let page = 2; items.length < first.totalCount; page++) {
    if (page > 10) throw new CatalogError("InvalidPublicCatalog");
    const next = await get(`menu?page=${page}&pageSize=48`);
    if (next.totalCount !== first.totalCount || !next.items?.length)
      throw new CatalogError("CatalogChangedDuringCapture");
    items.push(...next.items);
  }
  return publicSnapshot({ ...first, items }, attendance, areas, options, now());
}

export function catalogDigest(snapshot) {
  const { capturedAt: _ignored, ...business } = snapshot;
  return createHash("sha256").update(JSON.stringify(business)).digest("hex");
}

export function verifyRehearsalMenu(menu, snapshot) {
  if (
    menu.totalCount !== snapshot.products.length ||
    menu.items?.length !== snapshot.products.length ||
    menu.categories?.length !== snapshot.categories.length ||
    snapshot.categories.some(
      (category) =>
        !menu.categories.some(
          (item) => item.id === category.id && item.name === category.name,
        ),
    ) ||
    snapshot.products.some(
      (product) =>
        !menu.items.some(
          (item) =>
            item.id === product.id &&
            item.name === product.name &&
            item.categoryId === product.categoryId &&
            item.description === product.description &&
            item.price === product.price &&
            item.isAvailable === product.isAvailable,
        ),
    )
  )
    throw new CatalogError("RehearsalCatalogMismatch");
}
