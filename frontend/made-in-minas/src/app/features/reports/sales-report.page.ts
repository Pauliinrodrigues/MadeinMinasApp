import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { finalize } from 'rxjs';
import { apiError } from '../../core/api-error';
import { paymentMethodLabel } from '../../core/services/payment-api.service';
import { formatProductPrice } from '../../core/services/product-api.service';
import { ReportsApi, SalesReport } from '../../core/services/reports-api.service';

@Component({
  selector: 'app-sales-report',
  imports: [FormsModule],
  templateUrl: './sales-report.page.html',
  styleUrl: './sales-report.page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SalesReportPage {
  private readonly api = inject(ReportsApi);
  private readonly destroyRef = inject(DestroyRef);
  readonly data = signal<SalesReport | null>(null);
  readonly loading = signal(false);
  readonly error = signal('');
  readonly edited = signal(false);
  readonly price = formatProductPrice;
  readonly methodLabel = paymentMethodLabel;
  readonly number = new Intl.NumberFormat('pt-BR');
  readonly timestamp = new Intl.DateTimeFormat('pt-BR', {
    timeZone: 'America/Sao_Paulo',
    dateStyle: 'short',
    timeStyle: 'medium',
  });
  startDate = '';
  endDate = '';

  constructor() {
    this.load(true);
  }

  formatDate(value: string): string {
    return value.split('-').reverse().join('/');
  }

  formatTimestamp(value: string): string {
    return this.timestamp.format(new Date(value));
  }

  filtersChanged(): void {
    this.data.set(null);
    this.error.set('');
    this.edited.set(true);
  }

  load(useDefault = false): void {
    if (this.loading()) {
      return;
    }
    this.data.set(null);
    this.error.set('');
    if (!useDefault) {
      const dates = [this.startDate, this.endDate].map((value) => {
        if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) {
          return NaN;
        }
        const date = new Date(value + 'T00:00:00Z');
        return Number.isFinite(date.getTime()) && date.toISOString().slice(0, 10) === value
          ? date.getTime()
          : NaN;
      });
      const length = (dates[1] - dates[0]) / 86400000 + 1;
      if (!Number.isFinite(length) || length < 1 || length > 90 || this.endDate === '9999-12-31') {
        this.error.set(
          'Informe duas datas válidas, em ordem, com no máximo 90 dias incluindo início e fim.',
        );
        return;
      }
    }
    this.loading.set(true);
    this.edited.set(false);
    this.api
      .sales(useDefault ? undefined : this.startDate, useDefault ? undefined : this.endDate)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.loading.set(false)),
      )
      .subscribe({
        next: (report) => {
          this.startDate = report.startDate;
          this.endDate = report.endDate;
          this.data.set(report);
        },
        error: (error) => this.error.set(apiError(error)),
      });
  }
}
