import { request } from './client'
import type { ClassSummary, EnrolledStudent, JoinInfo, SaveClass, Session, Student } from './types'

const id = (value: string) => encodeURIComponent(value)

export const classesApi = {
  list: (includeInactive = false) => request<ClassSummary[]>(`/api/classes?includeInactive=${includeInactive}`),
  get: (classId: string) => request<ClassSummary>(`/api/classes/${id(classId)}`),
  create: (body: SaveClass) => request<ClassSummary>('/api/classes', { method: 'POST', body }),
  update: (classId: string, body: SaveClass) => request<ClassSummary>(`/api/classes/${id(classId)}`, { method: 'PUT', body }),
  enroll: (classId: string, studentIds: string[]) =>
    request<EnrolledStudent[]>(`/api/classes/${id(classId)}/students`, { method: 'POST', body: { studentIds } }),
  unenroll: (classId: string, studentId: string) =>
    request<void>(`/api/classes/${id(classId)}/students/${id(studentId)}`, { method: 'DELETE' }),
}

export const studentsApi = {
  list: (search = '') => request<Student[]>(`/api/students${search ? `?search=${encodeURIComponent(search)}` : ''}`),
  create: (fullName: string, phoneNumber: string, temporaryPassword: string) =>
    request<Student>('/api/auth/register', { method: 'POST', body: { fullName, phoneNumber, temporaryPassword } }),
  update: (studentId: string, fullName: string, phoneNumber: string) =>
    request<Student>(`/api/students/${id(studentId)}`, { method: 'PUT', body: { fullName, phoneNumber } }),
  setActive: (studentId: string, isActive: boolean) =>
    request<Student>(`/api/students/${id(studentId)}/status`, { method: 'PUT', body: { isActive } }),
  resetPassword: (studentId: string, temporaryPassword: string) =>
    request<void>(`/api/students/${id(studentId)}/reset-password`, { method: 'POST', body: { temporaryPassword } }),
}

export const sessionsApi = {
  start: (classId: string) => request<Session>(`/api/classes/${id(classId)}/sessions`, { method: 'POST' }),
  get: (sessionId: string) => request<Session>(`/api/sessions/${id(sessionId)}`),
  join: (sessionId: string) => request<JoinInfo>(`/api/sessions/${id(sessionId)}/join`, { method: 'POST' }),
  end: (sessionId: string) => request<Session>(`/api/sessions/${id(sessionId)}/end`, { method: 'POST' }),
  mute: (sessionId: string, userId: string) =>
    request<void>(`/api/sessions/${id(sessionId)}/participants/${id(userId)}/mute`, { method: 'POST' }),
  muteAll: (sessionId: string) =>
    request<{ mutedCount: number }>(`/api/sessions/${id(sessionId)}/participants/mute-all`, { method: 'POST' }),
  remove: (sessionId: string, userId: string) =>
    request<void>(`/api/sessions/${id(sessionId)}/participants/${id(userId)}/remove`, { method: 'POST', body: {} }),
}
