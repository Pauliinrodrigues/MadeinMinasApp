import { DestroyRef, Injectable, effect, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize, Subscription, timeout } from 'rxjs';
import { AuthSession } from '../auth/auth-session.service';
import { OrderApi, OrderPage } from './order-api.service';

const ALERT_REPEAT_INTERVAL_MS = 30000;

// Scoped to the staff workspace: opening an order must not silence its alert.
@Injectable()
export class OrderAlert {
  private readonly api = inject(OrderApi);
  private readonly session = inject(AuthSession);
  private readonly destroyRef = inject(DestroyRef);
  readonly enabled = signal(false);
  readonly warning = signal('');
  readonly pendingCount = signal(0);
  private audio?: AudioContext;
  private tone?: OscillatorNode;
  private request?: Subscription;
  private lastSync = 0;
  private lastRing = 0;

  constructor() {
    effect(() => {
      if (!this.session.canManageOrders()) {
        this.disable();
      }
    });
    const timer = setInterval(() => {
      if (!this.enabled() || document.hidden) {
        return;
      }
      if (Date.now() - this.lastSync >= 10000) {
        this.refresh();
      }
      if (this.pendingCount() > 0 && Date.now() - this.lastRing >= ALERT_REPEAT_INTERVAL_MS) {
        this.ring();
      }
    }, 1000);
    const visibility = () => {
      if (document.hidden) {
        this.stopTone();
      } else if (this.enabled()) {
        this.refresh();
      }
    };
    document.addEventListener('visibilitychange', visibility);
    this.destroyRef.onDestroy(() => {
      clearInterval(timer);
      document.removeEventListener('visibilitychange', visibility);
      this.disable();
      void this.audio?.close();
    });
  }

  async toggle(): Promise<void> {
    if (this.enabled()) {
      this.disable();
      return;
    }
    if (!this.session.canManageOrders()) {
      return;
    }
    try {
      if (!this.audio || this.audio.state === 'closed') {
        const audio = new AudioContext();
        this.audio = audio;
        audio.addEventListener('statechange', () => {
          if (this.destroyRef.destroyed || !this.enabled() || audio.state === 'running') {
            return;
          }
          this.disable();
          this.warning.set(
            'O som foi interrompido pelo navegador. Clique em Ativar aviso sonoro para reativá-lo.',
          );
        });
      }
      await this.audio.resume();
      if (this.destroyRef.destroyed || !this.session.canManageOrders()) {
        return;
      }
      if (this.audio.state !== 'running') {
        throw new Error('Audio context did not resume.');
      }
      this.enabled.set(true);
      this.warning.set('');
      this.ring(); // One sample even when the queue is empty.
      if (Date.now() - this.lastSync >= 10000) {
        this.refresh();
      }
    } catch {
      if (this.destroyRef.destroyed) {
        return;
      }
      this.disable();
      this.warning.set(
        'O navegador não permitiu ativar o som. Os avisos visuais continuam disponíveis.',
      );
    }
  }

  updateQueue(queue: OrderPage): void {
    this.pendingCount.set(queue.totalCount);
    this.lastSync = Date.now();
    if (queue.totalCount === 0) {
      this.stopTone();
    } else if (Date.now() - this.lastRing >= ALERT_REPEAT_INTERVAL_MS) {
      this.ring();
    }
  }

  accepted(): void {
    // Cancel an older poll so it cannot restore the pre-confirmation count.
    this.request?.unsubscribe();
    this.pendingCount.update((count) => Math.max(0, count - 1));
    if (this.pendingCount() === 0) {
      this.stopTone();
    }
    if (this.enabled()) {
      this.refresh();
    }
  }

  private refresh(): void {
    if (this.request && !this.request.closed) {
      return;
    }
    this.request = this.api
      .list(1, '', 'New')
      .pipe(
        timeout(15000),
        takeUntilDestroyed(this.destroyRef),
        finalize(() => {
          this.request = undefined;
        }),
      )
      .subscribe({
        next: (queue) => this.updateQueue(queue),
        // Retain the last known queue; a failed request is not an acceptance.
        error: () => {
          this.lastSync = Date.now();
        },
      });
  }

  private disable(): void {
    this.enabled.set(false);
    this.request?.unsubscribe();
    this.stopTone();
  }

  private ring(): void {
    if (!this.enabled() || document.hidden || this.audio?.state !== 'running') {
      return;
    }
    this.stopTone();
    const tone = this.audio.createOscillator();
    const volume = this.audio.createGain();
    const start = this.audio.currentTime + 0.01;
    tone.type = 'sine';
    tone.frequency.setValueAtTime(1046.5, start);
    volume.gain.setValueAtTime(0, start);
    for (const offset of [0, 0.3, 0.6]) {
      volume.gain.setValueAtTime(0, start + offset);
      volume.gain.linearRampToValueAtTime(0.16, start + offset + 0.01);
      volume.gain.setValueAtTime(0.16, start + offset + 0.13);
      volume.gain.linearRampToValueAtTime(0, start + offset + 0.18);
    }
    tone.connect(volume).connect(this.audio.destination);
    tone.addEventListener('ended', () => {
      tone.disconnect();
      volume.disconnect();
      if (this.tone === tone) {
        this.tone = undefined;
      }
    });
    tone.start(start);
    tone.stop(start + 0.8);
    this.tone = tone;
    this.lastRing = Date.now();
  }

  private stopTone(): void {
    this.tone?.stop();
    this.tone = undefined;
  }
}
