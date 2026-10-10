import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { finalize } from 'rxjs';
import { apiError } from '../../core/api-error';
import {
  StoreAttendanceApi,
  StoreAttendanceData,
  StoreAttendanceResponse,
  storeDayNames,
  storePaymentLabels,
} from '../../core/services/store-attendance-api.service';

@Component({
  selector: 'app-store-attendance',
  imports: [FormsModule],
  templateUrl: './store-attendance.page.html',
  styles: `
    .hours-grid {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 16px;
      margin-block: 20px;
    }
    fieldset {
      min-width: 0;
    }
    .hours-row {
      border: 1px solid #dfd3c3;
      border-radius: 12px;
      padding: 16px;
    }
    .hours-row label {
      display: block;
      margin-block: 8px;
    }
    input[type='time'] {
      max-width: 160px;
    }
    .choices {
      display: flex;
      flex-wrap: wrap;
      gap: 16px;
      margin-block: 12px;
    }
    @media (max-width: 700px) {
      .hours-grid {
        grid-template-columns: 1fr;
      }
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class StoreAttendancePage {
  private readonly api = inject(StoreAttendanceApi);
  private readonly destroyRef = inject(DestroyRef);
  readonly current = signal<StoreAttendanceResponse | null>(null);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly error = signal('');
  readonly notice = signal('');
  readonly needsRefresh = signal(false);
  readonly uncertain = signal(false);
  readonly reviewed = signal<StoreAttendanceData | null>(null);
  readonly days = storeDayNames;
  draft: StoreAttendanceData | null = null;
  card = false;
  cash = false;
  pix = false;
  constructor() {
    this.load();
  }
  load(): void {
    if (this.loading() || this.saving()) {
      return;
    }
    this.loading.set(true);
    this.error.set('');
    this.reviewed.set(null);
    this.api
      .get()
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.loading.set(false)),
      )
      .subscribe({
        next: (data) => {
          this.current.set(data);
          this.draft = structuredClone(data.settings);
          this.card = data.settings.paymentMethods.includes('Card');
          this.cash = data.settings.paymentMethods.includes('Cash');
          this.pix = data.settings.paymentMethods.includes('Pix');
          this.needsRefresh.set(false);
          this.uncertain.set(false);
        },
        error: (error) => this.error.set(apiError(error)),
      });
  }
  review(): void {
    if (!this.draft || this.saving() || this.loading() || this.needsRefresh()) {
      return;
    }
    const methods = [
      this.card ? 'Card' : '',
      this.cash ? 'Cash' : '',
      this.pix ? 'Pix' : '',
    ].filter(Boolean);
    if (
      !methods.length ||
      !this.draft.deliveryEstimateMessage.trim() ||
      this.draft.hours.some(
        (hours) =>
          !hours.isClosed &&
          (!hours.opensAt ||
            !hours.closesAt ||
            (hours.closesAt !== '00:00' && hours.closesAt <= hours.opensAt)),
      )
    ) {
      this.error.set(
        'Confira os horários, selecione ao menos uma forma de pagamento e informe a orientação de prazo. Use 00:00 para encerrar à meia-noite.',
      );
      return;
    }
    this.error.set('');
    this.notice.set('');
    this.reviewed.set({ ...structuredClone(this.draft), paymentMethods: methods });
  }
  save(): void {
    const settings = this.reviewed();
    const current = this.current();
    if (!settings || !current || this.saving() || this.needsRefresh()) {
      return;
    }
    this.saving.set(true);
    this.error.set('');
    this.api
      .save(current.revision, settings)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.saving.set(false)),
      )
      .subscribe({
        next: (data) => {
          this.current.set(data);
          this.draft = structuredClone(data.settings);
          this.reviewed.set(null);
          this.uncertain.set(false);
          this.notice.set(
            'Configurações de atendimento salvas. As novas condições já valem para o site.',
          );
        },
        error: (error) => {
          if (error instanceof HttpErrorResponse && [400, 403, 409].includes(error.status)) {
            this.needsRefresh.set(true);
            this.reviewed.set(null);
            this.error.set(apiError(error) + ' Recarregue antes de uma nova alteração.');
          } else {
            this.uncertain.set(true);
            this.error.set(
              'Não foi possível confirmar o salvamento. Repita a mesma alteração ou recarregue para conferir o resultado.',
            );
          }
        },
      });
  }
  paymentNames(methods: string[]): string {
    return methods.map((method) => storePaymentLabels[method]).join(', ');
  }
}
