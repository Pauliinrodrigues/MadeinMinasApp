import { HttpErrorResponse } from '@angular/common/http';

export function apiError(error: unknown, login = false): string {
  if (!(error instanceof HttpErrorResponse)) {
    return 'Não foi possível concluir. Verifique a conexão e tente novamente.';
  }
  const codes: Record<string, string> = {
    RecipeNotFound: 'Este produto ainda não possui ficha técnica.',
    InvalidRecipeIngredient:
      'Um ingrediente não existe mais. Reabra a ficha para atualizar as opções.',
    InactiveRecipeIngredient:
      'Um ingrediente foi inativado. Novos vínculos exigem ingredientes ativos; seus campos foram preservados.',
    IngredientNotFound: 'Ingrediente não encontrado.',
    DuplicateIngredientName:
      'Já existe um ingrediente com esse nome. Verifique também os ingredientes inativos.',
    IngredientUnitImmutable:
      'A unidade não pode ser alterada após o cadastro. Reabra o ingrediente para conferir os dados.',
    ProductNotFound: 'Produto não encontrado.',
    DuplicateProductName:
      'Já existe um produto com esse nome nesta categoria. Verifique também os inativos.',
    InvalidProductCategory: 'A categoria não existe mais. Reabra o cadastro e selecione outra.',
    InactiveProductCategory: 'Selecione uma categoria ativa para cadastrar ou mover o produto.',
    DuplicateCategoryName:
      'Já existe uma categoria com esse nome. Verifique também as categorias inativas.',
    CategoryNotFound: 'Categoria não encontrada.',
    DuplicateUsername: 'Este login já está em uso. Escolha outro.',
    LastAdministrator: 'É necessário manter pelo menos um administrador ativo.',
    InvalidRole: 'O perfil selecionado não está disponível.',
    InvalidPassword: 'Senha atual incorreta ou conta temporariamente bloqueada.',
    UseOwnPasswordEndpoint: 'Use Minha senha para alterar sua própria senha.',
    UserNotFound: 'Funcionário não encontrado.',
    InvalidSession: 'Sua sessão foi encerrada. Entre novamente.',
    PermissionDenied: 'Você não tem permissão para esta operação.',
  };
  const code: unknown = error.error?.code;
  if (typeof code === 'string' && codes[code]) {
    return codes[code];
  }
  switch (error.status) {
    case 0:
      return 'Não foi possível conectar ao servidor. Verifique a conexão e tente novamente.';
    case 400:
      return 'Confira os campos informados e tente novamente.';
    case 401:
      return login
        ? 'Login ou senha inválidos, ou conta indisponível. Confira os dados e tente novamente.'
        : 'Sua sessão foi encerrada. Entre novamente.';
    case 403:
      return 'Você não tem permissão para esta operação.';
    case 404:
      return 'Registro não encontrado.';
    case 409:
      return 'Os dados entraram em conflito. Atualize a página e confira o cadastro.';
    case 429:
      return 'Muitas tentativas. Aguarde um minuto antes de tentar novamente.';
    default:
      return 'O servidor não conseguiu concluir a operação. Tente novamente em instantes.';
  }
}
