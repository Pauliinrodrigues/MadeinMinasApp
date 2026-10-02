import { expect, Page, test } from '@playwright/test';
import type { ProductCost } from '../src/app/core/services/product-cost-api.service';

const original: ProductCost = {
  productId: 'product-1',
  productName: 'Uai Sô',
  productIsActive: true,
  isAvailableForSale: true,
  salePrice: 20,
  status: 'Ready',
  recipeYield: 2,
  recipeUpdatedAt: '2026-10-02T12:00:00Z',
  hasInactiveIngredients: false,
  knownRecipeCost: 12,
  recipeCost: 12,
  unitCost: 6,
  cmvPercentage: 30,
  grossMargin: 14,
  grossMarginPercentage: 70,
  calculatedAt: '2026-10-02T14:00:00Z',
  items: [
    {
      ingredientId: 'meat',
      ingredientName: 'Carne',
      unit: 'kg',
      ingredientIsActive: true,
      quantity: 0.3,
      unitCost: 30,
      recipeCost: 9,
    },
    {
      ingredientId: 'bread',
      ingredientName: 'Pão',
      unit: 'un',
      ingredientIsActive: true,
      quantity: 2,
      unitCost: 1.5,
      recipeCost: 3,
    },
  ],
};

async function setup(page: Page, role = 'Administrator') {
  const state = {
    cost: structuredClone(original),
    status: 200,
    code: '',
    reads: 0,
    writes: [] as string[],
    gate: null as Promise<void> | null,
  };
  const profile = {
    id: 'staff',
    name: 'Equipe',
    username: 'staff',
    role,
    permissions: role === 'Administrator' ? ['catalog.manage'] : [],
  };
  await page.route('**/api/**', async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    const json = (body: unknown, status = 200) => route.fulfill({ status, json: body });
    if (path === '/api/auth/login') {
      return json({
        accessToken: 'cmv-token',
        tokenType: 'Bearer',
        expiresAt: new Date(Date.now() + 900000).toISOString(),
        user: profile,
      });
    }
    expect(request.headers()['authorization']).toBe('Bearer cmv-token');
    if (path === '/api/auth/me') {
      return json(profile);
    }
    if (path === '/api/auth/logout') {
      return json({});
    }
    if (request.method() !== 'GET') {
      state.writes.push(path);
    }
    if (path === '/api/categories') {
      return json({ items: [], page: 1, pageSize: 20, totalCount: 0 });
    }
    if (path === '/api/products') {
      return json({
        items: [
          {
            id: 'product-1',
            name: 'Uai Sô',
            price: 20,
            categoryId: 'category',
            categoryName: 'Lanches',
            categoryIsActive: true,
            isActive: true,
            isAvailable: true,
            isAvailableForSale: true,
          },
        ],
        page: 1,
        pageSize: 20,
        totalCount: 1,
      });
    }
    if (path === '/api/products/product-1/costing') {
      state.reads++;
      if (state.gate) {
        await state.gate;
      }
      return state.status === 200 ? json(state.cost) : json({ code: state.code }, state.status);
    }
    return json({}, 404);
  });
  return state;
}

async function login(page: Page) {
  await page.goto('/entrar');
  await page.getByLabel('Login', { exact: true }).fill('staff');
  await page.getByLabel('Senha', { exact: true }).fill('Senha de teste');
  await page.getByRole('button', { name: 'Entrar', exact: true }).click();
  await expect(page).toHaveURL(/\/equipe$/);
}
async function openCost(page: Page) {
  await login(page);
  await page.getByRole('link', { name: 'Produtos', exact: true }).click();
  await page.getByRole('link', { name: 'CMV de Uai Sô', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'CMV teórico', exact: true })).toBeVisible();
}

test('CMV: consulta pelo produto, mostra custo, margem, composição e links', async ({ page }) => {
  const errors: string[] = [];
  page.on('pageerror', (error) => errors.push(error.message));
  const state = await setup(page);
  await openCost(page);
  const indicators = page.getByRole('region', { name: 'Indicadores de CMV' });
  await expect(indicators).toContainText('30,00%');
  await expect(indicators).toContainText('70,00%');
  await expect(indicators).toContainText(/R\$\s*6,00/);
  await expect(page.getByRole('article', { name: 'Custo de Carne' })).toContainText('0,3 kg');
  await expect(
    page.getByRole('link', { name: 'Editar ficha técnica', exact: true }),
  ).toHaveAttribute('href', '/equipe/produtos/product-1/ficha-tecnica');
  await expect(
    page.getByRole('link', { name: 'Editar produto e preço', exact: true }),
  ).toHaveAttribute('href', '/equipe/produtos/product-1');
  expect(state.writes).toEqual([]);
  expect(errors).toEqual([]);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});

