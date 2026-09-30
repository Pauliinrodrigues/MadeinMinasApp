import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { apiError } from '../../core/api-error';
import {
  Ingredient,
  IngredientApi,
  IngredientInput,
  IngredientUnit,
  ingredientUnits,
  unitLabel,
} from '../../core/services/ingredient-api.service';

@Component({
  selector: 'app-ingredient-form',
  imports: [FormsModule, RouterLink],
  templateUrl: './ingredient-form.page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class IngredientFormPage {
  private readonly api = inject(IngredientApi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  readonly id = this.route.snapshot.paramMap.get('id');
  readonly loading = signal(false);
  readonly ready = signal(!this.id);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly notice = signal('');
  readonly units = ingredientUnits;
  readonly unitLabel = unitLabel;
  name = '';
  unit: IngredientUnit = 'kg';
  unitCost = '';
  minimumStock = '0';
  supplier = '';
  isActive = true;

  constructor() {
    if (this.id) {
      this.load();
    }
    if (this.router.getCurrentNavigation()?.extras.state?.['ingredientCreated']) {
      this.notice.set('Ingrediente criado.');
    }
  }

  parseDecimal(value: string, places: number, maximum: number): number | null {
    const normalized = value.trim().replace(',', '.');
    if (!new RegExp('^\\d{1,6}(\\.\\d{1,' + places + '})?$').test(normalized)) {
      return null;
    }
    const number = Number(normalized);
    return Number.isFinite(number) && number >= 0 && number <= maximum ? number : null;
  }
  costValue(): number | null {
    return this.parseDecimal(this.unitCost, 4, 999999.9999);
  }
  minimumValue(): number | null {
    return this.parseDecimal(this.minimumStock, 3, 999999.999);
  }

  load(): void {
    if (!this.id) {
      return;
    }
    this.loading.set(true);
    this.error.set('');
    this.api
      .get(this.id)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.loading.set(false)),
      )
      .subscribe({
        next: (ingredient) => {
          this.assign(ingredient);
          this.ready.set(true);
        },
        error: (error) => this.error.set(apiError(error)),
      });
  }

  private assign(ingredient: Ingredient): void {
    this.name = ingredient.name;
    this.unit = ingredient.unit;
    this.unitCost = String(ingredient.unitCost).replace('.', ',');
    this.minimumStock = String(ingredient.minimumStock).replace('.', ',');
    this.supplier = ingredient.supplier ?? '';
    this.isActive = ingredient.isActive;
  }

  save(): void {
    const unitCost = this.costValue();
    const minimumStock = this.minimumValue();
    if (
      this.busy() ||
      !this.ready() ||
      !this.name.trim() ||
      this.name.length > 120 ||
      this.supplier.length > 150 ||
      !this.units.some((unit) => unit.value === this.unit) ||
      unitCost === null ||
      minimumStock === null
    ) {
      return;
    }
    this.busy.set(true);
    this.error.set('');
    this.notice.set('');
    const input: IngredientInput = {
      name: this.name.trim(),
      unit: this.unit,
      unitCost,
      minimumStock,
      supplier: this.supplier.trim() || null,
      isActive: this.isActive,
    };
    const request = this.id ? this.api.update(this.id, input) : this.api.create(input);
    request
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.busy.set(false)),
      )
      .subscribe({
        next: (ingredient) => {
          if (!this.id) {
            void this.router.navigate(['/equipe/ingredientes', ingredient.id], {
              replaceUrl: true,
              state: { ingredientCreated: true },
            });
            return;
          }
          this.assign(ingredient);
          this.notice.set('Ingrediente atualizado.');
        },
        error: (error) => this.error.set(apiError(error)),
      });
  }
}
