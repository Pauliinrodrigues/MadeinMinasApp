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
  ChatTranscript,
  PublicChatApi,
  chatStatusLabel,
} from '../../core/services/chat-api.service';
import { PublicChatState } from '../../core/services/public-chat-state.service';
import { ChatMessagesComponent } from './chat-messages.component';
import { PublicCheckoutState } from '../../core/services/public-checkout-state.service';
import { PublicOrderHistory } from '../../core/services/public-order-history.service';

@Component({
  selector: 'app-public-chat',
  host: { class: 'ion-page' },
  imports: [FormsModule, RouterLink, IonContent, ChatMessagesComponent],
  templateUrl: './public-chat.page.html',
  styleUrl: './public-chat.page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PublicChatPage {
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
  readonly loading = signal(false);
  readonly busy = signal(false);
  readonly unavailable = signal(false);
  readonly fresh = signal(false);
  readonly error = signal('');
  readonly loadError = signal('');
  readonly statusLabel = chatStatusLabel;
  private readRequest?: Subscription;
  private retryAt = 0;
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
            this.state.save({ access: result.access, pendingStart: null, pendingMessage: null });
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
    this.unavailable.set(false);
    this.fresh.set(false);
    this.error.set('');
    this.loadError.set('');
    this.name = '';
    this.text = '';
    this.retryAt = 0;
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
