import { Routes } from '@angular/router';
import {
  administratorGuard,
  deliveryGuard,
  dashboardGuard,
  reportsGuard,
  catalogGuard,
  customersGuard,
  ordersGuard,
  paymentsGuard,
  kitchenGuard,
  dispatchGuard,
  printGuard,
  staffGuard,
  chatGuard,
} from './core/auth/auth.guards';

export const routes: Routes = [
  {
    path: 'pedido/atendimento',
    loadComponent: () =>
      import('./features/chat/public-chat.page').then((page) => page.PublicChatPage),
  },
  {
    path: 'pedido/acompanhar',
    loadComponent: () =>
      import('./features/public-order-tracking/public-order-tracking.page').then(
        (page) => page.PublicOrderTrackingPage,
      ),
  },
  {
    path: 'pedido/finalizar',
    loadComponent: () =>
      import('./features/public-checkout/public-checkout.page').then(
        (page) => page.PublicCheckoutPage,
      ),
  },
  {
    path: 'pedido/carrinho',
    loadComponent: () =>
      import('./features/public-cart/public-cart.page').then((page) => page.PublicCartPage),
  },
  {
    path: 'pedido',
    loadComponent: () => import('./features/menu/menu.page').then((page) => page.MenuPage),
  },
  {
    path: 'comanda/:id/:mode',
    canActivate: [printGuard],
    loadComponent: () => import('./features/printing/print.page').then((page) => page.PrintPage),
  },
  {
    path: '',
    pathMatch: 'full',
    loadComponent: () => import('./features/home/home.page').then((page) => page.HomePage),
  },
  {
    path: 'entrar',
    loadComponent: () => import('./features/auth/login.page').then((page) => page.LoginPage),
  },
  {
    path: 'equipe',
    canActivate: [staffGuard],
    canActivateChild: [staffGuard],
    loadComponent: () =>
      import('./features/staff/staff-layout.page').then((page) => page.StaffLayoutPage),
    children: [
      {
        path: 'reposicao',
        canActivate: [catalogGuard],
        loadComponent: () =>
          import('./features/stock/replenishment.page').then((page) => page.ReplenishmentPage),
      },
      {
        path: 'regioes-entrega',
        canActivate: [deliveryGuard],
        loadComponent: () =>
          import('./features/delivery/delivery-settings.page').then(
            (page) => page.DeliverySettingsPage,
          ),
      },
      {
        path: 'atendimentos',
        canActivate: [chatGuard],
        loadComponent: () =>
          import('./features/chat/chat-list.page').then((page) => page.ChatListPage),
        children: [
          {
            path: ':id',
            loadComponent: () =>
              import('./features/chat/staff-chat-route.page').then(
                (page) => page.StaffChatRoutePage,
              ),
          },
        ],
      },
      {
        path: 'relatorios',
        canActivate: [reportsGuard],
        loadComponent: () =>
          import('./features/reports/sales-report.page').then((page) => page.SalesReportPage),
      },
      {
        path: 'dashboard',
        canActivate: [dashboardGuard],
        loadComponent: () =>
          import('./features/dashboard/dashboard.page').then((page) => page.DashboardPage),
      },
      {
        path: 'expedicao',
        canActivate: [dispatchGuard],
        loadComponent: () =>
          import('./features/dispatch/dispatch.page').then((page) => page.DispatchPage),
      },
      {
        path: 'cozinha',
        canActivate: [kitchenGuard],
        loadComponent: () =>
          import('./features/kitchen/kitchen.page').then((page) => page.KitchenPage),
      },
      {
        path: 'pedidos',
        canActivate: [ordersGuard],
        loadComponent: () =>
          import('./features/orders/orders.page').then((page) => page.OrdersPage),
      },
      {
        path: 'pedidos/:id/pagamentos',
        canActivate: [paymentsGuard],
        loadComponent: () =>
          import('./features/payments/payments.page').then((page) => page.PaymentsPage),
      },
      {
        path: 'pedidos/:id',
        canActivate: [ordersGuard],
        loadComponent: () =>
          import('./features/orders/order-detail.page').then((page) => page.OrderDetailPage),
      },
      {
        path: 'carrinho',
        canActivate: [ordersGuard],
        loadComponent: () => import('./features/cart/cart.page').then((page) => page.CartPage),
      },
      {
        path: 'clientes',
        canActivate: [customersGuard],
        loadComponent: () =>
          import('./features/customers/customers.page').then((page) => page.CustomersPage),
      },
      {
        path: 'clientes/novo',
        canActivate: [customersGuard],
        loadComponent: () =>
          import('./features/customers/customer-form.page').then((page) => page.CustomerFormPage),
      },
      {
        path: 'clientes/:customerId/enderecos',
        canActivate: [customersGuard],
        loadComponent: () =>
          import('./features/customers/addresses.page').then((page) => page.AddressesPage),
      },
      {
        path: 'clientes/:id',
        canActivate: [customersGuard],
        loadComponent: () =>
          import('./features/customers/customer-form.page').then((page) => page.CustomerFormPage),
      },
      {
        path: 'ingredientes',
        canActivate: [catalogGuard],
        loadComponent: () =>
          import('./features/catalog/ingredients.page').then((page) => page.IngredientsPage),
      },
      {
        path: 'ingredientes/novo',
        canActivate: [catalogGuard],
        loadComponent: () =>
          import('./features/catalog/ingredient-form.page').then((page) => page.IngredientFormPage),
      },
      {
        path: 'ingredientes/:id/estoque',
        canActivate: [catalogGuard],
        loadComponent: () => import('./features/stock/stock.page').then((page) => page.StockPage),
      },
      {
        path: 'ingredientes/:id',
        canActivate: [catalogGuard],
        loadComponent: () =>
          import('./features/catalog/ingredient-form.page').then((page) => page.IngredientFormPage),
      },
      {
        path: 'produtos',
        canActivate: [catalogGuard],
        loadComponent: () =>
          import('./features/catalog/products.page').then((page) => page.ProductsPage),
      },
      {
        path: 'produtos/novo',
        canActivate: [catalogGuard],
        loadComponent: () =>
          import('./features/catalog/product-form.page').then((page) => page.ProductFormPage),
      },
      {
        path: 'produtos/:id/ficha-tecnica',
        canActivate: [catalogGuard],
        loadComponent: () =>
          import('./features/catalog/recipe.page').then((page) => page.RecipePage),
      },
      {
        path: 'produtos/:id/cmv',
        canActivate: [catalogGuard],
        loadComponent: () =>
          import('./features/catalog/product-cost.page').then((page) => page.ProductCostPage),
      },
      {
        path: 'produtos/:id',
        canActivate: [catalogGuard],
        loadComponent: () =>
          import('./features/catalog/product-form.page').then((page) => page.ProductFormPage),
      },
      {
        path: 'categorias',
        canActivate: [catalogGuard],
        loadComponent: () =>
          import('./features/catalog/categories.page').then((page) => page.CategoriesPage),
      },
      {
        path: 'categorias/nova',
        canActivate: [catalogGuard],
        loadComponent: () =>
          import('./features/catalog/category-form.page').then((page) => page.CategoryFormPage),
      },
      {
        path: 'categorias/:id',
        canActivate: [catalogGuard],
        loadComponent: () =>
          import('./features/catalog/category-form.page').then((page) => page.CategoryFormPage),
      },
      {
        path: '',
        pathMatch: 'full',
        loadComponent: () =>
          import('./features/staff/account.page').then((page) => page.AccountPage),
      },
      {
        path: 'senha',
        loadComponent: () =>
          import('./features/staff/password.page').then((page) => page.PasswordPage),
      },
      {
        path: 'funcionarios',
        canActivate: [administratorGuard],
        loadComponent: () => import('./features/staff/users.page').then((page) => page.UsersPage),
      },
      {
        path: 'funcionarios/novo',
        canActivate: [administratorGuard],
        loadComponent: () =>
          import('./features/staff/user-form.page').then((page) => page.UserFormPage),
      },
      {
        path: 'funcionarios/:id',
        canActivate: [administratorGuard],
        loadComponent: () =>
          import('./features/staff/user-form.page').then((page) => page.UserFormPage),
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
