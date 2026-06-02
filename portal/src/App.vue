<script setup lang="ts">
  import { computed, onMounted, ref } from 'vue'
  import type { BalanceResponse } from './model/BalanceResponse'

  const isRunning = ref(true)
  const localUrl = "http://localhost:5000/"; // improve this later
  const userBalance = ref<BalanceResponse | null>(null);
  let balanceInterval: ReturnType<typeof setInterval> | null = null;


  const balanceDisplay = computed(() => {
    if (!userBalance.value || userBalance.value.balance_infos.length === 0)
        return 'Loading...';
    
    const info = userBalance.value.balance_infos[0];
    return `${info?.total_balance ?? 0.0} ${info?.currency ?? ''}`;
  });

  onMounted(() => {
    if (balanceInterval === null) {
      fetchUserBalance();
      balanceInterval = setInterval(fetchUserBalance, 60000);
    }

  });

  async function toggleProxy() {
    isRunning.value = !isRunning.value
    if (isRunning.value) {
      const response = await fetch(localUrl + 'start');
    } else {
      const response = await fetch(localUrl + 'stop');
    }
  }

  async function fetchUserBalance() {
    try {
      const response = await fetch(localUrl + 'user/balance');
      const data: BalanceResponse = await response.json();
      userBalance.value = data;
    } catch (error) {
      console.error('Failed to fetch balance:', error);
    }
  }
</script>

<template>
  <div class="min-h-screen bg-gray-900 text-gray-100">
    <!-- Navigation -->
    <nav class="border-b border-gray-700 bg-gray-800">
      <div class="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
        <div class="flex h-16 items-center justify-between">
          <!-- Logo / Brand -->
          <div class="flex items-center gap-3">
            <div class="flex h-8 w-8 items-center justify-center rounded-lg bg-indigo-500 text-sm font-bold">
              P
            </div>
            <span class="text-lg font-semibold">DeepSeek Proxy</span>
          </div>

          <!-- Desktop Menu -->
          <div class="hidden items-center gap-6 md:flex">
            <button class="rounded-md px-3 py-2 text-sm font-medium transition-colors hover:bg-gray-700 hover:text-white">
              Balance
              <span>{{ balanceDisplay }}</span>
            </button>
            <button class="rounded-md px-3 py-2 text-sm font-medium transition-colors hover:bg-gray-700 hover:text-white">
              Logs
            </button>
            <button class="rounded-md px-3 py-2 text-sm font-medium transition-colors hover:bg-gray-700 hover:text-white">
              Settings
            </button>
          </div>

          <!-- Start / Stop Toggle -->
          <div class="flex items-center gap-3">
            <span class="hidden text-sm sm:inline" :class="isRunning ? 'text-green-400' : 'text-gray-400'">
              {{ isRunning ? 'Running' : 'Stopped' }}
            </span>
            <button @click="toggleProxy"
                    class="rounded-full px-5 py-2 text-sm font-semibold transition-all focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-offset-gray-800"
                    :class="
                isRunning
                  ? 'bg-red-600 text-white hover:bg-red-500 focus:ring-red-500'
                  : 'bg-green-600 text-white hover:bg-green-500 focus:ring-green-500'
              ">
              {{ isRunning ? 'Stop' : 'Start' }}
            </button>
          </div>
        </div>
      </div>
    </nav>

    <!-- Mobile Menu (visible below md) -->
    <div class="border-b border-gray-700 bg-gray-800 px-4 py-3 md:hidden">
      <div class="flex items-center justify-center gap-4">
        <button class="rounded-md px-3 py-2 text-sm font-medium transition-colors hover:bg-gray-700 hover:text-white">
          Balance
        </button>
        <button class="rounded-md px-3 py-2 text-sm font-medium transition-colors hover:bg-gray-700 hover:text-white">
          Status
        </button>
        <button class="rounded-md px-3 py-2 text-sm font-medium transition-colors hover:bg-gray-700 hover:text-white">
          Logs
        </button>
      </div>
    </div>

    <!-- Main Content Area -->
    <main>
      <div class="mx-auto max-w-7xl px-4 py-8 sm:px-6 lg:px-8">
        <div class="rounded-lg border border-dashed border-gray-600 p-12 text-center">
          <p class="text-gray-400">Select a menu option to view details</p>
        </div>
      </div>
    </main>
  </div>
</template>
