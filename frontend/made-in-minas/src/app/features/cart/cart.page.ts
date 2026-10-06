import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthSession } from '../../core/auth/auth-session.service';
import { finalize } from 'rxjs';
import { apiError } from '../../core/api-error';
import { formatProductPrice } from '../../core/services/product-api.service';
import {
  CartApi,
  CartProduct,
  CartProductPage,
  CartQuote,
  CartQuoteInput,
} from '../../core/services/cart-api.service';
import {
  Address,
  AddressPage,
  Customer,
  CustomerApi,
  CustomerPage,
} from '../../core/services/customer-api.service';
import { CreateOrderInput, OrderApi } from '../../core/services/order-api.service';

interface CartLine {
  key: number;
  product: CartProduct;
  quantity: number | null;
  notes: string;
}

@Component({
  selector: 'app-cart',
  imports: [FormsModule, DatePipe, RouterLink],
  templateUrl: './cart.page.html',
  styleUrl: './cart.page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CartPage {
  readonly formatPrice = formatProductPrice;
  private readonly api = inject(CartApi);
  private readonly customersApi = inject(CustomerApi);
  private readonly ordersApi = inject(OrderApi);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly draftKey = 'made-in-minas.staff-cart.v1.' + inject(AuthSession).user()!.id;
  readonly draftNotice = signal('');
  readonly recoveryError = signal(false);
  private reviewedInput: CartQuoteInput | null = null;
  readonly pendingOrder = signal<CreateOrderInput | null>(null);
  readonly orderError = signal('');
  readonly uncertainOrder = signal(false);
  readonly requestConflict = signal(false);
  readonly createdOrderId = signal<string | null>(null);
  private readonly destroyRef = inject(DestroyRef);
  private nextKey = 0;
  readonly products = signal<CartProductPage | null>(null);
  readonly customers = signal<CustomerPage | null>(null);
  readonly addresses = signal<AddressPage | null>(null);
  readonly selectedCustomer = signal<Customer | null>(null);
  readonly selectedAddress = signal<Address | null>(null);
  readonly lines = signal<CartLine[]>([]);
  readonly quote = signal<CartQuote | null>(null);
  readonly busy = signal(false);
  readonly loadingProducts = signal(false);
  readonly loadingCustomers = signal(false);
  readonly loadingAddresses = signal(false);
  readonly productError = signal('');
  readonly customerError = signal('');
  readonly addressError = signal('');
  readonly error = signal('');
  readonly confirmClear = signal(false);
  productSearch = '';
  customerSearch = '';
  fulfillment: 'Pickup' | 'Delivery' = 'Pickup';
  notes = '';
  deliveryFee: number | null = null;

  constructor() {
    this.restoreDraft();
    this.loadProducts();
    this.loadCustomers();
    const customerId = this.route.snapshot.queryParamMap.get('customerId');
    if (customerId && !this.pendingOrder() && !this.recoveryError()) {
      this.customersApi
        .get(customerId)
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe({
          next: (customer) => this.selectCustomer(customer),
          error: (error) => this.customerError.set(apiError(error)),
        });
    } else if (this.fulfillment === 'Delivery') {
      this.loadAddresses();
    }
  }

  private restoreDraft(): void {
    try {
      const stored = sessionStorage.getItem(this.draftKey);
      if (!stored) {
        return;
      }
      if (stored.length > 131072) {
        throw new Error('Invalid draft');
      }
      const draft = JSON.parse(stored);
      if (
        draft.version !== 1 ||
        !Array.isArray(draft.lines) ||
        draft.lines.length > 50 ||
        !draft.lines.every(
          (line: CartLine) =>
            line &&
            typeof line.product?.id === 'string' &&
            typeof line.product.name === 'string' &&
            Number.isFinite(line.product.price) &&
            typeof line.notes === 'string' &&
            line.notes.length <= 250 &&
            (line.quantity === null || Number.isFinite(line.quantity)),
        ) ||
        typeof draft.notes !== 'string' ||
        draft.notes.length > 500 ||
        !['Pickup', 'Delivery'].includes(draft.fulfillment) ||
        !Number.isFinite(draft.savedAt) ||
        (draft.pending &&
          (typeof draft.pending.requestId !== 'string' ||
            !draft.pending.cart?.items?.length ||
            !draft.quote?.reviewToken))
      ) {
        throw new Error('Invalid draft');
      }
      if (!draft.pending && Date.now() - draft.savedAt > 8 * 60 * 60 * 1000) {
        sessionStorage.removeItem(this.draftKey);
        this.draftNotice.set('O rascunho anterior expirou após 8 horas. Monte uma nova revisão.');
        return;
      }
      this.lines.set(draft.lines.map((line: CartLine) => ({ ...line, key: ++this.nextKey })));
      this.selectedCustomer.set(draft.customer ?? null);
      this.selectedAddress.set(draft.address ?? null);
      this.fulfillment = draft.fulfillment;
      this.deliveryFee = draft.deliveryFee ?? null;
      this.notes = draft.notes;
      if (draft.pending) {
        this.pendingOrder.set(draft.pending);
        this.quote.set(draft.quote);
        this.reviewedInput = draft.pending.cart;
        this.uncertainOrder.set(true);
        this.orderError.set(
          'Há um envio anterior sem resultado confirmado. Tente novamente para recuperar a mesma tentativa.',
        );
      }
      this.draftNotice.set(
        'Rascunho recuperado nesta aba. Revise os dados e os valores atuais antes de registrar.',
      );
    } catch {
      this.recoveryError.set(true);
      this.draftNotice.set(
        'Não foi possível recuperar o rascunho. Confira se o pedido já foi registrado antes de descartar os dados.',
      );
    }
  }

  private saveDraft(): boolean {
    if (this.recoveryError()) {
      return false;
    }
    try {
      if (this.createdOrderId() || (!this.lines().length && !this.selectedCustomer())) {
        sessionStorage.removeItem(this.draftKey);
      } else {
        sessionStorage.setItem(
          this.draftKey,
          JSON.stringify({
            version: 1,
            savedAt: Date.now(),
            lines: this.lines(),
            customer: this.selectedCustomer(),
            address: this.selectedAddress(),
            fulfillment: this.fulfillment,
            notes: this.notes,
            deliveryFee: this.deliveryFee,
            pending: this.pendingOrder(),
            quote: this.pendingOrder() ? this.quote() : null,
          }),
        );
      }
      return true;
    } catch {
      this.draftNotice.set(
        'Não foi possível salvar nesta aba. Libere o armazenamento do navegador antes de registrar o pedido.',
      );
      return false;
    }
  }

  discardBrokenDraft(): void {
    try {
      sessionStorage.removeItem(this.draftKey);
      this.recoveryError.set(false);
      this.draftNotice.set('Rascunho descartado após conferência.');
    } catch {
      this.draftNotice.set('O navegador não permitiu limpar o rascunho.');
    }
  }

  loadProducts(page = 1): void {
    if (this.loadingProducts() || this.busy()) {
      return;
    }
    this.loadingProducts.set(true);
    this.productError.set('');
    this.api
      .products(page, this.productSearch)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.loadingProducts.set(false)),
      )
      .subscribe({
        next: (result) => this.products.set(result),
        error: (error) => {
          this.products.set(null);
          this.productError.set(apiError(error));
        },
      });
  }

  loadCustomers(page = 1): void {
    if (this.loadingCustomers() || this.busy()) {
      return;
    }
    this.loadingCustomers.set(true);
    this.customerError.set('');
    this.customersApi
      .list(page, this.customerSearch, 'true')
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.loadingCustomers.set(false)),
      )
      .subscribe({
        next: (result) => this.customers.set(result),
        error: (error) => {
          this.customers.set(null);
          this.customerError.set(apiError(error));
        },
      });
  }

  selectCustomer(customer: Customer): void {
    if (this.busy() || this.loadingAddresses()) {
      return;
    }
    this.selectedCustomer.set(customer);
    this.selectedAddress.set(null);
    this.addresses.set(null);
    this.addressError.set('');
    this.invalidate();
    if (this.fulfillment === 'Delivery') {
      this.loadAddresses();
    }
  }

  changeFulfillment(value: 'Pickup' | 'Delivery'): void {
    this.fulfillment = value;
    this.deliveryFee = null;
    this.selectedAddress.set(null);
    this.addresses.set(null);
    this.addressError.set('');
    this.invalidate();
    if (value === 'Delivery') {
      this.loadAddresses();
    }
  }

  loadAddresses(page = 1): void {
    const customer = this.selectedCustomer();
    if (!customer || this.loadingAddresses() || this.busy()) {
      return;
    }
    this.loadingAddresses.set(true);
    this.addressError.set('');
    this.customersApi
      .addresses(customer.id, page, 'true')
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.loadingAddresses.set(false)),
      )
      .subscribe({
        next: (result) => this.addresses.set(result),
        error: (error) => {
          this.addresses.set(null);
          this.addressError.set(apiError(error));
        },
      });
  }

  selectAddress(address: Address): void {
    this.selectedAddress.set(address);
    this.invalidate();
  }

  add(product: CartProduct): void {
    if (this.busy() || this.lines().length >= 50) {
      return;
    }
    this.lines.update((lines) => [
      ...lines,
      { key: ++this.nextKey, product, quantity: 1, notes: '' },
    ]);
    this.invalidate();
  }

  updateLine(key: number, field: 'quantity' | 'notes', value: number | null | string): void {
    this.lines.update((lines) =>
      lines.map((line) => (line.key === key ? { ...line, [field]: value } : line)),
    );
    this.invalidate();
  }

  remove(key: number): void {
    this.lines.update((lines) => lines.filter((line) => line.key !== key));
    this.invalidate();
  }

  invalidate(): void {
    this.quote.set(null);
    this.reviewedInput = null;
    this.orderError.set('');
    this.error.set('');
    this.confirmClear.set(false);
    this.saveDraft();
  }

  validItems(): boolean {
    const totals = new Map<string, number>();
    const lines = this.lines();
    if (!lines.length || lines.length > 50 || this.notes.length > 500) {
      return false;
    }
    for (const line of lines) {
      if (
        line.quantity === null ||
        !Number.isInteger(line.quantity) ||
        line.quantity < 1 ||
        line.quantity > 99 ||
        line.notes.length > 250
      ) {
        return false;
      }
      const total = (totals.get(line.product.id) ?? 0) + line.quantity;
      if (total > 99) {
        return false;
      }
      totals.set(line.product.id, total);
    }
    return true;
  }

  canReview(): boolean {
    return (
      !this.recoveryError() &&
      !!this.selectedCustomer() &&
      this.validItems() &&
      !this.loadingAddresses() &&
      (this.fulfillment === 'Pickup' ||
        (!!this.selectedAddress() &&
          this.deliveryFee !== null &&
          Number.isFinite(this.deliveryFee) &&
          this.deliveryFee >= 0 &&
          this.deliveryFee <= 9999.99 &&
          Math.abs(this.deliveryFee * 100 - Math.round(this.deliveryFee * 100)) < 0.000001))
    );
  }

  review(): void {
    const customer = this.selectedCustomer();
    if (
      !customer ||
      this.busy() ||
      this.pendingOrder() ||
      this.createdOrderId() ||
      !this.canReview()
    ) {
      return;
    }
    this.invalidate();
    this.busy.set(true);
    const input: CartQuoteInput = {
      customerId: customer.id,
      fulfillment: this.fulfillment,
      addressId: this.fulfillment === 'Delivery' ? this.selectedAddress()!.id : null,
      deliveryFee: this.fulfillment === 'Delivery' ? this.deliveryFee! : 0,
      items: this.lines().map((line) => ({
        productId: line.product.id,
        quantity: line.quantity!,
        notes: line.notes.trim() || null,
      })),
      notes: this.notes.trim() || null,
    };
    this.api
      .quote(input)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.busy.set(false)),
      )
      .subscribe({
        next: (quote) => {
          this.reviewedInput = input;
          this.quote.set(quote);
        },
        error: (error) => this.error.set(apiError(error)),
      });
  }

  clear(): void {
    this.lines.set([]);
    this.selectedCustomer.set(null);
    this.selectedAddress.set(null);
    this.addresses.set(null);
    this.fulfillment = 'Pickup';
    this.notes = '';
    this.deliveryFee = null;
    this.invalidate();
  }

  register(): void {
    const quote = this.quote();
    if (
      this.busy() ||
      this.createdOrderId() ||
      this.requestConflict() ||
      !quote ||
      !this.reviewedInput
    ) {
      return;
    }
    const previousAttempt = this.pendingOrder();
    const request = previousAttempt ?? {
      requestId: crypto.randomUUID(),
      reviewToken: quote.reviewToken,
      cart: this.reviewedInput,
    };
    this.pendingOrder.set(request);
    if (!this.saveDraft()) {
      this.pendingOrder.set(previousAttempt);
      return;
    }
    this.busy.set(true);
    this.orderError.set('');
    this.ordersApi
      .create(request)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.busy.set(false)),
      )
      .subscribe({
        next: (order) => {
          this.createdOrderId.set(order.id);
          this.uncertainOrder.set(false);
          this.saveDraft();
          void this.router.navigate(['/equipe/pedidos', order.id], { replaceUrl: true });
        },
        error: (error: unknown) => {
          const code: unknown = error instanceof HttpErrorResponse ? error.error?.code : null;
          if (code === 'OrderRequestConflict') {
            this.requestConflict.set(true);
            this.orderError.set(apiError(error));
            return;
          }
          const rejected = error instanceof HttpErrorResponse && [400, 409].includes(error.status);
          if (rejected) {
            this.pendingOrder.set(null);
            this.uncertainOrder.set(false);
            this.invalidate();
            this.error.set(apiError(error));
          } else {
            this.uncertainOrder.set(true);
            this.orderError.set(
              'Não foi possível confirmar o resultado do registro. Use Tentar registro novamente para consultar ou concluir a mesma tentativa. Se sair desta tela, confira os pedidos antes de montar outro carrinho.',
            );
          }
        },
      });
  }
}
