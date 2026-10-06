import { Injectable, inject, signal } from '@angular/core';
import { AuthSession } from '../auth/auth-session.service';

@Injectable({ providedIn: 'root' })
export class ChatReadState {
  private readonly revision = signal(0);
  private readonly session = inject(AuthSession);
  private key(id: string): string {
    return `made-in-minas.chat-read.${this.session.user()!.id}.${id}`;
  }

  hasUpdates(id: string, version: number): boolean {
    this.revision();
    try {
      const stored = sessionStorage.getItem(this.key(id));
      return stored === null || !Number.isInteger(Number(stored)) || version > Number(stored);
    } catch {
      return true;
    }
  }

  markRead(id: string, version: number): void {
    try {
      sessionStorage.setItem(this.key(id), String(version));
      this.revision.update((value) => value + 1);
    } catch {
      /* Indicador local é opcional e não pode impedir o atendimento. */
    }
  }
}
