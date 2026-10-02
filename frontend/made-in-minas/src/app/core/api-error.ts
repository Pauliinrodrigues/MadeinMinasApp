import { HttpErrorResponse } from '@angular/common/http';

export function apiError(error: unknown, login = false): string {
  if (!(error instanceof HttpErrorResponse)) {
    return 'Não foi possível concluir. Verifique a conexão e tente novamente.';
  }
  const codes: Record<string, string> = {
    OrderRecipeRequired:
      'Cadastre a ficha técnica de todos os produtos antes de confirmar o pedido.',
    OrderIngredientInactive: 'A ficha contém ingrediente inativo. Solicite a revisão do cadastro.',
    OrderInsufficientStock:
      'Saldo insuficiente para os ingredientes deste pedido. Solicite a conferência do estoque.',
    OrderStockQuantityExceeded:
      'O consumo calculado ultrapassa o limite. Revise as quantidades e as fichas técnicas.',
    OrderStockReturnOverflow:
      'A devolução ultrapassa o limite do estoque. Solicite uma conferência antes de cancelar.',
    InactiveIngredient: 'Reative o ingrediente antes de movimentar o estoque.',
    StockVersionConflict: 'O estoque mudou. Atualize o saldo e revise o lançamento.',
    StockRequestConflict:
      'Este identificador já foi utilizado. Confira o histórico antes de continuar.',
    InsufficientStock: 'A saída ultrapassa o saldo disponível.',
    StockLimitExceeded: 'O saldo máximo é 999999,999 na unidade-base.',
    StockUnchanged: 'A contagem é igual ao saldo atual. Nenhum ajuste é necessário.',
    PaymentNotFound: 'Pagamento não encontrado para este pedido.',
    PaymentOrderNotFound: 'Pedido não encontrado.',
    PaymentOrderCancelled: 'Pedido cancelado não pode receber novos pagamentos.',
    PaymentOrderFinalized: 'Pedido finalizado não pode receber novos pagamentos.',
    OrderPaymentRequired:
      'Solicite ao atendimento o registro do recebimento integral antes de finalizar.',
    PaymentOrderChanged: 'O pedido mudou. Atualize os dados antes de definir o pagamento.',
    PaymentAlreadyActive:
      'O pedido já possui pagamento pendente ou recebido. Confira o pagamento atual.',
    PaymentRequestConflict:
      'Esta tentativa já foi registrada com outro conteúdo. Confira o histórico antes de continuar.',
    PaymentVersionConflict: 'O pagamento foi alterado por outro atendimento. Atualize o histórico.',
    PaymentTransitionDenied: 'Esta alteração não é permitida para o pagamento atual.',
    PaymentInvalidCash:
      'Em dinheiro, informe o valor entregue, igual ou maior que o pedido. Nas demais formas, não informe dinheiro entregue.',
    OrderPaymentUnresolved:
      'Cancele o pagamento pendente ou registre a devolução do valor recebido antes de cancelar o pedido.',
    OrderNotFound: 'Pedido não encontrado.',
    OrderReviewChanged: 'Os dados da compra mudaram. Revise os valores e o endereço novamente.',
    OrderRequestConflict:
      'Esta tentativa já foi usada com outro conteúdo. Consulte os pedidos antes de tentar novamente.',
    OrderVersionConflict:
      'O pedido foi alterado por outro atendimento. Atualize os dados antes de continuar.',
    OrderTransitionDenied: 'Esta alteração de status não é permitida. Atualize o pedido.',
    OrderCancellationDenied: 'Somente o administrador pode cancelar um pedido confirmado.',
    CartCustomerUnavailable: 'O cliente não está disponível. Busque e selecione um cliente ativo.',
    CartAddressUnavailable:
      'O endereço não está disponível para este cliente. Atualize e selecione um endereço ativo.',
    CartProductUnavailable:
      'Um produto do carrinho está indisponível. Atualize os produtos e remova os itens indisponíveis.',
    CustomerNotFound: 'Cliente não encontrado.',
    AddressNotFound: 'Endereço não encontrado para este cliente.',
    DuplicateCustomerPhone:
      'Já existe um cliente com esse telefone. Verifique também os clientes inativos.',
    InvalidCustomerPhone: 'Informe um telefone brasileiro completo com DDD.',
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
