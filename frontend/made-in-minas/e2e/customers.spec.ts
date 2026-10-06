import { test, expect, Page } from '@playwright/test';
import type {
  Address,
  AddressInput,
  Customer,
  CustomerInput,
} from '../src/app/core/services/customer-api.service';

const customer: Customer = {
  id: 'customer-1',
  name: 'Maria',
  phone: '+5531999991234',
  isActive: true,
  createdAt: '2026-09-30T12:00:00Z',
  updatedAt: '2026-09-30T12:00:00Z',
};
const address: Address = {
  id: 'address-1',
  customerId: customer.id,
  street: 'Rua das Flores',
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

async function setup(page: Page, role = 'Administrator') {
  const profile = {
    id: 'staff',
    name: 'Equipe Teste',
    username: 'equipe.teste',
    role,
    permissions: ['Administrator', 'Attendant'].includes(role) ? ['customers.manage'] : [],
  };
  const state = {
    customers: [{ ...customer }],
    addresses: [{ ...address }],
    listStatus: 200,
    saveStatus: 200,
    addressListStatus: 200,
    addressSaveStatus: 200,
    writes: 0,
  };
  await page.route('**/api/**', async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    const path = url.pathname;
    const json = (body: unknown, status = 200) => route.fulfill({ status, json: body });
    if (path === '/api/auth/login') {
      return json({
        accessToken: 'customer-test-token',
        tokenType: 'Bearer',
        expiresAt: new Date(Date.now() + 900000).toISOString(),
        user: profile,
      });
    }
    expect(request.headers()['authorization']).toBe('Bearer customer-test-token');
    if (path === '/api/auth/me') {
      return json(profile);
    }
    if (path === '/api/customers') {
      if (request.method() === 'GET') {
        if (state.listStatus !== 200) {
          return json({}, state.listStatus);
        }
        const search = (url.searchParams.get('search') ?? '').toLowerCase();
        const active = url.searchParams.get('isActive');
        const digits = search.replace(/[^0-9]/g, '');
        const items = state.customers
          .filter(
            (item) =>
              (item.name.toLowerCase().includes(search) ||
                (!!digits && item.phone.includes(digits))) &&
              (!active || String(item.isActive) === active),
          )
          .sort((a, b) => a.name.localeCompare(b.name));
        const current = Number(url.searchParams.get('page') ?? '1');
        return json({
          items: items.slice((current - 1) * 20, current * 20),
          page: current,
          pageSize: 20,
          totalCount: items.length,
        });
      }
      state.writes++;
      if (state.saveStatus !== 200) {
        return json({ code: 'DuplicateCustomerPhone' }, state.saveStatus);
      }
      const input = request.postDataJSON() as CustomerInput;
      let phone = input.phone.replace(/[^0-9]/g, '');
      phone = phone.length > 11 ? '+' + phone : '+55' + phone;
      const created = { ...customer, ...input, phone, id: 'new-customer' };
      state.customers.push(created);
      return json(created, 201);
    }
    const match = path.match(
      /^\/api\/customers\/([^/]+)(?:\/(status|addresses)(?:\/([^/]+)(\/status)?)?)?$/,
    );
    if (!match) {
      return json({}, 404);
    }
    const owner = state.customers.find((item) => item.id === match[1]);
    if (!owner) {
      return json({ code: 'CustomerNotFound' }, 404);
    }
    if (match[2] !== 'addresses') {
      if (request.method() === 'GET') {
        return json(owner);
      }
      state.writes++;
      if (state.saveStatus !== 200) {
        return json({ code: 'DuplicateCustomerPhone' }, state.saveStatus);
      }
      Object.assign(owner, request.postDataJSON());
      return json(owner);
    }
    if (!match[3] && request.method() === 'GET') {
      if (state.addressListStatus !== 200) {
        return json({}, state.addressListStatus);
      }
      const active = url.searchParams.get('isActive');
      const current = Number(url.searchParams.get('page') ?? '1');
      const items = state.addresses.filter(
        (item) => item.customerId === owner.id && (!active || String(item.isActive) === active),
      );
      return json({
        items: items.slice((current - 1) * 20, current * 20),
        page: current,
        pageSize: 20,
        totalCount: items.length,
      });
    }
    state.writes++;
    if (state.addressSaveStatus !== 200) {
      return json({}, state.addressSaveStatus);
    }
    if (!match[3]) {
      const input = request.postDataJSON() as AddressInput;
      const created = {
        ...address,
        ...input,
        postalCode: input.postalCode?.replace('-', '') ?? null,
        id: 'new-address',
        customerId: owner.id,
      };
      state.addresses.push(created);
      return json(created, 201);
    }
    const item = state.addresses.find(
      (entry) => entry.id === match[3] && entry.customerId === owner.id,
    );
    if (!item) {
      return json({ code: 'AddressNotFound' }, 404);
    }
    Object.assign(item, request.postDataJSON());
    return json(item);
  });
  return state;
}

