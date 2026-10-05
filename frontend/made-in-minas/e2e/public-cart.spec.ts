import { expect, Page, test } from '@playwright/test';
import type { PublicCartInput } from '../src/app/core/services/public-cart-api.service';

async function setup(page: Page) {
  const products = [
    {
      id: 'burger',
      categoryId: 'food',
      name: 'Uai Sô',
      description: 'Pão e queijo.',
      price: 29.9,
      imageUrl: null,
      isAvailable: true,
    },
    {
      id: 'drink',
      categoryId: 'food',
      name: 'Suco',
      description: null,
      price: 5.15,
      imageUrl: null,
      isAvailable: true,
    },
    {
      id: 'paused',
      categoryId: 'food',
      name: 'Especial',
      description: null,
      price: 39.9,
      imageUrl: null,
      isAvailable: false,
    },
  ];
  const state = {
    products,
    status: 200,
    quotes: [] as PublicCartInput[],
    writes: [] as string[],
    gate: null as Promise<void> | null,
  };
  const user = {
    id: 'staff',
    name: 'Ana',
    username: 'ana',
    role: 'Administrator',
    permissions: ['users.manage'],
  };
  await page.route('**/api/**', async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    if (path === '/api/menu') {
      expect(request.headers()['authorization']).toBeUndefined();
      return route.fulfill({
        json: {
          categories: [{ id: 'food', name: 'Cardápio' }],
          items: state.products,
          page: 1,
          pageSize: 24,
          totalCount: state.products.length,
        },
      });
    }
    if (path === '/api/public-cart/quote') {
      expect(request.method()).toBe('POST');
      expect(request.headers()['authorization']).toBeUndefined();
      const input = request.postDataJSON() as PublicCartInput;
      expect(Object.keys(input).sort()).toEqual(['items', 'notes']);
      input.items.forEach((item) =>
        expect(Object.keys(item).sort()).toEqual(['notes', 'productId', 'quantity']),
      );
      state.quotes.push(input);
      if (state.gate) {
        await state.gate;
      }
      if (state.status !== 200) {
        return route.fulfill({
          status: state.status,
          json: { code: state.status === 409 ? 'CartProductUnavailable' : 'Failure' },
        });
      }
      const items = input.items.map((line) => {
        const product = state.products.find((item) => item.id === line.productId)!;
        return {
          productId: product.id,
          name: product.name,
          quantity: line.quantity,
          unitPrice: product.price,
          lineTotal: Math.round(product.price * line.quantity * 100) / 100,
          notes: line.notes?.trim() || null,
        };
      });
      return route.fulfill({
        json: {
          items,
          notes: input.notes?.trim() || null,
          subtotal:
            Math.round(items.reduce((total, item) => total + item.lineTotal, 0) * 100) / 100,
          calculatedAt: new Date().toISOString(),
        },
      });
    }
    if (path === '/api/auth/login') {
      return route.fulfill({
        json: {
          user,
          tokenType: 'Bearer',
          accessToken: 'staff-token',
          expiresAt: new Date(Date.now() + 900000).toISOString(),
        },
      });
    }
    if (path === '/api/auth/me') {
      expect(request.headers()['authorization']).toBe('Bearer staff-token');
      return route.fulfill({ json: user });
    }
    if (request.method() !== 'GET') {
      state.writes.push(path);
    }
    return route.fulfill({ status: 404, json: {} });
  });
  return state;
}

async function start(page: Page) {
  await page.goto('/pedido');
  await page.getByRole('button', { name: 'Adicionar Uai Sô', exact: true }).click();
  await page.getByRole('link', { name: 'Ver carrinho (1)', exact: true }).click();
  await expect(page).toHaveURL(/\/pedido\/carrinho$/);
}

