import { Injectable, signal } from '@angular/core';
import { ChatAccess, ChatMessageInput, StartChatInput } from './chat-api.service';

interface ChatRecovery {
  access: ChatAccess | null;
  pendingStart: StartChatInput | null;
  pendingMessage: ChatMessageInput | null;
}
const empty: ChatRecovery = { access: null, pendingStart: null, pendingMessage: null };
const key = 'made-in-minas.public-chat.v1';
const validMessage = (value: unknown): value is ChatMessageInput => {
  if (!value || typeof value !== 'object' || !('requestId' in value) || !('text' in value)) {
    return false;
  }
  return (
    typeof value.requestId === 'string' &&
    /^[0-9a-f-]{36}$/i.test(value.requestId) &&
    typeof value.text === 'string' &&
    !!value.text.trim() &&
    value.text.length <= 2000
  );
};

@Injectable({ providedIn: 'root' })
export class PublicChatState {
  readonly value = signal<ChatRecovery>(empty);
  readonly recoveryError = signal(false);
  constructor() {
    try {
      const stored = sessionStorage.getItem(key);
      if (!stored) {
        return;
      }
      if (stored.length > 16000) {
        throw new Error('Invalid chat recovery');
      }
      const parsed = JSON.parse(stored);
      const value = parsed.value;
      if (
        parsed.version !== 1 ||
        !value ||
        (value.access !== null &&
          (!value.access ||
            typeof value.access.token !== 'string' ||
            !value.access.token.length ||
            value.access.token.length > 2048 ||
            !Number.isFinite(Date.parse(value.access.expiresAt)))) ||
        (value.pendingStart !== null &&
          (!validMessage(value.pendingStart) ||
            typeof value.pendingStart.name !== 'string' ||
            !value.pendingStart.name.trim() ||
            value.pendingStart.name.length > 120)) ||
        (value.pendingMessage !== null && !validMessage(value.pendingMessage)) ||
        (value.pendingMessage && !value.access) ||
        (value.pendingStart && value.access)
      ) {
        throw new Error('Invalid chat recovery');
      }
      this.value.set(value);
    } catch {
      this.recoveryError.set(true);
    }
  }
  save(value: ChatRecovery): void {
    sessionStorage.setItem(key, JSON.stringify({ version: 1, value }));
    this.value.set(value);
  }
  clear(): void {
    sessionStorage.removeItem(key);
    this.value.set(empty);
    this.recoveryError.set(false);
  }
}
