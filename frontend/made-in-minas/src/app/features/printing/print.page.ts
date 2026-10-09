import { CurrencyPipe, DatePipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  afterRenderEffect,
  computed,
  inject,
  signal,
  viewChild,
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
  private readonly receipt = viewChild<ElementRef<HTMLElement>>('receipt');
  private readonly pageStyle = window.document.createElement('style');
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
  readonly sending = signal(false);
  readonly sent = signal(false);
  private printRequestId = crypto.randomUUID();
  readonly width = signal('80');
  private readonly receivedAt = signal<number | null>(null);
  private readonly tick = signal(performance.now());
  readonly stale = computed(
    () => this.receivedAt() === null || this.tick() - this.receivedAt()! >= 30000,
  );
  readonly statusLabel = orderStatusLabel;
  constructor() {
    window.document.body.classList.add('print-view');
    this.pageStyle.media = 'print';
    this.pageStyle.setAttribute('data-receipt-page', '');
    window.document.head.appendChild(this.pageStyle);
    afterRenderEffect(() => this.preparePage());
    window.addEventListener('beforeprint', this.preparePage);
    this.destroyRef.onDestroy(() => {
      window.document.body.classList.remove('print-view');
      window.removeEventListener('beforeprint', this.preparePage);
      this.pageStyle.remove();
    });
    interval(1000)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.tick.set(performance.now()));
    this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => {
      this.request?.unsubscribe();
      this.mode = params.get('mode')!;
      this.id = params.get('id')!;
      this.printRequestId = crypto.randomUUID();
      this.sent.set(false);
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
    this.preparePage();
    window.print();
    this.notice.set(
      'Diálogo solicitado. Confira a saída na impressora; cancelar ou fechar o diálogo não confirma impressão.',
    );
  }

  sendToPrinter(): void {
    if (this.sending() || this.sent() || this.loading() || !this.document() || this.stale()) {
      return;
    }
    this.sending.set(true);
    this.error.set('');
    this.http
      .post<{ id: string; state: string }>(
        environment.apiBaseUrl + '/printing/orders/' + this.id + '/' + this.mode,
        { requestId: this.printRequestId, expectedVersion: this.document()!.order.version },
      )
      .pipe(
        timeout(20000),
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.sending.set(false)),
      )
      .subscribe({
        next: () => {
          this.sent.set(true);
          this.notice.set(
            'Comanda registrada na fila. O agente enviará à impressora sem diálogo. Confira a saída no papel.',
          );
        },
        error: (error) => this.error.set(apiError(error)),
      });
  }

  prepareCopy(): void {
    this.printRequestId = crypto.randomUUID();
    this.sent.set(false);
    this.notice.set('Outra cópia preparada. Confira o papel antes de enviar novamente.');
  }

  private readonly preparePage = (): void => {
    const receipt = this.receipt()?.nativeElement;
    const width = this.width();
    if (!this.document() || !receipt) {
      this.pageStyle.textContent = '';
      return;
    }
    if (width === 'A4') {
      this.pageStyle.textContent = '@page { size: A4; margin: 4mm; }';
      return;
    }
    // Medir na largura física, mesmo quando a prévia cabe em uma tela menor.
    receipt.classList.add('measure-print');
    let height: number;
    try {
      height = receipt.getBoundingClientRect().height;
    } finally {
      receipt.classList.remove('measure-print');
    }
    // CSS usa 96 px/polegada. A folga inferior evita uma página extra por arredondamento.
    const millimeters = Math.ceil(((height * 25.4) / 96 + 0.5) * 10) / 10;
    this.pageStyle.textContent = `@page { size: ${width === '58' ? 58 : 80}mm ${millimeters}mm; margin: 0; }`;
  };
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
