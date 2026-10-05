import { expect, Page, test } from '@playwright/test';
import type {
  PublicCheckoutInput,
  PublicOrderInput,
  PublicOrderReceipt,
} from '../src/app/core/services/public-checkout-api.service';

const recoveryKey = 'made-in-minas.public-checkout.v1';

async function setup(page: Page) {
  const state = {
    reviews: [] as PublicCheckoutInput[],
    orders: [] as PublicOrderInput[],
    price: 29.9,
    reviewStatus: 200,
    orderStatus: 201,
    code: 'OrderReviewChanged',
    loseResponse: false,
    gate: null as Promise<void> | null,
    receipts: new Map<string, PublicOrderReceipt>(),
  };
  const user = {
    id: 'staff',
    name: 'Ana',
    username: 'ana',
    role: 'Administrator',
    permissions: ['orders.manage'],
  };
  await page.route('**/api/**', async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    if (path === '/api/menu') {
      expect(request.headers()['authorization']).toBeUndefined();
      return route.fulfill({
        json: {
          categories: [{ id: 'category', name: 'Lanches' }],
          items: [
            {
              id: 'product',
              categoryId: 'category',
              name: 'Uai Sô',
              description: null,
              price: 29.9,
              isAvailable: true,
              imageUrl: null,
            },
          ],
          page: 1,
          pageSize: 24,
          totalCount: 1,
        },
      });
    }
    if (path === '/api/public-checkout/review') {
      expect(request.headers()['authorization']).toBeUndefined();
      const input = request.postDataJSON() as PublicCheckoutInput;
      expect(Object.keys(input).sort()).toEqual(['cart', 'name', 'phone']);
      state.reviews.push(input);
      if (state.reviewStatus !== 200) {
        return route.fulfill({ status: state.reviewStatus, json: { code: state.code } });
      }
      const items = input.cart.items.map((item) => ({
        ...item,
        name: 'Uai Sô',
        unitPrice: state.price,
        lineTotal: Math.round(state.price * item.quantity * 100) / 100,
      }));
      const total = items.reduce((sum, item) => sum + item.lineTotal, 0);
      return route.fulfill({
        json: {
          name: input.name.trim(),
          phone: '+5531999991234',
          fulfillment: 'Pickup',
          items,
          notes: input.cart.notes,
          subtotal: total,
          deliveryFee: 0,
          total,
          reviewToken: 'A'.repeat(64),
          calculatedAt: new Date().toISOString(),
        },
      });
    }
    if (path === '/api/public-checkout/orders') {
      expect(request.headers()['authorization']).toBeUndefined();
      const input = request.postDataJSON() as PublicOrderInput;
      expect(Object.keys(input).sort()).toEqual(['checkout', 'requestId', 'reviewToken']);
      expect(Object.keys(input.checkout.cart.items[0]).sort()).toEqual([
        'notes',
        'productId',
        'quantity',
      ]);
      state.orders.push(input);
      if (state.gate) {
        await state.gate;
      }
      if (state.orderStatus >= 400) {
        return route.fulfill({ status: state.orderStatus, json: { code: state.code } });
      }
      const existing = state.receipts.get(input.requestId);
      const receipt = existing ?? {
        number: 1542,
        fulfillment: 'Pickup' as const,
        total: Math.round(state.price * input.checkout.cart.items[0].quantity * 100) / 100,
        createdAt: '2026-10-05T14:00:00Z',
        tracking: { token: 'private-order-access', expiresAt: '2026-10-12T14:00:00Z' },
      };
      state.receipts.set(input.requestId, receipt);
      if (state.loseResponse) {
        return route.abort('failed');
      }
      return route.fulfill({ status: existing ? 200 : 201, json: receipt });
    }
    if (path === '/api/auth/login') {
      return route.fulfill({
        json: {
          user,
          accessToken: 'staff-token',
          tokenType: 'Bearer',
          expiresAt: new Date(Date.now() + 900000).toISOString(),
        },
      });
    }
    if (path === '/api/auth/me') {
      expect(request.headers()['authorization']).toBe('Bearer staff-token');
      return route.fulfill({ json: user });
    }
    return route.fulfill({ status: 404, json: {} });
  });
  return state;
}

