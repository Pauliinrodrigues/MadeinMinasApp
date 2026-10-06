import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { finalize, Subscription, timeout } from 'rxjs';
import { apiError } from '../../core/api-error';
import { unitLabel } from '../../core/services/ingredient-api.service';
import {
  StockApi,
  StockReplenishment,
  StockReplenishmentFilters,
  StockReplenishmentStatus,
} from '../../core/services/stock-api.service';

@Component({
  selector: 'app-replenishment',
  imports: [FormsModule, RouterLink],
  templateUrl: './replenishment.page.html',
  styleUrl: './replenishment.page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReplenishmentPage {
  private readonly api = inject(StockApi);
  private readonly destroyRef = inject(DestroyRef);
  private request?: Subscription;
  readonly data = signal<StockReplenishment | null>(null);
  readonly loading = signal(false);
  readonly error = signal('');
  readonly updatedAt = signal<Date | null>(null);
  readonly appliedFilters = signal<StockReplenishmentFilters>({
    search: '',
    status: 'Attention',
    includeInactive: false,
  });
  readonly decimal = new Intl.NumberFormat('pt-BR', { maximumFractionDigits: 3 });
  readonly time = new Intl.DateTimeFormat('pt-BR', { timeStyle: 'short' });
  readonly unitLabel = unitLabel;
  search = '';
  status: StockReplenishmentStatus = 'Attention';
  includeInactive = false;
  page = 1;

  constructor() {
    this.refresh();
  }

  filtersChanged(): boolean {
    const applied = this.appliedFilters();
    return (
      this.search.trim() !== applied.search ||
      this.status !== applied.status ||
      this.includeInactive !== applied.includeInactive
    );
  }

  apply(): void {
    this.appliedFilters.set({
      search: this.search.trim(),
      status: this.status,
      includeInactive: this.includeInactive,
    });
    this.changePage(1);
  }

  selectQueue(status: StockReplenishmentStatus): void {
    const applied = this.appliedFilters();
    this.search = applied.search;
    this.includeInactive = applied.includeInactive;
    this.status = status;
    this.apply();
  }

  changePage(page: number): void {
    this.request?.unsubscribe();
    this.page = page;
    this.data.set(null);
    this.refresh();
  }

  refresh(): void {
    if (this.loading()) {
      return;
    }
    this.error.set('');
    this.loading.set(true);
    this.request = this.api
      .replenishment(this.page, this.appliedFilters())
      .pipe(
        timeout(15000),
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.loading.set(false)),
      )
      .subscribe({
        next: (data) => {
          this.data.set(data);
          this.updatedAt.set(new Date());
        },
        error: (error) => this.error.set(apiError(error)),
      });
  }
}
