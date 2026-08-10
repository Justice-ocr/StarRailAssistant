<template>
  <section class="page-wrap">
    <section class="panel logs-panel logs-page-panel">
      <div class="panel-title logs-title">
        <span>日志</span>
        <div class="log-tools">
          <el-input-number v-model="app.logCount" :min="10" :max="1000" :step="10" controls-position="right" />
          <el-switch v-model="app.streaming" active-text="实时" inactive-text="关闭" @change="toggleStream" />
          <el-button :icon="Tickets" @click="app.loadLogs()">刷新</el-button>
        </div>
      </div>
      <el-scrollbar ref="scrollbar" class="log-scroll large">
        <pre class="logs">{{ app.logs.join('\n') || '暂无日志' }}</pre>
      </el-scrollbar>
    </section>
  </section>
</template>

<script setup lang="ts">
import { nextTick, onMounted, onUnmounted, ref, watch } from 'vue'
import type { ScrollbarInstance } from 'element-plus'
import { Tickets } from '@element-plus/icons-vue'
import { useAppStore } from '@/stores/app'
import { baseURL } from '@/api/request'

const app = useAppStore()
const scrollbar = ref<ScrollbarInstance>()

let streamController: AbortController | null = null

async function scrollLogsToBottom() {
  await nextTick()
  scrollbar.value?.setScrollTop(999999)
}

function closeStream() {
  streamController?.abort()
  streamController = null
}

async function toggleStream() {
  closeStream()
  if (!app.streaming) return

  const controller = new AbortController()
  streamController = controller
  try {
    const response = await fetch(`${baseURL}/Task/logs/stream`, {
      headers: {
        Accept: 'text/event-stream',
        'X-Api-Key': app.token
      },
      signal: controller.signal
    })
    if (response.status === 401) app.logout()
    if (!response.ok || !response.body) throw new Error(`日志流连接失败 (${response.status})`)

    const reader = response.body.getReader()
    const decoder = new TextDecoder()
    let buffer = ''
    while (!controller.signal.aborted) {
      const { done, value } = await reader.read()
      if (done) break
      buffer += decoder.decode(value, { stream: true }).replace(/\r/g, '')
      let boundary = buffer.indexOf('\n\n')
      while (boundary >= 0) {
        const event = buffer.slice(0, boundary)
        buffer = buffer.slice(boundary + 2)
        const data = event
          .split('\n')
          .filter((line) => line.startsWith('data:'))
          .map((line) => line.slice(5).trimStart())
          .join('\n')
        if (data) {
          app.logs.push(data)
          if (app.logs.length > 600) app.logs.splice(0, app.logs.length - 600)
          await scrollLogsToBottom()
        }
        boundary = buffer.indexOf('\n\n')
      }
    }
  } catch (error) {
    if (error instanceof DOMException && error.name === 'AbortError') return
    app.streaming = false
    closeStream()
  }
}

watch(() => app.logs, () => scrollLogsToBottom(), { deep: true })

onMounted(async () => {
  await app.loadLogs()
  if (app.streaming) toggleStream()
})

onUnmounted(closeStream)
</script>
