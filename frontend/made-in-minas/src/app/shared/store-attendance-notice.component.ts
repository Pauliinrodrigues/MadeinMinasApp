import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { StoreAttendanceStatus } from '../core/services/store-attendance-status.service';
import { storeDayNames, storePaymentLabels } from '../core/services/store-attendance-api.service';

@Component({
  selector: 'app-store-attendance-notice',
  template: ` <section aria-label="Horários e atendimento" class="store-notice">
    @if (status.data(); as info) {
      <strong>{{
        info.isPaused
          ? 'Pedidos pausados no momento'
          : info.acceptingOrders
            ? 'Aberto para pedidos'
            : 'Estamos fora do horário de funcionamento'
      }}</strong>
      @if (!info.acceptingOrders) {
        <p>
          Você pode consultar o cardápio e montar seu carrinho. Envie o pedido quando estivermos
          atendendo.
        </p>
      }
      @if (info.acceptingOrders && !info.isOpen) {
        <p>Estamos recebendo pedidos fora do horário habitual.</p>
      }
      <p>Aceitamos {{ payments(info.paymentMethods) }}.</p>
      <p>{{ info.deliveryEstimateMessage }}</p>
      <details>
        <summary>Horários de funcionamento · horário de Brasília</summary>
        <ul>
          @for (hours of info.hours; track hours.day) {
            <li>
              {{ days[hours.day - 1] }}:
              {{
                hours.isClosed
                  ? 'Fechado'
                  : hours.opensAt +
                    ' às ' +
                    (hours.closesAt === '00:00' ? '00:00 (meia-noite)' : hours.closesAt)
              }}
            </li>
          }
        </ul>
      </details>
    } @else if (status.failed()) {
      <p>Não foi possível consultar o atendimento da loja. Atualize antes de enviar seu pedido.</p>
      <button type="button" (click)="status.refresh()" [disabled]="status.loading()">
        Atualizar atendimento
      </button>
    } @else {
      <p>Consultando atendimento da loja…</p>
    }
  </section>`,
  styles: `
    .store-notice {
      padding: 18px;
      margin-block: 18px;
      border: 1px solid #dfd3c3;
      border-radius: 12px;
      background: #faf5ec;
    }
    p {
      margin-block: 8px;
    }
    summary {
      cursor: pointer;
    }
    ul {
      padding-left: 20px;
    }
    @media (max-width: 760px) {
      .store-notice {
        padding: 10px 12px;
        margin-block: 10px;
        font-size: 13px;
        line-height: 1.35;
      }
      p {
        margin-block: 5px;
      }
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class StoreAttendanceNotice {
  readonly status = inject(StoreAttendanceStatus);
  readonly days = storeDayNames;
  payments(methods: string[]): string {
    return methods.map((method) => storePaymentLabels[method]).join(', ');
  }
}
