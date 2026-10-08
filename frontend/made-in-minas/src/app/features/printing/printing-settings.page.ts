import { DatePipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { finalize, forkJoin, interval, timeout } from 'rxjs';
import { environment } from '../../../environments/environment';
import { apiError } from '../../core/api-error';

interface Settings {
  registered: boolean;
  automatic: boolean;
  version: number;
  lastSeenAt: string | null;
}
interface Job {
  id: string;
  orderId: string;
  orderNumber: number;
  mode: string;
  state: string;
  createdAt: string;
}

@Component({
  selector: 'app-printing-settings',
  imports: [DatePipe, RouterLink],
  template: `
    <h1>Impressão automática</h1>
    <p>
      Na confirmação, sai a via da cozinha. Quando o pedido fica pronto, sai a via da expedição.
    </p>
    @if (error()) {
      <p class="error" role="alert">{{ error() }}</p>
    }
    @if (notice()) {
      <p role="status">{{ notice() }}</p>
    }
    @if (settings(); as config) {
      <section class="panel">
        <h2>Estação Windows</h2>
        <p>
          {{ connected() ? 'Agente conectado' : 'Agente desconectado' }} ·
          {{ config.automatic ? 'Automação ativada' : 'Automação desativada' }}
        </p>
        @if (config.lastSeenAt) {
          <p>Última conexão: {{ config.lastSeenAt | date: 'dd/MM/yyyy HH:mm:ss' }}</p>
        }
        <ol>
          <li>Baixe a configuração da estação neste computador.</li>
          <li>Execute o iniciador Windows com o arquivo baixado.</li>
          <li>Confira o teste na impressora e ative a automação abaixo.</li>
        </ol>
        <p>Impressora: Diebold Procomp IM453HU_A · Bobina IM4X3T/TSP143 76/80x500 mm.</p>
        <div class="actions">
          <button type="button" (click)="register()" [disabled]="busy() || config.registered">
            Baixar configuração da estação
          </button>
          @if (config.registered) {
            <button type="button" (click)="replacing.set(true)" [disabled]="busy()">
              Substituir estação
            </button>
          }
          <button
            type="button"
            class="primary"
            (click)="toggle()"
            [disabled]="busy() || (!config.automatic && !connected())"
          >
            {{ config.automatic ? 'Desativar automação' : 'Ativar automação' }}
          </button>
          <button type="button" (click)="load()" [disabled]="busy()">Atualizar fila</button>
        </div>
        @if (replacing()) {
          <p>
            Substituir desativa a automação e revoga a credencial anterior. Confira os envios em
            andamento antes de continuar.
          </p>
          <button type="button" (click)="register()" [disabled]="busy()">
            Confirmar substituição e baixar
          </button>
          <button type="button" (click)="replacing.set(false)" [disabled]="busy()">Voltar</button>
        }
        <p>
          Ativar inclui somente novas confirmações e novos pedidos prontos. Desativar impede novos
          envios automáticos; os já enfileirados continuam na fila e podem ser cancelados abaixo.
        </p>
      </section>
      <section class="panel">
        <h2>Últimos envios</h2>
        <p>
          “Enviado ao Windows” confirma o envio ao spooler. Confira a saída no papel. Envios para
          conferência não são repetidos automaticamente.
        </p>
        @if (!jobs().length) {
          <p>Nenhum envio registrado.</p>
        }
        @for (job of jobs(); track job.id) {
          <article class="job">
            <strong
              >Pedido #{{ job.orderNumber }} ·
              {{ job.mode === 'kitchen' ? 'Cozinha' : 'Expedição' }}</strong
            >
            <p>{{ label(job.state) }} · {{ job.createdAt | date: 'dd/MM/yyyy HH:mm:ss' }}</p>
            @if (job.state === 'Queued') {
              <button type="button" (click)="cancel(job.id)" [disabled]="busy()">
                Cancelar envio #{{ job.orderNumber }}
              </button>
            }
            @if (job.state === 'Review') {
              <p>Confira a impressora e o papel antes de solicitar outra cópia.</p>
              <a [routerLink]="['/comanda', job.orderId, job.mode]">Abrir comanda para conferir</a>
            }
          </article>
        }
      </section>
    }
  `,
  styles: `
    .panel {
      margin: 1rem 0;
      padding: 1rem;
      border: 1px solid #d9d9d9;
      border-radius: 0.75rem;
    }
    .job {
      padding: 0.75rem 0;
      border-top: 1px solid #ddd;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PrintingSettingsPage {
  private readonly http = inject(HttpClient);
  private readonly destroyRef = inject(DestroyRef);
  private readonly base = environment.apiBaseUrl + '/printing';
  readonly settings = signal<Settings | null>(null);
  readonly jobs = signal<Job[]>([]);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly notice = signal('');
  readonly replacing = signal(false);
  constructor() {
    this.load();
    interval(15000)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.load());
  }
  connected(): boolean {
    const seen = this.settings()?.lastSeenAt;
    return !!seen && Date.now() - Date.parse(seen) < 30000;
  }
  load(): void {
    if (this.busy()) {
      return;
    }
    this.busy.set(true);
    forkJoin({
      settings: this.http.get<Settings>(this.base + '/settings'),
      jobs: this.http.get<Job[]>(this.base + '/jobs'),
    })
      .pipe(
        timeout(20000),
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.busy.set(false)),
      )
      .subscribe({
        next: (result) => {
          this.settings.set(result.settings);
          this.jobs.set(result.jobs);
          this.error.set('');
        },
        error: (error) => this.error.set(apiError(error)),
      });
  }
  register(): void {
    if (this.busy() || !this.settings()) {
      return;
    }
    this.busy.set(true);
    this.http
      .post<{ settings: Settings; token: string }>(this.base + '/station', {
        expectedVersion: this.settings()!.version,
      })
      .pipe(
        timeout(20000),
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.busy.set(false)),
      )
      .subscribe({
        next: (result) => {
          this.settings.set(result.settings);
          this.replacing.set(false);
          this.error.set('');
          const file = new Blob(
            [
              JSON.stringify(
                {
                  apiBaseUrl: new URL(environment.apiBaseUrl, window.location.origin).href,
                  token: result.token,
                  printerName: 'Diebold Procomp IM453HU_A',
                  paperName: 'IM4X3T/TSP143 76/80x500 mm',
                },
                null,
                2,
              ),
            ],
            { type: 'application/json' },
          );
          const url = URL.createObjectURL(file);
          const link = document.createElement('a');
          link.href = url;
          link.download = 'MadeInMinas-printer.json';
          link.click();
          setTimeout(() => URL.revokeObjectURL(url), 10000);
          this.notice.set(
            'Configuração baixada. Execute scripts/Start-PrintAgent.ps1 com -ConfigurationFile apontando para esse arquivo. Após instalar, remova o arquivo baixado: ele contém a credencial da estação.',
          );
        },
        error: (error) => this.error.set(apiError(error)),
      });
  }
  toggle(): void {
    if (this.busy() || !this.settings()) {
      return;
    }
    this.busy.set(true);
    this.http
      .put<Settings>(this.base + '/settings', {
        automatic: !this.settings()!.automatic,
        expectedVersion: this.settings()!.version,
      })
      .pipe(
        timeout(20000),
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.busy.set(false)),
      )
      .subscribe({
        next: (result) => {
          this.settings.set(result);
          this.error.set('');
          this.notice.set(
            result.automatic
              ? 'Automação ativada para novos eventos.'
              : 'Automação desativada. Confira os envios pendentes.',
          );
        },
        error: (error) => this.error.set(apiError(error)),
      });
  }
  cancel(id: string): void {
    if (this.busy()) {
      return;
    }
    this.busy.set(true);
    this.http
      .post(this.base + '/jobs/' + id + '/cancel', {})
      .pipe(
        timeout(20000),
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.busy.set(false)),
      )
      .subscribe({
        next: () => {
          this.jobs.update((jobs) =>
            jobs.map((job) => (job.id === id ? { ...job, state: 'Cancelled' } : job)),
          );
          this.error.set('');
        },
        error: (error) => this.error.set(apiError(error)),
      });
  }
  label(state: string): string {
    return (
      (
        {
          Queued: 'Aguardando impressão',
          Claimed: 'Em envio',
          Submitted: 'Enviado ao Windows',
          Review: 'Conferir na impressora',
          Cancelled: 'Envio cancelado',
        } as Record<string, string>
      )[state] ?? state
    );
  }
}
