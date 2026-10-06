import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { finalize, Subscription } from 'rxjs';
import { apiError } from '../../core/api-error';
import { AuthSession } from '../../core/auth/auth-session.service';
import {
  Order,
  OrderApi,
  OrderStatusInput,
  orderStatusLabel,
} from '../../core/services/order-api.service';
import { formatProductPrice } from '../../core/services/product-api.service';
import {
  PaymentApi,
  PaymentPage,
  paymentMethodLabel,
  paymentStatusLabel,
} from '../../core/services/payment-api.service';

@Component({
  selector: 'app-order-detail',
  imports: [FormsModule, RouterLink, DatePipe],
  templateUrl: './order-detail.page.html',
  styles: `
    .order-content {
      display: grid;
      grid-template-columns: minmax(0, 1.4fr) minmax(240px, 1fr);
      gap: 24px;
      align-items: start;
      margin-block: 24px;
    }
    .order-summary {
      background: #faf5ec;
      border-radius: 12px;
      padding: 18px;
    }
    .order-content li {
      margin-bottom: 16px;
    }
    summary {
      cursor: pointer;
      font-size: 20px;
      font-weight: 700;
      padding: 8px 0;
    }
    @media (max-width: 850px) {
      .order-content {
        grid-template-columns: 1fr;
      }
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OrderDetailPage {
  private readonly api = inject(OrderApi);
  private readonly paymentsApi = inject(PaymentApi);
  private paymentRequest?: Subscription;
  readonly payments = signal<PaymentPage | null>(null);
  readonly paymentError = signal(false);
  readonly paymentMethodLabel = paymentMethodLabel;
  readonly paymentStatusLabel = paymentStatusLabel;
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
  readonly decimal = new Intl.NumberFormat('pt-BR', { maximumFractionDigits: 3 });
  readonly stockLabels = {
    Pending: 'Baixa pendente da confirmação',
    Consumed: 'Ingredientes baixados na confirmação',
    Returned: 'Ingredientes devolvidos ao estoque',
    Retained: 'Consumo mantido após início do preparo',
    Legacy: 'Pedido anterior ao controle automático; sem baixa retroativa',
    NotRequired: 'Cancelado antes da confirmação; sem consumo',
  };
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
    this.paymentRequest?.unsubscribe();
    this.payments.set(null);
    this.paymentError.set(false);
    if (this.canManagePayments()) {
      this.paymentRequest = this.paymentsApi
        .list(this.id, 1)
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe({
          next: (payments) => this.payments.set(payments),
          error: () => this.paymentError.set(true),
        });
    }
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
      ((status === 'Confirmed' ||
        status === 'InPreparation' ||
        status === 'Ready' ||
        status === 'AwaitingDelivery' ||
        status === 'OutForDelivery') &&
        this.session.user()?.role === 'Administrator')
    );
  }

  nextAction(): string {
    switch (this.order()?.status) {
      case 'New':
        return 'Confira contato, itens e recebimento; depois confirme o pedido.';
      case 'Confirmed':
        return 'Pedido confirmado. A cozinha pode iniciar o preparo.';
      case 'InPreparation':
        return 'Acompanhe o preparo na cozinha.';
      case 'Ready':
      case 'AwaitingDelivery':
        return 'Confira embalagem, destino e pagamento na expedição.';
      case 'OutForDelivery':
        return 'Aguarde a confirmação da entrega e confira o recebimento.';
      case 'Delivered':
        return 'Confira o pagamento e finalize o pedido na expedição.';
      case 'Finalized':
        return 'Pedido finalizado.';
      case 'Cancelled':
        return 'Pedido cancelado. Consulte o histórico abaixo.';
      default:
        return '';
    }
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
