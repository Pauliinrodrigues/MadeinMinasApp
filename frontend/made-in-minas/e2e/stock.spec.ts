import { test, expect, Page } from '@playwright/test';
import {
  IngredientStock,
  StockInput,
  StockMovement,
} from '../src/app/core/services/stock-api.service';

async function setup(page: Page, role = 'Administrator') {
  const profile = {
    id: 'staff',
    name: 'Administrador',
    username: 'admin',
    role,
    permissions: role === 'Administrator' ? ['catalog.manage'] : [],
  };
  const stock: IngredientStock = {
    ingredientId: 'ingredient-1',
    name: 'Carne',
    unit: 'kg',
    isActive: true,
    currentStock: 0,
    minimumStock: 2,
    isLowStock: true,
    version: 0,
    movements: [],
    page: 1,
    pageSize: 20,
    totalCount: 0,
  };
  const state = {
    stock,
    requests: [] as StockInput[],
    failAfterWrite: false,
    conflict: false,
    readFailure: false,
  };
  await page.route('**/api/**', async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    const json = (body: unknown, status = 200) => route.fulfill({ status, json: body });
    if (url.pathname === '/api/auth/login') {
      return json({
        accessToken: 'stock-test-token',
        tokenType: 'Bearer',
        expiresAt: new Date(Date.now() + 900000).toISOString(),
        user: profile,
      });
    }
    expect(request.headers()['authorization']).toBe('Bearer stock-test-token');
    if (url.pathname === '/api/auth/me') {
      return json(profile);
    }
    if (url.pathname === '/api/ingredients') {
      return json({
        items: [
          {
            id: stock.ingredientId,
            name: stock.name,
            unit: stock.unit,
            unitCost: 35,
            minimumStock: stock.minimumStock,
            supplier: null,
            isActive: stock.isActive,
          },
        ],
        page: 1,
        pageSize: 20,
        totalCount: 1,
      });
    }
    if (url.pathname === '/api/ingredients/ingredient-1/stock') {
      if (state.readFailure) {
        return json({}, 503);
      }
      const current = Number(url.searchParams.get('page') ?? '1');
      return json({
        ...stock,
        page: current,
        totalCount: stock.movements.length,
        movements: stock.movements.slice((current - 1) * 20, current * 20),
      });
    }
    if (url.pathname.endsWith('/stock/movements')) {
      const input = request.postDataJSON() as StockInput;
      state.requests.push(input);
      if (state.conflict) {
        return json({ code: 'StockVersionConflict' }, 409);
      }
      const existing = stock.movements.find((movement) => movement.requestId === input.requestId);
      if (existing) {
        return json(existing);
      }
      const previous = stock.currentStock;
      const balance =
        input.type === 'Count'
          ? input.quantity
          : previous + (input.type === 'Entry' ? input.quantity : -input.quantity);
      const movement: StockMovement = {
        id: 'movement-' + input.requestId,
        requestId: input.requestId,
        type: input.type,
        quantity: input.quantity,
        reason: input.reason,
        version: ++stock.version,
        previousBalance: previous,
        balance,
        delta: balance - previous,
        actorId: 'staff',
        actorName: 'Administrador',
        ingredientName: stock.name,
        unit: stock.unit,
        createdAt: '2026-10-02T12:00:00Z',
      };
      stock.currentStock = balance;
      stock.isLowStock = balance <= stock.minimumStock;
      stock.movements.unshift(movement);
      if (state.failAfterWrite) {
        state.failAfterWrite = false;
        return route.abort('failed');
      }
      return json(movement);
    }
    return json({}, 404);
  });
  return state;
}

