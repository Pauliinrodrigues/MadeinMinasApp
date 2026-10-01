import { expect, Page, test } from '@playwright/test';
import type { CartProduct, CartQuoteInput } from '../src/app/core/services/cart-api.service';
import type { Address, Customer } from '../src/app/core/services/customer-api.service';

const customer: Customer = {
  id: 'customer-1',
  name: 'Maria',
  phone: '+5531999991234',
  isActive: true,
  createdAt: '2026-10-01T12:00:00Z',
  updatedAt: '2026-10-01T12:00:00Z',
};
const address: Address = {
  id: 'address-1',
  customerId: customer.id,
  street: 'Rua A',
  number: '12',
  neighborhood: 'Centro',
  city: 'Belo Horizonte',
  state: 'MG',
  complement: 'Casa',
  postalCode: '30110000',
  reference: 'Portão verde',
  isActive: true,
  createdAt: customer.createdAt,
  updatedAt: customer.updatedAt,
};
const product: CartProduct = {
  id: 'product-1',
  categoryId: 'category-1',
  categoryName: 'Lanches',
  name: 'Uai Sô',
  description: 'Pão, carne e queijo',
  price: 29.9,
};

async function setup(page: Page, role = 'Attendant') {
  const profile = {
    id: 'staff',
    name: 'Equipe Teste',
    username: 'equipe.teste',
    role,
    permissions: ['Administrator', 'Attendant'].includes(role)
      ? ['customers.manage', 'orders.manage']
      : [],
  };
  const state = {
    products: [{ ...product }],
    customers: [{ ...customer }],
    addresses: [{ ...address }],
    productStatus: 200,
    customerStatus: 200,
    addressStatus: 200,
    quoteStatus: 200,
    quoteCode: 'CartProductUnavailable',
    quoted: [] as CartQuoteInput[],
    quoteGate: null as Promise<void> | null,
    cartRequests: 0,
  };
  await page.route('**/api/**', async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    const path = url.pathname;
    const json = (body: unknown, status = 200) => route.fulfill({ status, json: body });
    if (path === '/api/auth/login') {
      return json({
        accessToken: 'cart-token',
        tokenType: 'Bearer',
        expiresAt: new Date(Date.now() + 900000).toISOString(),
        user: profile,
      });
    }
    expect(request.headers()['authorization']).toBe('Bearer cart-token');
    if (path === '/api/auth/me') {
      return json(profile);
    }
    if (path === '/api/auth/logout') {
      return json({});
    }
    const paginate = <T>(items: T[]) => {
      const current = Number(url.searchParams.get('page') ?? 1);
      return {
        items: items.slice((current - 1) * 20, current * 20),
        page: current,
        pageSize: 20,
        totalCount: items.length,
      };
    };
    if (path.startsWith('/api/cart')) {
      state.cartRequests++;
    }
    if (path === '/api/cart/products') {
      if (state.productStatus !== 200) {
        return json({}, state.productStatus);
      }
      const search = (url.searchParams.get('search') ?? '').toLowerCase();
      return json(
        paginate(state.products.filter((item) => item.name.toLowerCase().includes(search))),
      );
    }
    if (path === '/api/customers') {
      expect(url.searchParams.get('isActive')).toBe('true');
      if (state.customerStatus !== 200) {
        return json({}, state.customerStatus);
      }
      const search = (url.searchParams.get('search') ?? '').toLowerCase();
      return json(
        paginate(
          state.customers.filter(
            (item) => item.name.toLowerCase().includes(search) || item.phone.includes(search),
          ),
        ),
      );
    }
    const addressMatch = path.match(/^\/api\/customers\/([^/]+)\/addresses$/);
    if (addressMatch) {
      expect(url.searchParams.get('isActive')).toBe('true');
      if (state.addressStatus !== 200) {
        return json({}, state.addressStatus);
      }
      return json(paginate(state.addresses.filter((item) => item.customerId === addressMatch[1])));
    }
    if (path === '/api/cart/quote') {
      const input = request.postDataJSON() as CartQuoteInput;
      state.quoted.push(input);
      if (state.quoteGate) {
        await state.quoteGate;
      }
      if (state.quoteStatus !== 200) {
        return json(state.quoteStatus === 503 ? {} : { code: state.quoteCode }, state.quoteStatus);
      }
      const owner = state.customers.find((item) => item.id === input.customerId)!;
      const destination = state.addresses.find((item) => item.id === input.addressId);
      const items = input.items.map((item) => {
        const product = state.products.find((entry) => entry.id === item.productId)!;
        return {
          ...item,
          name: product.name,
          unitPrice: product.price,
          lineTotal: Math.round(product.price * item.quantity * 100) / 100,
        };
      });
      return json({
        customer: owner,
        fulfillment: input.fulfillment,
        address: destination ?? null,
        items,
        notes: input.notes,
        subtotal: items.reduce((sum, item) => sum + item.lineTotal, 0),
        deliveryFee: input.deliveryFee,
        total: items.reduce((sum, item) => sum + item.lineTotal, 0) + input.deliveryFee,
        reviewToken: 'A'.repeat(64),
        calculatedAt: customer.createdAt,
      });
    }
    return json({}, 404);
  });
  return state;
}

