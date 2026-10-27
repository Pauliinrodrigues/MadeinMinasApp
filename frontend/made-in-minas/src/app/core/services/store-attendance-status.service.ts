import { DestroyRef, Injectable, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { PublicStoreAttendance, StoreAttendanceApi } from './store-attendance-api.service';

@Injectable()
export class StoreAttendanceStatus {
  private readonly api = inject(StoreAttendanceApi);
  private readonly destroyRef = inject(DestroyRef);
  readonly data = signal<PublicStoreAttendance | null>(null);
  readonly loading = signal(false);
  readonly failed = signal(false);
  constructor() {
    this.refresh();
    const refresh = () => {
      if (!document.hidden) {
        this.refresh();
      }
    };
    const timer = setInterval(refresh, 30000);
    document.addEventListener('visibilitychange', refresh);
    this.destroyRef.onDestroy(() => {
      clearInterval(timer);
      document.removeEventListener('visibilitychange', refresh);
    });
  }
  refresh(): void {
    if (this.loading()) {
      return;
    }
    this.loading.set(true);
    this.api
      .publicInfo()
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.loading.set(false)),
      )
      .subscribe({
        next: (data) => {
          this.data.set(data);
          this.failed.set(false);
        },
        error: () => {
          this.failed.set(true);
          this.data.set(null);
        },
      });
  }
}
