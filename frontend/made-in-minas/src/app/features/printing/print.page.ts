import { CurrencyPipe, DatePipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Subscription, finalize, interval, timeout } from 'rxjs';
import { environment } from '../../../environments/environment';
import { apiError } from '../../core/api-error';
import { DispatchOrder } from '../../core/services/dispatch-api.service';
import { OrderStatus, orderStatusLabel } from '../../core/services/order-api.service';

interface PrintOrder {
  id: string;
  number: number;
  status: OrderStatus;
  version: number;
  fulfillment: string;
  createdAt: string;
  notes: string | null;
  items: {
    name: string;
    quantity: number;
    notes: string | null;
    unitPrice?: number;
    lineTotal?: number;
  }[];
  customerName?: string;
  customerPhone?: string;
  address?: DispatchOrder['address'];
  subtotal?: number;
  deliveryFee?: number;
  total?: number;
  payment?: DispatchOrder['payment'];
}
interface PrintDocument {
  generatedAt: string;
  order: PrintOrder;
}

@Component({
  selector: 'app-print',
  imports: [CurrencyPipe, DatePipe, RouterLink],
  templateUrl: './print.page.html',
  styleUrl: './print.page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PrintPage {
  private readonly http = inject(HttpClient);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  mode = '';
  private id = '';
  private request?: Subscription;
  get returnPath(): string {
    return this.route.snapshot.queryParamMap.get('from') === 'kitchen'
      ? '/equipe/cozinha'
      : this.route.snapshot.queryParamMap.get('from') === 'dispatch'
        ? '/equipe/expedicao'
        : '/equipe/pedidos/' + this.id;
  }
  readonly document = signal<PrintDocument | null>(null);
  readonly loading = signal(false);
  readonly error = signal('');
  readonly notice = signal('');
  readonly width = signal('80');
  private readonly receivedAt = signal<number | null>(null);
  private readonly tick = signal(performance.now());
  readonly stale = computed(
    () => this.receivedAt() === null || this.tick() - this.receivedAt()! >= 30000,
  );
  readonly statusLabel = orderStatusLabel;
  constructor() {
    window.document.body.classList.add('print-view');
    this.destroyRef.onDestroy(() => window.document.body.classList.remove('print-view'));
    interval(1000)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.tick.set(performance.now()));
    this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => {
      this.request?.unsubscribe();
      this.mode = params.get('mode')!;
      this.id = params.get('id')!;
      this.load();
    });
  }
  load(): void {
    if (this.loading()) {
      return;
    }
    this.loading.set(true);
    this.document.set(null);
    this.receivedAt.set(null);
    this.error.set('');
    this.notice.set('');
    this.request = this.http
      .get<PrintDocument>(environment.apiBaseUrl + '/print/orders/' + this.id + '/' + this.mode)
      .pipe(
        timeout(20000),
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.loading.set(false)),
      )
      .subscribe({
        next: (data) => {
          this.document.set(data);
          this.receivedAt.set(performance.now());
          this.tick.set(performance.now());
        },
        error: (error) => this.error.set(apiError(error)),
      });
  }
  print(): void {
    if (
      this.loading() ||
      !this.document() ||
      this.stale() ||
      this.receivedAt() === null ||
      performance.now() - this.receivedAt()! >= 30000
    ) {
      return;
    }
    window.print();
    this.notice.set(
      'Diálogo solicitado. Confira a saída na impressora; cancelar ou fechar o diálogo não confirma impressão.',
    );
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
