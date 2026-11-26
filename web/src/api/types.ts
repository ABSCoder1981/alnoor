// Response shapes of the ALNoor API (PRD 16).

export interface ScheduleEntry {
  dayOfWeek: number // 0 = Sunday … 6 = Saturday
  startTimeLocal: string // HH:mm in the class time zone
  durationMinutes: number
}

export interface EnrolledStudent {
  id: string
  fullName: string
  enrollmentStatus: 'Active' | 'Removed'
}

export interface ClassSummary {
  id: string
  title: string
  description: string | null
  timeZoneId: string
  defaultDurationMinutes: number
  isActive: boolean
  schedule: ScheduleEntry[]
  nextOccurrences: string[]
  studentCount: number
  liveSessionId: string | null
  teacher: { id: string; fullName: string }
  students: EnrolledStudent[] | null
}

export interface SaveClass {
  title: string
  description: string | null
  timeZoneId: string
  defaultDurationMinutes: number
  schedule: ScheduleEntry[]
  isActive?: boolean
}

export interface Student {
  id: string
  fullName: string
  phoneNumber: string
  isActive: boolean
  mustChangePassword: boolean
  classCount?: number
}

export interface Session {
  id: string
  classId: string
  classTitle: string
  status: 'Live' | 'Ended'
  startedAt: string
  endedAt: string | null
  endReason: 'Teacher' | 'Timeout' | 'System' | null
}

export interface JoinInfo {
  livekitUrl: string
  token: string
  expiresAt: string
  role: 'Teacher' | 'Student'
  roomName: string
  identity: string
}