async function start(page: Page) {
  await page.goto('/pedido');
  await page.getByRole('button', { name: 'Adicionar Uai Sô', exact: true }).click();
  await page.getByRole('link', { name: 'Ver carrinho (1)', exact: true }).click();
  await page.getByLabel('Quantidade do item 1', { exact: true }).fill('2');
  await page.getByLabel('Observações do item 1', { exact: true }).fill('Sem cebola');
  await page.getByRole('link', { name: 'Continuar para retirada', exact: true }).click();
  await expect(page).toHaveURL(/\/pedido\/finalizar$/);
}

async function review(page: Page) {
  await page.getByLabel('Seu nome', { exact: true }).fill('Maria');
  await page.getByLabel('Telefone com DDD', { exact: true }).fill('(31) 99999-1234');
  await page.getByRole('button', { name: 'Revisar pedido para retirada', exact: true }).click();
  await expect(page.getByRole('region', { name: 'Pedido revisado' })).toBeVisible();
}

test('visitante revisa dados e envia retirada com preços atuais da API', async ({ page }) => {
  const state = await setup(page);
  await start(page);
  state.price = 33.2;
  await review(page);
  await expect(page.getByRole('region', { name: 'Pedido revisado' })).toContainText('R$ 66,40');
  expect(state.orders).toHaveLength(0);
  await page.getByRole('button', { name: 'Enviar pedido para retirada' }).click();
  await expect(page.getByRole('region', { name: 'Pedido recebido' })).toContainText('#1542');
  await expect(page.getByRole('region', { name: 'Pedido recebido' })).toContainText('R$ 66,40');
  await expect(
    page.getByText('Nenhuma cobrança foi realizada pelo site.', { exact: false }),
  ).toBeVisible();
  expect(state.orders).toHaveLength(1);
  expect(state.orders[0].checkout.cart.items).toEqual([
    { productId: 'product', quantity: 2, notes: 'Sem cebola' },
  ]);
  const stored = await page.evaluate((key) => sessionStorage.getItem(key), recoveryKey);
  expect(stored).not.toContain('Maria');
  expect(stored).not.toContain('phone');
  expect(stored).toContain('1542');
  expect(stored).toContain('private-order-access');
  await expect(page.getByRole('link', { name: 'Acompanhar meu pedido' })).toBeVisible();
});