async function login(page: Page) {
  await page.goto('/entrar?returnUrl=%2Fequipe');
  await page.getByLabel('Login', { exact: true }).fill('equipe.teste');
  await page.getByLabel('Senha', { exact: true }).fill('Senha apenas para os testes');
  await page.getByRole('button', { name: 'Entrar', exact: true }).click();
  await expect(page).toHaveURL(/\/equipe$/);
}
async function navigate(page: Page, path: string) {
  await page.evaluate((path) => {
    history.pushState(null, '', path);
    dispatchEvent(new PopStateEvent('popstate'));
  }, path);
}

for (const role of ['Administrator', 'Attendant']) {
  test('clientes: ' + role + ' cadastra, edita e reabre cliente', async ({ page }) => {
    const errors: string[] = [];
    page.on('pageerror', (error) => errors.push(error.message));
    const state = await setup(page, role);
    await login(page);
    await page.getByRole('link', { name: 'Clientes', exact: true }).click();
    await page.getByRole('link', { name: 'Novo cliente', exact: true }).click();
    const save = page.getByRole('button', { name: 'Salvar cliente' });
    await expect(save).toBeDisabled();
    await page.getByLabel('Nome', { exact: true }).fill('João');
    await page.getByLabel('Telefone com DDD').fill('(31) 98888-1234');
    await save.click();
    await expect(page.getByRole('status').filter({ hasText: 'Cliente criado' })).toBeVisible();
    await expect(page.getByLabel('Telefone com DDD')).toHaveValue('+5531988881234');
    await page.getByLabel('Nome', { exact: true }).fill('João Silva');
    await save.click();
    await expect(page.getByRole('status')).toContainText('Cliente atualizado');
    await page.getByRole('link', { name: 'Clientes', exact: true }).click();
    await page.getByRole('link', { name: 'Editar João Silva', exact: true }).click();
    await expect(page.getByLabel('Nome', { exact: true })).toHaveValue('João Silva');
    await expect(page.getByRole('link', { name: 'Gerenciar endereços' })).toBeVisible();
    expect(state.writes).toBe(2);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(
      true,
    );
    expect(errors).toEqual([]);
  });
}

test('clientes: telefone inválido bloqueia envio e duplicado mantém campos', async ({ page }) => {
  const state = await setup(page);
  await login(page);
  await navigate(page, '/equipe/clientes/novo');
  await page.getByLabel('Nome', { exact: true }).fill('Maria');
  const phone = page.getByLabel('Telefone com DDD');
  const save = page.getByRole('button', { name: 'Salvar cliente' });
  for (const invalid of ['999991234', '3199991234', '+131999991234', '31 89999-1234', 'telefone']) {
    await phone.fill(invalid);
    await expect(save).toBeDisabled();
  }
  expect(state.writes).toBe(0);
  await phone.fill('+55 (31) 99999-1234');
  state.saveStatus = 409;
  await save.click();
  await expect(page.getByRole('alert')).toContainText('clientes inativos');
  await expect(phone).toHaveValue('+55 (31) 99999-1234');
  await expect(page.getByLabel('Nome', { exact: true })).toHaveValue('Maria');
  state.saveStatus = 200;
  await phone.fill('(31) 3333-1234');
  await save.click();
  await expect(page.getByRole('status').filter({ hasText: 'Cliente criado' })).toBeVisible();
});

