<script setup lang="ts">
  import { ref, computed, onMounted, onUnmounted } from 'vue';
  import { useRouter } from 'vue-router'

  import { formatDate } from '@/utils/dateUtils';

  const router = useRouter()
  const now = new Date();
  const weekAgo = new Date(now);
  weekAgo.setDate(weekAgo.getDate() - 7);

  const dateFrom = ref<string>(formatDate(weekAgo));
  const dateTo = ref<string>(formatDate(now));
  const currentPage = ref(0);
  const amount = ref<number>(50);

  const logsResponse = ref<ApiLogsResponse | null>(null)

  interface ApiLogsResponse {
    totalCount: number
    logs: Array<ApiLogsResponseEntry>
    currentPage: number
    totalPages: number
    pageSize: number
  }

  interface ApiLogsResponseEntry {
    Id: number
    Method: string
    Path: string
    StatusCode: number
    ElapsedMs: number
    IsSuccess: boolean
    ErrorMessage: string | null
    Timestamp: Date
  }

  const hasPrev = computed(() => currentPage.value > 0);
  const hasNext = computed(() => logsResponse.value ? currentPage.value < logsResponse.value.totalPages - 1 : false);


  const tableHeader = [
    'Id',
    'Method',
    'Path',
    'StatusCode',
    'ElapsedMs',
    'IsSuccess',
    'ErrorMessage',
    'Timestamp',
  ];

  onMounted(async () => {
    await fetchLogs();
  });


  function buildUrl() {
    return `http://localhost:5000/api/requests/logs?from=${dateFrom.value}&to=${dateTo.value}&page=${currentPage.value}&amount=${amount.value}`;
  }

  function prevPage() {
    if (!hasPrev.value) return;
    currentPage.value--;
    fetchLogs();
  }

  function nextPage() {
    if (!hasNext.value) return;
    currentPage.value++;
    fetchLogs();
  }

  // Reset page when applying new filters
  function applyFilters() {
    currentPage.value = 0;
    fetchLogs();
  }

  async function fetchLogs() {
    try {
      const response = await fetch(buildUrl());
      const json = await response.json();
      const d = json.data;

      logsResponse.value = d ?? null

      console.log(logsResponse.value);

    } catch {
      logsResponse.value = null;
    }
  }

</script>
<template>
  <div class="space-y-6">
    <h2 class="text-2xl font-bold">Logs</h2>

    <!-- Filters -->
    <div class="rounded-lg border border-gray-700 bg-gray-800 p-4">
      <div class="flex flex-wrap items-end gap-4">
        <div class="flex flex-col gap-1">
          <label class="text-xs text-gray-400 font-medium uppercase tracking-wide" for="dateFrom">From</label>
          <input id="dateFrom" v-model="dateFrom" type="date"
                 class="rounded-md border border-gray-600 bg-gray-700 px-3 py-2 text-sm text-gray-200
                        focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500
                        [color-scheme:dark]" />
        </div>
        <div class="flex flex-col gap-1">
          <label class="text-xs text-gray-400 font-medium uppercase tracking-wide" for="dateTo">To</label>
          <input id="dateTo" v-model="dateTo" type="date"
                 class="rounded-md border border-gray-600 bg-gray-700 px-3 py-2 text-sm text-gray-200
                        focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500
                        [color-scheme:dark]" />
        </div>
        <!-- Amount -->
        <div class="flex flex-col gap-1">
          <label class="text-xs text-gray-400 font-medium uppercase tracking-wide" for="page">Amount</label>
          <input id="amount" v-model.number="amount" type="number" min="0"
                 class="w-20 rounded-md border border-gray-600 bg-gray-700 px-3 py-2 text-sm text-gray-200
                        focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500
                        [color-scheme:dark]" />
        </div>

        <button @click="fetchLogs"
                class="ml-auto cursor-pointer rounded-md bg-indigo-600 px-4 py-2 text-sm font-semibold text-white
                       hover:bg-indigo-500 focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:ring-offset-2 focus:ring-offset-gray-800
                       transition-colors">
          Apply
        </button>
      </div>
    </div>

    <div v-if="logsResponse" class="rounded-lg border border-gray-700 bg-gray-800 p-4 overflow-x-auto">
      <table class="w-full text-sm text-left">
        <thead>
          <tr class="text-gray-400 border-b border-gray-700">
            <th v-for="head in tableHeader" :key="head" class="px-4 py-3 font-medium whitespace-nowrap">
              {{ head }}
            </th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="log in logsResponse.logs" :key="log.Id" @click="router.push(`/logs/${log.Id}`)" class="cursor-pointer border-b border-gray-700 hover:bg-gray-700/50 transition-colors">
            <td class="px-4 py-3 text-gray-300">{{ log.Id }}</td>
            <td class="px-4 py-3 font-mono text-indigo-400">{{ log.Method }}</td>
            <td class="px-4 py-3 font-mono text-gray-300 max-w-xs truncate">{{ log.Path }}</td>
            <td class="px-4 py-3">
              <span class="px-2 py-0.5 rounded text-xs font-medium"
                    :class="log.StatusCode >= 400 ? 'bg-red-900/60 text-red-400' : 'bg-green-900/60 text-green-400'">
                {{ log.StatusCode }}
              </span>
            </td>
            <td class="px-4 py-3 text-gray-300">{{ log.ElapsedMs }}</td>
            <td class="px-4 py-3">{{ log.IsSuccess ? '✅' : '❌' }}</td>
            <td class="px-4 py-3 text-red-400 max-w-xs truncate">{{ log.ErrorMessage ?? '—' }}</td>
            <td class="px-4 py-3 text-gray-400 whitespace-nowrap">{{ log.Timestamp }}</td>
          </tr>
        </tbody>
      </table>
      <!-- Navigation -->
      <div class="mt-4 flex items-center justify-between text-sm text-gray-500">
        <span>Total entries: {{ logsResponse.totalCount }}</span>
        <div class="flex gap-2">
          <button @click="prevPage" :disabled="!hasPrev"
                  class="cursor-pointer rounded-md bg-gray-700 px-3 py-1 text-xs font-medium text-gray-300
                         hover:bg-gray-600 disabled:opacity-40 disabled:cursor-not-allowed transition-colors">
            ← Prev
          </button>
          <button @click="nextPage" :disabled="!hasNext"
                  class="cursor-pointer rounded-md bg-gray-700 px-3 py-1 text-xs font-medium text-gray-300
                         hover:bg-gray-600 disabled:opacity-40 disabled:cursor-not-allowed transition-colors">
            Next →
          </button>
        </div>
      </div>
    </div>

    <div v-else class="rounded-lg border border-dashed border-gray-600 p-12 text-center">
      <p class="text-gray-400">Loading logs…</p>
    </div>
  </div>
</template>
