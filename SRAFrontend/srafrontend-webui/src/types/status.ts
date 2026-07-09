export type SraStatus = {
  running: boolean
  pid?: number | null
  executablePath?: string
  port?: number
  detail?: string
  mode?: string
  configs?: string[]
  configNames?: string[]
  sessionId?: string
  task?: string
  taskName?: string
  status?: string
  state?: string
  owner?: string
}

export type HealthInfo = {
  ok: boolean
  sra?: SraStatus
}

export type PageMeta = {
  title: string
  description: string
  label: string
}
