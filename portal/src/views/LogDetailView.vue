<script setup lang="ts">
  import { ref, onMounted } from 'vue'
  import { useRoute, useRouter } from 'vue-router'

  const route = useRoute()
  const router = useRouter()

  const logEntry = ref<ApiLogResponseEntry | null>(null)
  const loading = ref(true)
  const error = ref<string | null>(null)

  interface ApiLogResponseEntry {
    Id: number
    Method: string
    Path: string
    StatusCode: number
    ElapsedMs: number
    IsSuccess: boolean
    ErrorMessage: string | null
    Timestamp: Date
  }

  onMounted(async () => {
    try {
      const id = route.params.id
      const response = await fetch(`http://localhost:5000/api/requests/log?id=${id}`)
      const json = await response.json()

      if (json.status === 1) {
        logEntry.value = json.data
      } else {
        error.value = json.error ?? 'Failed to load log entry.'
      }
    } catch {
      error.value = 'Network error while fetching log entry.'
    } finally {
      loading.value = false
    }
  })
</script>

<template>
  <div class="space-y-6">
    <div class="flex items-center gap-4">
      <button @click="router.back()"
              class="cursor-pointer rounded-md bg-gray-700 px-3 py-1 text-sm text-gray-300 hover:bg-gray-600 transition-colors">
        ← Back
      </button>
      <h2 class="text-2xl font-bold">Log Details</h2>
    </div>

    <div v-if="loading" class="rounded-lg border border-dashed border-gray-600 p-12 text-center">
      <p class="text-gray-400">Loading…</p>
    </div>

    <div v-else-if="error" class="rounded-lg border border-dashed border-red-800 p-12 text-center">
      <p class="text-red-400">{{ error }}</p>
    </div>

    <div v-else-if="logEntry" class="rounded-lg border border-gray-700 bg-gray-800 p-6 space-y-4">
      <div class="grid grid-cols-1 gap-4 md:grid-cols-2">
        <div>
          <p class="text-xs text-gray-500 uppercase tracking-wide">ID</p>
          <p class="font-mono text-sm text-gray-200">{{ logEntry.Id }}</p>
        </div>
        <div>
          <p class="text-xs text-gray-500 uppercase tracking-wide">Method</p>
          <p class="font-mono text-sm text-indigo-400">{{ logEntry.Method }}</p>
        </div>
        <div class="md:col-span-2">
          <p class="text-xs text-gray-500 uppercase tracking-wide">Path</p>
          <p class="font-mono text-sm text-gray-200 break-all">{{ logEntry.Path }}</p>
        </div>
        <div>
          <p class="text-xs text-gray-500 uppercase tracking-wide">Status Code</p>
          <span class="px-2 py-0.5 rounded text-xs font-medium"
                :class="logEntry.StatusCode >= 400 ? 'bg-red-900/60 text-red-400' : 'bg-green-900/60 text-green-400'">
            {{ logEntry.StatusCode }}
          </span>
        </div>
        <div>
          <p class="text-xs text-gray-500 uppercase tracking-wide">Elapsed</p>
          <p class="text-sm text-gray-200">{{ logEntry.ElapsedMs }} ms</p>
        </div>
        <div>
          <p class="text-xs text-gray-500 uppercase tracking-wide">Success</p>
          <p class="text-sm">{{ logEntry.IsSuccess ? '✅ Yes' : '❌ No' }}</p>
        </div>
        <div>
          <p class="text-xs text-gray-500 uppercase tracking-wide">Timestamp</p>
          <p class="text-sm text-gray-200">{{ logEntry.Timestamp }}</p>
        </div>
        <div class="md:col-span-2" v-if="logEntry.ErrorMessage">
          <p class="text-xs text-gray-500 uppercase tracking-wide">Error Message</p>
          <p class="text-sm text-red-400 break-all">{{ logEntry.ErrorMessage }}</p>
        </div>
      </div>
    </div>
  </div>
</template>
