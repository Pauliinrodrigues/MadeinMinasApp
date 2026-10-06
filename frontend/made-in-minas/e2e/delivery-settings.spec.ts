import { expect, Page, test } from '@playwright/test';
import {
  DeliveryArea,
  SaveDeliverySettings,
} from '../src/app/core/services/delivery-settings-api.service';

const area: DeliveryArea = {
  id: 'center',
  neighborhood: 'Centro',
  city: 'Cidade de teste',
  state: 'MG',
  fee: 5.5,
  isActive: true,
};

async function setup(page: Page, role = 'Administrator') {
  const profile = {
    id: 'staff',
    name: 'Equipe Teste',
    username: 'equipe.teste',
    role,
    permissions: role === 'Administrator' ? ['delivery.manage'] : [],
  };
  const state = {
    areas: [] as DeliveryArea[],
    revision: 'A'.repeat(64),
    updatedAt: null as string | null,
    listStatus: 200,
    saveStatus: 200,
    writes: [] as SaveDeliverySettings[],
    reads: 0,
    release: null as Promise<void> | null,
  };
  await page.route('**/api/**', async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    const json = (body: unknown, status = 200) => route.fulfill({ status, json: body });
    const result = () => ({
      areas: state.areas,
      revision: state.revision,
      updatedAt: state.updatedAt,
      updatedBy: state.updatedAt ? profile.name : null,
    });
    if (path === '/api/auth/login') {
      return json({
        accessToken: 'delivery-test-token',
        tokenType: 'Bearer',
        expiresAt: new Date(Date.now() + 900000).toISOString(),
        user: profile,
      });
    }
    expect(request.headers()['authorization']).toBe('Bearer delivery-test-token');
    if (path === '/api/auth/me') {
      return json(profile);
    }
    if (path === '/api/delivery-settings' && request.method() === 'GET') {
      state.reads++;
      return state.listStatus === 200 ? json(result()) : json({}, state.listStatus);
    }
    if (path === '/api/delivery-settings' && request.method() === 'PUT') {
      const input = request.postDataJSON() as SaveDeliverySettings;
      state.writes.push(input);
      if (state.release) {
        await state.release;
      }
      if (state.saveStatus !== 200) {
        return json(
          { code: state.saveStatus === 409 ? 'DeliverySettingsChanged' : undefined },
          state.saveStatus,
        );
      }
      state.areas = structuredClone(input.areas);
      state.revision = 'B'.repeat(64);
      state.updatedAt = '2026-10-06T12:00:00Z';
      return json(result());
    }
    return json({}, 404);
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
async function open(page: Page) {
  await login(page);
  await page.getByRole('link', { name: 'Regiões de entrega', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Nova região' })).toBeEnabled();
}
async function fill(page: Page, fee = '7.5') {
  await page.getByLabel('Bairro', { exact: true }).fill('Centro');
  await page.getByLabel('Cidade', { exact: true }).fill('Cidade de teste');
  await page.getByRole('combobox', { name: 'UF', exact: true }).selectOption('MG');
  await page.getByLabel('Taxa de entrega (R$)', { exact: true }).fill(fee);
}

test('entrega: cadastro revisado, edição, pausa e filtros preservam as regiões', async ({
  page,
}) => {
  const errors: string[] = [];
  page.on('pageerror', (error) => errors.push(error.message));
  const state = await setup(page);
  await open(page);
  await expect(page.getByText('O site aceita somente retirada', { exact: false })).toBeVisible();
  await page.getByRole('button', { name: 'Nova região' }).click();
  await expect(page.getByLabel('Bairro', { exact: true })).toBeFocused();
  await expect(page.getByLabel('Taxa de entrega (R$)')).toHaveValue('');
  await fill(page);
  await page.getByRole('button', { name: 'Revisar alteração' }).click();
  const review = page.getByRole('region', { name: 'Conferir alteração de entrega' });
  await expect(review).toContainText('7,50');
  expect(state.writes).toHaveLength(0);
  await expect(page.getByRole('button', { name: 'Confirmar salvamento' })).toBeFocused();
  await page.getByRole('button', { name: 'Confirmar salvamento' }).click();
  await expect(page.getByRole('status')).toContainText('Regiões de entrega atualizadas');
  expect(state.writes).toHaveLength(1);
  expect(state.areas[0].fee).toBe(7.5);
  const id = state.areas[0].id;
  await page.getByRole('button', { name: 'Editar Centro em Cidade de teste' }).click();
  await page.getByLabel('Taxa de entrega (R$)').fill('0');
  await page.getByRole('button', { name: 'Revisar alteração' }).click();
  await page.getByRole('button', { name: 'Voltar à edição' }).click();
  expect(state.writes).toHaveLength(1);
  await page.getByRole('button', { name: 'Revisar alteração' }).click();
  await page.getByRole('button', { name: 'Confirmar salvamento' }).click();
  await expect(
    page.getByRole('button', { name: 'Pausar Centro em Cidade de teste' }),
  ).toBeEnabled();
  expect(state.areas[0]).toMatchObject({ id, fee: 0 });
  await page.getByRole('button', { name: 'Pausar Centro em Cidade de teste' }).click();
  await expect(review).toContainText('Pausada');
  await page.getByRole('button', { name: 'Confirmar salvamento' }).click();
  await expect(
    page.getByRole('button', { name: 'Ativar Centro em Cidade de teste' }),
  ).toBeEnabled();
  expect(state.areas[0].isActive).toBe(false);
  await page.getByRole('combobox', { name: 'Situação', exact: true }).selectOption('true');
  await expect(page.getByText('Nenhuma região nesta busca.', { exact: false })).toBeVisible();
  await page.getByRole('combobox', { name: 'Situação', exact: true }).selectOption('false');
  await page.getByLabel('Buscar bairro ou cidade').fill('não existe');
  await expect(page.getByText('Nenhuma região nesta busca.', { exact: false })).toBeVisible();
  await page.getByLabel('Buscar bairro ou cidade').fill('CENTRO');
  await page.getByRole('button', { name: 'Ativar Centro em Cidade de teste' }).click();
  await page.getByRole('button', { name: 'Confirmar salvamento' }).click();
  await expect(page.getByText('Nenhuma região nesta busca.', { exact: false })).toBeVisible();
  await page.getByRole('combobox', { name: 'Situação', exact: true }).selectOption('');
  await page.getByRole('button', { name: 'Atualizar regiões' }).click();
  await expect(
    page.getByRole('button', { name: 'Editar Centro em Cidade de teste' }),
  ).toBeEnabled();
  expect(state.writes).toHaveLength(4);
  expect(state.areas[0]).toMatchObject({ id, fee: 0, isActive: true });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  expect(errors).toEqual([]);
});

test('entrega: taxa explícita, precisão e bairro duplicado são conferidos antes de enviar', async ({
  page,
}) => {
  const state = await setup(page);
  state.areas = [{ ...area, isActive: false }];
  await open(page);
  await page.getByRole('button', { name: 'Nova região' }).click();
  await page.getByRole('button', { name: 'Revisar alteração' }).click();
  await expect(page.getByRole('alert')).toContainText('Informe o bairro e a cidade');
  await fill(page, '');
  await page.getByRole('button', { name: 'Revisar alteração' }).click();
  await expect(page.getByRole('alert')).toContainText('Informe uma taxa');
  await page.getByLabel('Taxa de entrega (R$)').fill('1.001');
  await page.getByRole('button', { name: 'Revisar alteração' }).click();
  await expect(page.getByRole('alert')).toContainText('duas casas decimais');
  await page.getByLabel('Taxa de entrega (R$)').fill('5');
  await page.getByRole('button', { name: 'Revisar alteração' }).click();
  await expect(page.getByRole('alert')).toContainText('Confira também as pausadas');
  expect(state.writes).toHaveLength(0);
});

test('entrega: edição concorrente exige nova consulta e preserva os campos até recarregar', async ({
  page,
}) => {
  const state = await setup(page);
  state.areas = [{ ...area }];
  await open(page);
  await page.getByRole('button', { name: 'Editar Centro em Cidade de teste' }).click();
  await page.getByLabel('Taxa de entrega (R$)').fill('8');
  await page.getByRole('button', { name: 'Revisar alteração' }).click();
  state.saveStatus = 409;
  await page.getByRole('button', { name: 'Confirmar salvamento' }).click();
  await expect(page.getByRole('alert')).toContainText('Recarregue a lista');
  await expect(page.getByLabel('Taxa de entrega (R$)')).toHaveValue('8');
  await expect(page.getByRole('button', { name: 'Revisar alteração' })).toBeDisabled();
  state.areas[0].fee = 12;
  state.revision = 'C'.repeat(64);
  state.saveStatus = 200;
  await page.getByRole('button', { name: 'Recarregar lista e descartar edição' }).click();
  await page.getByRole('button', { name: 'Editar Centro em Cidade de teste' }).click();
  await expect(page.getByLabel('Taxa de entrega (R$)')).toHaveValue('12');
  await page.getByLabel('Taxa de entrega (R$)').fill('9');
  await page.getByRole('button', { name: 'Revisar alteração' }).click();
  await page.getByRole('button', { name: 'Confirmar salvamento' }).click();
  await expect(page.getByRole('status')).toContainText('atualizadas');
  expect(state.writes[1].expectedRevision).toBe('C'.repeat(64));
});

test('entrega: envio pendente bloqueia duplicidade e resposta incerta repete o mesmo corpo', async ({
  page,
}) => {
  const state = await setup(page);
  await open(page);
  await page.getByRole('button', { name: 'Nova região' }).click();
  await fill(page);
  await page.getByRole('button', { name: 'Revisar alteração' }).click();
  let release!: () => void;
  state.release = new Promise<void>((resolve) => {
    release = resolve;
  });
  state.saveStatus = 503;
  await page.getByRole('button', { name: 'Confirmar salvamento' }).click();
  await expect.poll(() => state.writes.length).toBe(1);
  await expect(page.getByRole('button', { name: 'Salvando…' })).toBeDisabled();
  await expect(
    page.getByRole('button', { name: 'Recarregar lista e descartar edição' }),
  ).toBeDisabled();
  release();
  await expect(page.getByRole('alert')).toContainText('Não foi possível confirmar');
  await expect(page.getByRole('button', { name: 'Voltar à edição' })).toBeDisabled();
  await expect(page.getByLabel('Taxa de entrega (R$)')).toBeDisabled();
  state.release = null;
  state.saveStatus = 200;
  await page.getByRole('button', { name: 'Tentar salvamento novamente' }).click();
  await expect(page.getByRole('status')).toContainText('atualizadas');
  expect(state.writes).toHaveLength(2);
  expect(state.writes[1]).toEqual(state.writes[0]);
});

test('entrega: falha na consulta permite recuperação sem supor lista vazia', async ({ page }) => {
  const state = await setup(page);
  state.listStatus = 503;
  await login(page);
  await page.getByRole('link', { name: 'Regiões de entrega', exact: true }).click();
  await expect(page.getByRole('alert')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Nova região' })).toBeDisabled();
  state.listStatus = 200;
  state.areas = [{ ...area }];
  await page.getByRole('button', { name: 'Atualizar regiões' }).click();
  await expect(
    page.getByRole('button', { name: 'Editar Centro em Cidade de teste' }),
  ).toBeEnabled();
  expect(state.writes).toHaveLength(0);
});

for (const role of ['Attendant', 'Kitchen', 'Dispatch']) {
  test('entrega: perfil ' + role + ' não acessa configurações', async ({ page }) => {
    const state = await setup(page, role);
    await login(page);
    await expect(page.getByRole('link', { name: 'Regiões de entrega', exact: true })).toHaveCount(
      0,
    );
    await page.evaluate(() => {
      history.pushState(null, '', '/equipe/regioes-entrega');
      dispatchEvent(new PopStateEvent('popstate'));
    });
    await expect(page).toHaveURL(/\/equipe$/);
    expect(state.reads).toBe(0);
  });
}

test('entrega: permissão recusada informa a rejeição sem encerrar a sessão', async ({ page }) => {
  const state = await setup(page);
  state.areas = [{ ...area }];
  await open(page);
  await page.getByRole('button', { name: 'Pausar Centro em Cidade de teste' }).click();
  state.saveStatus = 403;
  await page.getByRole('button', { name: 'Confirmar salvamento' }).click();
  await expect(page.getByRole('alert')).toContainText('permissão');
  await expect(page).toHaveURL(/\/equipe\/regioes-entrega$/);
  await expect(page.getByRole('button', { name: 'Nova região' })).toBeDisabled();
  expect(state.areas[0].isActive).toBe(true);
});

test('entrega: sessão revogada exige novo login e mantém o destino', async ({ page }) => {
  const state = await setup(page);
  await open(page);
  state.listStatus = 401;
  await page.getByRole('button', { name: 'Atualizar regiões' }).click();
  await expect(page).toHaveURL(/\/entrar\?returnUrl=%2Fequipe%2Fregioes-entrega/);
  await expect(page.getByLabel('Senha', { exact: true })).toBeVisible();
});
