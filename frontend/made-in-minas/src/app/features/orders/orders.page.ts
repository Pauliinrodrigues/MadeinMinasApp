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
  readonly soundWarning = signal('');
  private audio?: AudioContext;
  private notificationTone?: OscillatorNode;
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
      this.stopNotification();
      void this.audio?.close();
    });
  }

  async toggleSound(): Promise<void> {
    if (this.soundEnabled()) {
      this.soundEnabled.set(false);
      this.stopNotification();
      return;
    }
    try {
      if (!this.audio || this.audio.state === 'closed') {
        const audio = new AudioContext();
        this.audio = audio;
        audio.addEventListener('statechange', () => {
          if (this.destroyRef.destroyed || !this.soundEnabled() || audio.state === 'running') {
            return;
          }
          this.soundEnabled.set(false);
          this.stopNotification();
          this.soundWarning.set(
            'O som foi interrompido pelo navegador. Clique em Ativar aviso sonoro para reativá-lo.',
          );
        });
      }
      await this.audio.resume();
      if (this.destroyRef.destroyed) {
        return;
      }
      if (this.audio.state !== 'running') {
        throw new Error('Audio context did not resume.');
      }
      this.soundEnabled.set(true);
      this.soundWarning.set('');
      this.playNotification();
    } catch {
      if (this.destroyRef.destroyed) {
        return;
      }
      this.soundEnabled.set(false);
      this.soundWarning.set(
        'O navegador não permitiu ativar o som. Os avisos visuais continuam disponíveis.',
      );
    }
  }

  private playNotification(): void {
    if (!this.soundEnabled() || this.audio?.state !== 'running') {
      return;
    }
    this.stopNotification();
    const tone = this.audio.createOscillator();
    const volume = this.audio.createGain();
    const start = this.audio.currentTime + 0.01;
    const duration = 2.4;
    volume.gain.setValueAtTime(0, start);
    volume.gain.linearRampToValueAtTime(0.16, start + 0.03);
    volume.gain.setValueAtTime(0.16, start + duration - 0.15);
    volume.gain.linearRampToValueAtTime(0, start + duration);
    tone.connect(volume).connect(this.audio.destination);
    tone.frequency.setValueAtTime(600, start);
    for (let cycle = 0; cycle < 3; cycle++) {
      tone.frequency.linearRampToValueAtTime(1000, start + cycle * 0.8 + 0.4);
      tone.frequency.linearRampToValueAtTime(600, start + (cycle + 1) * 0.8);
    }
    tone.addEventListener('ended', () => {
      tone.disconnect();
      volume.disconnect();
      if (this.notificationTone === tone) {
        this.notificationTone = undefined;
      }
    });
    tone.start(start);
    tone.stop(start + duration);
    this.notificationTone = tone;
  }

  private stopNotification(): void {
    this.notificationTone?.stop();
    this.notificationTone = undefined;
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
