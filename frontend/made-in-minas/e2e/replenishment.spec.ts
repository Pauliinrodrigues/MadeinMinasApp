import { expect, Page, test } from '@playwright/test';
import { StockReplenishmentItem } from '../src/app/core/services/stock-api.service';

async function setup(page: Page, role = 'Administrator') {
  const profile = {
    id: 'admin',
    name: 'Equipe de teste',
    username: 'admin',
    role,
    permissions: role === 'Administrator' ? ['catalog.manage'] : [],
  };
  const items: StockReplenishmentItem[] = [
    {
      ingredientId: 'salt',
      name: 'Sal',
      unit: 'kg',
      supplier: 'Fornecedor da Serra',
      isActive: true,
      currentStock: 0,
      minimumStock: 1,
      quantityToMinimum: 1,
      isLowStock: true,
      hasMovements: false,
    },
    {
      ingredientId: 'cup',
      name: 'Copo',
      unit: 'un',
      supplier: null,
      isActive: true,
      currentStock: 0,
      minimumStock: 0,
      quantityToMinimum: 0,
      isLowStock: true,
      hasMovements: true,
    },
    {
      ingredientId: 'meat',
      name: 'Carne',
      unit: 'kg',
      supplier: 'Fornecedor da Serra',
      isActive: true,
      currentStock: 1.125,
      minimumStock: 2.5,
      quantityToMinimum: 1.375,
      isLowStock: true,
      hasMovements: true,
    },
    {
      ingredientId: 'sauce',
      name: 'Molho',
      unit: 'l',
      supplier: 'Molhos de Minas',
      isActive: true,
      currentStock: 3,
      minimumStock: 1,
      quantityToMinimum: 0,
      isLowStock: false,
      hasMovements: true,
    },
    {
      ingredientId: 'cheese',
      name: 'Queijo',
      unit: 'kg',
      supplier: 'Fornecedor da Serra',
      isActive: false,
      currentStock: 0,
      minimumStock: 2,
      quantityToMinimum: 2,
      isLowStock: true,
      hasMovements: false,
    },
  ];
  const state = { items, queries: [] as URLSearchParams[], writes: 0, readStatus: 200 };
  await page.route('**/api/**', async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    if (url.pathname === '/api/auth/login') {
      return route.fulfill({
        json: {
          accessToken: 'replenishment-test',
          tokenType: 'Bearer',
          expiresAt: new Date(Date.now() + 900000).toISOString(),
          user: profile,
        },
      });
    }
    expect(request.headers()['authorization']).toBe('Bearer replenishment-test');
    if (url.pathname === '/api/auth/me') {
      return route.fulfill({ json: profile });
    }
    if (request.method() !== 'GET') {
      state.writes++;
      return route.fulfill({ status: 405, json: {} });
    }
    if (url.pathname === '/api/stock/replenishment') {
      state.queries.push(url.searchParams);
      if (state.readStatus !== 200) {
        return route.fulfill({ status: state.readStatus, json: {} });
      }
      const search = (url.searchParams.get('search') ?? '').toLocaleLowerCase('pt-BR');
      const base = state.items.filter(
        (item) =>
          (item.isActive || url.searchParams.get('includeInactive') === 'true') &&
          (item.name + ' ' + (item.supplier ?? '')).toLocaleLowerCase('pt-BR').includes(search),
      );
      const summary = {
        totalCount: base.length,
        attentionCount: base.filter((item) => item.isLowStock || !item.hasMovements).length,
        outOfStockCount: base.filter((item) => item.currentStock === 0).length,
        lowStockCount: base.filter((item) => item.isLowStock).length,
        unrecordedCount: base.filter((item) => !item.hasMovements).length,
      };
      const status = url.searchParams.get('status');
      const filtered = base.filter(
        (item) =>
          status === 'All' ||
          (status === 'Attention' && (item.isLowStock || !item.hasMovements)) ||
          (status === 'OutOfStock' && item.currentStock === 0) ||
          (status === 'LowStock' && item.isLowStock) ||
          (status === 'Unrecorded' && !item.hasMovements),
      );
      const current = Number(url.searchParams.get('page') ?? '1');
      return route.fulfill({
        json: {
          summary,
          totalCount: filtered.length,
          page: current,
          pageSize: 20,
          items: filtered.slice((current - 1) * 20, current * 20),
        },
      });
    }
    if (url.pathname === '/api/ingredients/meat/stock') {
      const item = state.items.find((item) => item.ingredientId === 'meat')!;
      return route.fulfill({
        json: { ...item, version: 1, movements: [], page: 1, pageSize: 20, totalCount: 0 },
      });
    }
    if (url.pathname === '/api/ingredients/meat') {
      return route.fulfill({
        json: {
          ...state.items.find((item) => item.ingredientId === 'meat'),
          id: 'meat',
          unitCost: 35,
        },
      });
    }
    return route.fulfill({ status: 404, json: {} });
  });
  return state;
}

