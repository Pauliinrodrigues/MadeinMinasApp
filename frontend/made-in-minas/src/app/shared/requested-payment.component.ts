import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { formatProductPrice } from '../core/services/product-api.service';

@Component({
  selector: 'app-requested-payment',
  template: `
    @if (method()) {
      <div aria-label="Pagamento escolhido pelo cliente">
        <p>
          Escolhido pelo cliente: <strong>{{ label() }}</strong>
        </p>
        @if (changeFor(); as amount) {
          <p>
            Troco para {{ price(amount) }}:
            <strong>{{ price(amount - total()) }}</strong>
          </p>
        }
      </div>
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: 'p { margin-block: 6px; }',
})
export class RequestedPayment {
  readonly price = formatProductPrice;
  readonly method = input<string | null | undefined>(null);
  readonly changeFor = input<number | null | undefined>(null);
  readonly total = input.required<number>();
  readonly label = computed(() => {
    const labels: Record<string, string> = { Card: 'Cartão', Cash: 'Dinheiro', Pix: 'Pix' };
    return labels[this.method() ?? ''] ?? 'Não informado';
  });
}
