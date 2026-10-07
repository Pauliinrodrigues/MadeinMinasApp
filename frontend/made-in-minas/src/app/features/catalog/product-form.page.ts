import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { finalize, forkJoin, of, switchMap, tap } from 'rxjs';
import { apiError } from '../../core/api-error';
import { Category, CategoryApi } from '../../core/services/category-api.service';
import {
  Product,
  ProductApi,
  ProductInput,
  parseProductPrice,
  validProductImage,
  isManagedProductImage,
  maxProductImageBytes,
  productImageSource,
} from '../../core/services/product-api.service';

@Component({
  selector: 'app-product-form',
  imports: [FormsModule, RouterLink],
  templateUrl: './product-form.page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProductFormPage {
  private readonly api = inject(ProductApi);
  private readonly categoryApi = inject(CategoryApi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly router = inject(Router);
  readonly id = inject(ActivatedRoute).snapshot.paramMap.get('id');
  readonly categories = signal<Category[]>([]);
  readonly loading = signal(false);
  readonly ready = signal(false);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly notice = signal('');
  readonly imageFailed = signal(false);
  readonly imageError = signal('');
  readonly selectedImage = signal<File | null>(null);
  readonly localPreview = signal('');
  readonly uploading = signal(false);
  readonly managedImage = isManagedProductImage;
  readonly parsePrice = parseProductPrice;
  readonly validImage = validProductImage;
  originalCategoryId: string | null = null;
  name = '';
  categoryId = '';
  description = '';
  price = '';
  imageUrl = '';
  isActive = true;
  isAvailable = true;

  constructor() {
    this.destroyRef.onDestroy(() => this.clearSelectedImage());
    this.load();
    if (this.router.getCurrentNavigation()?.extras.state?.['productCreated']) {
      this.notice.set('Produto criado.');
    }
  }

  selectImage(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file) {
      return;
    }
    this.imageError.set('');
    if (
      !/\.(jpe?g|png|webp)$/i.test(file.name) ||
      (file.type && !['image/jpeg', 'image/png', 'image/webp'].includes(file.type))
    ) {
      this.imageError.set('Escolha uma foto JPG, PNG ou WebP.');
      return;
    }
    if (!file.size || file.size > maxProductImageBytes) {
      this.imageError.set('Escolha uma foto de até 8 MB, que não esteja vazia.');
      return;
    }
    this.clearSelectedImage();
    this.selectedImage.set(file);
    this.localPreview.set(URL.createObjectURL(file));
    this.imageUrl = '';
    this.imageFailed.set(false);
    this.notice.set('');
  }

  previewImage(): string {
    return (
      this.localPreview() ||
      (this.validImage(this.imageUrl) ? productImageSource(this.imageUrl.trim()) : '')
    );
  }

  removeImage(): void {
    this.clearSelectedImage();
    this.imageUrl = '';
    this.imageFailed.set(false);
    this.imageError.set('');
    this.notice.set('Foto removida do formulário. Salve o produto para confirmar.');
  }

  private clearSelectedImage(): void {
    if (this.localPreview()) {
      URL.revokeObjectURL(this.localPreview());
    }
    this.localPreview.set('');
    this.selectedImage.set(null);
  }

  selectedCategory(): Category | undefined {
    return this.categories().find((category) => category.id === this.categoryId);
  }
  validCategory(): boolean {
    const category = this.selectedCategory();
    return !!category && (category.isActive || category.id === this.originalCategoryId);
  }
  hasActiveCategory(): boolean {
    return this.categories().some((category) => category.isActive);
  }

  load(): void {
    this.loading.set(true);
    this.error.set('');
    forkJoin({
      categories: this.categoryApi.allForSelection(),
      product: this.id ? this.api.get(this.id) : of(null),
    })
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.loading.set(false)),
      )
      .subscribe({
        next: ({ categories, product }) => {
          this.categories.set(categories);
          if (product) {
            this.originalCategoryId = product.categoryId;
            this.fill(product);
          }
          this.ready.set(true);
        },
        error: (error) => this.error.set(apiError(error)),
      });
  }

  save(): void {
    const price = this.parsePrice(this.price);
    if (
      this.busy() ||
      !this.name.trim() ||
      price === null ||
      !this.validCategory() ||
      !this.validImage(this.imageUrl)
    ) {
      return;
    }
    this.busy.set(true);
    this.error.set('');
    this.notice.set('');
    const input: ProductInput = {
      name: this.name.trim(),
      categoryId: this.categoryId,
      price,
      description: this.description.trim() || null,
      imageUrl: this.imageUrl.trim() || null,
      isActive: this.isActive,
      isAvailable: this.isAvailable,
    };
    const file = this.selectedImage();
    this.uploading.set(!!file);
    const image = file
      ? this.api.uploadImage(file).pipe(
          tap(({ imageUrl }) => {
            // Keep an uploaded reference if the product save fails, so retrying does not upload again.
            this.imageUrl = imageUrl;
            this.clearSelectedImage();
            this.imageFailed.set(false);
            this.uploading.set(false);
          }),
        )
      : of({ imageUrl: input.imageUrl });
    const request = image.pipe(
      switchMap(({ imageUrl }) => {
        const payload = { ...input, imageUrl };
        return this.id ? this.api.update(this.id, payload) : this.api.create(payload);
      }),
    );
    request
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => {
          this.busy.set(false);
          this.uploading.set(false);
        }),
      )
      .subscribe({
        next: (product) => {
          if (!this.id) {
            void this.router.navigate(['/equipe/produtos', product.id], {
              replaceUrl: true,
              state: { productCreated: true },
            });
            return;
          }
          this.originalCategoryId = product.categoryId;
          this.fill(product);
          // A categoria pode ter sido inativada depois de o formulário ser aberto.
          this.categories.update((items) =>
            items.map((category) =>
              category.id === product.categoryId
                ? { ...category, isActive: product.categoryIsActive, name: product.categoryName }
                : category,
            ),
          );
          this.notice.set('Produto atualizado.');
        },
        error: (error) => this.error.set(apiError(error)),
      });
  }

  private fill(product: Product): void {
    this.clearSelectedImage();
    this.imageError.set('');
    this.name = product.name;
    this.categoryId = product.categoryId;
    this.description = product.description ?? '';
    this.price = product.price.toFixed(2).replace('.', ',');
    this.imageUrl = product.imageUrl ?? '';
    this.imageFailed.set(false);
    this.isActive = product.isActive;
    this.isAvailable = product.isAvailable;
  }
}
