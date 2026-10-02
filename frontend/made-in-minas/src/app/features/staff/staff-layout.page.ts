import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { IonContent } from '@ionic/angular/ion-content';
import { AuthSession } from '../../core/auth/auth-session.service';
import { StaffApi } from '../../core/services/staff-api.service';

@Component({
  selector: 'app-staff-layout',
  imports: [RouterLink, RouterLinkActive, RouterOutlet, IonContent],
  host: { class: 'ion-page' },
  template: `<ion-content
    ><div class="staff-page">
      <header class="staff-header">
        <a routerLink="/equipe" class="staff-brand">MADE IN MINAS <span>ÁREA DA EQUIPE</span></a>
        <div class="staff-identity">
          <span>{{ session.user()?.name }}</span>
          <button type="button" (click)="logout()" [disabled]="leaving()">
            {{ leaving() ? 'Saindo…' : 'Sair' }}
          </button>
        </div>
      </header>
      <nav class="staff-nav" aria-label="Área da equipe">
        @if (session.canViewDashboard()) {
          <a routerLink="/equipe/dashboard" routerLinkActive="selected">Dashboard</a>
        }
        @if (session.canViewReports()) {
          <a routerLink="/equipe/relatorios" routerLinkActive="selected">Relatórios</a>
        }
        @if (session.canWorkDispatch()) {
          <a routerLink="/equipe/expedicao" routerLinkActive="selected">Expedição</a>
        }
        @if (session.canWorkKitchen()) {
          <a routerLink="/equipe/cozinha" routerLinkActive="selected">Cozinha</a>
        }
        <a
          routerLink="/equipe"
          routerLinkActive="selected"
          [routerLinkActiveOptions]="{ exact: true }"
          >Minha conta</a
        >
        @if (session.canManageUsers()) {
          <a routerLink="/equipe/funcionarios" routerLinkActive="selected">Funcionários</a>
        }
        @if (session.canManageCatalog()) {
          <a routerLink="/equipe/categorias" routerLinkActive="selected">Categorias</a>
        }
        @if (session.canManageCatalog()) {
          <a routerLink="/equipe/produtos" routerLinkActive="selected">Produtos</a>
        }
        @if (session.canManageCatalog()) {
          <a routerLink="/equipe/ingredientes" routerLinkActive="selected">Ingredientes</a>
        }
        <a routerLink="/equipe/senha" routerLinkActive="selected">Minha senha</a>
        @if (session.canManageCustomers()) {
          <a routerLink="/equipe/clientes" routerLinkActive="selected">Clientes</a>
        }
        @if (session.canManageOrders()) {
          <a routerLink="/equipe/carrinho" routerLinkActive="selected">Carrinho</a>
          <a routerLink="/equipe/pedidos" routerLinkActive="selected">Pedidos</a>
        }
      </nav>
      @if (session.user()) {
        <router-outlet />
      }</div
  ></ion-content>`,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class StaffLayoutPage {
  readonly session = inject(AuthSession);
  private readonly api = inject(StaffApi);
  private readonly destroyRef = inject(DestroyRef);
  readonly leaving = signal(false);

  logout(): void {
    if (this.leaving()) {
      return;
    }
    this.leaving.set(true);
    this.api
      .logout()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => this.session.end('Você saiu. Suas sessões foram encerradas.'),
        error: () =>
          this.session.end(
            'Você saiu deste navegador. Não foi possível confirmar o encerramento das outras sessões no servidor.',
          ),
      });
  }
}
