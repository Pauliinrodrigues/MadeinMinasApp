import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { apiError } from '../../core/api-error';
import { AuthSession } from '../../core/auth/auth-session.service';
import { DailyDashboard, DashboardApi } from '../../core/services/dashboard-api.service';
import { formatProductPrice } from '../../core/services/product-api.service';

@Component({
  selector: 'app-dashboard',
  imports: [RouterLink],
  templateUrl: './dashboard.page.html',
  styleUrl: './dashboard.page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DashboardPage {
  private readonly api = inject(DashboardApi);
  private readonly destroyRef = inject(DestroyRef);
  readonly session = inject(AuthSession);
  readonly data = signal<DailyDashboard | null>(null);
  readonly loading = signal(false);
  readonly error = signal('');
  readonly price = formatProductPrice;
  readonly number = new Intl.NumberFormat('pt-BR', { maximumFractionDigits: 2 });
  readonly timestamp = new Intl.DateTimeFormat('pt-BR', {
    timeZone: 'America/Sao_Paulo',
    dateStyle: 'short',
    timeStyle: 'medium',
  });
  readonly statusLabels: Record<string, string> = {
    New: 'Novos',
    Confirmed: 'Confirmados',
    InPreparation: 'Em preparação',
    Ready: 'Prontos',
    AwaitingDelivery: 'Aguardando entrega',
    OutForDelivery: 'Saiu para entrega',
    Delivered: 'Entregues a finalizar',
  };

  constructor() {
    this.load();
  }

  formatDate(date: string): string {
    return date.split('-').reverse().join('/');
  }

  formatTimestamp(value: string): string {
    return this.timestamp.format(new Date(value));
  }

  load(): void {
    if (this.loading()) {
      return;
    }
    this.loading.set(true);
    this.data.set(null);
    this.error.set('');
    this.api
      .today()
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.loading.set(false)),
      )
      .subscribe({
        next: (data) => this.data.set(data),
        error: (error) => this.error.set(apiError(error)),
      });
  }
}
