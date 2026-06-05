<script setup lang="ts">
  import { ref, onMounted, watch } from 'vue'

  const apiKey = ref('')
  const baseUrl = ref('https://api.deepseek.com')
  const defaultModel = ref('')
  const port = ref(5000)
  const allowedModels = ref<string[]>([])
  const availableModels = ref<string[]>([])
  const balanceRefreshIntervalSec = ref<number>(60);
  const saving = ref(false)
  const loadingModels = ref(true)
  const saved = ref(false)
  const validationError = ref('')

  interface ModelItem {
    id: string
  }

  onMounted(async () => {
    await Promise.all([fetchSettings(), fetchModels()])
  })

  async function fetchModels() {
    try {
      const response = await fetch('http://localhost:5000/models')
      if (!response.ok) throw new Error('Failed')
      const json = await response.json()
      availableModels.value = (json.data ?? []).map((m: ModelItem) => m.id)
    } catch {
      // Fallback — let user type manually via a custom model input
      availableModels.value = []
    } finally {
      loadingModels.value = false
    }
  }

  async function fetchSettings() {
    try {
      const response = await fetch('http://localhost:5000/api/settings')
      const json = await response.json()
      if (json.status === 1 && json.data) {
        apiKey.value = json.data.apiKey ?? ''
        baseUrl.value = json.data.baseUrl ?? 'https://api.deepseek.com'
        defaultModel.value = json.data.model ?? ''
        port.value = json.data.port ?? 5000
        allowedModels.value = json.data.allowedModels ?? []
      }
    } catch { /* ignore */ }
  }

  const toggleAllowed = (model: string) => {
    const idx = allowedModels.value.indexOf(model)
    if (idx >= 0) {
      if (allowedModels.value.length <= 1) return
      allowedModels.value.splice(idx, 1)
      if (defaultModel.value === model) {
        defaultModel.value = allowedModels.value[0]
      }
    } else {
      allowedModels.value.push(model)
    }
  }

  watch(defaultModel, (newModel) => {
    if (newModel && !allowedModels.value.includes(newModel)) {
      allowedModels.value.push(newModel)
    }
  })

  async function saveSettings() {
    validationError.value = ''
    saving.value = true
    saved.value = false

    // Client-side validation
    if (allowedModels.value.length === 0) {
      validationError.value = 'At least one model must be allowed.'
      saving.value = false
      return
    }
    if (!allowedModels.value.includes(defaultModel.value)) {
      validationError.value = 'Default model must be in the allowed models list.'
      saving.value = false
      return
    }

    try {
      const response = await fetch('http://localhost:5000/api/settings/save', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          apiKey: apiKey.value,
          baseUrl: baseUrl.value,
          model: defaultModel.value,
          port: port.value,
          allowedModels: allowedModels.value,
        }),
      })
      const json = await response.json()
      if (json.status === 1) {
        saved.value = true
        setTimeout(() => saved.value = false, 3000)
      } else {
        validationError.value = json.error ?? 'Failed to save.'
      }
    } catch {
      validationError.value = 'Network error.'
    } finally {
      saving.value = false
    }
  }
</script>

