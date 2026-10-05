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
import { ActivatedRoute, RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { finalize, fromEvent, interval, Subscription } from 'rxjs';
import { apiError } from '../../core/api-error';
import { AuthSession } from '../../core/auth/auth-session.service';
import {
  ChatMessage,
  ChatMessageInput,
  StaffChat,
  StaffChatApi,
  chatStatusLabel,
} from '../../core/services/chat-api.service';
import { ChatMessagesComponent } from './chat-messages.component';

@Component({
  selector: 'app-staff-chat',
  imports: [FormsModule, RouterLink, ChatMessagesComponent],
  templateUrl: './staff-chat.page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    .actions {
      display: flex;
      gap: 12px;
      flex-wrap: wrap;
      margin: 16px 0;
    }
    .pending {
      white-space: pre-wrap;
      overflow-wrap: anywhere;
    }
  `,
})
export class StaffChatPage {
  readonly session = inject(AuthSession);
  private readonly api = inject(StaffChatApi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly id = inject(ActivatedRoute).snapshot.paramMap.get('id')!;
  private readonly storageKey =
    'made-in-minas.staff-chat.' + this.session.user()!.id + '.' + this.id;
  readonly data = signal<StaffChat | null>(null);
  readonly messages = signal<ChatMessage[]>([]);
  readonly loading = signal(false);
  readonly busy = signal(false);
  readonly fresh = signal(false);
  readonly error = signal('');
  readonly loadError = signal('');
  readonly recoveryError = signal(false);
  readonly pending = signal<ChatMessageInput | null>(null);
  readonly statusLabel = chatStatusLabel;
  readonly owner = computed(
    () => this.data()?.conversation.assignedToId === this.session.user()?.id,
  );
  readonly canClaim = computed(
    () =>
      this.data()?.conversation.status === 'Waiting' ||
      (this.data()?.conversation.status === 'InService' &&
        !this.owner() &&
        this.session.user()?.role === 'Administrator'),
  );
  private readRequest?: Subscription;
  text = '';
  closeRequested = false;
  recoveryAcknowledged = false;

  constructor() {
    try {
      const stored = sessionStorage.getItem(this.storageKey);
      if (stored) {
        if (stored.length > 12000) {
          throw new Error('Invalid message');
        }
        const pending = JSON.parse(stored);
        if (
          typeof pending.requestId !== 'string' ||
          !/^[0-9a-f-]{36}$/i.test(pending.requestId) ||
          typeof pending.text !== 'string' ||
          !pending.text.trim() ||
          pending.text.length > 2000
        ) {
          throw new Error('Invalid message');
        }
        this.pending.set(pending);
      }
    } catch {
      this.recoveryError.set(true);
    }
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
    if (
      this.loading() ||
      this.busy() ||
      document.hidden ||
      (this.data()?.conversation.status === 'Closed' &&
        !this.data()?.transcript.hasMore &&
        this.fresh())
    ) {
      return;
    }
    this.loading.set(true);
    this.readRequest = this.api
      .read(this.id, this.messages().at(-1)?.sequence ?? 0)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => {
          this.loading.set(false);
          if (
            !this.destroyRef.destroyed &&
            this.data()?.transcript.hasMore &&
            this.fresh() &&
            !document.hidden
          ) {
            queueMicrotask(() => this.load());
          }
        }),
      )
      .subscribe({
        next: (data) => {
          this.data.set(data);
          this.messages.update((messages) => [
            ...messages,
            ...data.transcript.messages.filter(
              (message) => message.sequence > (messages.at(-1)?.sequence ?? 0),
            ),
          ]);
          this.fresh.set(true);
          this.loadError.set('');
        },
        error: (error) => {
          this.fresh.set(false);
          this.loadError.set(apiError(error) + ' Conversa sem atualização.');
        },
      });
  }

  act(action: 'claim' | 'close'): void {
    const data = this.data();
    if (
      !data ||
      this.busy() ||
      this.loading() ||
      !this.fresh() ||
      this.pending() ||
      this.recoveryError() ||
      (action === 'close' && !this.closeRequested)
    ) {
      return;
    }
    this.busy.set(true);
    this.error.set('');
    this.api
      .act(this.id, action, data.conversation.version)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.busy.set(false)),
      )
      .subscribe({
        next: () => {
          this.closeRequested = false;
          this.busy.set(false);
          this.fresh.set(false);
          this.load();
        },
        error: (error) => {
          this.error.set(apiError(error) + ' Confira o histórico antes de repetir a ação.');
          this.closeRequested = false;
          this.busy.set(false);
          this.fresh.set(false);
          this.load();
        },
      });
  }

  send(): void {
    if (
      this.busy() ||
      this.recoveryError() ||
      (!this.pending() &&
        (!this.owner() || !this.fresh() || this.data()?.conversation.status !== 'InService'))
    ) {
      return;
    }
    this.error.set('');
    if (!this.pending()) {
      if (!this.text.trim() || this.text.length > 2000) {
        this.error.set('Escreva até 2.000 caracteres.');
        return;
      }
      const input = { requestId: crypto.randomUUID(), text: this.text.trim() };
      try {
        sessionStorage.setItem(this.storageKey, JSON.stringify(input));
        this.pending.set(input);
      } catch {
        this.error.set('Não foi possível guardar a tentativa. Nenhuma mensagem foi enviada.');
        return;
      }
    }
    this.busy.set(true);
    this.api
      .send(this.id, this.pending()!)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.busy.set(false)),
      )
      .subscribe({
        next: () => {
          if (!this.clearPending()) {
            return;
          }
          this.text = '';
          this.busy.set(false);
          this.fresh.set(false);
          this.load();
        },
        error: (error: unknown) => {
          const code = error instanceof HttpErrorResponse ? error.error?.code : '';
          if (
            ['ChatClosed', 'ChatExpired', 'ChatLimitReached', 'ChatAssignmentRequired'].includes(
              code,
            )
          ) {
            if (!this.clearPending()) {
              return;
            }
            this.error.set('Mensagem não enviada. ' + apiError(error));
            this.busy.set(false);
            this.fresh.set(false);
            this.load();
          } else {
            this.error.set(
              'Envio sem confirmação. Confira a mensagem pendente usando a mesma tentativa.',
            );
          }
        },
      });
  }

  clearRecovery(): void {
    if (!this.recoveryAcknowledged || !this.fresh() || this.busy()) {
      return;
    }
    if (this.clearPending()) {
      this.recoveryError.set(false);
    }
  }
  private clearPending(): boolean {
    try {
      sessionStorage.removeItem(this.storageKey);
      this.pending.set(null);
      return true;
    } catch {
      this.error.set(
        'Não foi possível atualizar a recuperação. Repita a conferência antes de escrever outra mensagem.',
      );
      return false;
    }
  }
}
