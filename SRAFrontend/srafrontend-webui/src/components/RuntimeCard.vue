<template>
  <section class="panel control-panel">
    <div class="panel-title">
      <span>运行</span>
      <el-button :icon="Refresh" circle text @click="$emit('refresh')" />
    </div>
    <div class="control-actions">
      <el-button :icon="VideoPlay" type="primary" size="large" @click="$emit('start')">启动任务</el-button>
      <el-button :icon="SwitchButton" type="danger" plain size="large" @click="$emit('stop')">停止</el-button>
    </div>
    <div class="status-list">
      <div>
        <span>WebUI</span>
        <strong>{{ healthOk ? '在线' : '离线' }}</strong>
      </div>
      <div>
        <span>SRA</span>
        <strong>{{ status.running ? '运行中' : '未运行' }}</strong>
      </div>
      <div>
        <span>端口</span>
        <strong>{{ status.port ?? '-' }}</strong>
      </div>
      <div>
        <span>PID</span>
        <strong>{{ status.pid ?? '-' }}</strong>
      </div>
      <div>
        <span>模式</span>
        <strong>{{ status.mode || '-' }}</strong>
      </div>
      <div>
        <span>配置</span>
        <strong>{{ configLabel }}</strong>
      </div>
      <div>
        <span>任务</span>
        <strong>{{ taskLabel }}</strong>
      </div>
      <div>
        <span>状态</span>
        <strong>{{ stateLabel }}</strong>
      </div>
    </div>
    <div class="path-line" :title="status.executablePath || '-'">
      {{ status.executablePath || '等待后端状态' }}
    </div>
  </section>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { Refresh, SwitchButton, VideoPlay } from '@element-plus/icons-vue'
import type { SraStatus } from '@/types'

const props = defineProps<{
  status: SraStatus
  healthOk: boolean
}>()

defineEmits<{
  refresh: []
  start: []
  stop: []
}>()

const configLabel = computed(() => {
  const configs = props.status.configNames?.length ? props.status.configNames : props.status.configs
  return configs?.length ? configs.join(', ') : '-'
})

const taskLabel = computed(() => props.status.taskName || props.status.task || '-')
const stateLabel = computed(() => props.status.state || props.status.status || (props.status.running ? 'running' : 'idle'))
</script>
