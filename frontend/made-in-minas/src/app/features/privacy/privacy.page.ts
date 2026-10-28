import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { IonContent } from '@ionic/angular/ion-content';
import { environment } from '../../../environments/environment';

@Component({
  selector: 'app-privacy',
  host: { class: 'ion-page' },
  imports: [RouterLink, IonContent],
  template: `
    <ion-content>
      <main class="checkout-shell">
        <header>
          <a class="brand" routerLink="/pedido">MADE IN MINAS<span>HAMBURGUERIA</span></a>
          <a routerLink="/pedido">Voltar ao cardápio</a>
        </header>
        <section class="intro"><h1>Privacidade e atendimento</h1></section>
        @if (testing) {
          <p class="notice">
            Este ambiente de teste usa apenas dados fictícios. Não informe dados reais.
          </p>
        }
        <section class="panel" aria-label="Uso dos dados no atendimento">
          <h2>Informações para seu pedido</h2>
          <p>
            Usamos nome, telefone, itens, observações e, na entrega, endereço para confirmar,
            preparar e entregar sua compra. As mensagens ficam registradas para a equipe acompanhar
            o atendimento.
          </p>
          <h2>Pagamento</h2>
          <p>
            Você informa a forma escolhida e, quando necessário, o valor para troco. O site não
            realiza cobrança online nem solicita número de cartão ou senha bancária. O recebimento é
            conferido pela equipe.
          </p>
          <h2>Acompanhamento e dispositivo</h2>
          <p>
            O acesso de acompanhamento vale por até sete dias. A opção “Lembrar neste dispositivo”
            mantém os acessos no navegador usado. Em um aparelho compartilhado, você pode remover os
            acompanhamentos salvos em “Meus pedidos”.
          </p>
          <p>
            O fim desse acesso não apaga automaticamente os registros de pedidos e mensagens usados
            na operação da loja.
          </p>
          <h2>Dúvidas e solicitações</h2>
          <p>
            Para dúvidas, correções ou solicitações sobre seus dados, fale com a equipe pelo
            atendimento do site.
          </p>
          <p><a routerLink="/pedido/atendimento">Falar com atendente</a></p>
          <p>Made in Minas · Rua Esperança, 68, Clarindo de Paiva, Corinto–MG.</p>
        </section>
      </main>
    </ion-content>
  `,
  styleUrl: '../public-checkout/public-checkout.page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PrivacyPage {
  readonly testing = environment.testing;
}
