<script setup lang="ts">
  import { ref } from 'vue'
  import BalanceDisplay from '@/components/BalanceDisplay.vue'

  const isRunning = ref(true)
  const localUrl = "http://localhost:5000/"; // improve this later

  async function toggleProxy() {
    isRunning.value = !isRunning.value
    if (isRunning.value) {
      await fetch(localUrl + 'start')
    } else {
      await fetch(localUrl + 'stop')
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
            <span class="rounded-md px-3 py-2 text-sm font-medium transition-colors hover:bg-gray-700 hover:text-white">
              Balance
              <BalanceDisplay :local-url="localUrl" />
            </span>
            <router-link to="/statistics" class="cursor-pointer rounded-md px-3 py-2 text-sm font-medium transition-colors hover:bg-gray-700 hover:text-white">
              Statistics
            </router-link>
            <router-link to="/logs" class="cursor-pointer rounded-md px-3 py-2 text-sm font-medium transition-colors hover:bg-gray-700 hover:text-white">
              Logs
            </router-link>
            <router-link to="/settings" class="cursor-pointer rounded-md px-3 py-2 text-sm font-medium transition-colors hover:bg-gray-700 hover:text-white">
              Settings
            </router-link>
          </div>

          <!-- Start / Stop Toggle -->
          <div class="flex items-center gap-3">
            <span class="hidden text-sm sm:inline" :class="isRunning ? 'text-green-400' : 'text-gray-400'">
              {{ isRunning ? 'Running' : 'Stopped' }}
            </span>
            <button @click="toggleProxy"
                    class="cursor-pointer rounded-full px-5 py-2 text-sm font-semibold transition-all focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-offset-gray-800"
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
        <span class="rounded-md px-3 py-2 text-sm font-medium transition-colors hover:bg-gray-700 hover:text-white">
          Balance
          <BalanceDisplay :local-url="localUrl" />
        </span>
        <router-link to="/statistics" class="cursor-pointer rounded-md px-3 py-2 text-sm font-medium transition-colors hover:bg-gray-700 hover:text-white">
          Statistics
        </router-link>
        <router-link to="/logs" class="cursor-pointer rounded-md px-3 py-2 text-sm font-medium transition-colors hover:bg-gray-700 hover:text-white">
          Logs
        </router-link>
        <router-link to="/settings" class="cursor-pointer rounded-md px-3 py-2 text-sm font-medium transition-colors hover:bg-gray-700 hover:text-white">
          Settings
        </router-link>
      </div>
    </div>

    <!-- Main Content Area -->
    <main>
      <div class="mx-auto max-w-7xl px-4 py-8 sm:px-6 lg:px-8">
        <router-view />
        <!--
        <div class="rounded-lg border border-dashed border-gray-600 p-12 text-center">
          <p class="text-gray-400">Select a menu option to view details</p>
        </div>
          -->
      </div>
    </main>
  </div>
</template>
