import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../environments/environment';

export type ChatStatus = 'Waiting' | 'InService' | 'Closed';
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
  kind: 'Visitor' | 'Staff' | 'System';
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
}
export const chatStatusLabel = (status: ChatStatus) =>
  ({ Waiting: 'Aguardando atendimento', InService: 'Em atendimento', Closed: 'Encerrado' })[status];

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
}
