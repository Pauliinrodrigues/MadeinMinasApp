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
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { IonContent } from '@ionic/angular/ion-content';
import { finalize, fromEvent, interval, Subscription } from 'rxjs';
import {
  ChatMessage,
  ChatOrderState,
  ChatTranscript,
  PublicChatApi,
  chatAttendanceLabel,
} from '../../core/services/chat-api.service';
import { PublicChatState } from '../../core/services/public-chat-state.service';
import { ChatMessagesComponent } from './chat-messages.component';
import { RequestedPayment } from '../../shared/requested-payment.component';
import { PublicCheckoutState } from '../../core/services/public-checkout-state.service';
import { PublicOrderHistory } from '../../core/services/public-order-history.service';
import { apiError } from '../../core/api-error';
import { environment } from '../../../environments/environment';

@Component({
  selector: 'app-public-chat',
  host: { class: 'ion-page' },
  imports: [FormsModule, RouterLink, IonContent, ChatMessagesComponent, RequestedPayment],
  templateUrl: './public-chat.page.html',
  styleUrl: './public-chat.page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PublicChatPage {
  readonly testing = environment.testing;
  readonly state = inject(PublicChatState);
  private readonly checkout = inject(PublicCheckoutState);
  private readonly history = inject(PublicOrderHistory);
  readonly receipt = computed(
    () =>
      this.history.entries().find((entry) => entry.number === this.history.selectedNumber()) ??
      this.checkout.receipt() ??
      this.history.entries()[0],
  );
  private readonly api = inject(PublicChatApi);
  private readonly destroyRef = inject(DestroyRef);
  readonly transcript = signal<ChatTranscript | null>(null);
  readonly messages = signal<ChatMessage[]>([]);
  readonly order = signal<ChatOrderState | null>(null);
  readonly loading = signal(false);
  readonly busy = signal(false);
  readonly unavailable = signal(false);
  readonly fresh = signal(false);
  readonly error = signal('');
  readonly loadError = signal('');
  readonly attendanceLabel = chatAttendanceLabel;
  readonly automatic = computed(() => this.transcript()?.automationMode === 'Automatic');
  readonly assistantPending = computed(() =>
    ['Pending', 'Processing'].includes(this.transcript()?.automationState ?? ''),
  );
  readonly orderProcessing = computed(
    () =>
      this.assistantPending() ||
      !!this.transcript()?.hasMore ||
      !!this.state.value().pendingMessage,
  );
  readonly missingOrderFields = computed(() => {
    const labels: Record<string, string> = {
      items: 'produtos',
      name: 'nome',
      phone: 'telefone com DDD',
      fulfillment: 'entrega ou retirada',
      address: 'endereço completo para entrega',
      paymentPreference: 'forma de pagamento e troco, se necessário',
    };
    return (this.order()?.missingFields ?? []).map(
      (field) => labels[field] ?? 'informações do pedido',
    );
  });
  private readRequest?: Subscription;
  private retryAt = 0;
  private invalidReviewToken: string | null = null;
  name = '';
  text = '';

  shareOrder(): void {
    const receipt = this.receipt();
    if (
      !receipt ||
      this.busy() ||
      this.state.value().pendingMessage ||
      this.state.value().pendingStart
    ) {
      return;
    }
    const message = `Preciso de ajuda com o pedido #${receipt.number}.`;
    if ((this.text + '\n' + message).length <= 2000) {
      this.text = this.text.trim() ? this.text + '\n' + message : message;
    }
  }

  constructor() {
    interval(5000)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.load());
    fromEvent(document, 'visibilitychange')
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        if (document.hidden) {
          this.readRequest?.unsubscribe();
          this.fresh.set(false);
        } else {
          this.load();
        }
      });
    this.load();
  }

  load(): void {
    const access = this.state.value().access;
    if (
      !access ||
      this.loading() ||
      this.busy() ||
      this.unavailable() ||
      document.hidden ||
      Date.now() < this.retryAt ||
      (this.transcript()?.status === 'Closed' && !this.transcript()?.hasMore && this.fresh())
    ) {
      return;
    }
    this.loading.set(true);
    const after = this.messages().at(-1)?.sequence ?? 0;
    this.readRequest = this.api
      .read(access.token, after)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => {
          this.loading.set(false);
          if (
            !this.destroyRef.destroyed &&
            this.transcript()?.hasMore &&
            this.fresh() &&
            !document.hidden
          ) {
            queueMicrotask(() => this.load());
          }
        }),
      )
      .subscribe({
        next: (result) => {
          this.transcript.set(result);
          this.acceptOrderState(result.order ?? null);
          this.messages.update((messages) => [
            ...messages,
            ...result.messages.filter(
              (message) => message.sequence > (messages.at(-1)?.sequence ?? 0),
            ),
          ]);
          this.fresh.set(true);
          this.loadError.set('');
        },
        error: (error: unknown) => {
          this.fresh.set(false);
          if (error instanceof HttpErrorResponse && error.status === 404) {
            this.unavailable.set(true);
          }
          this.retryAt =
            Date.now() +
            (error instanceof HttpErrorResponse && error.status === 429 ? 60000 : 15000);
          this.loadError.set(
            this.unavailable()
              ? 'A conversa não está disponível ou o acesso expirou.'
              : 'Conversa sem atualização. Confira a conexão e aguarde a próxima consulta.',
          );
        },
      });
  }

  start(): void {
    if (this.busy() || this.state.value().access || this.state.recoveryError()) {
      return;
    }
    this.error.set('');
    if (!this.state.value().pendingStart) {
      if (!this.name.trim() || this.name.length > 120 || !this.validText()) {
        this.error.set('Informe seu nome e uma mensagem de até 2.000 caracteres.');
        return;
      }
      try {
        this.state.save({
          access: null,
          pendingMessage: null,
          pendingOrder: null,
          pendingStart: {
            requestId: crypto.randomUUID(),
            name: this.name.trim(),
            text: this.text.trim(),
          },
        });
      } catch {
        this.storageError();
        return;
      }
    }
    this.busy.set(true);
    this.api
      .start(this.state.value().pendingStart!)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.busy.set(false)),
      )
      .subscribe({
        next: (result) => {
          if (!result.access) {
            this.unavailable.set(true);
            this.error.set('O acesso desta solicitação expirou. Você pode iniciar outra conversa.');
            return;
          }
          try {
            this.state.save({
              access: result.access,
              pendingStart: null,
              pendingMessage: null,
              pendingOrder: null,
            });
          } catch {
            this.storageError();
            return;
          }
          this.name = '';
          this.text = '';
          this.busy.set(false);
          this.load();
        },
        error: () =>
          this.error.set(
            'Não foi possível confirmar a solicitação. Use “Conferir solicitação” para recuperar a mesma conversa.',
          ),
      });
  }

  send(): void {
    const value = this.state.value();
    if (
      this.busy() ||
      !value.access ||
      value.pendingOrder ||
      this.unavailable() ||
      (!value.pendingMessage && (!this.fresh() || this.transcript()?.status === 'Closed'))
    ) {
      return;
    }
    this.error.set('');
    if (!value.pendingMessage) {
      if (!this.validText()) {
        this.error.set('Escreva uma mensagem de até 2.000 caracteres.');
        return;
      }
      try {
        this.state.save({
          ...value,
          pendingMessage: { requestId: crypto.randomUUID(), text: this.text.trim() },
        });
      } catch {
        this.storageError();
        return;
      }
    }
    this.busy.set(true);
    this.readRequest?.unsubscribe();
    this.api
      .send(value.access.token, this.state.value().pendingMessage!)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.busy.set(false)),
      )
      .subscribe({
        next: () => {
          try {
            this.state.save({ ...this.state.value(), pendingMessage: null });
          } catch {
            this.storageError();
            return;
          }
          this.text = '';
          this.order.update((order) => (order ? { ...order, review: null } : null));
          this.busy.set(false);
          this.fresh.set(false);
          this.load();
        },
        error: (error: unknown) => {
          const code = error instanceof HttpErrorResponse ? error.error?.code : '';
          if (['ChatClosed', 'ChatExpired', 'ChatLimitReached'].includes(code)) {
            try {
              this.state.save({ ...this.state.value(), pendingMessage: null });
            } catch {
              this.storageError();
              return;
            }
            this.error.set(
              'A mensagem não foi enviada: a conversa foi encerrada, expirou ou atingiu seu limite.',
            );
            this.fresh.set(false);
            this.busy.set(false);
            this.load();
          } else {
            this.error.set(
              'Envio sem confirmação. Use “Conferir mensagem” para repetir a mesma tentativa.',
            );
          }
        },
      });
  }

  reset(): void {
    if (
      this.busy() ||
      (!this.unavailable() && !this.state.recoveryError() && this.transcript()?.status !== 'Closed')
    ) {
      return;
    }
    try {
      this.state.clear();
    } catch {
      this.storageError();
      return;
    }
    this.readRequest?.unsubscribe();
    this.transcript.set(null);
    this.messages.set([]);
    this.order.set(null);
    this.unavailable.set(false);
    this.fresh.set(false);
    this.error.set('');
    this.loadError.set('');
    this.name = '';
    this.text = '';
    this.retryAt = 0;
    this.invalidReviewToken = null;
  }

  handoff(): void {
    const transcript = this.transcript();
    const value = this.state.value();
    if (!transcript || !value.access || !this.automatic() || this.busy() || !this.fresh()) {
      return;
    }
    this.busy.set(true);
    this.error.set('');
    this.readRequest?.unsubscribe();
    this.api
      .handoff(value.access.token, transcript.version)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.busy.set(false)),
      )
      .subscribe({
        next: (result) => {
          this.transcript.set(result);
          this.acceptOrderState(result.order ?? null);
          this.messages.update((messages) => [
            ...messages,
            ...result.messages.filter(
              (message) => message.sequence > (messages.at(-1)?.sequence ?? 0),
            ),
          ]);
          this.busy.set(false);
          this.fresh.set(false);
          this.load();
        },
        error: (error: unknown) => {
          this.error.set(apiError(error) + ' Confira o estado da conversa.');
          this.busy.set(false);
          this.fresh.set(false);
          this.load();
        },
      });
  }

  reviewOrder(): void {
    const value = this.state.value();
    const order = this.order();
    if (
      !value.access ||
      !order ||
      this.busy() ||
      !this.fresh() ||
      this.orderProcessing() ||
      value.pendingOrder ||
      value.pendingMessage ||
      order.receipt ||
      order.missingFields.length ||
      this.transcript()?.status === 'Closed'
    ) {
      return;
    }
    this.busy.set(true);
    this.error.set('');
    this.readRequest?.unsubscribe();
    this.api
      .reviewOrder(value.access.token, order.draftVersion)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.busy.set(false)),
      )
      .subscribe({
        next: (result) => {
          this.invalidReviewToken = null;
          this.acceptOrderState(result);
        },
        error: (error: unknown) => {
          this.invalidReviewToken = this.order()?.review?.reviewToken ?? null;
          this.order.update((current) => (current ? { ...current, review: null } : null));
          this.error.set(apiError(error));
          this.busy.set(false);
          this.fresh.set(false);
          this.load();
        },
      });
  }

  confirmOrder(): void {
    const value = this.state.value();
    const order = this.order();
    if (!value.access || this.busy() || this.state.recoveryError() || this.unavailable()) {
      return;
    }
    if (!value.pendingOrder) {
      if (
        !order?.review ||
        order.missingFields.length > 0 ||
        order.receipt ||
        !this.fresh() ||
        this.orderProcessing() ||
        value.pendingMessage ||
        this.transcript()?.status === 'Closed'
      ) {
        return;
      }
      try {
        this.state.save({
          ...value,
          pendingOrder: {
            requestId: crypto.randomUUID(),
            expectedDraftVersion: order.draftVersion,
            reviewToken: order.review.reviewToken,
          },
        });
      } catch {
        this.storageError();
        return;
      }
    }
    this.busy.set(true);
    this.error.set('');
    this.readRequest?.unsubscribe();
    this.api
      .confirmOrder(value.access.token, this.state.value().pendingOrder!)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.busy.set(false)),
      )
      .subscribe({
        next: (result) => {
          if (!result.receipt) {
            this.error.set('Envio sem confirmação. Confira a mesma tentativa antes de continuar.');
            return;
          }
          this.acceptOrderState(result);
          this.busy.set(false);
          this.fresh.set(false);
          this.load();
        },
        error: (error: unknown) => {
          const code = error instanceof HttpErrorResponse ? error.error?.code : '';
          if (
            (error instanceof HttpErrorResponse && error.status === 400) ||
            [
              'ChatDraftChanged',
              'ChatOrderReviewRequired',
              'OrderReviewChanged',
              'StoreNotAcceptingOrders',
              'CartProductUnavailable',
              'ChatOrderIncomplete',
              'ChatAutomationPending',
              'PublicDeliveryUnavailable',
              'InvalidPublicDeliveryAddress',
              'PublicCheckoutUnavailable',
              'ChatClosed',
              'ChatUnavailable',
            ].includes(code)
          ) {
            this.invalidReviewToken = this.state.value().pendingOrder?.reviewToken ?? null;
            try {
              this.state.save({ ...this.state.value(), pendingOrder: null });
            } catch {
              this.storageError();
              return;
            }
            this.order.update((current) => (current ? { ...current, review: null } : null));
            this.error.set('Pedido não enviado. ' + apiError(error) + ' Revise novamente.');
            this.busy.set(false);
            this.fresh.set(false);
            this.load();
          } else {
            this.error.set(
              'Envio sem confirmação. Use “Conferir envio” para recuperar a mesma tentativa.',
            );
          }
        },
      });
  }

  price(value: number): string {
    return new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' }).format(value);
  }

  private acceptOrderState(order: ChatOrderState | null): void {
    if (order?.review?.reviewToken === this.invalidReviewToken && !order.receipt) {
      order = { ...order, review: null, needsReview: true };
    }
    this.order.set(order);
    if (!order?.receipt) {
      return;
    }
    this.history.add(order.receipt);
    if (this.state.value().pendingOrder) {
      try {
        this.state.save({ ...this.state.value(), pendingOrder: null });
        this.error.set('');
      } catch {
        this.storageError();
      }
    }
  }
  private validText(): boolean {
    return !!this.text.trim() && this.text.length <= 2000;
  }
  private storageError(): void {
    this.error.set(
      'Não foi possível guardar a recuperação nesta aba. Confira o armazenamento do navegador e repita a conferência antes de iniciar outra tentativa.',
    );
  }
}
