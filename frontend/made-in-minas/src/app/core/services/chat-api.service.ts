import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../environments/environment';
import {
  PublicCheckoutReview,
  PublicDeliveryAddress,
  PublicOrderReceipt,
} from './public-checkout-api.service';
import { PublicCartQuote } from './public-cart-api.service';
import type { OrderStatus } from './order-api.service';

export type ChatStatus = 'Waiting' | 'InService' | 'Closed';
export type ChatAutomationMode = 'Manual' | 'Automatic';
export type ChatAutomationState = 'Idle' | 'Pending' | 'Processing' | 'Unavailable';
export interface ChatOrderConfirmation {
  requestId: string;
  expectedDraftVersion: number;
  reviewToken: string;
}
export interface ChatOrderState {
  draftVersion: number;
  items: PublicCartQuote['items'];
  name: string;
  phone: string | null;
  fulfillment: 'Pickup' | 'Delivery' | null;
  address: PublicDeliveryAddress | null;
  notes: string | null;
  quote: PublicCartQuote | null;
  review: PublicCheckoutReview | null;
  receipt: PublicOrderReceipt | null;
  missingFields: string[];
  needsReview: boolean;
}
export interface ChatAccess {
  token: string;
  expiresAt: string;
}
export interface StartChatInput {
  requestId: string;
  name: string;
  text: string;
}
export interface ChatMessageInput {
  requestId: string;
  text: string;
}
export interface ChatMessage {
  sequence: number;
  kind: 'Visitor' | 'Staff' | 'System' | 'Assistant';
  text: string;
  createdAt: string;
}
export interface ChatTranscript {
  status: ChatStatus;
  version: number;
  createdAt: string;
  updatedAt: string;
  messages: ChatMessage[];
  hasMore: boolean;
  automationMode?: ChatAutomationMode;
  automationState?: ChatAutomationState;
  automationAvailable?: boolean;
  order?: ChatOrderState | null;
}
export interface ChatSummary {
  id: string;
  visitorName: string;
  status: ChatStatus;
  assignedToId: string | null;
  assignedToName: string | null;
  version: number;
  createdAt: string;
  updatedAt: string;
  expiresAt: string;
  automationMode?: ChatAutomationMode;
  automationState?: ChatAutomationState;
  automationAvailable?: boolean;
}
export interface ChatPage {
  items: ChatSummary[];
  page: number;
  pageSize: number;
  totalCount: number;
}
export interface StaffChat {
  conversation: ChatSummary;
  transcript: ChatTranscript;
  linkedOrder?: {
    id: string;
    number: number;
    status: OrderStatus;
    fulfillment: 'Pickup' | 'Delivery';
    subtotal: number;
    deliveryFee: number;
    total: number;
  } | null;
}
export const chatStatusLabel = (status: ChatStatus) =>
  ({ Waiting: 'Aguardando atendimento', InService: 'Em atendimento', Closed: 'Encerrado' })[status];
export const chatAttendanceLabel = (chat: Pick<ChatTranscript, 'status' | 'automationMode'>) =>
  chat.status === 'Closed'
    ? chatStatusLabel(chat.status)
    : chat.automationMode === 'Automatic'
      ? 'Assistente virtual'
      : chat.automationMode === 'Manual'
        ? chat.status === 'Waiting'
          ? 'Aguardando atendente'
          : 'Atendente humano'
        : chatStatusLabel(chat.status);

@Injectable({ providedIn: 'root' })
export class PublicChatApi {
  private readonly http = inject(HttpClient);
  private readonly url = environment.apiBaseUrl + '/public-chat';
  start(input: StartChatInput) {
    return this.http.post<{ access: ChatAccess | null; createdAt: string }>(this.url, input);
  }
  read(token: string, after: number) {
    return this.http.get<ChatTranscript>(this.url, {
      headers: { 'X-Chat-Access': token },
      params: { after },
    });
  }
  send(token: string, input: ChatMessageInput) {
    return this.http.post<ChatMessage>(this.url + '/messages', input, {
      headers: { 'X-Chat-Access': token },
    });
  }
  handoff(token: string, expectedVersion: number) {
    return this.http.put<ChatTranscript>(
      this.url + '/handoff',
      { expectedVersion },
      {
        headers: { 'X-Chat-Access': token },
      },
    );
  }
  reviewOrder(token: string, expectedDraftVersion: number) {
    return this.http.post<ChatOrderState>(
      this.url + '/order/review',
      { expectedDraftVersion },
      {
        headers: { 'X-Chat-Access': token },
      },
    );
  }
  confirmOrder(token: string, input: ChatOrderConfirmation) {
    return this.http.post<ChatOrderState>(this.url + '/order/confirm', input, {
      headers: { 'X-Chat-Access': token },
    });
  }
}

@Injectable({ providedIn: 'root' })
export class StaffChatApi {
  private readonly http = inject(HttpClient);
  private readonly url = environment.apiBaseUrl + '/chat';
  list(status: string, page: number) {
    return this.http.get<ChatPage>(this.url, { params: { status, page } });
  }
  read(id: string, after: number) {
    return this.http.get<StaffChat>(this.url + '/' + id, { params: { after } });
  }
  send(id: string, input: ChatMessageInput) {
    return this.http.post<ChatMessage>(this.url + '/' + id + '/messages', input);
  }
  act(id: string, action: 'claim' | 'close', expectedVersion: number) {
    return this.http.put<ChatSummary>(this.url + '/' + id + '/' + action, { expectedVersion });
  }
  automate(id: string, expectedVersion: number) {
    return this.http.put<ChatSummary>(this.url + '/' + id + '/automation', { expectedVersion });
  }
}
