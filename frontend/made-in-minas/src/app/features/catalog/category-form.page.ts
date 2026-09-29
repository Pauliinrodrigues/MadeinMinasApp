import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { apiError } from '../../core/api-error';
import { CategoryApi, CategoryInput } from '../../core/services/category-api.service';

@Component({
  selector: 'app-category-form', imports: [FormsModule, RouterLink],
  templateUrl: './category-form.page.html', changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CategoryFormPage {
  private readonly api = inject(CategoryApi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  readonly id = this.route.snapshot.paramMap.get('id');
  readonly loading = signal(false);
  readonly ready = signal(!this.id);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly notice = signal('');
  name = '';
  description = '';
  displayOrder: number | null = 0;
  isActive = true;

  constructor() {
    if (this.id) this.load();
    if (this.router.getCurrentNavigation()?.extras.state?.['categoryCreated']) this.notice.set('Categoria criada.');
  }

  validOrder(): boolean {
    return this.displayOrder !== null && Number.isInteger(this.displayOrder) && this.displayOrder >= 0 && this.displayOrder <= 9999;
  }

  load(): void {
    if (!this.id) return;
    this.loading.set(true);
    this.error.set('');
    this.api.get(this.id).pipe(takeUntilDestroyed(this.destroyRef), finalize(() => this.loading.set(false))).subscribe({
      next: category => {
        this.name = category.name;
        this.description = category.description ?? '';
        this.displayOrder = category.displayOrder;
        this.isActive = category.isActive;
        this.ready.set(true);
      },
      error: error => this.error.set(apiError(error)),
    });
  }

  save(): void {
    if (this.busy() || !this.name.trim() || !this.validOrder()) return;
    this.busy.set(true);
    this.error.set('');
    this.notice.set('');
    const input: CategoryInput = { name: this.name.trim(), description: this.description.trim() || null,
      displayOrder: this.displayOrder!, isActive: this.isActive };
    const request = this.id ? this.api.update(this.id, input) : this.api.create(input);
    request.pipe(takeUntilDestroyed(this.destroyRef), finalize(() => this.busy.set(false))).subscribe({
      next: category => {
        if (!this.id) {
          void this.router.navigate(['/equipe/categorias', category.id], { replaceUrl: true, state: { categoryCreated: true } });
          return;
        }
        this.name = category.name;
        this.description = category.description ?? '';
        this.displayOrder = category.displayOrder;
        this.isActive = category.isActive;
        this.notice.set('Categoria atualizada.');
      },
      error: error => this.error.set(apiError(error)),
    });
  }
}

