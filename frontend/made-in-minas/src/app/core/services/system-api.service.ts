import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { timeout } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface SystemStatus {
  status: 'available';
}

@Injectable({ providedIn: 'root' })
export class SystemApiService {
  private readonly http = inject(HttpClient);

  getStatus() {
    return this.http.get<SystemStatus>(`${environment.apiBaseUrl}/system/status`)
      .pipe(timeout(5000));
  }
}