test('CMV: ficha ausente não apresenta indicadores e orienta cadastro', async ({ page }) => {
  const state = await setup(page);
  Object.assign(state.cost, {
    status: 'MissingRecipe',
    recipeYield: null,
    recipeUpdatedAt: null,
    items: [],
    knownRecipeCost: 0,
    recipeCost: null,
    unitCost: null,
    cmvPercentage: null,
    grossMargin: null,
    grossMarginPercentage: null,
  });
  await openCost(page);
  await expect(page.getByRole('status')).toContainText('Cadastre uma ficha técnica');
  await expect(page.getByRole('region', { name: 'Indicadores de CMV' })).toHaveCount(0);
  await expect(page.getByRole('link', { name: 'Editar ficha técnica', exact: true })).toBeVisible();
});

test('CMV: custo zero mostra apenas parcial e identifica ingrediente pendente', async ({
  page,
}) => {
  const state = await setup(page);
  Object.assign(state.cost, {
    status: 'MissingCosts',
    knownRecipeCost: 3,
    recipeCost: null,
    unitCost: null,
    cmvPercentage: null,
    grossMargin: null,
    grossMarginPercentage: null,
  });
  state.cost.items[0].unitCost = 0;
  state.cost.items[0].recipeCost = 0;
  await openCost(page);
  await expect(page.getByRole('region', { name: 'Custos pendentes' })).toContainText(/R\$\s*3,00/);
  await expect(page.getByRole('article', { name: 'Custo de Carne' })).toContainText(
    'Custo não informado',
  );
  await expect(page.getByRole('region', { name: 'Indicadores de CMV' })).toHaveCount(0);
});

test('CMV: atualização consulta backend, bloqueia repetição e remove dados após falha', async ({
  page,
}) => {
  const state = await setup(page);
  await openCost(page);
  await expect(page.getByRole('region', { name: 'Indicadores de CMV' })).toContainText('30,00%');
  let release = () => {};
  state.gate = new Promise<void>((resolve) => {
    release = resolve;
  });
  state.status = 503;
  await page.getByRole('button', { name: 'Atualizar cálculo', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Atualizar cálculo', exact: true })).toBeDisabled();
  await expect(page.getByRole('region', { name: 'Indicadores de CMV' })).toHaveCount(0);
  release();
  await expect(page.getByRole('alert')).toBeVisible();
  state.status = 200;
  state.cost.cmvPercentage = 37.41;
  await page.getByRole('button', { name: 'Tentar novamente', exact: true }).click();
  await expect(page.getByRole('region', { name: 'Indicadores de CMV' })).toContainText('37,41%');
  expect(state.reads).toBe(3);
  expect(state.writes).toEqual([]);
});

test('CMV: margem negativa e cadastros inativos continuam visíveis com avisos', async ({
  page,
}) => {
  const state = await setup(page);
  Object.assign(state.cost, {
    productIsActive: false,
    isAvailableForSale: false,
    hasInactiveIngredients: true,
    salePrice: 5,
    cmvPercentage: 120,
    grossMargin: -1,
    grossMarginPercentage: -20,
  });
  state.cost.items[0].ingredientIsActive = false;
  await openCost(page);
  await expect(page.getByRole('region', { name: 'Indicadores de CMV' })).toContainText('-20,00%');
  await expect(page.getByRole('status')).toContainText('supera o preço de venda');
  await expect(page.getByRole('region', { name: 'Dados do produto' })).toContainText(
    'Produto inativo',
  );
  await expect(
    page.getByText('A ficha contém ingredientes inativos.', { exact: false }),
  ).toBeVisible();
});

test('CMV: custo positivo abaixo da precisão visual não é apresentado como zero', async ({
  page,
}) => {
  const state = await setup(page);
  state.cost.unitCost = 0.00000000001;
  await openCost(page);
  await expect(page.getByRole('region', { name: 'Indicadores de CMV' })).toContainText(
    '< R$ 0,000001',
  );
});

test('CMV: produto inexistente mostra erro e sessão revogada encerra consulta', async ({
  page,
}) => {
  const state = await setup(page);
  state.status = 404;
  state.code = 'ProductNotFound';
  await openCost(page);
  await expect(page.getByRole('alert')).toContainText('Produto não encontrado');
  state.status = 401;
  await page.getByRole('button', { name: 'Tentar novamente', exact: true }).click();
  await expect(page).toHaveURL(/\/entrar$/);
  await expect(page.getByRole('region', { name: 'Indicadores de CMV' })).toHaveCount(0);
});

for (const role of ['Attendant', 'Kitchen', 'Dispatch']) {
  test('CMV: ' + role + ' não acessa dados de custo', async ({ page }) => {
    const state = await setup(page, role);
    await login(page);
    await page.evaluate(() => {
      history.pushState(null, '', '/equipe/produtos/product-1/cmv');
      dispatchEvent(new PopStateEvent('popstate'));
    });
    await expect(page).toHaveURL(/\/equipe$/);
    expect(state.reads).toBe(0);
  });
}