test('clientes: busca por telefone, paginação e inativação com confirmação', async ({ page }) => {
  const state = await setup(page);
  for (let index = 0; index < 21; index++) {
    state.customers.push({
      ...customer,
      id: 'extra-' + index,
      name: 'Outro ' + index,
      phone: '+5531988880000',
    });
  }
  await login(page);
  await page.getByRole('link', { name: 'Clientes', exact: true }).click();
  await expect(page.getByRole('status')).toContainText('22 cliente(s)');
  await page.getByRole('button', { name: 'Próxima' }).click();
  await expect(page.getByText('Página 2', { exact: true })).toBeVisible();
  await page.getByLabel('Buscar por nome ou telefone').fill('(31) 99999-1234');
  await page.getByRole('button', { name: 'Buscar', exact: true }).click();
  await expect(page.getByRole('status')).toContainText('1 cliente(s)');
  await page.getByRole('button', { name: 'Inativar Maria', exact: true }).click();
  await page.getByRole('button', { name: 'Cancelar', exact: true }).click();
  expect(state.writes).toBe(0);
  await page.getByRole('button', { name: 'Inativar Maria', exact: true }).click();
  await page.getByRole('button', { name: 'Confirmar', exact: true }).click();
  await expect(page.getByText('Cliente inativado.', { exact: true })).toBeVisible();
  await page.getByRole('combobox', { name: 'Status', exact: true }).selectOption('false');
  await page.getByRole('button', { name: 'Buscar', exact: true }).click();
  await page.getByRole('button', { name: 'Ativar Maria', exact: true }).click();
  await page.getByRole('button', { name: 'Confirmar', exact: true }).click();
  await expect(page.getByText('Cliente ativado.', { exact: true })).toBeVisible();
  await expect(page.getByText('Nenhum cliente nesta página.', { exact: false })).toBeVisible();
  expect(state.addresses[0].isActive).toBe(true);
});

