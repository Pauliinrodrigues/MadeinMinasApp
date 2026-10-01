import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { finalize } from 'rxjs';
import { apiError } from '../../core/api-error';
import { formatProductPrice } from '../../core/services/product-api.service';
import {
  CartApi,
  CartProduct,
  CartProductPage,
  CartQuote,
} from '../../core/services/cart-api.service';
import {
  Address,
  AddressPage,
  Customer,
  CustomerApi,
  CustomerPage,
} from '../../core/services/customer-api.service';

interface CartLine {
  key: number;
  product: CartProduct;
  quantity: number | null;
  notes: string;
}

@Component({
  selector: 'app-cart',
  imports: [FormsModule, DatePipe],
  templateUrl: './cart.page.html',
  styleUrl: './cart.page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CartPage {
  readonly formatPrice = formatProductPrice;
  private readonly api = inject(CartApi);
  private readonly customersApi = inject(CustomerApi);
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

  constructor() {
    this.loadProducts();
    this.loadCustomers();
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
    this.error.set('');
    this.confirmClear.set(false);
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
      !!this.selectedCustomer() &&
      this.validItems() &&
      !this.loadingAddresses() &&
      (this.fulfillment === 'Pickup' || !!this.selectedAddress())
    );
  }

  review(): void {
    const customer = this.selectedCustomer();
    if (!customer || this.busy() || !this.canReview()) {
      return;
    }
    this.invalidate();
    this.busy.set(true);
    this.api
      .quote({
        customerId: customer.id,
        fulfillment: this.fulfillment,
        addressId: this.fulfillment === 'Delivery' ? this.selectedAddress()!.id : null,
        items: this.lines().map((line) => ({
          productId: line.product.id,
          quantity: line.quantity!,
          notes: line.notes.trim() || null,
        })),
        notes: this.notes.trim() || null,
      })
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.busy.set(false)),
      )
      .subscribe({
        next: (quote) => this.quote.set(quote),
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
    this.invalidate();
  }
}