async function login(page: Page, returnUrl = '/equipe/reposicao') {
  await page.goto('/entrar?returnUrl=' + encodeURIComponent(returnUrl));
  await page.getByLabel('Login', { exact: true }).fill('admin');
  await page.getByLabel('Senha', { exact: true }).fill('Test-password-123!');
  await page.getByRole('button', { name: 'Entrar', exact: true }).click();
  await expect(page).toHaveURL(new RegExp(returnUrl + '$'));
}

test('reposição: pendências, unidades e mínimo ficam legíveis sem lançar movimentos', async ({
  page,
}, info) => {
  const errors: string[] = [];
  page.on('pageerror', (error) => errors.push(error.message));
  const state = await setup(page);
  await login(page);
  await expect(page.getByRole('link', { name: 'Reposição', exact: true })).toBeVisible();
  await expect(page.getByRole('status')).toHaveText('3 ingrediente(s) nesta busca.');
  const meat = page.getByRole('article', { name: 'Estoque de Carne', exact: true });
  await expect(meat).toContainText('1,375 kg');
  await expect(meat).toContainText('1,125 kg');
  await expect(page.getByRole('article', { name: 'Estoque de Copo', exact: true })).toContainText(
    'No mínimo ou abaixo',
  );
  await expect(page.getByRole('article', { name: 'Estoque de Sal', exact: true })).toContainText(
    'Confira o saldo físico',
  );
  await expect(page.getByRole('article', { name: 'Estoque de Queijo', exact: true })).toHaveCount(
    0,
  );
  const summary = page.getByRole('group', { name: 'Resumo do estoque' });
  await expect(summary.getByRole('button', { name: /4\s*Todos/ })).toBeVisible();
  await page
    .getByRole('heading', { name: 'Central de reposição', exact: true })
    .scrollIntoViewIfNeeded();
  await page.screenshot({ path: `../../.local/replenishment-${info.project.name}-overview.png` });
  await meat.scrollIntoViewIfNeeded();
  await page.screenshot({ path: `../../.local/replenishment-${info.project.name}-cards.png` });
  expect(
    await page.evaluate(async () => {
      const scroll = await document.querySelector('ion-content')!.getScrollElement();
      return (
        scroll.scrollWidth <= scroll.clientWidth &&
        document.documentElement.scrollWidth <= innerWidth
      );
    }),
  ).toBe(true);
  expect(errors).toEqual([]);
  expect(state.writes).toBe(0);
});

test('reposição: busca por fornecedor, filas e inativos combinam com os contadores', async ({
  page,
}) => {
  const state = await setup(page);
  await login(page);
  await page.getByLabel('Buscar ingrediente ou fornecedor', { exact: true }).fill('Serra');
  await page.getByLabel('Incluir ingredientes inativos', { exact: true }).check();
  await page.getByRole('button', { name: 'Buscar estoque', exact: true }).click();
  await expect(page.getByRole('article')).toHaveCount(3);
  await expect(page.getByRole('article', { name: 'Estoque de Queijo', exact: true })).toContainText(
    'Novos movimentos exigem reativação',
  );
  const summary = page.getByRole('group', { name: 'Resumo do estoque' });
  await summary.getByRole('button', { name: /2\s*Sem movimentação/ }).click();
  await expect(page.getByRole('article')).toHaveCount(2);
  expect(state.queries.at(-1)?.get('search')).toBe('Serra');
  expect(state.queries.at(-1)?.get('status')).toBe('Unrecorded');
  expect(state.queries.at(-1)?.get('includeInactive')).toBe('true');
  await expect(summary.getByRole('button', { name: /3\s*Todos/ })).toBeVisible();
  await expect(summary.getByRole('button', { name: /2\s*Sem movimentação/ })).toHaveAttribute(
    'aria-pressed',
    'true',
  );
  await page.getByLabel('Buscar ingrediente ou fornecedor', { exact: true }).fill('inexistente');
  await page.getByRole('button', { name: 'Buscar estoque', exact: true }).click();
  await expect(page.getByRole('status')).toHaveText('0 ingrediente(s) nesta busca.');
  await expect(page.getByText('Nenhum ingrediente nesta página.', { exact: false })).toBeVisible();
  expect(state.writes).toBe(0);
});

