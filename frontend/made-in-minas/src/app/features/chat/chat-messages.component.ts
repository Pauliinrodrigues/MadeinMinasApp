import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { ChatMessage } from '../../core/services/chat-api.service';

@Component({
  selector: 'app-chat-messages',
  imports: [DatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<ol aria-label="Mensagens da conversa">
      @for (message of messages(); track message.sequence) {
        <li [class.staff]="message.kind === 'Staff'" [class.system]="message.kind === 'System'">
          <div>
            <strong>{{
              message.kind === 'Visitor'
                ? 'Cliente'
                : message.kind === 'Staff'
                  ? 'Atendente'
                  : 'Atendimento'
            }}</strong>
            <time [attr.datetime]="message.createdAt">{{
              message.createdAt | date: 'dd/MM HH:mm' : '-0300'
            }}</time>
          </div>
          <p>{{ message.text }}</p>
        </li>
      }
    </ol>
    <p class="hint">Horários de Brasília.</p>`,
  styles: `
    ol {
      list-style: none;
      padding: 0;
      margin: 20px 0;
      display: grid;
      gap: 12px;
    }
    li {
      padding: 14px;
      border: 1px solid #dfd3c3;
      border-radius: 12px;
      background: #fffdf9;
      min-width: 0;
    }
    li.staff {
      background: #f1e5d6;
    }
    li.system {
      border-style: dashed;
    }
    div {
      display: flex;
      justify-content: space-between;
      flex-wrap: wrap;
      gap: 8px;
      font-size: 13px;
    }
    time,
    .hint {
      color: #695b4c;
      font-size: 12px;
    }
    p {
      white-space: pre-wrap;
      overflow-wrap: anywhere;
      line-height: 1.5;
      margin: 10px 0 0;
    }
  `,
})
export class ChatMessagesComponent {
  readonly messages = input<ChatMessage[]>([]);
}
