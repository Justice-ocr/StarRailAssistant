<template>
  <el-container class="shell">
    <el-header class="topbar">
      <div class="title-block">
        <div class="eyebrow">SRA WebUI</div>
        <h1>Local Control</h1>
        <p>Manage configs, tasks, and logs.</p>
      </div>
      <div class="top-actions">
        <el-tag :type="healthTag">{{ healthLabel }}</el-tag>
        <el-tag :type="runningTag">{{ runningLabel }}</el-tag>
      </div>
    </el-header>

    <el-main class="main">
      <el-row :gutter="16">
        <el-col :xs="24" :lg="8">
          <el-card class="panel">
            <template #header>Status</template>
            <el-descriptions :column="1" border size="small">
              <el-descriptions-item label="WebUI">{{ health.ok ? 'Online' : 'Offline' }}</el-descriptions-item>
              <el-descriptions-item label="SRA">{{ sraStatus.running ? 'Running' : 'Stopped' }}</el-descriptions-item>
              <el-descriptions-item label="PID">{{ sraStatus.pid ?? '-' }}</el-descriptions-item>
              <el-descriptions-item label="Port">{{ sraStatus.port ?? '-' }}</el-descriptions-item>
              <el-descriptions-item label="Executable">{{ sraStatus.executablePath || '-' }}</el-descriptions-item>
              <el-descriptions-item label="Detail">{{ sraStatus.detail || '-' }}</el-descriptions-item>
            </el-descriptions>
            <div class="toolbar">
              <el-button :icon="Refresh" @click="refreshAll">Refresh</el-button>
              <el-button :icon="VideoPlay" type="primary" @click="startTask">Start</el-button>
              <el-button :icon="SwitchButton" type="danger" @click="stopTask">Stop</el-button>
            </div>
          </el-card>

          <el-card class="panel">
            <template #header>Configs</template>
            <el-select v-model="selectedConfig" filterable class="full" @change="loadConfigDetail">
              <el-option v-for="item in configNames" :key="item" :label="item" :value="item" />
            </el-select>
            <el-input v-model="configNameDraft" class="spaced" placeholder="New config name" />
            <div class="toolbar">
              <el-button :icon="DocumentAdd" @click="createConfig">Create</el-button>
              <el-button :icon="Refresh" @click="loadConfigs">Reload</el-button>
            </div>
          </el-card>
        </el-col>

        <el-col :xs="24" :lg="16">
          <el-card class="panel">
            <template #header>Editor</template>
            <el-tabs v-model="editorTab">
              <el-tab-pane label="Config" name="config">
                <el-input
                  v-model="configText"
                  type="textarea"
                  :rows="18"
                  resize="none"
                  placeholder="Select a config, edit JSON, then save"
                />
                <div class="toolbar">
                  <el-button :icon="Refresh" @click="loadConfigDetail">Reload</el-button>
                  <el-button :icon="Check" type="primary" @click="saveConfig">Save Config</el-button>
                </div>
              </el-tab-pane>

              <el-tab-pane label="Settings" name="settings">
                <el-input
                  v-model="settingsText"
                  type="textarea"
                  :rows="18"
                  resize="none"
                  placeholder="Load settings, edit JSON, then save"
                />
                <div class="toolbar">
                  <el-button :icon="Operation" @click="loadSettings">Load Settings</el-button>
                  <el-button :icon="Files" type="primary" @click="saveSettings">Save Settings</el-button>
                </div>
              </el-tab-pane>
            </el-tabs>
          </el-card>

          <el-card class="panel">
            <template #header>Logs</template>
            <div class="log-toolbar">
              <el-input-number v-model="logCount" :min="10" :max="1000" :step="10" />
              <el-button :icon="Refresh" @click="loadLogs">Reload Logs</el-button>
              <el-switch v-model="streaming" active-text="Stream" inactive-text="Off" @change="toggleStream" />
            </div>
            <el-scrollbar height="320px">
              <pre class="logs">{{ logs.join('\n') || 'No logs yet' }}</pre>
            </el-scrollbar>
          </el-card>
        </el-col>
      </el-row>
    </el-main>
  </el-container>
</template>

<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'
import { Check, DocumentAdd, Files, Operation, Refresh, SwitchButton, VideoPlay } from '@element-plus/icons-vue'

const health = ref({ ok: false })
const sraStatus = ref<{ running: boolean; pid?: number | null; executablePath?: string; port?: number; detail?: string }>({ running: false })
const configNames = ref<string[]>([])
const selectedConfig = ref('')
const configNameDraft = ref('')
const configText = ref('')
const settingsText = ref('')
const editorTab = ref<'config' | 'settings'>('config')
const logs = ref<string[]>([])
const logCount = ref(100)
const streaming = ref(true)
let eventSource: EventSource | null = null

