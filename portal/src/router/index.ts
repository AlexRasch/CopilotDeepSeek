import { createRouter, createWebHistory } from 'vue-router'
//import BalanceView from './views/BalanceView.vue'
import StatisticsView from '@/views/StatisticsView.vue'
//import LogsView from './views/LogsView.vue'
//import SettingsView from './views/SettingsView.vue'

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
    {
      path: '/',
      redirect: '/statistics',
    },
    //{
    //  path: '/balance',
    //  name: 'balance',
    //  component: BalanceView,
    //},
    {
      path: '/statistics',
      name: 'statistics',
      component: StatisticsView,
    },
    //{
    //  path: '/logs',
    //  name: 'logs',
    //  component: LogsView,
    //},
    //{
    //  path: '/settings',
    //  name: 'settings',
    //  component: SettingsView,
    //},
  ],
})

export default router
