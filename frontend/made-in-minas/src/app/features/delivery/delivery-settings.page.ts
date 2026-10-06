import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  Injector,
  afterNextRender,
  computed,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { finalize } from 'rxjs';
import { apiError } from '../../core/api-error';
import { brazilianStates } from '../../core/services/customer-api.service';
import {
  DeliveryArea,
  DeliverySettings,
  DeliverySettingsApi,
  SaveDeliverySettings,
} from '../../core/services/delivery-settings-api.service';
import { formatProductPrice } from '../../core/services/product-api.service';

@Component({
  selector: 'app-delivery-settings',
  imports: [FormsModule, DatePipe],
  templateUrl: './delivery-settings.page.html',
  styleUrl: './delivery-settings.page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DeliverySettingsPage {
  private readonly api = inject(DeliverySettingsApi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
  readonly data = signal<DeliverySettings | null>(null);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly error = signal('');
  readonly notice = signal('');
  readonly needsRefresh = signal(false);
  readonly uncertain = signal(false);
  readonly pending = signal<SaveDeliverySettings | null>(null);
  readonly reviewedArea = signal<DeliveryArea | null>(null);
  readonly editing = signal(false);
  readonly search = signal('');
  readonly active = signal('');
  readonly price = formatProductPrice;
  readonly states = brazilianStates;
  readonly activeCount = computed(
    () => this.data()?.areas.filter((area) => area.isActive).length ?? 0,
  );
  readonly areas = computed(() => {
    const search = this.search().trim().normalize().toLocaleLowerCase('pt-BR');
    return (this.data()?.areas ?? [])
      .filter(
        (area) =>
          `${area.neighborhood} ${area.city} ${area.state}`
            .normalize()
            .toLocaleLowerCase('pt-BR')
            .includes(search) &&
          (!this.active() || String(area.isActive) === this.active()),
      )
      .sort(
        (a, b) =>
          a.city.localeCompare(b.city, 'pt-BR') ||
          a.neighborhood.localeCompare(b.neighborhood, 'pt-BR'),
      );
  });
  id = '';
  neighborhood = '';
  city = '';
  state = '';
  fee: number | null = null;
  isActive = true;
  attempted = false;

  constructor() {
    this.load();
  }

  load(): void {
    if (this.loading() || this.saving()) {
      return;
    }
    this.loading.set(true);
    this.error.set('');
    this.notice.set('');
    this.api
      .get()
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.loading.set(false)),
      )
      .subscribe({
        next: (data) => {
          this.data.set(data);
          this.editing.set(false);
          this.pending.set(null);
          this.reviewedArea.set(null);
          this.uncertain.set(false);
          this.needsRefresh.set(false);
        },
        error: (error) => {
          this.error.set(apiError(error));
          this.needsRefresh.set(true);
        },
      });
  }

  edit(area?: DeliveryArea): void {
    if (!this.data() || this.loading() || this.saving() || this.pending() || this.needsRefresh()) {
      return;
    }
    if (!area && this.data()!.areas.length >= 500) {
      return;
    }
    this.id = area?.id ?? crypto.randomUUID();
    this.neighborhood = area?.neighborhood ?? '';
    this.city = area?.city ?? '';
    this.state = area?.state ?? '';
    this.fee = area?.fee ?? null;
    this.isActive = area?.isActive ?? true;
    this.attempted = false;
    this.error.set('');
    this.notice.set('');
    this.editing.set(true);
    afterNextRender(() => document.getElementById('area-neighborhood')?.focus(), {
      injector: this.injector,
    });
  }

  validation(): string {
    if (!this.neighborhood.trim() || !this.city.trim()) {
      return 'Informe o bairro e a cidade.';
    }
    if (!this.states.includes(this.state)) {
      return 'Selecione uma UF válida.';
    }
    if (
      this.fee === null ||
      !Number.isFinite(this.fee) ||
      this.fee < 0 ||
      this.fee > 9999.99 ||
      Math.abs(this.fee * 100 - Math.round(this.fee * 100)) > 0.000001
    ) {
      return 'Informe uma taxa de 0 a 9999,99, com até duas casas decimais. Use zero somente para entrega gratuita.';
    }
    const normalized = (value: string) => value.trim().normalize().toLocaleUpperCase('pt-BR');
    if (
      this.data()?.areas.some(
        (area) =>
          area.id !== this.id &&
          area.state === this.state &&
          normalized(area.city) === normalized(this.city) &&
          normalized(area.neighborhood) === normalized(this.neighborhood),
      )
    ) {
      return 'Já existe uma região com esse bairro, cidade e UF. Confira também as pausadas.';
    }
    return '';
  }

  review(): void {
    this.attempted = true;
    if (
      this.validation() ||
      this.saving() ||
      this.loading() ||
      this.needsRefresh() ||
      this.pending()
    ) {
      return;
    }
    this.prepare({
      id: this.id,
      neighborhood: this.neighborhood.trim(),
      city: this.city.trim(),
      state: this.state,
      fee: this.fee!,
      isActive: this.isActive,
    });
  }

  reviewStatus(area: DeliveryArea): void {
    if (
      this.editing() ||
      this.pending() ||
      this.saving() ||
      this.loading() ||
      this.needsRefresh()
    ) {
      return;
    }
    this.error.set('');
    this.notice.set('');
    this.prepare({ ...area, isActive: !area.isActive });
  }

  private prepare(area: DeliveryArea): void {
    const data = this.data();
    if (!data) {
      return;
    }
    const areas = data.areas.filter((value) => value.id !== area.id).map((value) => ({ ...value }));
    areas.push(area);
    this.pending.set({ expectedRevision: data.revision, areas });
    this.reviewedArea.set(area);
    afterNextRender(() => document.getElementById('confirm-area-save')?.focus(), {
      injector: this.injector,
    });
  }

  cancelReview(): void {
    if (this.saving() || this.uncertain()) {
      return;
    }
    this.pending.set(null);
    this.reviewedArea.set(null);
  }

  save(): void {
    const input = this.pending();
    if (!input || this.saving() || this.loading() || this.needsRefresh()) {
      return;
    }
    this.saving.set(true);
    this.error.set('');
    this.api
      .save(input)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.saving.set(false)),
      )
      .subscribe({
        next: (data) => {
          this.data.set(data);
          this.editing.set(false);
          this.pending.set(null);
          this.reviewedArea.set(null);
          this.uncertain.set(false);
          this.notice.set(
            'Regiões de entrega atualizadas. As novas condições já valem para o site.',
          );
        },
        error: (error) => {
          if (error instanceof HttpErrorResponse && [400, 403, 409].includes(error.status)) {
            this.pending.set(null);
            this.reviewedArea.set(null);
            this.uncertain.set(false);
            this.needsRefresh.set(true);
            this.error.set(apiError(error) + ' Recarregue a lista antes de uma nova alteração.');
          } else {
            this.uncertain.set(true);
            this.error.set(
              'Não foi possível confirmar o salvamento. Repita a mesma alteração ou recarregue para conferir o que foi salvo.',
            );
          }
        },
      });
  }
}