test('visitante monta itens e revisa somente valores devolvidos pela API', async ({ page }) => {
  const state = await setup(page);
  await start(page);
  await expect(page.getByRole('region', { name: 'Valores revisados' })).toHaveCount(0);
  await page.getByLabel('Quantidade do item 1', { exact: true }).fill('2');
  await page.getByLabel('Observações do item 1', { exact: true }).fill('Sem cebola');
  await page.getByLabel('Observações gerais', { exact: true }).fill('Embalar separado');
  state.products[0].price = 33.2;
  state.products[0].name = 'Uai atualizado';
  await page.getByRole('button', { name: 'Revisar carrinho', exact: true }).click();
  const quote = page.getByRole('region', { name: 'Valores revisados' });
  await expect(quote).toContainText('R$ 66,40');
  await expect(quote).toContainText('R$ 33,20');
  await expect(quote).toContainText('Uai atualizado');
  await expect(quote).toContainText('Sem cebola');
  await expect(quote).toContainText('Embalar separado');
  await expect(page.getByRole('heading', { name: 'Uai atualizado', exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: /confirmar pedido|pagar/i })).toHaveCount(0);
  expect(state.quotes[0].items).toEqual([
    { productId: 'burger', quantity: 2, notes: 'Sem cebola' },
  ]);
  expect(state.writes).toEqual([]);
});

test('produto pausado não pode ser adicionado e adições repetidas somam quantidade', async ({
  page,
}) => {
  await setup(page);
  await page.goto('/pedido');
  await expect(
    page.getByRole('button', { name: 'Adicionar Especial', exact: true }),
  ).toBeDisabled();
  await page.getByRole('button', { name: 'Adicionar Uai Sô', exact: true }).click();
  await page.getByRole('button', { name: 'Adicionar Uai Sô', exact: true }).click();
  await page.getByRole('link', { name: 'Ver carrinho (2)', exact: true }).click();
  await expect(page.getByRole('article')).toHaveCount(1);
  await expect(page.getByLabel('Quantidade do item 1', { exact: true })).toHaveValue('2');
});

