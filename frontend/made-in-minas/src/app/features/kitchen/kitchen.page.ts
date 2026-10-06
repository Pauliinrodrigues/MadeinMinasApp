import { DatePipe } from '@angular/common';
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
import { AuthSession } from '../../core/auth/auth-session.service';
import { finalize, interval } from 'rxjs';
import { apiError } from '../../core/api-error';
import {
  KitchenApi,
  KitchenBoard,
  KitchenColumn,
  KitchenOrder,
  KitchenPages,
  KitchenStatusInput,
} from '../../core/services/kitchen-api.service';
import { orderStatusLabel } from '../../core/services/order-api.service';

@Component({
  selector: 'app-kitchen',
  imports: [DatePipe, RouterLink],
  templateUrl: './kitchen.page.html',
  styleUrl: './kitchen.page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class KitchenPage {
  readonly session = inject(AuthSession);
  private readonly api = inject(KitchenApi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly tick = signal(performance.now());
  private readonly receivedAt = signal<number | null>(null);
  private serverTime = 0;
  private pages: KitchenPages = { confirmedPage: 1, preparingPage: 1, readyPage: 1 };
  readonly board = signal<KitchenBoard | null>(null);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly needsRefresh = signal(true);
  readonly error = signal('');
  readonly notice = signal('');
  readonly queueNotice = signal('');
  readonly selectedStage = signal('');
  readonly visibleColumns = computed(
    () =>
      this.board()?.columns.filter(
        (column) => !this.selectedStage() || column.status === this.selectedStage(),
      ) ?? [],
  );
  private confirmedCount: number | null = null;
  readonly pending = signal<{ order: KitchenOrder; input: KitchenStatusInput } | null>(null);
  readonly statusLabel = orderStatusLabel;
  readonly stale = computed(
    () => this.receivedAt() === null || this.tick() - this.receivedAt()! >= 30000,
  );

  constructor() {
    interval(1000)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.tick.set(performance.now()));
    interval(10000)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        if (!this.pending()) {
          this.load();
        }
      });
    this.load();
  }
  blocked(): boolean {
    return (
      this.loading() ||
      this.saving() ||
      this.needsRefresh() ||
      this.stale() ||
      (this.receivedAt() !== null && performance.now() - this.receivedAt()! >= 30000)
    );
  }
  load(): void {
    if (this.loading() || this.saving()) {
      return;
    }
    this.pending.set(null);
    this.loading.set(true);
    this.api
      .board(this.pages)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.loading.set(false)),
      )
      .subscribe({
        next: (board) => {
          const confirmed =
            board.columns.find((column) => column.status === 'Confirmed')?.totalCount ?? 0;
          if (this.confirmedCount !== null && confirmed > this.confirmedCount) {
            this.queueNotice.set(
              'A fila de confirmados aumentou. Confira os pedidos aguardando preparo.',
            );
          }
          this.confirmedCount = confirmed;
          this.serverTime = Date.parse(board.serverTime);
          this.receivedAt.set(performance.now());
          this.tick.set(performance.now());
          this.board.set(board);
          this.pages = {
            confirmedPage: board.columns[0].page,
            preparingPage: board.columns[1].page,
            readyPage: board.columns[2].page,
          };
          this.error.set('');
          this.needsRefresh.set(false);
        },
        error: (error) => {
          this.needsRefresh.set(true);
          this.error.set(
            apiError(error) +
              ' Painel sem atualização. Confira a conexão e atualize antes de alterar pedidos.',
          );
        },
      });
  }
  turnPage(column: KitchenColumn, page: number): void {
    if (this.blocked() || this.pending()) {
      return;
    }
    const key =
      column.status === 'Confirmed'
        ? 'confirmedPage'
        : column.status === 'InPreparation'
          ? 'preparingPage'
          : 'readyPage';
    this.pages = { ...this.pages, [key]: page };
    this.load();
  }
  choose(order: KitchenOrder): void {
    if (this.blocked() || this.pending() || order.status === 'Ready') {
      return;
    }
    this.pending.set({
      order,
      input: {
        status: order.status === 'Confirmed' ? 'InPreparation' : 'Ready',
        expectedVersion: order.version,
      },
    });
    this.notice.set('');
  }
  confirm(): void {
    const pending = this.pending();
    if (!pending || this.blocked()) {
      return;
    }
    this.saving.set(true);
    this.api
      .status(pending.order.id, pending.input)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.saving.set(false)),
      )
      .subscribe({
        next: (order) => {
          this.notice.set('Pedido #' + order.number + ': ' + this.statusLabel(order.status) + '.');
          this.pending.set(null);
          this.saving.set(false);
          this.needsRefresh.set(true);
          this.load();
        },
        error: (error) => {
          this.pending.set(null);
          this.needsRefresh.set(true);
          this.error.set(
            apiError(error) +
              ' Atualize o painel e confira o status antes de repetir qualquer ação.',
          );
        },
      });
  }
  minutes(from: string | null, until: string | null = null): string {
    if (!from || this.receivedAt() === null) {
      return 'indisponível';
    }
    const end = until
      ? Date.parse(until)
      : this.serverTime + Math.max(0, this.tick() - this.receivedAt()!);
    return Math.max(0, Math.floor((end - Date.parse(from)) / 60000)) + ' min';
  }
}