const healthLabel = computed(() => (health.value.ok ? 'Backend Online' : 'Backend Offline'))
const healthTag = computed(() => (health.value.ok ? 'success' : 'danger'))
const runningLabel = computed(() => (sraStatus.value.running ? 'Task Running' : 'Task Stopped'))
const runningTag = computed(() => (sraStatus.value.running ? 'success' : 'info'))

async function api<T>(url: string, init?: RequestInit): Promise<T> {
  const res = await fetch(url, {
    headers: { 'Content-Type': 'application/json' },
    ...init
  })
  if (!res.ok) throw new Error(`${res.status} ${res.statusText}`)
  return await res.json() as T
}

async function refreshAll() {
  await Promise.allSettled([loadStatus(), loadConfigs(), loadLogs()])
  if (selectedConfig.value) await loadConfigDetail()
}

async function loadStatus() {
  sraStatus.value.running = await api<boolean>('/Task/status')
  health.value.ok = true
}

async function loadConfigs() {
  const payload = await api<string[]>('/Configs')
  configNames.value = payload
  if (!selectedConfig.value && payload.length) selectedConfig.value = payload[0]
}

async function loadConfigDetail() {
  if (!selectedConfig.value) return
  const payload = await api<unknown>(`/Configs/${encodeURIComponent(selectedConfig.value)}`)
  configText.value = JSON.stringify(payload, null, 2)
}

async function saveConfig() {
  if (!selectedConfig.value) return
  const body = JSON.parse(configText.value)
  await api(`/Configs/${encodeURIComponent(selectedConfig.value)}`, {
    method: 'PUT',
    body: JSON.stringify(body)
  })
  await refreshAll()
}

async function loadSettings() {
  const payload = await api<unknown>('/Settings')
  settingsText.value = JSON.stringify(payload, null, 2)
  configText.value = settingsText.value
}

async function saveSettings() {
  const source = configText.value || settingsText.value || '{}'
  const body = JSON.parse(source)
  settingsText.value = source
  await api('/Settings', {
    method: 'PUT',
    body: JSON.stringify(body)
  })
  await refreshAll()
}

async function startTask() {
  await api('/Task/run', {
    method: 'POST',
    body: JSON.stringify({ configName: selectedConfig.value || null })
  })
  await loadStatus()
}

async function stopTask() {
  await api('/Task/stop', {
    method: 'POST',
    body: '{}'
  })
  await loadStatus()
}

async function loadLogs() {
  const payload = await api<string[]>('/Task/logs?count=' + logCount.value)
  logs.value = payload
}

async function createConfig() {
  if (!configNameDraft.value.trim()) return
  await api(`/Configs/${encodeURIComponent(configNameDraft.value.trim())}`, { method: 'POST' })
  selectedConfig.value = configNameDraft.value.trim()
  configNameDraft.value = ''
  await loadConfigs()
  await loadConfigDetail()
}

function toggleStream(enabled: boolean) {
  eventSource?.close()
  eventSource = null
  if (!enabled) return

  eventSource = new EventSource('/Task/logs/stream')
  eventSource.onmessage = (event) => {
    logs.value.push(event.data)
    if (logs.value.length > 500) logs.value.splice(0, logs.value.length - 500)
  }
}

onMounted(async () => {
  await refreshAll()
  toggleStream(streaming.value)
})

onUnmounted(() => {
  eventSource?.close()
})
</script>

<style scoped>
.shell { min-height: 100vh; background: #f3f6fb; color: #1f2937; }
.topbar { display: flex; justify-content: space-between; align-items: flex-end; gap: 16px; padding: 20px 24px; background: #fff; border-bottom: 1px solid #e5e7eb; }
.title-block h1 { margin: 4px 0 6px; font-size: 22px; }
.title-block p, .eyebrow { margin: 0; color: #6b7280; }
.eyebrow { font-size: 12px; text-transform: uppercase; letter-spacing: 0; }
.main { padding: 16px; }
.panel { margin-bottom: 16px; border-radius: 8px; }
.full, .spaced { width: 100%; margin-top: 10px; }
.toolbar, .log-toolbar { display: flex; flex-wrap: wrap; gap: 8px; margin-top: 12px; }
.logs { margin: 0; white-space: pre-wrap; word-break: break-word; font: 13px/1.5 Consolas, monospace; }
</style>
