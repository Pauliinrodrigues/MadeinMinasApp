import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { apiError } from '../../core/api-error';
import { AuthSession } from '../../core/auth/auth-session.service';
import { orderStatusLabel } from '../../core/services/order-api.service';
import {
  PaymentApi,
  PaymentCommand,
  PaymentMethod,
  PaymentPage,
  paymentMethodLabel,
  paymentStatusLabel,
} from '../../core/services/payment-api.service';
import { formatProductPrice } from '../../core/services/product-api.service';

@Component({
  selector: 'app-payments',
  imports: [DatePipe, FormsModule, RouterLink],
  templateUrl: './payments.page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PaymentsPage {
  private readonly api = inject(PaymentApi);
  private readonly destroyRef = inject(DestroyRef);
  readonly orderId = inject(ActivatedRoute).snapshot.paramMap.get('id')!;
  readonly canRefund = inject(AuthSession).canRefundPayments;
  readonly result = signal<PaymentPage | null>(null);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly needsRefresh = signal(true);
  readonly retryCommand = signal<PaymentCommand | null>(null);
  readonly action = signal<'receive' | 'cancel' | 'refund' | null>(null);
  readonly error = signal('');
  readonly notice = signal('');
  readonly methodLabel = paymentMethodLabel;
  readonly statusLabel = paymentStatusLabel;
  readonly orderStatusLabel = orderStatusLabel;
  readonly formatPrice = formatProductPrice;
  method: PaymentMethod = 'Pix';
  cashTendered: number | null = null;
  acknowledged = false;
  reason = '';

  constructor() {
    this.load();
  }

  changePreview(): number | null {
    const amount = this.result()?.activePayment?.amount;
    const cash = this.cashTendered;
    if (
      amount === undefined ||
      cash === null ||
      !Number.isFinite(cash) ||
      cash < amount ||
      cash > 9999999999.99 ||
      Math.abs(cash * 100 - Math.round(cash * 100)) > 0.000001
    ) {
      return null;
    }
    return (Math.round(cash * 100) - Math.round(amount * 100)) / 100;
  }

  blocked(): boolean {
    return this.loading() || this.saving() || this.needsRefresh() || this.retryCommand() !== null;
  }

  load(page = this.result()?.page ?? 1): void {
    if (this.loading() || this.saving() || this.retryCommand()) {
      return;
    }
    this.loading.set(true);
    this.needsRefresh.set(true);
    this.action.set(null);
    this.error.set('');
    this.api
      .list(this.orderId, page)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.loading.set(false)),
      )
      .subscribe({
        next: (result) => {
          this.result.set(result);
          this.needsRefresh.set(false);
        },
        error: (error) =>
          this.error.set(
            apiError(error) + ' Atualize os pagamentos antes de registrar outra ação.',
          ),
      });
  }

  create(): void {
    const data = this.result();
    if (
      !data ||
      this.blocked() ||
      data.activePayment ||
      data.orderStatus === 'Cancelled' ||
      data.orderStatus === 'Finalized'
    ) {
      return;
    }
    this.send({
      action: 'create',
      input: {
        requestId: crypto.randomUUID(),
        method: this.method,
        expectedOrderVersion: data.orderVersion,
      },
    });
  }

  choose(action: 'receive' | 'cancel' | 'refund'): void {
    if (this.blocked() || (action === 'refund' && !this.canRefund())) {
      return;
    }
    this.action.set(action);
    this.cashTendered = null;
    this.acknowledged = false;
    this.reason = '';
    this.error.set('');
    this.notice.set('');
  }

  validAction(): boolean {
    const payment = this.result()?.activePayment;
    if (!payment || !this.action() || this.blocked()) {
      return false;
    }
    if (this.action() === 'receive') {
      const cash = this.cashTendered;
      return (
        this.acknowledged &&
        (payment.method !== 'Cash' ||
          (cash !== null &&
            Number.isFinite(cash) &&
            cash >= payment.amount &&
            cash <= 9999999999.99 &&
            Math.abs(cash * 100 - Math.round(cash * 100)) < 0.000001))
      );
    }
    return (
      !!this.reason.trim() &&
      this.reason.length <= 500 &&
      (this.action() !== 'refund' || (this.canRefund() && this.acknowledged))
    );
  }

  submit(): void {
    const payment = this.result()?.activePayment;
    const action = this.action();
    if (!payment || !action || !this.validAction()) {
      return;
    }
    const expectedVersion = payment.version;
    if (action === 'receive') {
      this.send({
        action,
        id: payment.id,
        input: {
          expectedVersion,
          receivedConfirmed: true,
          cashTendered: payment.method === 'Cash' ? this.cashTendered : null,
        },
      });
    } else if (action === 'cancel') {
      this.send({ action, id: payment.id, input: { expectedVersion, reason: this.reason.trim() } });
    } else {
      this.send({
        action,
        id: payment.id,
        input: { expectedVersion, reason: this.reason.trim(), refundedConfirmed: true },
      });
    }
  }

  retry(): void {
    const command = this.retryCommand();
    if (command && !this.saving()) {
      this.send(command);
    }
  }

  private send(command: PaymentCommand): void {
    this.saving.set(true);
    this.retryCommand.set(command);
    this.error.set('');
    this.notice.set('');
    this.api
      .execute(this.orderId, command)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.saving.set(false)),
      )
      .subscribe({
        next: (payment) => {
          this.retryCommand.set(null);
          this.action.set(null);
          this.notice.set(
            'Registro confirmado: ' +
              this.statusLabel(payment.status) +
              '.' +
              (command.action === 'receive' && payment.changeAmount !== null
                ? ' Troco: ' + this.formatPrice(payment.changeAmount) + '.'
                : ''),
          );
          this.saving.set(false);
          this.load(1);
        },
        error: (error: unknown) => {
          if (!(error instanceof HttpErrorResponse) || error.status === 0 || error.status >= 500) {
            this.error.set(
              'Não foi possível confirmar o resultado. Repita o registro para consultar ou concluir a mesma tentativa. Não receba nem devolva o dinheiro novamente.',
            );
          } else {
            this.retryCommand.set(null);
            this.needsRefresh.set(true);
            this.action.set(null);
            this.error.set(
              apiError(error) +
                ' Atualize os pagamentos para conferir o histórico antes de continuar.',
            );
          }
        },
      });
  }
}
