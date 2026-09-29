export interface StaffProfile {
  id: string;
  name: string;
  username: string;
  role: string;
  permissions: string[];
}

export interface LoginResponse {
  accessToken: string;
  tokenType: string;
  expiresAt: string;
  user: StaffProfile;
}

export interface StaffUser {
  id: string;
  name: string;
  username: string;
  roleId: number;
  role: string;
  isActive: boolean;
  createdAt: string;
}

export interface StaffRole { id: number; code: string; name: string; }
export interface UserPage { items: StaffUser[]; page: number; pageSize: number; totalCount: number; }
export interface UserInput { name: string; username: string; roleId: number; isActive: boolean; }

export function roleLabel(role: string): string {
  const labels: Record<string, string> = {
    Administrator: 'Administrador', Attendant: 'Atendente', Kitchen: 'Cozinha', Dispatch: 'Expedição',
  };
  return labels[role] ?? role;
}

