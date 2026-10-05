import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
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
import { IonContent } from '@ionic/angular/ion-content';
import { Subscription, finalize, fromEvent } from 'rxjs';
import { OrderStatus, orderStatusLabel } from '../../core/services/order-api.service';
import { formatProductPrice } from '../../core/services/product-api.service';
import { PublicCheckoutState } from '../../core/services/public-checkout-state.service';
import {
  PublicOrderTracking,
  PublicOrderTrackingApi,
} from '../../core/services/public-order-tracking-api.service';

@Component({
  selector: 'app-public-order-tracking',
  host: { class: 'ion-page' },
  imports: [DatePipe, RouterLink, IonContent],
  templateUrl: './public-order-tracking.page.html',
  styleUrl: './public-order-tracking.page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PublicOrderTrackingPage {
  readonly state = inject(PublicCheckoutState);
  private readonly api = inject(PublicOrderTrackingApi);
  private readonly destroyRef = inject(DestroyRef);
  readonly order = signal<PublicOrderTracking | null>(null);
  readonly loading = signal(false);
  readonly unavailable = signal(false);
  readonly error = signal('');
  readonly checkedAt = signal<Date | null>(null);
  readonly terminal = computed(() =>
    ['Finalized', 'Cancelled'].includes(this.order()?.status ?? ''),
  );
  readonly price = formatProductPrice;
  private timer: number | undefined;
  private request: Subscription | undefined;
  readonly access = computed(() => {
    const tracking = this.state.receipt()?.tracking;
    return tracking &&
      typeof tracking.token === 'string' &&
      tracking.token.length > 0 &&
      tracking.token.length <= 2048 &&
      Number.isFinite(Date.parse(tracking.expiresAt))
      ? tracking
      : null;
  });

  constructor() {
    this.destroyRef.onDestroy(() => {
      window.clearTimeout(this.timer);
      this.request?.unsubscribe();
    });
    fromEvent(document, 'visibilitychange')
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        window.clearTimeout(this.timer);
        if (document.hidden) {
          this.request?.unsubscribe();
        } else if (!this.terminal()) {
          this.load();
        }
      });
    this.load();
  }

  load(): void {
    const access = this.access();
    if (!access || this.loading() || this.unavailable() || document.hidden || this.terminal()) {
      return;
    }
    window.clearTimeout(this.timer);
    this.loading.set(true);
    let nextDelay = 15000;
    this.request = this.api
      .get(access.token)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => {
          this.loading.set(false);
          if (
            !this.destroyRef.destroyed &&
            !document.hidden &&
            !this.unavailable() &&
            !this.terminal()
          ) {
            this.timer = window.setTimeout(() => this.load(), nextDelay);
          }
        }),
      )
      .subscribe({
        next: (order) => {
          this.order.set(order);
          this.checkedAt.set(new Date());
          this.error.set('');
        },
        error: (error: unknown) => {
          this.order.set(null);
          this.checkedAt.set(null);
          if (error instanceof HttpErrorResponse && error.status === 404) {
            this.unavailable.set(true);
            this.error.set(
              'Este acesso não está disponível ou expirou. Procure o atendimento com o número do seu pedido.',
            );
          } else {
            nextDelay = error instanceof HttpErrorResponse && error.status === 429 ? 60000 : 30000;
            this.error.set(
              error instanceof HttpErrorResponse && error.status === 429
                ? 'Muitas consultas. Aguarde um minuto para atualizar.'
                : 'Não foi possível atualizar o pedido. Confira sua conexão. Tentaremos novamente.',
            );
          }
        },
      });
  }

  label(status: OrderStatus): string {
    return status === 'Delivered' ? 'Retirado' : orderStatusLabel(status);
  }

  message(status: OrderStatus): string {
    return {
      New: 'Recebemos seu pedido. Aguarde a confirmação da equipe antes de buscar.',
      Confirmed: 'A equipe confirmou seu pedido. Aguarde ficar pronto para retirar.',
      InPreparation: 'Seu pedido está sendo preparado.',
      Ready: 'Seu pedido está pronto para retirada no balcão.',
      AwaitingDelivery: 'Seu pedido aguarda a entrega.',
      OutForDelivery: 'Seu pedido saiu para entrega.',
      Delivered: 'A equipe registrou a retirada do seu pedido.',
      Finalized: 'Pedido finalizado. Obrigado por escolher a Made in Minas!',
      Cancelled: 'Pedido cancelado. Para esclarecer o motivo, procure o atendimento.',
    }[status];
  }
}
