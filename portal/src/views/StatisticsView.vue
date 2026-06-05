<script setup lang="ts">
  import { ref, onMounted, onUnmounted } from 'vue'
  import { Doughnut, Bar } from 'vue-chartjs'
  import {
    Chart as ChartJS,
    ArcElement,
    Tooltip,
    Legend,
    CategoryScale,
    LinearScale,
    BarElement,
  } from 'chart.js'

  ChartJS.register(ArcElement, Tooltip, Legend, CategoryScale, LinearScale, BarElement)

  interface StatsData {
    totalRequests: number
    successfulRequests: number
    failedRequests: number
    averageElapsedMs: number
  }

  const stats = ref<StatsData | null>(null)
  let refreshTimer: number | null = null

  const doughnutData = {
    labels: ['Successful', 'Failed'],
    datasets: [
      {
        backgroundColor: ['#22c55e', '#ef4444'],
        data: [0, 0],
      },
    ],
  }

  const doughnutOptions = {
    responsive: true,
    maintainAspectRatio: false,
  }

  const barData = {
    labels: ['Requests'],
    datasets: [
      {
        label: 'Total',
        backgroundColor: '#3b82f6',
        data: [0],
      },
      {
        label: 'Successful',
        backgroundColor: '#22c55e',
        data: [0],
      },
      {
        label: 'Failed',
        backgroundColor: '#ef4444',
        data: [0],
      },
    ],
  }

  const barOptions = {
    responsive: true,
    maintainAspectRatio: false,
  }

  onMounted(async () => {
    await fetchStats();
    setInterval(fetchStats, 60_000);
  })

  onUnmounted(() => {
    if (refreshTimer) clearInterval(refreshTimer)
  })

  async function fetchStats() {
    const response = await fetch('http://localhost:5000/api/requests/stats')
    const json = await response.json()
    stats.value = json.data
    doughnutData.datasets[0].data = [
      json.data.successfulRequests,
      json.data.failedRequests,
    ]
    barData.datasets[0].data = [json.data.totalRequests]
    barData.datasets[1].data = [json.data.successfulRequests]
    barData.datasets[2].data = [json.data.failedRequests]
  }


</script>

<template>
  <div class="space-y-6">
    <h2 class="text-2xl font-bold">Statistics</h2>

    <div v-if="stats" class="grid grid-cols-1 gap-6 md:grid-cols-2">
      <!-- Doughnut: success vs failure -->
      <div class="rounded-lg border border-gray-700 bg-gray-800 p-4 h-96">
        <h3 class="mb-2 font-semibold">Request Success Rate</h3>
        <div class="h-80">
          <Doughnut :data="doughnutData" :options="doughnutOptions" aria-label="Request success rate chart: successful vs failed requests" role="img" />
        </div>
      </div>

      <!-- Bar chart: request counts -->
      <div class="rounded-lg border border-gray-700 bg-gray-800 p-4 h-96">
        <h3 class="mb-2 font-semibold">Request Counts</h3>
        <div class="h-80">
          <Bar :data="barData" :options="barOptions" aria-label="Request counts chart: total, successful, and failed requests" role="img" />
        </div>
      </div>

      <!-- Summary cards -->
      <div class="rounded-lg border border-gray-700 bg-gray-800 p-4 md:col-span-2">
        <h3 class="mb-3 font-semibold">Summary</h3>
        <div class="grid grid-cols-2 gap-4 md:grid-cols-4">
          <div class="rounded bg-gray-700 p-3 text-center">
            <p class="text-2xl font-bold text-blue-400">{{ stats.totalRequests }}</p>
            <p class="text-sm text-gray-400">Total Requests</p>
          </div>
          <div class="rounded bg-gray-700 p-3 text-center">
            <p class="text-2xl font-bold text-green-400">{{ stats.successfulRequests }}</p>
            <p class="text-sm text-gray-400">Successful</p>
          </div>
          <div class="rounded bg-gray-700 p-3 text-center">
            <p class="text-2xl font-bold text-red-400">{{ stats.failedRequests }}</p>
            <p class="text-sm text-gray-400">Failed</p>
          </div>
          <div class="rounded bg-gray-700 p-3 text-center">
            <p class="text-2xl font-bold text-yellow-400">{{ stats.averageElapsedMs.toFixed(0) }} ms</p>
            <p class="text-sm text-gray-400">Avg Elapsed</p>
          </div>
        </div>
      </div>
    </div>

    <div v-else class="rounded-lg border border-dashed border-gray-600 p-12 text-center">
      <p class="text-gray-400">Loading statistics…</p>
    </div>
  </div>
</template>
