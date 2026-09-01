import { createRouter, createWebHistory } from 'vue-router'
import { queueDomain } from './appConfig'
import DetailView from './views/DetailView.vue'
import BrowseView from './views/BrowseView.vue'
import ComposeView from './views/ComposeView.vue'
import QueueView from './views/QueueView.vue'
import RelyingPartyView from './views/RelyingPartyView.vue'
import LineageView from './views/LineageView.vue'
import TasksView from './views/TasksView.vue'

// ADR-099 -- one route per what used to be an `activeTab` branch. Routes
// are keyed by path (not name) so App.vue's own n-menu can key its items
// identically without a second lookup table.
export const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/', redirect: '/detail' },
    { path: '/detail', component: DetailView },
    { path: '/browse', component: BrowseView },
    { path: '/compose', component: ComposeView },
    // ADR-101 -- cross-domain by design, no requiresDomain gate: the myTasks
    // query itself spans every domain sharing this Host's own database.
    { path: '/tasks', component: TasksView },
    { path: '/queue', component: QueueView, meta: { requiresDomain: true } },
    { path: '/relying-party', component: RelyingPartyView, meta: { requiresDomain: 'meridian' } },
    { path: '/lineage', component: LineageView },
  ],
})

// The navigation-guard equivalent of the old template `v-if="queueDomain"`/
// `v-if="queueDomain === 'meridian'"` gates -- a domain-gated route is
// simply unreachable (redirected to /detail) rather than rendering nothing,
// so a direct/bookmarked URL to e.g. /relying-party against the "mvvm-demo"
// standalone config can't land on a blank page.
router.beforeEach((to) => {
  const requiresDomain = to.meta.requiresDomain
  if (requiresDomain === true && !queueDomain) return '/detail'
  if (typeof requiresDomain === 'string' && queueDomain !== requiresDomain) return '/detail'
  return true
})