test('dados inválidos não são revisados e editar contato invalida a revisão', async ({ page }) => {
  const state = await setup(page);
  await start(page);
  await page.getByRole('button', { name: 'Revisar pedido para retirada' }).click();
  await expect(page.getByRole('alert')).toContainText('Informe seu nome');
  await page.getByLabel('Seu nome').fill('Maria');
  await page.getByLabel('Telefone com DDD').fill('123');
  await page.getByRole('button', { name: 'Revisar pedido para retirada' }).click();
  expect(state.reviews).toHaveLength(0);
  await review(page);
  await page.getByLabel('Seu nome').fill('Ana');
  await expect(page.getByRole('region', { name: 'Pedido revisado' })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Enviar pedido para retirada' })).toHaveCount(0);
});

test('revisão falha sem criar pedido e pode ser repetida', async ({ page }) => {
  const state = await setup(page);
  await start(page);
  state.reviewStatus = 500;
  await page.getByLabel('Seu nome').fill('Maria');
  await page.getByLabel('Telefone com DDD').fill('31999991234');
  await page.getByRole('button', { name: 'Revisar pedido para retirada' }).click();
  await expect(page.getByRole('alert')).toContainText('Não foi possível revisar');
  expect(state.orders).toHaveLength(0);
  state.reviewStatus = 200;
  await page.getByRole('button', { name: 'Revisar pedido para retirada' }).click();
  await expect(page.getByRole('region', { name: 'Pedido revisado' })).toBeVisible();
});

test('mudança de preço rejeita envio, preserva seleção e exige nova revisão', async ({ page }) => {
  const state = await setup(page);
  await start(page);
  await review(page);
  state.orderStatus = 409;
  await page.getByRole('button', { name: 'Enviar pedido para retirada' }).click();
  await expect(page.getByRole('alert')).toContainText('os dados mudaram');
  await expect(page.getByRole('region', { name: 'Pedido revisado' })).toHaveCount(0);
  state.price = 34;
  state.orderStatus = 201;
  await page.getByRole('button', { name: 'Revisar pedido para retirada' }).click();
  await expect(page.getByRole('region', { name: 'Pedido revisado' })).toContainText('R$ 68,00');
  await page.getByRole('button', { name: 'Enviar pedido para retirada' }).click();
  await expect(page.getByRole('region', { name: 'Pedido recebido' })).toBeVisible();
  expect(state.orders[0].requestId).not.toBe(state.orders[1].requestId);
});

test('resposta perdida é recuperada após recarregar com a mesma tentativa', async ({ page }) => {
  const state = await setup(page);
  await start(page);
  await review(page);
  state.loseResponse = true;
  await page.getByRole('button', { name: 'Enviar pedido para retirada' }).click();
  await expect(page.getByRole('alert')).toContainText('confirmar o resultado');
  await page.reload();
  await expect(page.getByRole('region', { name: 'Conferir envio' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Enviar pedido para retirada' })).toHaveCount(0);
  state.loseResponse = false;
  await page.getByRole('button', { name: 'Conferir envio', exact: true }).click();
  await expect(page.getByRole('region', { name: 'Pedido recebido' })).toContainText('#1542');
  expect(state.orders[1]).toEqual(state.orders[0]);
  expect(state.receipts.size).toBe(1);
  await page.reload();
  await expect(page.getByRole('region', { name: 'Pedido recebido' })).toContainText('#1542');
  expect(state.orders).toHaveLength(2);
  await page.getByRole('button', { name: 'Montar outro pedido' }).click();
  await expect(page.getByRole('link', { name: 'Ver carrinho (0)', exact: true })).toBeVisible();
  expect(await page.evaluate((key) => sessionStorage.getItem(key), recoveryKey)).toBeNull();
});

test('envio pendente bloqueia mudanças pelo carrinho e novas adições no menu', async ({ page }) => {
  const state = await setup(page);
  await start(page);
  await review(page);
  state.orderStatus = 500;
  await page.getByRole('button', { name: 'Enviar pedido para retirada' }).click();
  await expect(page.getByRole('alert')).toBeVisible();
  await page.getByRole('link', { name: 'Voltar ao carrinho' }).click();
  await expect(page.getByRole('button', { name: /Remover item|Limpar carrinho/ })).toHaveCount(0);
  await page.getByRole('link', { name: 'Voltar ao cardápio' }).click();
  await page.getByRole('button', { name: 'Adicionar Uai Sô', exact: true }).click();
  await expect(
    page.getByText('Confira o envio anterior antes de montar outro pedido.', { exact: true }),
  ).toBeVisible();
  await page.getByRole('link', { name: 'Ver envio do pedido' }).click();
  state.orderStatus = 201;
  await page.getByRole('button', { name: 'Conferir envio', exact: true }).click();
  await expect(page.getByRole('region', { name: 'Pedido recebido' })).toBeVisible();
  expect(state.orders[1]).toEqual(state.orders[0]);
});

test('resposta atrasada não libera segundo envio e sair permite recuperar', async ({ page }) => {
  const state = await setup(page);
  await start(page);
  await review(page);
  let release!: () => void;
  state.gate = new Promise<void>((resolve) => {
    release = resolve;
  });
  try {
    await page.getByRole('button', { name: 'Enviar pedido para retirada' }).click();
    await expect(page.getByRole('button', { name: 'Conferindo…' })).toBeDisabled();
    await expect(page.getByLabel('Seu nome')).toHaveCount(0);
    await page.getByRole('link', { name: 'Voltar ao carrinho' }).click();
    await page.getByRole('link', { name: 'Ver envio do pedido' }).click();
  } finally {
    release();
    state.gate = null;
  }
  await page.getByRole('button', { name: 'Conferir envio', exact: true }).click();
  await expect(page.getByRole('region', { name: 'Pedido recebido' })).toBeVisible();
  expect(state.receipts.size).toBe(1);
  expect(state.orders[1]).toEqual(state.orders[0]);
});

test('timeout mantém tentativa e retomada usa a mesma chave', async ({ page }) => {
  const state = await setup(page);
  await page.clock.install();
  await start(page);
  await review(page);
  let release!: () => void;
  state.gate = new Promise<void>((resolve) => {
    release = resolve;
  });
  try {
    await page.getByRole('button', { name: 'Enviar pedido para retirada' }).click();
    await expect(page.getByRole('button', { name: 'Conferindo…' })).toBeDisabled();
    await page.clock.fastForward(16000);
    await expect(page.getByRole('alert')).toContainText('confirmar o resultado');
  } finally {
    release();
    state.gate = null;
  }
  await page.getByRole('button', { name: 'Conferir envio', exact: true }).click();
  await expect(page.getByRole('region', { name: 'Pedido recebido' })).toBeVisible();
  expect(state.orders[1]).toEqual(state.orders[0]);
});

test('limite ou conflito de tentativa preserva recuperação e não libera outra compra', async ({
  page,
}) => {
  const state = await setup(page);
  await start(page);
  await review(page);
  state.orderStatus = 429;
  await page.getByRole('button', { name: 'Enviar pedido para retirada' }).click();
  await expect(page.getByRole('alert')).toBeVisible();
  state.orderStatus = 409;
  state.code = 'OrderRequestConflict';
  await page.getByRole('button', { name: 'Conferir envio', exact: true }).click();
  await expect(page.getByRole('alert')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Revisar pedido para retirada' })).toHaveCount(0);
  expect(state.orders[1]).toEqual(state.orders[0]);
});

test('rejeição definitiva após reload restaura produtos e observações', async ({ page }) => {
  const state = await setup(page);
  await start(page);
  await review(page);
  state.orderStatus = 500;
  await page.getByRole('button', { name: 'Enviar pedido para retirada' }).click();
  await expect(page.getByRole('alert')).toBeVisible();
  await page.reload();
  state.orderStatus = 409;
  state.code = 'CartProductUnavailable';
  await page.getByRole('button', { name: 'Conferir envio', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('os dados mudaram');
  await expect(page.getByLabel('Seu nome')).toHaveValue('Maria');
  await page.getByRole('link', { name: 'Voltar ao carrinho' }).click();
  await expect(page.getByRole('heading', { name: 'Uai Sô', exact: true })).toBeVisible();
  await expect(page.getByLabel('Quantidade do item 1', { exact: true })).toHaveValue('2');
  await expect(page.getByLabel('Observações do item 1', { exact: true })).toHaveValue('Sem cebola');
});

test('falha ao guardar tentativa impede HTTP e informa que envio não iniciou', async ({ page }) => {
  const state = await setup(page);
  await start(page);
  await review(page);
  await page.evaluate(() => {
    Storage.prototype.setItem = () => {
      throw new DOMException('Full', 'QuotaExceededError');
    };
  });
  await page.getByRole('button', { name: 'Enviar pedido para retirada' }).click();
  await expect(page.getByRole('alert')).toContainText('Nenhum envio foi iniciado');
  expect(state.orders).toHaveLength(0);
});

test('recuperação corrompida exige conferir atendimento antes de limpar', async ({ page }) => {
  const state = await setup(page);
  await page.goto('/pedido');
  await page.evaluate((key) => sessionStorage.setItem(key, '{invalid'), recoveryKey);
  await page.goto('/pedido/finalizar');
  await expect(page.getByRole('heading', { name: 'Precisamos conferir seu envio' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Limpar recuperação e voltar' })).toBeDisabled();
  await page.getByRole('checkbox', { name: 'Já conferi com o atendimento.' }).check();
  await page.getByRole('button', { name: 'Limpar recuperação e voltar' }).click();
  await expect(page).toHaveURL(/\/pedido$/);
  expect(state.orders).toHaveLength(0);
});

test('consulta pública não envia JWT e falha não encerra a sessão da equipe', async ({ page }) => {
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
  await page.getByRole('link', { name: 'Ver carrinho (1)' }).click();
  await page.getByRole('link', { name: 'Continuar para retirada' }).click();
  await review(page);
  state.orderStatus = 401;
  await page.getByRole('button', { name: 'Enviar pedido para retirada' }).click();
  await expect(page.getByRole('alert')).toBeVisible();
  await page.getByRole('link', { name: /^MADE IN MINAS/ }).click();
  await page.getByRole('link', { name: 'Área da equipe' }).click();
  await expect(page).toHaveURL(/\/equipe$/);
});

test('contato e observações são escapados e a revisão cabe no celular', async ({ page }) => {
  await setup(page);
  await start(page);
  await page.getByLabel('Seu nome').fill('<b>' + 'Maria '.repeat(15) + '</b>');
  await page.getByLabel('Telefone com DDD').fill('31999991234');
  await page.getByRole('button', { name: 'Revisar pedido para retirada' }).click();
  await expect(page.getByRole('region', { name: 'Pedido revisado' })).toContainText('<b>');
  await expect(page.locator('[aria-label="Pedido revisado"] b')).toHaveCount(0);
  const width = await page
    .locator('main')
    .evaluate((main) => ({ content: main.scrollWidth, visible: main.clientWidth }));
  expect(width.content).toBeLessThanOrEqual(width.visible + 1);
});
