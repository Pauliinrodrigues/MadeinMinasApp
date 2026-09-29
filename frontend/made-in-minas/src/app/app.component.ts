import { ChangeDetectionStrategy, Component } from '@angular/core';
import { IonApp } from '@ionic/angular/ion-app';
import { RouterOutlet } from '@angular/router';

@Component({
  selector: 'app-root',
  imports: [IonApp, RouterOutlet],
  template: '<ion-app><router-outlet /></ion-app>',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AppComponent {}