async function login(page: Page) {
  await page.goto('/entrar');
  await page.getByLabel('Login', { exact: true }).fill('equipe.teste');
  await page.getByLabel('Senha', { exact: true }).fill('Senha apenas para os testes');
  await page.getByRole('button', { name: 'Entrar', exact: true }).click();
  await expect(page).toHaveURL(/\/equipe$/);
}
async function openCart(page: Page) {
  await login(page);
  await page.getByRole('link', { name: 'Carrinho', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Carrinho da equipe' })).toBeVisible();
}
async function selectAndAdd(page: Page) {
  await page.getByRole('button', { name: 'Selecionar cliente Maria', exact: true }).click();
  await page.getByRole('button', { name: 'Adicionar Uai Sô', exact: true }).click();
}
const review = (page: Page) => page.getByRole('button', { name: 'Revisar carrinho', exact: true });
const summary = (page: Page) =>
  page.getByRole('region', { name: 'Revisão do carrinho', exact: true });

for (const role of ['Administrator', 'Attendant']) {
  test('carrinho: ' + role + ' revisa retirada com valores do servidor', async ({ page }) => {
    const errors: string[] = [];
    page.on('pageerror', (error) => errors.push(error.message));
    const state = await setup(page, role);
    await openCart(page);
    await expect(review(page)).toBeDisabled();
    await selectAndAdd(page);
    await page.getByLabel('Quantidade do item 1', { exact: true }).fill('2');
    await page.getByLabel('Observações do item 1', { exact: true }).fill('Sem cebola');
    await page.getByLabel('Observações gerais', { exact: true }).fill('Embalar separado');
    state.products[0].price = 31.01;
    await review(page).click();
    await expect(summary(page)).toContainText('62,02');
    await expect(summary(page)).toContainText('31,01');
    await expect(summary(page)).toContainText('Sem cebola');
    await expect(summary(page)).toContainText('Nenhum pedido foi criado');
    expect(state.quoted[0]).toEqual({
      customerId: customer.id,
      fulfillment: 'Pickup',
      addressId: null,
      deliveryFee: 0,
      items: [{ productId: product.id, quantity: 2, notes: 'Sem cebola' }],
      notes: 'Embalar separado',
    });
    expect(errors).toEqual([]);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(
      true,
    );
  });
}

test('carrinho: entrega exige endereço e troca de cliente ou modalidade limpa seleção', async ({
  page,
}) => {
  const state = await setup(page);
  state.customers.push({ ...customer, id: 'customer-2', name: 'João', phone: '+5531988881234' });
  await openCart(page);
  await selectAndAdd(page);
  await page.getByRole('combobox', { name: 'Recebimento', exact: true }).selectOption('Delivery');
  await expect(review(page)).toBeDisabled();
  await page.getByRole('button', { name: 'Selecionar endereço Rua A, 12', exact: true }).click();
  await expect(review(page)).toBeDisabled();
  await page.getByLabel('Taxa de entrega (R$)', { exact: true }).fill('4.50');
  await review(page).click();
  await expect(summary(page)).toContainText('Portão verde');
  expect(state.quoted[0].addressId).toBe(address.id);
  await page.getByRole('button', { name: 'Selecionar cliente João', exact: true }).click();
  await expect(summary(page)).toHaveCount(0);
  await expect(review(page)).toBeDisabled();
  await expect(
    page.getByText('Nenhum endereço ativo nesta página.', { exact: false }),
  ).toBeVisible();
  await page.getByRole('combobox', { name: 'Recebimento', exact: true }).selectOption('Pickup');
  await review(page).click();
  await expect(summary(page)).toContainText('João');
  expect(state.quoted[1].addressId).toBeNull();
  expect(state.quoted[1].customerId).toBe('customer-2');
});

test('carrinho: edição e remoção invalidam revisão e limites bloqueiam envio', async ({ page }) => {
  const state = await setup(page);
  await openCart(page);
  await selectAndAdd(page);
  const quantity = page.getByLabel('Quantidade do item 1', { exact: true });
  for (const value of ['0', '-1', '1.5', '100', '']) {
    await quantity.fill(value);
    await expect(review(page)).toBeDisabled();
  }
  await quantity.fill('99');
  await page.getByRole('button', { name: 'Adicionar Uai Sô', exact: true }).click();
  await expect(review(page)).toBeDisabled();
  await quantity.fill('1');
  await page.getByLabel('Observações do item 2', { exact: true }).fill('Bem passado');
  await review(page).click();
  await expect(summary(page)).toContainText('59,80');
  expect(state.quoted[0].items).toHaveLength(2);
  await page.getByLabel('Observações gerais', { exact: true }).fill('Nova observação');
  await expect(summary(page)).toHaveCount(0);
  await review(page).click();
  await expect(summary(page)).toBeVisible();
  await page.getByRole('button', { name: 'Remover item 1', exact: true }).click();
  await expect(summary(page)).toHaveCount(0);
  await expect(page.getByLabel('Observações do item 1', { exact: true })).toHaveValue(
    'Bem passado',
  );
  await page.getByRole('button', { name: 'Remover item 1', exact: true }).click();
  await expect(review(page)).toBeDisabled();
});

test('carrinho: falhas preservam itens e nova revisão atualiza preços', async ({ page }) => {
  const state = await setup(page);
  await openCart(page);
  await selectAndAdd(page);
  await review(page).click();
  await expect(summary(page)).toBeVisible();
  state.quoteStatus = 409;
  await review(page).click();
  await expect(page.getByRole('alert')).toContainText('indisponível');
  await expect(summary(page)).toHaveCount(0);
  await expect(page.getByLabel('Quantidade do item 1', { exact: true })).toHaveValue('1');
  state.quoteStatus = 503;
  await review(page).click();
  await expect(page.getByRole('alert')).toContainText('servidor não conseguiu');
  state.quoteStatus = 200;
  state.products[0].price = 40;
  await review(page).click();
  await expect(summary(page)).toContainText('40,00');
});

test('carrinho: buscas, paginação e seleção fora da primeira página', async ({ page }) => {
  const state = await setup(page);
  for (let index = 0; index < 20; index++) {
    state.products.push({ ...product, id: 'p-' + index, name: 'Lanche ' + index });
    state.customers.push({ ...customer, id: 'c-' + index, name: 'Cliente ' + index });
    state.addresses.push({ ...address, id: 'a-' + index, number: String(index + 20) });
  }
  await openCart(page);
  await page.getByRole('button', { name: 'Próximos clientes', exact: true }).click();
  await page.getByRole('button', { name: 'Selecionar cliente Cliente 19', exact: true }).click();
  await page.getByLabel('Buscar cliente por nome ou telefone').fill('Maria');
  await page.getByRole('button', { name: 'Buscar clientes', exact: true }).click();
  await page.getByRole('button', { name: 'Selecionar cliente Maria', exact: true }).click();
  await page.getByRole('combobox', { name: 'Recebimento', exact: true }).selectOption('Delivery');
  await page.getByRole('button', { name: 'Próximos endereços', exact: true }).click();
  await page.getByRole('button', { name: 'Selecionar endereço Rua A, 39', exact: true }).click();
  await page.getByLabel('Taxa de entrega (R$)', { exact: true }).fill('0');
  await page.getByRole('button', { name: 'Próximos produtos', exact: true }).click();
  await page.getByRole('button', { name: 'Adicionar Lanche 19', exact: true }).click();
  await page.getByLabel('Buscar produto', { exact: true }).fill('Uai');
  await page.getByRole('button', { name: 'Buscar produtos', exact: true }).click();
  await page.getByRole('button', { name: 'Adicionar Uai Sô', exact: true }).click();
  await review(page).click();
  await expect(summary(page)).toContainText('Rua A, 39');
  expect(state.quoted[0].items.map((item) => item.productId)).toEqual(['p-19', product.id]);
});

test('carrinho: falhas de carregamento permitem nova busca e endereço é obrigatório', async ({
  page,
}) => {
  const state = await setup(page);
  state.productStatus = state.customerStatus = state.addressStatus = 503;
  await openCart(page);
  await expect(page.getByRole('alert')).toHaveCount(2);
  state.productStatus = state.customerStatus = 200;
  await page.getByRole('button', { name: 'Buscar clientes', exact: true }).click();
  await page.getByRole('button', { name: 'Buscar produtos', exact: true }).click();
  await selectAndAdd(page);
  await page.getByRole('combobox', { name: 'Recebimento', exact: true }).selectOption('Delivery');
  await expect(page.getByRole('alert')).toHaveCount(1);
  await expect(review(page)).toBeDisabled();
  state.addressStatus = 200;
  await page.getByLabel('Taxa de entrega (R$)', { exact: true }).fill('0');
  await page.getByRole('button', { name: 'Atualizar endereços', exact: true }).click();
  await page.getByRole('button', { name: 'Selecionar endereço Rua A, 12', exact: true }).click();
  await expect(review(page)).toBeEnabled();
});

test('carrinho: revisão em andamento bloqueia edição e envio duplicado', async ({ page }) => {
  const state = await setup(page);
  let release = () => {};
  state.quoteGate = new Promise<void>((resolve) => {
    release = resolve;
  });
  await openCart(page);
  await selectAndAdd(page);
  await review(page).click();
  await expect(page.getByRole('button', { name: 'Revisando…', exact: true })).toBeDisabled();
  await expect(page.getByLabel('Quantidade do item 1', { exact: true })).toBeDisabled();
  await expect(page.getByRole('combobox', { name: 'Recebimento', exact: true })).toBeDisabled();
  release();
  await expect(summary(page)).toBeVisible();
  expect(state.quoted).toHaveLength(1);
});

test('carrinho: limpar exige confirmação e sair descarta os dados', async ({ page }) => {
  await setup(page);
  await openCart(page);
  await selectAndAdd(page);
  await page.getByRole('button', { name: 'Limpar carrinho', exact: true }).click();
  await page.getByRole('button', { name: 'Manter carrinho', exact: true }).click();
  await expect(page.getByLabel('Quantidade do item 1', { exact: true })).toHaveValue('1');
  await page.getByRole('button', { name: 'Limpar carrinho', exact: true }).click();
  await page.getByRole('button', { name: 'Confirmar limpeza', exact: true }).click();
  await expect(page.getByText('O carrinho está vazio.', { exact: false })).toBeVisible();
  await expect(review(page)).toBeDisabled();
  await selectAndAdd(page);
  await page.getByRole('link', { name: 'Minha conta', exact: true }).click();
  await page.getByRole('link', { name: 'Carrinho', exact: true }).click();
  await expect(review(page)).toBeDisabled();
  await expect(page.getByText('O carrinho está vazio.', { exact: false })).toBeVisible();
});

for (const role of ['Kitchen', 'Dispatch']) {
  test('carrinho: ' + role + ' não acessa a rota', async ({ page }) => {
    const state = await setup(page, role);
    await login(page);
    await expect(page.getByRole('link', { name: 'Carrinho', exact: true })).toHaveCount(0);
    await page.evaluate(() => {
      history.pushState(null, '', '/equipe/carrinho');
      dispatchEvent(new PopStateEvent('popstate'));
    });
    await expect(page).toHaveURL(/\/equipe$/);
    expect(state.cartRequests).toBe(0);
  });
}

test('carrinho: acesso anônimo e sessão revogada exigem login sem preservar dados', async ({
  page,
}) => {
  const state = await setup(page);
  await page.goto('/equipe/carrinho');
  await expect(page).toHaveURL(/\/entrar$/);
  await openCart(page);
  await selectAndAdd(page);
  state.quoteStatus = 401;
  state.quoteCode = 'InvalidSession';
  await review(page).click();
  await expect(page).toHaveURL(/\/entrar$/);
  await openCart(page);
  await expect(review(page)).toBeDisabled();
  await expect(page.getByText('O carrinho está vazio.', { exact: false })).toBeVisible();
});
