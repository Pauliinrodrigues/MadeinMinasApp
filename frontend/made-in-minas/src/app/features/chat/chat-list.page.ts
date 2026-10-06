import { DatePipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { NavigationEnd, Router, RouterLink, RouterOutlet } from '@angular/router';
import { filter, finalize, fromEvent, interval, map } from 'rxjs';
import { apiError } from '../../core/api-error';
import { ChatPage, StaffChatApi, chatStatusLabel } from '../../core/services/chat-api.service';
import { ChatReadState } from '../../core/services/chat-read-state.service';

@Component({
  selector: 'app-chat-list',
  imports: [DatePipe, FormsModule, RouterLink, RouterOutlet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<div class="chat-workspace" [class.has-selection]="selectedId()">
    <section class="panel queue" aria-label="Fila de atendimento">
      <p class="eyebrow">ATENDIMENTO</p>
      <h1>Conversas do site</h1>
      <p>Abra uma solicitação e assuma a conversa antes de responder.</p>
      <div class="toolbar">
        <label
          >Status<select [(ngModel)]="status" (ngModelChange)="load(1)" [disabled]="loading()">
            <option value="Waiting">Aguardando atendimento</option>
            <option value="InService">Em atendimento</option>
            <option value="Closed">Encerrados</option>
          </select></label
        >
        <button (click)="load()" [disabled]="loading()">
          {{ loading() ? 'Atualizando…' : 'Atualizar fila' }}
        </button>
      </div>
      @if (error()) {
        <p role="alert">{{ error() }}</p>
      }
      @if (result(); as data) {
        @if (!data.items.length) {
          <p>Nenhuma conversa neste status.</p>
        }
        <ul class="conversations">
          @for (chat of data.items; track chat.id) {
            <li [class.selected]="selectedId() === chat.id">
              <div>
                <a
                  [routerLink]="['/equipe/atendimentos', chat.id]"
                  [attr.aria-current]="selectedId() === chat.id ? 'page' : null"
                  >{{ chat.visitorName }}</a
                >
                @if (readState.hasUpdates(chat.id, chat.version)) {
                  <span class="badge">Nova atividade</span>
                }
                <p>{{ statusLabel(chat.status) }} · {{ chat.assignedToName ?? 'Sem atendente' }}</p>
              </div>
              <time [attr.datetime]="chat.createdAt"
                >Solicitado em {{ chat.createdAt | date: 'dd/MM HH:mm' : '-0300' }}</time
              >
            </li>
          }
        </ul>
        <div class="toolbar">
          <button (click)="load(data.page - 1)" [disabled]="loading() || data.page <= 1">
            Anterior</button
          ><span>Página {{ data.page }} · {{ data.totalCount }} conversas</span
          ><button
            (click)="load(data.page + 1)"
            [disabled]="loading() || data.page * data.pageSize >= data.totalCount"
          >
            Próxima
          </button>
        </div>
      }
    </section>
    <div class="conversation-pane">
      <router-outlet />
      @if (!selectedId()) {
        <section class="panel empty-conversation">
          <h2>Selecione uma conversa</h2>
          <p>A fila continua atualizando enquanto você atende.</p>
        </section>
      }
    </div>
  </div>`,
  styles: `
    .chat-workspace {
      display: grid;
      grid-template-columns: minmax(250px, 0.7fr) minmax(0, 1.3fr);
      gap: 20px;
      align-items: start;
    }
    .queue,
    .conversation-pane {
      min-width: 0;
    }
    .queue {
      margin: 0;
    }
    h1 {
      font-size: 26px;
    }
    .selected {
      border-left: 4px solid #722f27;
      padding-left: 12px;
      background: #faf5ec;
    }
    a {
      display: inline-block;
      padding: 10px 0;
    }
    @media (max-width: 1050px) {
      .chat-workspace {
        display: block;
      }
      .has-selection .queue,
      .empty-conversation {
        display: none;
      }
    }
    .toolbar {
      display: flex;
      flex-wrap: wrap;
      gap: 16px;
      align-items: end;
      margin: 16px 0;
    }
    .conversations {
      padding: 0;
      list-style: none;
    }
    li {
      padding: 16px 0;
      border-bottom: 1px solid #dfd3c3;
      display: flex;
      justify-content: space-between;
      flex-wrap: wrap;
      gap: 12px;
      overflow-wrap: anywhere;
    }
    time {
      font-size: 13px;
    }
    p {
      margin: 6px 0;
    }
  `,
})
export class ChatListPage {
  private readonly router = inject(Router);
  private readonly url = toSignal(
    this.router.events.pipe(
      filter((event) => event instanceof NavigationEnd),
      map(() => this.router.url),
    ),
    { initialValue: this.router.url },
  );
  readonly selectedId = computed(
    () =>
      this.url()
        .split('?')[0]
        .match(/\/atendimentos\/([^/]+)/)?.[1] ?? null,
  );
  readonly readState = inject(ChatReadState);
  private readonly api = inject(StaffChatApi);
  private readonly destroyRef = inject(DestroyRef);
  readonly result = signal<ChatPage | null>(null);
  readonly loading = signal(false);
  readonly error = signal('');
  readonly statusLabel = chatStatusLabel;
  status = 'Waiting';
  private page = 1;
  constructor() {
    this.load();
    interval(10000)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        if (!document.hidden) {
          this.load();
        }
      });
    fromEvent(document, 'visibilitychange')
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        if (!document.hidden) {
          this.load();
        }
      });
  }
  load(page = this.page): void {
    if (this.loading()) {
      return;
    }
    this.page = page;
    this.loading.set(true);
    this.error.set('');
    this.api
      .list(this.status, page)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.loading.set(false)),
      )
      .subscribe({
        next: (result) => {
          this.result.set(result);
          this.page = result.page;
        },
        error: (error) => {
          this.result.set(null);
          this.error.set(apiError(error));
        },
      });
  }
}
