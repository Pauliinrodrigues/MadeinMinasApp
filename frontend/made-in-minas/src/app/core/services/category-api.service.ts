import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { EMPTY, expand, reduce } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface Category {
  id: string; name: string; description: string | null; displayOrder: number;
  isActive: boolean; createdAt: string; updatedAt: string;
}
export interface CategoryPage { items: Category[]; page: number; pageSize: number; totalCount: number; }
export interface CategoryInput { name: string; description: string | null; displayOrder: number; isActive: boolean; }

@Injectable({ providedIn: 'root' })
export class CategoryApi {
  private readonly http = inject(HttpClient);
  private readonly url = environment.apiBaseUrl + '/categories';

  list(page: number, search: string, active: string) {
    let params = new HttpParams().set('page', page).set('pageSize', 20);
    if (search.trim()) params = params.set('search', search.trim());
    if (active) params = params.set('isActive', active);
    return this.http.get<CategoryPage>(this.url, { params });
  }
  get(id: string) { return this.http.get<Category>(this.url + '/' + encodeURIComponent(id)); }
  allForSelection() {
    return this.list(1, '', '').pipe(
      expand(page => page.page * page.pageSize < page.totalCount ? this.list(page.page + 1, '', '') : EMPTY),
      reduce((items, page) => [...items, ...page.items], [] as Category[]),
    );
  }
  create(input: CategoryInput) { return this.http.post<Category>(this.url, input); }
  update(id: string, input: CategoryInput) { return this.http.put<Category>(this.url + '/' + encodeURIComponent(id), input); }
  status(id: string, isActive: boolean) {
    return this.http.put<Category>(this.url + '/' + encodeURIComponent(id) + '/status', { isActive });
  }
}
