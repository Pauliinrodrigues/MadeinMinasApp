import { DatePipe, CurrencyPipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { finalize, interval } from 'rxjs';
import { apiError } from '../../core/api-error';
import {
  DispatchApi,
  DispatchOrder,
  DispatchPageResult,
  DispatchStatus,
  DispatchStatusInput,
} from '../../core/services/dispatch-api.service';
import { orderStatusLabel } from '../../core/services/order-api.service';

@Component({
  selector: 'app-dispatch',
  imports: [DatePipe, CurrencyPipe, RouterLink],
  templateUrl: './dispatch.page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DispatchPage {
  private readonly api = inject(DispatchApi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly tick = signal(performance.now());
  private readonly receivedAt = signal<number | null>(null);
  readonly filters: DispatchStatus[] = ['Ready', 'AwaitingDelivery', 'OutForDelivery', 'Delivered'];
  readonly selected = signal<DispatchStatus>('Ready');
  readonly result = signal<DispatchPageResult | null>(null);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly needsRefresh = signal(true);
  readonly error = signal('');
  readonly notice = signal('');
  readonly pending = signal<{ order: DispatchOrder; input: DispatchStatusInput } | null>(null);
  readonly stale = computed(
    () => this.receivedAt() === null || this.tick() - this.receivedAt()! >= 30000,
  );
  readonly statusLabel = orderStatusLabel;
  private page = 1;
  constructor() {
    interval(1000)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.tick.set(performance.now()));
    interval(10000)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        if (!this.pending()) {
          this.load();
        }
      });
    this.load();
  }
  blocked(): boolean {
    return (
      this.loading() ||
      this.saving() ||
      this.needsRefresh() ||
      this.stale() ||
      (this.receivedAt() !== null && performance.now() - this.receivedAt()! >= 30000)
    );
  }
  filter(status: DispatchStatus): void {
    if (this.loading() || this.saving()) {
      return;
    }
    this.selected.set(status);
    this.page = 1;
    this.result.set(null);
    this.needsRefresh.set(true);
    this.load();
  }
  turnPage(page: number): void {
    if (this.blocked() || this.pending()) {
      return;
    }
    this.page = page;
    this.load();
  }
  load(): void {
    if (this.loading() || this.saving()) {
      return;
    }
    this.pending.set(null);
    this.loading.set(true);
    this.api
      .list(this.selected(), this.page)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.loading.set(false)),
      )
      .subscribe({
        next: (data) => {
          this.result.set(data);
          this.page = data.page;
          this.receivedAt.set(performance.now());
          this.tick.set(performance.now());
          this.needsRefresh.set(false);
          this.error.set('');
        },
        error: (error) => {
          this.needsRefresh.set(true);
          this.error.set(apiError(error) + ' Atualize a fila antes de alterar pedidos.');
        },
      });
  }
  next(order: DispatchOrder): DispatchStatusInput['status'] {
    if (order.status === 'Ready') {
      return order.fulfillment === 'Delivery' ? 'AwaitingDelivery' : 'Delivered';
    }
    return order.status === 'AwaitingDelivery'
      ? 'OutForDelivery'
      : order.status === 'OutForDelivery'
        ? 'Delivered'
        : 'Finalized';
  }
  action(order: DispatchOrder): string {
    return {
      AwaitingDelivery: 'Liberar para entrega',
      OutForDelivery: 'Registrar saída',
      Delivered: order.fulfillment === 'Pickup' ? 'Registrar retirada' : 'Confirmar entrega',
      Finalized: 'Finalizar pedido',
    }[this.next(order)];
  }
  unpaid(order: DispatchOrder): boolean {
    return (
      order.status === 'Delivered' &&
      (order.payment?.status !== 'Received' || order.payment.amount !== order.total)
    );
  }
  choose(order: DispatchOrder): void {
    if (this.blocked() || this.pending() || this.unpaid(order)) {
      return;
    }
    this.notice.set('');
    this.pending.set({
      order,
      input: { status: this.next(order), expectedVersion: order.version },
    });
  }
  confirm(): void {
    const change = this.pending();
    if (!change || this.blocked()) {
      return;
    }
    this.saving.set(true);
    this.api
      .status(change.order.id, change.input)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.saving.set(false)),
      )
      .subscribe({
        next: (order) => {
          this.pending.set(null);
          this.notice.set('Pedido #' + order.number + ': ' + this.statusLabel(order.status) + '.');
          this.saving.set(false);
          this.needsRefresh.set(true);
          this.load();
        },
        error: (error) => {
          this.pending.set(null);
          this.needsRefresh.set(true);
          this.error.set(apiError(error) + ' Atualize e confira o status antes de repetir.');
        },
      });
  }
  method(value: string): string {
    return (
      (
        { Cash: 'Dinheiro', Pix: 'Pix', CreditCard: 'Crédito', DebitCard: 'Débito' } as Record<
          string,
          string
        >
      )[value] ?? value
    );
  }
}
