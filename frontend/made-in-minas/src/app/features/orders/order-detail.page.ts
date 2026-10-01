import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { apiError } from '../../core/api-error';
import { AuthSession } from '../../core/auth/auth-session.service';
import {
  Order,
  OrderApi,
  OrderStatusInput,
  orderStatusLabel,
} from '../../core/services/order-api.service';
import { formatProductPrice } from '../../core/services/product-api.service';

@Component({
  selector: 'app-order-detail',
  imports: [FormsModule, RouterLink, DatePipe],
  templateUrl: './order-detail.page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OrderDetailPage {
  private readonly api = inject(OrderApi);
  private readonly session = inject(AuthSession);
  private readonly destroyRef = inject(DestroyRef);
  private readonly id = inject(ActivatedRoute).snapshot.paramMap.get('id')!;
  readonly order = signal<Order | null>(null);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly error = signal('');
  readonly notice = signal('');
  readonly pending = signal<'Confirmed' | 'Cancelled' | null>(null);
  readonly needsRefresh = signal(false);
  readonly statusLabel = orderStatusLabel;
  readonly formatPrice = formatProductPrice;
  readonly canManagePayments = this.session.canManagePayments;
  reason = '';

  constructor() {
    this.load();
  }

  load(): void {
    if (this.loading() || this.saving()) {
      return;
    }
    this.loading.set(true);
    this.error.set('');
    this.order.set(null);
    this.pending.set(null);
    this.api
      .get(this.id)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.loading.set(false)),
      )
      .subscribe({
        next: (order) => {
          this.order.set(order);
          this.needsRefresh.set(false);
        },
        error: (error) => this.error.set(apiError(error)),
      });
  }

  canCancel(): boolean {
    const status = this.order()?.status;
    return (
      status === 'New' ||
      ((status === 'Confirmed' || status === 'InPreparation' || status === 'Ready') &&
        this.session.user()?.role === 'Administrator')
    );
  }

  requestStatus(status: 'Confirmed' | 'Cancelled'): void {
    this.pending.set(status);
    this.reason = '';
    this.error.set('');
    this.notice.set('');
  }

  changeStatus(): void {
    const order = this.order();
    const status = this.pending();
    if (
      !order ||
      !status ||
      this.saving() ||
      this.needsRefresh() ||
      (status === 'Cancelled' && !this.reason.trim())
    ) {
      return;
    }
    const input: OrderStatusInput = {
      status,
      expectedVersion: order.version,
      reason: status === 'Cancelled' ? this.reason.trim() : null,
    };
    this.saving.set(true);
    this.error.set('');
    this.api
      .status(order.id, input)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.saving.set(false)),
      )
      .subscribe({
        next: (result) => {
          this.order.set(result);
          this.pending.set(null);
          this.notice.set(
            'Pedido ' + (result.status === 'Confirmed' ? 'confirmado.' : 'cancelado.'),
          );
        },
        error: (error) => {
          this.needsRefresh.set(true);
          this.pending.set(null);
          this.error.set(
            apiError(error) + ' Atualize o pedido para conferir o status antes de uma nova ação.',
          );
        },
      });
  }
}
