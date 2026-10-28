import { ChangeDetectionStrategy, Component } from '@angular/core';
import { IonApp } from '@ionic/angular/ion-app';
import { RouterOutlet } from '@angular/router';
import { environment } from '../environments/environment';

@Component({
  selector: 'app-root',
  imports: [IonApp, RouterOutlet],
  template: `<ion-app [class.has-environment-notice]="environmentLabel">
    @if (environmentLabel) {
      <p class="environment-notice" role="note" aria-label="Ambiente do sistema">
        {{ environmentLabel }}
      </p>
    }
    <router-outlet />
  </ion-app>`,
  styles: `
    .environment-notice {
      position: absolute;
      inset: 0 0 auto;
      height: 32px;
      margin: 0;
      display: flex;
      align-items: center;
      justify-content: center;
      padding: 0 8px;
      background: #ffedbd;
      color: #533a09;
      font:
        700 12px/1.2 Arial,
        sans-serif;
      text-align: center;
    }
    @media print {
      .environment-notice {
        display: none;
      }
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AppComponent {
  readonly environmentLabel = environment.environmentLabel;
}
