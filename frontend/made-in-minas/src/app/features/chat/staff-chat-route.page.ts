import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { map } from 'rxjs';
import { StaffChatPage } from './staff-chat.page';

@Component({
  selector: 'app-staff-chat-route',
  imports: [StaffChatPage],
  changeDetection: ChangeDetectionStrategy.OnPush,
  // Recriar por id cancela consultas antigas e isola mensagens/tentativas de cada conversa.
  template: `@for (id of ids(); track id) {
    <app-staff-chat />
  }`,
})
export class StaffChatRoutePage {
  readonly ids = toSignal(
    inject(ActivatedRoute).paramMap.pipe(map((params) => [params.get('id')!])),
    { initialValue: [] },
  );
}
