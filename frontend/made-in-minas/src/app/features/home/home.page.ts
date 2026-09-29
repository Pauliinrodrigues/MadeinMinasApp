import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { IonButton } from '@ionic/angular/ion-button';
import { IonContent } from '@ionic/angular/ion-content';
import { IonSpinner } from '@ionic/angular/ion-spinner';
import { SystemApiService } from '../../core/services/system-api.service';

@Component({
  selector: 'app-home',
  host: { class: 'ion-page' },
  imports: [IonButton, IonContent, IonSpinner, RouterLink],
  templateUrl: './home.page.html',
  styleUrl: './home.page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class HomePage {
  private readonly systemApi = inject(SystemApiService);
  private readonly destroyRef = inject(DestroyRef);
  readonly connection = signal<'checking' | 'online' | 'offline'>('checking');

  constructor() {
    this.checkConnection();
  }

  checkConnection(): void {
    this.connection.set('checking');
    this.systemApi.getStatus().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (response) => this.connection.set(response.status === 'available' ? 'online' : 'offline'),
      error: () => this.connection.set('offline'),
    });
  }
}
