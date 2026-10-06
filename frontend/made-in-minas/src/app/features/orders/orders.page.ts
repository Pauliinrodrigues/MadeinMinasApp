import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { finalize, forkJoin, Subscription, timeout } from 'rxjs';
import { apiError } from '../../core/api-error';
import {
  OrderApi,
  OrderPage,
  orderStatusLabel,
  orderPaymentStatusLabel,
} from '../../core/services/order-api.service';
import { formatProductPrice } from '../../core/services/product-api.service';

@Component({
  selector: 'app-orders',
  imports: [FormsModule, RouterLink, DatePipe],
  templateUrl: './orders.page.html',
  styleUrl: './orders.page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OrdersPage {
  private readonly api = inject(OrderApi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly route = inject(ActivatedRoute);
  private request?: Subscription;
  readonly result = signal<OrderPage | null>(null);
  readonly loading = signal(false);
  readonly error = signal('');
  readonly newCount = signal<number | null>(null);
  readonly updatedAt = signal<Date | null>(null);
  readonly notification = signal('');
  readonly soundEnabled = signal(false);
  private audio?: AudioContext;
  private latestNewNumber: number | null = null;
  readonly appliedFilters = signal({ search: '', status: '', origin: '', paymentStatus: '' });
  readonly statusLabel = orderStatusLabel;
  readonly paymentLabel = orderPaymentStatusLabel;
  readonly formatPrice = formatProductPrice;
  search = '';
  status = '';
  origin = '';
  paymentStatus = '';
  page = 1;

  constructor() {
    this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => {
      const status = params.get('status') ?? '';
      this.status = [
        'New',
        'Confirmed',
        'InPreparation',
        'Ready',
        'AwaitingDelivery',
        'OutForDelivery',
        'Delivered',
        'Finalized',
        'Cancelled',
      ].includes(status)
        ? status
        : '';
      this.load();
    });
    const refresh = () => {
      if (!document.hidden && !this.loading()) {
        this.refresh();
      }
    };
    const timer = setInterval(refresh, 10000);
    document.addEventListener('visibilitychange', refresh);
    this.destroyRef.onDestroy(() => {
      clearInterval(timer);
      document.removeEventListener('visibilitychange', refresh);
      void this.audio?.close();
    });
  }

  async toggleSound(): Promise<void> {
    if (this.soundEnabled()) {
      this.soundEnabled.set(false);
      return;
    }
    try {
      this.audio ??= new AudioContext();
      await this.audio.resume();
      this.soundEnabled.set(true);
    } catch {
      this.notification.set(
        'O navegador não permitiu ativar o som. Os avisos visuais continuam disponíveis.',
      );
    }
  }

  private playNotification(): void {
    if (!this.soundEnabled() || this.audio?.state !== 'running') {
      return;
    }
    const tone = this.audio.createOscillator();
    const volume = this.audio.createGain();
    volume.gain.setValueAtTime(0.08, this.audio.currentTime);
    volume.gain.exponentialRampToValueAtTime(0.001, this.audio.currentTime + 0.25);
    tone.connect(volume).connect(this.audio.destination);
    tone.frequency.value = 660;
    tone.start();
    tone.stop(this.audio.currentTime + 0.25);
  }

  quickFilter(status: string, paymentStatus = ''): void {
    this.status = status;
    this.search = '';
    this.origin = '';
    this.paymentStatus = paymentStatus;
    this.load();
  }

  queueIsActive(status: string, paymentStatus = ''): boolean {
    const applied = this.appliedFilters();
    return (
      applied.status === status &&
      applied.paymentStatus === paymentStatus &&
      !applied.search &&
      !applied.origin
    );
  }

  filtersChanged(): boolean {
    const applied = this.appliedFilters();
    return (
      this.search.trim() !== applied.search ||
      this.status !== applied.status ||
      this.origin !== applied.origin ||
      this.paymentStatus !== applied.paymentStatus
    );
  }

  load(page = 1): void {
    this.appliedFilters.set({
      search: this.search.trim(),
      status: this.status,
      origin: this.origin,
      paymentStatus: this.paymentStatus,
    });
    this.changePage(page);
  }

  changePage(page: number): void {
    this.request?.unsubscribe();
    this.page = page;
    this.result.set(null);
    this.refresh();
  }

  private refresh(): void {
    const filters = this.appliedFilters();
    this.error.set('');
    this.loading.set(true);
    this.request = forkJoin({
      orders: this.api.list(
        this.page,
        filters.search,
        filters.status,
        filters.origin,
        filters.paymentStatus,
      ),
      newOrders: this.api.list(1, '', 'New'),
    })
      .pipe(
        timeout(15000),
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.loading.set(false)),
      )
      .subscribe({
        next: ({ orders, newOrders }) => {
          this.result.set(orders);
          this.newCount.set(newOrders.totalCount);
          this.updatedAt.set(new Date());
          const latest = Math.max(0, ...newOrders.items.map((order) => order.number));
          if (this.latestNewNumber !== null && latest > this.latestNewNumber) {
            this.notification.set('Chegou pedido novo. Confira a fila aguardando confirmação.');
            this.playNotification();
          }
          this.latestNewNumber = Math.max(this.latestNewNumber ?? 0, latest);
        },
        error: (error) => this.error.set(apiError(error)),
      });
  }
}