test('reposição: paginação e atualização preservam filtros aplicados enquanto há edição', async ({
  page,
}) => {
  const state = await setup(page);
  for (let index = 0; index < 21; index++) {
    state.items.push({
      ...state.items[2],
      ingredientId: 'extra-' + index,
      name: 'Extra ' + index,
      supplier: 'Lote',
    });
  }
  await login(page);
  await page.getByLabel('Buscar ingrediente ou fornecedor', { exact: true }).fill('Lote');
  await page.getByRole('button', { name: 'Buscar estoque', exact: true }).click();
  await expect(page.getByRole('status')).toHaveText('21 ingrediente(s) nesta busca.');
  await page
    .getByLabel('Buscar ingrediente ou fornecedor', { exact: true })
    .fill('Ainda não aplicar');
  await page.getByRole('button', { name: 'Próxima', exact: true }).click();
  await expect(page.getByText('Página 2', { exact: true })).toBeVisible();
  await expect(page.getByRole('article')).toHaveCount(1);
  expect(state.queries.at(-1)?.get('search')).toBe('Lote');
  await page.getByRole('button', { name: 'Atualizar estoque', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Atualizar estoque', exact: true })).toBeEnabled();
  expect(state.queries.at(-1)?.get('search')).toBe('Lote');
  expect(state.queries.at(-1)?.get('page')).toBe('2');
  await expect(page.getByText('Filtros alterados.', { exact: false })).toBeVisible();
  await page.getByRole('button', { name: 'Buscar estoque', exact: true }).click();
  await expect(page.getByText('Página 1', { exact: true })).toBeVisible();
  await expect(page.getByRole('status')).toHaveText('0 ingrediente(s) nesta busca.');
});

test('reposição: falha preserva dados e repetição atualiza a mesma busca', async ({ page }) => {
  const state = await setup(page);
  state.readStatus = 503;
  await login(page);
  await expect(page.getByRole('alert')).toBeVisible();
  state.readStatus = 200;
  await page.getByRole('button', { name: 'Tentar novamente', exact: true }).click();
  await expect(page.getByRole('status')).toHaveText('3 ingrediente(s) nesta busca.');
  state.readStatus = 503;
  await page.getByRole('button', { name: 'Atualizar estoque', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('desatualizados');
  await expect(page.getByRole('article')).toHaveCount(3);
  await page.getByLabel('Buscar ingrediente ou fornecedor', { exact: true }).fill('sem aplicar');
  const meat = state.items.find((item) => item.ingredientId === 'meat')!;
  Object.assign(meat, { currentStock: 5, quantityToMinimum: 0, isLowStock: false });
  state.readStatus = 200;
  await page.getByRole('button', { name: 'Tentar novamente', exact: true }).click();
  await expect(page.getByRole('status')).toHaveText('2 ingrediente(s) nesta busca.');
  await expect(page.getByRole('article', { name: 'Estoque de Carne', exact: true })).toHaveCount(0);
  expect(state.queries.at(-1)?.get('search')).toBe('');
  expect(state.writes).toBe(0);
});

test('reposição: atalhos abrem estoque e cadastro e retorno consulta o saldo atual', async ({
  page,
}) => {
  const state = await setup(page);
  await login(page);
  await page.getByRole('link', { name: 'Conferir estoque de Carne', exact: true }).click();
  await expect(
    page.getByRole('heading', { name: 'Estoque do ingrediente', exact: true }),
  ).toBeVisible();
  await expect(page.getByLabel('Quantidade (kg)', { exact: true })).toHaveValue('');
  Object.assign(state.items[2], { currentStock: 5, isLowStock: false, quantityToMinimum: 0 });
  await page.getByRole('link', { name: 'Voltar à central de reposição', exact: true }).click();
  await expect(page.getByRole('article', { name: 'Estoque de Carne', exact: true })).toHaveCount(0);
  await page
    .getByRole('group', { name: 'Resumo do estoque' })
    .getByRole('button', { name: /4\s*Todos/ })
    .click();
  await page.getByRole('link', { name: 'Editar cadastro de Carne', exact: true }).click();
  await expect(page).toHaveURL(/\/equipe\/ingredientes\/meat$/);
  await expect(page.getByLabel('Nome', { exact: true })).toHaveValue('Carne');
  expect(state.writes).toBe(0);
});

for (const role of ['Attendant', 'Kitchen', 'Dispatch']) {
  test('reposição: ' + role + ' não acessa a central', async ({ page }) => {
    const state = await setup(page, role);
    await login(page, '/equipe');
    await expect(page.getByRole('link', { name: 'Reposição', exact: true })).toHaveCount(0);
    await page.evaluate(() => {
      history.pushState(null, '', '/equipe/reposicao');
      dispatchEvent(new PopStateEvent('popstate'));
    });
    await expect(page).toHaveURL(/\/equipe$/);
    expect(state.queries).toHaveLength(0);
  });
}

test('reposição: sessão expirada retorna ao login com destino preservado', async ({ page }) => {
  const state = await setup(page);
  await login(page);
  await expect(page.getByRole('status')).toHaveText('3 ingrediente(s) nesta busca.');
  state.readStatus = 401;
  await page.getByRole('button', { name: 'Atualizar estoque', exact: true }).click();
  await expect(page).toHaveURL(/\/entrar\?returnUrl=%2Fequipe%2Freposicao/);
  expect(state.writes).toBe(0);
});
