import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { finalize, Subscription } from 'rxjs';
import { apiError } from '../../core/api-error';
import { OrderApi, OrderPage, orderStatusLabel } from '../../core/services/order-api.service';
import { formatProductPrice } from '../../core/services/product-api.service';

@Component({
  selector: 'app-orders',
  imports: [FormsModule, RouterLink, DatePipe],
  templateUrl: './orders.page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OrdersPage {
  private readonly api = inject(OrderApi);
  private readonly destroyRef = inject(DestroyRef);
  private request?: Subscription;
  readonly result = signal<OrderPage | null>(null);
  readonly loading = signal(false);
  readonly error = signal('');
  readonly statusLabel = orderStatusLabel;
  readonly formatPrice = formatProductPrice;
  search = '';
  status = '';
  page = 1;

  constructor() {
    this.load();
  }

  load(page = 1): void {
    this.request?.unsubscribe();
    this.page = page;
    this.result.set(null);
    this.error.set('');
    this.loading.set(true);
    this.request = this.api
      .list(page, this.search, this.status)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.loading.set(false)),
      )
      .subscribe({
        next: (result) => this.result.set(result),
        error: (error) => this.error.set(apiError(error)),
      });
  }
}