async function login(page: Page) {
  await page.goto('/entrar');
  await page.getByLabel('Login', { exact: true }).fill('admin');
  await page.getByLabel('Senha', { exact: true }).fill('Senha de teste');
  await page.getByRole('button', { name: 'Entrar', exact: true }).click();
  await expect(page).toHaveURL(/\/equipe$/);
}
async function openStock(page: Page) {
  await login(page);
  await page.getByRole('link', { name: 'Ingredientes', exact: true }).click();
  await page.getByRole('link', { name: 'Estoque de Carne', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Estoque do ingrediente' })).toBeVisible();
}
async function review(page: Page, type: string, quantity: string, reason: string) {
  await page.getByRole('combobox', { name: 'Tipo', exact: true }).selectOption(type);
  await page
    .getByLabel(type === 'Count' ? 'Quantidade contada (kg)' : 'Quantidade (kg)', { exact: true })
    .fill(quantity);
  await page.getByLabel('Motivo', { exact: true }).fill(reason);
  await page.getByRole('button', { name: 'Revisar movimentação' }).click();
}
async function confirm(page: Page) {
  await page.getByRole('button', { name: 'Confirmar movimentação', exact: true }).click();
  await expect(
    page.getByRole('status').filter({ hasText: 'Movimentação registrada' }),
  ).toBeVisible();
  await expect(page.getByRole('button', { name: 'Atualizar saldo' })).toBeEnabled();
}

test('estoque: entrada, saída, contagem e histórico com confirmação', async ({ page }) => {
  const state = await setup(page);
  await openStock(page);
  await expect(page.getByText('Estoque no mínimo ou abaixo do mínimo.')).toBeVisible();
  await review(page, 'Entry', '10,125', 'Compra de carne');
  await expect(page.getByRole('region', { name: 'Confirmar movimentação' })).toContainText(
    '0 → 10,125 kg',
  );
  expect(state.requests).toHaveLength(0);
  await page.getByRole('button', { name: 'Cancelar', exact: true }).click();
  await page.getByRole('button', { name: 'Revisar movimentação' }).click();
  await confirm(page);
  await expect(page.getByRole('region', { name: 'Saldo do ingrediente' })).toContainText(
    '10,125 kg',
  );
  await review(page, 'Exit', '1,125', 'Perda na produção');
  await confirm(page);
  await review(page, 'Count', '2', 'Contagem de fechamento');
  await confirm(page);
  await expect(page.getByRole('region', { name: 'Saldo do ingrediente' })).toContainText(
    'Saldo: 2 kg',
  );
  await expect(page.locator('article')).toHaveCount(3);
  await expect(page.locator('article').first()).toContainText('9 → 2 kg (-7)');
  expect(state.requests.map((input) => input.expectedVersion)).toEqual([0, 1, 2]);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});

test('estoque: valida precisão, motivo e saldo insuficiente antes de enviar', async ({ page }) => {
  const state = await setup(page);
  await openStock(page);
  await page.getByLabel('Quantidade (kg)', { exact: true }).fill('1,0001');
  await page.getByLabel('Motivo', { exact: true }).fill('Compra');
  await expect(page.getByRole('button', { name: 'Revisar movimentação' })).toBeDisabled();
  await expect(page.getByRole('alert')).toContainText('três casas');
  await page.getByLabel('Quantidade (kg)', { exact: true }).fill('1');
  await page.getByLabel('Motivo', { exact: true }).fill('  ');
  await expect(page.getByRole('button', { name: 'Revisar movimentação' })).toBeDisabled();
  await page.getByLabel('Motivo', { exact: true }).fill('Saída');
  await page.getByRole('combobox', { name: 'Tipo', exact: true }).selectOption('Exit');
  await expect(page.getByRole('alert')).toContainText('ultrapassa o saldo');
  await expect(page.getByRole('button', { name: 'Revisar movimentação' })).toBeDisabled();
  expect(state.requests).toHaveLength(0);
});

test('estoque: falha após gravação repete a mesma intenção sem duplicar', async ({ page }) => {
  const state = await setup(page);
  state.failAfterWrite = true;
  await openStock(page);
  await review(page, 'Entry', '5', 'Compra');
  await page.getByRole('button', { name: 'Confirmar movimentação', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Tentar o mesmo lançamento' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Cancelar', exact: true })).toBeDisabled();
  await expect(page.getByLabel('Quantidade (kg)', { exact: true })).toBeDisabled();
  await page.getByRole('button', { name: 'Tentar o mesmo lançamento' }).click();
  await expect(page.locator('article')).toHaveCount(1);
  expect(state.requests).toHaveLength(2);
  expect(state.requests[0]).toEqual(state.requests[1]);
  expect(state.stock.currentStock).toBe(5);
});

test('estoque: conflito exige atualizar e revisar; leitura falha bloqueia gravação', async ({
  page,
}) => {
  const state = await setup(page);
  state.conflict = true;
  await openStock(page);
  await review(page, 'Entry', '5', 'Compra');
  await page.getByRole('button', { name: 'Confirmar movimentação', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('O estoque mudou');
  await expect(page.getByRole('button', { name: 'Revisar movimentação' })).toBeDisabled();
  state.readFailure = true;
  await page.getByRole('button', { name: 'Atualizar saldo' }).click();
  await expect(page.getByRole('alert')).toContainText('servidor');
  await expect(page.getByRole('button', { name: 'Revisar movimentação' })).toBeDisabled();
  state.readFailure = false;
  state.conflict = false;
  state.stock.version = 3;
  await page.getByRole('button', { name: 'Atualizar saldo' }).click();
  await page.getByRole('button', { name: 'Revisar movimentação' }).click();
  await confirm(page);
  expect(state.requests[1].expectedVersion).toBe(3);
  expect(state.requests[1].requestId).not.toBe(state.requests[0].requestId);
});

test('estoque: ingrediente inativo mantém histórico e bloqueia movimentação', async ({ page }) => {
  const state = await setup(page);
  state.stock.isActive = false;
  await openStock(page);
  await expect(page.getByText('Ingrediente inativo.', { exact: false })).toBeVisible();
  await expect(page.getByRole('combobox', { name: 'Tipo', exact: true })).toBeDisabled();
  expect(state.requests).toHaveLength(0);
});

test('estoque: atendente não acessa a rota administrativa', async ({ page }) => {
  const state = await setup(page, 'Attendant');
  await login(page);
  await page.evaluate(() => {
    history.pushState(null, '', '/equipe/ingredientes/ingredient-1/estoque');
    dispatchEvent(new PopStateEvent('popstate'));
  });
  await expect(page).toHaveURL(/\/equipe$/);
  await expect(page.getByRole('heading', { name: 'Estoque do ingrediente' })).toHaveCount(0);
  expect(state.requests).toHaveLength(0);
});