test('endereços: atendente cadastra, reabre, edita e inativa endereço', async ({ page }) => {
  const state = await setup(page, 'Attendant');
  await login(page);
  await navigate(page, '/equipe/clientes/customer-1/enderecos');
  await page.getByRole('button', { name: 'Novo endereço', exact: true }).click();
  const save = page.getByRole('button', { name: 'Salvar endereço' });
  await expect(save).toBeDisabled();
  await page.getByLabel('Rua ou avenida').fill('Rua Nova');
  await page.getByLabel('Número', { exact: true }).fill('S/N');
  await page.getByLabel('Bairro', { exact: true }).fill('Centro');
  await page.getByLabel('Cidade', { exact: true }).fill('Belo Horizonte');
  await page.getByRole('combobox', { name: 'UF', exact: true }).selectOption('MG');
  await page.getByLabel('CEP (opcional)').fill('123');
  await expect(save).toBeDisabled();
  await page.getByLabel('CEP (opcional)').fill('30110-000');
  await page.getByLabel('Referência (opcional)').fill('Portão azul');
  await save.click();
  await expect(page.getByText('Endereço criado.', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Editar endereço Rua Nova, S/N', exact: true }).click();
  await expect(page.getByLabel('CEP (opcional)')).toHaveValue('30110000');
  await expect(page.getByLabel('Referência (opcional)')).toHaveValue('Portão azul');
  await page.getByLabel('Número', { exact: true }).fill('17B');
  await page.getByLabel('CEP (opcional)').fill('');
  await save.click();
  await expect(page.getByText('Endereço atualizado.', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Inativar endereço Rua Nova, 17B', exact: true }).click();
  await page.getByRole('button', { name: 'Confirmar', exact: true }).click();
  await expect(page.getByText('Endereço inativado.', { exact: true })).toBeVisible();
  await page.getByLabel('Status dos endereços').selectOption('false');
  await page.getByRole('button', { name: 'Filtrar', exact: true }).click();
  await expect(page.getByRole('status').filter({ hasText: 'endereço(s)' })).toContainText(
    '1 endereço(s)',
  );
  await page.getByRole('button', { name: 'Ativar endereço Rua Nova, 17B', exact: true }).click();
  await page.getByRole('button', { name: 'Confirmar', exact: true }).click();
  await expect(page.getByText('Endereço ativado.', { exact: true })).toBeVisible();
  expect(state.addresses.find((item) => item.id === 'new-address')).toMatchObject({
    customerId: customer.id,
    postalCode: null,
    number: '17B',
    isActive: true,
  });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});

test('endereços: falha ao salvar preserva formulário e cancelar não grava', async ({ page }) => {
  const state = await setup(page);
  await login(page);
  await navigate(page, '/equipe/clientes/customer-1/enderecos');
  await page
    .getByRole('button', { name: 'Editar endereço Rua das Flores, 12', exact: true })
    .click();
  await page.getByLabel('Número', { exact: true }).fill('99');
  state.addressSaveStatus = 503;
  await page.getByRole('button', { name: 'Salvar endereço' }).click();
  await expect(page.getByRole('alert')).toContainText('servidor não conseguiu');
  await expect(page.getByLabel('Número', { exact: true })).toHaveValue('99');
  state.addressSaveStatus = 200;
  await page.getByRole('button', { name: 'Salvar endereço' }).click();
  await expect(page.getByText('Endereço atualizado.', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Novo endereço', exact: true }).click();
  await page.getByLabel('Rua ou avenida').fill('Não salvar');
  await page.getByRole('button', { name: 'Cancelar edição', exact: true }).click();
  expect(state.writes).toBe(2);
  expect(state.addresses).toHaveLength(1);
});

test('endereços: paginação, cliente inativo e falha de carregamento', async ({ page }) => {
  const state = await setup(page);
  state.customers[0].isActive = false;
  for (let index = 0; index < 20; index++) {
    state.addresses.push({ ...address, id: 'extra-' + index, number: String(index + 20) });
  }
  state.addressListStatus = 503;
  await login(page);
  await navigate(page, '/equipe/clientes/customer-1/enderecos');
  await expect(page.getByRole('alert')).toContainText('servidor não conseguiu');
  await expect(page.getByRole('button', { name: 'Novo endereço', exact: true })).toHaveCount(0);
  state.addressListStatus = 200;
  await page.getByRole('button', { name: 'Tentar novamente' }).click();
  await expect(page.getByText('Cliente inativo.', { exact: false })).toBeVisible();
  await expect(page.getByRole('status')).toContainText('21 endereço(s)');
  await page.getByRole('button', { name: 'Próxima', exact: true }).click();
  await expect(page.getByText('Página 2', { exact: true })).toBeVisible();
  await expect(
    page.getByRole('button', { name: 'Editar endereço Rua das Flores, 39', exact: true }),
  ).toBeVisible();
});

test('clientes: falha na busca, nova tentativa e cliente inexistente', async ({ page }) => {
  const state = await setup(page);
  state.listStatus = 503;
  await login(page);
  await navigate(page, '/equipe/clientes');
  await expect(page.getByRole('alert')).toContainText('servidor não conseguiu');
  state.listStatus = 200;
  await page.getByRole('button', { name: 'Tentar novamente' }).click();
  await expect(page.getByRole('link', { name: 'Editar Maria', exact: true })).toBeVisible();
  await navigate(page, '/equipe/clientes/inexistente');
  await expect(page.getByRole('alert')).toContainText('Cliente não encontrado');
  await expect(page.getByRole('button', { name: 'Salvar cliente' })).toHaveCount(0);
  await navigate(page, '/equipe/clientes/inexistente/enderecos');
  await expect(page.getByRole('alert')).toContainText('Cliente não encontrado');
  await expect(page.getByRole('button', { name: 'Novo endereço', exact: true })).toHaveCount(0);
});

for (const role of ['Kitchen', 'Dispatch']) {
  test('clientes: perfil ' + role + ' não acessa cadastros', async ({ page }) => {
    await setup(page, role);
    await login(page);
    await expect(page.getByRole('link', { name: 'Clientes', exact: true })).toHaveCount(0);
    for (const path of [
      '/equipe/clientes',
      '/equipe/clientes/novo',
      '/equipe/clientes/customer-1',
      '/equipe/clientes/customer-1/enderecos',
    ]) {
      await navigate(page, path);
      await expect(page).toHaveURL(/\/equipe$/);
    }
  });
}

test('clientes: acesso sem sessão e sessão encerrada exigem login', async ({ page }) => {
  const state = await setup(page);
  await page.goto('/equipe/clientes/customer-1/enderecos');
  await expect(page).toHaveURL(/\/entrar(?:\?.*)?$/);
  await login(page);
  state.listStatus = 401;
  await navigate(page, '/equipe/clientes');
  await expect(page).toHaveURL(/\/entrar(?:\?.*)?$/);
  expect(state.writes).toBe(0);
});
