<script setup lang="ts">
  import { computed, onMounted, onUnmounted, ref } from 'vue'
  import type { BalanceResponse } from '../model/BalanceResponse'

  const props = defineProps<{
    localUrl: string
  }>()

  const userBalance = ref<BalanceResponse | null>(null)
  let balanceInterval: ReturnType<typeof setInterval> | null = null

  const balanceDisplay = computed(() => {
    if (!userBalance.value || userBalance.value.balance_infos.length === 0)
      return 'Loading...'

    const info = userBalance.value.balance_infos[0]
    return `${info?.total_balance ?? 0.0} ${info?.currency ?? ''}`
  })

  onMounted(() => {
    fetchUserBalance()
    balanceInterval = setInterval(fetchUserBalance, 60000)
  })

  onUnmounted(() => {
    if (balanceInterval) {
      clearInterval(balanceInterval)
      balanceInterval = null
    }
  })

  async function fetchUserBalance() {
    try {
      const response = await fetch(props.localUrl + 'user/balance')
      const data: BalanceResponse = await response.json()
      userBalance.value = data
    } catch (error) {
      console.error('Failed to fetch balance:', error)
    }
  }</script>

<template>
  <span>{{ balanceDisplay }}</span>
</template>