test('observações diferentes mantêm linhas distintas e limite por produto', async ({ page }) => {
  const state = await setup(page);
  await start(page);
  await page.getByLabel('Observações do item 1', { exact: true }).fill('Sem molho');
  await page.getByRole('link', { name: 'Adicionar outros produtos', exact: true }).click();
  await page.getByRole('button', { name: 'Adicionar Uai Sô', exact: true }).click();
  await page.getByRole('link', { name: 'Ver carrinho (2)', exact: true }).click();
  await expect(page.getByRole('article')).toHaveCount(2);
  await page.getByLabel('Quantidade do item 1', { exact: true }).fill('99');
  await page.getByRole('button', { name: 'Revisar carrinho', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('somando todos os itens');
  expect(state.quotes).toHaveLength(0);
  await page.getByLabel('Quantidade do item 1', { exact: true }).fill('98');
  await page.getByLabel('Observações do item 2', { exact: true }).fill('Com molho');
  await page.getByRole('button', { name: 'Revisar carrinho', exact: true }).click();
  await expect(page.getByRole('region', { name: 'Valores revisados' })).toContainText(
    'R$ 2.960,10',
  );
  expect(state.quotes[0].items.map((item) => item.notes)).toEqual(['Sem molho', 'Com molho']);
});

test('cada edição ou remoção invalida a revisão sem perder o restante do carrinho', async ({
  page,
}) => {
  await setup(page);
  await start(page);
  const quote = page.getByRole('region', { name: 'Valores revisados' });
  await page.getByRole('button', { name: 'Revisar carrinho', exact: true }).click();
  await expect(quote).toBeVisible();
  await page.getByLabel('Quantidade do item 1', { exact: true }).fill('3');
  await expect(quote).toHaveCount(0);
  await page.getByRole('button', { name: 'Revisar carrinho', exact: true }).click();
  await expect(quote).toBeVisible();
  await page.getByLabel('Observações do item 1', { exact: true }).fill('Sem cebola');
  await expect(quote).toHaveCount(0);
  await page.getByRole('button', { name: 'Revisar carrinho', exact: true }).click();
  await expect(quote).toBeVisible();
  await page.getByLabel('Observações gerais', { exact: true }).fill('Embalagens separadas');
  await expect(quote).toHaveCount(0);
  await page.getByRole('button', { name: 'Remover item 1', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Seu carrinho está vazio' })).toBeVisible();
  await page.getByRole('link', { name: 'Ver cardápio', exact: true }).click();
  await page.getByRole('button', { name: 'Adicionar Suco', exact: true }).click();
  await page.getByRole('link', { name: 'Ver carrinho (1)', exact: true }).click();
  await expect(page.getByLabel('Observações gerais', { exact: true })).toHaveValue('');
});

test('quantidades inválidas não enviam consulta e a seleção pode ser corrigida', async ({
  page,
}) => {
  const state = await setup(page);
  await start(page);
  for (const value of ['', '0', '100', '1.5']) {
    await page.getByLabel('Quantidade do item 1', { exact: true }).fill(value);
    await page.getByRole('button', { name: 'Revisar carrinho', exact: true }).click();
    await expect(page.getByRole('alert')).toContainText('inteiras de 1 a 99');
  }
  expect(state.quotes).toHaveLength(0);
  await page.getByLabel('Quantidade do item 1', { exact: true }).fill('99');
  await page.getByRole('link', { name: 'Adicionar outros produtos', exact: true }).click();
  await page.getByRole('button', { name: 'Adicionar Uai Sô', exact: true }).click();
  await expect(
    page.getByText('O limite é de 99 unidades por produto.', { exact: true }),
  ).toBeVisible();
  await expect(page.getByRole('link', { name: 'Ver carrinho (99)', exact: true })).toBeVisible();
});

test('limpar exige confirmação e remove também as observações gerais', async ({ page }) => {
  await setup(page);
  await start(page);
  await page.getByLabel('Observações gerais', { exact: true }).fill('Observação temporária');
  await page.getByRole('button', { name: 'Limpar carrinho', exact: true }).click();
  await page.getByRole('button', { name: 'Manter itens', exact: true }).click();
  await expect(page.getByRole('article')).toHaveCount(1);
  await page.getByRole('button', { name: 'Limpar carrinho', exact: true }).click();
  await page.getByRole('button', { name: 'Sim, limpar', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Seu carrinho está vazio' })).toBeVisible();
  await page.getByRole('link', { name: 'Ver cardápio', exact: true }).click();
  await page.getByRole('button', { name: 'Adicionar Suco', exact: true }).click();
  await page.getByRole('link', { name: 'Ver carrinho (1)', exact: true }).click();
  await expect(page.getByLabel('Observações gerais', { exact: true })).toHaveValue('');
});

test('navegação mantém itens, voltar descarta revisão e recarregar esvazia o carrinho', async ({
  page,
}) => {
  await setup(page);
  await start(page);
  await page.getByRole('button', { name: 'Revisar carrinho', exact: true }).click();
  await expect(page.getByRole('region', { name: 'Valores revisados' })).toBeVisible();
  await page.getByRole('link', { name: 'Voltar ao cardápio', exact: true }).click();
  await expect(page.getByRole('link', { name: 'Ver carrinho (1)', exact: true })).toBeVisible();
  await page.goBack();
  await expect(page.getByRole('heading', { name: 'Uai Sô', exact: true })).toBeVisible();
  await expect(page.getByRole('region', { name: 'Valores revisados' })).toHaveCount(0);
  await page.reload();
  await expect(page.getByRole('heading', { name: 'Seu carrinho está vazio' })).toBeVisible();
  expect(
    await page.evaluate(() => ({ local: localStorage.length, session: sessionStorage.length })),
  ).toEqual({ local: 0, session: 0 });
});

test('falhas preservam itens e indisponibilidade nunca mostra subtotal antigo', async ({
  page,
}) => {
  const state = await setup(page);
  await start(page);
  await page.getByRole('button', { name: 'Revisar carrinho', exact: true }).click();
  await expect(page.getByRole('region', { name: 'Valores revisados' })).toBeVisible();
  for (const status of [409, 500, 401]) {
    state.status = status;
    await page.getByRole('button', { name: 'Revisar carrinho', exact: true }).click();
    await expect(page.getByRole('alert')).toContainText(
      status === 409 ? 'ficou indisponível' : 'Seus itens foram mantidos',
    );
    await expect(page.getByRole('region', { name: 'Valores revisados' })).toHaveCount(0);
    await expect(page.getByLabel('Quantidade do item 1', { exact: true })).toHaveValue('1');
    await expect(page).toHaveURL(/\/pedido\/carrinho$/);
  }
  state.status = 200;
  await page.getByRole('button', { name: 'Revisar carrinho', exact: true }).click();
  await expect(page.getByRole('region', { name: 'Valores revisados' })).toBeVisible();
  expect(state.writes).toEqual([]);
});

test('durante revisão bloqueia edição e saída ignora resposta antiga', async ({ page }) => {
  const state = await setup(page);
  await start(page);
  let release!: () => void;
  state.gate = new Promise<void>((resolve) => {
    release = resolve;
  });
  try {
    await page.getByRole('button', { name: 'Revisar carrinho', exact: true }).click();
    await expect(page.getByRole('status')).toHaveText('Consultando preços e disponibilidade…');
    await expect(page.getByLabel('Quantidade do item 1', { exact: true })).toBeDisabled();
    await expect(page.getByRole('button', { name: 'Remover item 1', exact: true })).toBeDisabled();
    await expect(page.getByRole('button', { name: 'Revisando…', exact: true })).toBeDisabled();
    await page.getByRole('link', { name: 'Voltar ao cardápio', exact: true }).click();
    await page.getByRole('link', { name: 'Ver carrinho (1)', exact: true }).click();
  } finally {
    release();
    state.gate = null;
  }
  await expect(page.getByRole('region', { name: 'Valores revisados' })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Revisar carrinho', exact: true })).toBeEnabled();
});

test('consulta pública não envia token da equipe nem encerra sua sessão', async ({ page }) => {
  const state = await setup(page);
  await page.goto('/entrar');
  await page.getByLabel('Login', { exact: true }).fill('ana');
  await page.getByLabel('Senha', { exact: true }).fill('Senha teste 123!');
  await page.getByRole('button', { name: 'Entrar', exact: true }).click();
  await expect(page).toHaveURL(/\/equipe$/);
  await page.evaluate(() => {
    history.pushState(null, '', '/pedido');
    dispatchEvent(new PopStateEvent('popstate'));
  });
  await page.getByRole('button', { name: 'Adicionar Uai Sô', exact: true }).click();
  await page.getByRole('link', { name: 'Ver carrinho (1)', exact: true }).click();
  state.status = 401;
  await page.getByRole('button', { name: 'Revisar carrinho', exact: true }).click();
  await expect(page.getByRole('alert')).toBeVisible();
  await page.getByRole('link', { name: 'Voltar ao cardápio', exact: true }).click();
  await page.getByRole('link', { name: 'Área da equipe', exact: true }).click();
  await expect(page).toHaveURL(/\/equipe$/);
});

test('timeout libera revisão mantendo itens para uma nova tentativa', async ({ page }) => {
  const state = await setup(page);
  await page.clock.install();
  await start(page);
  let release!: () => void;
  state.gate = new Promise<void>((resolve) => {
    release = resolve;
  });
  try {
    await page.getByRole('button', { name: 'Revisar carrinho', exact: true }).click();
    await expect(page.getByRole('status')).toHaveText('Consultando preços e disponibilidade…');
    await page.clock.fastForward(16000);
    await expect(page.getByRole('alert')).toContainText('Seus itens foram mantidos');
  } finally {
    release();
    state.gate = null;
  }
  await expect(page.getByLabel('Quantidade do item 1', { exact: true })).toHaveValue('1');
  await page.getByRole('button', { name: 'Revisar carrinho', exact: true }).click();
  await expect(page.getByRole('region', { name: 'Valores revisados' })).toBeVisible();
});

test('nomes e observações são texto e cabem no celular', async ({ page }) => {
  const state = await setup(page);
  state.products[0].name = '<b>' + 'Hambúrguer '.repeat(10) + '</b>';
  await page.goto('/pedido');
  await page
    .getByRole('button', { name: 'Adicionar ' + state.products[0].name, exact: true })
    .click();
  await page.getByRole('link', { name: 'Ver carrinho (1)', exact: true }).click();
  await page
    .getByLabel('Observações do item 1', { exact: true })
    .fill('<script>alert(1)</script>' + 'x'.repeat(200));
  await page.getByRole('button', { name: 'Revisar carrinho', exact: true }).click();
  await expect(page.getByRole('region', { name: 'Valores revisados' })).toContainText(
    '<script>alert(1)</script>',
  );
  await expect(page.locator('.quote script, .cart-line h2 b')).toHaveCount(0);
  const width = await page
    .locator('main')
    .evaluate((main) => ({ content: main.scrollWidth, visible: main.clientWidth }));
  expect(width.content).toBeLessThanOrEqual(width.visible + 1);
});