<template>
  <div class="space-y-6">
    <h2 class="text-2xl font-bold">Settings</h2>

    <div class="rounded-lg border border-gray-700 bg-gray-800 p-6 space-y-5">
      <!-- API Key -->
      <div class="flex flex-col gap-1">
        <label class="text-xs text-gray-400 font-medium uppercase tracking-wide" for="apiKey">API Key</label>
        <input id="apiKey" v-model="apiKey" type="password" placeholder="sk-..."
               class="rounded-md border border-gray-600 bg-gray-700 px-3 py-2 text-sm text-gray-200 font-mono
                      focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500 [color-scheme:dark]" />
      </div>

      <!-- Base URL -->
      <div class="flex flex-col gap-1">
        <label class="text-xs text-gray-400 font-medium uppercase tracking-wide" for="baseUrl">Base URL</label>
        <input id="baseUrl" v-model="baseUrl" type="text"
               class="rounded-md border border-gray-600 bg-gray-700 px-3 py-2 text-sm text-gray-200 font-mono
                      focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500 [color-scheme:dark]" />
      </div>

      <!-- Port -->
      <div class="flex flex-col gap-1">
        <label class="text-xs text-gray-400 font-medium uppercase tracking-wide" for="port">Port</label>
        <input id="port" v-model.number="port" type="number" min="1024" max="65535"
               class="w-28 rounded-md border border-gray-600 bg-gray-700 px-3 py-2 text-sm text-gray-200 font-mono
                      focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500 [color-scheme:dark]" />
      </div>

      <!-- Default Model -->
      <div class="flex flex-col gap-1">
        <label class="text-xs text-gray-400 font-medium uppercase tracking-wide" for="defaultModel">Default Model</label>
        <select id="defaultModel" v-model="defaultModel"
                class="rounded-md border border-gray-600 bg-gray-700 px-3 py-2 text-sm text-gray-200
                       focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500">
          <option v-if="loadingModels" disabled value="">Loading models…</option>
          <option v-else-if="availableModels.length === 0" disabled value="">No models available</option>
          <option v-for="m in availableModels" :key="m" :value="m">{{ m }}</option>
        </select>
      </div>

      <!-- Allowed Models -->
      <div class="flex flex-col gap-2">
        <span class="text-xs text-gray-400 font-medium uppercase tracking-wide">Allowed Models</span>
        <p class="text-xs text-gray-500">At least one must be selected. The default model is always included.</p>
        <div v-if="loadingModels" class="text-sm text-gray-400">Loading…</div>
        <div v-else class="flex flex-wrap gap-3">
          <label v-for="m in availableModels" :key="m"
                 class="flex items-center gap-2 cursor-pointer rounded-md border px-3 py-2 text-sm transition-colors"
                 :class="allowedModels.includes(m)
                   ? 'border-indigo-500 bg-indigo-900/30 text-indigo-300'
                   : 'border-gray-600 text-gray-400 hover:border-gray-500'">
            <input type="checkbox" :checked="allowedModels.includes(m)"
                   @change="toggleAllowed(m)"
                   class="accent-indigo-500" />
            {{ m }}
          </label>
        </div>
        <p v-if="availableModels.length === 0 && !loadingModels" class="text-xs text-gray-500">
          Could not fetch models. Set your API key and save first, then refresh.
        </p>
      </div>

      <h3>Request trimming</h3>

      <!-- Max Messages -->
      <div class="flex flex-col gap-1">
        <label class="text-xs text-gray-400 font-medium uppercase tracking-wide" for="maxMessages">
          Max Messages
        </label>
        <select id="maxMessages" v-model.number="maxMessages"
                class="rounded-md border border-gray-600 bg-gray-700 px-3 py-2 text-sm text-gray-200
                       focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500">
          <option :value="5">5</option>
          <option :value="10">10</option>
          <option :value="15">15</option>
          <option :value="20">20</option>
          <option :value="50">50</option>
          <option :value="100">100</option>
          <option :value="0">All</option>
        </select>
        <p class="text-xs text-gray-500">
          {{
            maxMessages === 0
            ? 'Send the full conversation history to DeepSeek.'
            : `Only keep the last ${maxMessages} messages when forwarding to DeepSeek.`
          }}
        </p>
        <p class="text-xs text-gray-500">
          Lower values reduce token usage and cost, but may lose context from earlier in the conversation.
          <span class="text-indigo-400">{{ maxMessages === 0 ? 'All' : maxMessages >= 20 ? maxMessages : maxMessages === 5 ? '5 is not recommended for longer conversations.' : `${maxMessages} is a good balance.` }}</span>
          Default is <span class="text-indigo-400">All</span>.
        </p>
      </div>

      <!-- Modify Copilot start message -->

      <!--  -->

      <h3>Web interface</h3>

      <!-- Balance Refresh Interval -->
      <div class="flex flex-col gap-1">
        <label class="text-xs text-gray-400 font-medium uppercase tracking-wide" for="refreshInterval">
          Balance Refresh Interval
        </label>
        <div class="flex items-center gap-2">
          <input id="refreshInterval" v-model.number="balanceRefreshIntervalSec" type="number" min="60" max="3600"
                 class="w-28 rounded-md border border-gray-600 bg-gray-700 px-3 py-2 text-sm text-gray-200 font-mono
                        focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500 [color-scheme:dark]" />
          <span class="text-xs text-gray-500">sec</span>
        </div>
      </div>

      <!-- Validation error -->
      <p v-if="validationError" class="text-sm text-red-400">{{ validationError }}</p>

      <!-- Save -->
      <div class="flex items-center gap-3 pt-2">
        <button @click="saveSettings" :disabled="saving"
                class="cursor-pointer rounded-md bg-indigo-600 px-5 py-2 text-sm font-semibold text-white
                       hover:bg-indigo-500 focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:ring-offset-2 focus:ring-offset-gray-800
                       disabled:opacity-50 disabled:cursor-not-allowed transition-colors">
          {{ saving ? 'Saving…' : 'Save' }}
        </button>
        <span v-if="saved" class="text-sm text-green-400">✓ Saved</span>
      </div>
    </div>
  </div>
</template>
