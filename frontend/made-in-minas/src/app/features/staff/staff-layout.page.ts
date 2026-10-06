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
    ><div class="staff-page staff-workspace">
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
        @if (
          session.canManageOrders() ||
          session.canManageChat() ||
          session.canWorkDispatch() ||
          session.canWorkKitchen()
        ) {
          <div class="nav-group">
            <h2>Operação</h2>
            @if (session.canManageOrders()) {
              <a routerLink="/equipe/pedidos" routerLinkActive="selected">Pedidos</a>
              <a routerLink="/equipe/carrinho" routerLinkActive="selected">Carrinho</a>
            }
            @if (session.canManageChat()) {
              <a routerLink="/equipe/atendimentos" routerLinkActive="selected">Atendimentos</a>
            }
            @if (session.canWorkDispatch()) {
              <a routerLink="/equipe/expedicao" routerLinkActive="selected">Expedição</a>
            }
            @if (session.canWorkKitchen()) {
              <a routerLink="/equipe/cozinha" routerLinkActive="selected">Cozinha</a>
            }
          </div>
        }
        @if (
          session.canManageUsers() ||
          session.canManageCatalog() ||
          session.canManageCustomers() ||
          session.canManageDelivery()
        ) {
          <div class="nav-group">
            <h2>Cadastros</h2>
            @if (session.canManageDelivery()) {
              <a routerLink="/equipe/regioes-entrega" routerLinkActive="selected"
                >Regiões de entrega</a
              >
            }
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
            @if (session.canManageCustomers()) {
              <a routerLink="/equipe/clientes" routerLinkActive="selected">Clientes</a>
            }
          </div>
        }
        @if (session.canViewDashboard() || session.canViewReports() || session.canManageCatalog()) {
          <div class="nav-group">
            <h2>Gestão</h2>
            @if (session.canManageCatalog()) {
              <a routerLink="/equipe/reposicao" routerLinkActive="selected">Reposição</a>
            }
            @if (session.canViewDashboard()) {
              <a routerLink="/equipe/dashboard" routerLinkActive="selected">Dashboard</a>
            }
            @if (session.canViewReports()) {
              <a routerLink="/equipe/relatorios" routerLinkActive="selected">Relatórios</a>
            }
          </div>
        }
        <div class="nav-group">
          <h2>Conta</h2>
          <a
            routerLink="/equipe"
            routerLinkActive="selected"
            [routerLinkActiveOptions]="{ exact: true }"
            >Minha conta</a
          >
          <a routerLink="/equipe/senha" routerLinkActive="selected">Minha senha</a>
        </div>
      </nav>
      <main class="staff-body">
        @if (session.user()) {
          <router-outlet />
        }
      </main></div
  ></ion-content>`,
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './staff-layout.page.scss',
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
        next: () => this.session.end('Você saiu. Suas sessões foram encerradas.', false),
        error: () =>
          this.session.end(
            'Você saiu deste navegador. Não foi possível confirmar o encerramento das outras sessões no servidor.',
            false,
          ),
      });
  }
}
