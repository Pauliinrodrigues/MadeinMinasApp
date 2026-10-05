import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { finalize, interval } from 'rxjs';
import { apiError } from '../../core/api-error';
import { ChatPage, StaffChatApi, chatStatusLabel } from '../../core/services/chat-api.service';

@Component({
  selector: 'app-chat-list',
  imports: [DatePipe, FormsModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<section class="panel">
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
          <li>
            <div>
              <a [routerLink]="['/equipe/atendimentos', chat.id]">{{ chat.visitorName }}</a>
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
  </section>`,
  styles: `
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
